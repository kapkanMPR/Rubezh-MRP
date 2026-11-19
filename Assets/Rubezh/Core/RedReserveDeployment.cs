using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // The first assault detachment retains its own counterattack/withdrawal state.
    // Reinforcements arriving out of contact first spread along different defensive
    // posts, then hand control back to the existing adaptive defender AI.
    public sealed class RedReserveDeploymentState
    {
        public bool Initialized;
        public readonly Dictionary<int, V2> Posts = new Dictionary<int, V2>();
        public readonly Dictionary<int, float> NextRetry = new Dictionary<int, float>();
        public readonly HashSet<int> Completed = new HashSet<int>();

        public void Clear()
        {
            Initialized = false;
            Posts.Clear();
            NextRetry.Clear();
            Completed.Clear();
        }
    }

    public static class RedReserveDeployment
    {
        static bool Reserve(BattleSimulation b, Soldier s) =>
            !b.CustomDefenders
            && s.Side == Side.Red
            && b.Counterattack.OriginalCount > 0
            && s.Id >= 5 + b.Counterattack.OriginalCount;

        // A reserve encountering the enemy immediately does NOT abandon the fight
        // just to reach a precomputed map coordinate.
        static bool HasThreat(BattleSimulation b, Soldier s)
        {
            if (s.Visible.Count > 0 || b.Time - s.LastIncoming < 3f)
                return true;
            foreach (var c in s.Contacts.Values)
                if (
                    c.Confidence > .35f
                    && b.Time - c.SeenAt < 5f
                    && V2.Distance(c.Position, s.Position) < b.WeaponRange(s) + 3f
                )
                    return true;
            foreach (var c in b.Red.Contacts.Values)
                if (
                    c.Confidence > .45f
                    && b.Time - c.SeenAt < 5f
                    && V2.Distance(c.Position, s.Position) < b.WeaponRange(s) + 2f
                )
                    return true;
            return false;
        }

        public static void OnArrival(BattleSimulation b)
        {
            var st = b.Counterattack.ReserveDeployment;
            st.Clear();
            var troops = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (Reserve(b, s) && s.Available)
                    troops.Add(s);
            if (troops.Count == 0)
                return;
            var axis = PositionLogic.Axis(b);
            var sideways = new V2(-axis.Y, axis.X);
            var used = new List<V2>();
            for (int n = 0; n < troops.Count; n++)
            {
                var soldier = troops[n];
                // At least two sectors and one central anchor, separated from each other.
                float offset = (n - (troops.Count - 1) * .5f) * 5.2f;
                var preferred = b.Map.Objective - axis * 4.5f + sideways * offset;
                float best = float.MinValue;
                V2 destination = soldier.Position;
                List<V2> path = null;
                for (int forward = -2; forward <= 2; forward++)
                for (int lateral = -2; lateral <= 2; lateral++)
                {
                    var point = TacticalOrders.Open(b, preferred + axis * forward * 1.2f + sideways * lateral * 1.1f);
                    if (
                        !b.Map.Walkable(point)
                        || V2.Distance(point, b.Map.Objective) > 13f
                        || V2.Distance(point, soldier.Position) < 1.2f
                    )
                        continue;
                    bool occupied = false;
                    foreach (var p in used)
                        if (V2.Distance(p, point) < 3.3f)
                        {
                            occupied = true;
                            break;
                        }

                    if (occupied)
                        continue;
                    foreach (var other in b.Soldiers)
                        if (
                            other.Id != soldier.Id
                            && other.Alive
                            && other.Deployed
                            && V2.Distance(other.Position, point) < 1.05f
                        )
                        {
                            occupied = true;
                            break;
                        }

                    if (occupied)
                        continue;
                    bool cover = b.Map.NearCover(point);
                    bool protectedPost = PositionLogic.Protected(b, soldier, point);
                    bool observation = b.Map.LineOfSight(point, b.Map.Entry);
                    float score =
                        (cover ? 18f : 0f)
                        + (protectedPost ? 11f : 0f)
                        + (observation ? 3f : 0f)
                        - V2.Distance(point, preferred) * 1.5f
                        - V2.Distance(point, soldier.Position) * .4f;
                    if (score <= best)
                        continue;
                    var candidate = Pathfinder.Find(
                        b.Map,
                        soldier.Position,
                        point,
                        v => TacticalAI.Risk(b, soldier, v) * 1.5f
                    );
                    b.Stats.Paths++;
                    if (candidate.Count == 0 || SupportAI.PathLength(soldier.Position, candidate) > 18f)
                        continue;
                    best = score;
                    destination = point;
                    path = candidate;
                }

                if (path == null)
                    continue; // Fallback to the original adaptive defender.
                used.Add(destination);
                st.Posts[soldier.Id] = destination;
                st.NextRetry[soldier.Id] = b.Time + 3f;
                soldier.Path = path;
                soldier.PathCursor = 0;
                soldier.Destination = soldier.OrderAnchor = destination;
                soldier.Job = Job.Reposition;
                soldier.MoveAuthorized = true;
                soldier.ManeuverPaused = false;
                soldier.LastMoved = b.Time;
                soldier.Order = "Резерв: занять свой оборонительный сектор";
                soldier.Reason = "Развёртывание по разнесённым позициям, не кучковаться у флага";
                soldier.NextDecision = b.Time;
            }

            st.Initialized = true;
            if (st.Posts.Count > 0)
                b.Log(Side.Red, "Резерв рассредоточивается по оборонительным рубежам.");
        }

        public static bool Plan(BattleSimulation b, Squad q, List<Soldier> units)
        {
            if (q.Side != Side.Red)
                return false;
            var st = b.Counterattack.ReserveDeployment;
            if (!st.Initialized || st.Posts.Count == 0)
                return false;
            bool hasPending = false;
            foreach (var soldier in units)
            {
                if (!st.Posts.TryGetValue(soldier.Id, out var post))
                    continue;
                if (!soldier.Available || HasThreat(b, soldier) || V2.Distance(soldier.Position, post) < 1.1f)
                {
                    Complete(b, soldier);
                    continue;
                }

                hasPending = true;
                soldier.MoveAuthorized = true;
                soldier.ManeuverPaused = false;
                if (
                    soldier.PathCursor >= soldier.Path.Count
                    && (!st.NextRetry.TryGetValue(soldier.Id, out var next) || b.Time >= next)
                )
                {
                    st.NextRetry[soldier.Id] = b.Time + 2.5f;
                    var path = Pathfinder.Find(
                        b.Map,
                        soldier.Position,
                        post,
                        v => TacticalAI.Risk(b, soldier, v) * 1.5f
                    );
                    b.Stats.Paths++;
                    if (path.Count > 0)
                    {
                        soldier.Path = path;
                        soldier.PathCursor = 0;
                        soldier.OrderAnchor = soldier.Destination = post;
                        soldier.Job = Job.Reposition;
                        soldier.LastMoved = b.Time;
                    }
                    else
                        Complete(b, soldier); // No infinite movement or AI lock.
                }
            }

            if (hasPending)
            {
                q.Plan = "Резерв занимает разнесённые рубежи";
                q.Execution = "После занятия позиций — обычная оборона";
            }

            return hasPending;
        }

        public static bool Decide(BattleSimulation b, Soldier soldier)
        {
            var st = b.Counterattack.ReserveDeployment;
            if (!st.Initialized || !st.Posts.TryGetValue(soldier.Id, out var goal))
                return false;
            if (!soldier.Available || HasThreat(b, soldier) || V2.Distance(soldier.Position, goal) < 1.1f)
            {
                Complete(b, soldier);
                return false;
            }

            soldier.Job = Job.Reposition;
            soldier.MoveAuthorized = true;
            soldier.ManeuverPaused = false;
            soldier.Order = "Резерв: рассредоточиться для обороны";
            return soldier.PathCursor < soldier.Path.Count;
        }

        static void Complete(BattleSimulation b, Soldier soldier)
        {
            var st = b.Counterattack.ReserveDeployment;
            if (!st.Posts.Remove(soldier.Id))
                return;
            st.Completed.Add(soldier.Id);
            st.NextRetry.Remove(soldier.Id);
            TacticalAI.Stop(soldier);
            soldier.OrderAnchor = soldier.Destination = soldier.Position;
            soldier.Job = Job.Hold;
            soldier.MoveAuthorized = true;
            soldier.NextDecision = b.Time;
            soldier.Reason = "На оборонительном рубеже — действую по обычной тактике";
            b.Red.Defense.Initialized = false;
            b.Red.Defense.NextReview = 0;
        }
    }
}
