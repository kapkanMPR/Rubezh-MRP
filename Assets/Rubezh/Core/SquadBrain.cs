using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SquadBrain
    {
        public static void Plan(BattleSimulation b, Squad q)
        {
            b.CountSquadPlan();
            AiTasks.Group(b, q);
            CommandHierarchy.Maintain(b, q);
            System.Collections.Generic.List<Soldier> list = new System.Collections.Generic.List<Soldier>();
            foreach (Soldier squadMember in b.Soldiers)
            {
                if (q.Contains(squadMember) && squadMember.Available)
                {
                    list.Add(squadMember);
                }
            }

            if (list.Count == 0)
            {
                return;
            }

            if (!b.Soldiers[q.LeaderId].Alive || b.Soldiers[q.LeaderId].Incapacitated)
            {
                q.LeaderId = list[0].Id;
                foreach (Soldier item in list)
                {
                    if (item.Morale > b.Soldiers[q.LeaderId].Morale)
                    {
                        q.LeaderId = item.Id;
                    }
                }

                Dialogue.Say(b, b.Soldiers[q.LeaderId], 18, urgent: true);
                b.Log(q.Side, b.Soldiers[q.LeaderId].Name + " принял командование. Перегруппировка.");
                q.Initialized = false;
            }

            System.Collections.Generic.List<int> list2 = new System.Collections.Generic.List<int>();
            foreach (System.Collections.Generic.KeyValuePair<int, Contact> contact in q.Contacts)
            {
                if (b.Time - contact.Value.SeenAt > b.Config.ContactLifetime)
                {
                    list2.Add(contact.Key);
                }
            }

            foreach (int item2 in list2)
            {
                q.Contacts.Remove(item2);
            }

            if (b.Time >= q.NextFocus || !q.Contacts.ContainsKey(q.FocusEnemyId))
            {
                q.NextFocus = b.Time + 8f;
                int bestEnemyId = -1;
                float bestScore = -100000f;
                foreach (Contact value in q.Contacts.Values)
                {
                    float score =
                        value.Confidence * 4f
                        - V2.Distance(value.Position, b.Soldiers[q.LeaderId].Position) * 0.2f
                        - (b.Time - value.SeenAt) * 0.3f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestEnemyId = value.EnemyId;
                    }
                }

                if (bestEnemyId >= 0 && bestEnemyId != q.FocusEnemyId)
                {
                    Dialogue.Say(b, b.Soldiers[q.LeaderId], 21);
                }

                q.FocusEnemyId = bestEnemyId;
            }

            V2 threat = Threat(b, q);
            bool hasContact = q.Contacts.Count > 0;
            if (hasContact)
            {
                q.LastContact = b.Time;
            }

            if (!q.Initialized && !(q.Side == Side.Red && RedCounterattack.Enabled(b)))
            {
                q.Initialized = true;
                q.FrontX = 0f;
                foreach (Soldier item3 in list)
                {
                    q.FrontX += item3.Position.X;
                }

                q.FrontX /= list.Count;
                q.FrontX = ((q.Side == Side.Green) ? Math.Max(6.5f, q.FrontX) : q.FrontX);
                q.RouteY = b.Map.Objective.Y;
                q.MovingElement = 0;
                if (q.Side == Side.Red)
                {
                    if (q.DefenderGroup == null)
                        PrepareDefense(b, q, list, threat);
                }
                else
                {
                    q.Phase = ManeuverPhase.Hold;
                }
            }

            if (CommanderOrders.Waiting(b, q))
                return;
            if (q.TaskReplanRequested)
            {
                q.TaskReplanRequested = false;
                q.CommandPlan.GlobalResetsAvoided++;
                foreach (var member in list)
                    if (member.Execution.StalledSince >= 0)
                        CommandHierarchy.Failed(b, member, "Локальный отказ передан старшему двойки");
            }

            SquadCoordination.Pair(b, q, list);
            Encourage(b, q, list);
            CommanderPriorities.Feedback(b, q, list);
            if (RedCounterattack.Plan(b, q))
            {
                q.CommandPlan.Active = false;
                return;
            }

            if (WithdrawalTactics.Active(q) && WithdrawalTactics.Plan(b, q))
            {
                q.CommandPlan.Active = false;
                return;
            }

            if ((q.Style == CombatStyle.Aggressive || q.Breakthrough.Active) && BreakthroughTactics.Plan(b, q, list))
            {
                q.CommandPlan.Active = false;
                return;
            }

            if (ObjectiveOpportunity.Plan(b, q))
            {
                q.CommandPlan.Active = false;
                return;
            }

            if (!TacticalOrders.Emergency(q) && CommandHierarchy.Plan(b, q))
                return;
            if (TacticalOrders.Plan(b, q))
                return;
            if (q.Side == Side.Green)
            {
                CommanderPriorities.Plan(b, q, list, threat);
                SupportAI.FollowSquad(b, q);
                FireSupportBrain.PressureSupport(b, q);
            }
            else if (q.Side == Side.Red)
            {
                DefenseBrain.Plan(b, q, list);
                SupportAI.FollowSquad(b, q);
                FireSupportBrain.PressureSupport(b, q);
            }
        }

        public static V2 Threat(BattleSimulation b, Squad q)
        {
            V2 a = default(V2);
            float totalWeight = 0f;
            foreach (Contact value in q.Contacts.Values)
            {
                float weight = value.Confidence / (1f + (b.Time - value.SeenAt) * 0.2f);
                a += value.Position * weight;
                totalWeight += weight;
            }

            return (totalWeight > 0f)
                ? (a / totalWeight)
                : new V2((q.Side == Side.Green) ? (b.Map.Columns - 2) : 2, q.RouteY);
        }

        private static void PrepareDefense(
            BattleSimulation b,
            Squad q,
            System.Collections.Generic.List<Soldier> alive,
            V2 threat
        )
        {
            q.Phase = ManeuverPhase.Hold;
            q.PhaseSince = b.Time;
            q.Revision++;
            foreach (Soldier item in alive)
            {
                GiveOrder(
                    b,
                    q,
                    item,
                    (item.Id == q.LeaderId) ? Job.Command : Job.Hold,
                    FindPost(b, item, item.Position, threat, firing: true),
                    authorized: true
                );
            }

            q.CommanderOrder = "Удерживать позиции. Прикрывать соседей";
            Dialogue.Say(b, b.Soldiers[q.LeaderId], 17, urgent: true);
        }

        public static bool GiveOrder(BattleSimulation b, Squad q, Soldier s, Job job, V2 anchor, bool authorized)
        {
            if (
                s.Progress.Owned
                && ProgressRecovery.Active(b, s)
                && s.Progress.Revision == q.Revision
                && !TacticalOrders.Emergency(q)
                && !WithdrawalTactics.Active(q)
            )
                return false;
            if (ObjectiveOpportunity.IsCapturer(b, s) && !TacticalOrders.Emergency(q) && !WithdrawalTactics.Active(q))
                return false;
            if (!AiTasks.MayAssign(b, s))
                return false;
            if (ExecutionReview.RejectRepeatedGoal(b, s, job, anchor))
                return false;
            if (EngagementPolicy.Active(b, s) && !TacticalOrders.Emergency(q) && !WithdrawalTactics.Active(q))
                return false;
            if (FiringPositionPolicy.ShouldHold(b, s, job, anchor))
                anchor = s.Position;
            // Ordinary overwatch assignments must not replace a working sheltered firing
            // position with an exposed one. Assault, capture and retreat retain their jobs.
            if (
                (job == Job.Cover || job == Job.Hold || job == Job.Command)
                && !TacticalOrders.Emergency(q)
                && q.Style != CombatStyle.Withdraw
                && !WithdrawalTactics.Active(q)
                && V2.Distance(s.Position, anchor) > .7f
                && PositionLogic.Protected(b, s, s.Position)
                && PositionLogic.HasSector(b, s, s.Position)
                && !PositionLogic.Protected(b, s, anchor)
            )
            {
                anchor = s.Position;
            }

            if (
                b.Time < s.Tactical.CommitUntil
                && s.PathCursor < s.Path.Count
                && !q.PriorityChanged
                && q.Style != CombatStyle.Withdraw
                && s.Health >= 35
                && s.Suppression < .7f
            )
                return false;
            if (
                !s.Available
                || (
                    b.Time < s.PressureMoveUntil
                    && q.Style != 0
                    && !q.PriorityChanged
                    && s.Suppression < 0.7f
                    && s.Health >= 35f
                    && V2.Distance(s.Position, s.OrderAnchor) > 0.8f
                )
            )
            {
                return false;
            }

            if (s.Job == job && V2.Distance(s.OrderAnchor, anchor) < 0.6f)
            {
                s.MoveAuthorized = authorized;
                return true;
            }

            s.PreviousDestination = s.Destination;
            s.OrderAnchor = anchor;
            s.Destination = anchor;
            s.Job = job;
            s.MoveAuthorized = authorized;
            s.OrderRevision = q.Revision;
            s.OrderUntil = b.Time + 5f;
            s.Tactical.CommitUntil = b.Time + 2.5f;
            s.WasAtAnchor = false;
            object order;
            switch (job)
            {
                default:
                    order = "Удерживать назначенное укрытие";
                    break;
                case Job.Command:
                    order = "Координация и поддержка отделения";
                    break;
                case Job.Advance:
                    order = "Переместиться с группой по сигналу";
                    break;
                case Job.Flank:
                    order = "Обойти известный контакт вместе с парой по сигналу";
                    break;
            }

            s.Order = (string)order;
            s.Path.Clear();
            s.PathCursor = 0;
            s.NextDecision = 0f;
            s.SupportReady = false;
            b.Stats.Orders++;
            return true;
        }

        public static V2 FindPost(
            BattleSimulation b,
            Soldier s,
            V2 wanted,
            V2 threat,
            bool firing,
            bool capture = false
        )
        {
            V2 post;
            if (capture)
            {
                var point = b.Map.NearestOpen(wanted);
                if (!PositionLogic.Occupied(b, s, point) && V2.Distance(point, b.Map.Objective) < 1.9f)
                    return point;
            }

            if (!capture)
                wanted = FormationAI.Desired(b, s, wanted, false);
            if (PositionLogic.TryPost(b, s, wanted, 6, firing, out post))
                return post;
            if (PositionLogic.TryPost(b, s, wanted, 9, firing, out post))
                return post;
            s.Tactical.Block = "Нет доступного защищённого места";
            // Unobserved travel may use an open staging point; never label it cover.
            if (TacticalAI.SafeApproach(b, s) && b.Team(s).Contacts.Count == 0)
            {
                post = b.Map.NearestOpen(wanted);
                if (!PositionLogic.Occupied(b, s, post))
                    return post;
            }

            return s.Position;
        }

        public static bool Ready(BattleSimulation b, Soldier s, Squad q)
        {
            if (
                !PositionLogic.Able(s, b)
                || !SquadCoordination.SupportPosition(b, s)
                || V2.Distance(s.Position, s.OrderAnchor) > 1.2f
            )
                return false;
            if (s.Job != Job.Cover && s.Job != Job.Hold && s.Job != Job.Command)
                return false;
            foreach (var mover in b.Soldiers)
                if (
                    mover.Id != s.Id
                    && mover.Side == s.Side
                    && mover.Available
                    && (mover.Job == Job.Advance || mover.Job == Job.Flank || mover.Job == Job.Reposition)
                    && PositionLogic.UsefulSupport(b, s, mover, mover.OrderAnchor)
                )
                    return true;
            return PositionLogic.HasSector(b, s, s.Position);
        }

        public static int CountReady(BattleSimulation b, Squad q)
        {
            int readyCount = 0;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (q.Contains(soldier) && soldier.Alive)
                {
                    bool readyForSupport = Ready(b, soldier, q);
                    if (readyForSupport && !soldier.SupportReady)
                    {
                        Dialogue.Say(b, soldier, 1);
                    }

                    if (!readyForSupport && soldier.SupportReady)
                    {
                        b.Tactics.LostSupport++;
                        EventVoice.Emit(b, soldier, 3);
                    }

                    soldier.SupportReady = readyForSupport;
                    if (readyForSupport)
                    {
                        readyCount++;
                    }
                }
            }

            return readyCount;
        }

        public static bool HasCover(BattleSimulation b, Soldier mover)
        {
            Squad squad = b.Team(mover);
            int availableCount = 0;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == mover.Side && soldier.Available)
                {
                    availableCount++;
                }
            }

            if (availableCount == 1)
            {
                return true;
            }

            int coveringCount = 0;
            foreach (Soldier supporter in b.Soldiers)
            {
                if (
                    supporter.Id != mover.Id
                    && supporter.Side == mover.Side
                    && Ready(b, supporter, squad)
                    && PositionLogic.UsefulSupport(b, supporter, mover, mover.OrderAnchor)
                )
                {
                    coveringCount++;
                }
            }

            // Depth is a preference, not permission for unsafe movement: at least
            // one effective covering unit is still mandatory.
            int requested = squad.RequiredSupport;
            if (squad.Formation != null)
            {
                if (squad.Formation.depth == 2)
                    requested = Math.Max(1, requested - 1);
                if (squad.Formation.depth == 0)
                    requested = Math.Min(availableCount - 1, requested + 1);
            }

            return coveringCount >= Math.Min(requested, availableCount - 1) && coveringCount > 0;
        }

        public static bool MovementCovered(BattleSimulation b, Soldier mover)
        {
            if (TacticalOrders.Emergency(b.Team(mover)))
                return false;
            if (CoverWaitPolicy.Allow(b, mover))
                return true;
            if (LauncherLogistics.RoutePriority(b, mover))
                return true;
            if (CorpseEquipment.RecoveryRouteSafe(b, mover))
                return true;
            if (CorpseEquipment.ShouldReleaseWaiting(b, mover))
                return true;
            if (ObjectiveOpportunity.MovementAllowed(b, mover))
                return true;
            if (WithdrawalTactics.Active(b.Team(mover)))
                return WithdrawalTactics.HasCover(b, mover);
            if (b.Team(mover).Breakthrough.Active && b.Team(mover).Breakthrough.Movers.Contains(mover.Id))
                return BreakthroughTactics.MovementAllowed(b, mover);
            if (mover.PriorityMovement.Transfer)
                return FlankTactics.TransferCovered(b, mover);
            if (CommandHierarchy.Owns(b.Team(mover)))
                return CommandHierarchy.MovementAllowed(b, mover);
            if (TacticalOrders.Flanks(b.Team(mover)))
                return FlankTactics.CanAdvance(b, mover);
            if (BreakthroughTactics.BlastWindow(b, mover) || TacticalAI.SafeApproach(b, mover))
            {
                return true;
            }

            int availableAllies = 0;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == mover.Side && soldier.Available)
                {
                    availableAllies++;
                }
            }

            if (availableAllies <= 1 && !mover.Busy)
            {
                return true;
            }

            if (CommanderPriorities.SmokeSafe(b, mover))
            {
                return true;
            }

            if (!mover.Busy && (mover.Job == Job.Advance || mover.Job == Job.Flank || mover.Job == Job.Reposition))
            {
                return HasCover(b, mover);
            }

            foreach (var support in b.Soldiers)
                if (
                    support.Id != mover.Id
                    && support.Side == mover.Side
                    && PositionLogic.UsefulSupport(b, support, mover, mover.Destination)
                )
                    return true;
            return false;
        }

        private static void Encourage(BattleSimulation b, Squad q, System.Collections.Generic.List<Soldier> alive)
        {
            foreach (Soldier item in alive)
            {
                if ((!item.Broken && item.Morale > 0.3f) || b.Time < item.NextRally)
                {
                    continue;
                }

                Soldier soldier = null;
                foreach (Soldier item2 in alive)
                {
                    if (
                        item2.Id != item.Id
                        && !item2.Broken
                        && item2.Morale > 0.35f
                        && V2.Distance(item2.Position, item.Position) < 6f
                        && (soldier == null || item2.Id == q.LeaderId)
                    )
                    {
                        soldier = item2;
                    }
                }

                if (soldier != null)
                {
                    item.NextRally = b.Time + 9f;
                    b.Stats.Rallies++;
                    if (b.Time >= item.MoraleRecoveryBlockedUntil && b.Time - item.LastIncoming > 5f)
                        item.Morale = Util.Clamp(item.Morale + .035f, 0, item.MoraleMaximum);
                    Dialogue.Say(b, soldier, 10, urgent: true);
                }
            }
        }
    }
}
