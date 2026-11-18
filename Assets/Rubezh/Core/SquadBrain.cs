using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SquadBrain
    {
        public static void Plan(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            b.CountSquadPlan();
            AiTasks.Group(b, q);
            CommandHierarchy.Maintain(b, q);
            System.Collections.Generic.List<SilentOrders.Core.Soldier> list = new System.Collections.Generic.List<SilentOrders.Core.Soldier>();
            foreach (SilentOrders.Core.Soldier soldier3 in b.Soldiers)
            {
                if (q.Contains(soldier3) && soldier3.Available)
                {
                    list.Add(soldier3);
                }
            }

            if (list.Count == 0)
            {
                return;
            }

            if (!b.Soldiers[q.LeaderId].Alive || b.Soldiers[q.LeaderId].Incapacitated)
            {
                q.LeaderId = list[0].Id;
                foreach (SilentOrders.Core.Soldier item in list)
                {
                    if (item.Morale > b.Soldiers[q.LeaderId].Morale)
                    {
                        q.LeaderId = item.Id;
                    }
                }

                SilentOrders.Core.Dialogue.Say(b, b.Soldiers[q.LeaderId], 18, urgent: true);
                b.Log(q.Side, b.Soldiers[q.LeaderId].Name + " принял командование. Перегруппировка.");
                q.Initialized = false;
            }

            System.Collections.Generic.List<int> list2 = new System.Collections.Generic.List<int>();
            foreach (System.Collections.Generic.KeyValuePair<int, SilentOrders.Core.Contact> contact in q.Contacts)
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
                int num = -1;
                float num2 = -100000f;
                foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
                {
                    float num3 = value.Confidence * 4f - SilentOrders.Core.V2.Distance(value.Position, b.Soldiers[q.LeaderId].Position) * 0.2f - (b.Time - value.SeenAt) * 0.3f;
                    if (num3 > num2)
                    {
                        num2 = num3;
                        num = value.EnemyId;
                    }
                }

                if (num >= 0 && num != q.FocusEnemyId)
                {
                    SilentOrders.Core.Dialogue.Say(b, b.Soldiers[q.LeaderId], 21);
                }

                q.FocusEnemyId = num;
            }

            SilentOrders.Core.V2 threat = Threat(b, q);
            bool flag = q.Contacts.Count > 0;
            if (flag)
            {
                q.LastContact = b.Time;
            }

            if (!q.Initialized && !(q.Side == Side.Red && RedCounterattack.Enabled(b)))
            {
                q.Initialized = true;
                q.FrontX = 0f;
                foreach (SilentOrders.Core.Soldier item3 in list)
                {
                    q.FrontX += item3.Position.X;
                }

                q.FrontX /= list.Count;
                q.FrontX = ((q.Side == SilentOrders.Core.Side.Green) ? System.Math.Max(6.5f, q.FrontX) : q.FrontX);
                q.RouteY = b.Map.Objective.Y;
                q.MovingElement = 0;
                if (q.Side == SilentOrders.Core.Side.Red)
                {
                    if (q.DefenderGroup == null)
                        PrepareDefense(b, q, list, threat);
                }
                else
                {
                    q.Phase = SilentOrders.Core.ManeuverPhase.Hold;
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
            SilentOrders.Core.CommanderPriorities.Feedback(b, q, list);
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
            if (q.Side == SilentOrders.Core.Side.Green)
            {
                SilentOrders.Core.CommanderPriorities.Plan(b, q, list, threat);
                SilentOrders.Core.SupportAI.FollowSquad(b, q);
                SilentOrders.Core.FireSupportBrain.PressureSupport(b, q);
            }
            else if (q.Side == SilentOrders.Core.Side.Red)
            {
                SilentOrders.Core.DefenseBrain.Plan(b, q, list);
                SilentOrders.Core.SupportAI.FollowSquad(b, q);
                SilentOrders.Core.FireSupportBrain.PressureSupport(b, q);
            }
        }

        public static SilentOrders.Core.V2 Threat(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            SilentOrders.Core.V2 a = default(SilentOrders.Core.V2);
            float num = 0f;
            foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
            {
                float num2 = value.Confidence / (1f + (b.Time - value.SeenAt) * 0.2f);
                a += value.Position * num2;
                num += num2;
            }

            return (num > 0f) ? (a / num) : new SilentOrders.Core.V2((q.Side == SilentOrders.Core.Side.Green) ? (b.Map.Columns - 2) : 2, q.RouteY);
        }

        private static void PrepareDefense(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> alive, SilentOrders.Core.V2 threat)
        {
            q.Phase = SilentOrders.Core.ManeuverPhase.Hold;
            q.PhaseSince = b.Time;
            q.Revision++;
            foreach (SilentOrders.Core.Soldier item in alive)
            {
                GiveOrder(b, q, item, (item.Id == q.LeaderId) ? SilentOrders.Core.Job.Command : SilentOrders.Core.Job.Hold, FindPost(b, item, item.Position, threat, firing: true), authorized: true);
            }

            q.CommanderOrder = "Удерживать позиции. Прикрывать соседей";
            SilentOrders.Core.Dialogue.Say(b, b.Soldiers[q.LeaderId], 17, urgent: true);
        }

        public static bool GiveOrder(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.Soldier s, SilentOrders.Core.Job job, SilentOrders.Core.V2 anchor, bool authorized)
        {
            if (s.Progress.Owned && ProgressRecovery.Active(b, s) && s.Progress.Revision == q.Revision && !TacticalOrders.Emergency(q) && !WithdrawalTactics.Active(q))
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
            if ((job == Job.Cover || job == Job.Hold || job == Job.Command) && !TacticalOrders.Emergency(q) && q.Style != CombatStyle.Withdraw && !WithdrawalTactics.Active(q) && V2.Distance(s.Position, anchor) > .7f && PositionLogic.Protected(b, s, s.Position) && PositionLogic.HasSector(b, s, s.Position) && !PositionLogic.Protected(b, s, anchor))
            {
                anchor = s.Position;
            }

            if (b.Time < s.Tactical.CommitUntil && s.PathCursor < s.Path.Count && !q.PriorityChanged && q.Style != CombatStyle.Withdraw && s.Health >= 35 && s.Suppression < .7f)
                return false;
            if (!s.Available || (b.Time < s.PressureMoveUntil && q.Style != 0 && !q.PriorityChanged && s.Suppression < 0.7f && s.Health >= 35f && SilentOrders.Core.V2.Distance(s.Position, s.OrderAnchor) > 0.8f))
            {
                return false;
            }

            if (s.Job == job && SilentOrders.Core.V2.Distance(s.OrderAnchor, anchor) < 0.6f)
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
                case SilentOrders.Core.Job.Command:
                    order = "Координация и поддержка отделения";
                    break;
                case SilentOrders.Core.Job.Advance:
                    order = "Переместиться с группой по сигналу";
                    break;
                case SilentOrders.Core.Job.Flank:
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

        public static SilentOrders.Core.V2 FindPost(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 wanted, SilentOrders.Core.V2 threat, bool firing, bool capture = false)
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

        public static bool Ready(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.Squad q)
        {
            if (!PositionLogic.Able(s, b) || !SquadCoordination.SupportPosition(b, s) || V2.Distance(s.Position, s.OrderAnchor) > 1.2f)
                return false;
            if (s.Job != Job.Cover && s.Job != Job.Hold && s.Job != Job.Command)
                return false;
            foreach (var mover in b.Soldiers)
                if (mover.Id != s.Id && mover.Side == s.Side && mover.Available && (mover.Job == Job.Advance || mover.Job == Job.Flank || mover.Job == Job.Reposition) && PositionLogic.UsefulSupport(b, s, mover, mover.OrderAnchor))
                    return true;
            return PositionLogic.HasSector(b, s, s.Position);
        }

        public static int CountReady(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            int num = 0;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (q.Contains(soldier) && soldier.Alive)
                {
                    bool flag = Ready(b, soldier, q);
                    if (flag && !soldier.SupportReady)
                    {
                        SilentOrders.Core.Dialogue.Say(b, soldier, 1);
                    }

                    if (!flag && soldier.SupportReady)
                    {
                        b.Tactics.LostSupport++;
                        EventVoice.Emit(b, soldier, 3);
                    }

                    soldier.SupportReady = flag;
                    if (flag)
                    {
                        num++;
                    }
                }
            }

            return num;
        }

        public static bool HasCover(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier mover)
        {
            SilentOrders.Core.Squad squad = b.Team(mover);
            int num = 0;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == mover.Side && soldier.Available)
                {
                    num++;
                }
            }

            if (num == 1)
            {
                return true;
            }

            int num2 = 0;
            foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
            {
                if (soldier2.Id != mover.Id && soldier2.Side == mover.Side && Ready(b, soldier2, squad) && PositionLogic.UsefulSupport(b, soldier2, mover, mover.OrderAnchor))
                {
                    num2++;
                }
            }

            // Depth is a preference, not permission for unsafe movement: at least
            // one effective covering unit is still mandatory.
            int requested = squad.RequiredSupport;
            if (squad.Formation != null)
            {
                if (squad.Formation.depth == 2)
                    requested = System.Math.Max(1, requested - 1);
                if (squad.Formation.depth == 0)
                    requested = System.Math.Min(num - 1, requested + 1);
            }

            return num2 >= System.Math.Min(requested, num - 1) && num2 > 0;
        }

        public static bool MovementCovered(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier mover)
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
            if (BreakthroughTactics.BlastWindow(b, mover) || SilentOrders.Core.TacticalAI.SafeApproach(b, mover))
            {
                return true;
            }

            int num = 0;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == mover.Side && soldier.Available)
                {
                    num++;
                }
            }

            if (num <= 1 && !mover.Busy)
            {
                return true;
            }

            if (SilentOrders.Core.CommanderPriorities.SmokeSafe(b, mover))
            {
                return true;
            }

            if (!mover.Busy && (mover.Job == SilentOrders.Core.Job.Advance || mover.Job == SilentOrders.Core.Job.Flank || mover.Job == SilentOrders.Core.Job.Reposition))
            {
                return HasCover(b, mover);
            }

            foreach (var support in b.Soldiers)
                if (support.Id != mover.Id && support.Side == mover.Side && PositionLogic.UsefulSupport(b, support, mover, mover.Destination))
                    return true;
            return false;
        }

        private static void Encourage(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> alive)
        {
            foreach (SilentOrders.Core.Soldier item in alive)
            {
                if ((!item.Broken && item.Morale > 0.3f) || b.Time < item.NextRally)
                {
                    continue;
                }

                SilentOrders.Core.Soldier soldier = null;
                foreach (SilentOrders.Core.Soldier item2 in alive)
                {
                    if (item2.Id != item.Id && !item2.Broken && item2.Morale > 0.35f && SilentOrders.Core.V2.Distance(item2.Position, item.Position) < 6f && (soldier == null || item2.Id == q.LeaderId))
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
                    SilentOrders.Core.Dialogue.Say(b, soldier, 10, urgent: true);
                }
            }
        }
    }
}
