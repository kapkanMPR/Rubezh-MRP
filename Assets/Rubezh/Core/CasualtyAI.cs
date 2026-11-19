using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CasualtyAI
    {
        public const float AidSeconds = 3f;

        public static float Speed(BattleSimulation b)
        {
            return b.Config.MoveSpeed * 0.18f;
        }

        private static float Exposure(BattleSimulation b, Soldier s, V2 p)
        {
            float exposure = 0f;
            foreach (Contact value in b.Team(s).Contacts.Values)
            {
                if (
                    b.Time - value.SeenAt < 10f
                    && V2.Distance(p, value.Position) < b.Config.WeaponRange + 4f
                    && b.Map.LineOfSight(value.Position, p)
                )
                {
                    exposure +=
                        value.Confidence
                        * (1f - CombatTraining.Cover(b, s, p, value.Position))
                        * b.SmokeTransmission(value.Position, p);
                }
            }

            return exposure;
        }

        private static bool Covered(BattleSimulation b, Soldier s, V2 p)
        {
            return b.Map.NearCover(p)
                && (CombatTraining.Cover(b, s, p, SquadBrain.Threat(b, b.Team(s))) > 0.25f || Exposure(b, s, p) < 0.1f);
        }

        private static void Treat(BattleSimulation b, Soldier s)
        {
            TacticalAI.Stop(s);
            MedicalInventory.Start(b, s, s);
            s.State = ActionState.Treat;
        }

        public static void Update(BattleSimulation b, Soldier s, float dt)
        {
            s.Crouched = true;
            s.SupportReady = false;
            s.Moving = false;
            s.State = ActionState.Incapacitated;
            if (s.TreatmentUntil > 0f)
            {
                TacticalAI.Stop(s);
                s.State = ActionState.Treat;
                s.Reason = "Накладываю жгут: кровотечение продолжается до завершения";
                if (b.Time >= s.TreatmentUntil)
                {
                    SupportAI.CompleteTreatment(b, s, s);
                    s.TreatmentUntil = 0f;
                    s.NextDecision = 0f;
                }

                return;
            }

            if (CasualtyRecovery.CrawlToComrades(b, s, dt))
                return;
            if (Covered(b, s, s.Position) && (!s.Bleeding || !MedicalInventory.HasAid(s)))
            {
                TacticalAI.Stop(s);
                s.Reason = (
                    s.Bleeding ? "Жгута нет: жду помощь за укрытием" : "Кровотечение остановлено: остаюсь за укрытием"
                );
                return;
            }

            if (
                MedicalInventory.HasAid(s)
                && s.Bleeding
                && (s.Health <= MedicalInventory.Seconds(s, s) + 2f || Covered(b, s, s.Position))
            )
            {
                Treat(b, s);
                return;
            }

            if (s.PathCursor >= s.Path.Count && b.Time >= s.NextCrawlPlan)
            {
                s.NextCrawlPlan = b.Time + 2f;
                float availableSeconds = (
                    s.Bleeding
                        ? Math.Max(
                            0f,
                            s.Health - ((MedicalInventory.HasAid(s)) ? MedicalInventory.Seconds(s, s) : 0f) - 2f
                        )
                        : 12f
                );
                System.Collections.Generic.List<V2> list = null;
                float bestScore = -100000f;
                V2 destination = s.Position;
                bool bestPointSheltered = false;
                for (
                    int i = Math.Max(0, (int)s.Position.Y - 4);
                    i <= Math.Min(b.Map.Rows - 1, (int)s.Position.Y + 4);
                    i++
                )
                {
                    for (
                        int j = Math.Max(0, (int)s.Position.X - 4);
                        j <= Math.Min(b.Map.Columns - 1, (int)s.Position.X + 4);
                        j++
                    )
                    {
                        V2 candidate = new V2((float)j + 0.5f, (float)i + 0.5f);
                        float crawlDistance = V2.Distance(s.Position, candidate);
                        if (crawlDistance < 0.5f || crawlDistance > 4f || !b.Map.Walkable(candidate))
                        {
                            continue;
                        }

                        bool occupied = false;
                        foreach (Soldier soldier in b.Soldiers)
                        {
                            if (soldier.Alive && soldier.Id != s.Id && V2.Distance(soldier.Position, candidate) < 0.7f)
                            {
                                occupied = true;
                            }
                        }

                        if (occupied)
                        {
                            continue;
                        }

                        bool sheltered = Covered(b, s, candidate);
                        if (!sheltered && crawlDistance > 1.5f)
                        {
                            continue;
                        }

                        System.Collections.Generic.List<V2> list2 = LossMemory.Find(
                            b,
                            s,
                            candidate,
                            (V2 v) => Exposure(b, s, v) * 2f
                        );
                        float travelSeconds =
                            SupportAI.PathLength(s.Position, list2) / CasualtyRecovery.CrawlSpeed(b, s);
                        if (list2.Count == 0 || travelSeconds > availableSeconds)
                        {
                            continue;
                        }

                        float lateralSeparation = 0f;
                        foreach (Contact value in b.Team(s).Contacts.Values)
                        {
                            if (b.Time - value.SeenAt < 10f)
                            {
                                lateralSeparation = Math.Max(
                                    lateralSeparation,
                                    V2.SegmentDistance(candidate, value.Position, s.Position)
                                );
                            }
                        }

                        float exposureAtCandidate = Exposure(b, s, candidate);
                        if (
                            sheltered
                            || !(exposureAtCandidate >= Exposure(b, s, s.Position))
                            || !(lateralSeparation < 0.65f)
                        )
                        {
                            float candidateScore =
                                (float)(sheltered ? 100 : 0)
                                - exposureAtCandidate * 10f
                                - travelSeconds
                                + Math.Min(1.5f, lateralSeparation) * 3f;
                            if (candidateScore > bestScore)
                            {
                                bestScore = candidateScore;
                                list = list2;
                                destination = candidate;
                                bestPointSheltered = sheltered;
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
                    s.Reason = (
                        bestPointSheltered
                            ? "Успеваю доползти за укрытие и наложить жгут"
                            : "Отползаю с линии огня, затем сразу жгут"
                    );
                }
                else if (MedicalInventory.HasAid(s) && s.Bleeding)
                {
                    Treat(b, s);
                    return;
                }
            }

            if (s.PathCursor < s.Path.Count)
            {
                float remainingPath = 0f;
                V2 a = s.Position;
                for (int k = s.PathCursor; k < s.Path.Count; k++)
                {
                    remainingPath += V2.Distance(a, s.Path[k]);
                    a = s.Path[k];
                }

                if (
                    s.Bleeding
                    && MedicalInventory.HasAid(s)
                    && remainingPath / CasualtyRecovery.CrawlSpeed(b, s) + MedicalInventory.Seconds(s, s) + 1f
                        >= s.Health
                )
                {
                    Treat(b, s);
                    return;
                }

                V2 v3 = s.Path[s.PathCursor] - s.Position;
                if (
                    Locomotion.TryStep(
                        b,
                        s,
                        v3.Normalized * Math.Min(v3.Length, CasualtyRecovery.CrawlSpeed(b, s) * dt),
                        out var next
                    )
                )
                {
                    s.DistanceTravelled += V2.Distance(s.Position, next);
                    s.Position = next;
                    s.Moving = true;
                    s.LastMoved = b.Time;
                    if (V2.Distance(next, s.Path[s.PathCursor]) < 0.04f)
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
                        TacticalAI.Stop(s);
                    }
                }
            }
            else
            {
                s.Reason = (
                    s.Bleeding
                        ? "Кровотечение, жгута нет: нужна помощь"
                        : "Кровотечение остановлено, жду помощи за укрытием"
                );
            }
        }
    }
}
