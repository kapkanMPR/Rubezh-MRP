using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class TacticalAI
    {
        public static Contact FireTarget(BattleSimulation b, Soldier s)
        {
            if (!s.Available || s.Broken)
            {
                return null;
            }

            Contact contact = null;
            float bestScore = float.MinValue;
            foreach (Contact value in s.Contacts.Values)
            {
                float contactAge = b.Time - value.SeenAt;
                float contactDistance = V2.Distance(s.Position, value.Position);
                bool visuallyConfirmed = value.Visual && !value.Inferred && s.Visible.Contains(value.EnemyId);
                float memoryDuration = ((s.Role == Role.MachineGunner) ? 7 : 4);
                if (
                    (
                        !visuallyConfirmed
                        && !CounterSniper.AreaFire(b, s, value)
                        && (contactAge > memoryDuration || !value.Visual || !CombatCoordination.CanSuppress(b, s))
                    )
                    || (contactDistance > b.WeaponRange(s) && !EngagementPolicy.EmergencyAllowed(b, s, value))
                    || !b.Map.LineOfSight(s.Position, value.Position)
                )
                {
                    continue;
                }

                if (b.FriendlyInLane(s, value.Position) || value.Inferred && !CounterSniper.SafeFireLane(b, s, value))
                    continue;
                if (visuallyConfirmed && b.SmokeTransmission(s.Position, value.Position) < .25f)
                    visuallyConfirmed = false;
                if (
                    !visuallyConfirmed
                    && !CounterSniper.AreaFire(b, s, value)
                    && (!value.Visual || contactAge > memoryDuration || !CombatCoordination.CanSuppress(b, s))
                )
                    continue;
                float targetScore = (float)(visuallyConfirmed ? 8 : 0) + 8f / (contactDistance + 1f) - contactAge;
                if (s.Side == Side.Green && CombatCoordination.CanSuppress(b, s) && contactAge <= 8)
                {
                    // A visible immediate threat still permits self-defence. Otherwise,
                    // suppress the observed heavy weapon before an ordinary rifleman.
                    targetScore +=
                        visuallyConfirmed && contactDistance < 4 ? 1000 : SuppressionPriority.Rank(value) * 40;
                }

                foreach (int movingId in b.Team(s).ManeuverIds)
                {
                    var moving = b.Soldiers[movingId];
                    if (
                        moving.Id != s.Id
                        && moving.Available
                        && PositionLogic.Recent(b, value)
                        && b.Map.LineOfSight(value.Position, moving.Position)
                        && V2.Distance(value.Position, moving.Position) < b.Config.WeaponRange + 4
                    )
                    {
                        targetScore += 6;
                        break;
                    }
                }

                targetScore += ReconOpening.Priority(b, s, value.EnemyId);
                if (s.Sniper)
                {
                    if (
                        !visuallyConfirmed
                        || b.FriendlyInLane(s, value.Position)
                        || b.SmokeTransmission(s.Position, value.Position) < .3f
                    )
                        continue;
                    targetScore += (6 - value.TargetPriority) * 1.5f + (b.Time - value.FireSeenAt < 3 ? 6 : 0);
                }
                else if (b.Team(s).FocusEnemyId == value.EnemyId)
                {
                    targetScore += 5f;
                }

                if (s.Role == Role.MachineGunner && CombatCoordination.Pressuring(b, s, value))
                {
                    targetScore += 10f;
                }

                foreach (Soldier soldier in b.Soldiers)
                {
                    if (
                        soldier.Alive
                        && soldier.Side == s.Side
                        && soldier.Id != s.Id
                        && b.Time - soldier.LastFire < 1f
                        && V2.Dot(soldier.Facing, (value.Position - soldier.Position).Normalized) > 0.97f
                    )
                    {
                        targetScore -= 1.3f;
                    }
                }

                if (targetScore > bestScore)
                {
                    bestScore = targetScore;
                    contact = value;
                }
            }

            CombatCoordination.ChooseFireMode(b, s, contact);
            if (CounterSniper.AreaFire(b, s, contact) && !s.Sniper && s.Ammo >= 3)
                s.SuppressiveFire = true;
            return SomaliFire.SelectTarget(
                b,
                s,
                AreaSuppression.Select(
                    b,
                    s,
                    (contact != null && !s.Visible.Contains(contact.EnemyId) && !s.SuppressiveFire) ? null : contact
                )
            );
        }

        public static void Stop(Soldier s)
        {
            s.Path.Clear();
            s.PathCursor = 0;
            s.Destination = s.Position;
            s.Moving = false;
        }

        public static bool SafeTravel(BattleSimulation b, Soldier s)
        {
            if (s.Broken || s.Suppression > 0.25f || b.Time - s.LastIncoming < 5f)
            {
                return false;
            }

            Squad squad = b.Team(s);
            foreach (Contact value in Knowledge.Contacts(b, s))
            {
                if (
                    value.SeenAt <= b.Time
                    && b.Time - value.SeenAt < b.Config.ContactLifetime
                    && value.Confidence > 0.25f
                    && RouteExposed(b, s, value.Position)
                )
                {
                    return false;
                }
            }

            return true;
        }

        public static bool SafeApproach(BattleSimulation b, Soldier s)
        {
            V2 next =
                s.PathCursor < s.Path.Count
                    ? s.Path[s.PathCursor]
                    : (s.ManeuverPaused ? s.SavedManeuverAnchor : s.OrderAnchor);
            return SafeApproachTo(b, s, next);
        }

        public static bool SafeApproachTo(BattleSimulation b, Soldier s, V2 next)
        {
            if (!s.Available || s.Broken || s.Suppression > 0.25f || b.Time - s.LastIncoming < 5f)
            {
                return false;
            }

            V2 a = next;
            V2 v = a - s.Position;
            a = s.Position + v.Normalized * Math.Min(3f, v.Length);
            foreach (Contact value in Knowledge.Contacts(b, s))
            {
                if (
                    value.SeenAt <= b.Time
                    && b.Time - value.SeenAt < b.Config.ContactLifetime
                    && value.Confidence > 0.25f
                    && LocalExposed(b, s.Position, a, value)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LocalExposed(BattleSimulation b, V2 start, V2 end, Contact contact)
        {
            var threat = contact.Position;
            float range =
                contact.ObservedWeaponRange > 0 ? contact.ObservedWeaponRange + .75f : b.Config.WeaponRange + 5f;
            for (int i = 0; i <= 6; i++)
            {
                V2 v = start + (end - start) * ((float)i / 6f);
                if (V2.Distance(v, threat) <= range && b.Map.LineOfSight(threat, v))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RouteExposed(BattleSimulation b, Soldier s, V2 threat)
        {
            V2 v = s.Position;
            int firstWaypoint = ((s.PathCursor < s.Path.Count) ? s.PathCursor : 0);
            int waypointEnd = ((s.PathCursor >= s.Path.Count) ? 1 : s.Path.Count);
            for (int i = firstWaypoint; i < waypointEnd; i++)
            {
                V2 waypoint = ((s.PathCursor < s.Path.Count) ? s.Path[i] : s.OrderAnchor);
                int sampleCount = Math.Max(1, (int)Math.Ceiling(V2.Distance(v, waypoint)));
                for (int j = 0; j <= sampleCount; j++)
                {
                    V2 v3 = v + (waypoint - v) * ((float)j / (float)sampleCount);
                    if (V2.Distance(v3, threat) <= b.Config.WeaponRange + 4f && b.Map.LineOfSight(threat, v3))
                    {
                        return true;
                    }
                }

                v = waypoint;
            }

            return false;
        }

        public static void Decide(BattleSimulation b, Soldier s)
        {
            try
            {
                DecideCore(b, s);
            }
            finally
            {
                AiTasks.Observe(b, s);
            }
        }

        private static void DecideCore(BattleSimulation b, Soldier s)
        {
            if (!s.Available)
                return;
            bool recentlyUnderFire = b.Time - s.LastIncoming < 4f;
            if (s.Morale <= .001f && recentlyUnderFire && !s.Broken)
                MoraleDynamics.EnterBroken(b, s);
            if (s.Broken)
            {
                s.State = ActionState.Cower;
                s.Crouched = true;
                s.MoveAuthorized = false;
                if (!MoraleDynamics.CanRecover(b, s))
                {
                    if (PositionLogic.Protected(b, s, s.Position))
                    {
                        Stop(s);
                    }
                    else if (s.PathCursor >= s.Path.Count)
                    {
                        SeekLocalShelter(b, s, 2.6f);
                    }

                    s.Reason = "Мораль сломлена: прижался, не веду бой и не считаюсь прикрытием";
                    return;
                }

                s.Broken = false;
                s.NextDecision = 0f;
                s.MoveAuthorized = b.Team(s).Phase == ManeuverPhase.Move;
                Dialogue.Say(b, s, 11, urgent: true);
                s.Reason = "Восстановился после затишья; возвращаюсь к приказу";
            }

            if (s.Busy || s.SupplyReceiverId >= 0 || b.Time < s.ConcussionReactUntil)
                return;
            // The commander owns this finite, checked route. Local idle-role reviews
            // must not steal it before the runner can take its first step.
            if (
                ObjectiveOpportunity.IsCapturer(b, s)
                && !TacticalOrders.ServiceActive(s)
                && ObjectiveOpportunity.Decide(b, s)
            )
                return;
            // Stationary exposed units must not be starved by a persistent tactical owner.
            // A progressing bound keeps its route; medical/emergency exclusions live in ShelterEligible.
            if ((PositionLogic.OwnsShelter(s) || b.Time - s.LastMoved > 2.5f) && PositionLogic.ShelterReview(b, s))
                return;
            bool specialistEligible =
                PositionLogic.ShelterEligible(b, s)
                && !TacticalOrders.ServiceActive(s)
                && s.LootTargetId < 0
                && s.LootGroundId < 0
                && s.WeaponAidTargetId < 0
                && s.LauncherPickupId < 0;
            bool sniperReviewed =
                s.Sniper
                && specialistEligible
                && (s.SniperMoving || s.PathCursor >= s.Path.Count || b.Time - s.LastMoved > 2.5f);
            if (sniperReviewed && SniperTactics.Plan(b, s))
                return;
            bool medicReviewed =
                s.Role == Role.Medic
                && specialistEligible
                && (object.ReferenceEquals(s.Path, s.MedicCombatRoute) || b.Time - s.LastMoved > 2.5f);
            if (medicReviewed && MedicTactics.Decide(b, s))
                return;
            if (
                ReconOpening.Decide(b, s)
                || QuietRecon.Decide(b, s)
                || PairTacticalPlan.Decide(b, s)
                || ContactMovement.Decide(b, s)
                || TacticalMarch.Decide(b, s)
            )
                return;
            if (EngagementPolicy.Active(b, s))
                return;
            if (WithdrawalTactics.Active(b.Team(s)) && WithdrawalTactics.Decide(b, s))
                return;
            if (b.Team(s).Style == CombatStyle.Passive)
            {
                if (PositionLogic.ShelterReview(b, s))
                    return;
                if (CommandPosture.Decide(b, s))
                    return;
            }

            if (CounterSniper.Decide(b, s))
                return;
            if (LastOpponentHunt.Decide(b, s))
                return;
            if (CommandHierarchy.DuoMarch(b, s))
                return;
            if (PositionLogic.ShelterReview(b, s))
                return;
            if (BreakthroughTactics.SoldierPlan(b, s))
                return;
            if (ObjectiveOpportunity.Decide(b, s))
                return;
            if (StraightAdvance.Hold(b, s))
                return;
            if (ProgressRecovery.Decide(b, s))
                return;
            if (PositionLogic.UsefulPositionReview(b, s))
                return;
            if (SniperRetreatCover.Hold(b, s))
                return;
            if (AreaSuppression.HoldDecision(b, s))
                return;
            if (s.FlagResponseUntil > b.Time && s.Side == Side.Red && s.PathCursor < s.Path.Count)
            {
                s.MoveAuthorized = true;
                return;
            }

            if ((s.HelpUntil > b.Time || s.HelpRetreatUntil > b.Time) && CombatCohesion.Decide(b, s))
                return;
            if (CommanderCooperation.Decide(b, s))
                return;
            if (!s.Sniper && PositionLogic.Review(b, s))
                return;
            if (RedCounterattack.Decide(b, s))
                return;
            // Controlled withdrawal owns all movement; no loot or rocket route can steal it.
            if (WithdrawalTactics.Active(b.Team(s)) && TacticalOrders.Decide(b, s))
                return;
            if (LauncherLogistics.PlanPickup(b, s))
                return;
            if (
                (s.LootTargetId >= 0 || s.LootGroundId >= 0 || s.WeaponAidTargetId >= 0)
                && s.PathCursor < s.Path.Count
                && !TacticalOrders.Emergency(b.Team(s))
            )
                return;
            if (FiringPositionPolicy.Hold(b, s))
                return;
            if (!medicReviewed && MedicTactics.Decide(b, s))
                return;
            if (TacticalOrders.Decide(b, s))
                return;
            if (b.Time < s.ConcussionReactUntil)
                return;
            if (!s.Available)
            {
                return;
            }

            // A global damage revision is not evidence that this soldier lost cover.
            // In particular, never cancel an intentionally open advance/capture anchor.
            bool anchorExposed = PositionLogic.AnchorLostCover(b, s);
            s.KnownMapRevision = b.Map.Revision;
            if (anchorExposed)
            {
                V2 replacement;
                if (PositionLogic.TryPost(b, s, s.OrderAnchor, 6, true, out replacement))
                {
                    s.OrderAnchor = replacement;
                    s.Destination = replacement;
                    s.Path.Clear();
                    s.PathCursor = 0;
                    s.WasAtAnchor = false;
                    b.Say(s, "Укрытие разбито, ищу защиту!", urgent: true);
                }
            }

            if (s.ReloadUntil > b.Time)
            {
                return;
            }

            if (s.Sniper && !sniperReviewed && SniperTactics.Plan(b, s))
                return;
            if (BreakthroughTactics.SoldierPlan(b, s))
                return;
            if (
                !PositionLogic.Protected(b, s, s.Position)
                && (anchorExposed || recentlyUnderFire || Risk(b, s, s.Position) > 0.25f)
                && s.PathCursor >= s.Path.Count
                && SeekLocalShelter(b, s, 4.2f)
            )
            {
                s.OrderAnchor = s.Destination;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Job = Job.Reposition;
                s.WasAtAnchor = false;
                s.Reason = "Остался без защиты: занимаю ближайшее свободное укрытие";
                return;
            }

            if (s.ManeuverPaused && SafeApproach(b, s) && V2.Distance(s.Position, s.SavedManeuverAnchor) > 3f)
            {
                s.OrderAnchor = s.SavedManeuverAnchor;
                s.ManeuverPaused = false;
                s.Job = Job.Reposition;
                s.MoveAuthorized = false;
            }

            if (s.ManeuverPaused)
            {
                Stop(s);
                s.Reason = "Удерживаю позицию и прикрываю смену рубежа поддержки";
                return;
            }

            Soldier soldier = b.Soldiers[s.BuddyId];
            bool holdingPosition = s.Job == Job.Cover || s.Job == Job.Hold || s.Job == Job.Command;
            if (
                soldier.Alive
                && soldier.ReloadUntil > b.Time
                && b.Map.NearCover(s.Position)
                && !s.Broken
                && V2.Distance(soldier.Position, s.Position) < 8f
                && holdingPosition
                && !SafeApproach(b, s)
            )
            {
                Stop(s);
                s.Reason = "Держу сектор, пока напарник перезаряжается";
                Dialogue.Say(b, s, 6);
            }
            else
            {
                if (
                    recentlyUnderFire
                    && (s.Suppression > ((b.Team(s).Style == CombatStyle.Aggressive) ? 0.78f : 0.65f) || s.Health < 35f)
                    && !PositionLogic.Protected(b, s, s.Position)
                    && s.PathCursor >= s.Path.Count
                    && SeekLocalShelter(b, s, 3.2f)
                )
                {
                    return;
                }

                if (!s.MoveAuthorized && !holdingPosition && !SafeApproach(b, s))
                {
                    Stop(s);
                    s.State = ActionState.WaitCover;
                    s.Reason = "Приказ получен. Жду подтвержденного прикрытия и сигнала командира";
                    Dialogue.Say(b, s, 4);
                }
                else if (V2.Distance(s.Position, s.OrderAnchor) < 0.55f)
                {
                    Stop(s);
                    if (!s.WasAtAnchor)
                    {
                        if (s.Job == Job.Reposition)
                        {
                            s.Job = Job.Hold;
                        }
                        else if (s.Job == Job.Rally)
                        {
                            s.Job = Job.Cover;
                        }

                        s.WasAtAnchor = PositionLogic.Arrived(b, s);
                        s.SettledSince = b.Time;
                        b.Stats.Arrivals++;
                        Dialogue.Say(b, s, 3);
                    }

                    s.Facing = (SquadBrain.Threat(b, b.Team(s)) - s.Position).Normalized;
                    s.Reason =
                        s.Tactical.Status
                        + (s.SupportReady ? "; поддержка готова" : "; проверяю возможность поддержки");
                }
                else if (s.PathCursor < s.Path.Count)
                {
                    s.Reason = "Выполняю общий маневр к назначенному укрытию";
                }
                else if (b.Time < s.RejectedUntil && V2.Distance(s.OrderAnchor, s.RejectedPost) < 0.8f)
                {
                    s.Reason = "Маршрут заблокирован; сообщил командиру";
                }
                else
                {
                    BuildPath(b, s, s.OrderAnchor);
                    s.Reason = (
                        holdingPosition
                            ? "Занимаю назначенную командиром позицию прикрытия"
                            : "Перебегаю с группой под подтвержденным прикрытием"
                    );
                }
            }
        }

        public static bool SeekLocalShelter(BattleSimulation b, Soldier s, float radius)
        {
            return PositionLogic.Correct(b, s, radius);
        }

        private static void BuildPath(BattleSimulation b, Soldier s, V2 target)
        {
            b.Stats.Paths++;
            System.Collections.Generic.List<V2> list = LossMemory.Find(
                b,
                s,
                target,
                (V2 p) =>
                    Risk(b, s, p) * ((b.Team(s).Style == CombatStyle.Aggressive) ? 1.2f : 2f)
                    + OccupancyCost(b, s, p)
                    + FireLaneSafety.PathCost(b, s, p)
                    + (b.Map.NearCover(p) ? 0f : 0.45f)
            );
            if (list.Count == 0)
            {
                b.Stats.FailedPaths++;
                s.RejectedPost = target;
                s.RejectedUntil = b.Time + 10f;
                s.Tactical.Block = "Маршрут недоступен";
                EventVoice.Emit(b, s, 10);
                b.Team(s).NextPlan = 0;
                Stop(s);
                return;
            }

            s.Path = list;
            s.PathCursor = 0;
            s.Destination = target;
            s.LastMoved = b.Time;
            if (b.Map.NearCover(target))
            {
                b.Stats.CoverMoves++;
            }
        }

        public static float Risk(BattleSimulation b, Soldier s, V2 p)
        {
            return PositionLogic.Exposure(b, s, p);
        }

        private static float OccupancyCost(BattleSimulation b, Soldier s, V2 p)
        {
            float totalRisk = 0f;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Alive && soldier.Id != s.Id && soldier.Side == s.Side)
                {
                    float proximity = Math.Min(V2.Distance(p, soldier.Position), V2.Distance(p, soldier.OrderAnchor));
                    if (proximity < 1.5f)
                    {
                        totalRisk += (float)(soldier.Busy ? 20 : 12);
                    }
                    else if (proximity < 3f)
                    {
                        totalRisk += (3f - proximity) * 2f;
                    }
                }
            }

            return totalRisk;
        }
    }
}
