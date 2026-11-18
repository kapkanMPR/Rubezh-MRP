using System;

namespace SilentOrders.Core
{
    public sealed class PairExchangeState
    {
        public int Partner = -1, OldFront = -1, Revision, Started, Completed, Cancelled;
        public bool Done;
        public float StartAt, EndAt, NextAt;
        public V2 From, To, Visual;
        public string Reason = "";
    }

    public static class PairExchange
    {
        static Soldier Unit(BattleSimulation b, int id) => id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;
        public static bool Active(BattleSimulation b, Soldier s) => s.Exchange.Partner >= 0;
        static bool Mobile(Soldier s) => s != null && s.Alive && s.Deployed && !s.Incapacitated && s.RescuerId < 0 && s.RescueTargetId < 0 && !s.Busy && !TacticalOrders.ServiceActive(s) && !s.GrenadeEscape.Active && !s.VehicleDisperse && !s.VehicleRally;
        public static string Need(BattleSimulation b, Soldier front, Soldier rear)
        {
            if ((front.Suppression >= .7f && rear.Suppression < front.Suppression - .15f) || (front.Broken && !rear.Broken))
                return "Первый номер подавлен";
            if (front.Morale < .3f && rear.Morale > front.Morale + .2f)
                return "Первому номеру нужно восстановить мораль";
            if (front.Bleeding && rear.Health > front.Health && !rear.Bleeding)
                return "Первому номеру нужна перевязка";
            if ((front.Ammo <= 0 || front.ReloadUntil > b.Time + .5f) && rear.Ammo > 0)
                return "Первый номер меняет магазин";
            var c = TacticalOrders.ClosestKnown(b, rear);
            if (c != null && b.Time - c.SeenAt < 4 && V2.Distance(rear.Position, c.Position) > 4.5f && V2.Distance(rear.Position, c.Position) <= SpecialWeapons.Range(b) && (rear.Rockets > 0 && rear.Role == Role.Grenadier && !(front.Role == Role.Grenadier && front.Rockets > 0) || rear.Underbarrel && GrenadePockets.Count(rear, 2) > 0 && !(front.Underbarrel && GrenadePockets.Count(front, 2) > 0)) && (b.Map.Protection(c.Position, rear.Position) > .5f || rear.RocketAimTarget >= 0 || !b.Map.LineOfSight(rear.Position, c.Position) && rear.Underbarrel))
                return "Второй номер выводит гранатомёт вперёд";
            return "";
        }

        public static bool Clear(BattleSimulation b, Soldier a, Soldier z, V2 point)
        {
            if (!b.Map.Walkable(point))
                return false;
            foreach (var u in b.Soldiers)
                if (u != a && u != z && u.Alive && u.Deployed && V2.Distance(u.Position, point) < .5f)
                    return false;
            return true;
        }

        public static bool TryStart(BattleSimulation b, Soldier front, Soldier rear)
        {
            if (front == null || rear == null || front == rear || front.Side != rear.Side || front.DefenderGroup != rear.DefenderGroup || !Mobile(front) || !Mobile(rear) || rear.Broken || rear.Suppression >= .7f || rear.ReloadUntil > b.Time || rear.HandsBusyUntil > b.Time || Active(b, front) || Active(b, rear) || b.Time < front.Exchange.NextAt || b.Time < rear.Exchange.NextAt || MotionCombat.HasRoute(front) || MotionCombat.HasRoute(rear))
                return false;
            if (rear.Motion.FrontId != front.Id || !ShoulderPair.Linked(b, rear))
                return false;
            float distance = V2.Distance(front.Position, rear.Position);
            if (distance < .55f || distance > 1.35f || !Locomotion.WalkableSegment(b, front.Position, rear.Position))
                return false;
            if (GroupMovement.Count(b, b.Team(front)) > 1)
                return false;
            string why = Need(b, front, rear);
            if (why.Length == 0)
                return false;
            for (int i = 0; i <= 6; i++)
                if (!Clear(b, front, rear, front.Position + (rear.Position - front.Position) * (i / 6f)))
                    return false;
            CombatCrouch.Rise(b, front);
            CombatCrouch.Rise(b, rear);
            float start = Math.Max(b.Time, Math.Max(front.Crouch.StandUntil, rear.Crouch.StandUntil));
            foreach (var s in new[]
            {
                front,
                rear
            }

            )
            {
                var partner = s == front ? rear : front;
                var e = s.Exchange;
                e.Partner = partner.Id;
                e.OldFront = front.Id;
                e.From = s.Position;
                e.To = partner.Position;
                e.StartAt = start;
                e.EndAt = start + .65f;
                e.Done = false;
                e.Revision = b.Team(s).Revision;
                e.Reason = why;
                e.Started++;
                s.Motion.Fast = false;
                s.Motion.ShoulderApproachUntil = 0;
                s.Reason = "Меняем номера: " + why;
            }

            return true;
        }

