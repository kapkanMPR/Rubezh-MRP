using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CasualtyAI
    {
        public const float AidSeconds = 3f;
        public static float Speed(SilentOrders.Core.BattleSimulation b)
        {
            return b.Config.MoveSpeed * 0.18f;
        }

        private static float Exposure(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 p)
        {
            float num = 0f;
            foreach (SilentOrders.Core.Contact value in b.Team(s).Contacts.Values)
            {
                if (b.Time - value.SeenAt < 10f && SilentOrders.Core.V2.Distance(p, value.Position) < b.Config.WeaponRange + 4f && b.Map.LineOfSight(value.Position, p))
                {
                    num += value.Confidence * (1f - CombatTraining.Cover(b, s, p, value.Position)) * b.SmokeTransmission(value.Position, p);
                }
            }

            return num;
        }

        private static bool Covered(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, SilentOrders.Core.V2 p)
        {
            return b.Map.NearCover(p) && (CombatTraining.Cover(b, s, p, SilentOrders.Core.SquadBrain.Threat(b, b.Team(s))) > 0.25f || Exposure(b, s, p) < 0.1f);
        }

        private static void Treat(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            TacticalAI.Stop(s);
            MedicalInventory.Start(b, s, s);
            s.State = ActionState.Treat;
        }

        public static void Update(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, float dt)
        {
            s.Crouched = true;
            s.SupportReady = false;
            s.Moving = false;
            s.State = SilentOrders.Core.ActionState.Incapacitated;
            if (s.TreatmentUntil > 0f)
            {
                SilentOrders.Core.TacticalAI.Stop(s);
                s.State = SilentOrders.Core.ActionState.Treat;
                s.Reason = "Накладываю жгут: кровотечение продолжается до завершения";
                if (b.Time >= s.TreatmentUntil)
                {
                    SilentOrders.Core.SupportAI.CompleteTreatment(b, s, s);
                    s.TreatmentUntil = 0f;
                    s.NextDecision = 0f;
                }

                return;
            }

            if (CasualtyRecovery.CrawlToComrades(b, s, dt))
                return;
            if (Covered(b, s, s.Position) && (!s.Bleeding || !MedicalInventory.HasAid(s)))
            {
                SilentOrders.Core.TacticalAI.Stop(s);
                s.Reason = (s.Bleeding ? "Жгута нет: жду помощь за укрытием" : "Кровотечение остановлено: остаюсь за укрытием");
                return;
            }

            if (MedicalInventory.HasAid(s) && s.Bleeding && (s.Health <= MedicalInventory.Seconds(s, s) + 2f || Covered(b, s, s.Position)))
            {
                Treat(b, s);
                return;
            }

            if (s.PathCursor >= s.Path.Count && b.Time >= s.NextCrawlPlan)
            {
                s.NextCrawlPlan = b.Time + 2f;
                float num = (s.Bleeding ? System.Math.Max(0f, s.Health - ((MedicalInventory.HasAid(s)) ? MedicalInventory.Seconds(s, s) : 0f) - 2f) : 12f);
                System.Collections.Generic.List<SilentOrders.Core.V2> list = null;
                float num2 = -100000f;
                SilentOrders.Core.V2 destination = s.Position;
                bool flag = false;
                for (int i = System.Math.Max(0, (int)s.Position.Y - 4); i <= System.Math.Min(b.Map.Rows - 1, (int)s.Position.Y + 4); i++)
                {
                    for (int j = System.Math.Max(0, (int)s.Position.X - 4); j <= System.Math.Min(b.Map.Columns - 1, (int)s.Position.X + 4); j++)
                    {
                        SilentOrders.Core.V2 v2 = new SilentOrders.Core.V2((float)j + 0.5f, (float)i + 0.5f);
                        float num3 = SilentOrders.Core.V2.Distance(s.Position, v2);
                        if (num3 < 0.5f || num3 > 4f || !b.Map.Walkable(v2))
                        {
                            continue;
                        }

                        bool flag2 = false;
                        foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                        {
                            if (soldier.Alive && soldier.Id != s.Id && SilentOrders.Core.V2.Distance(soldier.Position, v2) < 0.7f)
                            {
                                flag2 = true;
                            }
                        }

                        if (flag2)
                        {
                            continue;
                        }

                        bool flag3 = Covered(b, s, v2);
                        if (!flag3 && num3 > 1.5f)
                        {
                            continue;
                        }

                        System.Collections.Generic.List<SilentOrders.Core.V2> list2 = SilentOrders.Core.LossMemory.Find(b, s, v2, (SilentOrders.Core.V2 v) => Exposure(b, s, v) * 2f);
                        float num4 = SilentOrders.Core.SupportAI.PathLength(s.Position, list2) / CasualtyRecovery.CrawlSpeed(b, s);
                        if (list2.Count == 0 || num4 > num)
                        {
                            continue;
                        }

                        float num5 = 0f;
                        foreach (SilentOrders.Core.Contact value in b.Team(s).Contacts.Values)
                        {
                            if (b.Time - value.SeenAt < 10f)
                            {
                                num5 = System.Math.Max(num5, SilentOrders.Core.V2.SegmentDistance(v2, value.Position, s.Position));
                            }
                        }

                        float num6 = Exposure(b, s, v2);
                        if (flag3 || !(num6 >= Exposure(b, s, s.Position)) || !(num5 < 0.65f))
                        {
                            float num7 = (float)(flag3 ? 100 : 0) - num6 * 10f - num4 + System.Math.Min(1.5f, num5) * 3f;
                            if (num7 > num2)
                            {
                                num2 = num7;
                                list = list2;
                                destination = v2;
                                flag = flag3;
                            }
                        }
                    }
                }

                if (list != null)
                {
                    s.Path = list;
                    s.PathCursor = 0;
                    s.Destination = destination;
                    s.LastMoved = b.Time;
                    s.Reason = (flag ? "Успеваю доползти за укрытие и наложить жгут" : "Отползаю с линии огня, затем сразу жгут");
                }
                else if (MedicalInventory.HasAid(s) && s.Bleeding)
                {
                    Treat(b, s);
                    return;
                }
            }

            if (s.PathCursor < s.Path.Count)
            {
                float num8 = 0f;
                SilentOrders.Core.V2 a = s.Position;
                for (int k = s.PathCursor; k < s.Path.Count; k++)
                {
                    num8 += SilentOrders.Core.V2.Distance(a, s.Path[k]);
                    a = s.Path[k];
                }

                if (s.Bleeding && MedicalInventory.HasAid(s) && num8 / CasualtyRecovery.CrawlSpeed(b, s) + MedicalInventory.Seconds(s, s) + 1f >= s.Health)
                {
                    Treat(b, s);
                    return;
                }

                SilentOrders.Core.V2 v3 = s.Path[s.PathCursor] - s.Position;
                if (SilentOrders.Core.Locomotion.TryStep(b, s, v3.Normalized * System.Math.Min(v3.Length, CasualtyRecovery.CrawlSpeed(b, s) * dt), out var next))
                {
                    s.DistanceTravelled += SilentOrders.Core.V2.Distance(s.Position, next);
                    s.Position = next;
                    s.Moving = true;
                    s.LastMoved = b.Time;
                    if (SilentOrders.Core.V2.Distance(next, s.Path[s.PathCursor]) < 0.04f)
                    {
                        s.PathCursor++;
                    }
                }

                if (s.PathCursor >= s.Path.Count || b.Time - s.LastMoved > 1f)
                {
                    if (MedicalInventory.HasAid(s) && s.Bleeding)
                    {
                        Treat(b, s);
                    }
                    else
                    {
                        SilentOrders.Core.TacticalAI.Stop(s);
                    }
                }
            }
            else
            {
                s.Reason = (s.Bleeding ? "Кровотечение, жгута нет: нужна помощь" : "Кровотечение остановлено, жду помощи за укрытием");
            }
        }
    }
}
