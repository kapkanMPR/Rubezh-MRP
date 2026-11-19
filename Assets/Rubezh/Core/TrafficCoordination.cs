using System;

namespace SilentOrders.Core
{
    // Local courtesy movement; never teleports or cancels a medical/weapon task.
    public static class TrafficCoordination
    {
        public static void Blocked(BattleSimulation b, Soldier s, V2 direction)
        {
            if (
                !s.Available
                || s.Busy
                || s.Suppression > .7f
                || b.Time < s.TrafficRetryAt
                || s.TrafficYieldUntil > b.Time
            )
                return;
            s.TrafficRetryAt = b.Time + 1.2f;
            Soldier blocker = null;
            float best = 1.4f;
            foreach (var other in b.Soldiers)
            {
                if (other == s || other.Side != s.Side || !other.Alive || !other.Deployed)
                    continue;
                V2 d = other.Position - s.Position;
                float distance = d.Length;
                if (distance < best && V2.Dot(d, direction) > .05f)
                {
                    best = distance;
                    blocker = other;
                }
            }

            if (blocker == null)
                return;
            bool canYield =
                !TacticalOrders.ServiceActive(blocker)
                && !EngagementPolicy.Active(b, blocker)
                && !CounterSniper.Reserved(b, blocker)
                && !LastOpponentHunt.Owns(b, blocker)
                && !TacticalOrders.PriorityMover(b, blocker)
                && blocker.Available
                && !blocker.Busy
                && !blocker.Broken
                && blocker.SupplyReceiverId < 0
                && blocker.RescuerId < 0
                && blocker.Suppression < .55f
                && b.Time >= blocker.ConcussionReactUntil
                && blocker.HandsBusyUntil <= b.Time
                && blocker.ReloadUntil <= b.Time
                && b.Time - blocker.LastFire > 1f
                && blocker.TrafficYieldUntil <= b.Time;
            // Two walkers agree on one priority rather than pushing each other aside.
            if (blocker.PathCursor < blocker.Path.Count && blocker.Id < s.Id)
                canYield = false;
            if (canYield)
            {
                var normal = new V2(-direction.Y, direction.X);
                for (int k = 0; k < 6; k++)
                {
                    var target =
                        blocker.Position
                        + (
                            k < 4
                                ? normal * (k % 2 == 0 ? 1.1f : -1.1f) * (k < 2 ? 1f : 1.7f)
                                : direction * (k == 4 ? 1.1f : 2.2f)
                        );
                    if (
                        !CommandPosture.Inside(b, blocker, target)
                        || !b.Map.Walkable(target)
                        || !Locomotion.WalkableSegment(b, blocker.Position, target)
                        || PositionLogic.Occupied(b, blocker, target)
                    )
                        continue;
                    if (
                        PositionLogic.Exposure(b, blocker, target)
                        > PositionLogic.Exposure(b, blocker, blocker.Position) + .1f
                    )
                        continue;
                    blocker.TrafficYieldTarget = target;
                    blocker.TrafficYieldUntil = b.Time + 2.5f;
                    blocker.Reason = "Освобождаю проход товарищу";
                    return;
                }
            }

            // No safe sidestep: route the moving unit around occupied cells, retain its goal.
            if (s.PathCursor < s.Path.Count)
            {
                var route = LossMemory.Find(
                    b,
                    s,
                    s.Destination,
                    p => PositionLogic.Exposure(b, s, p) * 2f + (PositionLogic.Occupied(b, s, p) ? 20f : 0f)
                );
                b.Stats.Paths++;
                if (
                    route.Count > 0
                    && SupportAI.PathLength(s.Position, route) <= SupportAI.PathLength(s.Position, s.Path) + 8f
                )
                {
                    s.Path = route;
                    s.PathCursor = 0;
                }
            }
        }

        public static bool Update(BattleSimulation b, Soldier s, float dt)
        {
            if (s.TrafficYieldUntil <= b.Time)
                return false;
            if (
                !s.Available
                || s.Busy
                || s.Broken
                || s.SupplyReceiverId >= 0
                || s.Suppression > .7f
                || s.HandsBusyUntil > b.Time
                || s.ReloadUntil > b.Time
                || b.Time < s.ConcussionReactUntil
            )
            {
                s.TrafficYieldUntil = 0;
                return false;
            }

            if (!CommandPosture.Inside(b, s, s.TrafficYieldTarget))
            {
                s.TrafficYieldUntil = 0;
                return false;
            }

            V2 delta = s.TrafficYieldTarget - s.Position;
            if (delta.Length < .1f)
            {
                s.TrafficYieldUntil = 0;
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
                s.Facing = delta.Normalized;
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }
    }
}