        public static void Review(BattleSimulation b, Soldier s)
        {
            if (Active(b, s))
                return;
            var front = Unit(b, s.Motion.FrontId);
            if (front != null)
                TryStart(b, front, s);
            var rear = Unit(b, s.Motion.RearId);
            if (rear != null)
                TryStart(b, s, rear);
        }

        public static void Cancel(BattleSimulation b, Soldier s)
        {
            var partner = Unit(b, s.Exchange.Partner);
            foreach (var u in new[]
            {
                s,
                partner
            }

            )
                if (u != null)
                {
                    u.Exchange.Partner = -1;
                    u.Exchange.Visual = new V2();
                    u.Exchange.NextAt = b.Time + 4;
                    u.Exchange.Cancelled++;
                    u.Motion.FrontId = u.Motion.RearId = -1;
                    u.Motion.PairUntil = 0;
                }
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            var e = s.Exchange;
            if (!Active(b, s))
                return false;
            var partner = Unit(b, e.Partner);
            if (!Mobile(s) || !Mobile(partner) || b.Team(s).Revision != e.Revision || MotionCombat.HasRoute(s) || MotionCombat.HasRoute(partner))
            {
                Cancel(b, s);
                return false;
            }

            if (b.Time < e.StartAt)
            {
                s.Moving = false;
                s.SupportReady = false;
                return true;
            }

            float t = Util.Clamp((b.Time - e.StartAt) / (e.EndAt - e.StartAt), 0, 1);
            var goal = e.From + (e.To - e.From) * t;
            if (!Clear(b, s, partner, goal) || !Locomotion.WalkableSegment(b, s.Position, goal))
            {
                Cancel(b, s);
                return false;
            }

            // Only these two bodies ignore each other. Terrain, third parties and bullets stay physical.
            if (!GroupMovement.AllowStep(b, s))
                return true;
            var delta = goal - s.Position;
            s.DistanceTravelled += delta.Length;
            s.Position = goal;
            s.Moving = delta.Length > .00001f;
            if (s.Moving)
                s.LastMoved = b.Time;
            var normal = new V2(-(e.To - e.From).Y, (e.To - e.From).X).Normalized;
            e.Visual = normal * (.3f * (float)Math.Sin(Math.PI * t));
            s.SupportReady = false;
            s.State = ActionState.Move;
            if (t >= 1)
                e.Done = true;
            if (e.Done && partner.Exchange.Done)
            {
                var old = Unit(b, e.OldFront);
                var now = old == s ? partner : s;
                foreach (var u in new[]
                {
                    s,
                    partner
                }

                )
                {
                    u.Exchange.Partner = -1;
                    u.Exchange.Visual = new V2();
                    u.Exchange.Completed++;
                    u.Exchange.NextAt = b.Time + 6;
                    u.Crouch.NextSit = b.Time + .5f;
                    u.Motion.PairUntil = b.Time + 1;
                    u.Motion.FrontId = u.Motion.RearId = -1;
                    u.Motion.NextPair = b.Time + .4f;
                }

                now.Motion.RearId = old.Id;
                old.Motion.FrontId = now.Id;
                now.Facing = old.Facing;
                CombatCrouch.ForPair(b, now);
            }

            return true;
        }

        public static void Sweep(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
                if (Active(b, s) && (!Mobile(s) || !Mobile(Unit(b, s.Exchange.Partner))))
                    Cancel(b, s);
        }

        public static bool Reserved(BattleSimulation b, Soldier mover, V2 point)
        {
            foreach (var u in b.Soldiers)
                if (Active(b, u) && u != mover && u.Exchange.Partner != mover.Id && V2.SegmentDistance(point, u.Exchange.From, u.Exchange.To) < .5f)
                    return true;
            return false;
        }
    }
}
