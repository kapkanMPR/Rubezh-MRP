using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Locomotion
    {
        public static bool TryStep(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 desired, out SilentOrders.Core.V2 next, int ignore = -1)
        {
            next = s.Position;
            if (!CombatCrouch.AllowMove(b, s) || !GroupMovement.AllowStep(b, s))
                return false;
            desired = SmokeTactics.CautiousStep(b, s, ShoulderPair.LimitStep(b, s, desired));
            if (desired.Length < .00001f)
                return false;
            if (!IncomingFire.AllowStep(b, s, desired) || !LossMemory.AllowStep(b, s, desired) || !FireLaneSafety.AllowStep(b, s, desired))
                return false;
            float length = desired.Length;
            SilentOrders.Core.V2 normalized = desired.Normalized;
            SilentOrders.Core.V2 v = new SilentOrders.Core.V2(0f - normalized.Y, normalized.X);
            next = s.Position + desired;
            if (FormationEnvelope.AllowStep(b, s, next) && MedicTactics.AllowStep(b, s, next) && Clear(b, s, next, ignore))
            {
                return true;
            }

            TrafficCoordination.Blocked(b, s, normalized);
            // Preserve the candidate order without allocating an array for every blocked step.
            float num = -10000f;
            bool result = false;
            for (int i = 0; i < 4; i++)
            {
                V2 v2 = i == 0 ? normalized + v : i == 1 ? normalized - v : i == 2 ? v : v * -1f;
                SilentOrders.Core.V2 v3 = s.Position + v2.Normalized * length;
                if (FormationEnvelope.AllowStep(b, s, v3) && MedicTactics.AllowStep(b, s, v3) && Clear(b, s, v3, ignore) && ShoulderPair.LimitStep(b, s, v3 - s.Position).Length > .00001f && IncomingFire.StepAllowed(b, s, s.Position, v3 - s.Position) && LossMemory.StepAllowed(b, s, s.Position, v3 - s.Position) && FireLaneSafety.AllowStep(b, s, v3 - s.Position))
                {
                    float num2 = SilentOrders.Core.V2.Dot(v2.Normalized, normalized);
                    if (num2 > num && num2 > .1f)
                    {
                        num = num2;
                        next = v3;
                        result = true;
                    }
                }
            }

            return result;
        }

        public static void AdvanceWaypoint(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            while (s.PathCursor + 1 < s.Path.Count && SilentOrders.Core.V2.Distance(s.Position, s.Path[s.PathCursor]) < 0.65f && WalkableSegment(b, s.Position, s.Path[s.PathCursor + 1]))
            {
                s.PathCursor++;
            }
        }

        public static bool WalkableSegment(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.V2 from, SilentOrders.Core.V2 to)
        {
            int num = System.Math.Max(1, (int)System.Math.Ceiling(SilentOrders.Core.V2.Distance(from, to) * 12f));
            SilentOrders.Core.V2 v = from;
            for (int i = 1; i <= num; i++)
            {
                SilentOrders.Core.V2 v2 = from + (to - from) * ((float)i / (float)num);
                if (!b.Map.Walkable(v2))
                {
                    return false;
                }

                int num2 = (int)v.X;
                int num3 = (int)v.Y;
                int num4 = (int)v2.X;
                int num5 = (int)v2.Y;
                if (num2 != num4 && num3 != num5 && (!b.Map.Walkable(num2, num5) || !b.Map.Walkable(num4, num3)))
                {
                    return false;
                }

                v = v2;
            }

            return true;
        }

        private static bool Clear(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 p, int ignore)
        {
            if (!b.Map.Walkable(p) || PairExchange.Reserved(b, s, p))
            {
                return false;
            }

            int num = (int)s.Position.X;
            int num2 = (int)s.Position.Y;
            int num3 = (int)p.X;
            int num4 = (int)p.Y;
            if (num != num3 && num2 != num4 && (!b.Map.Walkable(num, num4) || !b.Map.Walkable(num3, num2)))
            {
                return false;
            }

            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Alive && soldier.Deployed && soldier.Id != s.Id && soldier.Id != ignore)
                {
                    float num5 = SilentOrders.Core.V2.Distance(s.Position, soldier.Position);
                    float num6 = SilentOrders.Core.V2.Distance(p, soldier.Position);
                    float num7 = ((s.Busy || soldier.Busy || s.RescuerId >= 0 || soldier.RescuerId >= 0) ? 0.4f : 0.65f);
                    if (num6 < num7 && num6 < num5)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
