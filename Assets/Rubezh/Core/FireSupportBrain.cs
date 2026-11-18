using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class FireSupportBrain
    {
        public static SilentOrders.Core.Contact Threat(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            SilentOrders.Core.Contact result = null;
            float num = -10000f;
            foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
            {
                if (b.Time - value.SeenAt > 9f || !value.Visual)
                {
                    continue;
                }

                float num2 = 100f;
                foreach (int maneuverId in q.ManeuverIds)
                {
                    if (b.Soldiers[maneuverId].Alive)
                    {
                        num2 = System.Math.Min(num2, SilentOrders.Core.V2.Distance(b.Soldiers[maneuverId].Position, value.Position));
                    }
                }

                float num3 = value.Confidence * 5f - num2 * 0.3f - (b.Time - value.SeenAt) * 0.6f + (float)((b.Time - value.FireSeenAt < 3f) ? 3 : 0);
                if (num3 > num)
                {
                    num = num3;
                    result = value;
                }
            }

            return result;
        }

        public static bool FindRidge(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.Soldier s, SilentOrders.Core.Contact threat, out SilentOrders.Core.V2 result)
        {
            result = s.Position;
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>> list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>>();
            SilentOrders.Core.V2 b2 = default(SilentOrders.Core.V2);
            int num = 0;
            foreach (int maneuverId in q.ManeuverIds)
            {
                if (b.Soldiers[maneuverId].Alive)
                {
                    b2 += b.Soldiers[maneuverId].Position;
                    num++;
                }
            }

            if (num == 0)
            {
                foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                {
                    if (soldier.Side == s.Side && soldier.Available)
                    {
                        b2 += soldier.Position;
                        num++;
                    }
                }
            }

            b2 /= (float)System.Math.Max(1, num);
            float num2 = ((q.Style == SilentOrders.Core.CombatStyle.Aggressive) ? 22 : ((q.Style == SilentOrders.Core.CombatStyle.Passive) ? 12 : 17));
            for (int i = System.Math.Max(1, (int)s.Position.Y - 9); (float)i < System.Math.Min(b.Map.Rows - 1, s.Position.Y + 9f); i++)
            {
                for (int j = System.Math.Max(1, (int)s.Position.X - 9); (float)j < System.Math.Min(b.Map.Columns - 1, s.Position.X + 9f); j++)
                {
                    SilentOrders.Core.V2 v2 = new SilentOrders.Core.V2((float)j + 0.5f, (float)i + 0.5f);
                    float num3 = SilentOrders.Core.V2.Distance(s.Position, v2);
                    float num4 = SilentOrders.Core.V2.Distance(v2, threat.Position);
                    if (num3 < 1.6f || num3 > 9f || !b.Map.Walkable(v2) || !b.Map.NearCover(v2) || num4 > b.WeaponRange(s) - 0.5f || num4 < 4f || SilentOrders.Core.V2.Distance(v2, b2) > num2 || !b.Map.LineOfSight(v2, threat.Position) || b.SmokeTransmission(v2, threat.Position) < 0.3f)
                    {
                        continue;
                    }

                    bool flag = false;
                    foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
                    {
                        if (soldier2.Id != s.Id && soldier2.Alive && (SilentOrders.Core.V2.Distance(v2, soldier2.Position) < 1.1f || SilentOrders.Core.V2.Distance(v2, soldier2.OrderAnchor) < 1.1f))
                        {
                            flag = true;
                        }
                    }

                    if (!flag && (!(b.Time < s.SupportFailedUntil) || !(SilentOrders.Core.V2.Distance(v2, s.SupportFailedPost) < 2f)))
                    {
                        float key = CombatTraining.Cover(b, s, v2, threat.Position) * 6f - num3 * 0.7f - SilentOrders.Core.TacticalAI.Risk(b, s, v2) * 2f - System.Math.Abs(num4 - b.WeaponRange(s) * 0.7f) * 0.2f;
                        list.Add(new System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>(key, v2));
                    }
                }
            }

            list.Sort((System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> a, System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> z) => z.Key.CompareTo(a.Key));
            for (int k = 0; k < System.Math.Min(6, list.Count); k++)
            {
                SilentOrders.Core.V2 value = list[k].Value;
                System.Collections.Generic.List<SilentOrders.Core.V2> list2 = SilentOrders.Core.LossMemory.Find(b, s, value, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, s, v) * 3f);
                if (list2.Count > 0 && SilentOrders.Core.SupportAI.PathLength(s.Position, list2) <= 14f)
                {
                    result = value;
                    return true;
                }
            }

            float num5 = SilentOrders.Core.V2.Distance(s.Position, threat.Position);
            float num6 = -10000f;
            for (int l = System.Math.Max(1, (int)s.Position.Y - 8); (float)l < System.Math.Min(b.Map.Rows - 1, s.Position.Y + 9f); l++)
            {
                for (int m = System.Math.Max(1, (int)s.Position.X - 8); (float)m < System.Math.Min(b.Map.Columns - 1, s.Position.X + 9f); m++)
                {
                    SilentOrders.Core.V2 v3 = new SilentOrders.Core.V2((float)m + 0.5f, (float)l + 0.5f);
                    float num7 = SilentOrders.Core.V2.Distance(s.Position, v3);
                    float num8 = num5 - SilentOrders.Core.V2.Distance(v3, threat.Position);
                    if (num7 < 2f || num7 > 8f || num8 < 3f || !b.Map.Walkable(v3) || !b.Map.NearCover(v3) || SilentOrders.Core.TacticalAI.Risk(b, s, v3) > 0.25f)
                    {
                        continue;
                    }

                    bool flag2 = false;
                    foreach (SilentOrders.Core.Soldier soldier3 in b.Soldiers)
                    {
                        if (soldier3.Id != s.Id && soldier3.Alive && (SilentOrders.Core.V2.Distance(v3, soldier3.Position) < 1.4f || SilentOrders.Core.V2.Distance(v3, soldier3.OrderAnchor) < 1.4f))
                        {
                            flag2 = true;
                        }
                    }

                    if (flag2)
                    {
                        continue;
                    }

                    float num9 = num8 - num7 * 0.25f;
                    if (num9 <= num6)
                    {
                        continue;
                    }

                    System.Collections.Generic.List<SilentOrders.Core.V2> list3 = SilentOrders.Core.LossMemory.Find(b, s, v3, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, s, v) * 3f);
                    if (list3.Count == 0 || SilentOrders.Core.SupportAI.PathLength(s.Position, list3) > 12f)
                    {
                        continue;
                    }

                    bool flag3 = true;
                    foreach (SilentOrders.Core.V2 item in list3)
                    {
                        if (SilentOrders.Core.TacticalAI.Risk(b, s, item) > 0.25f)
                        {
                            flag3 = false;
                        }
                    }

                    if (flag3)
                    {
                        result = v3;
                        num6 = num9;
                    }
                }
            }

            return num6 > -10000f;
        }

        public static void PressureSupport(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            if (q.SupportMovePhase != 0 || q.Style == SilentOrders.Core.CombatStyle.Withdraw)
            {
                return;
            }

            foreach (SilentOrders.Core.Soldier s in b.Soldiers)
            {
                if (!q.Contains(s) || s.Role != SilentOrders.Core.Role.MachineGunner || !s.Available || s.Broken || s.Moving || s.Health < 35f || s.Suppression > 0.65f || s.Ammo + s.Reserve < 10 || s.ReloadUntil > b.Time || b.Time < s.NextPressureMove || q.ManeuverIds.Contains(s.Id))
                {
                    continue;
                }

                bool flag = false;
                foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                {
                    if (soldier.Side == s.Side && soldier.Alive && (b.Time - soldier.LastFire < 6f || b.Time - soldier.LastIncoming < 6f))
                    {
                        flag = true;
                    }
                }

                if (!flag)
                {
                    continue;
                }

                SilentOrders.Core.Contact contact = null;
                foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
                {
                    if (value.Visual && value.Confidence > 0.35f && b.Time - value.SeenAt < 9f && (contact == null || value.FireSeenAt > contact.FireSeenAt))
                    {
                        contact = value;
                    }
                }

                if (contact == null || SilentOrders.Core.CombatCoordination.Effective(b, s, contact))
                {
                    continue;
                }

                s.NextPressureMove = b.Time + 3f;
                if (!FindRidge(b, q, s, contact, out var result))
                {
                    continue;
                }

                System.Collections.Generic.List<SilentOrders.Core.V2> list = SilentOrders.Core.LossMemory.Find(b, s, result, (SilentOrders.Core.V2 p) => SilentOrders.Core.TacticalAI.Risk(b, s, p) * 3f);
                bool flag2 = true;
                foreach (SilentOrders.Core.V2 item in list)
                {
                    if (SilentOrders.Core.TacticalAI.Risk(b, s, item) > 0.25f)
                    {
                        flag2 = false;
                    }
                }

                if (!flag2 && !SilentOrders.Core.DefenseBrain.Covering(b, s, result))
                {
                    continue;
                }

                if (!SilentOrders.Core.SquadBrain.GiveOrder(b, q, s, SilentOrders.Core.Job.Cover, result, authorized: true))
                    continue;
                s.PressureMoveUntil = b.Time + 10f;
                s.Order = "Сменить рубеж и подавить огневую точку, которая прижала наших";
                b.Stats.SupportRelocations++;
                b.Say(s, "Смещаюсь. Прикрою наших!", urgent: true);
                break;
            }
        }

        public static void Reset(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: false);
            q.SupportMoverId = (q.SupportFollowerId = -1);
            q.SupportMovePhase = 0;
            q.LostSectorSince = -1f;
        }

        public static bool Update(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            if (!q.EnvelopmentActive)
            {
                if (q.SupportMovePhase != 0)
                {
                    Reset(b, q);
                }

                return false;
            }

            SilentOrders.Core.Contact contact = Threat(b, q);
            bool flag = false;
            bool flag2 = false;
            int num = 0;
            foreach (int supportId in q.SupportIds)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[supportId];
                if (!soldier.Available || soldier.Broken || soldier.Ammo + soldier.Reserve <= 0)
                {
                    continue;
                }

                num++;
                if (contact != null && SilentOrders.Core.CombatCoordination.Effective(b, soldier, contact))
                {
                    flag = true;
                    if (b.Time - soldier.LastFire < 2f)
                    {
                        flag2 = true;
                    }
                }
            }

            q.SupportStatus = (flag2 ? "Подавляет" : (flag ? "Сектор доступен" : ((num == 0) ? "Нет боеспособной поддержки" : ((contact == null) ? "Контакт потерян" : "Ищет рубеж огня"))));
            if (q.SupportMovePhase != 0)
            {
                SilentOrders.Core.Soldier soldier2 = b.Soldiers[q.SupportMoverId];
                bool flag3 = !soldier2.Available || soldier2.Broken || b.Time - q.SupportMoveSince > 18f;
                bool flag4 = SilentOrders.Core.V2.Distance(soldier2.Position, soldier2.OrderAnchor) < 1.2f;
                if (flag3)
                {
                    soldier2.SupportFailedPost = soldier2.OrderAnchor;
                    soldier2.SupportFailedUntil = b.Time + 30f;
                    SilentOrders.Core.TacticalAI.Stop(soldier2);
                    soldier2.OrderAnchor = soldier2.Position;
                    q.NextSupportMove = b.Time + 6f;
                    Reset(b, q);
                    return false;
                }

                if (flag4)
                {
                    SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier2, SilentOrders.Core.Job.Cover, soldier2.Position, authorized: true);
                    if (q.SupportMovePhase == 1 && q.SupportFollowerId >= 0 && contact != null)
                    {
                        SilentOrders.Core.Soldier soldier3 = b.Soldiers[q.SupportFollowerId];
                        if (soldier3.Available && !soldier3.Broken && FindRidge(b, q, soldier3, contact, out var result))
                        {
                            if (!SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier3, SilentOrders.Core.Job.Cover, result, authorized: true))
                                return true;
                            q.SupportMoverId = soldier3.Id;
                            q.SupportMovePhase = 2;
                            q.SupportMoveSince = b.Time;
                            return true;
                        }
                    }

                    Reset(b, q);
                    q.NextSupportMove = b.Time + 5f;
                    q.SupportStatus = "Новый рубеж занят";
                    return false;
                }

                q.SupportStatus = "Смена рубежа по частям";
                q.DelayReason = "Обходящая группа прикрывает перемещение поддержки";
                SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: true);
                return true;
            }

            if (flag)
            {
                q.LostSectorSince = -1f;
                return false;
            }

            if (q.LostSectorSince < 0f)
            {
                q.LostSectorSince = b.Time;
            }

            if (contact == null)
            {
                if (b.Time - q.LostSectorSince > 12f)
                {
                    q.EnvelopmentActive = false;
                    q.Phase = SilentOrders.Core.ManeuverPhase.Regroup;
                    q.RegroupAssigned = false;
                    q.PhaseSince = b.Time;
                    q.SupportStatus = "Контакт устарел: сбор группы";
                }

                return false;
            }

            if (b.Time - q.LostSectorSince < 3f || b.Time < q.NextSupportMove)
            {
                return false;
            }

            SilentOrders.Core.Soldier soldier4 = null;
            SilentOrders.Core.Soldier soldier5 = null;
            foreach (int supportId2 in q.SupportIds)
            {
                SilentOrders.Core.Soldier soldier6 = b.Soldiers[supportId2];
                if (soldier6.Available && !soldier6.Broken && !(soldier6.Health < 35f) && !(soldier6.Suppression > 0.65f))
                {
                    if (soldier4 == null || soldier6.Role == SilentOrders.Core.Role.Rifleman)
                    {
                        soldier5 = soldier4;
                        soldier4 = soldier6;
                    }
                    else
                    {
                        soldier5 = soldier6;
                    }
                }
            }

            if (soldier4 == null)
            {
                return false;
            }

            if (!FindRidge(b, q, soldier4, contact, out var result2))
            {
                q.NextSupportMove = b.Time + 5f;
                q.DelayReason = "Нет доступного рубежа подавления";
                return false;
            }

            if (!SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier4, SilentOrders.Core.Job.Cover, result2, authorized: true))
                return false;
            q.SupportMoverId = soldier4.Id;
            q.SupportFollowerId = soldier5?.Id ?? (-1);
            q.SupportMovePhase = 1;
            q.SupportMoveSince = b.Time;
            q.SupportRally = result2;
            SilentOrders.Core.CombatCoordination.PauseMovers(b, q, pause: true);
            soldier4.Order = "Занять новый рубеж подавления; напарник и группа прикрывают";
            b.Stats.SupportRelocations++;
            q.SupportStatus = "Смена рубежа по частям";
            b.Say(soldier4, "Сектор потерян. Меняем рубеж прикрытия", urgent: true);
            return true;
        }
    }
}
