using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    public static class LegacyQuietRecon
    {
        sealed class State
        {
            public bool Used,
                Active;
            public float Until,
                Started,
                ProgressAt;
            public int Revision,
                Scout = -1,
                Cursor;
            public V2 Goal,
                Progress;
            public List<V2> Route;
            public readonly HashSet<int> Known = new HashSet<int>();
            public readonly ReconStats Stats = new ReconStats();
        }

        static readonly ConditionalWeakTable<BattleSimulation, State> Data =
            new ConditionalWeakTable<BattleSimulation, State>();

        public static bool Owns(BattleSimulation b, Soldier s) =>
            s.Side == Side.Green
            && Active(b)
            && Data.GetOrCreateValue(b).Scout == s.Id
            && Data.GetOrCreateValue(b).Route != null;

        public static ReconStats Stats(BattleSimulation b) => Data.GetOrCreateValue(b).Stats;

        public static bool Active(BattleSimulation b) =>
            b.QuietReconEnabled && !b.Historical01225 && Data.GetOrCreateValue(b).Active;

        static bool Eligible(BattleSimulation b, Soldier s) =>
            s.Side == Side.Green && s.Deployed && s.Available && !s.Broken && !s.Bleeding && TacticalMarch.Free(b, s);

        // Belief only: observed direction/fire, never enemy Visible/Contacts/rest state.
        public static bool SeemsUnaware(BattleSimulation b, Soldier s, Contact c)
        {
            if (!c.Visual || c.Inferred || b.Time - c.SeenAt > 1 || b.Time - c.FireSeenAt < 5)
                return false;
            float d = V2.Distance(s.Position, c.Position);
            if (d < 7)
                return false;
            float facing =
                c.ObservedFacing.Length < .1f
                    ? 1
                    : V2.Dot(c.ObservedFacing.Normalized, (s.Position - c.Position).Normalized);
            return facing < .25f || d > DirectionalVision.FrontRange(b, s) * .9f;
        }

        static bool Safe(BattleSimulation b, State st)
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

        static void End(BattleSimulation b, State st, string why)
        {
            st.Active = false;
            st.Stats.EndReason = why;
            st.Stats.Seconds = b.Time - st.Started;
            if (st.Scout >= 0)
            {
                var s = b.Soldiers[st.Scout];
                if (object.ReferenceEquals(s.Path, st.Route))
                    TacticalAI.Stop(s);
            }

            st.Route = null;
            b.Green.NextPlan = b.Time;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green)
                    s.NextDecision = b.Time;
            b.History.Event(b, "reconEnd", st.Scout, -1, st.Goal, st.Goal, 0, why);
        }

        public static void Update(BattleSimulation b)
        {
            if (!b.QuietReconEnabled || b.Historical01225 || !b.PairExperimentMarch)
                return;
            var st = Data.GetOrCreateValue(b);
            if (st.Active)
            {
                if (!Safe(b, st) || b.Green.Revision != st.Revision)
                {
                    End(b, st, "reaction_or_emergency");
                    return;
                }

                if (b.Time >= st.Until)
                {
                    End(b, st, "observation_budget");
                    return;
                }

                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Green && s.Available)
                        foreach (var c in s.Contacts.Values)
                            if (c.Visual && !c.Inferred && b.Time - c.SeenAt <= 1 && st.Known.Add(c.EnemyId))
                                st.Stats.NewContacts++;
                if (st.Route != null)
                {
                    var s = b.Soldiers[st.Scout];
                    if (object.ReferenceEquals(s.Path, st.Route))
                        st.Cursor = s.PathCursor;
                    if (V2.Distance(s.Position, st.Goal) < .6f)
                    {
                        st.Stats.Arrivals++;
                        st.Route = null;
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

                return;
            }

            if (st.Used || !Safe(b, st))
                return;
            foreach (var s in b.Soldiers)
                if (Eligible(b, s))
                    foreach (var c in s.Contacts.Values)
                        if (!c.Reported && s.Visible.Contains(c.EnemyId) && SeemsUnaware(b, s, c))
                        {
                            st.Used = st.Active = true;
                            st.Started = b.Time;
                            st.Until = b.Time + 8;
                            st.Revision = b.Green.Revision;
                            st.Stats.Starts++;
                            st.Known.Add(c.EnemyId);
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
                            Plan(b, st, s, c);
                            return;
                        }
        }

        static bool SafePoint(BattleSimulation b, Soldier s, V2 p)
        {
            foreach (var c in Knowledge.Contacts(b, s))
                if (c.Visual && !c.Inferred && b.Time - c.SeenAt < 5)
                {
                    float d = V2.Distance(p, c.Position);
                    if (d < Math.Min(8, V2.Distance(s.Position, c.Position) - .25f))
                        return false;
                    if (
                        b.Map.LineOfSight(p, c.Position)
                        && c.ObservedFacing.Length > .1f
                        && V2.Dot(c.ObservedFacing.Normalized, (p - c.Position).Normalized) > .35f
                        && d < V2.Distance(s.Position, c.Position) - .5f
                    )
                        return false;
                }

            return true;
        }

        static void Plan(BattleSimulation b, State st, Soldier s, Contact contact)
        {
            b.Tactics.Refresh(b.Map);
            float best = 0;
            List<V2> route = null;
            V2 goal = new V2();
            int checkedPaths = 0;
            var points = new List<V2>();
            foreach (var slot in b.Tactics.Slots)
                points.Add(slot.Position);
            for (int y = -5; y <= 5; y++)
            for (int x = -5; x <= 5; x++)
                if (x * x + y * y <= 25)
                    points.Add(
                        new V2((float)Math.Floor(s.Position.X) + x + .5f, (float)Math.Floor(s.Position.Y) + y + .5f)
                    );
            foreach (var p in points)
            {
                float d = V2.Distance(p, s.Position);
                if (
                    !b.Map.Walkable(p)
                    || d < 1
                    || d > 5
                    || !CommandHierarchy.WithinManeuver(b, s, p)
                    || PositionLogic.Occupied(b, s, p)
                    || !SafePoint(b, s, p)
                )
                    continue;
                int gain = 0;
                for (int k = 0; k < 8; k++)
                {
                    float a = k * (float)Math.PI / 4;
                    var probe = contact.Position + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * 3;
                    if (b.Map.Walkable(probe) && !b.Map.LineOfSight(s.Position, probe) && b.Map.LineOfSight(p, probe))
                        gain++;
                }

                for (int k = 0; k < 12; k++)
                {
                    float a = k * (float)Math.PI / 6;
                    var probe = s.Position + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * 8;
                    if (
                        b.Map.Walkable(probe)
                        && (!DirectionalVision.InView(b, s, probe) || !b.Map.LineOfSight(s.Position, probe))
                        && b.Map.LineOfSight(p, probe)
                    )
                        gain++;
                }

                float score = gain * 2 - d;
                if (score <= best || checkedPaths++ >= 12)
                    continue;
                var path = LossMemory.Find(b, s, p, v => PositionLogic.Exposure(b, s, v) * 10);
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 7)
                    continue;
                bool safe = true;
                V2 prev = s.Position;
                foreach (var v in path)
                {
                    int n = Math.Max(1, (int)Math.Ceiling(V2.Distance(prev, v) * 2));
                    for (int j = 1; j <= n; j++)
                        if (!SafePoint(b, s, prev + (v - prev) * (j / (float)n)))
                            safe = false;
                    prev = v;
                }

                if (!safe)
                    continue;
                best = score;
                route = path;
                goal = p;
            }

            if (route != null)
            {
                st.Route = route;
                st.Goal = goal;
                st.Scout = s.Id;
                st.Progress = s.Position;
                st.ProgressAt = b.Time;
                st.Stats.Routes++;
                b.History.Event(b, "reconRoute", s.Id, contact.EnemyId, s.Position, goal, 0, "new_sightlines");
            }
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!Active(b) || !Eligible(b, s))
                return false;
            var st = Data.GetOrCreateValue(b);
            if (!Safe(b, st))
            {
                End(b, st, "reaction_or_emergency");
                return false;
            }

            if (s.Id == st.Scout && st.Route != null)
            {
                s.Path = st.Route;
                s.PathCursor = st.Cursor;
                s.Destination = s.OrderAnchor = st.Goal;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                CommandHierarchy.TransferAssignment(b, s, "Передано разведке");
                s.Job = Job.Reposition;
            }
            else
            {
                TacticalAI.Stop(s);
                s.MoveAuthorized = false;
                s.Job = Job.Cover;
            }

            s.Reason = "Наблюдаю; противник пока не проявляет реакции";
            return true;
        }

        public static void Enforce(BattleSimulation b)
        {
            if (Active(b))
                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Green)
                        Decide(b, s);
        }

        public static bool HoldFire(BattleSimulation b, Soldier s)
        {
            if (!Active(b) || s.Side != Side.Green)
                return false;
            var st = Data.GetOrCreateValue(b);
            if (!Safe(b, st))
            {
                End(b, st, "reaction_or_emergency");
                return false;
            }

            st.Stats.HeldShots++;
            return true;
        }
    }
}
