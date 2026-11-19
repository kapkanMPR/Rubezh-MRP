using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    public sealed class ReconStats
    {
        public int Starts,
            Routes,
            Arrivals,
            HeldShots,
            NewContacts,
            ObservedCells,
            Replans,
            NoGain,
            WatchChanges,
            InformedQueries;
        public string EndReason = "";
        public float Seconds;
    }

    public static class QuietRecon
    {
        sealed class State
        {
            public bool Used,
                Active;
            public float Until,
                Started,
                ProgressAt,
                NextSense,
                ArrivedAt = -1;
            public int Revision,
                Scout = -1,
                Cursor;
            public V2 Goal,
                Progress,
                Watch;
            public List<V2> Route,
                Published;
            public Contact Contact;
            public readonly HashSet<int> Known = new HashSet<int>(),
                Seen = new HashSet<int>();
            public readonly List<V2> Probes = new List<V2>();
            public readonly ReconStats Stats = new ReconStats();
        }

        static readonly ConditionalWeakTable<BattleSimulation, State> Data =
            new ConditionalWeakTable<BattleSimulation, State>();

        public static ReconStats Stats(BattleSimulation b) =>
            !b.UsefulReconEnabled ? LegacyQuietRecon.Stats(b) : Data.GetOrCreateValue(b).Stats;

        public static bool Active(BattleSimulation b) =>
            !ReconCombatPhase.Active(b)
            && (
                !b.UsefulReconEnabled
                    ? LegacyQuietRecon.Active(b)
                    : b.QuietReconEnabled && !b.Historical01225 && Data.GetOrCreateValue(b).Active
            );

        public static bool Owns(BattleSimulation b, Soldier s) =>
            !b.UsefulReconEnabled
                ? LegacyQuietRecon.Owns(b, s)
                : s.Side == Side.Green
                    && Active(b)
                    && Data.GetOrCreateValue(b).Scout == s.Id
                    && Data.GetOrCreateValue(b).Route != null;

        static bool Eligible(BattleSimulation b, Soldier s) =>
            s.Side == Side.Green && s.Deployed && s.Available && !s.Broken && !s.Bleeding && TacticalMarch.Free(b, s);

        public static bool SeemsUnaware(BattleSimulation b, Soldier s, Contact c)
        {
            if (!b.UsefulReconEnabled)
                return LegacyQuietRecon.SeemsUnaware(b, s, c);
            if (!c.Visual || c.Inferred || b.Time - c.SeenAt > 1 || b.Time - c.FireSeenAt < 5)
                return false;
            float distance = V2.Distance(s.Position, c.Position);
            if (distance < 7)
                return false;
            float facing =
                c.ObservedFacing.Length < .1f
                    ? 1
                    : V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized);
            return facing < .25f || distance >= b.ReconStandOff;
        }

        public static float UncertaintyCost(BattleSimulation b, V2 p)
        {
            if (!b.UsefulReconEnabled || !b.QuietReconEnabled)
                return 0;
            var st = Data.GetOrCreateValue(b);
            if (!st.Used || st.Active || b.Time - st.Started > 30)
                return 0;
            st.Stats.InformedQueries++;
            return st.Seen.Contains(Key(b, p)) ? 0 : .4f;
        }

        static bool Safe(BattleSimulation b)
        {
            int n = 0;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Alive && s.Deployed)
                {
                    n++;
                    if (
                        !Eligible(b, s)
                        || s.LastFire >= 0
                        || b.Time - s.LastIncoming < 5
                        || b.Time - s.LastBlast < 5
                        || s.Suppression > .15f
                    )
                        return false;
                    foreach (var c in Knowledge.Contacts(b, s))
                        if (c.Visual && !c.Inferred && b.Time - c.SeenAt <= 1 && !SeemsUnaware(b, s, c))
                            return false;
                }

            return n >= 2;
        }

        static int Key(BattleSimulation b, V2 p) => (int)p.Y * b.Map.Columns + (int)p.X;

        static bool Visible(BattleSimulation b, Soldier s, V2 p) =>
            DirectionalVision.InView(b, s, p)
            && b.Map.LineOfSight(s.Position, p)
            && b.SmokeTransmission(s.Position, p) > .8f;

        static void Observe(BattleSimulation b, State st)
        {
            bool sample = b.Time >= st.NextSense;
            if (sample)
                st.NextSense = b.Time + .4f;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Available && s.Deployed)
                {
                    foreach (var c in s.Contacts.Values)
                        if (
                            c.Visual
                            && !c.Inferred
                            && !c.Reported
                            && b.Time - c.SeenAt <= 1
                            && st.Known.Add(c.EnemyId)
                            && st.Active
                        )
                        {
                            st.Stats.NewContacts++;
                            st.Contact = c.Copy();
                            b.Say(s, "Ещё контакт.", true);
                            b.History.Event(
                                b,
                                "reconContact",
                                s.Id,
                                c.EnemyId,
                                s.Position,
                                c.Position,
                                0,
                                "new_direct_contact"
                            );
                        }

                    if (!sample)
                        continue;
                    int r = (int)Math.Ceiling(DirectionalVision.FrontRange(b, s));
                    for (
                        int y = Math.Max(0, (int)s.Position.Y - r);
                        y <= Math.Min(b.Map.Rows - 1, (int)s.Position.Y + r);
                        y++
                    )
                    for (
                        int x = Math.Max(0, (int)s.Position.X - r);
                        x <= Math.Min(b.Map.Columns - 1, (int)s.Position.X + r);
                        x++
                    )
                    {
                        int k = y * b.Map.Columns + x;
                        if (st.Seen.Contains(k))
                            continue;
                        var p = new V2(x + .5f, y + .5f);
                        if (!b.Map.Walkable(p) || !Visible(b, s, p))
                            continue;
                        st.Seen.Add(k);
                        if (st.Active && st.Contact != null && V2.Distance(p, st.Contact.Position) <= 9)
                            st.Stats.ObservedCells++;
                    }
                }
        }

        static void End(BattleSimulation b, State st, string why)
        {
            st.Active = false;
            st.Stats.EndReason = why;
            st.Stats.Seconds = b.Time - st.Started;
            if (st.Scout >= 0)
            {
                var s = b.Soldiers[st.Scout];
                if (object.ReferenceEquals(s.Path, st.Published))
                    TacticalAI.Stop(s);
            }

            st.Route = null;
            if (st.Scout >= 0 && why == "survey_complete")
                b.Say(b.Soldiers[st.Scout], "Сектор осмотрен. Двигаемся.", true);
            b.Green.NextPlan = b.Time;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green)
                    s.NextDecision = b.Time;
            b.History.Event(b, "reconEnd", st.Scout, -1, st.Goal, st.Goal, st.Stats.ObservedCells, why);
            if (why == "survey_complete" || why == "no_useful_observation" || why == "observation_budget")
                ReconOpening.Begin(b);
            else
                ReconCombatPhase.Enter(b, "recon_ended:" + why);
        }

        public static void Update(BattleSimulation b)
        {
            if (ReconCombatPhase.Active(b))
                return;
            if (!b.UsefulReconEnabled)
            {
                LegacyQuietRecon.Update(b);
                return;
            }

            if (!b.QuietReconEnabled || b.Historical01225 || !b.PairExperimentMarch)
                return;
            var st = Data.GetOrCreateValue(b);
            if (st.Used && !st.Active)
                return;
            Observe(b, st);
            if (st.Active)
            {
                if (!Safe(b) || b.Green.Revision != st.Revision)
                {
                    End(b, st, "reaction_or_emergency");
                    return;
                }

                if (b.Time >= st.Until)
                {
                    End(b, st, "observation_budget");
                    return;
                }

                if (st.Route != null)
                {
                    var s = b.Soldiers[st.Scout];
                    if (object.ReferenceEquals(s.Path, st.Published) && s.Path.Count > 0)
                        st.Cursor = s.PathCursor;
                    if (V2.Distance(s.Position, st.Goal) < .6f)
                    {
                        st.Stats.Arrivals++;
                        st.Route = null;
                        st.ArrivedAt = b.Time;
                        TacticalAI.Stop(s);
                    }
                    else if (V2.Distance(s.Position, st.Progress) > .2f)
                    {
                        st.Progress = s.Position;
                        st.ProgressAt = b.Time;
                    }
                    else if (b.Time - st.ProgressAt > 3)
                    {
                        End(b, st, "route_stalled");
                        return;
                    }
                }

                if (st.Route == null)
                {
                    var scout = b.Soldiers[st.Scout];
                    bool pending = ChooseWatch(b, st, scout);
                    if (!pending || b.Time - st.ArrivedAt >= 2.5f)
                    {
                        if (st.Stats.Routes >= 1)
                        {
                            End(b, st, "survey_complete");
                        }
                        else if (!Plan(b, st))
                        {
                            st.Stats.NoGain++;
                            End(b, st, "no_useful_observation");
                        }
                        else
                            st.Stats.Replans++;
                    }
                }

                return;
            }

            if (!Safe(b))
            {
                // A real exchange can begin before the first recon task. It is one-way too.
                foreach (var unit in b.Soldiers)
                    if (
                        unit.Side == Side.Green
                        && unit.Deployed
                        && (unit.LastFire >= 0 || b.Time - unit.LastIncoming < 2 || b.Time - unit.LastBlast < 2)
                    )
                    {
                        ReconCombatPhase.Enter(b, "contact_before_recon");
                        break;
                    }

                return;
            }

            foreach (var s in b.Soldiers)
                if (Eligible(b, s))
                    foreach (var c in s.Contacts.Values)
                        if (!c.Reported && s.Visible.Contains(c.EnemyId) && SeemsUnaware(b, s, c))
                        {
                            st.Used = st.Active = true;
                            st.Started = b.Time;
                            st.Until = b.Time + b.ReconBudget;
                            st.Revision = b.Green.Revision;
                            st.Stats.Starts++;
                            st.Contact = c.Copy();
                            foreach (var unit in b.Soldiers)
                                if (unit.Side == Side.Green && unit.Available && unit.Deployed)
                                    foreach (var known in unit.Contacts.Values)
                                        if (known.Visual && !known.Inferred)
                                            st.Known.Add(known.EnemyId);
                            b.History.Event(
                                b,
                                "reconStart",
                                s.Id,
                                c.EnemyId,
                                s.Position,
                                c.Position,
                                0,
                                "suspected_unnoticed"
                            );
                            if (!Plan(b, st))
                            {
                                st.Stats.NoGain++;
                                End(b, st, "no_useful_observation");
                            }
                            else
                                b.Say(s, "Наблюдаю. Без огня.", true);
                            return;
                        }
        }

        static bool SafePoint(BattleSimulation b, Soldier s, V2 p, State st)
        {
            var contacts = new List<Contact> { st.Contact };
            foreach (var c in Knowledge.Contacts(b, s))
                contacts.Add(c);
            foreach (var c in contacts)
                if (c != null && c.Visual && !c.Inferred && b.Time - c.SeenAt < 5)
                {
                    float d = V2.Distance(p, c.Position);
                    if (d < Math.Min(8, V2.Distance(s.Position, c.Position) - .25f))
                        return false;
                    if (
                        b.Map.LineOfSight(p, c.Position)
                        && c.ObservedFacing.Length > .1f
                        && V2.Dot(c.ObservedFacing.Normalized, (p - c.Position).Normalized) > .25f
                        && d < b.ReconStandOff + .2f
                    )
                        return false;
                }

            return true;
        }

        sealed class Candidate
        {
            public Soldier Unit;
            public V2 Point;
            public float Score;
            public List<V2> Probes;
        }

        static bool Plan(BattleSimulation b, State st)
        {
            var candidates = new List<Candidate>();
            var probes = new List<V2>();
            var c = st.Contact;
            for (
                int y = Math.Max(0, (int)c.Position.Y - 9);
                y <= Math.Min(b.Map.Rows - 1, (int)c.Position.Y + 9);
                y += 2
            )
            for (
                int x = Math.Max(0, (int)c.Position.X - 9);
                x <= Math.Min(b.Map.Columns - 1, (int)c.Position.X + 9);
                x += 2
            )
            {
                var p = new V2(x + .5f, y + .5f);
                if (b.Map.Walkable(p) && !st.Seen.Contains(Key(b, p)) && V2.Distance(p, c.Position) <= 9)
                    probes.Add(p);
            }

            foreach (var s in b.Soldiers)
                if (Eligible(b, s))
                {
                    for (int y = -7; y <= 7; y += 2)
                    for (int x = -7; x <= 7; x += 2)
                    {
                        var p = new V2(
                            (float)Math.Floor(s.Position.X) + x + .5f,
                            (float)Math.Floor(s.Position.Y) + y + .5f
                        );
                        float d = V2.Distance(s.Position, p);
                        if (
                            d < 1
                            || d > 8
                            || !b.Map.Walkable(p)
                            || !CommandHierarchy.WithinManeuver(b, s, p)
                            || PositionLogic.Occupied(b, s, p)
                            || !SafePoint(b, s, p, st)
                        )
                            continue;
                        var visible = new List<V2>();
                        foreach (var probe in probes)
                            if (
                                V2.Distance(p, probe) < DirectionalVision.FrontRange(b, s) - .5f
                                && b.Map.LineOfSight(p, probe)
                                && b.SmokeTransmission(p, probe) > .8f
                            )
                                visible.Add(probe);
                        // A rotation alone does not justify a move; reserve translation for blocked/out-of-range sightlines.
                        int gain = 0;
                        foreach (var probe in visible)
                            if (
                                V2.Distance(s.Position, probe) > DirectionalVision.FrontRange(b, s) - .5f
                                || !b.Map.LineOfSight(s.Position, probe)
                            )
                                gain += b.Map.NearCover(probe) ? 4 : 1;
                        if (gain < 2)
                            continue;
                        float score =
                            gain * 2
                            - d * .7f
                            - PositionLogic.Exposure(b, s, p) * 5
                            + b.Map.Protection(p, c.Position) * 5;
                        candidates.Add(
                            new Candidate
                            {
                                Unit = s,
                                Point = p,
                                Score = score,
                                Probes = visible,
                            }
                        );
                    }
                }

            candidates.Sort((a, z) => z.Score.CompareTo(a.Score));
            int checkedPaths = 0;
            foreach (var candidate in candidates)
            {
                if (checkedPaths++ >= 16)
                    break;
                var s = candidate.Unit;
                var path = LossMemory.Find(b, s, candidate.Point, v => PositionLogic.Exposure(b, s, v) * 8);
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 12)
                    continue;
                bool safe = true;
                V2 prev = s.Position;
                foreach (var v in path)
                {
                    int n = Math.Max(1, (int)Math.Ceiling(V2.Distance(prev, v) * 2));
                    for (int j = 1; j <= n; j++)
                        if (!SafePoint(b, s, prev + (v - prev) * (j / (float)n), st))
                            safe = false;
                    prev = v;
                }

                if (!safe)
                    continue;
                st.Route = path;
                st.Published = null;
                st.Cursor = 0;
                st.Scout = s.Id;
                st.Goal = candidate.Point;
                st.Progress = s.Position;
                st.ProgressAt = b.Time;
                st.ArrivedAt = -1;
                st.Probes.Clear();
                st.Probes.AddRange(candidate.Probes);
                st.Stats.Routes++;
                ChooseWatch(b, st, s);
                b.History.Event(
                    b,
                    "reconRoute",
                    s.Id,
                    c.EnemyId,
                    s.Position,
                    st.Goal,
                    candidate.Probes.Count,
                    "unobserved_sightlines"
                );
                return true;
            }

            return false;
        }

        static bool ChooseWatch(BattleSimulation b, State st, Soldier s)
        {
            float best = float.MaxValue;
            V2 chosen = new V2();
            bool found = false;
            foreach (var p in st.Probes)
                if (
                    !st.Seen.Contains(Key(b, p))
                    && b.Map.LineOfSight(s.Position, p)
                    && V2.Distance(s.Position, p) <= DirectionalVision.FrontRange(b, s)
                )
                {
                    float cost = DirectionalVision.Angle(s.Awareness.Look, p - s.Position);
                    if (cost < best)
                    {
                        best = cost;
                        chosen = p;
                        found = true;
                    }
                }

            if (found)
            {
                if (V2.Distance(st.Watch, chosen) > .5f)
                    st.Stats.WatchChanges++;
                st.Watch = chosen;
            }

            return found;
        }

        public static bool Watch(BattleSimulation b, Soldier s, out V2 direction)
        {
            direction = new V2();
            if (!b.UsefulReconEnabled || !Active(b) || !Eligible(b, s))
                return false;
            var st = Data.GetOrCreateValue(b);
            if (st.Scout == s.Id && ChooseWatch(b, st, s))
                direction = st.Watch - s.Position;
            else if (st.Contact != null)
                direction = st.Contact.Position - s.Position;
            return direction.Length > .1f;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!b.UsefulReconEnabled)
                return LegacyQuietRecon.Decide(b, s);
            if (!Active(b) || !Eligible(b, s))
                return false;
            var st = Data.GetOrCreateValue(b);
            if (!Safe(b))
            {
                End(b, st, "reaction_or_emergency");
                return false;
            }

            if (s.Id == st.Scout && st.Route != null)
            {
                if (!object.ReferenceEquals(s.Path, st.Published) || s.Path.Count == 0)
                {
                    st.Published = new List<V2>(st.Route);
                    s.Path = st.Published;
                }

                s.PathCursor = st.Cursor;
                s.Destination = s.OrderAnchor = st.Goal;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                CommandHierarchy.TransferAssignment(b, s, "Передано разведке");
                s.Job = Job.Reposition;
                s.Reason = "Разведка: открываю непроверенный сектор";
            }
            else
            {
                TacticalAI.Stop(s);
                s.MoveAuthorized = false;
                s.Job = Job.Cover;
                s.Reason =
                    s.Id == st.Scout
                        ? "Разведка: осматриваю новый сектор"
                        : "Контролирую известный контакт, жду доклада";
            }

            return true;
        }

        public static void Enforce(BattleSimulation b)
        {
            if (!b.UsefulReconEnabled)
            {
                LegacyQuietRecon.Enforce(b);
                return;
            }

            if (Active(b))
                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Green)
                        Decide(b, s);
        }

        public static bool HoldFire(BattleSimulation b, Soldier s)
        {
            if (!b.UsefulReconEnabled)
                return LegacyQuietRecon.HoldFire(b, s);
            if (!Active(b) || s.Side != Side.Green)
                return false;
            var st = Data.GetOrCreateValue(b);
            if (!Safe(b))
            {
                End(b, st, "reaction_or_emergency");
                return false;
            }

            st.Stats.HeldShots++;
            return true;
        }
    }
}
