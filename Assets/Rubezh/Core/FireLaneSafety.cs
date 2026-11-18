using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Arbitration uses actual recent friendly fire, not knowledge of hidden enemies.
    // Movement keeps its original job/destination; no orders or medical reservations change.
    public static class FireLaneSafety
    {
        const float Width = .55f;
        public static bool Important(Soldier s) => s.SuppressiveFire || s.SupportReady && (s.Job == Job.Cover || s.Job == Job.Hold || s.Job == Job.Command);
        static bool Active(BattleSimulation b, Soldier s) => s.Available && !s.Broken && !s.Motion.Fast && s.Ammo > 0 && s.ReloadUntil <= b.Time && s.FireLaneUntil > b.Time && V2.Distance(s.Position, s.FireLaneTarget) > .8f;
        public static bool Crosses(V2 a, V2 c, V2 from, V2 to)
        {
            V2 d = (to - from).Normalized;
            float range = V2.Distance(from, to), pa = V2.Dot(a - from, d), pc = V2.Dot(c - from, d);
            if (Math.Max(pa, pc) < .25f || Math.Min(pa, pc) > range + .3f)
                return false;
            V2 n = new V2(-d.Y, d.X);
            float sa = V2.Dot(a - from, n), sc = V2.Dot(c - from, n);
            if (sa * sc <= 0 && Math.Abs(sa - sc) > .00001f)
            {
                float t = sa / (sa - sc), along = pa + (pc - pa) * t;
                if (along >= .25f && along <= range + .3f)
                    return true;
            }

            return V2.SegmentDistance(a, from, to) < Width || V2.SegmentDistance(c, from, to) < Width;
        }

        static V2 IntendedStep(BattleSimulation b, Soldier s, float horizon)
        {
            if (s.PathCursor >= s.Path.Count)
                return s.Position;
            V2 delta = s.Path[s.PathCursor] - s.Position;
            return s.Position + delta.Normalized * Math.Min(delta.Length, Math.Max(.5f, CombatMovement.Speed(b, s)) * horizon);
        }

        static void Pause(BattleSimulation b, Soldier shooter)
        {
            if (shooter.FireHoldUntil <= b.Time)
                b.Ballistics.CrossingPauses++;
            shooter.FireHoldUntil = Math.Max(shooter.FireHoldUntil, b.Time + .25f);
            shooter.SupportReady = false;
            shooter.FireStatus = "Пропускает союзника";
            shooter.Reason = "Короткая пауза огня для перебежки союзника";
        }

        public static bool HoldFire(BattleSimulation b, Soldier shooter, V2 target)
        {
            if (shooter.FireHoldUntil > b.Time)
                return true;
            if (Important(shooter))
                return false;
            foreach (var mover in b.Soldiers)
            {
                if (mover.Id == shooter.Id || mover.Side != shooter.Side || !mover.Alive || !mover.Deployed || mover.PathCursor >= mover.Path.Count || !mover.Moving)
                    continue;
                if (mover.FireLaneWaitSince >= 0 && mover.FireLaneCrossUntil <= b.Time)
                    continue;
                if (ShoulderPair.CanShootOver(b, shooter, mover, target))
                    continue;
                if (Crosses(mover.Position, IntendedStep(b, mover, .3f), shooter.Position, target))
                {
                    mover.FireLaneCrossUntil = Math.Max(mover.FireLaneCrossUntil, b.Time + 1.5f);
                    Pause(b, shooter);
                    return true;
                }
            }

            return false;
        }

        public static void Fired(BattleSimulation b, Soldier shooter, V2 target)
        {
            shooter.FireLaneTarget = target;
            shooter.FireLaneUntil = b.Time + Util.Clamp(WeaponBalance.For(shooter).FireSeconds + .3f, .8f, 1.6f);
        }

        internal static bool CostSource(BattleSimulation b, Soldier mover, Soldier s) => s.Id != mover.Id && s.Side == mover.Side && Active(b, s) && Important(s);
        public static float PathCost(BattleSimulation b, Soldier mover, V2 point)
        {
            float cost = 0;
            if (b.RouteView.Owner == mover)
            {
                foreach (var s in b.RouteView.Lanes(b))
                    if (Crosses(point, point, s.Position, s.FireLaneTarget))
                        cost += 40;
                return cost;
            }

            foreach (var s in b.Soldiers)
                if (CostSource(b, mover, s) && Crosses(point, point, s.Position, s.FireLaneTarget))
                    cost += 40;
            return cost;
        }

        static bool SafePath(BattleSimulation b, Soldier shooter, V2 from, List<V2> path)
        {
            foreach (var p in path)
            {
                if (Crosses(from, p, shooter.Position, shooter.FireLaneTarget) || !Locomotion.WalkableSegment(b, from, p))
                    return false;
                from = p;
            }

            return true;
        }

        static bool Detour(BattleSimulation b, Soldier mover, Soldier shooter)
        {
            if (b.Time < mover.FireLaneReplanAt || mover.PathCursor >= mover.Path.Count || V2.Distance(mover.Position, shooter.Position) > 5)
                return false;
            mover.FireLaneReplanAt = b.Time + 2;
            V2 behind = shooter.Position - (shooter.FireLaneTarget - shooter.Position).Normalized * 1.15f;
            if (!b.Map.Walkable(behind) || !b.Map.Walkable(mover.Destination))
                return false;
            Func<V2, float> cost = p => Crosses(p, p, shooter.Position, shooter.FireLaneTarget) ? 80 : 0;
            var first = Pathfinder.Find(b.Map, mover.Position, behind, cost);
            if (first.Count == 0 || !SafePath(b, shooter, mover.Position, first))
                return false;
            var second = Pathfinder.Find(b.Map, behind, mover.Destination, cost);
            if (second.Count == 0 || !SafePath(b, shooter, behind, second))
                return false;
            float old = 0;
            V2 from = mover.Position;
            for (int i = mover.PathCursor; i < mover.Path.Count; i++)
            {
                old += V2.Distance(from, mover.Path[i]);
                from = mover.Path[i];
            }

            float length = 0;
            from = mover.Position;
            foreach (var p in first)
            {
                length += V2.Distance(from, p);
                from = p;
            }

            foreach (var p in second)
            {
                length += V2.Distance(from, p);
                from = p;
            }

            if (length > old + 8)
                return false;
            first.AddRange(second);
            mover.Path = first;
            mover.PathCursor = 0;
            mover.LastMoved = b.Time;
            mover.FireLaneWaitSince = -1;
            mover.Reason = "Обхожу огневой сектор за спиной своего стрелка";
            b.Ballistics.CrossingDetours++;
            return true;
        }

        public static bool AllowStep(BattleSimulation b, Soldier mover, V2 desired)
        {
            V2 next = mover.Position + desired;
            // A cease-fire cannot recall rounds already in flight. Wait for their actual sweep.
            if (!mover.GrenadeEscape.Active && Ballistics.CrossingDanger(b, mover, next))
            {
                mover.Reason = "Пропускаю уже выпущенную пулю союзника";
                b.Ballistics.CrossingWaits++;
                return false;
            }

            V2 ahead = mover.Position + desired.Normalized * Math.Max(desired.Length, Math.Min(1.1f, CombatMovement.Speed(b, mover) * .35f));
            bool yielding = false;
            foreach (var shooter in b.Soldiers)
            {
                if (shooter.Id == mover.Id || shooter.Side != mover.Side || !Active(b, shooter) || !Crosses(mover.Position, ahead, shooter.Position, shooter.FireLaneTarget))
                    continue;
                if (ShoulderPair.CanShootOver(b, shooter, mover, shooter.FireLaneTarget))
                    continue;
                bool alreadyInLane = Crosses(mover.Position, mover.Position, shooter.Position, shooter.FireLaneTarget);
                bool urgent = mover.GrenadeEscape.Active || mover.Busy || mover.Incapacitated || mover.Broken || mover.Health < 25 || mover.Suppression > .85f;
                if (!Important(shooter) || urgent || alreadyInLane || mover.FireLaneCrossUntil > b.Time)
                {
                    mover.FireLaneCrossUntil = Math.Max(mover.FireLaneCrossUntil, b.Time + 1.5f);
                    Pause(b, shooter);
                    continue;
                }

                yielding = true;
                if (Detour(b, mover, shooter))
                    return false;
                if (mover.FireLaneWaitSince < 0 || mover.FireLaneWaitShooter != shooter.Id)
                {
                    mover.FireLaneWaitSince = b.Time;
                    mover.FireLaneWaitShooter = shooter.Id;
                }

                // Bounded wait: an unavailable rear route must never lock a whole squad.
                if (b.Time - mover.FireLaneWaitSince >= 1.2f)
                {
                    mover.FireLaneCrossUntil = b.Time + 1.5f;
                    Pause(b, shooter);
                    continue;
                }

                mover.Reason = "Не вхожу в важный сектор прикрытия — ищу окно для перебежки";
                b.Ballistics.CrossingWaits++;
                return false;
            }

            if (!yielding)
                mover.FireLaneWaitSince = -1;
            return true;
        }
    }
}
