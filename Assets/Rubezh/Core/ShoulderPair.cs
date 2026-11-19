using System;

namespace SilentOrders.Core
{
    // A two-body game stance. Physical projectile/body collisions are never disabled.
    public static class ShoulderPair
    {
        // Archived showcases retain their exact historical rules.
        public static bool TerrainRequired(BattleSimulation b, Soldier s) => !b.Historical01225 && s.Side == Side.Green;

        public static bool ShelteredFront(BattleSimulation b, Soldier s) =>
            s != null && (!TerrainRequired(b, s) || PositionLogic.ProtectedRaw(b, s, s.Position));

        // The exception belongs to the rear fighter; each threshold is strictly below half.
        public static bool BelowHalf(Soldier rear) =>
            rear != null && (rear.Health < 50f || rear.Morale < .5f || rear.Suppression < .5f);

        public static bool AllowsFront(BattleSimulation b, Soldier rear, Soldier front) =>
            rear != null
            && front != null
            && (!TerrainRequired(b, rear) || BelowHalf(rear) || PositionLogic.ProtectedRaw(b, front, front.Position));

        public static bool KeepClose(BattleSimulation b, Soldier s)
        {
            if (Linked(b, s))
                return true;
            if (s.Motion.ShoulderApproachUntil > b.Time && AllowsFront(b, s, Unit(b, s.Motion.ApproachFront)))
                return true;
            foreach (var rear in b.Soldiers)
                if (
                    rear.Side == s.Side
                    && rear.Motion.ApproachFront == s.Id
                    && rear.Motion.ShoulderApproachUntil > b.Time
                    && AllowsFront(b, rear, s)
                )
                    return true;
            return false;
        }

        public static void Release(BattleSimulation b, Soldier s)
        {
            var front = Unit(b, s.Motion.FrontId);
            var rear = Unit(b, s.Motion.RearId);
            if (front != null && front.Motion.RearId == s.Id)
            {
                front.Motion.RearId = -1;
                front.Motion.PairUntil = 0;
            }

            if (rear != null && rear.Motion.FrontId == s.Id)
            {
                rear.Motion.FrontId = -1;
                rear.Motion.PairUntil = 0;
            }

            s.Motion.FrontId = s.Motion.RearId = -1;
            s.Motion.PairUntil = 0;
            s.Motion.ShoulderApproachUntil = 0;
            s.Motion.ApproachFront = -1;
        }

        static Soldier Unit(BattleSimulation b, int id) => id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;

        static bool Free(BattleSimulation b, Soldier s) =>
            s != null
            && CombatTraining.Skill(s, 1, "pair")
            && s.Available
            && s.Deployed
            && !s.Busy
            && !s.Broken
            && !s.GrenadeEscape.Active
            && !s.Motion.Fast
            && s.ReloadUntil <= b.Time
            && !TacticalOrders.ServiceActive(s);

        public static bool Linked(BattleSimulation b, Soldier s)
        {
            if (s.Motion.PairUntil <= b.Time)
                return false;
            if (s.Motion.FrontId >= 0)
                return AllowsFront(b, s, Unit(b, s.Motion.FrontId));
            if (s.Motion.RearId >= 0)
                return AllowsFront(b, Unit(b, s.Motion.RearId), s);
            return false;
        }

        public static bool Front(BattleSimulation b, Soldier s) => Linked(b, s) && s.Motion.RearId >= 0;

