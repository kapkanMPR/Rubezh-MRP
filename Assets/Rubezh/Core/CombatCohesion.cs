using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // A local tactical overlay, not a new commander: assists isolated team mates and
    // unsticks soldiers with no useful sector while respecting the current maneuver.
    public static class CombatCohesion
    {
        public const float IdleReviewSeconds = 6f;
        public const float HelpSuppressionThreshold = .72f;
        static bool Suitable(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            return !AreaSuppression.Reserved(b, s) && !CounterSniper.Reserved(b, s) && s.Available && !s.Busy && s.SupplyReceiverId < 0 && !s.VehicleRally && !s.VehicleDisperse && s.FlagResponseUntil <= b.Time && !s.Broken && !TacticalOrders.PriorityMover(b, s) && !RedCounterattack.Retreating(b, s) && q.Style != CombatStyle.Withdraw && !TacticalOrders.Emergency(q) && !q.Breakthrough.Active && s.Health > 28f;
        }

        static bool FreshContact(BattleSimulation b, Contact c)
        {
            return c != null && c.Confidence >= .25f && c.SeenAt <= b.Time && b.Time - c.SeenAt <= 9f;
        }

        public static bool InCombat(BattleSimulation b, Side side)
        {
            var team = b.Team(side);
            foreach (var c in team.Contacts.Values)
                if (FreshContact(b, c))
                    return true;
            foreach (var s in b.Soldiers)
                if (s.Side == side && s.Alive && (b.Time - s.LastFire < 4f || b.Time - s.LastIncoming < 3f))
                    return true;
            return false;
        }

        static Contact KnownThreat(BattleSimulation b, Soldier s)
        {
            Contact nearest = null;
            float distance = float.MaxValue;
            foreach (var c in b.Team(s).Contacts.Values)
                Consider(b, s, c, ref nearest, ref distance);
            foreach (var c in s.Contacts.Values)
                Consider(b, s, c, ref nearest, ref distance);
            return nearest;
        }

        static void Consider(BattleSimulation b, Soldier s, Contact c, ref Contact nearest, ref float dist)
        {
            if (!FreshContact(b, c))
                return;
            float d = V2.Distance(s.Position, c.Position);
            if (d < dist)
            {
                dist = d;
                nearest = c;
            }
        }

        static bool SameElement(BattleSimulation b, Soldier a, Soldier z)
        {
            var team = b.Team(a);
            return !TacticalOrders.Flanks(team) || !team.FlankPlan.Active || FlankTactics.SideOf(team, a.Id) == FlankTactics.SideOf(team, z.Id);
        }

        // Called once per simulation tick, after suppression is updated. It only
        // creates short-lived requests; all actual movement remains pathfinder-driven.
        public static void Tick(BattleSimulation b)
        {
            foreach (var pinned in b.Soldiers)
            {
                if (!Suitable(b, pinned) || pinned.Suppression < HelpSuppressionThreshold || b.Time - pinned.LastIncoming > 2.4f || b.Time < pinned.NextHelpRequest)
                    continue;
                Contact threat = KnownThreat(b, pinned);
                pinned.NextHelpRequest = b.Time + 10f;
                pinned.HelpRetreatUntil = b.Time + 7f;
                pinned.HelpRetreatNextRoute = 0f;
                pinned.HelpRetreatPlanned = false;
                pinned.NextDecision = b.Time;
                b.Say(pinned, "Прижали! Нужна поддержка!", true);
                int count = 0;
                // Use the same flank first, but never force another wing to abandon its job.
                for (int pass = 0; pass < 2 && count < 2; pass++)
                {
                    Soldier best = null;
                    float score = float.MaxValue;
                    foreach (var helper in b.Soldiers)
                    {
                        if (helper.Side != pinned.Side || helper.Id == pinned.Id || !Suitable(b, helper) || !GroupCoordination.CanLend(b, helper, pinned) || helper.Suppression > .65f || helper.Morale < .24f || helper.HelpTargetId >= 0 && helper.HelpUntil > b.Time || helper.Ammo + helper.Reserve <= 0)
                            continue;
                        bool same = SameElement(b, pinned, helper);
                        if (pass == 0 && !same)
                            continue;
                        if (pass == 1 && same)
                            continue;
                        float distance = V2.Distance(helper.Position, pinned.Position);
                        if (distance > 9f || distance >= score)
                            continue;
                        // A medic treating a patient is Busy, and cannot be stolen by this task.
                        score = distance;
                        best = helper;
                    }

                    if (best != null)
                    {
                        best.HelpTargetId = pinned.Id;
                        best.HelpUntil = b.Time + 7f;
                        best.HelpNextSmoke = 0f;
                        best.HelpNextRoute = 0f;
                        best.HelpRouteUntil = 0f;
                        best.HelpSmokePreferred = count == 1;
                        best.NextDecision = b.Time;
                        count++;
                    }
                }

                if (count > 0)
                {
                    b.Log(pinned.Side, pinned.Name + ": группа организует выручку");
                    // A short morale boost follows only after effective relief, not on request.
                    pinned.HelpReliefEligible = true;
                }
            }
        }

        public static bool BypassCover(BattleSimulation b, Soldier s)
        {
            // Proactive flanking must respect ordinary cover and buddy coordination.
            // Only a short emergency withdrawal from intense suppression bypasses it.
            return s.HelpRetreatUntil > b.Time && s.PathCursor < s.Path.Count;
        }

        public static void Clear(Soldier s)
        {
            s.HelpTargetId = -1;
            s.HelpUntil = s.HelpRetreatUntil = s.InitiativeUntil = s.HelpRouteUntil = 0;
            s.HelpReliefEligible = s.HelpRetreatPlanned = s.HelpSmokePreferred = false;
            s.IdleCombatSince = -1f;
            s.NextInitiativeAt = 0;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!Suitable(b, s))
                return false;
            if (s.HelpRetreatUntil > b.Time)
            {
                if (s.PathCursor < s.Path.Count)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                if (s.HelpRetreatPlanned)
                {
                    s.HelpRetreatUntil = 0;
                    return false;
                } // short displacement completed

                if (b.Time >= s.HelpRetreatNextRoute)
                    TryRetreat(b, s);
                return s.PathCursor < s.Path.Count;
            }

            if (s.HelpTargetId >= 0)
            {
                if (DecideHelper(b, s))
                    return true;
            }

            if (s.InitiativeUntil > b.Time)
            {
                if (s.PathCursor < s.Path.Count && s.Suppression < .65f)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                s.InitiativeUntil = 0;
            }

            // Do not mistake deliberate overwatch or a useful firing sector for idleness.
            if (s.Ammo <= 0 || s.Role == Role.Medic || s.Sniper || !InCombat(b, s.Side) || b.Time - s.LastFire < 4f || PositionLogic.HasSector(b, s, s.Position) || s.Suppression > .65f || s.Morale < .32f || s.ReloadUntil > b.Time || (s.PathCursor < s.Path.Count && s.MoveAuthorized))
            {
                s.IdleCombatSince = -1f;
                return false;
            }

            // An already useful fire lane is never abandoned merely because the
            // team is attacking. This includes a radio report only when we can see
            // the actual target ourselves.
            var direct = KnownThreat(b, s);
            if (direct != null && s.Visible.Contains(direct.EnemyId) && PositionLogic.CanEngage(b, s, s.Position, direct))
            {
                s.IdleCombatSince = -1f;
                return false;
            }

            if (s.IdleCombatSince < 0)
                s.IdleCombatSince = b.Time;
            if (b.Time - s.IdleCombatSince < IdleReviewSeconds || b.Time < s.NextInitiativeAt)
                return false;
            s.NextInitiativeAt = b.Time + 3f;
            var threat = KnownThreat(b, s);
            if (threat == null)
                return false;
            if (TryRejoin(b, s, threat))
            {
                s.IdleCombatSince = -1f;
                s.InitiativeUntil = b.Time + 6f;
                s.Reason = "Не могу работать с позиции — самостоятельно ищу боевой сектор";
                s.Order = "Перехожу к новому рубежу по плану";
                return true;
            }

            // Reassess later; do not permanently lock on an unreachable candidate.
            return false;
        }

        static bool TryRejoin(BattleSimulation b, Soldier s, Contact threat)
        {
            var team = b.Team(s);
            if (team.Style != CombatStyle.Aggressive)
            {
                // Balance and Passive must not use this autonomous probe as a solo rush.
                Soldier partner = null;
                if (s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count && s.BuddyId != s.Id)
                    partner = b.Soldiers[s.BuddyId];
                if (partner == null || !partner.Available || partner.Broken || V2.Distance(partner.Position, s.Position) > 7f)
                    return false;
            }

            V2 forward = (threat.Position - s.Position).Normalized;
            if (forward.Length < .1f)
                return false;
            V2 lateral = new V2(-forward.Y, forward.X);
            int wing = 1;
            if (TacticalOrders.Flanks(team) && team.FlankPlan.Active)
            {
                wing = FlankTactics.SideOf(team, s.Id);
                if (wing == 0)
                    wing = team.FlankPlan.CommanderWing;
                // Preserve side of the maneuver even if the current enemy is elsewhere.
                lateral = TacticalOrders.Lateral(b);
            }
            else
                wing = s.Id % 2 == 0 ? 1 : -1;
            bool aggressive = team.Style == CombatStyle.Aggressive;
            float range = b.WeaponRange(s);
            float best = float.MinValue;
            List<V2> selectedPath = null;
            V2 bestPosition = s.Position;
            // Bounded local search. Distance to the known contact must improve, or
            // a firing lane must be gained; never use a hidden enemy coordinate.
            for (int row = 0; row < 3; row++)
                for (int side = 0; side < 2; side++)
                {
                    float step = row == 0 ? 3.5f : row == 1 ? 5.5f : 8f;
                    float spread = (side == 0 ? 1 : -1) * wing * (row == 2 ? 6f : 3.5f);
                    V2 desired = s.Position + forward * step + lateral * spread;
                    V2 post = TacticalOrders.Open(b, desired);
                    if (!b.Map.Walkable(post) || !CommandHierarchy.WithinManeuver(b, s, post) || PositionLogic.Occupied(b, s, post) || V2.Distance(s.Position, post) < 1.5f)
                        continue;
                    float targetDistance = V2.Distance(post, threat.Position);
                    bool canFire = targetDistance <= range && PositionLogic.CanEngage(b, s, post, threat);
                    float progress = V2.Distance(s.Position, threat.Position) - targetDistance;
                    if (!canFire && progress < 1.5f)
                        continue;
                    float risk = PositionLogic.Exposure(b, s, post);
                    bool cover = PositionLogic.Protected(b, s, post);
                    if (!aggressive && (!cover && risk > .35f || V2.Distance(s.Position, post) > 3.8f || V2.Distance(post, b.Soldiers[s.BuddyId].Position) > 7f))
                        continue;
                    float score = (canFire ? 18f : 0f) + progress * .7f + (cover ? 3f : 0f) - risk * (aggressive ? 1.2f : 3f) - Math.Abs(targetDistance - range * .85f) * .3f;
                    if (score <= best)
                        continue;
                    var path = LossMemory.Find(b, s, post, p => PositionLogic.Exposure(b, s, p) * (aggressive ? 1f : 2.5f));
                    b.Stats.Paths++;
                    if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 17f)
                        continue;
                    best = score;
                    selectedPath = path;
                    bestPosition = post;
                }

            if (selectedPath == null)
                return false;
            s.Path = selectedPath;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = bestPosition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.WasAtAnchor = false;
            s.Job = Job.Reposition;
            s.LastMoved = b.Time;
            s.SupportReady = false;
            s.Tactical.CommitUntil = b.Time + 3f;
            return true;
        }

        static bool DecideHelper(BattleSimulation b, Soldier helper)
        {
            if (b.Time >= helper.HelpUntil || helper.HelpTargetId >= b.Soldiers.Count)
            {
                helper.HelpTargetId = -1;
                return false;
            }

            var victim = b.Soldiers[helper.HelpTargetId];
            if (!victim.Alive || victim.Side != helper.Side || victim.Suppression < .27f)
            {
                helper.HelpTargetId = -1;
                return false;
            }

            var threat = KnownThreat(b, victim) ?? KnownThreat(b, helper);
            bool lane = threat != null && PositionLogic.CanEngage(b, helper, helper.Position, threat);
            if (threat != null && b.Time >= helper.HelpNextSmoke && (helper.HelpSmokePreferred || !lane || victim.Suppression > .87f))
            {
                helper.HelpNextSmoke = b.Time + 3f;
                TryHelpSmoke(b, helper, victim, threat);
            }

            // A short verbal/local report carries a *known* contact, never the current
            // position of an enemy hidden from the squad. Firing still requires line of fire.
            if (threat != null && (!helper.Contacts.TryGetValue(threat.EnemyId, out var known) || known.SeenAt < threat.SeenAt))
            {
                var report = threat.Copy();
                report.Reported = true;
                helper.Contacts[threat.EnemyId] = report;
            }

            // Let UpdateSoldier do the normal weapon fire with real ammo/cooldowns.
            // The temporary Cover job keeps the support soldier from wandering away.
            if (threat != null && PositionLogic.CanEngage(b, helper, helper.Position, threat))
            {
                TacticalAI.Stop(helper);
                helper.Job = Job.Cover;
                helper.MoveAuthorized = true;
                helper.ManeuverPaused = false;
                if (!helper.Sniper && helper.Role != Role.Medic)
                {
                    helper.SuppressUntil = Math.Max(helper.SuppressUntil, b.Time + 1.4f);
                    helper.NextSuppressDecision = Math.Max(helper.NextSuppressDecision, b.Time + 1.4f);
                }

                helper.Reason = "Выручка товарища: держу противника под огнём";
                helper.Order = "Прикрываю " + victim.Name;
                return true;
            }

            if (helper.HelpRouteUntil > b.Time && helper.PathCursor < helper.Path.Count)
            {
                helper.MoveAuthorized = true;
                helper.ManeuverPaused = false;
                return true;
            }

            if (threat != null && b.Time >= helper.HelpNextRoute && TrySupportRoute(b, helper, victim, threat))
                return true;
            return false; // No reachable post: resume own task instead of waiting pointlessly.
        }

        static bool TrySupportRoute(BattleSimulation b, Soldier helper, Soldier victim, Contact threat)
        {
            helper.HelpNextRoute = b.Time + 2.5f;
            V2 forward = (threat.Position - helper.Position).Normalized;
            if (forward.Length < .1f)
                return false;
            V2 lateral = new V2(-forward.Y, forward.X);
            for (int i = 0; i < 6; i++)
            {
                V2 p = TacticalOrders.Open(b, helper.Position + forward * (2f + (i / 2) * 2f) + lateral * (i % 2 == 0 ? 3f : -3f));
                if (!b.Map.Walkable(p) || !CommandHierarchy.WithinManeuver(b, helper, p) || PositionLogic.Occupied(b, helper, p) || V2.Distance(p, victim.Position) > 11f || V2.Distance(helper.Position, p) < 1f || !PositionLogic.CanEngage(b, helper, p, threat))
                    continue;
                var path = Pathfinder.Find(b.Map, helper.Position, p, q => PositionLogic.Exposure(b, helper, q) * 2f);
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(helper.Position, path) > 10f)
                    continue;
                helper.Path = path;
                helper.PathCursor = 0;
                helper.OrderAnchor = helper.Destination = p;
                helper.Job = Job.Reposition;
                helper.MoveAuthorized = true;
                helper.ManeuverPaused = false;
                helper.HelpRouteUntil = b.Time + 5f;
                helper.LastMoved = b.Time;
                helper.Order = "Выручка: занимаю сектор поддержки";
                helper.Reason = "Сближаюсь для огневого прикрытия товарища";
                return true;
            }

            return false;
        }

        public static bool TryHelpSmoke(BattleSimulation b, Soldier helper, Soldier victim, Contact threat)
        {
            bool used = SmokeTactics.Request(b, b.Team(helper), victim, victim.OrderAnchor, SmokePurpose.Rescue, helper);
            if (used)
                helper.HelpActions++;
            return used;
        }

        static bool TryRetreat(BattleSimulation b, Soldier s)
        {
            s.HelpRetreatNextRoute = b.Time + 1.5f;
            var threat = KnownThreat(b, s);
            V2 back = threat == null ? TacticalOrders.Axis(b, s.Side) * -1f : (s.Position - threat.Position).Normalized;
            if (back.Length < .1f)
                back = TacticalOrders.Axis(b, s.Side) * -1f;
            // A short independent displacement — never teleport and never require a buddy.
            var position = TacticalOrders.Open(b, s.Position + back * 4f);
            if (V2.Distance(position, s.Position) < .7f)
                return false;
            var path = LossMemory.Find(b, s, position, p => PositionLogic.Exposure(b, s, p) * 2f);
            b.Stats.Paths++;
            if (path.Count == 0)
                return false;
            s.Path = path;
            s.PathCursor = 0;
            s.OrderAnchor = s.Destination = position;
            s.HelpRetreatPlanned = true;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.WasAtAnchor = false;
            s.SupportReady = false;
            s.Reason = "Прижали: откатываюсь и меняю позицию";
            s.Order = "Перехожу назад под прикрытие";
            return true;
        }

        public static void AwardRelief(BattleSimulation b, Soldier s)
        {
            if (!s.HelpReliefEligible || s.HelpRetreatUntil > b.Time || s.Suppression > .32f)
                return;
            s.HelpReliefEligible = false;
            // A one-time small rally; cannot be farmed without fresh incoming fire.
            s.Morale = Util.Clamp(s.Morale + .035f, 0, s.MoraleMaximum);
            b.Stats.Rallies++;
        }
    }
}
