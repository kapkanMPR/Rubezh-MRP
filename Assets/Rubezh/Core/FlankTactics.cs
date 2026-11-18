using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class FlankPlan
    {
        public bool Active;
        public ManeuverIntent Mode;
        public readonly Dictionary<int, int> Wing = new Dictionary<int, int>();
        public readonly HashSet<int> FixedSupport = new HashSet<int>();
        public int CommanderWing = -1;
        // Rapid crossing is a brief allowance only when changing between flank variants.
        // Every other maneuver uses ordinary covered bounds immediately.
        public float FastTransferEndsAt;
        public float NextCommanderSwitch;
        public readonly int[] LocalMover =
        {
            -1,
            -1
        };
        public readonly float[] NextBound =
        {
            0,
            0
        };
        public readonly Dictionary<int, int> PairMover = new Dictionary<int, int>();
        public readonly Dictionary<int, float> PairNextBound = new Dictionary<int, float>();
        public void Clear()
        {
            PairMover.Clear();
            PairNextBound.Clear();
            FastTransferEndsAt = 0;
            Active = false;
            Wing.Clear();
            FixedSupport.Clear();
            CommanderWing = -1;
            NextCommanderSwitch = 0;
            LocalMover[0] = LocalMover[1] = -1;
            NextBound[0] = NextBound[1] = 0;
        }
    }

    public static class FlankTactics
    {
        static bool Mobile(Soldier s)
        {
            return TacticalOrders.CanMove(s);
        }

        public static int SideOf(Squad q, int id)
        {
            if (id == q.LeaderId)
                return q.FlankPlan.CommanderWing;
            return q.FlankPlan.Wing.TryGetValue(id, out var side) ? side : 0;
        }

        static bool Envelopment(Squad q)
        {
            return q.Intent == ManeuverIntent.LeftHold || q.Intent == ManeuverIntent.RightHold;
        }

        static int TargetSide(Squad q)
        {
            return q.Intent == ManeuverIntent.Right || q.Intent == ManeuverIntent.RightHold ? 1 : -1;
        }

        static List<Soldier> Members(BattleSimulation b, Squad q, int side, bool leader = true)
        {
            var list = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Mobile(s) && (leader || s.Id != q.LeaderId) && SideOf(q, s.Id) == side)
                    list.Add(s);
            list.Sort((a, z) => a.Id.CompareTo(z.Id));
            return list;
        }

        static float PairScore(Soldier a, Soldier z)
        {
            float score = a.Role == z.Role ? -30 : 12;
            if (a.Role == Role.MachineGunner && z.Role == Role.MachineGunner)
                score -= 60;
            if (a.Role == Role.Medic && z.Role == Role.Medic)
                score -= 50;
            if (a.Sniper && z.Sniper)
                score -= 50;
            if (a.SupplyPack && z.Role == Role.MachineGunner || z.SupplyPack && a.Role == Role.MachineGunner)
                score += 22;
            return score - V2.Distance(a.Position, z.Position) * .15f;
        }

        static void AssignPairs(BattleSimulation b, Squad q)
        {
            var plan = q.FlankPlan;
            plan.Wing.Clear();
            var people = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Mobile(s) && s.Id != q.LeaderId)
                    people.Add(s);
            // Manual preferences are a PRIORITY, not an all-or-nothing 4-man constraint.
            // A casualty must never reset a surviving preferred pair to random partners.
            var saved = q.Formation;
            if (saved != null && saved.Valid() && saved.pairing != 0 && q.Side == Side.Green)
            {
                // Explicit four-slot mode has fixed left/right wings; older numeric
                // presets retain their historical side chosen from starting geography.
                int leftWing = saved.pairing == 4 ? -1 : people.Count == 0 ? -1 : V2.Dot(people[0].Position - b.Map.Entry, TacticalOrders.Lateral(b)) <= 0 ? -1 : 1;
                foreach (var member in people)
                {
                    int leftSlot = saved.DisplaySlot(0), leftPartner = saved.DisplaySlot(1), rightSlot = saved.DisplaySlot(2), rightPartner = saved.DisplaySlot(3);
                    if (member.Id == leftSlot || member.Id == leftPartner)
                        plan.Wing[member.Id] = leftWing;
                    else if (member.Id == rightSlot || member.Id == rightPartner)
                        plan.Wing[member.Id] = -leftWing;
                }

                if (plan.Wing.Count == people.Count)
                {
                    // When only one survivor remains on each flank, let them form a real
                    // two-person element rather than wait for a dead preferred partner.
                    if (people.Count == 2 && plan.Wing[people[0].Id] != plan.Wing[people[1].Id])
                    {
                        int wing = V2.Distance(people[0].Position, b.Map.Objective) < V2.Distance(people[1].Position, b.Map.Objective) ? plan.Wing[people[0].Id] : plan.Wing[people[1].Id];
                        plan.Wing[people[0].Id] = plan.Wing[people[1].Id] = wing;
                    }

                    return;
                }

                // Legacy/incomplete data falls back to automatic selection below.
                plan.Wing.Clear();
            }

            // Four followers: inspect all three pairings instead of a greedy pairing that
            // can leave two identical specialists in the other pair.
            if (people.Count == 4)
            {
                int best = 1;
                float bestScore = float.MinValue;
                for (int j = 1; j < 4; j++)
                {
                    var rest = new List<int>();
                    for (int k = 1; k < 4; k++)
                        if (k != j)
                            rest.Add(k);
                    float score = PairScore(people[0], people[j]) + PairScore(people[rest[0]], people[rest[1]]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = j;
                    }
                }

                V2 lateral = TacticalOrders.Lateral(b);
                V2 first = (people[0].Position + people[best].Position) * .5f, second = new V2();
                for (int i = 1; i < 4; i++)
                    if (i != best)
                        second += people[i].Position;
                second *= .5f;
                int side = V2.Dot(first - second, lateral) <= 0 ? -1 : 1;
                for (int i = 0; i < 4; i++)
                    plan.Wing[people[i].Id] = i == 0 || i == best ? side : -side;
            }
            else
            {
                int side = -1;
                while (people.Count > 0)
                {
                    var a = people[0];
                    people.RemoveAt(0);
                    plan.Wing[a.Id] = side;
                    int chosen = -1;
                    float score = float.MinValue;
                    for (int i = 0; i < people.Count; i++)
                    {
                        float s = PairScore(a, people[i]);
                        if (s > score)
                        {
                            score = s;
                            chosen = i;
                        }
                    }

                    if (chosen >= 0)
                    {
                        plan.Wing[people[chosen].Id] = side;
                        people.RemoveAt(chosen);
                    }

                    side = -side;
                }
            }
        }

        public static void Configure(BattleSimulation b, Squad q, ManeuverIntent oldIntent, bool retain)
        {
            var st = q.FlankPlan;
            bool changedFlank = retain && TacticalOrders.IsFlank(oldIntent) && TacticalOrders.IsFlank(q.Intent) && oldIntent != q.Intent;
            st.FastTransferEndsAt = changedFlank ? b.Time + 2.5f : 0f;
            if (!retain || !st.Active || (q.Intent == ManeuverIntent.Both || Envelopment(q)) && oldIntent != q.Intent)
                AssignPairs(b, q);
            st.Active = true;
            st.Mode = q.Intent;
            st.FixedSupport.Clear();
            q.SupportIds.Clear();
            q.ManeuverIds.Clear();
            int target = TargetSide(q);
            if (Envelopment(q))
            {
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && Mobile(s) && SideOf(q, s.Id) == -target)
                        st.FixedSupport.Add(s.Id);
            // When entering directly without separated wings, the initial role-diverse
            // assignment still supplies a whole opposite pair (never a weapon-priority sort).
            }
            else if (q.Intent != ManeuverIntent.Both)
            {
                var opposite = new List<Soldier>();
                int receiving = 0;
                // Snapshot BEFORE updating wing assignments; otherwise the entire squad
                // appears to be on the destination flank even though they have not moved.
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && Mobile(s))
                    {
                        // Sniper repositions using a separate fire-lane planner; do not force
                        // him into the generic fast crossing or count him as ready flank cover.
                        if (SideOf(q, s.Id) != target)
                        {
                            if (!s.Sniper)
                                opposite.Add(s);
                        }
                        else if (!s.Sniper && !s.Broken && s.Suppression < .70f && s.Morale >= .25f && s.Ammo + s.Reserve > 0)
                            receiving++;
                    }

                bool fast = changedFlank && receiving > 0 && receiving >= opposite.Count;
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && Mobile(s) && s.Id != q.LeaderId)
                        st.Wing[s.Id] = target;
                st.CommanderWing = target;
                // Mark the whole moving group before building ANY paths; otherwise the
                // first mover sees waiting movers as receiving support on the new wing.
                if (changedFlank)
                {
                    foreach (var s in opposite)
                        if (!TacticalOrders.ServiceActive(s) && !s.GrenadeEscape.Active && !CounterSniper.Reserved(b, s))
                            s.PriorityMovement.Transfer = true;
                    foreach (var s in opposite)
                        if (s.PriorityMovement.Transfer)
                            BeginTransfer(b, s, target, fast, opposite.Count);
                }
            }

            st.LocalMover[0] = st.LocalMover[1] = -1;
            st.NextBound[0] = st.NextBound[1] = 0;
            st.PairMover.Clear();
            st.PairNextBound.Clear();
            q.Phase = ManeuverPhase.Move;
            q.PhaseSince = b.Time;
            q.Initialized = true;
            q.PriorityInitialized = true;
            q.PriorityChanged = false;
            q.EnvelopmentActive = Envelopment(q);
            RebuildBuddies(b, q);
        }

        static void RebuildBuddies(BattleSimulation b, Squad q)
        {
            if (q.Side == Side.Green && q.CommandPlan.Pairs.Count > 0)
                return;
            var used = new HashSet<int>();
            foreach (var u in b.Soldiers)
                if (q.Contains(u))
                    u.BuddyId = u.Id;
            var form = q.Formation;
            // Manual pairing persists across wing changes, without insisting all four
            // original fighters are alive/available. The commander is still reserve.
            if (form != null && form.Valid() && form.pairing != 0)
            {
                for (int pair = 0; pair < 2; pair++)
                {
                    int a = form.DisplaySlot(pair * 2), z = form.DisplaySlot(pair * 2 + 1);
                    if (a < 0 || z < 0 || a >= b.Soldiers.Count || z >= b.Soldiers.Count)
                        continue;
                    var x = b.Soldiers[a];
                    var y = b.Soldiers[z];
                    if (Mobile(x) && Mobile(y) && SideOf(q, a) == SideOf(q, z) && a != q.LeaderId && z != q.LeaderId)
                    {
                        x.BuddyId = z;
                        y.BuddyId = a;
                        used.Add(a);
                        used.Add(z);
                    }
                }
            }

            for (int wing = -1; wing <= 1; wing += 2)
            {
                var available = Members(b, q, wing, false);
                available.RemoveAll(u => used.Contains(u.Id));
                while (available.Count > 1)
                {
                    Soldier bestA = null, bestB = null;
                    float best = float.MinValue;
                    for (int i = 0; i < available.Count; i++)
                        for (int j = i + 1; j < available.Count; j++)
                        {
                            float score = PairScore(available[i], available[j]);
                            if (score > best)
                            {
                                best = score;
                                bestA = available[i];
                                bestB = available[j];
                            }
                        }

                    bestA.BuddyId = bestB.Id;
                    bestB.BuddyId = bestA.Id;
                    available.Remove(bestA);
                    available.Remove(bestB);
                }

                // Commander replaces a missing wing-mate rather than leaving the medic
                // or another survivor indefinitely alone.
                if (available.Count == 1 && q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count)
                {
                    var leader = b.Soldiers[q.LeaderId];
                    if (Mobile(leader) && q.FlankPlan.CommanderWing == wing && leader.BuddyId == leader.Id)
                    {
                        available[0].BuddyId = leader.Id;
                        leader.BuddyId = available[0].Id;
                    }
                }
            }
        }

        static float Pressure(BattleSimulation b, Squad q, int side)
        {
            float score = 0;
            foreach (var s in Members(b, q, side, false))
            {
                score += s.Suppression * 3 + (100 - s.Health) * .012f;
                var c = TacticalOrders.ClosestKnown(b, s);
                if (c != null)
                    score += Math.Max(0, 1 - V2.Distance(s.Position, c.Position) / (b.WeaponRange(s) + 5));
            }

            return score;
        }

        static void Commander(BattleSimulation b, Squad q)
        {
            if (q.Intent != ManeuverIntent.Both)
                return;
            var commander = b.Soldiers[q.LeaderId];
            if (!Mobile(commander) || TacticalOrders.ServiceActive(commander))
                return;
            var st = q.FlankPlan;
            int left = Members(b, q, -1, false).Count, right = Members(b, q, 1, false).Count;
            int wanted = st.CommanderWing;
            bool vacancy = left == 1 && right >= 2 || right == 1 && left >= 2;
            if (left == 1 && right >= 2)
                wanted = -1;
            else if (right == 1 && left >= 2)
                wanted = 1;
            else if (b.Time >= st.NextCommanderSwitch)
            {
                float l = Pressure(b, q, -1), r = Pressure(b, q, 1);
                if (l > r + .7f)
                    wanted = -1;
                else if (r > l + .7f)
                    wanted = 1;
            }

            if (wanted != st.CommanderWing && (vacancy || b.Time >= st.NextCommanderSwitch))
            {
                st.CommanderWing = wanted;
                st.NextCommanderSwitch = b.Time + 4;
                // Commander moves independently; cancel rescue links first, without healing anyone.
                if (commander.RescueTargetId >= 0)
                    SupportAI.Release(b, commander, false);
                TacticalOrders.CancelUnit(b, commander);
                int receiving = 0;
                foreach (var u in b.Soldiers)
                    if (q.Contains(u) && u.Id != commander.Id && Mobile(u) && SideOf(q, u.Id) == wanted && !u.PriorityMovement.Transfer && !u.Broken && u.Suppression < .7f)
                        receiving++;
                // A casualty is not a change of flank maneuver: no automatic sprint.
                BeginTransfer(b, commander, wanted, false, 1);
                b.Log(q.Side, commander.Name + ": перехожу на " + (wanted < 0 ? "левый" : "правый") + " фланг, не ждать прикрытия");
            }
        }

        static void BeginTransfer(BattleSimulation b, Soldier s, int side, bool fast, int movingCount)
        {
            if (TacticalOrders.ServiceActive(s))
            {
                s.PriorityMovement.Transfer = false;
                return;
            }

            s.PriorityMovement.Transfer = true;
            s.PriorityMovement.FastTransfer = fast && b.Time < b.Team(s).FlankPlan.FastTransferEndsAt;
            s.PriorityMovement.TransferGroupSize = movingCount;
            s.PriorityMovement.WaitSince = -1;
            s.PriorityMovement.RearLegCompleted = false;
            s.PriorityMovement.DestinationWing = side;
            s.PriorityMovement.NextRoute = 0;
            // Never magically cancel reload, trauma or existing weapon cooldowns.
            s.ManeuverPaused = false;
            s.MoveAuthorized = true;
            s.Job = fast ? Job.Reposition : Job.Cover;
            s.PriorityMovement.Goal = s.Position + TacticalOrders.Lateral(b) * side * 4;
            s.Order = (fast ? "Быстро перейти на " : "Перейти боевыми перебежками на ") + (side < 0 ? "левый" : "правый") + " фланг";
            s.NextDecision = b.Time;
            TransferRoute(b, s, true);
        }

        static void EnsureLeader(BattleSimulation b, Squad q)
        {
            if (q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count && Mobile(b.Soldiers[q.LeaderId]))
                return;
            Soldier replacement = null;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Mobile(s) && (replacement == null || s.Morale > replacement.Morale))
                    replacement = s;
            if (replacement != null)
            {
                int side = SideOf(q, replacement.Id);
                q.LeaderId = replacement.Id;
                q.FlankPlan.CommanderWing = side == 0 ? -1 : side;
                q.FlankPlan.Wing.Remove(replacement.Id);
                b.Log(q.Side, replacement.Name + " принял командование флангами");
            }
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            EnsureLeader(b, q);
            if (!q.FlankPlan.Active)
                Configure(b, q, q.Intent, false);
            if (q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count)
                return true;
            Commander(b, q);
            RebuildBuddies(b, q);
            ReviewPairBounds(b, q);
            q.SupportIds.Clear();
            q.ManeuverIds.Clear();
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Mobile(s))
                {
                    if (Envelopment(q) && q.FlankPlan.FixedSupport.Contains(s.Id))
                        q.SupportIds.Add(s.Id);
                    else
                        q.ManeuverIds.Add(s.Id);
                    s.Element = SideOf(q, s.Id) < 0 ? 0 : 1;
                }

            q.EnvelopmentActive = Envelopment(q);
            q.TaskReplanRequested = false;
            // Each pair has its OWN bound clock; a wounded/reloading far wing cannot stall it.
            for (int side = -1; side <= 1; side += 2)
            {
                int index = side < 0 ? 0 : 1;
                var list = Members(b, q, side, false);
                int old = q.FlankPlan.LocalMover[index];
                // Once the commander replaces a casualty in a real pair, he also takes
                // turns moving. Excluding him made his surviving partner the eternal mover.
                if (q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count)
                {
                    var leader = b.Soldiers[q.LeaderId];
                    if (Mobile(leader) && leader.Available && !leader.Broken && SideOf(q, leader.Id) == side && leader.BuddyId != leader.Id && list.Exists(u => u.Id == leader.BuddyId))
                        list.Add(leader);
                }

                if (list.Count > 0 && b.Time >= q.FlankPlan.NextBound[index])
                {
                    // Never swap the moving half of a pair while its short bound is still
                    // being executed; otherwise both partners may move simultaneously.
                    bool currentBound = false;
                    foreach (var member in list)
                        if (member.Id == old && member.MoveAuthorized && member.PathCursor < member.Path.Count)
                        {
                            currentBound = true;
                            break;
                        }

                    if (currentBound)
                    {
                        q.FlankPlan.NextBound[index] = b.Time + 1f;
                        continue;
                    }

                    int pick = -1;
                    // Rotate between actual buddies, including a free medic. A medic should
                    // NOT remain the permanent stationary half of his pair.
                    foreach (var unit in list)
                        if (unit.Id != old && unit.Available && !unit.Broken && !unit.Sniper && (unit.Role != Role.Medic || MedicTactics.FreeToFight(b, unit)))
                        {
                            pick = unit.Id;
                            break;
                        }

                    if (pick < 0)
                        foreach (var unit in list)
                            if (unit.Available && !unit.Broken && !unit.Sniper && (unit.Role != Role.Medic || MedicTactics.FreeToFight(b, unit)))
                            {
                                pick = unit.Id;
                                break;
                            }

                    if (pick < 0)
                    {
                        q.FlankPlan.LocalMover[index] = -1;
                        q.FlankPlan.NextBound[index] = b.Time + 1;
                        continue;
                    }

                    q.FlankPlan.LocalMover[index] = pick;
                    q.FlankPlan.NextBound[index] = b.Time + 4;
                }
            }

            q.SupportStatus = Envelopment(q) ? "Противоположный фланг прикрывает независимо от оружия" : "Двойки работают самостоятельно";
            q.Plan = q.Execution = q.Intent == ManeuverIntent.Both ? "Две автономные двойки; командир — резерв" : CommanderPriorities.IntentName(q.Intent) + "; перебежки без ожидания другого фланга";
            return true;
        }

        static V2 WingCenter(BattleSimulation b, Squad q, int side, int omit)
        {
            V2 sum = new V2();
            int n = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Id != omit && Mobile(s) && SideOf(q, s.Id) == side && !s.PriorityMovement.Transfer)
                {
                    sum += s.Position;
                    n++;
                }

            if (n > 0)
                return sum / n;
            var self = b.Soldiers[omit];
            var axis = PositionLogic.Axis(b);
            var lateral = TacticalOrders.Lateral(b);
            float progress = V2.Dot(self.Position - b.Map.Entry, axis);
            return TacticalOrders.Open(b, b.Map.Entry + axis * progress + lateral * side * 7 * FormationAI.LateralMultiplier(q));
        }

        public static int MoverFor(Squad q, Soldier s)
        {
            int key = Math.Min(s.Id, s.BuddyId);
            if (s.BuddyId != s.Id && q.FlankPlan.PairMover.TryGetValue(key, out int mover))
                return mover;
            return q.FlankPlan.LocalMover[SideOf(q, s.Id) < 0 ? 0 : 1];
        }

        static void ReviewPairBounds(BattleSimulation b, Squad q)
        {
            var st = q.FlankPlan;
            foreach (var a in b.Soldiers)
                if (q.Contains(a) && Mobile(a) && a.BuddyId > a.Id && a.BuddyId < b.Soldiers.Count)
                {
                    var z = b.Soldiers[a.BuddyId];
                    if (!Mobile(z) || z.BuddyId != a.Id || SideOf(q, a.Id) != SideOf(q, z.Id) || st.FixedSupport.Contains(a.Id))
                        continue;
                    int key = a.Id;
                    st.PairMover.TryGetValue(key, out int old);
                    if (st.PairNextBound.TryGetValue(key, out float next) && b.Time < next)
                        continue;
                    if (st.PairNextBound.ContainsKey(key) && old >= 0 && old < b.Soldiers.Count)
                    {
                        var current = b.Soldiers[old];
                        if (current.Available && current.MoveAuthorized && current.PathCursor < current.Path.Count && b.Time - current.LastMoved < 2.5f)
                        {
                            st.PairNextBound[key] = b.Time + 1;
                            continue;
                        }
                    }

                    Soldier first = old == a.Id ? z : a, second = first == a ? z : a;
                    Soldier pick = BoundReady(b, first) ? first : BoundReady(b, second) ? second : null;
                    st.PairMover[key] = pick == null ? -1 : pick.Id;
                    st.PairNextBound[key] = b.Time + (pick == null ? 1 : 4);
                }
        }

        static bool BoundReady(BattleSimulation b, Soldier s)
        {
            return s.Available && !s.Broken && !s.Sniper && !TacticalOrders.ServiceActive(s) && (s.Role != Role.Medic || MedicTactics.FreeToFight(b, s));
        }

        // Fast deployment must never secretly become an offensive route: every
        // waypoint, not just the goal, must remain inside the lateral formation band.
        public static bool FreeCrossingIsLateral(BattleSimulation b, Soldier s, V2 origin)
        {
            var forward = PositionLogic.Axis(b);
            if (V2.Dot(s.Destination - origin, forward) > .7f)
                return false;
            foreach (var point in s.Path)
                if (V2.Dot(point - origin, forward) > .7f)
                    return false;
            return true;
        }

        // A fast crossing is only acceptable if a receiving group can protect
        // at least as many incoming fighters. A missing, outnumbered or broken
        // receiving wing requires covered, independently timed combat bounds.
        public static void DowngradeFastTransfer(BattleSimulation b, Soldier s)
        {
            if (!s.PriorityMovement.FastTransfer)
                return;
            s.PriorityMovement.FastTransfer = false;
            // The old sprint route may be long/exposed: discard it immediately so the
            // following decision builds a short cover-first combat bound.
            s.Path.Clear();
            s.PathCursor = 0;
            s.PriorityMovement.NextRoute = 0;
            s.PriorityMovement.MapRevision = -1;
            s.PriorityMovement.WaitSince = -1;
            s.Job = Job.Cover;
            s.NextDecision = b.Time;
            s.Reason = "Быстрый переход отменён: требуется боевое смещение";
        }

        public static bool HasEnoughReceivingSupport(BattleSimulation b, Soldier mover)
        {
            var q = b.Team(mover);
            var movement = mover.PriorityMovement;
            if (b.Time >= q.FlankPlan.FastTransferEndsAt)
                return false;
            if (!movement.Transfer || movement.TransferGroupSize <= 0)
                return false;
            int receiving = 0;
            foreach (var s in b.Soldiers)
                if (s.Side == mover.Side && s.Id != mover.Id && Mobile(s) && !s.Broken && !s.PriorityMovement.Transfer && s.Suppression < .70f && s.Morale >= .25f && s.Ammo + s.Reserve > 0 && SideOf(q, s.Id) == movement.DestinationWing)
                    receiving++;
            return receiving >= movement.TransferGroupSize;
        }

        public static bool TransferCovered(BattleSimulation b, Soldier s)
        {
            if (!s.PriorityMovement.Transfer)
                return false;
            ExpireFastTransfer(b, s);
            if (s.PriorityMovement.FastTransfer)
                return true;
            if (s.Broken || s.Suppression >= .70f || s.Morale < .20f)
                return false;
            var threat = TacticalOrders.ClosestKnown(b, s);
            if (threat == null || TacticalAI.SafeApproach(b, s) || CommanderPriorities.SmokeSafe(b, s))
            {
                s.PriorityMovement.WaitSince = -1;
                return true;
            }

            bool protectedByWing = false;
            foreach (var u in b.Soldiers)
                if (u.Id != s.Id && u.Side == s.Side && u.Available && !u.Broken && !u.PriorityMovement.Transfer && u.Suppression < .55f && u.Ammo > 0 && SideOf(b.Team(s), u.Id) == s.PriorityMovement.DestinationWing && PositionLogic.UsefulSupport(b, u, s, s.Destination))
                {
                    protectedByWing = true;
                    break;
                }

            if (protectedByWing)
            {
                s.PriorityMovement.WaitSince = -1;
                return true;
            }

            // No receiver must not produce infinite WaitCover: short tactical bounds
            // are allowed after an observation window, not an instant full sprint.
            if (s.PriorityMovement.WaitSince < 0)
                s.PriorityMovement.WaitSince = b.Time;
            return b.Time - s.PriorityMovement.WaitSince >= 2.5f && s.Suppression < .55f;
        }

        // Expiration also invalidates any long exposed waypoint created during the sprint.
        public static void ExpireFastTransfer(BattleSimulation b, Soldier s)
        {
            if (s.PriorityMovement.Transfer && s.PriorityMovement.FastTransfer && (b.Time >= b.Team(s).FlankPlan.FastTransferEndsAt || !HasEnoughReceivingSupport(b, s)))
                DowngradeFastTransfer(b, s);
        }

        public static void TransferRoute(BattleSimulation b, Soldier s, bool force)
        {
            if (!s.PriorityMovement.Transfer || !Mobile(s))
                return;
            ExpireFastTransfer(b, s);
            var st = s.PriorityMovement;
            if (!force && b.Time < st.NextRoute && st.MapRevision == b.Map.Revision)
                return;
            if (!force && s.PathCursor < s.Path.Count && st.MapRevision == b.Map.Revision)
                return;
            if (st.FastTransfer && !HasEnoughReceivingSupport(b, s))
                DowngradeFastTransfer(b, s);
            var q = b.Team(s);
            V2 center = WingCenter(b, q, st.DestinationWing, s.Id);
            var wanted = TacticalOrders.Open(b, center + PositionLogic.Axis(b) * -1.8f + TacticalOrders.Lateral(b) * (s.Id % 2 == 0 ? 1.3f : -1.3f));
            var threat = TacticalOrders.ClosestKnown(b, s);
            // Route around the rear before crossing when the direct transfer is exposed.
            if (V2.Dot(s.Position - center, PositionLogic.Axis(b)) <= -3f)
                st.RearLegCompleted = true;
            if (!st.RearLegCompleted && threat != null && V2.SegmentDistance(threat.Position, s.Position, wanted) < b.WeaponRange(s) * .65f && V2.Dot(s.Position - center, PositionLogic.Axis(b)) > -3f)
            {
                wanted = TacticalOrders.Open(b, s.Position - PositionLogic.Axis(b) * 4f + TacticalOrders.Lateral(b) * st.DestinationWing * 2f);
            }

            if (V2.Distance(s.Position, center) < 3f)
            {
                st.Transfer = false;
                st.NextRoute = 0;
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Hold;
                s.NextDecision = b.Time;
                s.Reason = "Перебежка завершена: работаю на своём фланге";
                return;
            }

            if (st.FastTransfer)
            {
                // ONLY spread/reshape laterally; the forward axis belongs to the normal
                // flank combat planner, never to an unrestricted transfer dash.
                var lateral = TacticalOrders.Lateral(b);
                float sideways = V2.Dot(wanted - s.Position, lateral);
                if (Math.Abs(sideways) < .65f)
                {
                    DowngradeFastTransfer(b, s);
                    return;
                }

                wanted = TacticalOrders.Open(b, s.Position + lateral * sideways);
                if (Math.Abs(V2.Dot(wanted - s.Position, PositionLogic.Axis(b))) > .8f)
                {
                    DowngradeFastTransfer(b, s);
                    return;
                }
            }

            if (!st.FastTransfer)
            {
                // Break the flank crossing into short, cover-first combat bounds.
                var delta = wanted - s.Position;
                float recommended = MotionCombat.Recommended(b.Team(s));
                if (delta.Length > recommended)
                    wanted = s.Position + delta.Normalized * recommended;
                bool found = threat != null && TacticalOrders.Route(b, s, wanted, true, false);
                if (!found)
                    TacticalOrders.Route(b, s, wanted, false, false);
                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.Reason = "Боевая перебежка: учитываю огонь, мораль, подавление и укрытия";
                if (threat != null)
                    TacticalOrders.TrySmoke(b, s, threat);
            }
            else
            {
                // Supported transfer: longer bounds and no *mandatory* covering pause,
                // but shooting, reloads and combat debuffs remain fully active.
                var delta = wanted - s.Position;
                if (delta.Length > 7f)
                    wanted = s.Position + delta.Normalized * 7f;
                V2 begin = s.Position;
                bool obtained = TacticalOrders.Route(b, s, wanted, false, false);
                if (!obtained || !FreeCrossingIsLateral(b, s, begin))
                {
                    // No cheating with an A* detour toward the enemy on the free phase.
                    DowngradeFastTransfer(b, s);
                    return;
                }

                if (threat != null)
                    TacticalOrders.TrySmoke(b, s, threat);
                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.Reason = "Перестраиваюсь поперёк фронта, без движения к противнику";
            }
        }

        public static bool CanAdvance(BattleSimulation b, Soldier s)
        {
            var assignment = s.Assignment;
            if (CommandHierarchy.Owns(b.Team(s)) && assignment.Active && assignment.IsBound && V2.Distance(assignment.Goal, s.Destination) < .2f)
                return CommandHierarchy.MovementAllowed(b, s);
            if (CoverWaitPolicy.Allow(b, s))
                return true;
            var q = b.Team(s);
            if (s.PriorityMovement.Transfer)
                return TransferCovered(b, s);
            if (TacticalOrders.PriorityMover(b, s))
                return true;
            if (q.Style != CombatStyle.Aggressive && s.PairRegroupUntil > b.Time && s.PathCursor < s.Path.Count)
                return true; // limited regroup route, not a solo assault
            // Aggression relaxes cover-position selection, NOT buddy overwatch and bounds.
            if (s.BuddyId == s.Id || s.BuddyId < 0 || s.BuddyId >= b.Soldiers.Count)
                return q.Style == CombatStyle.Aggressive || !CombatCohesion.InCombat(b, s.Side);
            // While a pair is engaged, only its designated mover advances; the
            // other member must remain available for overwatch, including on Balance.
            int wing = SideOf(q, s.Id), wingSlot = wing < 0 ? 0 : 1;
            var buddy = s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count ? b.Soldiers[s.BuddyId] : s;
            bool paired = buddy.Id != s.Id && Mobile(buddy) && SideOf(q, buddy.Id) == wing;
            bool combat = CombatCohesion.InCombat(b, s.Side);
            // Sniper is a legitimate alternate mover of this pair, despite the normal
            // LocalMover token being assigned to the rifleman. He must stay near him.
            if (paired && s.Sniper && s.SniperMoving)
                return !buddy.Moving && buddy.PathCursor >= buddy.Path.Count && SniperTactics.BuddyCanCover(b, buddy) && V2.Distance(s.Destination, buddy.Position) <= SniperTactics.PairPostLimit;
            // A paired sniper's moving step owns the bound; the rifleman stays to cover.
            if (paired && buddy.Sniper && buddy.SniperMoving && buddy.PathCursor < buddy.Path.Count)
                return false;
            if (paired && combat && MoverFor(q, s) != s.Id)
                return false;
            if ((!paired || !combat) && TacticalAI.SafeApproach(b, s))
                return true;
            if (CommanderPriorities.SmokeSafe(b, s))
                return true;
            if (Envelopment(q) && !q.FlankPlan.FixedSupport.Contains(s.Id))
            {
                bool any = false;
                foreach (int id in q.SupportIds)
                {
                    var u = b.Soldiers[id];
                    if (!Mobile(u))
                        continue;
                    any = true;
                    if (PositionLogic.UsefulSupport(b, u, s, s.OrderAnchor))
                        return true;
                }

                return !any;
            }

            if (!Mobile(buddy) || SideOf(q, buddy.Id) != SideOf(q, s.Id))
                return q.Style == CombatStyle.Aggressive || !combat;
            if (PositionLogic.UsefulSupport(b, buddy, s, s.OrderAnchor))
            {
                s.PriorityMovement.WaitSince = -1;
                return true;
            }

            // No infinite hold if the available buddy cannot cover: a short
            // observation delay allows a cautious local bound when pressure is low.
            if (s.PriorityMovement.WaitSince < 0)
                s.PriorityMovement.WaitSince = b.Time;
            // No blind solo pushes under fire for Balanced/Passive. If buddy cover
            // truly cannot be verified, let movement fall back to a short, safe bound
            // near the buddy instead of a long independent offensive advance.
            if (q.Style != CombatStyle.Aggressive && combat)
                return false;
            return b.Time - s.PriorityMovement.WaitSince >= 2.5f && s.Suppression < .55f && s.Morale >= .35f;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (!q.FlankPlan.Active)
                Plan(b, q);
            if (!s.Available)
                return true;
            if (TacticalOrders.ServiceActive(s))
                return false;
            if (q.CommandTransition.Waiting && s.PathCursor >= s.Path.Count)
            {
                s.Job = Job.Cover;
                s.Reason = "Прикрываю завершение перебежки перед сменой замысла";
                return true;
            }

            if (s.Broken)
            {
                if (MoraleDynamics.CanRecover(b, s))
                {
                    s.Broken = false;
                }
                else
                {
                    TacticalAI.Stop(s);
                    s.State = ActionState.Cower;
                    return true;
                }
            }

            if (s.ReloadUntil > b.Time || b.Time < s.ConcussionReactUntil)
                return true;
            bool medic = s.Role == Role.Medic;
            if (medic && !MedicTactics.FreeToFight(b, s))
                return false; // medical task takes precedence
            bool aggressive = q.Style == CombatStyle.Aggressive && !medic;
            var threat = TacticalOrders.ClosestKnown(b, s);
            int side = SideOf(q, s.Id);
            if (side == 0)
                side = TargetSide(q);
            bool support = Envelopment(q) && q.FlankPlan.FixedSupport.Contains(s.Id);
            bool contact = threat != null && V2.Distance(s.Position, threat.Position) < b.WeaponRange(s) + 6;
            var buddy = s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count ? b.Soldiers[s.BuddyId] : s;
            bool partner = buddy.Id != s.Id && Mobile(buddy) && SideOf(q, buddy.Id) == side;
            int mover = MoverFor(q, s);
            bool groupInCombat = CombatCohesion.InCombat(b, s.Side);
            if (!aggressive && groupInCombat && !partner && !support)
            {
                // Balanced/Passive must rejoin a friendly element instead of exploiting
                // the lone-fighter exemption as a permanent license to rush the enemy.
                if (s.PairRegroupUntil > b.Time && s.PathCursor < s.Path.Count && s.Job == Job.Reposition)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                // A former aggressive route must not be mistaken for a regroup route.
                if (s.PathCursor < s.Path.Count && s.PairRegroupUntil <= b.Time)
                {
                    TacticalAI.Stop(s);
                    s.PriorityMovement.NextRoute = 0;
                }

                if (b.Time >= s.PriorityMovement.NextRoute)
                {
                    Soldier near = null;
                    float distance = float.MaxValue;
                    foreach (var ally in b.Soldiers)
                        if (ally.Id != s.Id && ally.Side == s.Side && Mobile(ally))
                        {
                            float d = V2.Distance(ally.Position, s.Position);
                            if (d < distance)
                            {
                                distance = d;
                                near = ally;
                            }
                        }

                    if (near != null && distance > 3.5f)
                    {
                        V2 regroupGoal = s.Position + (near.Position - s.Position).Normalized * Math.Min(3.5f, distance - 2.5f);
                        if (TacticalOrders.Route(b, s, regroupGoal, true, false))
                        {
                            s.PairRegroupUntil = b.Time + 3.5f;
                            s.Job = Job.Reposition;
                            s.MoveAuthorized = true;
                            s.Order = "Возвращаюсь к своей группе";
                            s.Reason = "Баланс: работаю с товарищами, без самостоятельного прорыва";
                            return true;
                        }
                    }
                }

                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.Reason = "Один на фланге: удерживаю сектор, ожидаю группу";
                return true;
            }

            // The stationary partner covers even in aggressive mode. One mobile fighter
            // per pair, unless the other is incapacitated or no battle is underway.
            if ((support && PositionLogic.HasSector(b, s, s.Position)) || (!support && partner && (contact || groupInCombat) && s.Id != mover))
            {
                // Keep a previously assigned short covering reposition alive. Otherwise
                // a remote partner can freeze forever without an effective firing sector.
                if (s.Job == Job.Cover && s.PathCursor < s.Path.Count)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                bool hasLane = PositionLogic.HasSector(b, s, s.Position);
                if (!hasLane && threat != null && b.Time >= s.PriorityMovement.NextRoute)
                {
                    V2 alternative;
                    if (PositionLogic.TryPost(b, s, s.Position, 3f, true, out alternative) && V2.Distance(alternative, s.Position) > 1.1f && V2.Distance(alternative, buddy.Position) < 7f && TacticalOrders.Route(b, s, alternative, true, false))
                    {
                        s.Job = Job.Cover;
                        s.MoveAuthorized = true;
                        s.Reason = "Коротко смещаюсь для прикрытия своей двойки";
                        return true;
                    }

                    s.PriorityMovement.NextRoute = b.Time + 2f;
                }

                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.Reason = hasLane ? "Прикрываю свою двойку" : "Наблюдаю сектор и держусь рядом с напарником";
                return true;
            }

            // When the outward leg hits the lateral map boundary, an old waypoint
            // must not hold the unit against the invisible wall forever.
            bool atWingEdge = !support && FlankOutmaneuver.AtSideEdge(b, s.Position, side);
            bool usefulHere = threat != null && PositionLogic.CanEngage(b, s, s.Position, threat);
            if (atWingEdge && !usefulHere && s.PathCursor < s.Path.Count)
            {
                var towardOut = TacticalOrders.Lateral(b) * side;
                if (V2.Dot(s.Destination - s.Position, towardOut) > .25f)
                {
                    s.Path.Clear();
                    s.PathCursor = 0;
                    s.PriorityMovement.NextRoute = 0;
                }
            }

            if (s.PathCursor < s.Path.Count && s.PriorityMovement.MapRevision == b.Map.Revision)
            {
                s.MoveAuthorized = CanAdvance(b, s);
                s.Job = support ? Job.Cover : Job.Flank;
                return true;
            }

            if (b.Time < s.PriorityMovement.NextRoute)
                return true;
            var axis = PositionLogic.Axis(b);
            var lateral = TacticalOrders.Lateral(b);
            float lateralPos = V2.Dot(s.Position - b.Map.Entry, lateral) * side;
            float range = b.WeaponRange(s);
            if (threat != null && V2.Distance(s.Position, threat.Position) <= range && PositionLogic.CanEngage(b, s, s.Position, threat) && (support || q.Style == CombatStyle.Passive || lateralPos >= 4f) && (s.Visible.Contains(threat.EnemyId) || b.Time - s.LastFire < 3f))
            {
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
                s.Reason = "Держу фланговый прострел на рабочей дальности";
                return true;
            }

            V2 wanted;
            if (support)
                wanted = s.Position + (threat == null ? axis : (threat.Position - s.Position).Normalized) * 2.5f;
            else if (threat != null)
            {
                var direction = (axis * -0.65f + lateral * side * .76f).Normalized;
                wanted = threat.Position + direction * range * .9f;
            }
            else
            {
                float forward = Math.Min(V2.Dot(b.Map.Objective - b.Map.Entry, axis) - 1f, V2.Dot(s.Position - b.Map.Entry, axis) + 6f);
                float spread = (6f + (s.Id % 2) * 2f) * FormationAI.LateralMultiplier(q);
                wanted = b.Map.Entry + axis * forward + lateral * side * spread;
            }

            wanted = FormationAI.Desired(b, s, wanted, true);
            // The lateral maneuver is complete at the map edge: turn *inward* and
            // advance toward the objective instead of indefinitely re-requesting an
            // impossible out-of-bounds lateral post. Direct useful fire has precedence.
            if (atWingEdge && !usefulHere && !support)
            {
                wanted = FlankOutmaneuver.TowardFlag(b, s.Position);
                s.Order = "Фланг достигнут — к району флага";
                s.Reason = "Ограничение карты: перехожу на внутреннюю линию наступления";
            }

            // Standard tactical moves are short. 7-unit fast bounds only exist in the
            // first five seconds of an eligible *flank-to-flank* maneuver switch.
            V2 delta = wanted - s.Position;
            float boundLength = medic ? 3.2f : q.Style == CombatStyle.Passive ? 2.5f : (contact || groupInCombat) ? 3.8f : 4.5f;
            if (delta.Length > boundLength)
                wanted = s.Position + delta.Normalized * boundLength;
            bool requireCover = partner && !support && groupInCombat;
            bool route = TacticalOrders.Route(b, s, wanted, requireCover, false);
            if (!route && requireCover)
            {
                // Open ground remains possible when protected movement cannot be found,
                // but only once the pair has been allowed to seek covering fire/smoke.
                if (s.PriorityMovement.WaitSince < 0)
                    s.PriorityMovement.WaitSince = b.Time;
                if (b.Time - s.PriorityMovement.WaitSince >= 2.5f && CanAdvance(b, s) && ((aggressive && !medic) || !groupInCombat))
                {
                    route = TacticalOrders.Route(b, s, wanted, false, false);
                    s.PriorityMovement.WaitSince = -1;
                }
            }
            else if (route)
                s.PriorityMovement.WaitSince = -1;
            s.Job = support ? Job.Cover : Job.Flank;
            s.MoveAuthorized = CanAdvance(b, s);
            s.ManeuverPaused = false;
            if (route)
            {
                s.Reason = atWingEdge && !usefulHere && !support ? "Край фланга достигнут, двигаюсь к району флага" : aggressive ? "Обхожу через доступную местность; укрытие желательно, но не обязательно" : "Работаю со своей двойкой, не жду дальний фланг";
            }
            else if (contact)
                TacticalOrders.TrySmoke(b, s, threat);
            return true;
        }
    }
}
