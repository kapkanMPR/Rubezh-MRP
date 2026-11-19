using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // A controlled retrograde. It must never share the emergency retreat sprint path.
    public sealed class WithdrawalPlan
    {
        public int MoverId = -1; // compatibility: first member of the current moving element
        public readonly List<int> Movers = new List<int>();
        public readonly HashSet<int> LastMovers = new HashSet<int>();
        public int MaxConcurrent,
            GroupBounds;
        public string Composition = "";
        public float BoundStarted,
            NextBoundAt;
        public int Bounds,
            TotalBounds,
            Completed,
            Interrupted,
            CoverWaits,
            CohesionWaits,
            NoRoute;
        public bool Initialized;
        public V2 Rear;
        public float NextReport;
        public int LastMover = -1;

        public void Clear()
        {
            Movers.Clear();
            LastMovers.Clear();
            Composition = "";
            MoverId = -1;
            BoundStarted = NextBoundAt = 0;
            Bounds = 0;
            Initialized = false;
            LastMover = -1;
        }
    }

    public static class WithdrawalTactics
    {
        const float Reach = .35f,
            MaxStage = 5.2f;

        public static bool Active(Squad q)
        {
            return q.Style == CombatStyle.Withdraw && !TacticalOrders.Emergency(q);
        }

        static bool Mobile(BattleSimulation b, Soldier s)
        {
            return s.Available
                && !s.Busy
                && !s.Broken
                && s.Health >= 25
                && s.Suppression < .82f
                && s.ReloadUntil <= b.Time
                && !TacticalOrders.ServiceActive(s)
                && !s.GrenadeEscape.Active
                && s.HelpUntil <= b.Time
                && s.HelpRetreatUntil <= b.Time;
        }

        public static bool CanCover(BattleSimulation b, Soldier s)
        {
            return s.Available
                && !s.Moving
                && !s.Busy
                && !s.Broken
                && s.Health >= 25
                && s.Suppression < .64f
                && s.Ammo > 0
                && s.ReloadUntil <= b.Time
                && s.HelpTargetId < 0
                && s.RescueTargetId < 0
                && !TacticalOrders.ServiceActive(s)
                && !s.GrenadeEscape.Active;
        }

        public static bool HasCover(BattleSimulation b, Soldier mover)
        {
            V2 goal = mover.PathCursor < mover.Path.Count ? mover.Path[mover.PathCursor] : mover.Destination;
            return Cohesive(b, mover, mover.Destination) && Covered(b, mover, goal);
        }

        public static bool Covered(BattleSimulation b, Soldier mover, V2 end, HashSet<int> excluded = null)
        {
            if (mover.Broken || mover.Suppression > .82f)
                return false;
            bool exposed = false;
            for (int i = 0; i <= 6; i++)
                if (PositionLogic.Exposure(b, mover, mover.Position + (end - mover.Position) * (i / 6f)) > .12f)
                {
                    exposed = true;
                    break;
                }

            if (!exposed)
                return true;
            foreach (var ally in b.Soldiers)
                if (
                    ally != mover
                    && (excluded == null || !excluded.Contains(ally.Id))
                    && !b.Team(mover).Withdrawal.Movers.Contains(ally.Id)
                    && ally.Side == mover.Side
                    && ally.Deployed
                    && CanCover(b, ally)
                    && ally.PathCursor >= ally.Path.Count
                    && PositionLogic.UsefulSupport(b, ally, mover, end)
                )
                    return true;
            return false;
        }

        public static bool Cohesive(BattleSimulation b, Soldier mover, V2 goal)
        {
            var q = b.Team(mover);
            var rear = q.Withdrawal.Rear;
            foreach (var ally in b.Soldiers)
                if (ally != mover && q.Contains(ally) && ally.Alive && ally.Deployed)
                {
                    bool vulnerable =
                        ally.Incapacitated || ally.Broken || ally.Bleeding || TacticalOrders.ServiceActive(ally);
                    float max = vulnerable ? 7 : 11;
                    // Do not increase an existing gap. A separated member may still rejoin.
                    float old = V2.Dot(mover.Position - ally.Position, rear),
                        next = V2.Dot(goal - ally.Position, rear);
                    if (next > max && next > old + .2f)
                        return false;
                    if (
                        vulnerable
                        && V2.Distance(goal, ally.Position) > 12
                        && V2.Distance(goal, ally.Position) > V2.Distance(mover.Position, ally.Position) + .2f
                    )
                        return false;
                }

            return true;
        }

        static void Initialize(BattleSimulation b, Squad q)
        {
            var p = q.Withdrawal;
            if (p.Initialized)
                return;
            p.Initialized = true;
            V2 center = new V2();
            int count = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Alive && s.Deployed)
                {
                    center += s.Position;
                    count++;
                }

            if (count > 0)
                center /= count;
            V2 danger = new V2();
            float weight = 0;
            foreach (var c in q.Contacts.Values)
                if (PositionLogic.Recent(b, c))
                {
                    danger += c.Position * c.Confidence;
                    weight += c.Confidence;
                }

            p.Rear = weight > 0 ? (center - danger / weight).Normalized : TacticalOrders.Axis(b, q.Side) * -1;
            if (p.Rear.Length < .1f)
                p.Rear = TacticalOrders.Axis(b, q.Side) * -1;
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && s.Available
                    && !TacticalOrders.ServiceActive(s)
                    && !s.GrenadeEscape.Active
                    && !s.Busy
                )
                {
                    TacticalAI.Stop(s);
                    s.OrderAnchor = s.Destination = s.Position;
                    CommandHierarchy.ReleaseAssignment(b, s, "Прежнее назначение отменено приказом отхода", false);
                }
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            if (!Active(q))
                return false;
            Initialize(b, q);
            CommandPosture.Sync(b, q);
            var p = q.Withdrawal;
            if (q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count || !b.Soldiers[q.LeaderId].Available)
            {
                Soldier replacement = null;
                foreach (var unit in b.Soldiers)
                    if (q.Contains(unit) && unit.Available && (replacement == null || unit.Morale > replacement.Morale))
                        replacement = unit;
                if (replacement != null)
                    q.LeaderId = replacement.Id;
            }

            if (p.Movers.Count > 0)
            {
                var finished = new List<int>();
                foreach (int id in p.Movers)
                {
                    var mover = id < b.Soldiers.Count ? b.Soldiers[id] : null;
                    if (mover == null || !q.Contains(mover) || !Mobile(b, mover))
                    {
                        finished.Add(id);
                        p.Interrupted++;
                        continue;
                    }

                    if (mover.PathCursor >= mover.Path.Count || V2.Distance(mover.Position, mover.OrderAnchor) < Reach)
                    {
                        finished.Add(id);
                        p.LastMover = id;
                        p.Completed++;
                    }
                    else if (b.Time - p.BoundStarted > 14)
                    {
                        TacticalAI.Stop(mover);
                        finished.Add(id);
                        p.Interrupted++;
                    }
                }

                foreach (int id in finished)
                    p.Movers.Remove(id);
                if (p.Movers.Count == 0)
                    p.NextBoundAt = b.Time + 1.1f;
            }

            p.MoverId = p.Movers.Count > 0 ? p.Movers[0] : -1;
            if (p.Movers.Count == 0 && b.Time >= p.NextBoundAt)
            {
                var candidates = new List<Soldier>();
                foreach (var unit in b.Soldiers)
                    if (q.Contains(unit) && Mobile(b, unit) && unit.RescueTargetId < 0)
                        candidates.Add(unit);
                candidates.Sort((a, z) => V2.Dot(a.Position, p.Rear).CompareTo(V2.Dot(z.Position, p.Rear)));
                // First correct an isolated forward member before pulling the entire line back.
                bool isolated =
                    candidates.Count > 1 && V2.Dot(candidates[1].Position - candidates[0].Position, p.Rear) > 4;
                var proposals = new List<List<Soldier>>();
                int count = Math.Min(8, candidates.Count);
                for (int i = 0; i < count; i++)
                {
                    if (isolated && i > 0)
                        break;
                    proposals.Add(new List<Soldier> { candidates[i] });
                    if (isolated)
                        continue;
                    for (int j = i + 1; j < count; j++)
                    {
                        proposals.Add(new List<Soldier> { candidates[i], candidates[j] });
                        for (int k = j + 1; k < count; k++)
                            proposals.Add(new List<Soldier> { candidates[i], candidates[j], candidates[k] });
                    }
                }

                // Favour useful throughput, forward members and the same pair, but preserve a real cover element.
                proposals.Sort((a, z) => Score(z, p).CompareTo(Score(a, p)));
                bool assigned = false;
                int budget = 12;
                foreach (var group in proposals)
                {
                    if (TryElement(b, q, group))
                    {
                        assigned = true;
                        break;
                    }

                    if (--budget == 0)
                        break;
                }

                if (!assigned)
                    foreach (var candidate in candidates)
                    {
                        if (TryElement(b, q, new List<Soldier> { candidate }))
                        {
                            assigned = true;
                            break;
                        }

                        if (isolated)
                            break;
                    }

                if (assigned)
                {
                    p.MoverId = p.Movers[0];
                    p.BoundStarted = b.Time;
                    p.Bounds++;
                    p.TotalBounds++;
                    p.GroupBounds += p.Movers.Count > 1 ? 1 : 0;
                    p.MaxConcurrent = Math.Max(p.MaxConcurrent, p.Movers.Count);
                    q.Revision++;
                    b.Stats.Bounds++;
                    TacticalOrders.TrySmoke(
                        b,
                        b.Soldiers[p.MoverId],
                        TacticalOrders.ClosestKnown(b, b.Soldiers[p.MoverId])
                    );
                    b.History.Event(
                        b,
                        "withdrawalElement",
                        q.LeaderId,
                        p.MoverId,
                        b.Soldiers[p.MoverId].Position,
                        b.Soldiers[p.MoverId].Destination,
                        p.Movers.Count,
                        p.Composition
                    );
                }
                else
                {
                    p.NextBoundAt = b.Time + 1.5f;
                    if (candidates.Count > 0 && b.Time >= p.NextReport)
                    {
                        p.NextReport = b.Time + 8;
                        SmokeTactics.Request(
                            b,
                            q,
                            candidates[0],
                            candidates[0].Position + p.Rear * 3,
                            SmokePurpose.Withdrawal
                        );
                        b.History.Event(
                            b,
                            "withdrawalWait",
                            q.LeaderId,
                            candidates[0].Id,
                            candidates[0].Position,
                            candidates[0].Position,
                            0,
                            "Отход задержан: прикрытие, отставшие или маршрут"
                        );
                    }
                }
            }

            q.Phase = p.Movers.Count == 0 ? ManeuverPhase.Prepare : ManeuverPhase.Move;
            q.Plan = q.Execution =
                p.Movers.Count > 0
                    ? "Отход: перемещаются " + p.Movers.Count + ", остальные прикрывают"
                    : "Отход: готовим следующую прикрытую перебежку";
            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (!Active(q))
                return false;
            if (
                !s.Available
                || s.Broken
                || TacticalOrders.ServiceActive(s)
                || s.GrenadeEscape.Active
                || s.Busy
                || s.HelpUntil > b.Time
                || s.HelpRetreatUntil > b.Time
            )
                return false; // existing patient/panic AI retains control
            if (q.Withdrawal.Movers.Contains(s.Id))
            {
                s.Job = Job.Reposition;
                s.MoveAuthorized = HasCover(b, s);
                s.ManeuverPaused = false;
                s.Reason = s.MoveAuthorized
                    ? "Отхожу к следующему укрытию под прикрытием"
                    : "Жду возможности безопасной перебежки";
            }
            else
            {
                if (s.PathCursor < s.Path.Count)
                    TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = s.Id == q.LeaderId ? Job.Command : Job.Cover;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Reason = "Прикрываю отход товарищей";
            }

            return true;
        }

        static float Score(List<Soldier> group, WithdrawalPlan p)
        {
            float score = group.Count * 3;
            foreach (var s in group)
                score -= V2.Dot(s.Position, p.Rear) * .4f + (p.LastMovers.Contains(s.Id) ? 3 : 0) + s.Suppression * 3;
            if (group.Count >= 2 && group[0].Assignment.PairId == group[1].Assignment.PairId)
                score += 1;
            return score;
        }

        // Plan every route against the SAME stationary covering set, then commit atomically.
        public static bool TryElement(BattleSimulation b, Squad q, List<Soldier> group)
        {
            if (group.Count < 1 || group.Count > 3)
                return false;
            var moving = new HashSet<int>();
            foreach (var s in group)
                if (!q.Contains(s) || !Mobile(b, s) || !moving.Add(s.Id))
                    return false;
            int watchers = 0;
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && s.Deployed
                    && !moving.Contains(s.Id)
                    && CanCover(b, s)
                    && s.PathCursor >= s.Path.Count
                )
                    watchers++;
            if (group.Count > 1 && watchers < 1)
                return false;
            var routes = new List<List<V2>>();
            var posts = new List<V2>();
            foreach (var s in group)
            {
                List<V2> path;
                V2 post;
                if (!FindRoute(b, s, moving, out path, out post))
                    return false;
                foreach (var other in posts)
                    if (V2.Distance(post, other) < 1.1f)
                        return false;
                routes.Add(path);
                posts.Add(post);
            }

            q.Withdrawal.Movers.Clear();
            q.Withdrawal.LastMovers.Clear();
            for (int i = 0; i < group.Count; i++)
            {
                var s = group[i];
                s.Path = routes[i];
                s.PathCursor = 0;
                s.OrderAnchor = s.Destination = posts[i];
                CommandHierarchy.TransferAssignment(b, s, "Передано прикрытому отходу");
                s.Job = Job.Reposition;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.WasAtAnchor = false;
                s.LastMoved = b.Time;
                s.NextDecision = b.Time;
                s.Order = "Прикрытая перебежка назад";
                s.Reason = "Отхожу в назначенном составе";
                q.Withdrawal.Movers.Add(s.Id);
                q.Withdrawal.LastMovers.Add(s.Id);
            }

            q.Withdrawal.Composition = string.Join(",", q.Withdrawal.Movers);
            return true;
        }

        static bool FindRoute(BattleSimulation b, Soldier s, HashSet<int> excluded, out List<V2> route, out V2 goal)
        {
            route = null;
            goal = s.Position;
            var plan = b.Team(s).Withdrawal;
            V2 rear = plan.Rear;
            V2 lateral = new V2(-rear.Y, rear.X);
            float best = float.MinValue;
            List<V2> bestPath = null;
            V2 bestPost = s.Position;
            // Six short candidates. A path is chosen only if its endpoint is genuinely behind the start.
            for (int i = 0; i < 6; i++)
            {
                float depth = i < 3 ? 3.3f : MaxStage;
                float spread = ((i % 3) - 1) * 2.0f;
                V2 post = TacticalOrders.Open(b, s.Position + rear * depth + lateral * spread);
                if (
                    !b.Map.Walkable(post)
                    || PositionLogic.Occupied(b, s, post)
                    || V2.Dot(post - s.Position, rear) < 1.7f
                )
                    continue;
                if (!Cohesive(b, s, post))
                {
                    plan.CohesionWaits++;
                    continue;
                }

                float risk = PositionLogic.Exposure(b, s, post);
                bool sheltered = PositionLogic.Protected(b, s, post);
                if (!sheltered && risk > .48f)
                    continue;
                var path = LossMemory.Find(b, s, post, p => PositionLogic.Exposure(b, s, p) * 2.5f);
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 9f)
                    continue;
                bool covered = true;
                foreach (var point in path)
                {
                    if (!Covered(b, s, point, excluded) || !Cohesive(b, s, point))
                    {
                        covered = false;
                        break;
                    }
                }

                if (!covered)
                {
                    plan.CoverWaits++;
                    continue;
                }

                float score =
                    (sheltered ? 8f : 0f)
                    - risk * 5f
                    + V2.Dot(post - s.Position, rear) * .45f
                    - SupportAI.PathLength(s.Position, path) * .15f;
                if (score > best)
                {
                    best = score;
                    bestPath = path;
                    bestPost = post;
                }
            }

            if (bestPath == null)
            {
                plan.NoRoute++;
                return false;
            }

            route = bestPath;
            goal = bestPost;
            return true;
        }
    }
}
