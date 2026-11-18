using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Battle-local reports of friendly casualties; never contains the shooter's hidden position.
    public sealed class LossSite
    {
        public Side Side;
        public int Victim;
        public V2 Position;
        public float At, Severity;
    }

    public sealed class LossMemoryState
    {
        public readonly List<LossSite> Sites = new List<LossSite>();
        public readonly Dictionary<int, float> Replan = new Dictionary<int, float>();
    }

    // Only read by the current mover during Find; source lists stay in simulation order.
    internal sealed class RouteSnapshot
    {
        public Soldier Owner;
        bool gotContacts, gotLanes, gotPositions;
        readonly List<Contact> contacts = new List<Contact>();
        readonly List<Soldier> lanes = new List<Soldier>();
        readonly List<V2> positions = new List<V2>();
        public void Begin(Soldier s)
        {
            Owner = s;
            gotContacts = gotLanes = gotPositions = false;
        }

        public void End()
        {
            Owner = null;
            contacts.Clear();
            lanes.Clear();
            positions.Clear();
        }

        public List<Contact> Contacts(BattleSimulation b)
        {
            if (!gotContacts)
            {
                foreach (var c in Knowledge.Contacts(b, Owner))
                    if (PositionLogic.Recent(b, c))
                        contacts.Add(c);
                gotContacts = true;
            }

            return contacts;
        }

        public List<Soldier> Lanes(BattleSimulation b)
        {
            if (!gotLanes)
            {
                foreach (var s in b.Soldiers)
                    if (FireLaneSafety.CostSource(b, Owner, s))
                        lanes.Add(s);
                gotLanes = true;
            }

            return lanes;
        }

        public List<V2> Positions(BattleSimulation b)
        {
            if (!gotPositions)
            {
                foreach (var s in b.Soldiers)
                    if (s.Side == Owner.Side && s.Id != Owner.Id && s.Alive && s.Deployed)
                    {
                        positions.Add(s.Position);
                        if (s.Available && V2.Distance(s.Position, s.OrderAnchor) > .6f && ExecutionReview.ReservesGoal(b, s))
                            positions.Add(s.OrderAnchor);
                    }

                gotPositions = true;
            }

            return positions;
        }
    }

    public static class LossMemory
    {
#if RUBEZH_BENCH
        public static Action<bool, V2> TraceRiskEvaluation;
#endif
        // One synchronous Pathfinder.Find snapshot only. Exact points, no cell rounding,
        // time-to-live, cross-unit reuse or new tactical information. As for Pathfinder's
        // cost memo, callbacks must not mutate the queried world during a search.
        struct RiskPoint : IEquatable<RiskPoint>
        {
            readonly float x, y;
            public RiskPoint(V2 p)
            {
                x = p.X;
                y = p.Y;
            }

            public bool Equals(RiskPoint p) => x == p.x && y == p.y;
            public override bool Equals(object p) => p is RiskPoint && Equals((RiskPoint)p);
            public override int GetHashCode()
            {
                unchecked
                {
                    return x.GetHashCode() * 397 ^ y.GetHashCode();
                }
            }
        }

        sealed class QueryRisk
        {
            public bool Busy;
            BattleSimulation battle;
            Soldier unit;
            Func<V2, float> extra;
            bool losses, lanes;
            readonly Dictionary<RiskPoint, float> loss = new Dictionary<RiskPoint, float>(), fire = new Dictionary<RiskPoint, float>();
            public readonly Func<V2, float> Cost;
            public readonly Func<V2, V2, bool> Edge;
            public QueryRisk()
            {
                Cost = Score;
                Edge = Permits;
            }

            public void Begin(BattleSimulation b, Soldier s, Func<V2, float> risk)
            {
                Busy = true;
                battle = b;
                unit = s;
                extra = risk;
                losses = lanes = false;
                foreach (var e in b.LossLearning.Sites)
                    if (e.Side == s.Side && !(b.Time - e.At >= 90))
                    {
                        losses = true;
                        break;
                    }

                foreach (var e in s.IncomingFire.Lanes)
                    if (e.Until > b.Time)
                    {
                        lanes = true;
                        break;
                    }
            }

            public bool HasEdges => losses || lanes;

            float Loss(V2 p)
            {
                if (!losses)
                    return 0;
                var key = new RiskPoint(p);
                float value;
                if (!loss.TryGetValue(key, out value))
                {
                    value = Risk(battle, unit, p);
                    loss.Add(key, value);
                }

                return value;
            }

            float Fire(V2 p)
            {
                if (!lanes)
                    return 0;
                var key = new RiskPoint(p);
                float value;
                if (!fire.TryGetValue(key, out value))
                {
                    value = IncomingFire.Risk(battle, unit, p);
                    fire.Add(key, value);
                }

                return value;
            }

            float Score(V2 p) => (extra == null ? 0 : extra(p)) + Loss(p) * 24 + Fire(p) * 40;
            bool Permits(V2 from, V2 to)
            {
                var step = to - from;
                if (losses && !IgnoresRisk(unit))
                {
                    var ahead = from + step.Normalized * Math.Max(.8f, step.Length);
                    float next = Loss(ahead), current = Loss(from);
                    if (!PermitsRisk(next, current))
                        return false;
                }

                if (lanes && !IncomingFire.IgnoresRisk(unit))
                {
                    var ahead = from + step.Normalized * Math.Max(.65f, step.Length);
                    if (Fire(ahead) > Fire(from))
                        return false;
                }

                return true;
            }

            public void End()
            {
                battle = null;
                unit = null;
                extra = null;
                loss.Clear();
                fire.Clear();
                Busy = false;
            }
        }

        [ThreadStatic]
        static QueryRisk reusableRisk;
        public static void Record(BattleSimulation b, Soldier victim, float amount)
        {
            if (amount < 20 && victim.Health > 20)
                return;
            bool reported = victim.Alive && !victim.Incapacitated;
            foreach (var mate in b.Soldiers)
                if (mate != victim && mate.Side == victim.Side && mate.Available && mate.Deployed && V2.Distance(mate.Position, victim.Position) < 12 && b.Map.LineOfSight(mate.Position, victim.Position))
                {
                    reported = true;
                    break;
                }

            if (!reported)
                return;
            var sites = b.LossLearning.Sites;
            sites.RemoveAll(e => b.Time - e.At >= 90);
            var e = sites.Find(x => x.Side == victim.Side && x.Victim == victim.Id && V2.Distance(x.Position, victim.Position) < 3);
            if (e == null)
            {
                if (sites.Count >= 64)
                    sites.RemoveAt(0);
                e = new LossSite
                {
                    Side = victim.Side,
                    Victim = victim.Id,
                    Position = victim.Position
                };
                sites.Add(e);
            }

            e.At = b.Time;
            e.Severity = Math.Max(e.Severity, victim.Health <= 20 ? 1f : .45f);
        }

        public static float Risk(BattleSimulation b, Soldier s, V2 p)
        {
#if RUBEZH_BENCH
            TraceRiskEvaluation?.Invoke(false, p);
#endif
            float risk = 0;
            foreach (var e in b.LossLearning.Sites)
            {
                float age = b.Time - e.At, d = V2.Distance(p, e.Position);
                if (e.Side != s.Side || age >= 90 || d >= 3 || !b.Map.LineOfSight(p, e.Position))
                    continue;
                risk += e.Severity * Math.Max(0, 1 - age / 90) * Math.Min(1, (3 - d) / 1.5f);
            }

            return Math.Min(3, risk);
        }

        public static List<V2> Find(BattleSimulation b, Soldier s, V2 end, Func<V2, float> risk = null, Func<V2, V2, bool> edgeAllowed = null)
        {
            var view = b.RouteView;
            bool ownsView = view.Owner == null;
            var query = reusableRisk;
            if (query == null)
                reusableRisk = query = new QueryRisk();
            else if (query.Busy)
                query = new QueryRisk();
            try
            {
                query.Begin(b, s, risk);
                if (ownsView)
                    view.Begin(s);
                Func<V2, V2, bool> edge = query.HasEdges ? query.Edge : null;
                if (edgeAllowed != null)
                    edge = (from, to) => query.Edge(from, to) && edgeAllowed(from, to);
                return Pathfinder.Find(b.Map, s.Position, end, query.Cost, edge);
            }
            finally
            {
                query.End();
                if (ownsView)
                    view.End();
            }
        }

        internal static bool IgnoresRisk(Soldier s) => s.Incapacitated || s.Busy || s.GrenadeEscape.Active || s.CounterSniper.Phase != CounterSniperPhase.None || s.RescueTargetId >= 0;
        internal static bool PermitsRisk(float next, float current) => next < .7f || next <= current + .02f;
        // No mutation. Future edges cannot use SmokeSafe: it examines the old active route.
        // Planning is conservative until a per-edge smoke forecast is available.
        public static bool StepAllowed(BattleSimulation b, Soldier s, V2 from, V2 desired, bool activeRouteSmoke = true)
        {
            if (IgnoresRisk(s))
                return true;
            var ahead = from + desired.Normalized * Math.Max(.8f, desired.Length);
            float risk = Risk(b, s, ahead), current = Risk(b, s, from);
            return PermitsRisk(risk, current) || activeRouteSmoke && CommanderPriorities.SmokeSafe(b, s);
        }

        public static bool AllowStep(BattleSimulation b, Soldier s, V2 desired)
        {
            if (StepAllowed(b, s, s.Position, desired))
                return true;
            float next;
            b.LossLearning.Replan.TryGetValue(s.Id, out next);
            if (b.Time >= next)
            {
                b.LossLearning.Replan[s.Id] = b.Time + 2;
                var route = Find(b, s, s.Destination, p => PositionLogic.Exposure(b, s, p) * 3 + (PositionLogic.Occupied(b, s, p) ? 12 : 0));
                b.Stats.Paths++;
                s.Path = route;
                s.PathCursor = 0;
                s.NextDecision = b.Time;
                s.Reason = "Здесь недавно потери: проверяю обход, не повторяю опасный выход";
                TacticalOrders.TrySmoke(b, s, TacticalOrders.ClosestKnown(b, s));
            }

            s.Moving = false;
            return false;
        }
    }
}
