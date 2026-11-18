using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class CommanderTransition
    {
        public bool Waiting;
        public CombatStyle Style = CombatStyle.Balanced;
        public ManeuverIntent Intent;
        public float AcceptedAt, ExecuteAt, Deadline, NextReview, LastProgress;
        public int LocalRepairs, ReviewNoRoute, ReviewSamples;
        public float NextBlockedReport;
        public int Accepted, Executed, Superseded, PreservedServices, PreservedFire, FinishedBounds, ExpiredBounds, BlockedReports;
        public string Phase = "Исполнение", Reason = "Начальный замысел";
        public readonly HashSet<int> Bounds = new HashSet<int>();
        public readonly Dictionary<int, V2> Positions = new Dictionary<int, V2>();
    }

    // Radio orders belong to the commander. Receipt, transition and execution
    // are distinct; ordinary orders do not reset every soldier's combat action.
    public static class CommanderOrders
    {
        public static void Receive(BattleSimulation b, Squad q)
        {
            var t = q.CommandTransition;
            if (t.Waiting && t.Style == q.PendingStyle && t.Intent == q.PendingIntent)
            {
                q.PriorityPending = false;
                return;
            }

            if (!t.Waiting && q.Style == q.PendingStyle && q.Intent == q.PendingIntent)
            {
                q.PriorityPending = false;
                q.PriorityStatus = "Приказ уже выполняется";
                return;
            }

            if (t.Waiting)
                t.Superseded++;
            t.Style = q.PendingStyle;
            t.Intent = q.PendingIntent;
            t.AcceptedAt = b.Time;
            t.Deadline = b.Time + 6f;
            t.Accepted++;
            t.Waiting = true;
            t.Bounds.Clear();
            q.PriorityPending = false;
            if (t.Intent != q.Intent || t.Style == CombatStyle.Withdraw || t.Style == CombatStyle.Passive)
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && s.Available && s.Deployed && !TacticalOrders.ServiceActive(s) && s.PathCursor < s.Path.Count && s.MoveAuthorized && b.Time - s.LastMoved < 1.5f && V2.Distance(s.Position, s.Destination) <= 5f)
                        t.Bounds.Add(s.Id);
            Report(b, q, "Принял: " + CommanderPriorities.StyleName(t.Style) + " / " + CommanderPriorities.IntentName(t.Intent), "commanderAccepted");
            Update(b, q);
        }

        static void Report(BattleSimulation b, Squad q, string reason, string kind)
        {
            q.CommandTransition.Reason = reason;
            q.PriorityStatus = reason;
            b.History.Event(b, kind, q.LeaderId, -1, new V2(), new V2(), (int)q.CommandTransition.Intent, reason);
            b.Log(q.Side, reason);
            if (q.LeaderId >= 0 && q.LeaderId < b.Soldiers.Count && b.Soldiers[q.LeaderId].Available)
                b.Say(b.Soldiers[q.LeaderId], reason, true);
        }

        public static void Update(BattleSimulation b, Squad q)
        {
            var t = q.CommandTransition;
            if (!t.Waiting)
                return;
            Soldier commander = null;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Deployed && s.Available && !s.Broken)
                {
                    if (s.Id == q.LeaderId)
                    {
                        commander = s;
                        break;
                    }

                    if (commander == null || s.Morale > commander.Morale)
                        commander = s;
                }

            if (commander == null)
            {
                if (b.Time >= t.NextReview)
                {
                    t.Phase = "Задержан";
                    Report(b, q, "Не могу перестроить группу: нет боеспособного командира или заместителя", "commanderBlocked");
                    t.NextReview = b.Time + 8;
                    t.BlockedReports++;
                }

                return;
            }

            q.LeaderId = commander.Id;
            var done = new List<int>();
            foreach (int id in t.Bounds)
            {
                var s = b.Soldiers[id];
                if (!s.Available || s.PathCursor >= s.Path.Count || V2.Distance(s.Position, s.Destination) < .6f || !s.MoveAuthorized || b.Time - s.LastMoved >= 2.5f)
                {
                    done.Add(id);
                    t.FinishedBounds++;
                }
            }

            foreach (int id in done)
                t.Bounds.Remove(id);
            if (t.Bounds.Count > 0 && b.Time < t.Deadline)
            {
                t.Phase = "Переход";
                q.PriorityStatus = "Принял. Завершаем короткую перебежку под прежним прикрытием";
                return;
            }

            t.ExpiredBounds += t.Bounds.Count;
            t.Bounds.Clear();
            q.PendingStyle = t.Style;
            q.PendingIntent = t.Intent;
            TacticalOrders.Execute(b, q, false);
            t.Waiting = false;
            t.Executed++;
            t.ExecuteAt = b.Time;
            t.LastProgress = b.Time;
            t.NextReview = b.Time + 12;
            t.Phase = "Исполнение";
            t.Positions.Clear();
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available)
                    t.Positions[s.Id] = s.Position;
            Report(b, q, "Выполняем: " + CommanderPriorities.StyleName(q.Style) + " / " + CommanderPriorities.IntentName(q.Intent), "commanderExecuting");
        }

        public static bool Waiting(BattleSimulation b, Squad q)
        {
            Update(b, q);
            return q.CommandTransition.Waiting;
        }

        public static void Review(BattleSimulation b, Squad q)
        {
            var t = q.CommandTransition;
            CommandHierarchy.Maintain(b, q);
            if (b.CustomDefenders && q == b.Red)
                return;
            if (t.Waiting || b.Time < 10 || b.Time < t.NextReview || TacticalOrders.Emergency(q) || WithdrawalTactics.Active(q) || q.Style == CombatStyle.Passive)
                return;
            t.NextReview = b.Time + 6;
            t.ReviewSamples++;
            Soldier chosen = null;
            float oldest = b.Time;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && s.Deployed)
                {
                    ExecutionReview.Sample(b, s);
                    var e = s.Execution;
                    if (e.StalledSince < 0 || b.Time - e.StalledSince < 8 || b.Time - s.Progress.ProgressAt < 8 || b.Time < e.NextRepair || ExecutionReview.ProtectedTask(b, s) || ExecutionReview.UsefulWatch(b, s))
                        continue;
                    if (e.StalledSince < oldest)
                    {
                        oldest = e.StalledSince;
                        chosen = s;
                    }
                }

            if (chosen == null)
            {
                t.Phase = "Исполнение";
                return;
            }

            var memory = chosen.Execution;
            memory.CommanderReviews++;
            // No target does not make an interrupted movement assignment useful overwatch.
            // Return only this unit's failed contract, preserving every other running action.
            if (TacticalOrders.ClosestKnown(b, chosen) == null && CommandHierarchy.Owns(q))
            {
                CommandHierarchy.Failed(b, chosen, "Нет продвижения без контакта: пересмотреть маршрут этой двойки");
                var pair = CommandHierarchy.Pair(q, chosen.Assignment.PairId);
                if (pair != null)
                {
                    pair.LastTried = chosen.Id;
                    pair.NextReview = pair.NextBound = 0;
                }

                memory.NextRepair = b.Time + 16;
                memory.Repairs++;
                t.LocalRepairs++;
                memory.LastRepair = memory.Cause;
                t.Phase = "Локальная коррекция";
                b.History.Event(b, "commanderLocalRepair", q.LeaderId, chosen.Id, chosen.Position, chosen.Position, 0, memory.Cause);
                return;
            }

            // Reuse the single existing recovery owner; retain the player's intent.
            if (q.SupportIds.Remove(chosen.Id))
            {
                chosen.SupportReady = false;
                memory.ReleasedSupport++;
            }

            // One bounded retry per chosen member, not a reset of all squad actions.
            chosen.Progress.NextReview = b.Time;
            chosen.Tactical.NextUsefulReview = b.Time;
            int started = chosen.Progress.Started, failed = chosen.Progress.NoRoute;
            ProgressRecovery.Decide(b, chosen);
            memory.NextRepair = b.Time + 10;
            if (chosen.Progress.Started > started)
            {
                memory.Repairs++;
                t.LocalRepairs++;
                memory.LastRepair = memory.Cause;
                t.Phase = "Локальная коррекция";
                b.History.Event(b, "commanderLocalRepair", q.LeaderId, chosen.Id, chosen.Position, chosen.Destination, 0, memory.Cause);
            }
            else if (chosen.Progress.NoRoute > failed)
            {
                t.ReviewNoRoute++;
                t.Phase = "Задержан";
                if (b.Time >= t.NextBlockedReport)
                {
                    t.NextBlockedReport = b.Time + 24;
                    t.BlockedReports++;
                    Report(b, q, "Замысел сохраняю. " + chosen.Name + ": " + memory.Cause + "; безопасное смещение не найдено. Нужна оценка направления", "commanderBlocked");
                }
            }
        }
    }
}
