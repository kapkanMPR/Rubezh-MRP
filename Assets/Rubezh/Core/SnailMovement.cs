using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class SnailMovement
    {
        static bool Ready(BattleSimulation b, Soldier s)
        {
            return s != null
                && s.Available
                && !s.Busy
                && !s.Broken
                && !s.Moving
                && s.PathCursor >= s.Path.Count
                && s.Ammo >= 3
                && s.ReloadUntil <= b.Time
                && s.HandsBusyUntil <= b.Time
                && s.Suppression < .55f
                && !CommandPosture.Service(b, s);
        }

        public static bool CoverReady(BattleSimulation b, Soldier s, int id)
        {
            if (id < 0 || id >= b.Soldiers.Count)
                return false;
            var cover = b.Soldiers[id];
            return Ready(b, cover) && cover != s && b.Team(s).Contains(cover);
        }

        // This is a one-way lateral transfer, never a movement style along the flank.
        public static bool NeedsShift(BattleSimulation b, Squad q, PairCommandPlan pair, Soldier s)
        {
            if (
                pair == null
                || pair.Duty != PairDuty.Maneuver
                || pair.LateralComplete
                || pair.IntentKey != (int)q.Intent
                || q.Intent == ManeuverIntent.Center
                || (q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front)
                || q.Intent == ManeuverIntent.Breakthrough
                || q.Style == CombatStyle.Passive
                || q.Style == CombatStyle.Withdraw
                || b.Time >= pair.LateralDeadline
                || pair.ManeuverSide == 0
            )
                return false;
            return V2.Dot(s.Position - b.Map.Entry, TacticalOrders.Lateral(b)) * pair.ManeuverSide
                < pair.LateralTarget - .8f;
        }

        public static bool Sprint(BattleSimulation b, Soldier s)
        {
            var a = s.Assignment;
            var q = b.Team(s);
            if (!NeedsShift(b, q, CommandHierarchy.Pair(q, a.PairId), s))
                return false;
            return a.Active
                && a.IsBound
                && a.SnailBound
                && s.PathCursor < s.Path.Count
                && s.MoveAuthorized
                && !CommandPosture.Service(b, s)
                && s.Suppression < .55f
                && s.Morale > .4f
                && s.ReloadUntil <= b.Time
                && V2.Distance(s.Destination, a.Goal) < .3f
                && CoverReady(b, s, a.CoverId);
        }

        public static bool TryRoute(
            BattleSimulation b,
            Squad q,
            PairCommandPlan pair,
            Soldier mover,
            out V2 goal,
            out List<V2> route,
            out int coverId
        )
        {
            goal = mover.Position;
            route = null;
            coverId = -1;
            if (!NeedsShift(b, q, pair, mover) || mover.Suppression > .4f || mover.Health < 45 || mover.Morale < .45f)
                return false;
            int id = pair.A == mover.Id ? pair.B : pair.A;
            if (!CoverReady(b, mover, id))
                return false;
            var cover = b.Soldiers[id];
            if (V2.Distance(cover.Position, mover.Position) > 9)
                return false;
            var contact = TacticalOrders.ClosestKnown(b, cover);
            V2 facing =
                contact == null ? TacticalOrders.Axis(b, q.Side) : (contact.Position - cover.Position).Normalized;
            if (facing.Length < .1f)
                facing = TacticalOrders.Axis(b, q.Side);
            V2 lateral = TacticalOrders.Lateral(b);
            float sign = pair.ManeuverSide;
            if (pair.LateralTarget - V2.Dot(mover.Position - b.Map.Entry, lateral) * sign < 3)
                return false;
            var rear = TacticalOrders.Open(b, cover.Position - facing * 1.8f);
            // A wall or map edge cannot turn a rear crossing into a crossing of the gun sector.
            if (V2.Dot(rear - cover.Position, facing) > -1)
                return false;
            float length = Math.Min(7, pair.LateralTarget - V2.Dot(rear - b.Map.Entry, lateral) * sign);
            if (length <= 0)
                return false;
            var wanted = TacticalOrders.Open(b, rear + lateral * sign * length);
            if (
                PositionLogic.Occupied(b, mover, wanted)
                || V2.Dot(wanted - mover.Position, lateral) * sign < 3
                || V2.Dot(wanted - b.Map.Entry, lateral) * sign > pair.LateralTarget + .8f
            )
                return false;
            var first = LossMemory.Find(
                b,
                mover,
                rear,
                v => PositionLogic.Exposure(b, mover, v) * 4 + FireLaneSafety.PathCost(b, mover, v)
            );
            b.Stats.Paths++;
            if (first.Count == 0 && V2.Distance(mover.Position, rear) > .6f)
                return false;
            var second = Pathfinder.Find(
                b.Map,
                rear,
                wanted,
                v => PositionLogic.Exposure(b, mover, v) * 4 + FireLaneSafety.PathCost(b, mover, v)
            );
            b.Stats.Paths++;
            if (second.Count == 0)
                return false;
            var path = new List<V2>(first);
            path.AddRange(second);
            if (SupportAI.PathLength(mover.Position, path) > 16)
                return false;
            foreach (var v in path)
            {
                var delta = v - cover.Position;
                if (V2.Dot(delta, facing) > -.45f && Math.Abs(V2.Dot(delta, new V2(-facing.Y, facing.X))) < 2)
                    return false;
                if (PositionLogic.Exposure(b, mover, v) > .12f && !PositionLogic.UsefulSupport(b, cover, mover, v))
                    return false;
            }

            if (!PositionLogic.Protected(b, mover, wanted) && PositionLogic.Exposure(b, mover, wanted) > .12f)
                return false;
            route = path;
            goal = wanted;
            coverId = id;
            return true;
        }
    }
}
