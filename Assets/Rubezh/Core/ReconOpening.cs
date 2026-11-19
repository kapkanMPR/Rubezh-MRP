using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    public sealed class OpeningStats
    {
        public int HorizonCandidates,
            HorizonExits,
            CritEligible,
            CritRolled,
            CritHits;
        public int Evaluations,
            Plans,
            Rejected,
            Commands,
            Aborted,
            Assigned,
            Ready,
            Shots,
            ParticipantsFired;
        public float DamageFirst10Seconds;
        public float PreparationSeconds,
            FirstShotDelay = -1,
            ShotSpread = -1;
        public string EndReason = "";
    }

    // Experimental commander-level transition. Only friendly knowledge is used to plan.
    public static class ReconOpening
    {
        sealed class Task
        {
            public int Unit,
                Target,
                Cursor,
                CritRemaining = 3;
            public float CritLast = -100;
            public bool CritSpent;
            public V2 Next;
            public V2 Goal;
            public List<V2> Route,
                Published;
            public bool Ready,
                Fired,
                OpenGround;
            public float ProgressAt;
            public string Block = "";
            public V2 Progress;
        }

        sealed class State
        {
            public bool Preparing,
                Released;
            public int Leader,
                Revision;
            public float Started,
                ReleaseAt,
                Until,
                First = -1,
                Last,
                DamageAtRelease;
            public bool DamageMeasured;
            public readonly List<Task> Tasks = new List<Task>();
            public readonly OpeningStats Stats = new OpeningStats();
        }

        sealed class Candidate
        {
            public bool OpenGround;
            public int Target;
            public V2 Point;
            public float Score;
            public V2 Next;
            public int Exits;
            public List<V2> Path;
        }

        static readonly ConditionalWeakTable<BattleSimulation, State> Data =
            new ConditionalWeakTable<BattleSimulation, State>();

        public static OpeningStats Stats(BattleSimulation b) => Data.GetOrCreateValue(b).Stats;

        public static bool Preparing(BattleSimulation b) => b.ReconOpeningEnabled && Data.GetOrCreateValue(b).Preparing;

        static bool Free(BattleSimulation b, Soldier s) =>
            s.Side == Side.Green
            && s.Deployed
            && s.Available
            && !s.Broken
            && !s.Bleeding
            && !s.Busy
            && !s.GrenadeEscape.Active
            && !TacticalOrders.ServiceActive(s)
            && !FriendlyGrenadeSafety.Active(b, s)
            && !HandLauncher.Active(s)
            && !PairExchange.Active(b, s)
            && s.HandsBusyUntil <= b.Time
            && s.ReloadUntil <= b.Time;

        static Contact Known(BattleSimulation b, Soldier s, int target)
        {
            Contact best = null;
            foreach (var c in Knowledge.Contacts(b, s))
                if (
                    c.EnemyId == target
                    && !c.Inferred
                    && c.Confidence > .45f
                    && b.Time - c.SeenAt < 4
                    && (best == null || c.SeenAt > best.SeenAt)
                )
                    best = c;
            return best;
        }

        static Task Find(State st, Soldier s)
        {
            foreach (var t in st.Tasks)
                if (t.Unit == s.Id)
                    return t;
            return null;
        }

        static bool SafePoint(BattleSimulation b, Soldier s, V2 point, List<Contact> known)
        {
            foreach (var c in known)
            {
                float distance = V2.Distance(point, c.Position);
                if (distance < 5)
                    return false;
                if (
                    b.Map.LineOfSight(point, c.Position)
                    && c.ObservedFacing.Length > .1f
                    && V2.Dot(c.ObservedFacing.Normalized, (point - c.Position).Normalized) > .25f
                    && distance < b.ReconStandOff
                )
                    return false;
            }

            return true;
        }

        public static void Begin(BattleSimulation b)
        {
            if (!b.ReconOpeningEnabled || !b.PairExperimentMarch || b.Historical01225 || ReconCombatPhase.Active(b))
                return;
            var st = Data.GetOrCreateValue(b);
            st.Stats.Evaluations++;
            int leader = b.Green.LeaderId;
            if (leader < 0 || leader >= b.Soldiers.Count || !Free(b, b.Soldiers[leader]))
            {
                Reject(b, st, "commander_unavailable");
                return;
            }

            var units = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Deployed && s.Available)
                {
                    if (!Free(b, s) || s.LastFire >= 0 || s.Suppression > .15f)
                    {
                        Reject(b, st, "group_not_quiet");
                        return;
                    }

                    units.Add(s);
                }

            if (units.Count < 2)
            {
                Reject(b, st, "not_enough_units");
                return;
            }

            var known = new List<Contact>();
            var ids = new HashSet<int>();
            foreach (var s in units)
            foreach (var c in Knowledge.Contacts(b, s))
                if (!c.Inferred && c.Visual && c.Confidence > .5f && b.Time - c.SeenAt < 2 && ids.Add(c.EnemyId))
                    known.Add(c.Copy());
            if (known.Count == 0)
            {
                Reject(b, st, "no_fresh_targets");
                return;
            }

            st.Tasks.Clear();
            var assigned = new HashSet<int>();
            foreach (var s in units)
            {
                var candidates = new List<Candidate>();
                foreach (var c in known)
                {
                    for (int y = -12; y <= 12; y++)
                    for (int x = -12; x <= 12; x++)
                    {
                        V2 p =
                            x == 0 && y == 0
                                ? s.Position
                                : new V2(
                                    (float)Math.Floor(s.Position.X) + x * .5f + .5f,
                                    (float)Math.Floor(s.Position.Y) + y * .5f + .5f
                                );
                        float d = V2.Distance(s.Position, p),
                            range = V2.Distance(p, c.Position);
                        if (
                            d > 7
                            || !b.Map.Walkable(p)
                            || !CommandHierarchy.WithinManeuver(b, s, p)
                            || PositionLogic.Occupied(b, s, p)
                            || range > b.WeaponRange(s) - .5f
                            || !b.Map.LineOfSight(p, c.Position)
                            || b.SmokeTransmission(p, c.Position) < .8f
                            || !SafePoint(b, s, p, known)
                        )
                            continue;
                        float cover = CombatTraining.Cover(b, s, p, c.Position);
                        bool open = cover < .1f;
                        // Last resort: fire crouched from the current spot, never march into open ground for the bonus.
                        if (open && (!b.OpeningAllowOpenGround || d > .1f))
                            continue;
                        bool stable = true;
                        foreach (
                            var offset in new[] { new V2(.18f, 0), new V2(-.18f, 0), new V2(0, .18f), new V2(0, -.18f) }
                        )
                        {
                            var test = p + offset;
                            if (
                                !open && CombatTraining.Cover(b, s, test, c.Position) < .1f
                                || !b.Map.LineOfSight(test, c.Position)
                            )
                                stable = false;
                        }

                        if (
                            !stable
                            || Ballistics
                                .Geometry(b.Map, p, Ballistics.MuzzleHeight, (c.Position - p).Normalized, 0, .32f)
                                .Kind != BulletStop.None
                        )
                            continue;
                        bool crowded = false;
                        foreach (var t in st.Tasks)
                        {
                            if (V2.Distance(p, t.Goal) < 1.8f)
                                crowded = true;
                            if (b.OpeningHorizonEnabled)
                            {
                                var other = Known(b, s, t.Target);
                                if (
                                    V2.SegmentDistance(t.Goal, p, c.Position) < .65f
                                    || other != null && V2.SegmentDistance(p, t.Goal, other.Position) < .65f
                                )
                                    crowded = true;
                            }
                        }

                        if (crowded)
                            continue;
                        float progress = V2.Distance(s.Position, c.Position) - range;
                        float angleGain = 0;
                        foreach (var task in st.Tasks)
                            angleGain = Math.Max(
                                angleGain,
                                DirectionalVision.Angle(task.Goal - c.Position, p - c.Position)
                            );
                        float score =
                            cover * 10
                            - d * .6f
                            - PositionLogic.Exposure(b, s, p) * 3
                            - b.Map.Protection(c.Position, p) * 14
                            + progress * .9f
                            + angleGain * .04f
                            + (assigned.Contains(c.EnemyId) ? 0 : 3)
                            + c.Confidence * 2;
                        V2 next = p;
                        int exits = 0;
                        if (b.OpeningHorizonEnabled)
                        {
                            score += OpeningHorizon.Evaluate(b, s, p, known, out next, out exits);
                            st.Stats.HorizonCandidates++;
                        }

                        candidates.Add(
                            new Candidate
                            {
                                OpenGround = open,
                                Target = c.EnemyId,
                                Point = p,
                                Score = score,
                                Next = next,
                                Exits = exits,
                            }
                        );
                    }
                }

                candidates.Sort(
                    (a, z) => a.OpenGround != z.OpenGround ? (a.OpenGround ? 1 : -1) : z.Score.CompareTo(a.Score)
                );
                Candidate chosen = null;
                int checkedPaths = 0;
                foreach (var c in candidates)
                {
                    if (!c.OpenGround && checkedPaths++ >= 20)
                        continue;
                    var path =
                        V2.Distance(s.Position, c.Point) < .5f
                            ? new List<V2>()
                            : LossMemory.Find(b, s, c.Point, p => PositionLogic.Exposure(b, s, p) * 8);
                    if (
                        V2.Distance(s.Position, c.Point) >= .5f && path.Count == 0
                        || SupportAI.PathLength(s.Position, path) > 10
                    )
                        continue;
                    if (path.Count > 0 && V2.Distance(path[path.Count - 1], c.Point) > .05f)
                        path.Add(c.Point);
                    bool safe = true;
                    V2 previous = s.Position;
                    foreach (var p in path)
                    {
                        int n = Math.Max(1, (int)Math.Ceiling(V2.Distance(previous, p) * 2));
                        for (int j = 1; j <= n; j++)
                            if (
                                !b.Map.Walkable(previous + (p - previous) * (j / (float)n))
                                || !SafePoint(b, s, previous + (p - previous) * (j / (float)n), known)
                            )
                                safe = false;
                        previous = p;
                    }

                    if (!safe)
                        continue;
                    c.Path = path;
                    chosen = c;
                    break;
                }

                if (chosen == null)
                {
                    st.Tasks.Clear();
                    Reject(b, st, "no_joint_cover_plan");
                    return;
                }

                st.Tasks.Add(
                    new Task
                    {
                        OpenGround = chosen.OpenGround,
                        Unit = s.Id,
                        Target = chosen.Target,
                        Goal = chosen.Point,
                        Route = chosen.Path,
                        Next = chosen.Next,
                        Progress = s.Position,
                        ProgressAt = b.Time,
                    }
                );
                assigned.Add(chosen.Target);
                st.Stats.HorizonExits += chosen.Exits;
            }

            st.Leader = leader;
            st.Revision = b.Green.Revision;
            st.Preparing = true;
            st.Started = b.Time;
            st.Until = b.Time + 8;
            st.Stats.Plans++;
            st.Stats.Assigned += st.Tasks.Count;
            b.Say(b.Soldiers[leader], "Цели распределены. Занять позиции. Без огня!", true);
            foreach (var t in st.Tasks)
            {
                b.History.Event(
                    b,
                    "openingAssign",
                    t.Unit,
                    t.Target,
                    b.Soldiers[t.Unit].Position,
                    t.Goal,
                    0,
                    "commander_cover_plan"
                );
                if (b.OpeningHorizonEnabled)
                    b.History.Event(
                        b,
                        "openingNextPosition",
                        t.Unit,
                        t.Target,
                        t.Goal,
                        t.Next,
                        0,
                        "known_geometry_lookahead"
                    );
            }
        }

        static void Reject(BattleSimulation b, State st, string why)
        {
            st.Stats.Rejected++;
            st.Stats.EndReason = why;
            ReconCombatPhase.Enter(b, "opening_rejected:" + why);
        }

        static void Finish(BattleSimulation b, State st, string reason, bool release)
        {
            foreach (var task in st.Tasks)
                if (!task.Ready)
                    b.History.Event(
                        b,
                        "openingBlocked",
                        task.Unit,
                        task.Target,
                        b.Soldiers[task.Unit].Position,
                        task.Goal,
                        0,
                        task.Block
                    );
            st.Preparing = false;
            st.Released = release;
            st.Stats.PreparationSeconds += b.Time - st.Started;
            st.Stats.EndReason = reason;
            foreach (var t in st.Tasks)
            {
                var s = b.Soldiers[t.Unit];
                if (object.ReferenceEquals(s.Path, t.Published))
                    TacticalAI.Stop(s);
                s.NextDecision = b.Time;
            }

            b.Green.NextPlan = b.Time;
            if (release)
            {
                st.ReleaseAt = b.Time;
                st.Until = b.Time + 6;
                foreach (var unit in b.Soldiers)
                    if (unit.Side == Side.Green)
                        st.DamageAtRelease += unit.DamageDealt;
                foreach (var task in st.Tasks)
                    b.Soldiers[task.Unit].Crouch.NextSit = b.Time + 1;
                st.Stats.Commands++;
                ReconCombatPhase.Enter(b);
                b.Say(b.Soldiers[st.Leader], "РАБОТАЕМ!", true);
            }
            else
            {
                st.Stats.Aborted++;
                ReconCombatPhase.Enter(b, "opening_aborted:" + reason);
            }

            b.History.Event(
                b,
                release ? "openingCommand" : "openingAbort",
                st.Leader,
                -1,
                b.Soldiers[st.Leader].Position,
                b.Soldiers[st.Leader].Position,
                st.Stats.Ready,
                reason
            );
        }

        public static void Update(BattleSimulation b)
        {
            var st = Data.GetOrCreateValue(b);
            if (st.Released && !st.DamageMeasured)
            {
                float damage = 0;
                foreach (var unit in b.Soldiers)
                    if (unit.Side == Side.Green)
                        damage += unit.DamageDealt;
                st.Stats.DamageFirst10Seconds = damage - st.DamageAtRelease;
                if (b.Time - st.ReleaseAt >= 10)
                    st.DamageMeasured = true;
            }

            if (!st.Preparing)
                return;
            if (!b.ReconOpeningEnabled || b.Green.Revision != st.Revision || b.Green.LeaderId != st.Leader)
            {
                Finish(b, st, "order_or_commander_changed", false);
                return;
            }

            foreach (var t in st.Tasks)
            {
                var s = b.Soldiers[t.Unit];
                if (
                    !Free(b, s)
                    || s.Suppression > .25f
                    || b.Time - s.LastIncoming < 2
                    || b.Time - s.LastBlast < 2
                    || s.LastFire >= st.Started
                )
                {
                    Finish(
                        b,
                        st,
                        "emergency:"
                            + s.Id
                            + ":"
                            + s.Job
                            + ":"
                            + s.LastIncoming
                            + ":"
                            + s.LastFire
                            + ":"
                            + s.Suppression
                            + ":"
                            + Free(b, s),
                        false
                    );
                    return;
                }
            }

            if (b.Time >= st.Until)
            {
                Finish(b, st, "preparation_timeout", false);
                return;
            }

            int ready = 0;
            foreach (var t in st.Tasks)
            {
                var s = b.Soldiers[t.Unit];
                var c = Known(b, s, t.Target);
                if (c == null)
                {
                    Finish(b, st, "target_lost", false);
                    return;
                }

                if (object.ReferenceEquals(s.Path, t.Published) && s.Path.Count > 0)
                    t.Cursor = s.PathCursor;
                if (V2.Distance(s.Position, t.Progress) > .2f)
                {
                    t.Progress = s.Position;
                    t.ProgressAt = b.Time;
                }

                bool at = V2.Distance(s.Position, t.Goal) < .15f;
                if (!at && b.Time - t.ProgressAt > 3)
                {
                    Finish(b, st, "route_stalled", false);
                    return;
                }

                if (at)
                {
                    if (t.OpenGround)
                    {
                        s.Crouch.Seated = true;
                        s.Crouched = true;
                    }
                    else
                    {
                        CombatCrouch.Rise(b, s);
                        s.Crouch.NextSit = b.Time + .5f;
                    }
                }

                bool clear =
                    at
                    && s.Visible.Contains(t.Target)
                    && c.Visual
                    && !c.Inferred
                    && b.Time - c.SeenAt <= b.Config.PerceptionPeriod * 2
                    && s.Ammo > 0
                    && s.ReloadUntil <= b.Time
                    && s.HandsBusyUntil <= b.Time
                    && V2.Distance(s.Position, c.Position) <= b.WeaponRange(s)
                    && b.Map.LineOfSight(s.Position, c.Position)
                    && b.SmokeTransmission(s.Position, c.Position) > .5f
                    && !b.FriendlyInLane(s, c.Position)
                    && (CombatTraining.Cover(b, s, s.Position, c.Position) > .1f || t.OpenGround && s.Crouched);
                if (clear)
                {
                    if (!t.OpenGround)
                        CombatCrouch.Rise(b, s);
                    V2 muzzle;
                    clear =
                        CombatCrouch.FireAllowed(b, s, c.Position)
                        && Ballistics.TryMuzzle(b, s, c.Position, out muzzle)
                        && ReactionTiming.WeaponReady(b, s, c)
                        && DirectionalVision.ReadyToFire(b, s, c);
                }

                t.Block =
                    "at="
                    + at
                    + ",visible="
                    + s.Visible.Contains(t.Target)
                    + ",cover="
                    + CombatTraining.Cover(b, s, s.Position, c.Position)
                    + ",los="
                    + b.Map.LineOfSight(s.Position, c.Position)
                    + ",lane="
                    + b.FriendlyInLane(s, c.Position)
                    + ",crouch="
                    + s.Crouched;
                V2 diagnosticMuzzle;
                t.Block += ",muzzle=" + Ballistics.TryMuzzle(b, s, c.Position, out diagnosticMuzzle);
                t.Ready = clear;
                if (clear)
                    ready++;
            }

            st.Stats.Ready = ready;
            if (ready == st.Tasks.Count)
                Finish(b, st, "all_ready", true);
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!Free(b, s))
                return false;
            var st = Data.GetOrCreateValue(b);
            var t = Find(st, s);
            if (t == null)
                return false;
            if (!Preparing(b))
            {
                if (ReconCombatPhase.Active(b))
                    return false;
                if (b.OpeningHorizonEnabled && ActiveFire(b, s) && b.Time - st.ReleaseAt > 2)
                {
                    var current = Known(b, s, t.Target);
                    if (
                        current == null
                        || !s.Visible.Contains(t.Target)
                        || CombatTraining.Cover(b, s, s.Position, current.Position) < .1f
                    )
                    {
                        s.NextDecision = b.Time;
                        s.Reason = "Пересмотр продолжения боя";
                        return false;
                    }
                }

                if (
                    ActiveFire(b, s)
                    && s.Suppression < .65f
                    && Known(b, s, t.Target) != null
                    && s.Visible.Contains(t.Target)
                    && CombatTraining.Cover(b, s, s.Position, Known(b, s, t.Target).Position) > .1f
                )
                {
                    TacticalAI.Stop(s);
                    s.MoveAuthorized = false;
                    s.Job = Job.Cover;
                    s.Reason = "Работаем по назначенной цели";
                    return true;
                }

                return false;
            }

            if (V2.Distance(s.Position, t.Goal) < .15f)
            {
                TacticalAI.Stop(s);
                s.MoveAuthorized = false;
                s.Job = Job.Cover;
                s.Reason = t.Ready ? "Готов. Жду команды «Работаем»" : "На позиции. Проверяю назначенную цель";
            }
            else
            {
                if (!object.ReferenceEquals(s.Path, t.Published) || s.Path.Count == 0)
                {
                    t.Published = new List<V2>(t.Route);
                    s.Path = t.Published;
                }

                s.PathCursor = t.Cursor;
                s.Destination = s.OrderAnchor = t.Goal;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                CommandHierarchy.TransferAssignment(b, s, "Передано занятию разведывательной позиции");
                s.Job = Job.Reposition;
                s.Reason = "Занимаю укрытие для назначенной цели";
            }

            return true;
        }

        public static void Enforce(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
                if (Preparing(b) || ActiveFire(b, s))
                    Decide(b, s);
        }

        public static bool Owns(BattleSimulation b, Soldier s) =>
            Preparing(b) && Find(Data.GetOrCreateValue(b), s) != null;

        public static bool HoldFire(BattleSimulation b, Soldier s)
        {
            if (!Owns(b, s))
                return false;
            Update(b);
            return Preparing(b);
        }

        public static bool Watch(BattleSimulation b, Soldier s, out V2 direction)
        {
            direction = new V2();
            var st = Data.GetOrCreateValue(b);
            if (!st.Preparing && (!st.Released || b.Time > st.Until))
                return false;
            var t = Find(st, s);
            if (t == null)
                return false;
            var c = Known(b, s, t.Target);
            if (c == null)
                return false;
            direction = c.Position - s.Position;
            return true;
        }

        public static float Priority(BattleSimulation b, Soldier s, int target)
        {
            var st = Data.GetOrCreateValue(b);
            if (!st.Preparing && (!st.Released || b.Time > st.Until))
                return 0;
            var t = Find(st, s);
            return t != null && t.Target == target ? 30 : 0;
        }

        public static bool ActiveFire(BattleSimulation b, Soldier s)
        {
            var st = Data.GetOrCreateValue(b);
            return st.Released && b.Time <= st.Until && Find(st, s) != null;
        }

        public static void Fired(BattleSimulation b, Soldier s, Contact c)
        {
            var st = Data.GetOrCreateValue(b);
            if (!ActiveFire(b, s))
                return;
            var t = Find(st, s);
            st.Stats.Shots++;
            if (!t.Fired)
            {
                t.Fired = true;
                st.Stats.ParticipantsFired++;
                if (st.First < 0)
                {
                    st.First = b.Time;
                    st.Stats.FirstShotDelay = b.Time - st.ReleaseAt;
                }

                st.Last = b.Time;
                if (st.Stats.ParticipantsFired == st.Tasks.Count)
                    st.Stats.ShotSpread = st.Last - st.First;
                b.History.Event(
                    b,
                    "openingFirstShot",
                    s.Id,
                    c.EnemyId,
                    s.Position,
                    c.Position,
                    b.Time - st.ReleaseAt,
                    c.EnemyId == t.Target ? "assigned" : "other"
                );
            }
        }

        // Up to three actual emitted rounds in the first assigned burst. No rearming.
        public static bool BurstEligible(BattleSimulation b, Soldier s, Contact c)
        {
            if (s.SomaliFiring || !ActiveFire(b, s) || c == null)
                return false;
            var t = Find(Data.GetOrCreateValue(b), s);
            return t != null
                && !t.CritSpent
                && t.CritRemaining > 0
                && c.EnemyId == t.Target
                && c.Visual
                && !c.Inferred
                && s.Visible.Contains(c.EnemyId)
                && s.ReloadUntil <= b.Time
                && (b.OpeningFixedAccuracyEnabled || s.Suppression <= .65f)
                && (t.CritLast < 0 || b.Time - t.CritLast <= Math.Max(2, WeaponBalance.For(s).FireSeconds * 2.5f));
        }

        public static bool CriticalEligible(BattleSimulation b, Soldier s, Contact c)
        {
            if (
                (
                    !b.OpeningFixedAccuracyEnabled
                    && !b.OpeningCriticalEnabled
                    && !b.OpeningIgnoreCoverEnabled
                    && !b.OpeningDoubleAccuracyEnabled
                )
                || (
                    s.GunSniper
                    && !b.OpeningFixedAccuracyEnabled
                    && !b.OpeningIgnoreCoverEnabled
                    && !b.OpeningDoubleAccuracyEnabled
                )
                || s.SomaliFiring
                || !ActiveFire(b, s)
                || c == null
            )
                return false;
            var st = Data.GetOrCreateValue(b);
            var t = Find(st, s);
            if (t == null || t.CritSpent || t.CritRemaining <= 0)
                return false;
            if (
                c.EnemyId != t.Target
                || !c.Visual
                || c.Inferred
                || !s.Visible.Contains(c.EnemyId)
                || s.ReloadUntil > b.Time
                || (!b.OpeningFixedAccuracyEnabled && s.Suppression > .65f)
                || t.CritLast >= 0 && b.Time - t.CritLast > Math.Max(2, WeaponBalance.For(s).FireSeconds * 2.5f)
            )
            {
                t.CritSpent = true;
                return false;
            }

            t.CritRemaining--;
            t.CritLast = b.Time;
            bool crit = b.OpeningCriticalEnabled && !s.GunSniper;
            if (crit)
                st.Stats.CritEligible++;
            return crit;
        }

        public static void RecordCritical(BattleSimulation b, bool rolled)
        {
            if (rolled)
                Data.GetOrCreateValue(b).Stats.CritRolled++;
        }

        public static void RecordCriticalHit(BattleSimulation b)
        {
            Data.GetOrCreateValue(b).Stats.CritHits++;
        }
    }
}
