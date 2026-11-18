using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class TacticalAI
    {
        public static SilentOrders.Core.Contact FireTarget(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (!s.Available || s.Broken)
            {
                return null;
            }

            SilentOrders.Core.Contact contact = null;
            float num = float.MinValue;
            foreach (SilentOrders.Core.Contact value in s.Contacts.Values)
            {
                float num2 = b.Time - value.SeenAt;
                float num3 = SilentOrders.Core.V2.Distance(s.Position, value.Position);
                bool flag = value.Visual && !value.Inferred && s.Visible.Contains(value.EnemyId);
                float num4 = ((s.Role == SilentOrders.Core.Role.MachineGunner) ? 7 : 4);
                if ((!flag && !CounterSniper.AreaFire(b, s, value) && (num2 > num4 || !value.Visual || !SilentOrders.Core.CombatCoordination.CanSuppress(b, s))) || (num3 > b.WeaponRange(s) && !EngagementPolicy.EmergencyAllowed(b, s, value)) || !b.Map.LineOfSight(s.Position, value.Position))
                {
                    continue;
                }

                if (b.FriendlyInLane(s, value.Position) || value.Inferred && !CounterSniper.SafeFireLane(b, s, value))
                    continue;
                if (flag && b.SmokeTransmission(s.Position, value.Position) < .25f)
                    flag = false;
                if (!flag && !CounterSniper.AreaFire(b, s, value) && (!value.Visual || num2 > num4 || !CombatCoordination.CanSuppress(b, s)))
                    continue;
                float num5 = (float)(flag ? 8 : 0) + 8f / (num3 + 1f) - num2;
                if (s.Side == Side.Green && CombatCoordination.CanSuppress(b, s) && num2 <= 8)
                {
                    // A visible immediate threat still permits self-defence. Otherwise,
                    // suppress the observed heavy weapon before an ordinary rifleman.
                    num5 += flag && num3 < 4 ? 1000 : SuppressionPriority.Rank(value) * 40;
                }

                foreach (int movingId in b.Team(s).ManeuverIds)
                {
                    var moving = b.Soldiers[movingId];
                    if (moving.Id != s.Id && moving.Available && PositionLogic.Recent(b, value) && b.Map.LineOfSight(value.Position, moving.Position) && V2.Distance(value.Position, moving.Position) < b.Config.WeaponRange + 4)
                    {
                        num5 += 6;
                        break;
                    }
                }

                num5 += ReconOpening.Priority(b, s, value.EnemyId);
                if (s.Sniper)
                {
                    if (!flag || b.FriendlyInLane(s, value.Position) || b.SmokeTransmission(s.Position, value.Position) < .3f)
                        continue;
                    num5 += (6 - value.TargetPriority) * 1.5f + (b.Time - value.FireSeenAt < 3 ? 6 : 0);
                }
                else if (b.Team(s).FocusEnemyId == value.EnemyId)
                {
                    num5 += 5f;
                }

                if (s.Role == SilentOrders.Core.Role.MachineGunner && SilentOrders.Core.CombatCoordination.Pressuring(b, s, value))
                {
                    num5 += 10f;
                }

                foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                {
                    if (soldier.Alive && soldier.Side == s.Side && soldier.Id != s.Id && b.Time - soldier.LastFire < 1f && SilentOrders.Core.V2.Dot(soldier.Facing, (value.Position - soldier.Position).Normalized) > 0.97f)
                    {
                        num5 -= 1.3f;
                    }
                }

                if (num5 > num)
                {
                    num = num5;
                    contact = value;
                }
            }

            SilentOrders.Core.CombatCoordination.ChooseFireMode(b, s, contact);
            if (CounterSniper.AreaFire(b, s, contact) && !s.Sniper && s.Ammo >= 3)
                s.SuppressiveFire = true;
            return SomaliFire.SelectTarget(b, s, AreaSuppression.Select(b, s, (contact != null && !s.Visible.Contains(contact.EnemyId) && !s.SuppressiveFire) ? null : contact));
        }

        public static void Stop(SilentOrders.Core.Soldier s)
        {
            s.Path.Clear();
            s.PathCursor = 0;
            s.Destination = s.Position;
            s.Moving = false;
        }

        public static bool SafeTravel(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (s.Broken || s.Suppression > 0.25f || b.Time - s.LastIncoming < 5f)
            {
                return false;
            }

            SilentOrders.Core.Squad squad = b.Team(s);
            foreach (SilentOrders.Core.Contact value in Knowledge.Contacts(b, s))
            {
                if (value.SeenAt <= b.Time && b.Time - value.SeenAt < b.Config.ContactLifetime && value.Confidence > 0.25f && RouteExposed(b, s, value.Position))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool SafeApproach(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            V2 next = s.PathCursor < s.Path.Count ? s.Path[s.PathCursor] : (s.ManeuverPaused ? s.SavedManeuverAnchor : s.OrderAnchor);
            return SafeApproachTo(b, s, next);
        }

        public static bool SafeApproachTo(BattleSimulation b, Soldier s, V2 next)
        {
            if (!s.Available || s.Broken || s.Suppression > 0.25f || b.Time - s.LastIncoming < 5f)
            {
                return false;
            }

            SilentOrders.Core.V2 a = next;
            SilentOrders.Core.V2 v = a - s.Position;
            a = s.Position + v.Normalized * System.Math.Min(3f, v.Length);
            foreach (SilentOrders.Core.Contact value in Knowledge.Contacts(b, s))
            {
                if (value.SeenAt <= b.Time && b.Time - value.SeenAt < b.Config.ContactLifetime && value.Confidence > 0.25f && LocalExposed(b, s.Position, a, value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LocalExposed(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.V2 start, SilentOrders.Core.V2 end, SilentOrders.Core.Contact contact)
        {
            var threat = contact.Position;
            float range = contact.ObservedWeaponRange > 0 ? contact.ObservedWeaponRange + .75f : b.Config.WeaponRange + 5f;
            for (int i = 0; i <= 6; i++)
            {
                SilentOrders.Core.V2 v = start + (end - start) * ((float)i / 6f);
                if (SilentOrders.Core.V2.Distance(v, threat) <= range && b.Map.LineOfSight(threat, v))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RouteExposed(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 threat)
        {
            SilentOrders.Core.V2 v = s.Position;
            int num = ((s.PathCursor < s.Path.Count) ? s.PathCursor : 0);
            int num2 = ((s.PathCursor >= s.Path.Count) ? 1 : s.Path.Count);
            for (int i = num; i < num2; i++)
            {
                SilentOrders.Core.V2 v2 = ((s.PathCursor < s.Path.Count) ? s.Path[i] : s.OrderAnchor);
                int num3 = System.Math.Max(1, (int)System.Math.Ceiling(SilentOrders.Core.V2.Distance(v, v2)));
                for (int j = 0; j <= num3; j++)
                {
                    SilentOrders.Core.V2 v3 = v + (v2 - v) * ((float)j / (float)num3);
                    if (SilentOrders.Core.V2.Distance(v3, threat) <= b.Config.WeaponRange + 4f && b.Map.LineOfSight(threat, v3))
                    {
                        return true;
                    }
                }

                v = v2;
            }

            return false;
        }

        public static void Decide(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
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
            bool flag2 = b.Time - s.LastIncoming < 4f;
            if (s.Morale <= .001f && flag2 && !s.Broken)
                MoraleDynamics.EnterBroken(b, s);
            if (s.Broken)
            {
                s.State = SilentOrders.Core.ActionState.Cower;
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
                s.MoveAuthorized = b.Team(s).Phase == SilentOrders.Core.ManeuverPhase.Move;
                SilentOrders.Core.Dialogue.Say(b, s, 11, urgent: true);
                s.Reason = "Восстановился после затишья; возвращаюсь к приказу";
            }

            if (s.Busy || s.SupplyReceiverId >= 0 || b.Time < s.ConcussionReactUntil)
                return;
            // The commander owns this finite, checked route. Local idle-role reviews
            // must not steal it before the runner can take its first step.
            if (ObjectiveOpportunity.IsCapturer(b, s) && !TacticalOrders.ServiceActive(s)
                && ObjectiveOpportunity.Decide(b, s)) return;
            if (FormationEnvelope.Decide(b, s)) return;
            // Stationary exposed units must not be starved by a persistent tactical owner.
            // A progressing bound keeps its route; medical/emergency exclusions live in ShelterEligible.
            if ((PositionLogic.OwnsShelter(s) || b.Time - s.LastMoved > 2.5f) && PositionLogic.ShelterReview(b, s))
                return;
            bool specialistEligible = PositionLogic.ShelterEligible(b, s) && !TacticalOrders.ServiceActive(s)
                && s.LootTargetId < 0 && s.LootGroundId < 0 && s.WeaponAidTargetId < 0 && s.LauncherPickupId < 0;
            bool sniperReviewed = s.Sniper && specialistEligible
                && (s.SniperMoving || s.PathCursor >= s.Path.Count || b.Time - s.LastMoved > 2.5f);
            if (sniperReviewed && SniperTactics.Plan(b, s))
                return;
            bool medicReviewed = s.Role == Role.Medic && specialistEligible
                && (object.ReferenceEquals(s.Path, s.MedicCombatRoute) || b.Time - s.LastMoved > 2.5f);
            if (medicReviewed && MedicTactics.Decide(b, s))
                return;
            if (ReconOpening.Decide(b, s) || QuietRecon.Decide(b, s) || PairTacticalPlan.Decide(b, s) || ContactMovement.Decide(b, s) || TacticalMarch.Decide(b, s))
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
            if ((s.LootTargetId >= 0 || s.LootGroundId >= 0 || s.WeaponAidTargetId >= 0) && s.PathCursor < s.Path.Count && !TacticalOrders.Emergency(b.Team(s)))
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
            bool flag = PositionLogic.AnchorLostCover(b, s);
            s.KnownMapRevision = b.Map.Revision;
            if (flag)
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
            if (!PositionLogic.Protected(b, s, s.Position) && (flag || flag2 || Risk(b, s, s.Position) > 0.25f) && s.PathCursor >= s.Path.Count && SeekLocalShelter(b, s, 4.2f))
            {
                s.OrderAnchor = s.Destination;
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Job = SilentOrders.Core.Job.Reposition;
                s.WasAtAnchor = false;
                s.Reason = "Остался без защиты: занимаю ближайшее свободное укрытие";
                return;
            }

            if (s.ManeuverPaused && SafeApproach(b, s) && SilentOrders.Core.V2.Distance(s.Position, s.SavedManeuverAnchor) > 3f)
            {
                s.OrderAnchor = s.SavedManeuverAnchor;
                s.ManeuverPaused = false;
                s.Job = SilentOrders.Core.Job.Reposition;
                s.MoveAuthorized = false;
            }

            if (s.ManeuverPaused)
            {
                Stop(s);
                s.Reason = "Удерживаю позицию и прикрываю смену рубежа поддержки";
                return;
            }

            SilentOrders.Core.Soldier soldier = b.Soldiers[s.BuddyId];
            bool flag3 = s.Job == SilentOrders.Core.Job.Cover || s.Job == SilentOrders.Core.Job.Hold || s.Job == SilentOrders.Core.Job.Command;
            if (soldier.Alive && soldier.ReloadUntil > b.Time && b.Map.NearCover(s.Position) && !s.Broken && SilentOrders.Core.V2.Distance(soldier.Position, s.Position) < 8f && flag3 && !SafeApproach(b, s))
            {
                Stop(s);
                s.Reason = "Держу сектор, пока напарник перезаряжается";
                SilentOrders.Core.Dialogue.Say(b, s, 6);
            }
            else
            {
                if (flag2 && (s.Suppression > ((b.Team(s).Style == SilentOrders.Core.CombatStyle.Aggressive) ? 0.78f : 0.65f) || s.Health < 35f) && !PositionLogic.Protected(b, s, s.Position) && s.PathCursor >= s.Path.Count && SeekLocalShelter(b, s, 3.2f))
                {
                    return;
                }

                if (!s.MoveAuthorized && !flag3 && !SafeApproach(b, s))
                {
                    Stop(s);
                    s.State = SilentOrders.Core.ActionState.WaitCover;
                    s.Reason = "Приказ получен. Жду подтвержденного прикрытия и сигнала командира";
                    SilentOrders.Core.Dialogue.Say(b, s, 4);
                }
                else if (SilentOrders.Core.V2.Distance(s.Position, s.OrderAnchor) < 0.55f)
                {
                    Stop(s);
                    if (!s.WasAtAnchor)
                    {
                        if (s.Job == SilentOrders.Core.Job.Reposition)
                        {
                            s.Job = SilentOrders.Core.Job.Hold;
                        }
                        else if (s.Job == SilentOrders.Core.Job.Rally)
                        {
                            s.Job = SilentOrders.Core.Job.Cover;
                        }

                        s.WasAtAnchor = PositionLogic.Arrived(b, s);
                        s.SettledSince = b.Time;
                        b.Stats.Arrivals++;
                        SilentOrders.Core.Dialogue.Say(b, s, 3);
                    }

                    s.Facing = (SilentOrders.Core.SquadBrain.Threat(b, b.Team(s)) - s.Position).Normalized;
                    s.Reason = s.Tactical.Status + (s.SupportReady ? "; поддержка готова" : "; проверяю возможность поддержки");
                }
                else if (s.PathCursor < s.Path.Count)
                {
                    s.Reason = "Выполняю общий маневр к назначенному укрытию";
                }
                else if (b.Time < s.RejectedUntil && SilentOrders.Core.V2.Distance(s.OrderAnchor, s.RejectedPost) < 0.8f)
                {
                    s.Reason = "Маршрут заблокирован; сообщил командиру";
                }
                else
                {
                    BuildPath(b, s, s.OrderAnchor);
                    s.Reason = (flag3 ? "Занимаю назначенную командиром позицию прикрытия" : "Перебегаю с группой под подтвержденным прикрытием");
                }
            }
        }

        public static bool SeekLocalShelter(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, float radius)
        {
            return PositionLogic.Correct(b, s, radius);
        }

        private static void BuildPath(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 target)
        {
            b.Stats.Paths++;
            System.Collections.Generic.List<SilentOrders.Core.V2> list = SilentOrders.Core.LossMemory.Find(b, s, target, (SilentOrders.Core.V2 p) => Risk(b, s, p) * ((b.Team(s).Style == SilentOrders.Core.CombatStyle.Aggressive) ? 1.2f : 2f) + OccupancyCost(b, s, p) + FireLaneSafety.PathCost(b, s, p) + (b.Map.NearCover(p) ? 0f : 0.45f));
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

        public static float Risk(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 p)
        {
            return PositionLogic.Exposure(b, s, p);
        }

        private static float OccupancyCost(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 p)
        {
            float num = 0f;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Alive && soldier.Id != s.Id && soldier.Side == s.Side)
                {
                    float num2 = System.Math.Min(SilentOrders.Core.V2.Distance(p, soldier.Position), SilentOrders.Core.V2.Distance(p, soldier.OrderAnchor));
                    if (num2 < 1.5f)
                    {
                        num += (float)(soldier.Busy ? 20 : 12);
                    }
                    else if (num2 < 3f)
                    {
                        num += (3f - num2) * 2f;
                    }
                }
            }

            return num;
        }
    }
}
