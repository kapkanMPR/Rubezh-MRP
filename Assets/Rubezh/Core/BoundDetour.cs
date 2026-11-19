using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // A retreat is useful only as part of a verified route to the existing group goal.
    // Keep that route until it is consumed; choosing a new forward shortcut after each
    // backward bound would otherwise send the soldier straight back into the dead end.
    public sealed class BoundDetourState
    {
        internal List<V2> Route;
        internal int Cursor,
            PendingCursor,
            MapRevision,
            OrderRevision;
        internal V2 Target,
            Anchor;
        public bool Active => Route != null;

        public void Clear()
        {
            Route = null;
            Cursor = PendingCursor = 0;
        }
    }

    public static class BoundDetour
    {
        static bool AllowedPost(BattleSimulation b, Soldier s, V2 point)
        {
            return StraightAdvance.Inside(b, s, point)
                && (s.Side != Side.Red || ProgressRecovery.WithinAssignment(b, s, point))
                && (PositionLogic.Protected(b, s, point) || PositionLogic.Exposure(b, s, point) <= .12f);
        }

        static bool SafeEdge(BattleSimulation b, Soldier s, V2 from, V2 to)
        {
            if (!b.Map.Walkable(to) || !Locomotion.WalkableSegment(b, from, to))
                return false;
            int samples = Math.Max(1, (int)Math.Ceiling(V2.Distance(from, to) * 2));
            for (int i = 1; i <= samples; i++)
                if (!AllowedPost(b, s, from + (to - from) * (i / (float)samples)))
                    return false;
            return true;
        }

        public static bool TryRoute(
            BattleSimulation b,
            Soldier s,
            V2 target,
            bool start,
            out V2 goal,
            out List<V2> route
        )
        {
            goal = s.Position;
            route = null;
            var state = s.Tactical.Detour;
            var q = b.Team(s);
            if (!PositionLogic.ShelterEligible(b, s) || q.Style == CombatStyle.Passive)
            {
                state.Clear();
                return false;
            }

            if (
                state.Active
                && (
                    state.MapRevision != b.Map.Revision
                    || state.OrderRevision != q.Revision
                    || V2.Distance(state.Target, target) > .5f
                )
            )
                state.Clear();
            if (!state.Active)
            {
                if (
                    !start
                    || !PositionLogic.ShelterEligible(b, s)
                    || q.Style == CombatStyle.Passive
                    || !AllowedPost(b, s, target)
                )
                    return false;
                var complete = LossMemory.Find(
                    b,
                    s,
                    target,
                    x => PositionLogic.Exposure(b, s, x) * 3 + FireLaneSafety.PathCost(b, s, x),
                    (from, to) => SafeEdge(b, s, from, to)
                );
                b.Stats.Paths++;
                if (complete.Count == 0 || V2.Distance(complete[complete.Count - 1], target) > 1)
                    return false;
                state.Route = complete;
                state.Cursor = state.PendingCursor = 0;
                state.MapRevision = b.Map.Revision;
                state.OrderRevision = q.Revision;
                state.Target = target;
                state.Anchor = s.Position;
            }

            // Only physical arrival consumes the route. A refused assignment cannot
            // advance the cursor or turn a subsequent attempt into a new retreat.
            if (
                state.PendingCursor > state.Cursor
                && V2.Distance(s.Position, state.Route[state.PendingCursor - 1]) < .8f
            )
            {
                state.Cursor = state.PendingCursor;
                state.Anchor = state.Route[state.Cursor - 1];
            }

            if (state.Cursor >= state.Route.Count || V2.Distance(s.Position, state.Anchor) > 1.25f)
            {
                state.Clear();
                return false;
            }

            var prefix = new List<V2>();
            V2 previous = s.Position;
            float length = 0;
            float maximum = Math.Min(5, MotionCombat.Maximum(q));
            for (int i = state.Cursor; i < state.Route.Count; i++)
            {
                var point = state.Route[i];
                length += V2.Distance(previous, point);
                if (length > maximum)
                    break;
                // Revalidate current knowledge and casualty/fire lanes before every
                // bound. Cached geometry is never an exemption from safety checks.
                if (
                    !SafeEdge(b, s, previous, point)
                    || !LossMemory.StepAllowed(b, s, previous, point - previous, false)
                    || !IncomingFire.StepAllowed(b, s, previous, point - previous)
                )
                {
                    state.Clear();
                    return false;
                }

                prefix.Add(point);
                previous = point;
            }

            while (prefix.Count > 0)
            {
                var end = prefix[prefix.Count - 1];
                if (
                    V2.Distance(s.Position, end) >= .8f
                    && !PositionLogic.Occupied(b, s, end)
                    && !(s.Assignment.FailedUntil > b.Time && V2.Distance(end, s.Assignment.FailedGoal) < 1.3f)
                    && PositionLogic.UsefulRoute(b, s, prefix)
                    && MedicTactics.AllowRoute(b, s, prefix)
                )
                {
                    state.PendingCursor = state.Cursor + prefix.Count;
                    goal = end;
                    route = prefix;
                    return true;
                }

                prefix.RemoveAt(prefix.Count - 1);
            }

            state.Clear();
            return false;
        }
    }
}
