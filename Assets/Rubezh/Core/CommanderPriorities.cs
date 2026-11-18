using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CommanderPriorities
    {
        public static string StyleName(SilentOrders.Core.CombatStyle s)
        {
            if (s == CombatStyle.EmergencyWithdraw)
                return "да ну нахер!";
            object result;
            switch (s)
            {
                default:
                    result = "баланс";
                    break;
                case SilentOrders.Core.CombatStyle.Aggressive:
                    result = "агрессивно";
                    break;
                case SilentOrders.Core.CombatStyle.Passive:
                    result = "закрепление";
                    break;
                case SilentOrders.Core.CombatStyle.Withdraw:
                    result = "отход";
                    break;
            }

            return (string)result;
        }

        public static string IntentName(SilentOrders.Core.ManeuverIntent i)
        {
            object result;
            switch (i)
            {
                default:
                    result = "центр";
                    break;
                case SilentOrders.Core.ManeuverIntent.Breakthrough:
                    return "Центр";
                case SilentOrders.Core.ManeuverIntent.Straight:
                    return "Прямо";
                case SilentOrders.Core.ManeuverIntent.Front:
                    return "Фронтом";
                case SilentOrders.Core.ManeuverIntent.RightHold:
                    result = "обхват справа";
                    break;
                case SilentOrders.Core.ManeuverIntent.LeftHold:
                    result = "обхват слева";
                    break;
                case SilentOrders.Core.ManeuverIntent.Both:
                    result = "оба фланга";
                    break;
                case SilentOrders.Core.ManeuverIntent.Right:
                    result = "правый фланг";
                    break;
                case SilentOrders.Core.ManeuverIntent.Left:
                    result = "левый фланг";
                    break;
            }

            return (string)result;
        }

        public static void Request(SilentOrders.Core.BattleSimulation b, int style, int intent)
        {
            TacticalOrders.Request(b, style, intent);
        }

        public static void Feedback(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units)
        {
            foreach (SilentOrders.Core.Soldier unit in units)
            {
                string text = (unit.Broken ? "Не могу вести бой" : ((unit.ReloadUntil > b.Time) ? "Перезаряжаюсь" : ((unit.Suppression > 0.7f) ? "Прижат" : ((unit.Ammo < 3) ? "Нет патронов" : ((b.Time < unit.RejectedUntil) ? "Маршрут недоступен" : (unit.Moving ? "Перемещаюсь" : (SilentOrders.Core.SquadBrain.Ready(b, unit, q) ? "Прикрытие готово" : ((unit.Visible.Count == 0 && q.Contacts.Count > 0) ? "Не вижу цель" : "На позиции"))))))));
                if (text != unit.Readiness && b.Time >= unit.NextStatus)
                {
                    unit.Readiness = text;
                    unit.NextStatus = b.Time + 5f;
                    b.Say(unit, text);
                }
            }
        }

        public static bool SmokeSafe(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            if (b.Time < b.Team(s).SmokeWaitUntil)
            {
                return false;
            }

            bool result = false;
            SilentOrders.Core.V2 v = ((s.PathCursor < s.Path.Count) ? s.Path[s.PathCursor] : s.OrderAnchor);
            foreach (SilentOrders.Core.Contact value in Knowledge.Contacts(b, s))
            {
                if (!(b.Time - value.SeenAt > 8f))
                {
                    result = true;
                    if (b.Map.LineOfSight(value.Position, s.Position) && b.SmokeTransmission(value.Position, s.Position) > 0.25f)
                    {
                        return false;
                    }

                    if (b.Map.LineOfSight(value.Position, v) && b.SmokeTransmissionAt(value.Position, v, b.Time + SilentOrders.Core.V2.Distance(s.Position, v) / System.Math.Max(0.1f, b.Config.MoveSpeed) + 1f) > 0.25f)
                    {
                        return false;
                    }
                }
            }

            return result;
        }

        private static SilentOrders.Core.V2 Center(System.Collections.Generic.List<SilentOrders.Core.Soldier> units)
        {
            SilentOrders.Core.V2 a = default(SilentOrders.Core.V2);
            foreach (SilentOrders.Core.Soldier unit in units)
            {
                a += unit.Position;
            }

            return a / System.Math.Max(1, units.Count);
        }

        private static void Status(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, string text)
        {
            text = SilentOrders.Core.Dialogue.Adapt(b, b.Soldiers[q.LeaderId], text);
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

        public static void Plan(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units, SilentOrders.Core.V2 threat)
        {
            TacticalOrders.ApplyPending(b, q);
            if (CommanderOrders.Waiting(b, q))
                return;
            if (!q.PriorityInitialized)
            {
                q.PriorityInitialized = true;
                q.Phase = SilentOrders.Core.ManeuverPhase.Hold;
                q.LastProgress = b.Time;
                q.ProgressCenter = Center(units);
            }

            if (BreakthroughTactics.Plan(b, q, units))
                return;
            SquadCoordination.ReleaseBounds(b, q);
            SilentOrders.Core.V2 v = Center(units);
            int num = 0;
            bool flag = false;
            foreach (SilentOrders.Core.Soldier soldier6 in b.Soldiers)
            {
                if (q.Contains(soldier6) && soldier6.Alive)
                {
                    if (soldier6.Broken || soldier6.Incapacitated || soldier6.Health < 30f)
                    {
                        num++;
                    }

                    if (soldier6.Busy)
                    {
                        flag = true;
                    }
                }
            }

            if (SilentOrders.Core.V2.Distance(v, q.ProgressCenter) > 2f)
            {
                q.ProgressCenter = v;
                q.LastProgress = b.Time;
                q.CombatStall = 0f;
            }
            else
            {
                q.CombatStall = b.Time - q.LastProgress;
            }

            SilentOrders.Core.CombatCoordination.Assess(b, q);
            q.DelayReason = "";
            if (!flag && TryQuietAdvance(b, q, units))
            {
                return;
            }

            if (q.Style == SilentOrders.Core.CombatStyle.Aggressive && (q.Phase == SilentOrders.Core.ManeuverPhase.Move || q.Phase == SilentOrders.Core.ManeuverPhase.Prepare))
            {
                SilentOrders.Core.SmokePlanner.Plan(b, q, q.Stage);
            }

            if (SilentOrders.Core.FireSupportBrain.Update(b, q))
            {
                q.PhaseSince += b.Config.SquadPeriod;
                Status(b, q, q.SupportStatus + "; обходящая группа удерживает прикрытие");
                return;
            }

            int num2 = 0;
            foreach (SilentOrders.Core.Soldier unit2 in units)
            {
                if (!unit2.Broken && unit2.Suppression < 0.75f && unit2.Ammo + unit2.Reserve > 0 && unit2.Health >= 30f)
                {
                    num2++;
                }
            }

            if (flag && (q.Phase == SilentOrders.Core.ManeuverPhase.Move || q.Phase == SilentOrders.Core.ManeuverPhase.Prepare))
            {
                if (q.AidPauseSince < 0f)
                {
                    q.AidPauseSince = b.Time;
                }

                if (b.Time - q.AidPauseSince >= 18f)
                {
                    SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: false);
                }

                if (b.Time - q.AidPauseSince < 18f)
                {
                    SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: true);
                    q.PhaseSince += b.Config.SquadPeriod;
                    Status(b, q, "Манёвр сохранён. Удерживаем позиции на время помощи своим");
                    return;
                }
            }
            else if (q.AidPauseSince >= 0f)
            {
                q.AidPauseSince = -1f;
                SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: false);
            }

            if (q.Phase == SilentOrders.Core.ManeuverPhase.Prepare)
            {
                q.ReadyCount = SilentOrders.Core.SquadBrain.CountReady(b, q);
                q.RequiredSupport = ((units.Count > 1) ? 1 : 0);
                b.Stats.CoverChecks++;
                bool flag2 = true;
                bool flag3 = false;
                foreach (int maneuverId in q.ManeuverIds)
                {
                    SilentOrders.Core.Soldier soldier = b.Soldiers[maneuverId];
                    if (soldier.Available)
                    {
                        flag3 = true;
                        if (!SmokeSafe(b, soldier) && !SilentOrders.Core.TacticalAI.SafeTravel(b, soldier))
                        {
                            flag2 = false;
                        }
                    }
                }

                bool flag4 = SquadCoordination.AnyRouteCovered(b, q) || (flag2 && flag3);
                if (b.Time < q.SmokeWaitUntil || q.PlannedSmoke.Count > 0)
                {
                    flag4 = false;
                    Status(b, q, "Жду раскрытия дыма; позиции удерживать");
                }
                else if (flag4)
                {
                    if (q.ReadySince < 0f)
                    {
                        q.ReadySince = b.Time;
                    }

                    if (b.Time - q.ReadySince >= ((q.Style == SilentOrders.Core.CombatStyle.Aggressive || q.OpeningUntil > b.Time) ? 0.6f : 1.2f))
                    {
                        q.Phase = SilentOrders.Core.ManeuverPhase.Move;
                        q.PhaseSince = b.Time;
                        foreach (int maneuverId2 in q.ManeuverIds)
                        {
                            SilentOrders.Core.Soldier soldier2 = b.Soldiers[maneuverId2];
                            soldier2.MoveAuthorized = maneuverId2 == q.ManeuverIds[0] && SquadBrain.MovementCovered(b, soldier2);
                            soldier2.NextDecision = 0f;
                        }

                        b.Stats.Bounds++;
                        Status(b, q, flag2 ? "Маршрут безопасен или закрыт завесой. Выполняем смену позиции" : "Прикрытие подтвердило готовность. Маневр разрешён");
                    }
                }
                else
                {
                    q.ReadySince = -1f;
                    b.Stats.SupportWaits++;
                    Status(b, q, "Замысел принят, жду прикрытие: " + q.ReadyCount + "/" + q.RequiredSupport);
                }

                if (!flag4 && b.Time - q.PhaseSince > 7f)
                {
                    SilentOrders.Core.SupportAI.TrySmoke(b, q, v);
                }

                if (b.Time - q.PhaseSince > 22f)
                {
                    Abort(b, q, units, threat, "Прикрытие не готово. Пересматриваю позиции");
                }
            }
            else if (q.Phase == SilentOrders.Core.ManeuverPhase.Move)
            {
                bool flag5 = true;
                int num3 = 0;
                foreach (int maneuverId3 in q.ManeuverIds)
                {
                    SilentOrders.Core.Soldier soldier3 = b.Soldiers[maneuverId3];
                    if (soldier3.Available && !soldier3.Broken)
                    {
                        num3++;
                        if (SilentOrders.Core.V2.Distance(soldier3.Position, soldier3.OrderAnchor) > 1.1f || (!PositionLogic.Protected(b, soldier3, soldier3.Position) && V2.Distance(soldier3.Position, b.Map.Objective) > 1.9f))
                        {
                            flag5 = false;
                        }
                    }
                }

                Status(b, q, q.PriorityChanged ? "Новый приоритет принят; завершаю текущую перебежку" : ((q.Style == SilentOrders.Core.CombatStyle.Withdraw) ? "Отходим к соседним укрытиям; поддержка остаётся" : ("Выполняем замысел: " + IntentName(q.Intent))));
                if (q.EnvelopmentActive && !q.PriorityChanged)
                {
                    bool flag6 = false;
                    foreach (int supportId in q.SupportIds)
                    {
                        SilentOrders.Core.Soldier soldier4 = b.Soldiers[supportId];
                        if (soldier4.Available && !soldier4.Broken && soldier4.Ammo + soldier4.Reserve > 0)
                        {
                            flag6 = true;
                        }
                    }

                    if (num3 == 0 || !flag6 || num >= 2 || b.Time - q.PhaseSince > 24f || b.Time - q.EnvelopmentSince > 75f)
                    {
                        Abort(b, q, units, threat, "Обхват остановлен обороной или потерей поддержки. Перегруппировка");
                        return;
                    }

                    if (flag5 && !FlankReached(b, q))
                    {
                        ContinueEnvelopment(b, q, threat);
                        return;
                    }

                    if (flag5)
                    {
                        q.EnvelopmentActive = false;
                        q.SupportIds.Clear();
                        Status(b, q, "Обходящая группа заняла фланг. Поддержка может подтянуться");
                    }
                }

                if (flag5 || num3 == 0)
                {
                    q.Phase = SilentOrders.Core.ManeuverPhase.Regroup;
                    q.PhaseSince = b.Time;
                    q.RegroupAssigned = false;
                }
                else if (b.Time - q.PhaseSince > 30f)
                {
                    Abort(b, q, units, threat, "Продвижение остановилось. Сохраняем людей, меняю план");
                }
            }
            else if (q.Phase == SilentOrders.Core.ManeuverPhase.Regroup)
            {
                if (!q.RegroupAssigned)
                {
                    q.RegroupAssigned = true;
                    q.Revision++;
                    foreach (int maneuverId4 in q.ManeuverIds)
                    {
                        SilentOrders.Core.Soldier soldier5 = b.Soldiers[maneuverId4];
                        if (soldier5.Available)
                        {
                            SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier5, SilentOrders.Core.Job.Cover, soldier5.Position, authorized: true);
                        }
                    }

                    foreach (SilentOrders.Core.Soldier unit3 in units)
                    {
                        if (!q.ManeuverIds.Contains(unit3.Id))
                        {
                            float num4 = (unit3.Sniper ? (-3.5f) : ((unit3.Role == SilentOrders.Core.Role.Medic) ? (-2f) : ((unit3.Id == q.LeaderId) ? (-1.5f) : (-1f))));
                            if (q.Style == SilentOrders.Core.CombatStyle.Withdraw)
                            {
                                num4 = 0f - System.Math.Abs(num4);
                            }

                            SilentOrders.Core.V2 v2 = q.Stage + new SilentOrders.Core.V2(num4, (unit3.Role != SilentOrders.Core.Role.MachineGunner) ? 1 : (-1));
                            SilentOrders.Core.SquadBrain.GiveOrder(anchor: SilentOrders.Core.SquadBrain.FindPost(b, unit3, new SilentOrders.Core.V2(SilentOrders.Core.Util.Clamp(v2.X, 2.5f, (float)b.Map.Columns - 2.5f), SilentOrders.Core.Util.Clamp(v2.Y, 3.5f, (float)b.Map.Rows - 4.5f)), threat, firing: true), b: b, q: q, s: unit3, job: (unit3.Id != q.LeaderId) ? SilentOrders.Core.Job.Cover : SilentOrders.Core.Job.Command, authorized: true);
                        }
                    }
                }

                bool flag7 = true;
                foreach (SilentOrders.Core.Soldier unit4 in units)
                {
                    if (unit4.Moving || SilentOrders.Core.V2.Distance(unit4.Position, unit4.OrderAnchor) > 1.2f || (!PositionLogic.Protected(b, unit4, unit4.Position) && V2.Distance(unit4.Position, b.Map.Objective) > 1.9f))
                    {
                        flag7 = false;
                    }
                }

                Status(b, q, flag ? "Удерживаем позиции, ждём помощь своим" : "Новые позиции заняты; подтягиваем поддержку и медика");
                if (!flag && ((flag7 && b.Time - q.PhaseSince > ((q.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 1.2f : 3f)) || b.Time - q.PhaseSince > 22f))
                {
                    q.Phase = SilentOrders.Core.ManeuverPhase.Hold;
                    q.NextPriorityPlan = b.Time + ((q.Style == SilentOrders.Core.CombatStyle.Passive) ? 8f : ((q.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 0.5f : 2f));
                    q.ManeuverIds.Clear();
                }
            }
            else
            {
                if (b.Time < q.NextPriorityPlan)
                {
                    return;
                }

                if (flag)
                {
                    Status(b, q, "Приоритет сохранён. Сначала помощь своим и пополнение запасов");
                    SilentOrders.Core.SupportAI.TrySmoke(b, q, v);
                    q.NextPriorityPlan = b.Time + 3f;
                    return;
                }

                q.EmergencyRetreat = num >= 2 || (q.Contacts.Count > 0 && num2 < System.Math.Min(2, units.Count));
                if (q.EmergencyRetreat && v.X < 5f)
                {
                    Status(b, q, "Группа истощена. Удерживаем безопасные тыловые позиции");
                    return;
                }

                bool flag8 = false;
                foreach (SilentOrders.Core.Soldier unit5 in units)
                {
                    if (SilentOrders.Core.V2.Distance(unit5.Position, b.Map.Objective) < 2.1f)
                    {
                        flag8 = true;
                    }
                }

                if (flag8 && q.Style != 0)
                {
                    Status(b, q, "Удерживаем точку; не покидаем зону ради маневра");
                }
                else if (q.Style == SilentOrders.Core.CombatStyle.Withdraw && v.X < 5f)
                {
                    Status(b, q, "Отход завершён. Удерживаем тыловые укрытия");
                }
                else if (!q.EmergencyRetreat && q.Style == SilentOrders.Core.CombatStyle.Passive && q.Contacts.Count > 0 && q.CombatStall < 40f)
                {
                    Status(b, q, "Закрепление: удерживаем занятые позиции и секторы");
                }
                else
                {
                    Start(b, q, units, threat, v);
                }
            }
        }

        private static bool TryQuietAdvance(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units)
        {
            if (q.Style == SilentOrders.Core.CombatStyle.Withdraw || q.CombatStall < 30f)
            {
                return false;
            }

            foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
            {
                if (b.Time - value.SeenAt < 10f && value.Confidence > 0.25f)
                {
                    return false;
                }
            }

            foreach (SilentOrders.Core.Soldier unit in units)
            {
                if (unit.Moving || unit.Busy || unit.Broken || unit.Suppression > 0.25f || b.Time - unit.LastIncoming < 10f || unit.Visible.Count > 0)
                {
                    return false;
                }

                if (SilentOrders.Core.V2.Distance(unit.Position, b.Map.Objective) < 2.1f)
                {
                    return false;
                }

                foreach (SilentOrders.Core.Contact value2 in unit.Contacts.Values)
                {
                    if (b.Time - value2.SeenAt < 10f && value2.Confidence > 0.25f)
                    {
                        return false;
                    }
                }
            }

            System.Collections.Generic.List<SilentOrders.Core.Soldier> list = new System.Collections.Generic.List<SilentOrders.Core.Soldier>(units);
            list.Sort((SilentOrders.Core.Soldier a, SilentOrders.Core.Soldier z) => SilentOrders.Core.V2.Distance(a.Position, b.Map.Objective).CompareTo(SilentOrders.Core.V2.Distance(z.Position, b.Map.Objective)));
            foreach (SilentOrders.Core.Soldier item in list)
            {
                System.Collections.Generic.List<SilentOrders.Core.V2> list2 = SilentOrders.Core.Pathfinder.Find(b.Map, item.Position, b.Map.NearestOpen(b.Map.Objective), (SilentOrders.Core.V2 p) => b.Map.NearCover(p) ? 0f : 0.3f);
                float num = 0f;
                SilentOrders.Core.V2 a2 = item.Position;
                SilentOrders.Core.V2 v = item.Position;
                float num2 = -10000f;
                foreach (SilentOrders.Core.V2 item2 in list2)
                {
                    num += SilentOrders.Core.V2.Distance(a2, item2);
                    a2 = item2;
                    if (num > 7f)
                    {
                        break;
                    }

                    if (num < 2f)
                    {
                        continue;
                    }

                    bool flag = false;
                    foreach (SilentOrders.Core.Soldier unit2 in units)
                    {
                        if (unit2.Id != item.Id && (SilentOrders.Core.V2.Distance(item2, unit2.Position) < 1.7f || SilentOrders.Core.V2.Distance(item2, unit2.OrderAnchor) < 1.7f))
                        {
                            flag = true;
                        }
                    }

                    if (!flag)
                    {
                        float num3 = num + (float)(b.Map.NearCover(item2) ? 2 : 0);
                        if (num3 > num2)
                        {
                            v = item2;
                            num2 = num3;
                        }
                    }
                }

                if (num2 == -10000f)
                {
                    continue;
                }

                SilentOrders.Core.FireSupportBrain.Reset(b, q);
                q.EnvelopmentActive = false;
                q.SupportIds.Clear();
                q.PlannedSmoke.Clear();
                q.ManeuverIds.Clear();
                q.ManeuverIds.Add(item.Id);
                q.Stage = v;
                q.Revision++;
                q.Phase = SilentOrders.Core.ManeuverPhase.Prepare;
                q.PhaseSince = b.Time;
                q.ReadySince = -1f;
                q.LastProgress = b.Time;
                q.ProgressCenter = Center(units);
                q.Replans++;
                b.Stats.Stalemates++;
                SilentOrders.Core.SquadBrain.GiveOrder(b, q, item, SilentOrders.Core.Job.Reposition, v, authorized: false);
                Status(b, q, "Контакта нет. Проверяем путь к точке короткой перебежкой; остальные прикрывают");
                return true;
            }

            return false;
        }

        private static bool FlankReached(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            if (q.EnvelopmentBounds < 2)
            {
                return false;
            }

            foreach (int maneuverId in q.ManeuverIds)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[maneuverId];
                if (!soldier.Available || (((q.Intent == SilentOrders.Core.ManeuverIntent.LeftHold) ? (soldier.Position.Y < q.EnvelopmentThreat.Y - 4f) : (soldier.Position.Y > q.EnvelopmentThreat.Y + 4f)) && !(soldier.Position.X < q.EnvelopmentThreat.X - 6f)))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static void ContinueEnvelopment(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.V2 threat)
        {
            q.EnvelopmentBounds++;
            q.Revision++;
            q.ReadySince = -1f;
            q.Phase = SilentOrders.Core.ManeuverPhase.Prepare;
            q.PhaseSince = b.Time;
            float b2 = ((q.Intent == SilentOrders.Core.ManeuverIntent.LeftHold) ? ((float)b.Map.Rows * 0.23f) : ((float)b.Map.Rows * 0.8f));
            SilentOrders.Core.V2 v = default(SilentOrders.Core.V2);
            int num = 0;
            foreach (int maneuverId in q.ManeuverIds)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[maneuverId];
                if (soldier.Available)
                {
                    v += soldier.Position;
                    num++;
                }
            }

            v /= (float)System.Math.Max(1, num);
            q.Stage = new SilentOrders.Core.V2(System.Math.Min(b.Map.Objective.X, v.X + 4f), SilentOrders.Core.Util.Lerp(v.Y, b2, 0.6f));
            int num2 = 0;
            foreach (int maneuverId2 in q.ManeuverIds)
            {
                SilentOrders.Core.Soldier soldier2 = b.Soldiers[maneuverId2];
                if (soldier2.Available)
                {
                    SilentOrders.Core.V2 wanted = q.Stage + new SilentOrders.Core.V2(0f, (num2++ != 0) ? 1 : (-1));
                    SilentOrders.Core.V2 anchor = SilentOrders.Core.SquadBrain.FindPost(b, soldier2, wanted, threat, firing: false);
                    SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier2, SilentOrders.Core.Job.Reposition, anchor, authorized: false);
                }
            }

            SilentOrders.Core.SmokePlanner.Plan(b, q, q.Stage);
            Status(b, q, "Поддержка остаётся и подавляет. Обходящая группа продолжает обхват");
        }

        public static void Abort(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units, SilentOrders.Core.V2 threat, string reason)
        {
            foreach (SilentOrders.Core.Soldier unit in units)
            {
                unit.RejectedPost = unit.OrderAnchor;
                unit.RejectedUntil = b.Time + 12f;
                SilentOrders.Core.SquadBrain.GiveOrder(b, q, unit, (unit.Id != q.LeaderId) ? SilentOrders.Core.Job.Cover : SilentOrders.Core.Job.Command, SilentOrders.Core.SquadBrain.FindPost(b, unit, unit.Position, threat, firing: true), authorized: true);
            }

            SilentOrders.Core.FireSupportBrain.Reset(b, q);
            q.PlannedSmoke.Clear();
            q.EnvelopmentActive = false;
            q.SupportIds.Clear();
            q.Phase = SilentOrders.Core.ManeuverPhase.Hold;
            q.ManeuverIds.Clear();
            q.NextPriorityPlan = b.Time + 4f;
            q.Replans++;
            b.Stats.PlanAborts++;
            b.Stats.Stalemates++;
            Status(b, q, reason);
        }

        private static void Start(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units, SilentOrders.Core.V2 threat, SilentOrders.Core.V2 center)
        {
            SilentOrders.Core.FireSupportBrain.Reset(b, q);
            bool flag = q.Contacts.Count > 0 && (q.Intent == SilentOrders.Core.ManeuverIntent.LeftHold || q.Intent == SilentOrders.Core.ManeuverIntent.RightHold) && q.Style != 0 && !q.EmergencyRetreat;
            q.SupportIds.Clear();
            q.EnvelopmentActive = false;
            if (flag)
            {
                foreach (SilentOrders.Core.Soldier unit in units)
                {
                    if ((unit.Role == Role.MachineGunner || unit.Sniper || unit.SupplyPack) && !unit.Busy && !unit.Broken && unit.Ammo + unit.Reserve > 0 && q.SupportIds.Count < 2)
                    {
                        q.SupportIds.Add(unit.Id);
                    }
                }
            }

            if (flag && q.SupportIds.Count == 0)
                foreach (var u in units)
                    if (u.Role == Role.Rifleman && !u.Broken && u.Ammo > 0)
                    {
                        q.SupportIds.Add(u.Id);
                        break;
                    }

            flag = flag && q.SupportIds.Count > 0 && units.Count > q.SupportIds.Count;
            System.Collections.Generic.List<SilentOrders.Core.Soldier> list = new System.Collections.Generic.List<SilentOrders.Core.Soldier>();
            foreach (SilentOrders.Core.Soldier unit2 in units)
            {
                if (!unit2.Busy && !unit2.Broken && (unit2.Role != Role.Medic || (MedicTactics.FreeToFight(b, unit2) && q.Style != CombatStyle.Aggressive) || q.Style == CombatStyle.Withdraw || q.EmergencyRetreat) && (unit2.Health >= 35f || q.Style == SilentOrders.Core.CombatStyle.Withdraw || q.EmergencyRetreat) && unit2.Suppression < ((q.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 0.78f : 0.7f) && (flag ? (!q.SupportIds.Contains(unit2.Id)) : (unit2.Id != q.LeaderId)))
                {
                    list.Add(unit2);
                }
            }

            list.Sort((SilentOrders.Core.Soldier a, SilentOrders.Core.Soldier z) => SquadCoordination.MovePriority(b, q, a).CompareTo(SquadCoordination.MovePriority(b, q, z)));
            if (list.Count == 0 && units.Count == 1)
            {
                list.Add(units[0]);
            }

            if (list.Count == 0)
            {
                Status(b, q, "Нет готовых к перемещению. Восстанавливаем группу");
                return;
            }

            float num = ((q.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 7f : ((q.Style == SilentOrders.Core.CombatStyle.Passive) ? 3f : 4.5f));
            num += ((q.Character == SilentOrders.Core.CommanderCharacter.Persistent) ? 0.5f : ((q.Character == SilentOrders.Core.CommanderCharacter.Cautious) ? (-0.5f) : 0f));
            V2 axis = TacticalOrders.Axis(b, q.Side), lateral = new V2(-axis.Y, axis.X), origin = q.Side == Side.Green ? b.Map.Entry : b.Map.Objective;
            V2 objective = q.Side == Side.Green ? b.Map.Objective : b.Map.Entry;
            float objectiveProgress = V2.Dot(objective - origin, axis), centerProgress = V2.Dot(center - origin, axis), centerLateral = V2.Dot(center - origin, lateral);
            float num2 = centerProgress;
            foreach (var u in units)
                if (u.Role != Role.Medic)
                    num2 = Math.Max(num2, V2.Dot(u.Position - origin, axis));
            bool flag2 = q.Style == CombatStyle.Withdraw || q.EmergencyRetreat;
            float num3 = flag2 ? centerProgress - 4 : num2 + num;
            float wing = q.Intent == ManeuverIntent.Left || q.Intent == ManeuverIntent.LeftHold ? -1 : q.Intent == ManeuverIntent.Right || q.Intent == ManeuverIntent.RightHold ? 1 : 0;
            if (q.Intent == ManeuverIntent.Both && !SquadCoordination.CanSplit(b, units))
                wing = V2.Dot(center - threat, lateral) <= 0 ? -1 : 1;
            float width = Math.Abs(lateral.X) * b.Map.Columns + Math.Abs(lateral.Y) * b.Map.Rows;
            float desiredLateral = wing * width * .22f;
            if (q.CombatStall > 25 && q.Contacts.Count > 0 && !flag2)
            {
                q.Replans++;
                desiredLateral += q.Replans % 2 == 0 ? 3 : -3;
                b.Stats.Stalemates++;
            }

            if (num3 > objectiveProgress - 6)
                desiredLateral = Util.Lerp(desiredLateral, 0, .7f);
            q.Stage = TacticalOrders.Open(b, origin + axis * Util.Clamp(num3, 0, objectiveProgress) + lateral * Util.Lerp(centerLateral, desiredLateral, .5f));
            q.Revision++;
            q.ManeuverIds.Clear();
            q.RequiredSupport = ((units.Count > 1) ? 1 : 0);
            q.ReadySince = -1f;
            q.PriorityChanged = false;
            int num5 = System.Math.Min(2, System.Math.Min(list.Count, System.Math.Max(1, units.Count - 1)));
            foreach (SilentOrders.Core.Soldier unit4 in units)
            {
                SilentOrders.Core.SquadBrain.GiveOrder(b, q, unit4, (unit4.Id != q.LeaderId) ? SilentOrders.Core.Job.Cover : SilentOrders.Core.Job.Command, SilentOrders.Core.SquadBrain.FindPost(b, unit4, unit4.Position, threat, firing: true), authorized: true);
            }

            for (int i = 0; i < num5; i++)
            {
                SilentOrders.Core.Soldier soldier = list[i];
                SilentOrders.Core.V2 wanted = q.Stage + lateral * ((i == 0) ? -1.5f : 1.5f);
                if (q.Intent == SilentOrders.Core.ManeuverIntent.Both && !flag2 && num3 < objectiveProgress - 5f && SquadCoordination.CanSplit(b, units))
                {
                    wanted = origin + axis * num3 + lateral * Util.Lerp(centerLateral, (i == 0 ? -1 : 1) * width * .22f, .55f);
                }

                if (flag && !flag2)
                {
                    wanted -= axis * Math.Max(0, V2.Dot(wanted - origin, axis) - (num2 + 3));
                    b.Stats.Flanks++;
                }

                SilentOrders.Core.V2 v = SilentOrders.Core.SquadBrain.FindPost(b, soldier, wanted, threat, false, num3 >= objectiveProgress - 2f && !flag2);
                if (!(SilentOrders.Core.V2.Distance(v, soldier.Position) < 0.8f))
                {
                    if (!SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier, SilentOrders.Core.Job.Reposition, v, authorized: false))
                        continue;
                    q.ManeuverIds.Add(soldier.Id);
                    soldier.Order = (flag2 ? "Приоритет отхода: ближайшая тыловая позиция" : ((q.Intent == SilentOrders.Core.ManeuverIntent.Both) ? "Своя сторона разделённой группы; дождаться прикрытия" : ("Приоритет " + IntentName(q.Intent) + "; движение после подтверждения")));
                }
            }

            if (q.ManeuverIds.Count == 0)
            {
                q.NextPriorityPlan = b.Time + 5f;
                q.Replans++;
                Status(b, q, "Подходящих новых укрытий нет; проверяю другой маршрут");
                return;
            }

            if (flag)
            {
                q.EnvelopmentActive = true;
                q.EnvelopmentSince = b.Time;
                q.EnvelopmentBounds = 1;
                q.EnvelopmentThreat = threat;
                foreach (int supportId in q.SupportIds)
                {
                    b.Soldiers[supportId].Order = "Подавлять угрозы обходящей группе; при потере сектора сменить рубеж двойкой";
                }
            }

            if (q.Intent == SilentOrders.Core.ManeuverIntent.Both)
            {
                b.Stats.SplitManeuvers++;
            }

            if (flag2)
            {
                b.Stats.Retreats++;
            }

            q.Phase = SilentOrders.Core.ManeuverPhase.Prepare;
            q.PhaseSince = b.Time;
            bool flag3 = false;
            foreach (SilentOrders.Core.Soldier unit5 in units)
            {
                if (unit5.Suppression > 0.45f || b.Time - unit5.LastIncoming < 3f)
                {
                    flag3 = true;
                }
            }

            SilentOrders.Core.SmokePlanner.Plan(b, q, q.Stage);
            if (q.PlannedSmoke.Count == 0 && flag3 && (flag2 || q.CombatStall > 18f))
            {
                SilentOrders.Core.SupportAI.TrySmoke(b, q, center);
            }

            Status(b, q, q.EmergencyRetreat ? "Приоритет сохранён, но группа истощена. Локальный отход под прикрытием" : ("Распределяю позиции: " + IntentName(q.Intent) + "; поддержка подтверждает готовность"));
        }
    }
}
