using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // A medic without a patient is a cautious rifleman, not a static observer.
    public static class MedicTactics
    {
        public static bool Front(BattleSimulation b, Soldier s, out float front)
        {
            front = float.MinValue;
            bool found = false;
            var axis = TacticalOrders.Axis(b, s.Side);
            foreach (var mate in b.Soldiers)
                if (
                    b.Team(s).Contains(mate)
                    && mate.Id != s.Id
                    && mate.Deployed
                    && mate.Available
                    && !mate.Broken
                    && mate.Role != Role.Medic
                )
                {
                    front = Math.Max(front, V2.Dot(mate.Position, axis));
                    found = true;
                }

            return found;
        }

        public static bool AllowStep(BattleSimulation b, Soldier s, V2 next)
        {
            return AllowStepFrom(b, s, s.Position, next);
        }

        struct MovementBoundary
        {
            public V2 Axis,
                Partner;
            public float Front;
            public bool LastDuo;

            public bool Allows(V2 from, V2 next)
            {
                float current = V2.Dot(from, Axis),
                    wanted = V2.Dot(next, Axis);
                if (LastDuo && V2.Distance(next, Partner) <= 6 && wanted <= Front + 3)
                    return true;
                return wanted <= current + .0001f || wanted <= Front + .5f;
            }

            public bool AllowsSegment(V2 from, V2 next)
            {
                float current = V2.Dot(from, Axis),
                    wanted = V2.Dot(next, Axis);
                if (wanted <= current + .0001f || wanted <= Front + .5f)
                    return true;
                if (!LastDuo || wanted > Front + 3 || V2.Distance(next, Partner) > 6)
                    return false;
                // The union of the rear half-plane and buddy circle is not convex.
                // Check where a forward edge first leaves that half-plane: entering
                // the circle only at its endpoint cannot authorize forbidden early steps.
                V2 entering =
                    current >= Front + .5f
                        ? from
                        : from + (next - from) * ((Front + .5f - current) / (wanted - current));
                return V2.Distance(entering, Partner) <= 6;
            }
        }

        // Snapshot only for this synchronous query: no stale cross-tick front cache.
        static bool TryBoundary(BattleSimulation b, Soldier s, out MovementBoundary boundary)
        {
            boundary = new MovementBoundary();
            if (
                ObjectiveOpportunity.IsCapturer(b, s)
                || s.Role != Role.Medic
                || !s.Available
                || s.RescueTargetId >= 0
                || s.RescuerId >= 0
                || s.TreatmentUntil > b.Time
                || s.GrenadeEscape.Active
                || FriendlyGrenadeSafety.Active(b, s)
                || WithdrawalTactics.Active(b.Team(s))
                || TacticalOrders.Emergency(b.Team(s))
            )
                return false;
            float front;
            if (!Front(b, s, out front))
                return false;
            boundary.Front = front;
            boundary.Axis = TacticalOrders.Axis(b, s.Side);
            if (b.Team(s).CommandPlan.LastDuo && s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count)
            {
                var partner = b.Soldiers[s.BuddyId];
                if (partner != s && partner.Available)
                {
                    boundary.LastDuo = true;
                    boundary.Partner = partner.Position;
                }
            }

            return true;
        }

        // The executor and route planner use the same hypothetical edge origin.
        public static bool AllowStepFrom(BattleSimulation b, Soldier s, V2 from, V2 next)
        {
            MovementBoundary boundary;
            return !TryBoundary(b, s, out boundary) || boundary.Allows(from, next);
        }

        public static bool AllowSegment(BattleSimulation b, Soldier s, V2 from, V2 next)
        {
            MovementBoundary boundary;
            return !TryBoundary(b, s, out boundary) || boundary.AllowsSegment(from, next);
        }

        public static bool AllowRoute(BattleSimulation b, Soldier s, List<V2> route, int cursor = 0)
        {
            MovementBoundary boundary;
            if (!TryBoundary(b, s, out boundary))
                return true;
            V2 from = s.Position;
            for (int i = cursor; i < route.Count; i++)
            {
                if (!boundary.AllowsSegment(from, route[i]))
                    return false;
                from = route[i];
            }

            return true;
        }

        public static bool FreeToFight(BattleSimulation b, Soldier s)
        {
            return s.Role == Role.Medic
                && s.Available
                && !s.Broken
                && s.RescueTargetId < 0
                && s.RescuerId < 0
                && s.TreatmentUntil <= b.Time
                && s.SupplyDonorId < 0
                && s.SupplyReceiverId < 0
                && s.Ammo + s.Reserve > 0;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (!FreeToFight(b, s))
                return false;
            var q = b.Team(s);
            if (q.Style == CombatStyle.Withdraw || TacticalOrders.Emergency(q))
                return false;
            if (
                b.Time < s.MedicCombatCommitUntil
                || b.Time < s.MedicCombatCommitUntil + 8f
                    && object.ReferenceEquals(s.Path, s.MedicCombatRoute)
                    && s.PathCursor < s.Path.Count
                    && b.Time - s.Progress.ProgressAt < 2.5f
            )
            {
                bool owns =
                    object.ReferenceEquals(s.Path, s.MedicCombatRoute)
                    && V2.Distance(s.OrderAnchor, s.MedicCombatGoal) < .2f;
                if (!owns)
                {
                    s.MedicCombatCommitUntil = 0;
                    s.MedicCombatRoute = null;
                    return false; // A later executor owns this route, even if it violates our front limit.
                }

                if (s.PathCursor < s.Path.Count && AllowRoute(b, s, s.Path, s.PathCursor))
                    return true;
                if (s.PathCursor < s.Path.Count)
                {
                    TacticalAI.Stop(s);
                    s.OrderAnchor = s.Destination = s.Position;
                }

                s.MedicCombatCommitUntil = 0;
                s.MedicCombatRoute = null;
            }

            bool personalShot = false;
            foreach (var contact in s.Contacts.Values)
                if (s.Visible.Contains(contact.EnemyId) && PositionLogic.CanEngage(b, s, s.Position, contact))
                {
                    personalShot = true;
                    break;
                }

            if (personalShot && PositionLogic.Protected(b, s, s.Position))
            {
                if (s.PathCursor < s.Path.Count)
                    TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.Reason = "Медицинских задач нет — веду огонь с позиции";
                return true;
            }

            if (b.Time < s.MedicCombatReviewAt || s.Suppression > .65f || s.Health < 35f || s.Morale < .3f)
                return false;
            s.MedicCombatReviewAt = b.Time + 2f;
            Contact threat = TacticalOrders.ClosestKnown(b, s);
            var direction = TacticalOrders.Axis(b, s.Side);
            V2 center = new V2();
            int count = 0;
            foreach (var mate in b.Soldiers)
                if (q.Contains(mate) && mate.Alive && !mate.Incapacitated && mate.Id != s.Id && mate.Deployed)
                {
                    center += mate.Position;
                    count++;
                }

            if (count == 0)
                return false;
            center = center / count;
            bool hasThreat = threat != null;
            // Keep slightly behind the fighting element. Never lead a solo breakthrough.
            V2 target = hasThreat ? center - direction * 1.2f : center - direction * 2.5f;
            // Honour the pre-mission depth preference without displacing an urgent patient.
            if (!q.CommandPlan.LastDuo)
                target = FormationAI.Desired(b, s, target, false);
            if (V2.Distance(s.Position, center) < 4f && !hasThreat)
                return false;
            var delta = target - s.Position;
            if (delta.Length > 3.5f)
                target = s.Position + delta.Normalized * 3.5f;
            float currentRisk = PositionLogic.Exposure(b, s, s.Position);
            float currentScore =
                (hasThreat && PositionLogic.CanEngage(b, s, s.Position, threat) ? 12f : 0f)
                + (PositionLogic.Protected(b, s, s.Position) ? 5f : 0f)
                - currentRisk * 8f
                - V2.Distance(s.Position, target)
                - FormationEnvelope.LaneCost(b, s, s.Position);
            float best = currentScore + 1.5f;
            V2 post = s.Position;
            for (int i = 0; i < 25; i++)
            {
                float side = (i % 5 - 2) * 1.25f;
                float fore = (i / 5 - 2) * 1.25f;
                var lat = new V2(-direction.Y, direction.X);
                V2 test = TacticalOrders.Open(b, target + lat * side + direction * fore);
                if (
                    !PositionLogic.UsefulLocalChange(b, s, test)
                    || !AllowStep(b, s, test)
                    || !b.Map.Walkable(test)
                    || PositionLogic.Occupied(b, s, test)
                    || V2.Distance(test, s.Position) < .9f
                )
                    continue;
                float risk = PositionLogic.Exposure(b, s, test);
                bool sheltered = PositionLogic.Protected(b, s, test);
                // Prefer cover strongly, but allow cautious open-ground movement in a
                // genuinely safe sector: an all-open map must not imprison the medic.
                if (risk > (sheltered ? .65f : .25f))
                    continue;
                if (hasThreat && V2.Distance(test, threat.Position) < 3.5f)
                    continue;
                float score =
                    (hasThreat && PositionLogic.CanEngage(b, s, test, threat) ? 12f : 0f)
                    + (sheltered ? 5f : 0f)
                    - risk * 8f
                    - V2.Distance(test, target)
                    - FormationEnvelope.LaneCost(b, s, test)
                    - FormationEnvelope.ShuffleCost(b, s, test)
                    - V2.Distance(test, s.Position) * .25f;
                if (score > best)
                {
                    best = score;
                    post = test;
                }
            }

            if (V2.Distance(post, s.Position) < .9f)
                return false;
            var path = LossMemory.Find(
                b,
                s,
                post,
                p => PositionLogic.Exposure(b, s, p) * 3f + FireLaneSafety.PathCost(b, s, p)
            );
            b.Stats.Paths++;
            if (
                path.Count == 0
                || !AllowRoute(b, s, path)
                || !PositionLogic.UsefulRoute(b, s, path)
                || SupportAI.PathLength(s.Position, path) > 8f
            )
                return false;
            s.Path = path;
            s.PathCursor = 0;
            s.OrderAnchor = s.Destination = post;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.MedicCombatCommitUntil = b.Time + 4f;
            s.MedicCombatRoute = path;
            s.MedicCombatGoal = post;
            s.Reason = "Нет пациента — осторожно занимаю огневой рубеж";
            return true;
        }
    }
}
