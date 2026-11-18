using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class GroupMovement
    {
        public const int Maximum = 3;
        public static bool Ordinary(BattleSimulation b, Soldier s) => s.Available && !s.Broken && !s.Busy && !s.GrenadeEscape.Active && !s.VehicleRally && !s.VehicleDisperse && (!TacticalOrders.ServiceActive(s) || BuddyActions.Active(s) || SupplyRecovery.Active(s)) && s.CounterSniper.Phase == CounterSniperPhase.None && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time && s.TrafficYieldUntil <= b.Time;
        public static void Sample(BattleSimulation b)
        {
            var counts = b.MovementCounts;
            counts.Clear();
            int green = 0, red = 0;
            foreach (var v in b.Soldiers)
            {
                if (v.Motion.StepAt != b.Time || v.Motion.ExemptStep || !(V2.Distance(v.Motion.Previous, v.Position) > .00001f))
                    continue;
                if (v.DefenderGroup == null)
                {
                    if (v.Side == Side.Green)
                        green++;
                    else
                        red++;
                }
                else
                {
                    var key = (v.Side, v.DefenderGroup);
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                }
            }

            foreach (var u in b.Soldiers)
                if (u.Available && u.Deployed)
                {
                    int n;
                    if (u.DefenderGroup == null)
                        n = u.Side == Side.Green ? green : red;
                    else
                        counts.TryGetValue((u.Side, u.DefenderGroup), out n);
                    u.Motion.GroupPeak = Math.Max(u.Motion.GroupPeak, n);
                    if (n >= 3)
                        u.Motion.TripleSeconds += b.Config.Tick;
                }
        }

        public static bool AllowStep(BattleSimulation b, Soldier s)
        {
            if (TacticalMarch.Quiet(b, b.Team(s)) || b.Time < s.Awareness.ContactSprintUntil)
                return true;
            if (!Ordinary(b, s))
            {
                s.Motion.ExemptStep = true;
                return true;
            }

            int n = 0;
            foreach (var other in b.Soldiers)
                if (other != s && other.Side == s.Side && other.DefenderGroup == s.DefenderGroup)
                {
                    if (other.Motion.StepAt == b.Time)
                    {
                        if (!other.Motion.ExemptStep && V2.Distance(other.Motion.Previous, other.Position) > .00001f)
                            n++;
                    }
                    else if (Ordinary(b, other) && MotionCombat.HasRoute(other) && other.Moving)
                        n++;
                }

            if (n < Maximum)
                return true;
            s.Motion.GroupWaitUntil = b.Time + .2f;
            s.Reason = "Три бойца уже перемещаются: сохраняю сектор до освобождения места";
            return false;
        }

        public static int Count(BattleSimulation b, Squad q)
        {
            int n = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && !s.Busy && (!TacticalOrders.ServiceActive(s) && MotionCombat.HasRoute(s) && s.MoveAuthorized || BuddyActions.Active(s) && s.BuddyAction.Approaching || SupplyRecovery.Active(s) && s.Mechanics.UnpackAt < 0))
                    n++;
            return n;
        }

        public static bool Covered(BattleSimulation b, Soldier mover, V2 goal, HashSet<int> excluded = null)
        {
            if (WallTravel.Safe(b, mover, goal))
                return true;
            bool danger = false;
            for (int i = 0; i <= 6; i++)
                if (PositionLogic.Exposure(b, mover, mover.Position + (goal - mover.Position) * (i / 6f)) > .12f)
                {
                    danger = true;
                    break;
                }

            if (!danger)
                return true;
            foreach (var s in b.Soldiers)
                if (s != mover && b.Team(mover).Contains(s) && (excluded == null || !excluded.Contains(s.Id)) && s.Available && !s.Busy && !s.Moving && !MotionCombat.HasRoute(s) && PositionLogic.UsefulSupport(b, s, mover, goal))
                    return true;
            return false;
        }

        // A limited bound was selected under the command planner's existing
        // wait/safety policy. Admit its actual path, not a different straight
        // line, while retaining the shared movement capacity and safety checks.
        public static bool CanStart(BattleSimulation b, Soldier s, V2 goal, List<V2> limitedRoute)
        {
            return CanStart(b, s, goal) || limitedRoute != null && limitedRoute.Count > 0 && V2.Distance(goal, limitedRoute[limitedRoute.Count - 1]) < .001f && Count(b, b.Team(s)) < Maximum && PositionLogic.ShelterEligible(b, s) && CommandHierarchy.LimitedBoundSafe(b, s, limitedRoute);
        }

        public static bool CanStart(BattleSimulation b, Soldier s, V2 goal) => TacticalMarch.Quiet(b, b.Team(s)) || Count(b, b.Team(s)) < Maximum && Covered(b, s, goal);
    }
}
