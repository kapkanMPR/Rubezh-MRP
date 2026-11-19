using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Locomotion
    {
        public static bool TryStep(BattleSimulation b, Soldier s, V2 desired, out V2 next, int ignore = -1)
        {
            next = s.Position;
            if (!CombatCrouch.AllowMove(b, s) || !GroupMovement.AllowStep(b, s))
                return false;
            desired = SmokeTactics.CautiousStep(b, s, ShoulderPair.LimitStep(b, s, desired));
            if (desired.Length < .00001f)
                return false;
            if (
                !IncomingFire.AllowStep(b, s, desired)
                || !LossMemory.AllowStep(b, s, desired)
                || !FireLaneSafety.AllowStep(b, s, desired)
            )
                return false;
            float length = desired.Length;
            V2 normalized = desired.Normalized;
            V2 v = new V2(0f - normalized.Y, normalized.X);
            next = s.Position + desired;
            if (MedicTactics.AllowStep(b, s, next) && Clear(b, s, next, ignore))
            {
                return true;
            }

            TrafficCoordination.Blocked(b, s, normalized);
            // Preserve the candidate order without allocating an array for every blocked step.
            float bestAlignment = -10000f;
            bool result = false;
            for (int i = 0; i < 4; i++)
            {
                V2 candidateDirection =
                    i == 0 ? normalized + v
                    : i == 1 ? normalized - v
                    : i == 2 ? v
                    : v * -1f;
                V2 v3 = s.Position + candidateDirection.Normalized * length;
                if (
                    MedicTactics.AllowStep(b, s, v3)
                    && Clear(b, s, v3, ignore)
                    && ShoulderPair.LimitStep(b, s, v3 - s.Position).Length > .00001f
                    && IncomingFire.StepAllowed(b, s, s.Position, v3 - s.Position)
                    && LossMemory.StepAllowed(b, s, s.Position, v3 - s.Position)
                    && FireLaneSafety.AllowStep(b, s, v3 - s.Position)
                )
                {
                    float alignment = V2.Dot(candidateDirection.Normalized, normalized);
                    if (alignment > bestAlignment && alignment > .1f)
                    {
                        bestAlignment = alignment;
                        next = v3;
                        result = true;
                    }
                }
            }

            return result;
        }

        public static void AdvanceWaypoint(BattleSimulation b, Soldier s)
        {
            while (
                s.PathCursor + 1 < s.Path.Count
                && V2.Distance(s.Position, s.Path[s.PathCursor]) < 0.65f
                && WalkableSegment(b, s.Position, s.Path[s.PathCursor + 1])
            )
            {
                s.PathCursor++;
            }
        }

        public static bool WalkableSegment(BattleSimulation b, V2 from, V2 to)
        {
            int sampleCount = Math.Max(1, (int)Math.Ceiling(V2.Distance(from, to) * 12f));
            V2 v = from;
            for (int i = 1; i <= sampleCount; i++)
            {
                V2 samplePoint = from + (to - from) * ((float)i / (float)sampleCount);
                if (!b.Map.Walkable(samplePoint))
                {
                    return false;
                }

                int previousX = (int)v.X;
                int previousY = (int)v.Y;
                int sampleX = (int)samplePoint.X;
                int sampleY = (int)samplePoint.Y;
                if (
                    previousX != sampleX
                    && previousY != sampleY
                    && (!b.Map.Walkable(previousX, sampleY) || !b.Map.Walkable(sampleX, previousY))
                )
                {
                    return false;
                }

                v = samplePoint;
            }

            return true;
        }

        private static bool Clear(BattleSimulation b, Soldier s, V2 p, int ignore)
        {
            if (!b.Map.Walkable(p) || PairExchange.Reserved(b, s, p))
            {
                return false;
            }

            int sourceX = (int)s.Position.X;
            int sourceY = (int)s.Position.Y;
            int proposedX = (int)p.X;
            int proposedY = (int)p.Y;
            if (
                sourceX != proposedX
                && sourceY != proposedY
                && (!b.Map.Walkable(sourceX, proposedY) || !b.Map.Walkable(proposedX, sourceY))
            )
            {
                return false;
            }

            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Alive && soldier.Deployed && soldier.Id != s.Id && soldier.Id != ignore)
                {
                    float initialSeparation = V2.Distance(s.Position, soldier.Position);
                    float proposedSeparation = V2.Distance(p, soldier.Position);
                    float minimumSeparation = (
                        (s.Busy || soldier.Busy || s.RescuerId >= 0 || soldier.RescuerId >= 0) ? 0.4f : 0.65f
                    );
                    if (proposedSeparation < minimumSeparation && proposedSeparation < initialSeparation)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
