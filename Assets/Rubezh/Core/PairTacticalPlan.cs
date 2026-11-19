using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    // Experimental game AI. Candidate generation -> filters -> scoring -> bounded commitment.
    // Only map geometry, own soldiers and Knowledge contacts are read. Never hidden enemy state.
    public sealed class PairPlanSettings
    {
        public bool AdaptiveRisk = false,
            RouteSmoke = false;
        public bool Enabled = true,
            Explore = true,
            AllowFlank = true,
            PlanWatch = false;
        public float OpenBudget = 4,
            MaxExposure = 1.2f;
        public float Search = 10,
            MaxPath = 14,
            Commit = 12,
            Replan = 3,
            MinSupportHealth = 45,
            SupportGrace = 5,
            HiddenWeight = 5,
            ProgressWeight = .35f,
            Separation = 3.5f,
            ExposureWeight = 9,
            ExploreWeight = 2,
            ExploreCommit = 7,
            DamageAbort = 18,
            NoProgress = 3,
            FireHold = 6;
    }

    public sealed class PairPlanStats
    {
        public int RouteSmokeRequests,
            RouteSmokeAccepted;
        public int CombatArrivals,
            CombatUsefulArrivals,
            ExplorationCellsObserved,
            ExploreStarts,
            SupportStarts,
            FlankStarts,
            Arrivals,
            UsefulArrivals,
            Aborts,
            EmergencyAborts,
            Stalls,
            Overwrites,
            HeldShots,
            Queries,
            RejectedRoutes,
            SupportReleases;
    }

    public static class PairTacticalPlan
    {
        sealed class Move
        {
            public int Unit,
                Buddy,
                Revision,
                Enemy = -1,
                Shots;
            public string Kind;
            public V2 Goal,
                Progress;
            public float Until,
                ProgressAt,
                Health,
                BuddyHealth;
            public List<V2> Route;
            public int Cursor;
            public Contact Threat;
            public List<V2> Unseen = new List<V2>();
        }

        sealed class Pending
        {
            public int Unit,
                Shots;
            public float Until;
            public string Kind;
            public List<V2> Unseen;
        }

        sealed class Memory
        {
            public float Next,
                SmokeRetry;
            public readonly Dictionary<int, float> SupportOnly = new Dictionary<int, float>();
            public readonly Dictionary<int, Move> Moves = new Dictionary<int, Move>();
            public readonly Dictionary<int, float> Retry = new Dictionary<int, float>();
            public readonly List<Pending> Arrivals = new List<Pending>();
            public readonly Dictionary<int, float> Explored = new Dictionary<int, float>();
            public readonly PairPlanStats Stats = new PairPlanStats();
        }

        static readonly ConditionalWeakTable<BattleSimulation, Memory> Data =
            new ConditionalWeakTable<BattleSimulation, Memory>();

        public static void ReleaseForCombat(BattleSimulation b)
        {
            var m = Data.GetOrCreateValue(b);
            foreach (var move in new List<Move>(m.Moves.Values))
                End(b, m, move, "combat_handover", false);
            m.SupportOnly.Clear();
            m.Retry.Clear();
        }

        public static PairPlanStats Stats(BattleSimulation b) => Data.GetOrCreateValue(b).Stats;

        static bool Enabled(BattleSimulation b) =>
            !ReconCombatPhase.Active(b)
            && b.PairPlan != null
            && b.PairPlan.Enabled
            && b.PairExperimentMarch
            && !b.Historical01225;

        static bool Free(BattleSimulation b, Soldier s) =>
            s.Side == Side.Green
            && s.Deployed
            && s.Available
            && !s.Broken
            && !s.Bleeding
            && TacticalMarch.Free(b, s)
            && !s.GrenadeEscape.Active;

        static Soldier Buddy(BattleSimulation b, Soldier s)
        {
            if (s.BuddyId < 0 || s.BuddyId >= b.Soldiers.Count || s.BuddyId == s.Id)
                return null;
            var a = b.Soldiers[s.BuddyId];
            return a.Side == s.Side && a.Deployed && a.Available ? a : null;
        }

        static bool Ready(BattleSimulation b, Soldier s, Contact c) =>
            Free(b, s)
            && s.Ammo >= 3
            && s.Suppression < .6f
            && PositionLogic.CanEngage(b, s, s.Position, c)
            && !b.FriendlyInLane(s, c.Position);

        static Contact Threat(BattleSimulation b, Soldier a, Soldier z)
        {
            Contact best = null;
            foreach (var s in new[] { a, z })
            foreach (var c in Knowledge.Contacts(b, s))
                if (c.Visual && !c.Inferred && PositionLogic.Recent(b, c) && (best == null || c.SeenAt > best.SeenAt))
                    best = c;
            return best;
        }

        static void Event(BattleSimulation b, string kind, Soldier s, V2 goal, string why)
        {
            b.History.Event(b, "pairPlan" + kind, s.Id, s.BuddyId, s.Position, goal, 0, why);
        }

        public static bool FlankRoute(BattleSimulation b, Soldier s)
        {
            if (!Enabled(b))
                return false;
            Move move;
            return Data.GetOrCreateValue(b).Moves.TryGetValue(s.Id, out move)
                && move.Kind == "Flank"
                && b.Time < move.Until
                && object.ReferenceEquals(s.Path, move.Route);
        }

        public static bool Owns(BattleSimulation b, Soldier s) =>
            Enabled(b) && Data.GetOrCreateValue(b).Moves.ContainsKey(s.Id);

        static void End(BattleSimulation b, Memory m, Move p, string reason, bool arrived)
        {
            var s = b.Soldiers[p.Unit];
            m.Moves.Remove(p.Unit);
            m.Retry[p.Unit] = b.Time + b.PairPlan.Replan;
            if (object.ReferenceEquals(s.Path, p.Route))
                TacticalAI.Stop(s);
            if (arrived)
            {
                m.Stats.Arrivals++;
                if (p.Kind != "Explore")
                    m.Stats.CombatArrivals++;
                m.Arrivals.Add(
                    new Pending
                    {
                        Unit = s.Id,
                        Shots = s.Shots,
                        Until = b.Time + 5,
                        Kind = p.Kind,
                        Unseen = p.Unseen,
                    }
                );
            }
            else
            {
                m.Stats.Aborts++;
                if (reason == "emergency")
                    m.Stats.EmergencyAborts++;
                if (reason == "stalled")
                    m.Stats.Stalls++;
            }

            Event(b, arrived ? "Arrival" : "Abort", s, p.Goal, p.Kind + ":" + reason);
            s.NextDecision = b.Time;
        }

        static void Start(
            BattleSimulation b,
            Memory m,
            Soldier s,
            Soldier buddy,
            V2 goal,
            List<V2> route,
            string kind,
            Contact c
        )
        {
            var p = new Move
            {
                Unit = s.Id,
                Buddy = buddy.Id,
                Revision = b.Team(s).Revision,
                Goal = goal,
                Route = route,
                Kind = kind,
                Enemy = c == null ? -1 : c.EnemyId,
                Threat = c == null ? null : c.Copy(),
                Until = b.Time + (kind == "Explore" ? b.PairPlan.ExploreCommit : b.PairPlan.Commit),
                Progress = s.Position,
                ProgressAt = b.Time,
                Health = s.Health,
                BuddyHealth = buddy.Health,
                Shots = s.Shots,
            };
            if (kind == "Explore")
                for (int k = 0; k < 12; k++)
                {
                    float angle = k * (float)Math.PI / 6;
                    var probe = goal + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 3;
                    if (b.Map.Walkable(probe) && !b.Map.LineOfSight(s.Position, probe))
                        p.Unseen.Add(probe);
                }

            m.Moves[s.Id] = p;
            if (kind == "Explore")
                m.Stats.ExploreStarts++;
            else if (kind == "Flank")
                m.Stats.FlankStarts++;
            else
                m.Stats.SupportStarts++;
            s.Awareness.ContactMove.Until = 0;
            s.Awareness.MarchRouteUntil = 0;
            s.Tactical.ShelterUntil = 0;
            s.Tactical.CommitUntil = 0;
            Apply(b, s, p);
            Event(b, "Start", s, goal, kind);
        }

        static void Apply(BattleSimulation b, Soldier s, Move p)
        {
            s.Path = p.Route;
            s.PathCursor = p.Cursor;
            s.Destination = s.OrderAnchor = p.Goal;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            CommandHierarchy.TransferAssignment(b, s, "Передано тактическому плану двойки");
            s.SupportReady = false;
            s.Job = Job.Reposition;
            s.Reason =
                p.Kind == "Explore" ? "Исследую отдельный сектор у препятствия"
                : p.Kind == "Flank" ? "Иду на другой рубеж; проверяю состояние напарника"
                : "Занимаю позицию с полезным сектором поддержки";
        }

        public static void Enforce(BattleSimulation b)
        {
            if (!Enabled(b) || QuietRecon.Active(b) || ReconOpening.Preparing(b))
                return;
            var m = Data.GetOrCreateValue(b);
            var cfg = b.PairPlan;
            for (int i = m.Arrivals.Count - 1; i >= 0; i--)
            {
                var a = m.Arrivals[i];
                var u = b.Soldiers[a.Unit];
                if (a.Kind == "Explore" && u.Available)
                {
                    for (int k = a.Unseen.Count - 1; k >= 0; k--)
                        if (
                            DirectionalVision.InView(b, u, a.Unseen[k])
                            && b.Map.LineOfSight(u.Position, a.Unseen[k])
                            && b.SmokeTransmission(u.Position, a.Unseen[k]) > .8f
                        )
                        {
                            m.Stats.ExplorationCellsObserved++;
                            a.Unseen.RemoveAt(k);
                        }
                }

                if (u.Shots > a.Shots)
                {
                    m.Stats.UsefulArrivals++;
                    if (a.Kind != "Explore")
                        m.Stats.CombatUsefulArrivals++;
                    Event(b, "Useful", u, u.Position, "fire_after_arrival");
                    m.Arrivals.RemoveAt(i);
                }
                else if (b.Time >= a.Until)
                    m.Arrivals.RemoveAt(i);
            }

            foreach (var p in new List<Move>(m.Moves.Values))
            {
                var s = b.Soldiers[p.Unit];
                var buddy = Buddy(b, s);
                if (
                    !Free(b, s)
                    || buddy == null
                    || buddy.Id != p.Buddy
                    || b.Team(s).Revision != p.Revision
                    || b.Team(s).Style != CombatStyle.Balanced && b.Team(s).Style != CombatStyle.Aggressive
                )
                {
                    End(b, m, p, "state_or_order", false);
                    continue;
                }

                if (object.ReferenceEquals(s.Path, p.Route))
                    p.Cursor = s.PathCursor;
                else
                    m.Stats.Overwrites++;
                if (V2.Distance(s.Position, p.Progress) > .25f)
                {
                    p.Progress = s.Position;
                    p.ProgressAt = b.Time;
                }

                if (V2.Distance(s.Position, p.Goal) < .6f)
                {
                    End(b, m, p, "reached", true);
                    continue;
                }

                if (p.Cursor >= p.Route.Count)
                {
                    End(b, m, p, "route_removed_before_arrival", false);
                    continue;
                }

                if (SmokeTactics.Waiting(b, s))
                    p.ProgressAt = b.Time;
                if (b.Time > p.Until || b.Time - p.ProgressAt > cfg.NoProgress)
                {
                    End(b, m, p, b.Time > p.Until ? "deadline" : "stalled", false);
                    continue;
                }

                if (p.Kind == "Explore" && !TacticalMarch.Quiet(b, b.Team(s)))
                {
                    End(b, m, p, "contact", false);
                    m.Retry[s.Id] = b.Time;
                    continue;
                }

                if (p.Kind != "Explore")
                {
                    var c = Threat(b, s, buddy);
                    if (c == null)
                    {
                        End(b, m, p, "stale_contact", false);
                        continue;
                    }

                    if (
                        p.Kind == "Flank"
                        && (
                            buddy.Health < cfg.MinSupportHealth
                            || p.BuddyHealth - buddy.Health >= cfg.DamageAbort
                            || buddy.Suppression > .6f
                            || buddy.ReloadUntil > b.Time
                            || b.Time - buddy.LastFire > cfg.SupportGrace
                        )
                    )
                    {
                        End(b, m, p, "emergency", false);
                        m.Retry[s.Id] = b.Time;
                        m.SupportOnly[s.Id] = b.Time + 8;
                        continue;
                    }

                    if (p.Health - s.Health >= cfg.DamageAbort)
                    {
                        End(b, m, p, "incoming_damage", false);
                        continue;
                    }
                }

                Apply(b, s, p);
            }

            // Ownership is applied after legacy squad planning. Medical/service actions always win.
            foreach (var p in m.Moves.Values)
                if (p.Kind == "Flank")
                {
                    var support = b.Soldiers[p.Buddy];
                    if (Free(b, support) && p.Threat != null && Ready(b, support, p.Threat))
                    {
                        TacticalAI.Stop(support);
                        support.MoveAuthorized = false;
                        support.Job = Job.Cover;
                        support.Reason = "Поддерживаю общий манёвр двойки";
                    }
                }

            if (b.Time < m.Next)
                return;
            m.Next = b.Time + .75f;
            var used = new HashSet<int>();
            foreach (var s in b.Soldiers)
            {
                if (!Free(b, s) || used.Contains(s.Id))
                    continue;
                var buddy = Buddy(b, s);
                if (buddy == null || !Free(b, buddy))
                    continue;
                used.Add(s.Id);
                used.Add(buddy.Id);
                if (m.Moves.ContainsKey(s.Id) || m.Moves.ContainsKey(buddy.Id))
                    continue;
                if (b.Team(s).Style != CombatStyle.Balanced && b.Team(s).Style != CombatStyle.Aggressive)
                    continue;
                Contact c = Threat(b, s, buddy);
                if (c == null)
                {
                    if (cfg.Explore && TacticalMarch.Quiet(b, b.Team(s)))
                        Explore(b, m, s, buddy);
                    continue;
                }

                Soldier mover = s,
                    support = buddy;
                bool sr = Ready(b, s, c),
                    br = Ready(b, buddy, c);
                if (sr && !br || sr && br && s.Health >= buddy.Health)
                {
                    mover = buddy;
                    support = s;
                }

                float retry;
                if (m.Retry.TryGetValue(mover.Id, out retry) && retry > b.Time)
                    continue;
                m.Retry[mover.Id] = b.Time + cfg.Replan;
                bool urgent =
                    support.Health < cfg.MinSupportHealth || support.Suppression > .5f || support.ReloadUntil > b.Time;
                if (Ready(b, mover, c) && b.Time - mover.LastFire < 3 && !urgent)
                    continue;
                float supportOnly;
                bool flank =
                    cfg.AllowFlank
                    && (!m.SupportOnly.TryGetValue(mover.Id, out supportOnly) || supportOnly <= b.Time)
                    && !urgent
                    && Ready(b, support, c)
                    && b.Time - support.LastFire < cfg.SupportGrace
                    && V2.Distance(mover.Position, support.Position) < 12;
                if (!CombatQuery(b, m, mover, support, c, flank) && flank)
                    CombatQuery(b, m, mover, support, c, false);
            }
        }

        static bool CombatQuery(BattleSimulation b, Memory m, Soldier s, Soldier buddy, Contact c, bool flank)
        {
            m.Stats.Queries++;
            b.Tactics.Refresh(b.Map);
            var cfg = b.PairPlan;
            var candidates = new List<KeyValuePair<float, V2>>();
            var seen = new HashSet<int>();
            foreach (var slot in b.Tactics.Slots)
            {
                var p = slot.Position;
                int key = (int)p.Y * b.Map.Columns + (int)p.X;
                float d = V2.Distance(s.Position, p);
                if (
                    !seen.Add(key)
                    || d < 1
                    || d > cfg.Search
                    || PositionLogic.Occupied(b, s, p)
                    || !CommandHierarchy.WithinManeuver(b, s, p)
                    || !PositionLogic.CanEngage(b, s, p, c)
                    || !PositionLogic.Protected(b, s, p)
                    || V2.Distance(p, buddy.Position) < cfg.Separation
                )
                    continue;
                float angle = DirectionalVision.Angle(s.Position - c.Position, p - c.Position);
                if (flank && angle < 30)
                    continue;
                float rear = -V2.Dot(c.ObservedFacing.Normalized, (p - c.Position).Normalized);
                float score =
                    -d * .5f
                    - PositionLogic.Exposure(b, s, p) * cfg.ExposureWeight
                    + (flank ? rear * 3 + angle * .025f : 0)
                    - V2.Distance(p, b.Map.Objective) * cfg.ProgressWeight;
                candidates.Add(new KeyValuePair<float, V2>(score, p));
            }

            candidates.Sort((a, z) => z.Key.CompareTo(a.Key));
            List<V2> best = null;
            V2 goal = new V2();
            float bestScore = float.MinValue;
            int n = 0;
            V2 smokeGoal = new V2();
            bool riskBlocked = false;
            float openBudget = cfg.OpenBudget,
                maxExposure = cfg.MaxExposure;
            if (cfg.AdaptiveRisk && buddy.Health >= 65 && Ready(b, buddy, c) && b.Time - buddy.LastFire < 2)
            {
                openBudget = Math.Max(openBudget, 6);
                maxExposure = Math.Max(maxExposure, 1.5f);
            }

            foreach (var candidate in candidates)
            {
                if (n++ >= 10)
                    break;
                var p = candidate.Value;
                var path = LossMemory.Find(
                    b,
                    s,
                    p,
                    v =>
                        QuietRecon.UncertaintyCost(b, v)
                        + PositionLogic.Exposure(b, s, v) * cfg.ExposureWeight
                        + FireLaneSafety.PathCost(b, s, v)
                        + (PositionLogic.Occupied(b, s, v) ? 12 : 0)
                );
                b.Stats.Paths++;
                float length = SupportAI.PathLength(s.Position, path);
                if (path.Count == 0 || length > cfg.MaxPath)
                {
                    m.Stats.RejectedRoutes++;
                    continue;
                }

                float hidden = 0,
                    open = 0,
                    peak = 0;
                V2 prev = s.Position;
                foreach (var v in path)
                {
                    float lengthStep = V2.Distance(prev, v);
                    int samples = Math.Max(1, (int)Math.Ceiling(lengthStep * 2));
                    for (int j = 1; j <= samples; j++)
                    {
                        var sample = prev + (v - prev) * (j / (float)samples);
                        float exposure = PositionLogic.Exposure(b, s, sample);
                        peak = Math.Max(peak, exposure);
                        if (exposure > .5f)
                            open += lengthStep / samples;
                    }

                    if (!b.Map.LineOfSight(v, c.Position))
                        hidden++;
                    prev = v;
                }

                if (open > openBudget || peak > maxExposure)
                {
                    m.Stats.RejectedRoutes++;
                    if (!riskBlocked)
                    {
                        smokeGoal = p;
                        riskBlocked = true;
                    }

                    continue;
                }

                float score = candidate.Key - length * .4f + (flank ? hidden / path.Count * cfg.HiddenWeight : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = path;
                    goal = p;
                }
            }

            if (best == null)
            {
                if (
                    cfg.RouteSmoke
                    && riskBlocked
                    && c.Visual
                    && !c.Inferred
                    && b.Time - c.SeenAt < 6
                    && b.Time >= m.SmokeRetry
                )
                {
                    m.SmokeRetry = b.Time + 5;
                    m.Stats.RouteSmokeRequests++;
                    if (SmokeTactics.Request(b, b.Team(s), s, smokeGoal, SmokePurpose.Route, s))
                    {
                        m.Stats.RouteSmokeAccepted++;
                        m.Retry[s.Id] = b.Time + 3;
                        Event(b, "Smoke", s, smokeGoal, "known_threat_blocks_route");
                        return true;
                    }
                }

                return false;
            }

            Start(b, m, s, buddy, goal, best, flank ? "Flank" : "Support", c);
            return true;
        }

        static void Explore(BattleSimulation b, Memory m, Soldier a, Soldier z)
        {
            float retry;
            if (m.Retry.TryGetValue(a.Id, out retry) && retry > b.Time)
                return;
            m.Retry[a.Id] = b.Time + 4;
            b.Tactics.Refresh(b.Map);
            V2 wall = new V2();
            float nearest = 10;
            int wallKey = -1;
            foreach (var slot in b.Tactics.Slots)
            {
                var p = slot.Position;
                float d = V2.Distance(a.Position, p);
                float inspected;
                if (
                    d < 2
                    || d >= nearest
                    || !b.Map.LineOfSight(a.Position, p)
                    || !DirectionalVision.InView(b, a, p)
                    || V2.Dot(p - a.Position, PositionLogic.Axis(b)) < 0
                    || m.Explored.TryGetValue(slot.ObjectId, out inspected) && b.Time - inspected < 25
                )
                    continue;
                int x = slot.ObjectId % b.Map.Columns,
                    y = slot.ObjectId / b.Map.Columns;
                if (b.Map.At(new V2(x + .5f, y + .5f)) != Tile.Wall)
                    continue;
                nearest = d;
                wall = new V2(x + .5f, y + .5f);
                wallKey = slot.ObjectId;
            }

            if (wallKey < 0)
                return;
            var chosen = new List<V2>();
            bool started = false;
            foreach (var s in new[] { a, z })
            {
                var buddy = s == a ? z : a;
                float best = float.MinValue;
                V2 goal = new V2();
                List<V2> route = null;
                int checks = 0;
                var candidates = new List<KeyValuePair<float, V2>>();
                var seen = new HashSet<int>();
                foreach (var slot in b.Tactics.Slots)
                {
                    var p = slot.Position;
                    int key = (int)p.Y * b.Map.Columns + (int)p.X;
                    float d = V2.Distance(s.Position, p);
                    if (
                        !seen.Add(key)
                        || d < 1
                        || d > 8
                        || V2.Distance(p, wall) > 6
                        || PositionLogic.Occupied(b, s, p)
                        || !CommandHierarchy.WithinManeuver(b, s, p)
                    )
                        continue;
                    bool duplicate = false;
                    foreach (var q in chosen)
                        if (V2.Distance(q, p) < b.PairPlan.Separation)
                            duplicate = true;
                    if (duplicate)
                        continue;
                    float gain = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        float ang = k * (float)Math.PI / 4;
                        V2 probe = wall + new V2((float)Math.Cos(ang), (float)Math.Sin(ang)) * 3;
                        if (
                            b.Map.Walkable(probe)
                            && !b.Map.LineOfSight(s.Position, probe)
                            && b.Map.LineOfSight(p, probe)
                        )
                            gain++;
                    }

                    float score =
                        gain * b.PairPlan.ExploreWeight
                        - d * .6f
                        + (V2.Distance(s.Position, b.Map.Objective) - V2.Distance(p, b.Map.Objective)) * .5f;
                    foreach (var q in chosen)
                    {
                        float overlap = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            float ang = k * (float)Math.PI / 4;
                            V2 probe = wall + new V2((float)Math.Cos(ang), (float)Math.Sin(ang)) * 3;
                            if (b.Map.LineOfSight(q, probe) && b.Map.LineOfSight(p, probe))
                                overlap++;
                        }

                        score -= overlap;
                    }

                    if (gain > 0 || chosen.Count > 0)
                        candidates.Add(new KeyValuePair<float, V2>(score, p));
                }

                candidates.Sort((x, y) => y.Key.CompareTo(x.Key));
                foreach (var c in candidates)
                {
                    if (checks++ >= 5)
                        break;
                    var path = LossMemory.Find(
                        b,
                        s,
                        c.Value,
                        v => PairExperiment.RouteCost(b, s, v) + FireLaneSafety.PathCost(b, s, v)
                    );
                    b.Stats.Paths++;
                    float len = SupportAI.PathLength(s.Position, path);
                    if (path.Count == 0 || len > 10)
                        continue;
                    float score = c.Key - len * .3f;
                    if (score > best)
                    {
                        best = score;
                        goal = c.Value;
                        route = path;
                    }
                }

                if (route != null)
                {
                    Start(b, m, s, buddy, goal, route, "Explore", null);
                    chosen.Add(goal);
                    started = true;
                }
            }

            if (started)
                m.Explored[wallKey] = b.Time;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!Enabled(b))
                return false;
            var m = Data.GetOrCreateValue(b);
            Move p;
            if (m.Moves.TryGetValue(s.Id, out p) && Free(b, s))
            {
                if (object.ReferenceEquals(s.Path, p.Route))
                    p.Cursor = s.PathCursor;
                Apply(b, s, p);
                return true;
            }

            foreach (var move in m.Moves.Values)
                if (
                    move.Kind == "Flank"
                    && move.Buddy == s.Id
                    && Free(b, s)
                    && move.Threat != null
                    && Ready(b, s, move.Threat)
                )
                {
                    TacticalAI.Stop(s);
                    s.MoveAuthorized = false;
                    s.Job = Job.Cover;
                    return true;
                }

            return false;
        }

        public static bool Watch(BattleSimulation b, Soldier s, out V2 direction)
        {
            direction = new V2();
            if (!Enabled(b) || !b.PairPlan.PlanWatch || !TacticalMarch.Quiet(b, b.Team(s)))
                return false;
            var m = Data.GetOrCreateValue(b);
            Move p;
            if (
                !m.Moves.TryGetValue(s.Id, out p)
                || p.Kind != "Explore"
                || p.Unseen.Count == 0
                || V2.Distance(s.Position, p.Goal) > 2.5f
            )
                return false;
            V2 best = p.Unseen[0];
            float score = float.MinValue;
            foreach (var point in p.Unseen)
            {
                if (DirectionalVision.Angle(s.Awareness.Look, point - s.Position) > 60)
                    continue;
                float v = -V2.Distance(s.Position, point);
                if (b.Map.LineOfSight(s.Position, point))
                    v += 5;
                if (v > score)
                {
                    score = v;
                    best = point;
                }
            }

            if (score == float.MinValue)
                return false;
            direction = (best - s.Position).Normalized;
            return direction.Length > .1f;
        }

        public static bool StepCovered(BattleSimulation b, Soldier s)
        {
            if (!Owns(b, s) || !Free(b, s) || !MotionCombat.HasRoute(s))
                return false;
            V2 delta = s.Path[s.PathCursor] - s.Position,
                next = s.Position + delta.Normalized * Math.Min(.6f, delta.Length);
            float exposure = PositionLogic.Exposure(b, s, next),
                current = PositionLogic.Exposure(b, s, s.Position);
            if (exposure <= .12f || exposure <= current + .02f)
                return true;
            var buddy = Buddy(b, s);
            var c = buddy == null ? null : Threat(b, s, buddy);
            return c != null
                && Ready(b, buddy, c)
                && !buddy.Moving
                && b.Time - buddy.LastFire < 2
                && PositionLogic.UsefulSupport(b, buddy, s, next);
        }

        public static bool HoldFire(BattleSimulation b, Soldier s, Contact c)
        {
            if (!Enabled(b))
                return false;
            var m = Data.GetOrCreateValue(b);
            Move p;
            if (
                !m.Moves.TryGetValue(s.Id, out p)
                || p.Kind != "Flank"
                || b.Time - s.LastIncoming < 3
                || b.Time - s.LastFire < 3
                || V2.Distance(s.Position, p.Goal) < 1
            )
                return false;
            if (b.Time > p.Until - b.PairPlan.Commit + b.PairPlan.FireHold)
                return false;
            bool away =
                c.ObservedFacing.Length > .1f
                && V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized) < -.3f;
            if (away)
                m.Stats.HeldShots++;
            return away;
        }
    }
}
