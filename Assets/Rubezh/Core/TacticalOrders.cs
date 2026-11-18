using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class MovementPriority
    {
        public bool Transfer, RearLegCompleted, FastTransfer;
        public int TransferGroupSize;
        public int DestinationWing;
        public float NextRoute, NextSmoke, WaitSince = -1f;
        public int Attempts, MapRevision = -1;
        public V2 Goal, LastPosition;
    }

    public static class TacticalOrders
    {
        public static bool Emergency(Squad q)
        {
            return q.Style == CombatStyle.EmergencyWithdraw;
        }

        public static bool Flanks(Squad q)
        {
            return q.Side == Side.Green && !Emergency(q) && q.Style != CombatStyle.Withdraw && q.Style != CombatStyle.Passive && IsFlank(q.Intent);
        }

        public static bool IsFlank(ManeuverIntent i)
        {
            return i >= ManeuverIntent.Left && i <= ManeuverIntent.RightHold;
        }

        public static bool PriorityMover(BattleSimulation b, Soldier s)
        {
            return Emergency(b.Team(s)) || s.PriorityMovement.Transfer;
        }

        public static bool CanMove(Soldier s)
        {
            return s.Alive && s.Deployed && !s.Incapacitated;
        }

        public static void Request(BattleSimulation b, int style, int intent)
        {
            if (b == null || b.Outcome != Outcome.Running || style < 0 || style > 4 || intent < 0 || intent > 8)
                return;
            var q = b.Green;
            q.PendingStyle = (CombatStyle)style;
            q.PendingIntent = intent == 6 ? ManeuverIntent.Center : (ManeuverIntent)intent;
            q.PriorityPending = true;
            b.History.Event(b, "playerOrder", q.LeaderId, -1, new V2(), new V2(), style, "intent=" + intent);
            // Emergency command and release from it are never held in the radio queue.
            q.PriorityAt = Emergency(q) || style == 4 ? b.Time : b.Time + b.Config.RadioDelay;
            q.PriorityStatus = "Передаю: " + CommanderPriorities.StyleName(q.PendingStyle) + " / " + CommanderPriorities.IntentName(q.PendingIntent);
            if (q.PriorityAt <= b.Time)
                ApplyPending(b, q);
        }

        public static void ApplyPending(BattleSimulation b, Squad q)
        {
            if (q.PriorityPending && b.Time >= q.PriorityAt)
            {
                if (q.PendingStyle == CombatStyle.EmergencyWithdraw || Emergency(q))
                {
                    var t = q.CommandTransition;
                    t.Waiting = false;
                    t.Bounds.Clear();
                    t.Style = q.PendingStyle;
                    t.Intent = q.PendingIntent;
                    t.AcceptedAt = b.Time;
                    t.Accepted++;
                    Execute(b, q, true);
                    t.Executed++;
                    t.ExecuteAt = b.Time;
                    t.Phase = "Исполнение";
                    t.Reason = q.PriorityStatus;
                }
                else
                    CommanderOrders.Receive(b, q);
            }

            CommanderOrders.Update(b, q);
            CommanderOrders.Review(b, q);
        }

        public static void Execute(BattleSimulation b, Squad q, bool hard)
        {
            bool wasEmergency = Emergency(q), wasFlanks = Flanks(q);
            var oldIntent = q.Intent;
            var nextStyle = q.PendingStyle;
            var nextIntent = q.PendingIntent == ManeuverIntent.Breakthrough ? ManeuverIntent.Center : q.PendingIntent;
            // Repeating the accepted order must not restart execution or discard progress.
            if (q.Style == nextStyle && q.Intent == nextIntent)
            {
                q.PriorityPending = false;
                q.PriorityStatus = "Приказ уже выполняется";
                return;
            }

            // Cancel old tasks BEFORE installing a new owner for movement. Preserve contacts,
            // physical injuries, suppression, ammunition, projectiles and weapon cooldowns.
            bool sameIntent = oldIntent == nextIntent && !wasEmergency && q.Style != CombatStyle.Withdraw && nextStyle != CombatStyle.Withdraw;
            if (hard)
                Reset(b, q, nextStyle != CombatStyle.EmergencyWithdraw);
            else if (!sameIntent || nextStyle == CombatStyle.Passive)
                Replan(b, q);
            q.Style = nextStyle;
            q.Intent = nextIntent;
            q.PriorityPending = false;
            CommandPosture.Sync(b, q);
            StraightAdvance.Sync(b, q);
            if (nextStyle == CombatStyle.Passive || nextStyle == CombatStyle.Withdraw)
                foreach (var unit in b.Soldiers)
                    if (q.Contains(unit) && EngagementPolicy.Active(b, unit))
                        EngagementPolicy.End(b, unit, false, "Новый режим движения группы");
            q.PriorityAt = b.Time;
            q.PriorityChanged = !sameIntent;
            q.Revision++;
            b.Stats.PriorityChanges++;
            if (nextStyle == CombatStyle.Passive || nextStyle == CombatStyle.Withdraw)
                foreach (var unit in b.Soldiers)
                    if (q.Contains(unit) && !ServiceActive(unit) && !unit.GrenadeEscape.Active)
                    {
                        unit.CounterSniper.Phase = CounterSniperPhase.None;
                        unit.PriorityMovement.Transfer = false;
                    }

            q.NextPlan = b.Time;
            q.NextPriorityPlan = b.Time;
            q.NextPairReview = 0f;
            q.PriorityStatus = Emergency(q) ? "ДА НУ НАХЕР! Всем немедленно отрываться!" : "Принято: " + CommanderPriorities.StyleName(q.Style) + " / " + CommanderPriorities.IntentName(q.Intent);
            q.Plan = q.CommanderOrder = q.Execution = q.PriorityStatus;
            b.Log(q.Side, q.PriorityStatus);
            if (q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count)
                b.Say(b.Soldiers[q.LeaderId], q.PriorityStatus, true);
            if (Emergency(q))
            {
                q.FlankPlan.Clear();
                if (q.Side == Side.Green && !wasEmergency)
                    RetreatVoices.Emit(b, Side.Green);
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && CanMove(s))
                    {
                        // Emergency cancellation may interrupt a reload, but never refills ammo.
                        s.ReloadUntil = 0;
                        PlanRetreat(b, s, true);
                    }
            }
            else if (Flanks(q) && !sameIntent)
            {
                if (!wasFlanks || wasEmergency)
                    q.FlankPlan.Clear();
                FlankTactics.Configure(b, q, oldIntent, wasFlanks && !wasEmergency);
            }
            else if (!Flanks(q))
                q.FlankPlan.Clear();
        }

        // Release obsolete maneuver ownership, keeping useful firing, reload and aid.
        public static void Replan(BattleSimulation b, Squad q)
        {
            if (q.CapturePlan.Active)
                ObjectiveOpportunity.Cancel(b, q, "Командир меняет направление");
            if (q.Breakthrough.Active)
                BreakthroughTactics.End(b, q, "Новый замысел командира");
            q.SupportIds.Clear();
            q.ManeuverIds.Clear();
            q.PlannedSmoke.Clear();
            q.Smoke.Pending = null;
            q.SupportMoverId = q.SupportFollowerId = -1;
            q.SupportMovePhase = 0;
            q.EnvelopmentActive = false;
            q.EnvelopmentBounds = 0;
            q.EmergencyRetreat = false;
            q.Phase = ManeuverPhase.Hold;
            q.PhaseSince = b.Time;
            q.ReadySince = -1;
            q.Initialized = q.PriorityInitialized = q.RegroupAssigned = q.TaskReplanRequested = false;
            q.Adaptation = Adaptation.None;
            q.AdaptReleased = false;
            q.CombatStall = 0;
            q.LastProgress = b.Time;
            q.Withdrawal.Clear();
            q.SmokeWaitUntil = 0;
            q.DelayReason = "";
            foreach (var s in b.Soldiers)
                if (q.Contains(s))
                {
                    if (ServiceActive(s))
                    {
                        q.CommandTransition.PreservedServices++;
                        continue;
                    }

                    if (s.GrenadeEscape.Active || CounterSniper.Reserved(b, s))
                        continue;
                    if (!s.Available || !s.Deployed)
                        continue;
                    bool firing = s.PathCursor >= s.Path.Count && PositionLogic.HasSector(b, s, s.Position);
                    if (firing || s.Sniper)
                    {
                        q.CommandTransition.PreservedFire++;
                    }
                    else
                    {
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Destination = s.Position;
                        s.Job = Job.Hold;
                    }

                    ProgressRecovery.Cancel(b, s);
                    CombatCohesion.Clear(s);
                    s.Outmaneuver.Clear();
                    s.PriorityMovement.Transfer = s.PriorityMovement.FastTransfer = false;
                    s.PriorityMovement.NextRoute = 0;
                    s.PriorityMovement.WaitSince = -1;
                    s.PriorityMovement.MapRevision = -1;
                    s.Tactical.CommitUntil = 0;
                    s.Tactical.NextReview = 0;
                    s.CoverWaitStartedAt = -1;
                    s.ManeuverPaused = false;
                    s.MoveAuthorized = true;
                    s.NextDecision = b.Time;
                }
        }

        public static bool ServiceActive(Soldier s)
        {
            return (SupplyRecovery.Active(s) || HandLauncher.Active(s)) || BuddyActions.Active(s) || s.RescueTargetId >= 0 || s.RescuerId >= 0 || s.RescuePhase != RescuePhase.None || s.TreatmentUntil > 0 || s.SupplyDonorId >= 0 || s.SupplyReceiverId >= 0;
        }

        public static void Reset(BattleSimulation b, Squad q, bool preserveServices = false)
        {
            if (q.CapturePlan.Active)
                ObjectiveOpportunity.Cancel(b, q, "Поступил новый приказ");
            // Release BOTH sides of rescue links before clearing the action timers.
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.RescueTargetId >= 0 && !preserveServices)
                    SupportAI.Release(b, s, false);
            if (q.Breakthrough.Active)
                q.Breakthrough.Aborted++;
            q.Breakthrough.Phase = BreakthroughPhase.Idle;
            q.Breakthrough.Targets.Clear();
            q.Breakthrough.Movers.Clear();
            q.Breakthrough.Fired.Clear();
            q.Breakthrough.NextAttempt = 0;
            q.SupportIds.Clear();
            q.ManeuverIds.Clear();
            q.PlannedSmoke.Clear();
            q.Smoke.Pending = null;
            q.FocusEnemyId = -1;
            q.NextFocus = 0;
            q.SupportMoverId = q.SupportFollowerId = -1;
            q.SupportMovePhase = 0;
            q.LostSectorSince = -1;
            q.EnvelopmentActive = false;
            q.EnvelopmentBounds = 0;
            q.EmergencyRetreat = false;
            q.Phase = ManeuverPhase.Hold;
            q.PhaseSince = b.Time;
            q.ReadySince = -1;
            q.ReadyCount = q.RequiredSupport = q.MovingCount = 0;
            q.Initialized = q.PriorityInitialized = q.RegroupAssigned = q.TaskReplanRequested = false;
            q.Adaptation = Adaptation.None;
            q.AdaptReleased = false;
            q.CombatStall = 0;
            q.LastProgress = b.Time;
            q.SmokeWaitUntil = 0;
            q.DelayReason = "";
            q.SupportStatus = "Задачи сброшены";
            q.Defense.Initialized = false;
            q.Defense.Active = false;
            q.Defense.Movers.Clear();
            q.Withdrawal.Clear();
            foreach (var s in b.Soldiers)
                if (q.Contains(s))
                {
                    s.MedicCombatReviewAt = 0;
                    s.MedicCombatCommitUntil = 0;
                }

            foreach (var s in b.Soldiers)
                if (q.Contains(s) && (!preserveServices || !ServiceActive(s)))
                    CancelUnit(b, s);
        }

        public static void CancelUnit(BattleSimulation b, Soldier s)
        {
            ProgressRecovery.Cancel(b, s);
            if (s.RescuerId >= 0 && s.RescuerId < b.Soldiers.Count)
                SupportAI.Release(b, b.Soldiers[s.RescuerId], false);
            if (s.RescueTargetId >= 0)
                SupportAI.Release(b, s, false);
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Destination = s.SavedManeuverAnchor = s.Position;
            s.SupplyDonorId = s.SupplyReceiverId = -1;
            s.SupplyKind = 0;
            s.RescuerId = s.RescueTargetId = -1;
            s.RescuePhase = RescuePhase.None;
            s.TreatmentUntil = 0;
            s.TreatmentDonorId = -1;
            s.TreatmentKind = 0; // preserve an active reload during ordinary tactical replans
            s.ManeuverPaused = s.SupportReady = s.WasAtAnchor = s.SniperMoving = s.SniperRetreating = false;
            s.MoveAuthorized = true;
            s.SuppressUntil = s.NextSuppressDecision = 0;
            s.SuppressiveFire = false;
            s.BurstRounds = 0;
            s.RocketAimTarget = s.OrdnanceAimTarget = -1;
            s.RocketAimSince = s.OrdnanceAimSince = 0;
            s.RecoverUntil = 0;
            s.SupportFailedUntil = s.RejectedUntil = s.PressureMoveUntil = s.SniperRetreatUntil = 0;
            s.Job = Job.Hold;
            s.State = s.Alive ? ActionState.Observe : ActionState.Down;
            s.Order = "Получаю новый приказ";
            s.Reason = "Старые задачи и ожидания отменены";
            s.OrderRevision++;
            s.NextDecision = b.Time;
            s.BuddyId = s.Id;
            s.Tactical.CommitUntil = s.Tactical.NextReview = 0;
            s.Tactical.WaitSince = s.Tactical.LostSectorSince = -1;
            s.Tactical.WaitReviews = 0;
            s.Tactical.Block = "";
            s.Tactical.SearchAt = -100;
            s.Tactical.SearchValue = false;
            s.Tactical.ProtectionAt = -100;
            s.Tactical.OrderRevision = -1;
            // A new tactical order cancels retrieval/delivery routes, not carried guns.
            s.LootTargetId = s.LootGroundId = s.WeaponAidTargetId = -1;
            s.LauncherPickupId = -1;
            s.LauncherPickupRetryAt = b.Time;
            s.WeaponPickupPriority = false;
            s.WeaponAidRetryAt = b.Time;
            CombatCohesion.Clear(s);
            s.Outmaneuver.Clear();
            s.PriorityMovement.Transfer = s.PriorityMovement.FastTransfer = false;
            s.PriorityMovement.TransferGroupSize = 0;
            s.PriorityMovement.NextRoute = 0;
            s.PriorityMovement.WaitSince = -1;
            s.PriorityMovement.MapRevision = -1;
            s.CoverWaitStartedAt = -1f;
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            if (Emergency(q))
            {
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && CanMove(s))
                        PlanRetreat(b, s, false);
                q.Plan = q.Execution = "Аварийный отход: не ждать прикрытия";
                return true;
            }

            return Flanks(q) && FlankTactics.Plan(b, q);
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            if (Emergency(b.Team(s)))
            {
                if (CanMove(s))
                    PlanRetreat(b, s, false);
                return true;
            }

            if (WithdrawalTactics.Active(b.Team(s)))
                return WithdrawalTactics.Decide(b, s);
            if (s.PriorityMovement.Transfer)
            {
                FlankTactics.TransferRoute(b, s, false);
                return true;
            }

            if (ObjectiveOpportunity.Decide(b, s))
                return true;
            // Snipers own their firing-post planner. Ordinary flank navigation previously
            // intercepted the decision and kept moving the sniper without a useful shot.
            if (s.Sniper)
                return false;
            if (CommandHierarchy.Owns(b.Team(s)))
                return CommandHierarchy.Decide(b, s);
            if (FlankOutmaneuver.Decide(b, s))
                return true;
            if (CombatCohesion.Decide(b, s))
                return true;
            return Flanks(b.Team(s)) && FlankTactics.Decide(b, s);
        }

        public static Contact ClosestKnown(BattleSimulation b, Soldier s)
        {
            Contact best = null;
            float gap = float.MaxValue;
            foreach (var c in Knowledge.Contacts(b, s))
                Consider(b, s, c, ref best, ref gap);
            return best;
        }

        static void Consider(BattleSimulation b, Soldier s, Contact c, ref Contact best, ref float gap)
        {
            if (c.Confidence < .25f || b.Time - c.SeenAt > 8f || c.SeenAt > b.Time)
                return;
            float d = V2.Distance(s.Position, c.Position);
            if (d < gap)
            {
                gap = d;
                best = c;
            }
        }

        public static V2 Axis(BattleSimulation b, Side side)
        {
            return PositionLogic.Axis(b) * (side == Side.Green ? 1f : -1f);
        }

        public static V2 Lateral(BattleSimulation b)
        {
            var a = PositionLogic.Axis(b);
            return new V2(-a.Y, a.X);
        }

        static V2 Clamp(BattleMap m, V2 p)
        {
            return new V2(Util.Clamp(p.X, 1.1f, m.Columns - 1.1f), Util.Clamp(p.Y, 1.1f, m.Rows - 1.1f));
        }

        public static V2 Open(BattleSimulation b, V2 p)
        {
            return b.Map.NearestOpen(Clamp(b.Map, p));
        }

        public static void PlanRetreat(BattleSimulation b, Soldier s, bool force)
        {
            var m = s.PriorityMovement;
            if (!force && b.Time < m.NextRoute && m.MapRevision == b.Map.Revision && s.PathCursor < s.Path.Count)
                return;
            if (!force && b.Time < m.NextRoute && s.PathCursor >= s.Path.Count)
                return;
            var threat = ClosestKnown(b, s);
            var back = Axis(b, s.Side) * -1f;
            if (threat != null)
            {
                var away = (s.Position - threat.Position).Normalized;
                if (away.Length > .1f)
                    back = (away * 2 + back).Normalized;
            }

            var lateral = new V2(-back.Y, back.X);
            float side = (s.Id % 2 == 0 ? 1f : -1f) * (2f + s.Id % 3);
            var wanted = Open(b, s.Position + back * 12 + lateral * side);
            if (V2.Distance(wanted, s.Position) < 1.5f)
                wanted = Open(b, s.Position + lateral * (side * 2));
            Route(b, s, wanted, false, true);
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "ДА НУ НАХЕР! Назад, без ожидания прикрытия";
            s.Reason = "Приоритет — оторваться; боевые задачи отменены";
            TrySmoke(b, s, threat);
        }

        // Bounded local candidate set and a cached path. At most three path searches per review.
        public static bool Route(BattleSimulation b, Soldier s, V2 wanted, bool requireCover, bool sprint)
        {
            var m = s.PriorityMovement;
            var candidates = new List<KeyValuePair<float, V2>>(10);
            var axis = Axis(b, s.Side);
            var lateral = new V2(-axis.Y, axis.X);
            var threat = ClosestKnown(b, s);
            V2[] offsets =
            {
                new V2(),
                lateral * 2,
                lateral * -2,
                axis * -2,
                lateral * 4,
                lateral * -4,
                axis * -4,
                axis * 2
            };
            foreach (var o in offsets)
            {
                var p = Open(b, wanted + o);
                bool cover = PositionLogic.Protected(b, s, p);
                if (!b.Map.Walkable(p) || PositionLogic.Occupied(b, s, p) || (requireCover && !cover))
                    continue;
                float d = V2.Distance(s.Position, p);
                if (d < .35f)
                    continue;
                float risk = PositionLogic.Exposure(b, s, p);
                float score = -V2.Distance(p, wanted) * 1.3f - d * .08f + (cover ? 2.5f : 0) - risk * (sprint ? 2f : 4f);
                if (Emergency(b.Team(s)) && threat != null)
                    score += Math.Min(15, V2.Distance(p, threat.Position) - V2.Distance(s.Position, threat.Position)) * 1.5f;
                candidates.Add(new KeyValuePair<float, V2>(score, p));
            }

            candidates.Sort((a, z) => z.Key.CompareTo(a.Key));
            m.NextRoute = b.Time + .8f + (s.Id % 5) * .08f;
            m.MapRevision = b.Map.Revision;
            m.Attempts++;
            int routeBudget = 3;
            foreach (var c in candidates)
            {
                var path = LossMemory.Find(b, s, c.Value, p => PositionLogic.Exposure(b, s, p) * (sprint ? 1.5f : 3f));
                b.Stats.Paths++;
                if (path.Count > 0)
                {
                    s.Path = path;
                    s.PathCursor = 0;
                    s.Destination = s.OrderAnchor = m.Goal = c.Value;
                    s.LastMoved = b.Time;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.WasAtAnchor = false;
                    return true;
                }

                if (--routeBudget == 0)
                    break;
            }

            if (s.PathCursor >= s.Path.Count)
            {
                TacticalAI.Stop(s);
                s.Reason = "Ищу свободный путь, приказ остаётся активен";
            }

            return false;
        }

        public static bool TrySmoke(BattleSimulation b, Soldier s, Contact threat)
        {
            var purpose = Emergency(b.Team(s)) || WithdrawalTactics.Active(b.Team(s)) || RedCounterattack.Retreating(b, s) ? SmokePurpose.Withdrawal : SmokePurpose.Route;
            return SmokeTactics.Request(b, b.Team(s), s, s.Destination, purpose, s);
        }

        public static bool UpdatePriorityMovement(BattleSimulation b, Soldier s, float dt)
        {
            if (!PriorityMover(b, s))
                return false;
            if (s.PriorityMovement.Transfer)
                FlankTactics.ExpireFastTransfer(b, s);
            // A relocation command does not override combat trauma, panic, reload or
            // concussion reaction. Tactical transfers use the normal combat loop so
            // they can fire, take cover, and bound between shorter waypoints.
            if (s.Broken || s.Suppression > .87f)
                return false;
            if (b.Time < s.ConcussionReactUntil)
            {
                s.Moving = false;
                s.State = ActionState.Observe;
                return true;
            }

            if (s.PriorityMovement.Transfer)
            {
                if (s.PriorityMovement.FastTransfer && (s.Suppression >= .65f || s.Morale < .40f || ConcussionEffects.Current(b, s) > .45f || !FlankTactics.HasEnoughReceivingSupport(b, s)))
                    FlankTactics.DowngradeFastTransfer(b, s);
                // Even a supported fast crossing is executed by the normal combat loop:
                // it can fire, reload and take short bounds, never a magic no-combat sprint.
                return false;
            }

            s.SupportReady = false;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            if (!CanMove(s))
            {
                s.Moving = false;
                s.State = s.Alive ? ActionState.Incapacitated : ActionState.Down;
                return true;
            }

            if (Emergency(b.Team(s)))
                PlanRetreat(b, s, false);
            else
                FlankTactics.TransferRoute(b, s, false);
            if (s.PathCursor < s.Path.Count)
            {
                Locomotion.AdvanceWaypoint(b, s);
                var delta = s.Path[s.PathCursor] - s.Position;
                float speed = CombatMovement.Speed(b, s);
                if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, speed * dt), out var next))
                {
                    s.DistanceTravelled += V2.Distance(s.Position, next);
                    s.Position = next;
                    s.Facing = delta.Normalized;
                    s.Moving = true;
                    s.LastMoved = b.Time;
                    if (V2.Distance(next, s.Path[s.PathCursor]) < .05f)
                        s.PathCursor++;
                }
                else if (b.Time - s.LastMoved > 1.3f)
                {
                    s.Path.Clear();
                    s.PathCursor = 0;
                    s.PriorityMovement.NextRoute = 0;
                }
            }

            s.Crouched = s.Suppression > .55f;
            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }

        public static bool LocalService(BattleSimulation b, Soldier a, Soldier z)
        {
            if (PriorityMover(b, a) || PriorityMover(b, z))
                return false;
            var q = b.Team(a);
            if (!Flanks(q) || !q.FlankPlan.Active)
                return true;
            return FlankTactics.SideOf(q, a.Id) == FlankTactics.SideOf(q, z.Id);
        }
    }
}
