using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum PairDuty
    {
        Advance,
        Maneuver,
        Support,
        Guard,
        Reserve,
        External
    }

    public enum AssignmentStatus
    {
        None,
        Executing,
        Holding,
        Waiting,
        Interrupted,
        Completed,
        Blocked,
        Transferred
    }

    // Group-owned memory survives any particular leader. IDs are battle-local, not roster slots.
    public sealed class GroupCommandPlan
    {
        public readonly TacticalMarchState March = new TacticalMarchState();
        public readonly GreenExperimentMemory Experiments = new GreenExperimentMemory();
        public bool Initialized, Active, LastDuo;
        public int Leader = -1, Deputy = -1, Generation, NextPairId, OrderRevision = -1, Composition, ReviewCount;
        public int Successions, PairSuccessions, PairRebuilds, LocalFailures, PairFailures, GlobalResetsAvoided;
        public int SnailBounds;
        public int Assignments, Completed, Interrupted, Transfers, Rejected, NoRoute, DependencyWaits, DependencyReleases, PlanChanges, OrphanMerges, StaleReleases, LimitedBounds;
        public float NextReview, LeaderUnfitSince = -1;
        public CombatStyle Style;
        public ManeuverIntent Intent;
        public string Scheme = "Начальный план", Reason = "";
        public readonly List<PairCommandPlan> Pairs = new List<PairCommandPlan>();
    }

    public sealed class PairCommandPlan
    {
        public int Id, A = -1, B = -1, Leader = -1, Mover = -1, LastMover = -1, SupportPair = -1;
        public int Revision, Failures, Completed, Successions, LastTried = -1, PreferredSide;
        public PairDuty Duty;
        public V2 Goal;
        public float NextReview, NextBound, WaitingSince = -1, UnderstrengthSince = -1, FailedUntil;
        public string Reason = "";
        public bool NeedsReview, LateralComplete, Converging;
        public int IntentKey = -1, ManeuverSide;
        public float LateralDeadline, LateralTarget;
    }

    public sealed class UnitAssignment
    {
        public int PairId = -1, Leader = -1, PlanGeneration = -1, PairRevision = -1, Revision, Assignments, Completed, Interrupted, Transfers, Rejected, Failures;
        public int LastShots;
        public bool Active, IsBound, LimitedBound, SnailBound;
        public int CoverId = -1;
        public PairDuty Duty;
        public AssignmentStatus Status;
        public V2 Goal, LastPosition, FailedGoal;
        internal List<V2> OwnedRoute;
        public float Since, Deadline, ProgressAt, NextSample, LastSample, FailedUntil, NextRoute;
        public float ExecutingSeconds, UsefulSeconds, WaitingSeconds, InterruptedSeconds, BlockedSeconds;
        public string Reason = "", Owner = "";
    }

    public static class CommandHierarchy
    {
        static Soldier Unit(BattleSimulation b, int id)
        {
            return id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;
        }

        static bool Member(Soldier s, Squad q)
        {
            return s != null && q.Contains(s) && s.Deployed && s.Available;
        }

        static bool CommanderReady(Soldier s, Squad q)
        {
            return Member(s, q) && !s.Broken && !s.VehicleRally && !s.VehicleDisperse;
        }

        static bool Free(BattleSimulation b, Soldier s)
        {
            return PositionLogic.ShelterEligible(b, s) && !SniperTactics.OwnsSpecialPost(b, s) && !ExecutionReview.ProtectedTask(b, s) && s.ReloadUntil <= b.Time && s.Ammo + s.Reserve > 0 && s.CounterSniper.Phase == CounterSniperPhase.None && !LastOpponentHunt.Owns(b, s);
        }

        public static bool Owns(Squad q)
        {
            return q.CommandPlan.Initialized && q.CommandPlan.Active;
        }

        public static PairCommandPlan Pair(Squad q, int id)
        {
            foreach (var p in q.CommandPlan.Pairs)
                if (p.Id == id)
                    return p;
            return null;
        }

        // PairId + plan generation + pair revision identify the only pair contract
        // permitted to control this unit. Specialist executors explicitly release it
        // before publishing their own route; diagnostics do not create ownership.
        public static void ReleaseAssignment(BattleSimulation b, Soldier s, string reason, bool stopOwnedRoute)
        {
            EndAssignment(b, s, reason, stopOwnedRoute, false);
        }

        // A successor already owns a route or hold. Retire the pair lease without
        // waking its planner or the unit's decision loop over that successor.
        public static void TransferAssignment(BattleSimulation b, Soldier s, string reason)
        {
            EndAssignment(b, s, reason, false, true);
        }

        static void EndAssignment(BattleSimulation b, Soldier s, string reason, bool stopOwnedRoute, bool transfer)
        {
            var a = s.Assignment;
            if (!a.Active && !a.IsBound && a.OwnedRoute == null)
                return;
            var q = b.Team(s);
            var p = Pair(q, a.PairId);
            bool owned = a.Active, wasMover = p != null && p.Mover == s.Id;
            if (stopOwnedRoute && V2.Distance(s.OrderAnchor, a.Goal) < .2f && (a.OwnedRoute != null && object.ReferenceEquals(a.OwnedRoute, s.Path) || a.OwnedRoute == null && owned && a.IsBound && !Special(b, s)))
            {
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
            }

            if (owned)
            {
                if (transfer)
                {
                    a.Transfers++;
                    q.CommandPlan.Transfers++;
                    a.Status = AssignmentStatus.Transferred;
                }
                else
                {
                    a.Interrupted++;
                    q.CommandPlan.Interrupted++;
                    a.Status = AssignmentStatus.Interrupted;
                }

                a.Reason = reason;
            }

            a.Active = false;
            a.IsBound = false;
            a.LimitedBound = false;
            a.SnailBound = false;
            a.CoverId = -1;
            a.Owner = "";
            a.OwnedRoute = null;
            if (wasMover)
                p.Mover = -1;
            if (!transfer && (owned || wasMover))
            {
                if (p != null)
                {
                    p.NeedsReview = true;
                    p.NextReview = 0;
                }

                q.NextPlan = 0;
                s.NextDecision = 0;
            }
        }

        static bool AssignmentCurrent(BattleSimulation b, Soldier s)
        {
            var a = s.Assignment;
            if (!a.Active)
                return false;
            var q = b.Team(s);
            var p = Pair(q, a.PairId);
            bool current = Owns(q) && Member(s, q) && p != null && (p.A == s.Id || p.B == s.Id) && (a.PlanGeneration < 0 || a.PlanGeneration == q.CommandPlan.Generation) && (a.PairRevision < 0 || a.PairRevision == p.Revision);
            if (!current)
                ReleaseAssignment(b, s, "Назначение устарело: состав или план изменён", true);
            return current;
        }

        static int Rank(Soldier s, GroupCommandPlan st)
        {
            int v = s.Role == Role.Commander ? 100 : 0;
            foreach (var p in st.Pairs)
                if (p.Leader == s.Id)
                    v += 25;
            return v + (int)(s.Morale * 10);
        }

        public static void Maintain(BattleSimulation b, Squad q)
        {
            if (b.CustomDefenders && q == b.Red)
                return;
            var st = q.CommandPlan;
            var leader = Unit(b, st.Leader);
            if (!st.Initialized)
            {
                st.Initialized = true;
                st.Leader = q.LeaderId;
                st.Style = q.Style;
                st.Intent = q.Intent;
                st.Reason = "Начальное распределение";
                leader = Unit(b, st.Leader);
            }

            if (Member(leader, q) && leader.Broken)
            {
                if (st.LeaderUnfitSince < 0)
                    st.LeaderUnfitSince = b.Time;
            }
            else
                st.LeaderUnfitSince = -1;
            bool replace = !Member(leader, q) || st.LeaderUnfitSince >= 0 && b.Time - st.LeaderUnfitSince >= 6;
            if (replace)
            {
                Soldier next = Unit(b, st.Deputy);
                if (!CommanderReady(next, q))
                    next = null;
                foreach (var s in b.Soldiers)
                    if (CommanderReady(s, q) && (next == null || Rank(s, st) > Rank(next, st) || Rank(s, st) == Rank(next, st) && s.Id < next.Id))
                        next = s;
                if (next != null && next.Id != st.Leader)
                {
                    int old = st.Leader;
                    st.Leader = next.Id;
                    st.Successions++;
                    st.LeaderUnfitSince = -1;
                    b.History.Event(b, "commandSuccession", next.Id, old, next.Position, next.Position, st.Generation, "Наследую план группы; действующие задания сохраняются");
                }
            }

            if (Member(Unit(b, st.Leader), q))
                q.LeaderId = st.Leader;
            Soldier deputy = null;
            foreach (var s in b.Soldiers)
                if (s.Id != st.Leader && CommanderReady(s, q) && (deputy == null || Rank(s, st) > Rank(deputy, st)))
                    deputy = s;
            st.Deputy = deputy == null ? -1 : deputy.Id;
            foreach (var p in st.Pairs)
            {
                var a = Unit(b, p.A);
                var z = Unit(b, p.B);
                var current = Unit(b, p.Leader);
                if (!CommanderReady(current, q))
                {
                    var next = CommanderReady(a, q) ? a : CommanderReady(z, q) ? z : null;
                    if (next != null && next.Id != p.Leader)
                    {
                        int old = p.Leader;
                        p.Leader = next.Id;
                        p.Successions++;
                        st.PairSuccessions++;
                        b.History.Event(b, "pairSuccession", next.Id, old, next.Position, p.Goal, p.Id, "Старший двойки сменился; задача сохранена");
                    }
                }

                for (int memberIndex1 = 0; memberIndex1 < 2; memberIndex1++)
                {
                    int id = memberIndex1 == 0 ? p.A : p.B;
                    var s = Unit(b, id);
                    if (Member(s, q))
                    {
                        s.Assignment.Leader = p.Leader;
                        s.Assignment.PairId = p.Id;
                    }
                }
            }
        }

        // Joining healthy singletons is optional. Preserve a current bound
        // until its normal completion/cancellation; casualty pairs never defer.
        static bool WorkingSingleton(BattleSimulation b, Squad q, PairCommandPlan p, Soldier s)
        {
            var a = s.Assignment;
            if (!a.Active || !a.IsBound || a.PairId != p.Id || p.Mover != s.Id || a.PlanGeneration != q.CommandPlan.Generation || a.PairRevision != p.Revision || a.OwnedRoute == null || !object.ReferenceEquals(a.OwnedRoute, s.Path) || !Owns(q) || !Member(s, q) || Special(b, s) || b.Time > a.Deadline || V2.Distance(s.OrderAnchor, a.Goal) > .2f || s.PathCursor < 0 || s.PathCursor >= s.Path.Count)
                return false;
            var next = s.Path[s.PathCursor];
            return b.Map.Walkable(a.Goal) && b.Map.Walkable(next) && Locomotion.WalkableSegment(b, s.Position, next);
        }

        static bool InLine(Soldier u, Squad q) => u != null && q.Contains(u) && u.Deployed && u.Alive && !u.Incapacitated;
        static void Regroup(BattleSimulation b, Squad q)
        {
            var st = q.CommandPlan;
            var left = new List<Soldier>();
            int live = 0;
            foreach (var u in b.Soldiers)
                if (InLine(u, q))
                {
                    left.Add(u);
                    live++;
                }

            st.LastDuo = q.Side == Side.Green && live == 2;
            var singles = new List<PairCommandPlan>();
            bool changed = false;
            for (int i = st.Pairs.Count - 1; i >= 0; i--)
            {
                var p = st.Pairs[i];
                var a = Unit(b, p.A);
                var z = Unit(b, p.B);
                bool aa = InLine(a, q), zz = InLine(z, q);
                if (aa && zz)
                {
                    left.Remove(a);
                    left.Remove(z);
                    a.BuddyId = z.Id;
                    z.BuddyId = a.Id;
                    continue;
                }

                if (aa && p.B < 0)
                {
                    if (WorkingSingleton(b, q, p, a))
                    {
                        left.Remove(a);
                        a.BuddyId = a.Id;
                        continue;
                    }

                    singles.Add(p);
                    continue;
                }

                st.Pairs.RemoveAt(i);
                changed = true;
                foreach (var u in new[]
                {
                    a,
                    z
                }

                )
                    if (u != null && u.Assignment.PairId == p.Id)
                    {
                        ReleaseAssignment(b, u, "Двойка расформирована", true);
                        u.Assignment.PairId = -1;
                        u.Assignment.Leader = -1;
                        u.BuddyId = u.Id;
                    }
            }

            while (left.Count > 0)
            {
                Soldier a = null, z = null;
                float best = float.NegativeInfinity;
                if (left.Count == 1)
                    a = left[0];
                else
                    for (int i = 0; i < left.Count; i++)
                        for (int j = i + 1; j < left.Count; j++)
                        {
                            var x = left[i];
                            var y = left[j];
                            float score = -V2.Distance(x.Position, y.Position);
                            if (FormationAI.PairPreferred(q, x.Id, y.Id))
                                score += 10000;
                            if (live >= 5 && (left.Count % 2) == 1 && (x.Id == q.LeaderId || y.Id == q.LeaderId))
                                score -= 20000;
                            if (x.BuddyId == y.Id && y.BuddyId == x.Id)
                                score += 2;
                            if (score > best)
                            {
                                best = score;
                                a = x;
                                z = y;
                            }
                        }

                left.Remove(a);
                if (z != null)
                    left.Remove(z);
                if (z != null && (z.Id == q.LeaderId || a.Id != q.LeaderId && a.Role == Role.Medic && z.Role != Role.Medic))
                {
                    var swap = a;
                    a = z;
                    z = swap;
                }

                var existing = singles.Find(p => p.A == a.Id);
                if (z == null && existing != null)
                {
                    a.BuddyId = a.Id;
                    continue;
                }

                for (int i = singles.Count - 1; i >= 0; i--)
                {
                    var old = singles[i];
                    if (old.A == a.Id || z != null && old.A == z.Id)
                    {
                        st.Pairs.Remove(old);
                        singles.RemoveAt(i);
                    }
                }

                var plan = new PairCommandPlan
                {
                    Id = st.NextPairId++,
                    A = a.Id,
                    B = z == null ? -1 : z.Id,
                    Leader = a.Id,
                    NeedsReview = true
                };
                if (q.Formation != null && q.Formation.Valid() && q.Formation.pairing != 0)
                    for (int k = 0; k < 4; k++)
                        if (q.Formation.DisplaySlot(k) == a.Id)
                            plan.PreferredSide = k < 2 ? -1 : 1;
                st.Pairs.Add(plan);
                changed = true;
                st.PairRebuilds++;
                foreach (var u in new[]
                {
                    a,
                    z
                }

                )
                    if (u != null)
                    {
                        ReleaseAssignment(b, u, "Назначена новая двойка", true);
                        u.Assignment.PairId = plan.Id;
                        u.Assignment.Leader = a.Id;
                        u.BuddyId = z == null ? u.Id : u == a ? z.Id : a.Id;
                    }
            }

            if (changed)
                st.NextReview = 0;
        }

        static void BuildPairs(BattleSimulation b, Squad q)
        {
            var st = q.CommandPlan;
            if (!(b.CustomDefenders && q == b.Red) && (st.Pairs.Count > 0 || q.Side == Side.Green && b.Soldiers.FindAll(u => InLine(u, q)).Count < 5))
            {
                Regroup(b, q);
                return;
            }

            var used = new HashSet<int>();
            foreach (var p in st.Pairs)
            {
                if (Member(Unit(b, p.A), q))
                    used.Add(p.A);
                if (Member(Unit(b, p.B), q))
                    used.Add(p.B);
            }

            var left = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (Member(s, q) && !used.Contains(s.Id) && !s.VehicleRally && !s.VehicleDisperse)
                    left.Add(s);
            left.Sort((a, z) => a.Id.CompareTo(z.Id));
            // Respect surviving preferred pairs. Temporary reload/medical service does not dissolve them.
            while (left.Count > 0)
            {
                var a = left[0];
                left.RemoveAt(0);
                Soldier z = null;
                if (a.Id != q.LeaderId || b.PairExperimentMarch && q.Side == Side.Green)
                {
                    foreach (var other in left)
                        if ((other.Id != q.LeaderId || b.PairExperimentMarch && q.Side == Side.Green) && FormationAI.PairPreferred(q, a.Id, other.Id))
                        {
                            z = other;
                            break;
                        }

                    if (z == null)
                        foreach (var other in left)
                            if (other.Id == a.BuddyId && (other.Id != q.LeaderId || b.PairExperimentMarch && q.Side == Side.Green))
                            {
                                z = other;
                                break;
                            }

                    if (z == null)
                        foreach (var other in left)
                            if ((other.Id != q.LeaderId || b.PairExperimentMarch && q.Side == Side.Green))
                            {
                                z = other;
                                break;
                            }
                }

                if (z != null)
                    left.Remove(z);
                var p = new PairCommandPlan
                {
                    Id = st.NextPairId++,
                    A = a.Id,
                    B = z == null ? -1 : z.Id,
                    Leader = a.Id
                };
                if (q.Side == Side.Green && q.Formation != null && q.Formation.Valid() && q.Formation.pairing != 0)
                {
                    for (int slot = 0; slot < 4; slot++)
                        if (q.Formation.DisplaySlot(slot) == a.Id)
                            p.PreferredSide = slot < 2 ? -1 : 1;
                }

                st.Pairs.Add(p);
                st.PairRebuilds++;
                p.NeedsReview = true;
                a.Assignment.PairId = p.Id;
                a.Assignment.Leader = p.Leader;
                a.BuddyId = z == null ? a.Id : z.Id;
                if (z != null)
                {
                    z.Assignment.PairId = p.Id;
                    z.Assignment.Leader = p.Leader;
                    z.BuddyId = a.Id;
                }
            }

            // Merge only nearby idle remnants; never recall a working intact pair.
            for (int i = 0; i < st.Pairs.Count; i++)
            {
                var p = st.Pairs[i];
                var a = Member(Unit(b, p.A), q) ? Unit(b, p.A) : Member(Unit(b, p.B), q) ? Unit(b, p.B) : null;
                if (a == null || Member(Unit(b, p.A), q) && Member(Unit(b, p.B), q) || a.Id == q.LeaderId || p.UnderstrengthSince < 0 || b.Time - p.UnderstrengthSince < 8 || a.PathCursor < a.Path.Count)
                    continue;
                for (int j = i + 1; j < st.Pairs.Count; j++)
                {
                    var other = st.Pairs[j];
                    var z = Member(Unit(b, other.A), q) ? Unit(b, other.A) : Member(Unit(b, other.B), q) ? Unit(b, other.B) : null;
                    if (z == null || Member(Unit(b, other.A), q) && Member(Unit(b, other.B), q) || z.Id == q.LeaderId || z.PathCursor < z.Path.Count || other.UnderstrengthSince < 0 || b.Time - other.UnderstrengthSince < 8 || V2.Distance(a.Position, z.Position) > 12)
                        continue;
                    ReleaseAssignment(b, a, "Остатки двоек объединены", true);
                    ReleaseAssignment(b, z, "Остатки двоек объединены", true);
                    p.A = a.Id;
                    p.B = z.Id;
                    p.Leader = a.Id;
                    p.NeedsReview = true;
                    p.NextReview = 0;
                    p.UnderstrengthSince = -1;
                    other.A = other.B = other.Leader = other.Mover = -1;
                    a.Assignment.PairId = z.Assignment.PairId = p.Id;
                    a.Assignment.Leader = z.Assignment.Leader = p.Leader;
                    st.OrphanMerges++;
                    st.PairRebuilds++;
                    b.History.Event(b, "pairMerged", a.Id, z.Id, a.Position, z.Position, p.Id, "Объединены ближайшие свободные остатки двоек");
                    break;
                }
            }

            foreach (var p in st.Pairs)
            {
                var a = Unit(b, p.A);
                var z = Unit(b, p.B);
                bool aa = Member(a, q), zz = Member(z, q);
                if (aa)
                    a.BuddyId = zz ? z.Id : a.Id;
                if (zz)
                    z.BuddyId = aa ? a.Id : z.Id;
                if (aa != zz && !(p.A == q.LeaderId && p.B < 0))
                {
                    if (p.UnderstrengthSince < 0)
                        p.UnderstrengthSince = b.Time;
                    p.Reason = "Неполный состав: задача выполняется доступными силами";
                }
                else
                    p.UnderstrengthSince = -1;
            }
        }

        // Between contacts, the two survivors use a common approach lane. The medic
        // follows the commander's actual position rather than a vacant formation slot.
        public static bool DuoMarch(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (!q.CommandPlan.LastDuo || !Owns(q) || q.CommandTransition.Waiting || q.Style != CombatStyle.Aggressive && q.Style != CombatStyle.Balanced || !Free(b, s) || s.Suppression > .25f || s.Visible.Count > 0 || b.Time - s.LastIncoming < 5 || s.RescueTargetId >= 0 || s.TreatmentUntil > b.Time || TacticalOrders.ServiceActive(s))
                return false;
            foreach (var c in Knowledge.Contacts(b, s))
                if (PositionLogic.Recent(b, c))
                    return false;
            var mate = Unit(b, s.BuddyId);
            if (mate == s || !Member(mate, q))
                return false;
            var pair = Pair(q, s.Assignment.PairId);
            if (pair == null || pair.Duty != PairDuty.Advance && pair.Duty != PairDuty.Maneuver)
                return false;
            bool follower = (s.Role == Role.Medic) != (mate.Role == Role.Medic) ? s.Role == Role.Medic : s.Id != pair.Leader;
            float gap = V2.Distance(s.Position, mate.Position);
            if (follower && gap < 3)
                return false;
            var goal = follower ? mate.Position : (gap > 9 ? mate.Position : pair.Goal);
            if (V2.Distance(s.Position, goal) < 2)
                return false;
            if (b.Time < s.DuoReview && s.PathCursor < s.Path.Count)
            {
                s.MoveAuthorized = true;
                return true;
            }

            var path = LossMemory.Find(b, s, goal, p => PositionLogic.Exposure(b, s, p) * 3 + FireLaneSafety.PathCost(b, s, p));
            b.Stats.Paths++;
            var route = new List<V2>();
            float len = 0;
            var prev = s.Position;
            foreach (var pt in path)
            {
                len += V2.Distance(prev, pt);
                if (len > 5 || !MedicTactics.AllowSegment(b, s, prev, pt) || !WithinManeuver(b, s, pt))
                    break;
                route.Add(pt);
                prev = pt;
                if (follower && V2.Distance(pt, mate.Position) < 2.5f)
                    break;
            }

            if (route.Count == 0 || !PositionLogic.UsefulRoute(b, s, route) || !GroupMovement.CanStart(b, s, prev))
                return false;
            s.Path = route;
            s.PathCursor = 0;
            s.OrderAnchor = s.Destination = prev;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.DuoReview = b.Time + 2;
            s.Reason = follower ? "Подтягиваюсь за старшим двойки" : gap > 9 ? "Сближаюсь с напарником" : "Веду двойку к рубежу";
            return true;
        }

        static V2 Center(BattleSimulation b, Squad q, PairCommandPlan p)
        {
            V2 v = new V2();
            int n = 0;
            for (int memberIndex2 = 0; memberIndex2 < 2; memberIndex2++)
            {
                int id = memberIndex2 == 0 ? p.A : p.B;
                var s = Unit(b, id);
                if (Member(s, q))
                {
                    v += s.Position;
                    n++;
                }
            }

            return n > 0 ? v / n : p.Goal;
        }

        static bool Live(BattleSimulation b, Squad q, PairCommandPlan p)
        {
            return Member(Unit(b, p.A), q) || Member(Unit(b, p.B), q);
        }

        static Contact Threat(BattleSimulation b, Squad q)
        {
            Contact best = null;
            foreach (var c in q.Contacts.Values)
                if (PositionLogic.Recent(b, c) && (best == null || c.Confidence > best.Confidence))
                    best = c;
            return best;
        }

        static bool CanLeadPair(BattleSimulation b, Squad q, PairCommandPlan p)
        {
            for (int memberIndex3 = 0; memberIndex3 < 2; memberIndex3++)
            {
                int id = memberIndex3 == 0 ? p.A : p.B;
                var s = Unit(b, id);
                if (!Member(s, q) || s.Broken || s.Ammo + s.Reserve <= 0 || s.Sniper && !SniperTactics.MobileLead(b, s))
                    continue;
                float front;
                if (s.Role != Role.Medic || !MedicTactics.Front(b, s, out front))
                    return true;
            }

            return false;
        }

        static bool Delegated(BattleSimulation b, Squad q)
        {
            if (TacticalOrders.Emergency(q) || WithdrawalTactics.Active(q) || q.CapturePlan.Active || q.Breakthrough.Active)
                return true;
            if (q.DefenderGroup != null)
            {
                var g = q.DefenderGroup;
                if (g.Counterattack.Phase == RedCounterattackPhase.Assault || g.Counterattack.Phase == RedCounterattackPhase.Retreat || LastOpponentHunt.GroupActive(b, g))
                    return true;
            }
            else if (q.Side == Side.Red && RedCounterattack.Enabled(b) && b.Counterattack.Phase != RedCounterattackPhase.Defense)
                return true;
            return false;
        }

        public static void Begin(BattleSimulation b, Squad q)
        {
            if (q.Intent == ManeuverIntent.Breakthrough)
                q.Intent = ManeuverIntent.Center;
            Maintain(b, q);
            CommandPosture.Sync(b, q);
            BuildPairs(b, q);
            StraightAdvance.Review(b, q);
            var st = q.CommandPlan;
            st.Active = !Delegated(b, q);
            if (!st.Active)
                return;
            bool changed = st.Style != q.Style || st.Intent != q.Intent || st.Generation == 0;
            if (!changed && b.Time < st.NextReview)
                return;
            st.NextReview = b.Time + 6;
            st.ReviewCount++;
            if (changed)
            {
                bool newDirection = st.Intent != q.Intent;
                foreach (var unit in b.Soldiers)
                    if (q.Contains(unit) && newDirection)
                    {
                        ReleaseAssignment(b, unit, "Изменено направление группы", true);
                    }

                st.Style = q.Style;
                st.Intent = q.Intent;
                st.Generation++;
                st.PlanChanges++;
                st.Reason = "Замысел игрока или исходная задача группы";
            }

            var threat = Threat(b, q);
            var live = new List<PairCommandPlan>();
            foreach (var p in st.Pairs)
                if (Live(b, q, p))
                    live.Add(p);
            if (q.Side == Side.Green)
                foreach (var inactive in st.Pairs)
                    if (!Live(b, q, inactive))
                        inactive.SupportPair = -1;
            PairCommandPlan support = null;
            bool wrap = q.Side == Side.Green && (q.Intent == ManeuverIntent.LeftHold || q.Intent == ManeuverIntent.RightHold);
            if (wrap && !changed && (!GreenExperiments.Enabled(q, 1) || live.Count > 1))
                foreach (var p in live)
                    if (p.Duty == PairDuty.Support)
                    {
                        support = p;
                        break;
                    }

            if (wrap && live.Count > 1 && support == null)
            {
                float best = float.MinValue;
                foreach (var p in live)
                {
                    if (q.Side == Side.Green && p.A == q.LeaderId && p.B < 0)
                        continue;
                    float score = 0;
                    for (int memberIndex4 = 0; memberIndex4 < 2; memberIndex4++)
                    {
                        int id = memberIndex4 == 0 ? p.A : p.B;
                        var s = Unit(b, id);
                        if (Member(s, q))
                            score += (s.GunMachineGunner ? 5 : 1) + (ExecutionReview.WorkingSector(b, s) ? 3 : 0);
                    }

                    if (score > best)
                    {
                        best = score;
                        support = p;
                    }
                }
            }

            if (GreenExperiments.Enabled(q, 1) && live.Count == 1 && live[0].Duty == PairDuty.Support)
                st.Experiments.OrphanReleases++;
            bool reserveLeader = q.Side == Side.Green && live.Exists(p => !(p.A == q.LeaderId && p.B < 0) && CanLeadPair(b, q, p));
            if (q.Side == Side.Green && support != null && !live.Exists(p => p != support && !(reserveLeader && p.A == q.LeaderId && p.B < 0) && CanLeadPair(b, q, p)))
                support = null;
            int ordinal = 0;
            foreach (var p in live)
            {
                var center = Center(b, q, p);
                var duty = PairDuty.Advance;
                V2 goal = b.Map.Objective;
                if (q.Style == CombatStyle.Passive)
                {
                    duty = PairDuty.Guard;
                    V2 sum = new V2();
                    int count = 0;
                    for (int memberIndex5 = 0; memberIndex5 < 2; memberIndex5++)
                    {
                        int id = memberIndex5 == 0 ? p.A : p.B;
                        var unit = Unit(b, id);
                        if (Member(unit, q))
                        {
                            sum += CommandPosture.Anchor(b, unit);
                            count++;
                        }
                    }

                    goal = count > 0 ? sum / count : center;
                }
                else if (q.Side == Side.Red)
                {
                    duty = PairDuty.Guard;
                    goal = q.DefenseCenter(b);
                    var axis = threat == null ? TacticalOrders.Axis(b, q.Side) : (threat.Position - goal).Normalized;
                    var lateral = new V2(-axis.Y, axis.X);
                    goal += lateral * ((ordinal % 3 - 1) * 2.5f);
                    if (q.DefenderGroup != null && q.DefenderGroup.Cooperation.Duty == GroupDuty.Support)
                        duty = PairDuty.Support;
                }
                else if (p == support)
                {
                    duty = PairDuty.Support;
                    V2 sum = new V2();
                    int n = 0;
                    foreach (var other in live)
                        if (other != p)
                        {
                            sum += Center(b, q, other);
                            n++;
                        }

                    goal = n > 0 ? sum / n - TacticalOrders.Axis(b, q.Side) * 2 : center;
                }
                else if (q.Intent != ManeuverIntent.Center && q.Intent != ManeuverIntent.Breakthrough && q.Intent != ManeuverIntent.Straight && q.Intent != ManeuverIntent.Front)
                {
                    duty = PairDuty.Maneuver;
                    int side = q.Intent == ManeuverIntent.Left || q.Intent == ManeuverIntent.LeftHold ? -1 : q.Intent == ManeuverIntent.Both ? (p.PreferredSide != 0 ? p.PreferredSide : ordinal % 2 == 0 ? -1 : 1) : 1;
                    float width = Math.Min((wrap ? 9 : 6) * FormationAI.LateralMultiplier(q), V2.Distance(center, b.Map.Objective) * .45f);
                    var axis = TacticalOrders.Axis(b, q.Side);
                    var lateral = TacticalOrders.Lateral(b);
                    if (p.IntentKey != (int)q.Intent)
                    {
                        p.IntentKey = (int)q.Intent;
                        p.ManeuverSide = side;
                        p.LateralComplete = false;
                        p.Converging = false;
                        p.LateralDeadline = b.Time + 14;
                        p.LateralTarget = width;
                    }

                    side = p.ManeuverSide;
                    float along = V2.Dot(center - b.Map.Entry, axis), across = V2.Dot(center - b.Map.Entry, lateral) * side;
                    bool onFlank = true;
                    for (int memberIndex6 = 0; memberIndex6 < 2; memberIndex6++)
                    {
                        int id = memberIndex6 == 0 ? p.A : p.B;
                        var member = Unit(b, id);
                        if (Member(member, q) && member.Available && V2.Dot(member.Position - b.Map.Entry, lateral) * side < p.LateralTarget - .8f)
                            onFlank = false;
                    }

                    // A deadline requests another route review; it is not evidence of arrival.
                    if (onFlank || V2.Distance(center, b.Map.Objective) < 8)
                        p.LateralComplete = true;
                    else if (b.Time >= p.LateralDeadline)
                    {
                        p.LateralDeadline = b.Time + 14;
                        p.NeedsReview = true;
                        p.NextReview = 0;
                    }

                    goal = p.LateralComplete ? goal + lateral * side * width : b.Map.Entry + axis * (along + 2) + lateral * side * width;
                    if (p.LateralComplete && V2.Distance(center, goal) < 4 && V2.Distance(center, b.Map.Objective) < 12)
                        p.Converging = true;
                    if (p.Converging)
                        goal = b.Map.Objective;
                }

                if (p.A == q.LeaderId && p.B < 0 && reserveLeader && q.Side == Side.Green && q.Style != CombatStyle.Passive)
                {
                    duty = PairDuty.Reserve;
                    V2 sum = new V2();
                    int n = 0;
                    foreach (var other in live)
                        if (other != p)
                        {
                            sum += Center(b, q, other);
                            n++;
                        }

                    if (n > 0)
                        goal = sum / n;
                }

                if (GreenExperiments.Enabled(q, 1) && live.Count == 1 && p.Duty == PairDuty.Support)
                {
                    duty = PairDuty.Advance;
                    goal = b.Map.Objective;
                    p.LateralComplete = true;
                    p.Converging = true;
                }

                if (q.Side == Side.Green && q.Intent == ManeuverIntent.Center && q.Style != CombatStyle.Passive && q.Style != CombatStyle.Withdraw)
                    goal = CenterCorridor.Limit(b, goal);
                if (q.Side == Side.Green && q.Intent == ManeuverIntent.Center && q.Style != CombatStyle.Passive && q.Style != CombatStyle.Withdraw)
                    goal = CenterCorridor.Limit(b, goal);
                if (q.Intent == ManeuverIntent.Center || q.Intent == ManeuverIntent.Breakthrough || q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front)
                    p.IntentKey = -1;
                int depends = support != null && p != support ? support.Id : -1;
                goal = TacticalOrders.Open(b, goal);
                if (changed || p.Revision == 0 || p.Duty != duty || V2.Distance(goal, p.Goal) > 3 || q.Side == Side.Green && p.SupportPair != depends)
                {
                    p.Duty = duty;
                    p.Goal = goal;
                    p.SupportPair = depends;
                    p.Revision++;
                    p.NextReview = 0;
                    p.NeedsReview = true;
                    p.Reason = duty == PairDuty.Support ? "Обеспечить работу назначенной двойки" : duty == PairDuty.Guard ? "Удерживать назначенный сектор" : duty == PairDuty.Reserve ? "Сохранять связь и поддерживать двойки" : "Продвигаться в рамках принятого направления";
                }

                if (!(p.A == q.LeaderId && p.B < 0))
                    ordinal++;
            }

            st.Scheme = q.Style == CombatStyle.Passive ? "Закрепление: удержание занятых позиций" : q.Style == CombatStyle.Aggressive ? "Наступление: последовательные перебежки в заданном направлении" : q.Side == Side.Red ? "Согласованные сектора обороны" : wrap ? "Поддержка и маневр" : q.Intent == ManeuverIntent.Both ? "Две самостоятельные двойки" : "Последовательное продвижение двоек";
        }

        // Local failure returns to the owning pair; it never raises the legacy group reset.
        public static void Failed(BattleSimulation b, Soldier s, string reason)
        {
            var q = b.Team(s);
            var st = q.CommandPlan;
            var a = s.Assignment;
            if (!a.Active && a.Status == AssignmentStatus.Blocked && b.Time < a.FailedUntil)
                return;
            var failedGoal = a.Active && a.IsBound ? a.Goal : s.OrderAnchor;
            if (a.Active && a.IsBound && !Special(b, s) && V2.Distance(s.OrderAnchor, a.Goal) < .2f)
            {
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
            }

            if (a.Status != AssignmentStatus.Blocked)
            {
                a.Failures++;
                st.LocalFailures++;
            }

            a.Status = AssignmentStatus.Blocked;
            a.Reason = reason;
            a.Active = false;
            a.OwnedRoute = null;
            a.FailedGoal = failedGoal;
            a.FailedUntil = b.Time + 10;
            var p = Pair(q, a.PairId);
            if (p != null)
            {
                p.NeedsReview = true;
                p.NextReview = Math.Min(p.NextReview, b.Time + 1);
                p.Failures++;
                if (p.Mover == s.Id)
                    p.Mover = -1;
            }

            q.NextPlan = 0;
        }

        static bool Special(BattleSimulation b, Soldier s)
        {
            return ProgressRecovery.Active(b, s) || ContactMovement.Active(b, s) || ContactMovement.Supporting(b, s) || !PositionLogic.ShelterEligible(b, s) || SniperTactics.OwnsSpecialPost(b, s) || ExecutionReview.ProtectedTask(b, s) || s.ReloadUntil > b.Time;
        }

        public static void Observe(BattleSimulation b, Soldier s)
        {
            var a = s.Assignment;
            if (a.Active && !AssignmentCurrent(b, s))
                return;
            if (a.PairId < 0 || b.Time < a.NextSample)
                return;
            float dt = a.NextSample == 0 ? 0 : Math.Min(1, b.Time - a.LastSample);
            a.NextSample = b.Time + .5f;
            a.LastSample = b.Time;
            if (!a.Active)
            {
                if (a.Status == AssignmentStatus.Blocked)
                    a.BlockedSeconds += dt;
                return;
            }

            if (Special(b, s))
            {
                ReleaseAssignment(b, s, "Приоритетная задача прервала назначение двойки", true);
                a.InterruptedSeconds += dt;
                return;
            }

            if (a.Status == AssignmentStatus.Interrupted)
            {
                ReleaseAssignment(b, s, "Прерванное назначение завершено", false);
                return;
            }

            // Existing specialist/safety executors may take the route temporarily. Never
            // credit their different destination as progress on this assignment.
            if (a.IsBound && V2.Distance(s.OrderAnchor, a.Goal) > .8f)
            {
                TransferAssignment(b, s, "Маршрут передан другому исполнителю");
                return;
            }

            bool progress = V2.Distance(a.LastPosition, s.Position) > .4f;
            if (progress)
            {
                a.LastPosition = s.Position;
                a.ProgressAt = b.Time;
            }

            if (s.PathCursor < s.Path.Count)
            {
                a.Status = s.MoveAuthorized ? AssignmentStatus.Executing : AssignmentStatus.Waiting;
                if (s.MoveAuthorized)
                    a.ExecutingSeconds += dt;
                else
                    a.WaitingSeconds += dt;
                if (b.Time - a.ProgressAt > 12 && b.Time - s.LastIncoming > 3)
                {
                    Failed(b, s, "Нет фактического продвижения по назначению двойки");
                    return;
                }
            }
            else if (V2.Distance(s.Position, a.Goal) < 1.1f)
            {
                bool useful = AssignmentUseful(b, s);
                a.Status = useful ? AssignmentStatus.Holding : AssignmentStatus.Waiting;
                if (useful)
                    a.UsefulSeconds += dt;
                else
                    a.WaitingSeconds += dt;
                if (!a.Active)
                    return;
                if (a.IsBound)
                {
                    a.Completed++;
                    b.Team(s).CommandPlan.Completed++;
                    a.Status = AssignmentStatus.Completed;
                    a.Active = false;
                    a.OwnedRoute = null;
                    var p = Pair(b.Team(s), a.PairId);
                    if (p != null)
                    {
                        p.Completed++;
                        p.LastMover = s.Id;
                        if (p.Mover == s.Id)
                            p.Mover = -1;
                        p.NextBound = b.Time + (b.Team(s).Style == CombatStyle.Aggressive ? .65f : 1.2f);
                    }
                }
            }
            else if (b.Time > a.Deadline)
            {
                Failed(b, s, "Назначение не достигнуто за отведённое время");
            }

            a.LastShots = s.Shots;
        }

        public static bool AssignmentUseful(BattleSimulation b, Soldier s)
        {
            var a = s.Assignment;
            var q = b.Team(s);
            var p = Pair(q, a.PairId);
            if (p == null)
                return false;
            if (p.Duty == PairDuty.Support)
            {
                foreach (var other in q.CommandPlan.Pairs)
                    if (other.SupportPair == p.Id)
                        for (int memberIndex7 = 0; memberIndex7 < 2; memberIndex7++)
                        {
                            int id = memberIndex7 == 0 ? other.A : other.B;
                            var mover = Unit(b, id);
                            if (Member(mover, q) && mover != s && PositionLogic.UsefulSupport(b, s, mover, mover.Destination))
                                return true;
                        }

                return false;
            }

            if (StraightAdvance.Waiting(b, s))
                return true;
            if (p.Duty == PairDuty.Guard && Threat(b, q) == null)
                return V2.Distance(s.Position, p.Goal) <= 6;
            var partner = Unit(b, p.Mover);
            if (Member(partner, q) && partner != s && partner.PathCursor < partner.Path.Count && PositionLogic.UsefulSupport(b, s, partner, partner.Destination))
                return true;
            return ExecutionReview.WorkingSector(b, s) || b.Time - s.LastFire < 3;
        }

        // A deliberate lateral leg may leave a firing post, but only the assigned
        // mover owns it; ordinary approach still preserves a useful firing position.
        public static bool ManeuverNeeded(BattleSimulation b, Soldier s, V2 destination, bool assignedOnly)
        {
            var q = b.Team(s);
            if (!Owns(q) || q.Style == CombatStyle.Passive || q.CommandTransition.Waiting)
                return false;
            var p = Pair(q, s.Assignment.PairId);
            if (p == null || p.Duty != PairDuty.Maneuver)
                return false;
            if (assignedOnly && p.Mover != s.Id && !(s.Assignment.Active && s.Assignment.IsBound && s.Assignment.PairId == p.Id))
                return false;
            var lateral = TacticalOrders.Lateral(b);
            float target = V2.Dot(p.Goal - b.Map.Entry, lateral), current = V2.Dot(s.Position - b.Map.Entry, lateral);
            float sign = target < 0 ? -1 : 1;
            float desired = Math.Min(4, Math.Abs(target));
            return current * sign < desired - .5f && V2.Dot(destination - s.Position, lateral) * sign > .3f;
        }

        // Ordinary position optimisation must retain the player's maneuver lane.
        // Emergency shelter, grenade escape and casualty evacuation remain separate.
        public static bool WithinManeuver(BattleSimulation b, Soldier s, V2 point)
        {
            var q = b.Team(s);
            if (q.Side != Side.Green || !Owns(q) || q.Style == CombatStyle.Passive || q.Style == CombatStyle.Withdraw || q.Style == CombatStyle.EmergencyWithdraw || q.CommandTransition.Waiting)
                return true;
            var p = Pair(q, s.Assignment.PairId);
            if (p == null || p.Duty != PairDuty.Maneuver || p.IntentKey != (int)q.Intent || p.Converging || p.ManeuverSide == 0)
                return true;
            var outward = TacticalOrders.Lateral(b) * p.ManeuverSide;
            float current = V2.Dot(s.Position - b.Map.Entry, outward);
            float target = V2.Dot(p.Goal - b.Map.Entry, outward);
            return V2.Dot(point - b.Map.Entry, outward) >= Math.Min(current, target - .8f) - .25f;
        }

        static bool Covered(BattleSimulation b, Soldier s, V2 goal)
        {
            if (CommanderPriorities.SmokeSafe(b, s))
                return true;
            if (s.Suppression < .55f && s.Morale > .3f && b.Time - s.LastIncoming > 3)
            {
                var delta = goal - s.Position;
                var end = s.Position + delta.Normalized * Math.Min(3, delta.Length);
                bool safe = true;
                for (int i = 0; i <= 6; i++)
                    if (PositionLogic.Exposure(b, s, s.Position + (end - s.Position) * (i / 6f)) > .12f)
                    {
                        safe = false;
                        break;
                    }

                if (safe)
                    return true;
            }

            var q = b.Team(s);
            foreach (var ally in b.Soldiers)
                if (ally != s && q.Contains(ally) && ally.Available && !ally.Moving && ally.PathCursor >= ally.Path.Count && PositionLogic.UsefulSupport(b, ally, s, goal))
                    return true;
            return false;
        }

        static bool LimitedBoundReady(BattleSimulation b, Soldier s)
        {
            return !s.Broken && s.Suppression <= .45f && s.Morale >= .45f && s.Health >= 35 && s.ReloadUntil <= b.Time && b.Time - s.LastIncoming >= 3 && b.Team(s).Style != CombatStyle.Passive;
        }

        public static bool LimitedBoundSafe(BattleSimulation b, Soldier s, List<V2> route)
        {
            if (route == null || route.Count == 0 || !LimitedBoundReady(b, s) || !PositionLogic.Protected(b, s, route[route.Count - 1]))
                return false;
            if (SupportAI.PathLength(s.Position, route) > 5 || !PositionLogic.UsefulRoute(b, s, route))
                return false;
            return true;
        }

        public static bool MovementAllowed(BattleSimulation b, Soldier s)
        {
            if (ContactMovement.Active(b, s))
                return true;
            if (StraightAdvance.Waiting(b, s))
                return false;
            if (WallTravel.Active(b, s))
                return true;
            if (s.Assignment.SnailBound && s.Assignment.Active && !SnailMovement.CoverReady(b, s, s.Assignment.CoverId))
                return false;
            if (Covered(b, s, s.PathCursor < s.Path.Count ? s.Path[s.PathCursor] : s.Destination))
                return true;
            var a = s.Assignment;
            if (!a.Active || !a.IsBound || !a.LimitedBound || b.Time > a.Deadline || s.PathCursor >= s.Path.Count)
                return false;
            var rest = s.Path.GetRange(s.PathCursor, s.Path.Count - s.PathCursor);
            return LimitedBoundSafe(b, s, rest);
        }

        // Candidate posts and route-prefix fallback must obey the same execution
        // constraints. Otherwise a long unexecutable prefix hides a shorter safe
        // bound on the exact same route and is selected again on every review.
        static bool MaySelectBound(BattleSimulation b, Soldier s, PairCommandPlan p, List<V2> route)
        {
            return MedicTactics.AllowRoute(b, s, route) && (p.WaitingSince < 0 || b.Time - p.WaitingSince < 4 || Covered(b, s, route[0]) || LimitedBoundSafe(b, s, route));
        }

        // The guide-centred window can lie beyond every executable unsupported bound.
        // After that search fails, inspect protected stepping stones near the mover.
        // Progress is still toward the SAME route guide; this is not a blind retreat.
        static bool FindLocalBound(BattleSimulation b, Squad q, PairCommandPlan p, Soldier s, V2 routeGuide, List<KeyValuePair<float, V2>> previousCandidates, out V2 goal, out List<V2> route)
        {
            goal = s.Position;
            route = null;
            if (!LimitedBoundReady(b, s) || !PositionLogic.ShelterEligible(b, s) || !AiTasks.MayAssign(b, s))
                return false;
            var candidates = new List<KeyValuePair<float, V2>>();
            float currentDistance = V2.Distance(s.Position, routeGuide);
            for (int y = -5; y <= 5; y++)
                for (int x = -5; x <= 5; x++)
                {
                    var point = new V2((int)s.Position.X + x + .5f, (int)s.Position.Y + y + .5f);
                    float distance = V2.Distance(s.Position, point);
                    if (distance < 1 || distance > 5 || V2.Distance(point, routeGuide) >= currentDistance - .35f || !b.Map.Walkable(point) || !StraightAdvance.Inside(b, s, point) || q.Side == Side.Red && !ProgressRecovery.WithinAssignment(b, s, point) || PositionLogic.Occupied(b, s, point) || s.Assignment.FailedUntil > b.Time && V2.Distance(point, s.Assignment.FailedGoal) < 1.3f || !PositionLogic.Protected(b, s, point))
                        continue;
                    bool alreadyTried = false;
                    for (int i = 0; i < Math.Min(4, previousCandidates.Count); i++)
                        if (V2.Distance(previousCandidates[i].Value, point) < .001f)
                        {
                            alreadyTried = true;
                            break;
                        }

                    if (!alreadyTried)
                        candidates.Add(new KeyValuePair<float, V2>(distance, point));
                }

            candidates.Sort((a, z) =>
            {
                int distance = a.Key.CompareTo(z.Key);
                if (distance != 0)
                    return distance;
                int row = a.Value.Y.CompareTo(z.Value.Y);
                return row != 0 ? row : a.Value.X.CompareTo(z.Value.X);
            });
            int budget = 4;
            foreach (var candidate in candidates)
            {
                var path = LossMemory.Find(b, s, candidate.Value, x => PositionLogic.Exposure(b, s, x) * 4 + FireLaneSafety.PathCost(b, s, x) + (PositionLogic.Occupied(b, s, x) ? 15 : 0));
                b.Stats.Paths++;
                if (LimitedBoundSafe(b, s, path) && MaySelectBound(b, s, p, path))
                {
                    goal = candidate.Value;
                    route = path;
                    return true;
                }

                if (--budget == 0)
                    break;
            }

            return false;
        }

        static bool FindBound(BattleSimulation b, Squad q, PairCommandPlan p, Soldier s, out V2 goal, out List<V2> route)
        {
            goal = s.Position;
            route = null;
            var target = (q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front) ? StraightAdvance.Goal(b, s) : p.Goal;
            target = GreenExperiments.Target(b, q, p, s, target);
            if (p.Duty == PairDuty.Guard)
            {
                var known = TacticalOrders.ClosestKnown(b, s);
                if (known != null)
                    target = p.Goal + (known.Position - p.Goal).Normalized * 3.5f;
            }

            if (V2.Distance(target, s.Position) < 2 && (p.Duty == PairDuty.Guard || p.Duty == PairDuty.Support))
            {
                var threat = TacticalOrders.ClosestKnown(b, s);
                if (threat != null)
                    target = s.Position + (threat.Position - s.Position).Normalized * 3;
            }

            // A forward pair can reach its staging point without gaining a firing lane.
            // Look one short bound farther; ordinary cover, partner and route checks below still apply.
            if (q.Side == Side.Green && (p.Duty == PairDuty.Advance || p.Duty == PairDuty.Maneuver) && q.Style != CombatStyle.Withdraw && V2.Distance(s.Position, target) < 3.25f && b.Time - s.LastFire > 6 && !ExecutionReview.WorkingSector(b, s) && !StraightAdvance.Waiting(b, s))
            {
                var axis = b.Map.Objective - target;
                if (axis.Length > 2)
                    target = TacticalOrders.Open(b, target + axis.Normalized * Math.Min(3, axis.Length));
            }

            if (BoundDetour.TryRoute(b, s, target, false, out goal, out route))
                return true;
            // Route to the group goal first; use its reachable short waypoint around walls.
            var longPath = LossMemory.Find(b, s, target, x => PositionLogic.Exposure(b, s, x) * 3 + FireLaneSafety.PathCost(b, s, x));
            b.Stats.Paths++;
            if (longPath.Count == 0)
                return false;
            List<V2> wallRoute;
            if (WallTravel.TryRoute(b, s, longPath, out wallRoute) && MedicTactics.AllowRoute(b, s, wallRoute))
            {
                goal = wallRoute[wallRoute.Count - 1];
                route = wallRoute;
                return true;
            }

            V2 guide = s.Position, prev = s.Position;
            float len = 0;
            foreach (var v in longPath)
            {
                len += V2.Distance(prev, v);
                guide = v;
                prev = v;
                if (len >= MotionCombat.Recommended(q))
                    break;
            }

            var routeGuide = guide;
            if (!q.CommandPlan.LastDuo && V2.Distance(s.Position, target) > 7)
                guide = FormationAI.Desired(b, s, guide, p.Duty == PairDuty.Maneuver);
            var candidates = new List<KeyValuePair<float, V2>>();
            int searchRadius = GreenExperiments.SearchRadius(q, p);
            for (int y = -searchRadius; y <= searchRadius; y++)
                for (int x = -searchRadius; x <= searchRadius; x++)
                {
                    V2 v = new V2((int)guide.X + x + .5f, (int)guide.Y + y + .5f);
                    float d = V2.Distance(s.Position, v);
                    if (d < 1 || d > MotionCombat.Maximum(q) || V2.Distance(v, routeGuide) >= V2.Distance(s.Position, routeGuide) - .35f || !StraightAdvance.Inside(b, s, v) || !b.Map.Walkable(v) || PositionLogic.Occupied(b, s, v) || s.Assignment.FailedUntil > b.Time && V2.Distance(v, s.Assignment.FailedGoal) < 1.3f)
                        continue;
                    if (d < 3.5f && PositionLogic.Protected(b,s,s.Position) && PositionLogic.HasSector(b,s,s.Position) && !PositionLogic.UsefulLocalChange(b,s,v))
                        continue;
                    if (q.Side == Side.Red && !ProgressRecovery.WithinAssignment(b, s, v))
                        continue;
                    bool cover = PositionLogic.Protected(b, s, v);
                    float risk = PositionLogic.Exposure(b, s, v);
                    bool supported = false;
                    if (!cover && risk > .12f && risk <= .32f)
                        foreach (var ally in b.Soldiers)
                            if (ally != s && q.Contains(ally) && ally.GunMachineGunner && b.Time - ally.LastFire < 2
                                && PositionLogic.UsefulSupport(b, ally, s, v)) { supported = true; break; }
                    if (!cover && risk > .12f && !supported)
                        continue;
                    float score = -V2.Distance(v, guide) * 1.5f - d * .1f - risk * 7 + PositionLogic.FightingPostBonus(b,s,v);
                    score += GreenExperiments.Spacing(b, q, s, v);
                    score -= CenterCorridor.Penalty(b, s, v) + FormationEnvelope.LaneCost(b, s, v) + FormationEnvelope.ShuffleCost(b, s, v);
                    candidates.Add(new KeyValuePair<float, V2>(score, v));
                }

            candidates.Sort((a, z) => z.Key.CompareTo(a.Key));
            int budget = 4;
            foreach (var candidate in candidates)
            {
                var path = LossMemory.Find(b, s, candidate.Value, x => PositionLogic.Exposure(b, s, x) * 4 + FireLaneSafety.PathCost(b, s, x) + (PositionLogic.Occupied(b, s, x) ? 15 : 0));
                b.Stats.Paths++;
                if (path.Count > 0 && SupportAI.PathLength(s.Position, path) <= MotionCombat.Maximum(q) + 2 && PositionLogic.UsefulRoute(b, s, path) && MaySelectBound(b, s, p, path))
                {
                    goal = candidate.Value;
                    route = path;
                    return true;
                }

                if (--budget == 0)
                    break;
            }

            // Cover candidates may all require a long detour around the same wall.
            // Preserve the already found route instead of retrying those same candidates forever.
            var prefix = new List<V2>();
            prev = s.Position;
            len = 0;
            foreach (var point in longPath)
            {
                len += V2.Distance(prev, point);
                if (len > MotionCombat.Maximum(q))
                    break;
                prefix.Add(point);
                prev = point;
            }

            while (prefix.Count > 0)
            {
                var end = prefix[prefix.Count - 1];
                if (V2.Distance(s.Position, end) >= .8f && !PositionLogic.Occupied(b, s, end) && (q.Side != Side.Red || ProgressRecovery.WithinAssignment(b, s, end)) && StraightAdvance.Inside(b, s, end) && !(s.Assignment.FailedUntil > b.Time && V2.Distance(end, s.Assignment.FailedGoal) < 1.3f) && PositionLogic.UsefulRoute(b, s, prefix) && MaySelectBound(b, s, p, prefix) && (PositionLogic.Protected(b, s, end) || PositionLogic.Exposure(b, s, end) <= .12f))
                {
                    goal = end;
                    route = prefix;
                    return true;
                }

                prefix.RemoveAt(prefix.Count - 1);
            }

            if (FindLocalBound(b, q, p, s, routeGuide, candidates, out goal, out route))
                return true;
            // Forward-only candidate scoring has exhausted its legal choices. A
            // backward bound is permitted only along a complete verified bypass.
            return BoundDetour.TryRoute(b, s, target, true, out goal, out route);
        }

        static void Assign(BattleSimulation b, Squad q, PairCommandPlan p, Soldier s, Job job, V2 goal, List<V2> path, bool authorized)
        {
            if (!AiTasks.MayAssign(b, s))
            {
                s.Assignment.Rejected++;
                q.CommandPlan.Rejected++;
                return;
            }

            if (path == null && s.Assignment.Active && !s.Assignment.IsBound && s.Assignment.Duty == p.Duty && V2.Distance(s.Assignment.Goal, goal) < .2f)
                return;
            if (!SquadBrain.GiveOrder(b, q, s, job, goal, authorized))
            {
                s.Assignment.Rejected++;
                q.CommandPlan.Rejected++;
                return;
            }

            if (V2.Distance(s.OrderAnchor, goal) > .15f)
            {
                // GiveOrder may accept a protected hold instead of the requested advance.
                // Keep a still-consistent unchanged contract, but retire one whose goal was replaced.
                var previous = s.Assignment;
                if (previous.Active && V2.Distance(previous.Goal, s.OrderAnchor) > .15f)
                {
                    ReleaseAssignment(b, s, "Принятый приказ изменил цель назначения", false);
                    p.NeedsReview = true;
                    p.NextReview = 0;
                    if (p.Mover == s.Id)
                        p.Mover = -1;
                    TacticalAI.Stop(s);
                }

                return;
            }

            var a = s.Assignment;
            a.Active = true;
            a.IsBound = path != null;
            a.LimitedBound = false;
            a.SnailBound = false;
            a.CoverId = -1;
            a.OwnedRoute = path;
            a.PairId = p.Id;
            a.Leader = p.Leader;
            a.PlanGeneration = q.CommandPlan.Generation;
            a.PairRevision = p.Revision;
            a.Revision++;
            a.Assignments++;
            q.CommandPlan.Assignments++;
            a.Duty = p.Duty;
            a.Goal = goal;
            a.LastPosition = s.Position;
            a.ProgressAt = a.Since = b.Time;
            a.Deadline = b.Time + 18;
            a.Status = path == null ? AssignmentStatus.Holding : AssignmentStatus.Executing;
            a.Reason = p.Reason;
            a.Owner = "Двойка " + p.Id + ", старший " + p.Leader;
            if (path != null)
            {
                s.Path = path;
                s.PathCursor = 0;
                s.LastMoved = b.Time;
                s.MoveAuthorized = authorized;
                s.ManeuverPaused = false;
                p.Mover = s.Id;
            }

            s.Order = "Задача двойки: " + p.Reason;
            s.Reason = a.Reason;
            b.History.Event(b, "pairAssignment", p.Leader, s.Id, s.Position, goal, p.Id, a.Reason);
        }

        static void ExecutePair(BattleSimulation b, Squad q, PairCommandPlan p)
        {
            if (q.Style == CombatStyle.Passive)
                return;
            if (b.Time < p.NextReview)
                return;
            p.NextReview = b.Time + (q.Style == CombatStyle.Aggressive ? 1f : 1.5f);
            var st = q.CommandPlan;
            var current = Unit(b, p.Mover);
            for (int memberIndex8 = 0; memberIndex8 < 2; memberIndex8++)
            {
                int id = memberIndex8 == 0 ? p.A : p.B;
                var unit = Unit(b, id);
                if (Member(unit, q))
                {
                    if (unit.Assignment.Active)
                        AssignmentCurrent(b, unit);
                    if (!Special(b, unit))
                        StraightAdvance.Hold(b, unit);
                }
            }

            if (Member(current, q) && current.Assignment.Active && current.PathCursor < current.Path.Count && !Special(b, current))
            {
                current.MoveAuthorized = MovementAllowed(b, current);
            }

            if (current == null || !current.Assignment.Active || !MotionCombat.HasRoute(current))
                p.Mover = -1;
            var members = new List<Soldier>();
            for (int memberIndex9 = 0; memberIndex9 < 2; memberIndex9++)
            {
                int id = memberIndex9 == 0 ? p.A : p.B;
                var s = Unit(b, id);
                if (Member(s, q))
                    members.Add(s);
            }

            foreach (var s in members)
                if (s.Assignment.Active && s.Assignment.Status == AssignmentStatus.Blocked)
                    s.Assignment.Active = false;
            if (b.Time < p.NextBound)
                return;
            foreach (var member in members)
                if (member.PathCursor < member.Path.Count && !Special(b, member))
                    member.MoveAuthorized = MovementAllowed(b, member);
            if (GroupMovement.Count(b, q) >= GroupMovement.Maximum)
                return;
            Soldier chosen = null;
            foreach (var s in members)
            {
                if (!Free(b, s) || s.PathCursor < s.Path.Count || StraightAdvance.Waiting(b, s))
                    continue;
                if ((p.Duty == PairDuty.Guard || p.Duty == PairDuty.Support) && AssignmentUseful(b, s))
                    continue;
                if (p.Duty == PairDuty.Reserve && V2.Distance(s.Position, p.Goal) < 4)
                    continue;
                if (q.Intent != ManeuverIntent.Straight && q.Intent != ManeuverIntent.Front && !CommandPosture.ForwardNeeded(b, s, p.Goal, false) && !ManeuverNeeded(b, s, p.Goal, false) && FiringPositionPolicy.ShouldHold(b, s, Job.Reposition, p.Goal))
                    continue;
                int previous = p.LastTried >= 0 ? p.LastTried : p.LastMover;
                if (chosen == null || previous >= 0 && chosen.Id == previous && s.Id != previous || previous < 0 && FormationAI.MovePriority(q, s) < FormationAI.MovePriority(q, chosen))
                    chosen = s;
            }

            if (chosen == null)
                return;
            if (GreenExperiments.WaitForPartner(b, q, p, chosen))
                return;
            if (chosen.Assignment.Status == AssignmentStatus.Blocked && chosen.PathCursor >= chosen.Path.Count)
            {
                chosen.MoveAuthorized = true;
                chosen.Job = Job.Hold;
                chosen.OrderAnchor = chosen.Destination = chosen.Position;
            }

            V2 goal;
            List<V2> route;
            int snailCover;
            bool snail = SnailMovement.TryRoute(b, q, p, chosen, out goal, out route, out snailCover);
            if (!snail && !FindBound(b, q, p, chosen, out goal, out route))
            {
                st.NoRoute++;
                p.Failures++;
                p.LastTried = chosen.Id;
                p.NextBound = b.Time + 3;
                return;
            }

            p.LastTried = -1;
            bool wall = WallTravel.Safe(b, chosen, goal);
            bool covered = wall || Covered(b, chosen, route.Count > 0 ? route[0] : goal);
            bool limited = !covered && p.WaitingSince >= 0 && b.Time - p.WaitingSince >= 4 && LimitedBoundSafe(b, chosen, route);
            if (limited)
                covered = true;
            if (!covered)
            {
                st.DependencyWaits++;
                if (p.WaitingSince < 0)
                    p.WaitingSince = b.Time;
                if (b.Time - p.WaitingSince >= 6)
                {
                    SmokeTactics.Request(b, q, chosen, goal, SmokePurpose.Route);
                    p.NeedsReview = true;
                    p.NextBound = b.Time + 3;
                }

                if (b.Time - p.WaitingSince >= 12)
                {
                    st.PairFailures++;
                    p.Reason = "Прикрытие недоступно: требуется другое локальное решение";
                    p.WaitingSince = b.Time;
                    p.NextBound = b.Time + 5;
                    foreach (var supporter in st.Pairs)
                        if (supporter.Id == p.SupportPair)
                        {
                            supporter.Goal = Center(b, q, p) - TacticalOrders.Axis(b, q.Side) * 2;
                            supporter.NextReview = 0;
                            supporter.NextBound = 0;
                            supporter.NeedsReview = true;
                        }
                }

                return;
            }

            foreach (var partner in members)
                if (partner != chosen && Free(b, partner) && partner.PathCursor >= partner.Path.Count)
                {
                    Assign(b, q, p, partner, partner.Id == q.LeaderId ? Job.Command : Job.Cover, partner.Position, null, true);
                }

            if (!GroupMovement.CanStart(b, chosen, goal, limited ? route : null))
                return;
            p.Mover = chosen.Id;
            Assign(b, q, p, chosen, p.Duty == PairDuty.Maneuver ? Job.Flank : Job.Reposition, goal, route, true);
            if (!chosen.Assignment.Active || !chosen.Assignment.IsBound || V2.Distance(chosen.Assignment.Goal, goal) > .2f || !object.ReferenceEquals(chosen.Path, route) || !object.ReferenceEquals(chosen.Assignment.OwnedRoute, route))
            {
                p.Mover = -1;
                return;
            }

            if (p.WaitingSince >= 0)
            {
                st.DependencyReleases++;
                p.WaitingSince = -1;
            }

            if (chosen.Tactical.Detour.Active)
            {
                chosen.Assignment.Reason = chosen.Reason = "Обход по проверенному маршруту к цели группы";
                chosen.Order = "Задача двойки: " + chosen.Reason;
            }

            if (wall)
                WallTravel.Mark(b, chosen, goal);
            if (current != null && MotionCombat.HasRoute(current) && current != chosen)
                chosen.Motion.ExtraBounds++;
            if (snail)
            {
                st.SnailBounds++;
                chosen.Assignment.SnailBound = true;
                chosen.Assignment.CoverId = snailCover;
                chosen.Assignment.Deadline = b.Time + 22;
                chosen.Assignment.Reason = chosen.Reason = "Улитка: длинная перебежка за спиной прикрывающего";
            }
            else if (limited)
            {
                chosen.Assignment.LimitedBound = true;
                chosen.Assignment.Deadline = b.Time + 8;
                st.LimitedBounds++;
                chosen.Assignment.Reason = chosen.Reason = "Прикрытие не подтверждено: ограниченное смещение в проверенное укрытие";
            }

            p.NextBound = b.Time + (q.Style == CombatStyle.Aggressive ? 1f : 2f);
            if (q.Style == CombatStyle.Aggressive && p.Mover >= 0)
                q.Posture.ForwardBounds++;
            p.NeedsReview = false;
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            Begin(b, q);
            if (!Owns(q))
                return false;
            if (TacticalMarch.Plan(b, q))
                return true;
            q.TaskReplanRequested = false;
            q.Phase = q.Style == CombatStyle.Passive ? ManeuverPhase.Hold : ManeuverPhase.Move;
            q.RequiredSupport = 1;
            q.Initialized = q.PriorityInitialized = true;
            q.SupportIds.Clear();
            q.ManeuverIds.Clear();
            foreach (var p in q.CommandPlan.Pairs)
                if (Live(b, q, p))
                {
                    ExecutePair(b, q, p);
                    foreach (int id in new[]
                    {
                        p.A,
                        p.B
                    }

                    )
                        if (Member(Unit(b, id), q))
                        {
                            if (Unit(b, id).Assignment.Active && Unit(b, id).Assignment.IsBound && MotionCombat.HasRoute(Unit(b, id)))
                                q.ManeuverIds.Add(id);
                            else
                                q.SupportIds.Add(id);
                        }
                }

            q.Plan = q.Execution = q.CommanderOrder = q.CommandPlan.Scheme;
            return true;
        }

        public static void Verify(BattleSimulation b)
        {
            VerifyGroup(b, b.Green);
            if (!b.CustomDefenders)
                VerifyGroup(b, b.Red);
            foreach (var g in b.DefenderGroups)
                if (g.Deployed)
                    VerifyGroup(b, g.Command);
        }

        static void VerifyGroup(BattleSimulation b, Squad q)
        {
            var seen = new HashSet<int>();
            foreach (var p in q.CommandPlan.Pairs)
            {
                for (int memberIndex10 = 0; memberIndex10 < 2; memberIndex10++)
                {
                    int id = memberIndex10 == 0 ? p.A : p.B;
                    var s = Unit(b, id);
                    if (!Member(s, q))
                        continue;
                    if (!seen.Add(id))
                        throw new InvalidOperationException("Duplicate pair owner: " + id);
                    if (s.Assignment.PairId != p.Id)
                        throw new InvalidOperationException("Pair contract mismatch: " + id);
                }

                if (p.SupportPair == p.Id)
                    throw new InvalidOperationException("Self-dependent pair: " + p.Id);
                var support = Pair(q, p.SupportPair);
                if (support != null && support.SupportPair == p.Id)
                    throw new InvalidOperationException("Cyclic pair support");
            }
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (!Owns(q) || Special(b, s))
            {
                if (s.Assignment.Active)
                    ReleaseAssignment(b, s, "Приоритетная задача получила управление", true);
                return false;
            }

            if (s.Assignment.Active && !AssignmentCurrent(b, s))
                return false;
            var a = s.Assignment;
            if (!a.Active)
            {
                if (s.PathCursor >= s.Path.Count && a.Status == AssignmentStatus.Blocked)
                {
                    s.MoveAuthorized = true;
                    s.OrderAnchor = s.Destination = s.Position;
                    s.Job = Job.Hold;
                    s.Reason = "Задача возвращена старшему двойки; оцениваю доступные варианты";
                    return false;
                }

                return false;
            }

            if (s.PathCursor < s.Path.Count)
            {
                s.MoveAuthorized = MovementAllowed(b, s);
                s.Reason = a.Reason;
                return true;
            }

            if (a.Status == AssignmentStatus.Holding)
            {
                s.Reason = AssignmentUseful(b, s) ? "Исполняю назначение двойки" : "Проверяю полезность назначенного сектора";
                return true;
            }

            return false;
        }
    }
}