        public static void Update(BattleSimulation b, Soldier rear)
        {
            if (TerrainRequired(b, rear))
            {
                var lead = rear.Motion.FrontId >= 0 ? Unit(b, rear.Motion.FrontId) : rear;
                var follower = rear.Motion.FrontId >= 0 ? rear : Unit(b, rear.Motion.RearId);
                if ((rear.Motion.FrontId >= 0 || rear.Motion.RearId >= 0) && !AllowsFront(b, follower, lead))
                {
                    if (PairExchange.Active(b, rear))
                        PairExchange.Cancel(b, rear);
                    Release(b, rear);
                }
            }

            if (PairExchange.Active(b, rear))
                return;
            if (b.Time < rear.Motion.NextPair)
                return;
            rear.Motion.NextPair = b.Time + .4f;
            if (rear.Motion.PairUntil <= b.Time)
            {
                rear.Motion.FrontId = rear.Motion.RearId = -1;
            }

            var front = Unit(b, rear.Motion.FrontId);
            if (
                front != null
                && (!Free(b, rear) || !Free(b, front) || V2.Distance(rear.Position, front.Position) > 1.35f)
            )
            {
                rear.Motion.FrontId = -1;
                front.Motion.RearId = -1;
                rear.Motion.PairUntil = front.Motion.PairUntil = 0;
            }

            if (!Free(b, rear) || rear.GunSniper || rear.Motion.RearId >= 0)
                return;
            foreach (var first in b.Soldiers)
            {
                if (
                    first == rear
                    || first.Side != rear.Side
                    || first.DefenderGroup != rear.DefenderGroup
                    || !Free(b, first)
                    || first.Motion.FrontId >= 0
                    || first.Motion.RearId >= 0 && first.Motion.RearId != rear.Id
                )
                    continue;
                if (!AllowsFront(b, rear, first))
                    continue;
                if (first.Id != rear.BuddyId && first.Id != b.Team(rear).LeaderId && rear.Id != b.Team(rear).LeaderId)
                    continue;
                V2 delta = first.Position - rear.Position;
                float distance = delta.Length;
                if (
                    distance > 1.15f
                    && distance <= 2
                    && !MotionCombat.HasRoute(rear)
                    && !MotionCombat.HasRoute(first)
                    && rear.Motion.ShoulderApproachUntil <= b.Time
                )
                {
                    var contact = TacticalOrders.ClosestKnown(b, rear);
                    if (
                        contact != null
                        && V2.SegmentDistance(first.Position, rear.Position, contact.Position) < .45f
                        && V2.Dot(first.Position - rear.Position, contact.Position - rear.Position) > 0
                    )
                    {
                        V2 goal = first.Position - (contact.Position - first.Position).Normalized * .7f;
                        if (
                            V2.Distance(rear.Position, goal) <= 1.4f
                            && b.Map.Walkable(goal)
                            && Locomotion.WalkableSegment(b, rear.Position, goal)
                            && PositionLogic.Exposure(b, rear, goal)
                                <= PositionLogic.Exposure(b, rear, rear.Position) + .05f
                        )
                        {
                            rear.Motion.ApproachFront = first.Id;
                            rear.Motion.ShoulderGoal = goal;
                            rear.Motion.ShoulderApproachUntil = b.Time + 2;
                            rear.Motion.ShoulderApproaches++;
                            rear.Reason = "Короткое смещение за товарища для совместного сектора";
                            return;
                        }
                    }
                }

                if (
                    distance < .55f
                    || distance > 1.15f
                    || V2.Dot(delta.Normalized, first.Facing.Normalized) < .85f
                    || V2.Dot(first.Facing.Normalized, rear.Facing.Normalized) < .8f
                )
                    continue;
                bool moving = MotionCombat.HasRoute(first) || MotionCombat.HasRoute(rear);
                if (moving && (!WallTravel.Active(b, first) || !WallTravel.Active(b, rear)))
                    continue;
                if (!moving && TacticalOrders.ClosestKnown(b, rear) == null)
                    continue;
                rear.Motion.FrontId = first.Id;
                first.Motion.RearId = rear.Id;
                rear.Motion.PairUntil = first.Motion.PairUntil = b.Time + 1;
                CombatCrouch.ForPair(b, first);
                return;
            }
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            var m = s.Motion;
            if (m.ShoulderApproachUntil <= b.Time)
                return false;
            var front = Unit(b, m.ApproachFront);
            if (
                !Free(b, s)
                || !Free(b, front)
                || !AllowsFront(b, s, front)
                || MotionCombat.HasRoute(s)
                || MotionCombat.HasRoute(front)
                || V2.Distance(m.ShoulderGoal, front.Position) > 1.05f
            )
            {
                m.ShoulderApproachUntil = 0;
                return false;
            }

            V2 delta = m.ShoulderGoal - s.Position;
            if (delta.Length < .08f)
            {
                m.ShoulderApproachUntil = 0;
                m.NextPair = 0;
                return false;
            }

            V2 next;
            if (
                Locomotion.TryStep(
                    b,
                    s,
                    delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt),
                    out next
                )
            )
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.LastMoved = b.Time;
                s.Moving = true;
                s.State = ActionState.Move;
            }

            return true;
        }

        public static bool CanShootOver(BattleSimulation b, Soldier shooter, Soldier first, V2 target)
        {
            if (
                PairExchange.Active(b, shooter)
                || PairExchange.Active(b, first)
                || CombatCrouch.Standing(b, first)
                || shooter.Motion.FrontId != first.Id
                || !Linked(b, shooter)
                || !Free(b, first)
                || !first.Crouched
            )
                return false;
            var dir = (target - shooter.Position).Normalized;
            float along = V2.Dot(first.Position - shooter.Position, dir);
            if (along < .45f || along > 1.2f || V2.SegmentDistance(first.Position, shooter.Position, target) > .3f)
                return false;
            // Worst descending miss to a neighbouring cell must still clear the first body.
            float range = V2.Distance(shooter.Position, target) - 1.5f - .32f;
            if (range <= 1)
                return false;
            float height = Ballistics.MuzzleHeight * (1 - Math.Max(0, along - .32f) / range);
            return height > Ballistics.BodyHeight(first) + .08f;
        }

        public static float Protection(BattleSimulation b, Soldier target, V2 shooter)
        {
            var first = Unit(b, target.Motion.FrontId);
            if (first == null || !Linked(b, target) || !Free(b, first))
                return 0;
            var toward = (shooter - target.Position).Normalized;
            var offset = first.Position - target.Position;
            return offset.Length <= 1.2f && V2.Dot(offset.Normalized, toward) > .85f ? .68f * .6f : 0;
        }

        public static V2 LimitStep(BattleSimulation b, Soldier s, V2 step)
        {
            if (!Linked(b, s))
                return step;
            var first = Unit(b, s.Motion.FrontId);
            if (first != null && V2.Distance(s.Position + step, first.Position) < .6f)
                return new V2();
            var rear = Unit(b, s.Motion.RearId);
            if (
                rear != null
                && MotionCombat.HasRoute(rear)
                && V2.Distance(s.Position + step, rear.Position) > 1.15f
                && b.Time - rear.LastMoved < .8f
            )
                return new V2();
            return step;
        }
    }
}
