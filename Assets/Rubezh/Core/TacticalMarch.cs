using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class TacticalMarchState
    {
        public bool Initialized,
            Quiet,
            CoverAssigned;
        public float LastDanger = -100,
            NextSectors,
            ContactAt = -100,
            NextRoute;
        public int Contacts,
            Resumes,
            Coverer = -1,
            Handovers,
            EmergencyReleases;
        public readonly Dictionary<int, Contact> Danger = new Dictionary<int, Contact>();
        public readonly Dictionary<int, V2> Sectors = new Dictionary<int, V2>();
        public readonly HashSet<int> Dispersed = new HashSet<int>();
        public double QuietSeconds;
        public Contact Threat;
    }

    public static class TacticalMarch
    {
        static bool Enabled(BattleSimulation b, Squad q)
        {
            if (ReconCombatPhase.Active(b) || !b.TacticalMarchEnabled || b.Historical01225 || q.Side != Side.Green)
                return false;
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && s.Deployed
                    && s.Available
                    && (
                        CombatTraining.Skill(s, 2, "contact")
                        || s.CampSkills == null && b.PairExperimentMarch && s.Side == Side.Green
                    )
                )
                    return true;
            return false;
        }

        static bool MarchOrder(Squad q) => q.Style == CombatStyle.Balanced || q.Style == CombatStyle.Aggressive;

        internal static bool Free(BattleSimulation b, Soldier s) =>
            (
                CombatTraining.Skill(s, 2, "contact")
                || s.CampSkills == null && b.PairExperimentMarch && s.Side == Side.Green
            )
            && GroupMovement.Ordinary(b, s)
            && !TacticalOrders.ServiceActive(s)
            && !EngagementPolicy.Active(b, s)
            && s.LootTargetId < 0
            && s.LootGroundId < 0
            && s.LauncherPickupId < 0
            && s.WeaponAidTargetId < 0
            && !s.PriorityMovement.Transfer
            && !AreaSuppression.Reserved(b, s)
            && !ProgressRecovery.Active(b, s)
            && !b.Team(s).Breakthrough.Active
            && !b.Team(s).CommandTransition.Waiting
            && s.PairRegroupUntil <= b.Time
            && !HandLauncher.Active(s)
            && !PairExchange.Active(b, s)
            && !FriendlyGrenadeSafety.Active(b, s)
            && s.HandsBusyUntil <= b.Time
            && s.ReloadUntil <= b.Time;

        public static bool Quiet(BattleSimulation b, Squad q) =>
            Enabled(b, q) && MarchOrder(q) && q.CommandPlan.March.Quiet;

        static V2 Rotate(V2 v, float degrees)
        {
            float a = degrees * (float)Math.PI / 180;
            return new V2(
                v.X * (float)Math.Cos(a) - v.Y * (float)Math.Sin(a),
                v.X * (float)Math.Sin(a) + v.Y * (float)Math.Cos(a)
            );
        }

        static void Voice(BattleSimulation b, Squad q, Soldier speaker, string text)
        {
            if (speaker != null)
                b.Say(speaker, text, true);
            else if (q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count)
                b.Say(b.Soldiers[q.LeaderId], text, true);
        }

        static bool ObservedEmpty(BattleSimulation b, Squad q, Contact c)
        {
            // Close inspection of the old position AND its immediate exits, with no smoke.
            // This does not inspect the enemy's actual hidden position or health.
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && !s.Broken && V2.Distance(s.Position, c.Position) < 7)
                {
                    bool clear = true;
                    foreach (var offset in new[] { new V2(), new V2(2, 0), new V2(-2, 0), new V2(0, 2), new V2(0, -2) })
                    {
                        var p = c.Position + offset;
                        if (!b.Map.Walkable(p))
                            continue;
                        if (
                            !DirectionalVision.InView(b, s, p)
                            || !b.Map.LineOfSight(s.Position, p)
                            || b.SmokeTransmission(s.Position, p) < .8f
                        )
                        {
                            clear = false;
                            break;
                        }
                    }

                    if (clear)
                        return true;
                }

            return false;
        }

        public static void Update(BattleSimulation b, Squad q)
        {
            if (!Enabled(b, q))
                return;
            var st = q.CommandPlan.March;
            bool danger = false;
            Soldier observer = null;
            Contact fresh = null;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available)
                {
                    if (b.Time - s.LastIncoming < 5 || b.Time - s.LastBlast < 5 || s.Suppression > .25f)
                        danger = true;
                    foreach (var c in Knowledge.Contacts(b, s))
                    {
                        if (c.Inferred || !c.Visual)
                            continue;
                        Contact old;
                        if (!st.Danger.TryGetValue(c.EnemyId, out old) || old.SeenAt < c.SeenAt)
                            st.Danger[c.EnemyId] = c.Copy();
                        if (s.Visible.Contains(c.EnemyId) && b.Time - c.SeenAt <= b.Config.PerceptionPeriod * 2)
                        {
                            danger = true;
                            if (fresh == null || c.TargetPriority < fresh.TargetPriority)
                            {
                                fresh = c;
                                observer = s;
                            }
                        }
                    }
                }

            var remove = new List<int>();
            foreach (var pair in st.Danger)
            {
                var c = pair.Value;
                float age = b.Time - c.SeenAt;
                if (age > 60 || age > 2 && ObservedEmpty(b, q, c))
                {
                    remove.Add(pair.Key);
                    continue;
                }

                foreach (var s in b.Soldiers)
                    if (
                        q.Contains(s)
                        && s.Available
                        && V2.Distance(s.Position, c.Position) < DirectionalVision.FrontRange(b, s) + 6
                    )
                    {
                        danger = true;
                        break;
                    }
            }

            foreach (int id in remove)
                st.Danger.Remove(id);
            if (danger)
                st.LastDanger = b.Time;
            if (fresh != null)
                st.Threat = fresh;
            bool was = st.Quiet;
            st.Quiet = MarchOrder(q) && !danger && b.Time - st.LastDanger >= 12;
            if (!st.Initialized)
            {
                st.Initialized = true;
                was = false;
            }

            if (was && !st.Quiet)
            {
                st.ContactAt = b.Time;
                st.Threat = fresh;
                st.Coverer = -1;
                st.CoverAssigned = false;
                st.Dispersed.Clear();
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && s.Awareness.MarchRouteUntil > 0)
                    {
                        s.Awareness.MarchRouteUntil = 0;
                        if (Free(b, s))
                            TacticalAI.Stop(s);
                    }

                if (danger)
                {
                    st.Contacts++;
                    Voice(b, q, observer, "КОНТАКТ!!");
                    foreach (var s in b.Soldiers)
                        if (
                            q.Contains(s)
                            && Free(b, s)
                            && s.GunMachineGunner
                            && s.Ammo >= 3
                            && s.Suppression < .65f
                            && fresh != null
                            && PositionLogic.CanEngage(b, s, s.Position, fresh)
                        )
                        {
                            st.Coverer = s.Id;
                            st.CoverAssigned = true;
                            break;
                        }
                }

                q.NextPlan = b.Time;
                foreach (var s in b.Soldiers)
                    if (q.Contains(s))
                        s.NextDecision = b.Time;
            }

            if (!was && st.Quiet)
            {
                st.Resumes++;
                Voice(b, q, null, "Готовность. Движение");
                q.NextPlan = b.Time;
            }

            if (st.Quiet)
                st.QuietSeconds += b.Config.Tick;
            if (b.Time >= st.NextSectors)
            {
                st.NextSectors = b.Time + 1;
                AssignSectors(b, q);
            }

            if (!st.Quiet && !st.CoverAssigned && MarchOrder(q) && b.Time - st.ContactAt < 8 && fresh != null)
            {
                foreach (var s in b.Soldiers)
                    if (
                        q.Contains(s)
                        && Free(b, s)
                        && s.GunMachineGunner
                        && s.Ammo >= 3
                        && s.Suppression < .65f
                        && PositionLogic.CanEngage(b, s, s.Position, fresh)
                    )
                    {
                        st.Coverer = s.Id;
                        st.CoverAssigned = true;
                        break;
                    }
            }

            if (st.Coverer >= 0)
            {
                var gun = b.Soldiers[st.Coverer];
                int ready = 0,
                    others = 0;
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && s != gun && s.Available)
                    {
                        others++;
                        if (
                            PositionLogic.Able(s, b)
                            && b.Time - s.LastFire < 2
                            && st.Threat != null
                            && PositionLogic.CanEngage(b, s, s.Position, st.Threat)
                            && PositionLogic.Protected(b, s, s.Position)
                        )
                            ready++;
                    }

                if (ready >= Math.Min(2, Math.Max(1, others)))
                {
                    st.Handovers++;
                    st.Coverer = -1;
                    gun.NextDecision = b.Time;
                }
                else if (
                    !Free(b, gun)
                    || gun.Ammo < 3
                    || gun.Health < 35
                    || gun.Suppression > .65f
                    || (b.Time - st.ContactAt > 8 && fresh == null)
                    || !MarchOrder(q)
                )
                {
                    st.EmergencyReleases++;
                    st.Coverer = -1;
                    gun.NextDecision = b.Time;
                }
            }
        }

        static void AssignSectors(BattleSimulation b, Squad q)
        {
            var st = q.CommandPlan.March;
            st.Sectors.Clear();
            var assigned = new List<V2>();
            int index = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && !s.Broken)
                {
                    V2 axis = MotionCombat.HasRoute(s)
                        ? (s.Path[s.PathCursor] - s.Position).Normalized
                        : TacticalOrders.Axis(b, s.Side);
                    if (axis.Length < .1f)
                        axis = TacticalOrders.Axis(b, s.Side);
                    V2 fallback = s.Position + Rotate(axis, new[] { 0f, -70f, 70f, -140f, 140f }[index++ % 5]) * 8,
                        best = fallback;
                    float bestScore = float.MinValue;
                    var candidates = new List<KeyValuePair<float, V2>> { new KeyValuePair<float, V2>(1, fallback) };
                    foreach (var c in st.Danger.Values)
                        if (V2.Distance(s.Position, c.Position) < DirectionalVision.FrontRange(b, s) + 4)
                            candidates.Add(new KeyValuePair<float, V2>(7, c.Position));
                    // Visible occlusion boundaries, never actual hidden enemies.
                    for (int ray = 0; ray < 12; ray++)
                    {
                        V2 dir = Rotate(axis, ray * 30);
                        for (float d = 2; d <= 12; d += 1)
                        {
                            var point = s.Position + dir * d;
                            if (!b.Map.LineOfSight(s.Position, point) || b.SmokeTransmission(s.Position, point) < .5f)
                            {
                                V2 side = new V2(-dir.Y, dir.X);
                                bool found = false;
                                foreach (int sign in new[] { -1, 1 })
                                    for (int offset = 1; offset <= 4; offset++)
                                    {
                                        var edge = point + side * (sign * offset);
                                        if (
                                            b.Map.Walkable(edge)
                                            && b.Map.LineOfSight(s.Position, edge)
                                            && b.SmokeTransmission(s.Position, edge) > .5f
                                        )
                                        {
                                            candidates.Add(new KeyValuePair<float, V2>(4, edge));
                                            found = true;
                                            break;
                                        }
                                    }

                                if (!found)
                                    candidates.Add(
                                        new KeyValuePair<float, V2>(2, s.Position + dir * Math.Max(1, d - 1))
                                    );
                                break;
                            }
                        }
                    }

                    foreach (var c in candidates)
                    {
                        float score = c.Key - V2.Distance(s.Position, c.Value) * .03f;
                        foreach (var used in assigned)
                            if (V2.Distance(used, c.Value) < 5)
                                score -= 8;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = c.Value;
                        }
                    }

                    if (b.PairExperimentProfile >= 1)
                        best = PairExperiment.ChooseSector(b, s, candidates, assigned, best);
                    st.Sectors[s.Id] = best;
                    assigned.Add(best);
                }
        }

        public static bool Watch(BattleSimulation b, Soldier s, out V2 direction)
        {
            direction = new V2();
            var q = b.Team(s);
            if (!Quiet(b, q))
                return false;
            V2 point;
            if (!q.CommandPlan.March.Sectors.TryGetValue(s.Id, out point))
                return false;
            direction = (point - s.Position).Normalized;
            return direction.Length > .1f;
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            if (!Quiet(b, q))
                return false;
            var st = q.CommandPlan.March;
            q.Phase = ManeuverPhase.Move;
            q.RequiredSupport = 0;
            q.Initialized = q.PriorityInitialized = true;
            q.Plan = q.Execution = q.CommanderOrder = "Тактическое движение · контроль секторов";
            if (b.Time < st.NextRoute)
                return true;
            st.NextRoute = b.Time + 1.5f;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Free(b, s))
                {
                    if (MotionCombat.HasRoute(s) && s.Awareness.MarchRouteUntil > b.Time)
                    {
                        s.MoveAuthorized = true;
                        continue;
                    }

                    var pair = CommandHierarchy.Pair(q, s.Assignment.PairId);
                    V2 goal = pair == null ? b.Map.Objective : pair.Goal;
                    if (V2.Distance(goal, s.Position) < 1)
                        continue;
                    var path = LossMemory.Find(
                        b,
                        s,
                        TacticalOrders.Open(b, goal),
                        v =>
                            FireLaneSafety.PathCost(b, s, v)
                            + (PositionLogic.Occupied(b, s, v) ? 12 : 0)
                            + PairExperiment.RouteCost(b, s, v)
                    );
                    b.Stats.Paths++;
                    if (path.Count == 0)
                        continue;
                    CommandHierarchy.TransferAssignment(b, s, "Передано тактическому маршу");
                    s.Path = path;
                    s.PathCursor = 0;
                    s.Destination = s.OrderAnchor = goal;
                    s.Job = Job.Reposition;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.SupportReady = false;
                    s.Awareness.MarchRouteUntil = b.Time + 6;
                    s.Reason = "Тактический марш: двигаюсь и контролирую свой сектор";
                }

            return true;
        }

        public static void EnforceCover(BattleSimulation b, Squad q)
        {
            if (!Enabled(b, q) || !MarchOrder(q))
                return;
            var st = q.CommandPlan.March;
            if (st.Coverer < 0)
                return;
            var s = b.Soldiers[st.Coverer];
            if (!Free(b, s))
                return;
            TacticalAI.Stop(s);
            s.Job = Job.Cover;
            s.MoveAuthorized = false;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (!Enabled(b, q) || !Free(b, s) || !MarchOrder(q))
                return false;
            var st = q.CommandPlan.March;
            if (st.Quiet)
            {
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Reason = "Тактический марш: контролирую назначенный сектор";
                return true;
            }

            if (st.Coverer == s.Id)
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.MoveAuthorized = false;
                s.Reason = "Контакт: прикрываю развёртывание, жду передачи сектора";
                return true;
            }

            if (b.Time - st.ContactAt > 8 || st.Threat == null)
                return false;
            if (s.Awareness.ContactSprintUntil > b.Time && MotionCombat.HasRoute(s))
            {
                s.MoveAuthorized = true;
                return true;
            }

            if (st.Dispersed.Contains(s.Id) || PositionLogic.Protected(b, s, s.Position))
                return false;
            st.Dispersed.Add(s.Id);
            if (ContactMovement.TryCover(b, s, st.Threat))
                return true;
            V2 post;
            if (PositionLogic.TryPost(b, s, s.Position, 7, false, out post) && V2.Distance(post, s.Position) > .7f)
            {
                var path = LossMemory.Find(
                    b,
                    s,
                    post,
                    v => PositionLogic.Exposure(b, s, v) * 4 + (PositionLogic.Occupied(b, s, v) ? 15 : 0)
                );
                b.Stats.Paths++;
                if (path.Count > 0)
                {
                    s.Path = path;
                    s.PathCursor = 0;
                    s.OrderAnchor = s.Destination = post;
                    CommandHierarchy.TransferAssignment(b, s, "Передано тактическому маршу");
                    s.Job = Job.Reposition;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.Awareness.ContactSprintUntil = b.Time + 6;
                    s.Reason = "КОНТАКТ: бегом в отдельное укрытие";
                    return true;
                }
            }

            SmokeTactics.Request(b, q, s, s.Position, SmokePurpose.Route, s);
            return false;
        }
    }
}
