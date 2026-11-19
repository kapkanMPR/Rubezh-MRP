using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CommanderPriorities
    {
        public static string StyleName(CombatStyle s)
        {
            if (s == CombatStyle.EmergencyWithdraw)
                return "да ну нахер!";
            object result;
            switch (s)
            {
                default:
                    result = "баланс";
                    break;
                case CombatStyle.Aggressive:
                    result = "агрессивно";
                    break;
                case CombatStyle.Passive:
                    result = "закрепление";
                    break;
                case CombatStyle.Withdraw:
                    result = "отход";
                    break;
            }

            return (string)result;
        }

        public static string IntentName(ManeuverIntent i)
        {
            object result;
            switch (i)
            {
                default:
                    result = "центр";
                    break;
                case ManeuverIntent.Breakthrough:
                    return "Центр";
                case ManeuverIntent.Straight:
                    return "Прямо";
                case ManeuverIntent.Front:
                    return "Фронтом";
                case ManeuverIntent.RightHold:
                    result = "обхват справа";
                    break;
                case ManeuverIntent.LeftHold:
                    result = "обхват слева";
                    break;
                case ManeuverIntent.Both:
                    result = "оба фланга";
                    break;
                case ManeuverIntent.Right:
                    result = "правый фланг";
                    break;
                case ManeuverIntent.Left:
                    result = "левый фланг";
                    break;
            }

            return (string)result;
        }

        public static void Request(BattleSimulation b, int style, int intent)
        {
            TacticalOrders.Request(b, style, intent);
        }

        public static void Feedback(BattleSimulation b, Squad q, System.Collections.Generic.List<Soldier> units)
        {
            foreach (Soldier unit in units)
            {
                string text = (
                    unit.Broken
                        ? "Не могу вести бой"
                        : (
                            (unit.ReloadUntil > b.Time)
                                ? "Перезаряжаюсь"
                                : (
                                    (unit.Suppression > 0.7f)
                                        ? "Прижат"
                                        : (
                                            (unit.Ammo < 3)
                                                ? "Нет патронов"
                                                : (
                                                    (b.Time < unit.RejectedUntil)
                                                        ? "Маршрут недоступен"
                                                        : (
                                                            unit.Moving
                                                                ? "Перемещаюсь"
                                                                : (
                                                                    SquadBrain.Ready(b, unit, q)
                                                                        ? "Прикрытие готово"
                                                                        : (
                                                                            (
                                                                                unit.Visible.Count == 0
                                                                                && q.Contacts.Count > 0
                                                                            )
                                                                                ? "Не вижу цель"
                                                                                : "На позиции"
                                                                        )
                                                                )
                                                        )
                                                )
                                        )
                                )
                        )
                );
                if (text != unit.Readiness && b.Time >= unit.NextStatus)
                {
                    unit.Readiness = text;
                    unit.NextStatus = b.Time + 5f;
                    b.Say(unit, text);
                }
            }
        }

        public static bool SmokeSafe(BattleSimulation b, Soldier s)
        {
            if (b.Time < b.Team(s).SmokeWaitUntil)
            {
                return false;
            }

            bool result = false;
            V2 v = ((s.PathCursor < s.Path.Count) ? s.Path[s.PathCursor] : s.OrderAnchor);
            foreach (Contact value in Knowledge.Contacts(b, s))
            {
                if (!(b.Time - value.SeenAt > 8f))
                {
                    result = true;
                    if (
                        b.Map.LineOfSight(value.Position, s.Position)
                        && b.SmokeTransmission(value.Position, s.Position) > 0.25f
                    )
                    {
                        return false;
                    }

                    if (
                        b.Map.LineOfSight(value.Position, v)
                        && b.SmokeTransmissionAt(
                            value.Position,
                            v,
                            b.Time + V2.Distance(s.Position, v) / Math.Max(0.1f, b.Config.MoveSpeed) + 1f
                        ) > 0.25f
                    )
                    {
                        return false;
                    }
                }
            }

            return result;
        }

        private static V2 Center(System.Collections.Generic.List<Soldier> units)
        {
            V2 a = default(V2);
            foreach (Soldier unit in units)
            {
                a += unit.Position;
            }

            return a / Math.Max(1, units.Count);
        }

        private static void Status(BattleSimulation b, Squad q, string text)
        {
            text = Dialogue.Adapt(b, b.Soldiers[q.LeaderId], text);
            q.Execution = text;
            q.Plan = text;
            if (text != q.CommanderOrder && b.Time >= q.NextFeedback)
            {
                q.CommanderOrder = text;
                q.NextFeedback = b.Time + 6f;
                b.Say(b.Soldiers[q.LeaderId], text, urgent: true);
                b.Log(q.Side, b.Soldiers[q.LeaderId].Name + ": " + text);
            }
        }

        public static void Plan(BattleSimulation b, Squad q, System.Collections.Generic.List<Soldier> units, V2 threat)
        {
            TacticalOrders.ApplyPending(b, q);
            if (CommanderOrders.Waiting(b, q))
                return;
            if (!q.PriorityInitialized)
            {
                q.PriorityInitialized = true;
                q.Phase = ManeuverPhase.Hold;
                q.LastProgress = b.Time;
                q.ProgressCenter = Center(units);
            }

            if (BreakthroughTactics.Plan(b, q, units))
                return;
            SquadCoordination.ReleaseBounds(b, q);
            V2 v = Center(units);
            int compromisedCount = 0;
            bool hasBusyMember = false;
            foreach (Soldier member in b.Soldiers)
            {
                if (q.Contains(member) && member.Alive)
                {
                    if (member.Broken || member.Incapacitated || member.Health < 30f)
                    {
                        compromisedCount++;
                    }

                    if (member.Busy)
                    {
                        hasBusyMember = true;
                    }
                }
            }

            if (V2.Distance(v, q.ProgressCenter) > 2f)
            {
                q.ProgressCenter = v;
                q.LastProgress = b.Time;
                q.CombatStall = 0f;
            }
            else
            {
                q.CombatStall = b.Time - q.LastProgress;
            }

            CombatCoordination.Assess(b, q);
            q.DelayReason = "";
            if (!hasBusyMember && TryQuietAdvance(b, q, units))
            {
                return;
            }

            if (
                q.Style == CombatStyle.Aggressive
                && (q.Phase == ManeuverPhase.Move || q.Phase == ManeuverPhase.Prepare)
            )
            {
                SmokePlanner.Plan(b, q, q.Stage);
            }

            if (FireSupportBrain.Update(b, q))
            {
                q.PhaseSince += b.Config.SquadPeriod;
                Status(b, q, q.SupportStatus + "; обходящая группа удерживает прикрытие");
                return;
            }

            int fitCount = 0;
            foreach (Soldier unit2 in units)
            {
                if (!unit2.Broken && unit2.Suppression < 0.75f && unit2.Ammo + unit2.Reserve > 0 && unit2.Health >= 30f)
                {
                    fitCount++;
                }
            }

            if (hasBusyMember && (q.Phase == ManeuverPhase.Move || q.Phase == ManeuverPhase.Prepare))
            {
                if (q.AidPauseSince < 0f)
                {
                    q.AidPauseSince = b.Time;
                }

                if (b.Time - q.AidPauseSince >= 18f)
                {
                    CombatCoordination.PauseMovers(b, q, pause: false);
                }

                if (b.Time - q.AidPauseSince < 18f)
                {
                    CombatCoordination.PauseMovers(b, q, pause: true);
                    q.PhaseSince += b.Config.SquadPeriod;
                    Status(b, q, "Манёвр сохранён. Удерживаем позиции на время помощи своим");
                    return;
                }
            }
            else if (q.AidPauseSince >= 0f)
            {
                q.AidPauseSince = -1f;
                CombatCoordination.PauseMovers(b, q, pause: false);
            }

            if (q.Phase == ManeuverPhase.Prepare)
            {
                q.ReadyCount = SquadBrain.CountReady(b, q);
                q.RequiredSupport = ((units.Count > 1) ? 1 : 0);
                b.Stats.CoverChecks++;
                bool allRoutesSafe = true;
                bool hasMover = false;
                foreach (int maneuverId in q.ManeuverIds)
                {
                    Soldier soldier = b.Soldiers[maneuverId];
                    if (soldier.Available)
                    {
                        hasMover = true;
                        if (!SmokeSafe(b, soldier) && !TacticalAI.SafeTravel(b, soldier))
                        {
                            allRoutesSafe = false;
                        }
                    }
                }

                bool readyToMove = SquadCoordination.AnyRouteCovered(b, q) || (allRoutesSafe && hasMover);
                if (b.Time < q.SmokeWaitUntil || q.PlannedSmoke.Count > 0)
                {
                    readyToMove = false;
                    Status(b, q, "Жду раскрытия дыма; позиции удерживать");
                }
                else if (readyToMove)
                {
                    if (q.ReadySince < 0f)
                    {
                        q.ReadySince = b.Time;
                    }

                    if (
                        b.Time - q.ReadySince
                        >= ((q.Style == CombatStyle.Aggressive || q.OpeningUntil > b.Time) ? 0.6f : 1.2f)
                    )
                    {
                        q.Phase = ManeuverPhase.Move;
                        q.PhaseSince = b.Time;
                        foreach (int maneuverId2 in q.ManeuverIds)
                        {
                            Soldier authorizedMover = b.Soldiers[maneuverId2];
                            authorizedMover.MoveAuthorized =
                                maneuverId2 == q.ManeuverIds[0] && SquadBrain.MovementCovered(b, authorizedMover);
                            authorizedMover.NextDecision = 0f;
                        }

                        b.Stats.Bounds++;
                        Status(
                            b,
                            q,
                            allRoutesSafe
                                ? "Маршрут безопасен или закрыт завесой. Выполняем смену позиции"
                                : "Прикрытие подтвердило готовность. Маневр разрешён"
                        );
                    }
                }
                else
                {
                    q.ReadySince = -1f;
                    b.Stats.SupportWaits++;
                    Status(b, q, "Замысел принят, жду прикрытие: " + q.ReadyCount + "/" + q.RequiredSupport);
                }

                if (!readyToMove && b.Time - q.PhaseSince > 7f)
                {
                    SupportAI.TrySmoke(b, q, v);
                }

                if (b.Time - q.PhaseSince > 22f)
                {
                    Abort(b, q, units, threat, "Прикрытие не готово. Пересматриваю позиции");
                }
            }
            else if (q.Phase == ManeuverPhase.Move)
            {
                bool allMoversArrived = true;
                int moverCount = 0;
                foreach (int maneuverId3 in q.ManeuverIds)
                {
                    Soldier mover = b.Soldiers[maneuverId3];
                    if (mover.Available && !mover.Broken)
                    {
                        moverCount++;
                        if (
                            V2.Distance(mover.Position, mover.OrderAnchor) > 1.1f
                            || (
                                !PositionLogic.Protected(b, mover, mover.Position)
                                && V2.Distance(mover.Position, b.Map.Objective) > 1.9f
                            )
                        )
                        {
                            allMoversArrived = false;
                        }
                    }
                }

                Status(
                    b,
                    q,
                    q.PriorityChanged
                        ? "Новый приоритет принят; завершаю текущую перебежку"
                        : (
                            (q.Style == CombatStyle.Withdraw)
                                ? "Отходим к соседним укрытиям; поддержка остаётся"
                                : ("Выполняем замысел: " + IntentName(q.Intent))
                        )
                );
                if (q.EnvelopmentActive && !q.PriorityChanged)
                {
                    bool hasSupport = false;
                    foreach (int supportId in q.SupportIds)
                    {
                        Soldier supporter = b.Soldiers[supportId];
                        if (supporter.Available && !supporter.Broken && supporter.Ammo + supporter.Reserve > 0)
                        {
                            hasSupport = true;
                        }
                    }

                    if (
                        moverCount == 0
                        || !hasSupport
                        || compromisedCount >= 2
                        || b.Time - q.PhaseSince > 24f
                        || b.Time - q.EnvelopmentSince > 75f
                    )
                    {
                        Abort(b, q, units, threat, "Обхват остановлен обороной или потерей поддержки. Перегруппировка");
                        return;
                    }

                    if (allMoversArrived && !FlankReached(b, q))
                    {
                        ContinueEnvelopment(b, q, threat);
                        return;
                    }

                    if (allMoversArrived)
                    {
                        q.EnvelopmentActive = false;
                        q.SupportIds.Clear();
                        Status(b, q, "Обходящая группа заняла фланг. Поддержка может подтянуться");
                    }
                }

                if (allMoversArrived || moverCount == 0)
                {
                    q.Phase = ManeuverPhase.Regroup;
                    q.PhaseSince = b.Time;
                    q.RegroupAssigned = false;
                }
                else if (b.Time - q.PhaseSince > 30f)
                {
                    Abort(b, q, units, threat, "Продвижение остановилось. Сохраняем людей, меняю план");
                }
            }
            else if (q.Phase == ManeuverPhase.Regroup)
            {
                if (!q.RegroupAssigned)
                {
                    q.RegroupAssigned = true;
                    q.Revision++;
                    foreach (int maneuverId4 in q.ManeuverIds)
                    {
                        Soldier arrivedMover = b.Soldiers[maneuverId4];
                        if (arrivedMover.Available)
                        {
                            SquadBrain.GiveOrder(
                                b,
                                q,
                                arrivedMover,
                                Job.Cover,
                                arrivedMover.Position,
                                authorized: true
                            );
                        }
                    }

                    foreach (Soldier unit3 in units)
                    {
                        if (!q.ManeuverIds.Contains(unit3.Id))
                        {
                            float roleOffset = (
                                unit3.Sniper
                                    ? (-3.5f)
                                    : (
                                        (unit3.Role == Role.Medic)
                                            ? (-2f)
                                            : ((unit3.Id == q.LeaderId) ? (-1.5f) : (-1f))
                                    )
                            );
                            if (q.Style == CombatStyle.Withdraw)
                            {
                                roleOffset = 0f - Math.Abs(roleOffset);
                            }

                            V2 rallyPoint = q.Stage + new V2(roleOffset, (unit3.Role != Role.MachineGunner) ? 1 : (-1));
                            SquadBrain.GiveOrder(
                                anchor: SquadBrain.FindPost(
                                    b,
                                    unit3,
                                    new V2(
                                        Util.Clamp(rallyPoint.X, 2.5f, (float)b.Map.Columns - 2.5f),
                                        Util.Clamp(rallyPoint.Y, 3.5f, (float)b.Map.Rows - 4.5f)
                                    ),
                                    threat,
                                    firing: true
                                ),
                                b: b,
                                q: q,
                                s: unit3,
                                job: (unit3.Id != q.LeaderId) ? Job.Cover : Job.Command,
                                authorized: true
                            );
                        }
                    }
                }

                bool allSettled = true;
                foreach (Soldier unit4 in units)
                {
                    if (
                        unit4.Moving
                        || V2.Distance(unit4.Position, unit4.OrderAnchor) > 1.2f
                        || (
                            !PositionLogic.Protected(b, unit4, unit4.Position)
                            && V2.Distance(unit4.Position, b.Map.Objective) > 1.9f
                        )
                    )
                    {
                        allSettled = false;
                    }
                }

                Status(
                    b,
                    q,
                    hasBusyMember
                        ? "Удерживаем позиции, ждём помощь своим"
                        : "Новые позиции заняты; подтягиваем поддержку и медика"
                );
                if (
                    !hasBusyMember
                    && (
                        (allSettled && b.Time - q.PhaseSince > ((q.Style == CombatStyle.Aggressive) ? 1.2f : 3f))
                        || b.Time - q.PhaseSince > 22f
                    )
                )
                {
                    q.Phase = ManeuverPhase.Hold;
                    q.NextPriorityPlan =
                        b.Time
                        + ((q.Style == CombatStyle.Passive) ? 8f : ((q.Style == CombatStyle.Aggressive) ? 0.5f : 2f));
                    q.ManeuverIds.Clear();
                }
            }
            else
            {
                if (b.Time < q.NextPriorityPlan)
                {
                    return;
                }

                if (hasBusyMember)
                {
                    Status(b, q, "Приоритет сохранён. Сначала помощь своим и пополнение запасов");
                    SupportAI.TrySmoke(b, q, v);
                    q.NextPriorityPlan = b.Time + 3f;
                    return;
                }

                q.EmergencyRetreat =
                    compromisedCount >= 2 || (q.Contacts.Count > 0 && fitCount < Math.Min(2, units.Count));
                if (q.EmergencyRetreat && v.X < 5f)
                {
                    Status(b, q, "Группа истощена. Удерживаем безопасные тыловые позиции");
                    return;
                }

                bool atObjective = false;
                foreach (Soldier unit5 in units)
                {
                    if (V2.Distance(unit5.Position, b.Map.Objective) < 2.1f)
                    {
                        atObjective = true;
                    }
                }

                if (atObjective && q.Style != 0)
                {
                    Status(b, q, "Удерживаем точку; не покидаем зону ради маневра");
                }
                else if (q.Style == CombatStyle.Withdraw && v.X < 5f)
                {
                    Status(b, q, "Отход завершён. Удерживаем тыловые укрытия");
                }
                else if (
                    !q.EmergencyRetreat
                    && q.Style == CombatStyle.Passive
                    && q.Contacts.Count > 0
                    && q.CombatStall < 40f
                )
                {
                    Status(b, q, "Закрепление: удерживаем занятые позиции и секторы");
                }
                else
                {
                    Start(b, q, units, threat, v);
                }
            }
        }

        private static bool TryQuietAdvance(BattleSimulation b, Squad q, System.Collections.Generic.List<Soldier> units)
        {
            if (q.Style == CombatStyle.Withdraw || q.CombatStall < 30f)
            {
                return false;
            }

            foreach (Contact value in q.Contacts.Values)
            {
                if (b.Time - value.SeenAt < 10f && value.Confidence > 0.25f)
                {
                    return false;
                }
            }

            foreach (Soldier unit in units)
            {
                if (
                    unit.Moving
                    || unit.Busy
                    || unit.Broken
                    || unit.Suppression > 0.25f
                    || b.Time - unit.LastIncoming < 10f
                    || unit.Visible.Count > 0
                )
                {
                    return false;
                }

                if (V2.Distance(unit.Position, b.Map.Objective) < 2.1f)
                {
                    return false;
                }

                foreach (Contact value2 in unit.Contacts.Values)
                {
                    if (b.Time - value2.SeenAt < 10f && value2.Confidence > 0.25f)
                    {
                        return false;
                    }
                }
            }

            System.Collections.Generic.List<Soldier> list = new System.Collections.Generic.List<Soldier>(units);
            list.Sort(
                (Soldier a, Soldier z) =>
                    V2.Distance(a.Position, b.Map.Objective).CompareTo(V2.Distance(z.Position, b.Map.Objective))
            );
            foreach (Soldier item in list)
            {
                System.Collections.Generic.List<V2> list2 = Pathfinder.Find(
                    b.Map,
                    item.Position,
                    b.Map.NearestOpen(b.Map.Objective),
                    (V2 p) => b.Map.NearCover(p) ? 0f : 0.3f
                );
                float routeDistance = 0f;
                V2 a2 = item.Position;
                V2 v = item.Position;
                float bestScore = -10000f;
                foreach (V2 item2 in list2)
                {
                    routeDistance += V2.Distance(a2, item2);
                    a2 = item2;
                    if (routeDistance > 7f)
                    {
                        break;
                    }

                    if (routeDistance < 2f)
                    {
                        continue;
                    }

                    bool occupied = false;
                    foreach (Soldier unit2 in units)
                    {
                        if (
                            unit2.Id != item.Id
                            && (
                                V2.Distance(item2, unit2.Position) < 1.7f
                                || V2.Distance(item2, unit2.OrderAnchor) < 1.7f
                            )
                        )
                        {
                            occupied = true;
                        }
                    }

                    if (!occupied)
                    {
                        float candidateScore = routeDistance + (float)(b.Map.NearCover(item2) ? 2 : 0);
                        if (candidateScore > bestScore)
                        {
                            v = item2;
                            bestScore = candidateScore;
                        }
                    }
                }

                if (bestScore == -10000f)
                {
                    continue;
                }

                FireSupportBrain.Reset(b, q);
                q.EnvelopmentActive = false;
                q.SupportIds.Clear();
                q.PlannedSmoke.Clear();
                q.ManeuverIds.Clear();
                q.ManeuverIds.Add(item.Id);
                q.Stage = v;
                q.Revision++;
                q.Phase = ManeuverPhase.Prepare;
                q.PhaseSince = b.Time;
                q.ReadySince = -1f;
                q.LastProgress = b.Time;
                q.ProgressCenter = Center(units);
                q.Replans++;
                b.Stats.Stalemates++;
                SquadBrain.GiveOrder(b, q, item, Job.Reposition, v, authorized: false);
                Status(b, q, "Контакта нет. Проверяем путь к точке короткой перебежкой; остальные прикрывают");
                return true;
            }

            return false;
        }

        private static bool FlankReached(BattleSimulation b, Squad q)
        {
            if (q.EnvelopmentBounds < 2)
            {
                return false;
            }

            foreach (int maneuverId in q.ManeuverIds)
            {
                Soldier soldier = b.Soldiers[maneuverId];
                if (
                    !soldier.Available
                    || (
                        (
                            (q.Intent == ManeuverIntent.LeftHold)
                                ? (soldier.Position.Y < q.EnvelopmentThreat.Y - 4f)
                                : (soldier.Position.Y > q.EnvelopmentThreat.Y + 4f)
                        ) && !(soldier.Position.X < q.EnvelopmentThreat.X - 6f)
                    )
                )
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void ContinueEnvelopment(BattleSimulation b, Squad q, V2 threat)
        {
            q.EnvelopmentBounds++;
            q.Revision++;
            q.ReadySince = -1f;
            q.Phase = ManeuverPhase.Prepare;
            q.PhaseSince = b.Time;
            float b2 = (
                (q.Intent == ManeuverIntent.LeftHold) ? ((float)b.Map.Rows * 0.23f) : ((float)b.Map.Rows * 0.8f)
            );
            V2 v = default(V2);
            int availableMovers = 0;
            foreach (int maneuverId in q.ManeuverIds)
            {
                Soldier soldier = b.Soldiers[maneuverId];
                if (soldier.Available)
                {
                    v += soldier.Position;
                    availableMovers++;
                }
            }

            v /= (float)Math.Max(1, availableMovers);
            q.Stage = new V2(Math.Min(b.Map.Objective.X, v.X + 4f), Util.Lerp(v.Y, b2, 0.6f));
            int slotIndex = 0;
            foreach (int maneuverId2 in q.ManeuverIds)
            {
                Soldier mover = b.Soldiers[maneuverId2];
                if (mover.Available)
                {
                    V2 wanted = q.Stage + new V2(0f, (slotIndex++ != 0) ? 1 : (-1));
                    V2 anchor = SquadBrain.FindPost(b, mover, wanted, threat, firing: false);
                    SquadBrain.GiveOrder(b, q, mover, Job.Reposition, anchor, authorized: false);
                }
            }

            SmokePlanner.Plan(b, q, q.Stage);
            Status(b, q, "Поддержка остаётся и подавляет. Обходящая группа продолжает обхват");
        }

        public static void Abort(
            BattleSimulation b,
            Squad q,
            System.Collections.Generic.List<Soldier> units,
            V2 threat,
            string reason
        )
        {
            foreach (Soldier unit in units)
            {
                unit.RejectedPost = unit.OrderAnchor;
                unit.RejectedUntil = b.Time + 12f;
                SquadBrain.GiveOrder(
                    b,
                    q,
                    unit,
                    (unit.Id != q.LeaderId) ? Job.Cover : Job.Command,
                    SquadBrain.FindPost(b, unit, unit.Position, threat, firing: true),
                    authorized: true
                );
            }

            FireSupportBrain.Reset(b, q);
            q.PlannedSmoke.Clear();
            q.EnvelopmentActive = false;
            q.SupportIds.Clear();
            q.Phase = ManeuverPhase.Hold;
            q.ManeuverIds.Clear();
            q.NextPriorityPlan = b.Time + 4f;
            q.Replans++;
            b.Stats.PlanAborts++;
            b.Stats.Stalemates++;
            Status(b, q, reason);
        }

        private static void Start(
            BattleSimulation b,
            Squad q,
            System.Collections.Generic.List<Soldier> units,
            V2 threat,
            V2 center
        )
        {
            FireSupportBrain.Reset(b, q);
            bool holdFlank =
                q.Contacts.Count > 0
                && (q.Intent == ManeuverIntent.LeftHold || q.Intent == ManeuverIntent.RightHold)
                && q.Style != 0
                && !q.EmergencyRetreat;
            q.SupportIds.Clear();
            q.EnvelopmentActive = false;
            if (holdFlank)
            {
                foreach (Soldier unit in units)
                {
                    if (
                        (unit.Role == Role.MachineGunner || unit.Sniper || unit.SupplyPack)
                        && !unit.Busy
                        && !unit.Broken
                        && unit.Ammo + unit.Reserve > 0
                        && q.SupportIds.Count < 2
                    )
                    {
                        q.SupportIds.Add(unit.Id);
                    }
                }
            }

            if (holdFlank && q.SupportIds.Count == 0)
                foreach (var u in units)
                    if (u.Role == Role.Rifleman && !u.Broken && u.Ammo > 0)
                    {
                        q.SupportIds.Add(u.Id);
                        break;
                    }

            holdFlank = holdFlank && q.SupportIds.Count > 0 && units.Count > q.SupportIds.Count;
            System.Collections.Generic.List<Soldier> list = new System.Collections.Generic.List<Soldier>();
            foreach (Soldier unit2 in units)
            {
                if (
                    !unit2.Busy
                    && !unit2.Broken
                    && (
                        unit2.Role != Role.Medic
                        || (MedicTactics.FreeToFight(b, unit2) && q.Style != CombatStyle.Aggressive)
                        || q.Style == CombatStyle.Withdraw
                        || q.EmergencyRetreat
                    )
                    && (unit2.Health >= 35f || q.Style == CombatStyle.Withdraw || q.EmergencyRetreat)
                    && unit2.Suppression < ((q.Style == CombatStyle.Aggressive) ? 0.78f : 0.7f)
                    && (holdFlank ? (!q.SupportIds.Contains(unit2.Id)) : (unit2.Id != q.LeaderId))
                )
                {
                    list.Add(unit2);
                }
            }

            list.Sort(
                (Soldier a, Soldier z) =>
                    SquadCoordination.MovePriority(b, q, a).CompareTo(SquadCoordination.MovePriority(b, q, z))
            );
            if (list.Count == 0 && units.Count == 1)
            {
                list.Add(units[0]);
            }

            if (list.Count == 0)
            {
                Status(b, q, "Нет готовых к перемещению. Восстанавливаем группу");
                return;
            }

            float boundDistance = (
                (q.Style == CombatStyle.Aggressive) ? 7f : ((q.Style == CombatStyle.Passive) ? 3f : 4.5f)
            );
            boundDistance += (
                (q.Character == CommanderCharacter.Persistent)
                    ? 0.5f
                    : ((q.Character == CommanderCharacter.Cautious) ? (-0.5f) : 0f)
            );
            V2 axis = TacticalOrders.Axis(b, q.Side),
                lateral = new V2(-axis.Y, axis.X),
                origin = q.Side == Side.Green ? b.Map.Entry : b.Map.Objective;
            V2 objective = q.Side == Side.Green ? b.Map.Objective : b.Map.Entry;
            float objectiveProgress = V2.Dot(objective - origin, axis),
                centerProgress = V2.Dot(center - origin, axis),
                centerLateral = V2.Dot(center - origin, lateral);
            float startProgress = centerProgress;
            foreach (var u in units)
                if (u.Role != Role.Medic)
                    startProgress = Math.Max(startProgress, V2.Dot(u.Position - origin, axis));
            bool withdrawing = q.Style == CombatStyle.Withdraw || q.EmergencyRetreat;
            float targetProgress = withdrawing ? centerProgress - 4 : startProgress + boundDistance;
            float wing =
                q.Intent == ManeuverIntent.Left || q.Intent == ManeuverIntent.LeftHold ? -1
                : q.Intent == ManeuverIntent.Right || q.Intent == ManeuverIntent.RightHold ? 1
                : 0;
            if (q.Intent == ManeuverIntent.Both && !SquadCoordination.CanSplit(b, units))
                wing = V2.Dot(center - threat, lateral) <= 0 ? -1 : 1;
            float width = Math.Abs(lateral.X) * b.Map.Columns + Math.Abs(lateral.Y) * b.Map.Rows;
            float desiredLateral = wing * width * .22f;
            if (q.CombatStall > 25 && q.Contacts.Count > 0 && !withdrawing)
            {
                q.Replans++;
                desiredLateral += q.Replans % 2 == 0 ? 3 : -3;
                b.Stats.Stalemates++;
            }

            if (targetProgress > objectiveProgress - 6)
                desiredLateral = Util.Lerp(desiredLateral, 0, .7f);
            q.Stage = TacticalOrders.Open(
                b,
                origin
                    + axis * Util.Clamp(targetProgress, 0, objectiveProgress)
                    + lateral * Util.Lerp(centerLateral, desiredLateral, .5f)
            );
            q.Revision++;
            q.ManeuverIds.Clear();
            q.RequiredSupport = ((units.Count > 1) ? 1 : 0);
            q.ReadySince = -1f;
            q.PriorityChanged = false;
            int desiredMovers = Math.Min(2, Math.Min(list.Count, Math.Max(1, units.Count - 1)));
            foreach (Soldier unit4 in units)
            {
                SquadBrain.GiveOrder(
                    b,
                    q,
                    unit4,
                    (unit4.Id != q.LeaderId) ? Job.Cover : Job.Command,
                    SquadBrain.FindPost(b, unit4, unit4.Position, threat, firing: true),
                    authorized: true
                );
            }

            for (int i = 0; i < desiredMovers; i++)
            {
                Soldier soldier = list[i];
                V2 wanted = q.Stage + lateral * ((i == 0) ? -1.5f : 1.5f);
                if (
                    q.Intent == ManeuverIntent.Both
                    && !withdrawing
                    && targetProgress < objectiveProgress - 5f
                    && SquadCoordination.CanSplit(b, units)
                )
                {
                    wanted =
                        origin
                        + axis * targetProgress
                        + lateral * Util.Lerp(centerLateral, (i == 0 ? -1 : 1) * width * .22f, .55f);
                }

                if (holdFlank && !withdrawing)
                {
                    wanted -= axis * Math.Max(0, V2.Dot(wanted - origin, axis) - (startProgress + 3));
                    b.Stats.Flanks++;
                }

                V2 v = SquadBrain.FindPost(
                    b,
                    soldier,
                    wanted,
                    threat,
                    false,
                    targetProgress >= objectiveProgress - 2f && !withdrawing
                );
                if (!(V2.Distance(v, soldier.Position) < 0.8f))
                {
                    if (!SquadBrain.GiveOrder(b, q, soldier, Job.Reposition, v, authorized: false))
                        continue;
                    q.ManeuverIds.Add(soldier.Id);
                    soldier.Order = (
                        withdrawing
                            ? "Приоритет отхода: ближайшая тыловая позиция"
                            : (
                                (q.Intent == ManeuverIntent.Both)
                                    ? "Своя сторона разделённой группы; дождаться прикрытия"
                                    : ("Приоритет " + IntentName(q.Intent) + "; движение после подтверждения")
                            )
                    );
                }
            }

            if (q.ManeuverIds.Count == 0)
            {
                q.NextPriorityPlan = b.Time + 5f;
                q.Replans++;
                Status(b, q, "Подходящих новых укрытий нет; проверяю другой маршрут");
                return;
            }

            if (holdFlank)
            {
                q.EnvelopmentActive = true;
                q.EnvelopmentSince = b.Time;
                q.EnvelopmentBounds = 1;
                q.EnvelopmentThreat = threat;
                foreach (int supportId in q.SupportIds)
                {
                    b.Soldiers[supportId].Order =
                        "Подавлять угрозы обходящей группе; при потере сектора сменить рубеж двойкой";
                }
            }

            if (q.Intent == ManeuverIntent.Both)
            {
                b.Stats.SplitManeuvers++;
            }

            if (withdrawing)
            {
                b.Stats.Retreats++;
            }

            q.Phase = ManeuverPhase.Prepare;
            q.PhaseSince = b.Time;
            bool underPressure = false;
            foreach (Soldier unit5 in units)
            {
                if (unit5.Suppression > 0.45f || b.Time - unit5.LastIncoming < 3f)
                {
                    underPressure = true;
                }
            }

            SmokePlanner.Plan(b, q, q.Stage);
            if (q.PlannedSmoke.Count == 0 && underPressure && (withdrawing || q.CombatStall > 18f))
            {
                SupportAI.TrySmoke(b, q, center);
            }

            Status(
                b,
                q,
                q.EmergencyRetreat
                    ? "Приоритет сохранён, но группа истощена. Локальный отход под прикрытием"
                    : ("Распределяю позиции: " + IntentName(q.Intent) + "; поддержка подтверждает готовность")
            );
        }
    }
}
