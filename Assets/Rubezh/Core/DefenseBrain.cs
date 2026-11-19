using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class DefenseBrain
    {
        public static Contact SelectThreat(BattleSimulation b, Squad q)
        {
            Contact result = null;
            float bestScore = -10000f;
            foreach (Contact value in q.Contacts.Values)
            {
                float age = b.Time - value.SeenAt;
                if (value.Visual && !(age > 9f) && !(value.Confidence < 0.35f))
                {
                    float score =
                        20f
                        - V2.Distance(value.Position, q.DefenseCenter(b))
                        + value.Confidence * 4f
                        - age * 0.65f
                        + (float)((b.Time - value.FireSeenAt < 4f) ? 5 : 0);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        result = value;
                    }
                }
            }

            return result;
        }

        public static bool Covering(BattleSimulation b, Soldier mover, V2 post)
        {
            foreach (var s in b.Soldiers)
                if (s.Id != mover.Id && s.Side == mover.Side && PositionLogic.UsefulSupport(b, s, mover, post))
                    return true;
            return false;
        }

        public static bool FindPost(
            BattleSimulation b,
            Squad q,
            Soldier unit,
            V2 wanted,
            V2 threat,
            bool rear,
            out V2 post
        )
        {
            Contact contact = SelectThreat(b, q);
            post = unit.Position;
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>> list =
                new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>>();
            for (int i = Math.Max(1, (int)wanted.Y - 4); i <= Math.Min(b.Map.Rows - 2, (int)wanted.Y + 4); i++)
            {
                for (
                    int j = Math.Max(q.DefenderGroup == null ? b.Map.Columns - 20 : 0, (int)wanted.X - 4);
                    j <= Math.Min(b.Map.Columns - 2, (int)wanted.X + 4);
                    j++
                )
                {
                    V2 candidate = new V2((float)j + 0.5f, (float)i + 0.5f);
                    float travelDistance = V2.Distance(unit.Position, candidate);
                    if (
                        travelDistance < 0.6f
                        || travelDistance > 9f
                        || !b.Map.Walkable(candidate)
                        || !b.Map.NearCover(candidate)
                        || V2.Distance(candidate, q.DefenseCenter(b)) > 18f
                        || (contact != null && !Sheltered(b, candidate, contact, unit))
                        || (b.Time < unit.RejectedUntil && V2.Distance(candidate, unit.RejectedPost) < 2f)
                    )
                    {
                        continue;
                    }

                    float nearestAlly = 100f;
                    bool occupied = false;
                    foreach (Soldier soldier in b.Soldiers)
                    {
                        if (soldier.Id != unit.Id && soldier.Side == unit.Side && soldier.Alive)
                        {
                            nearestAlly = Math.Min(
                                nearestAlly,
                                Math.Min(
                                    V2.Distance(candidate, soldier.Position),
                                    V2.Distance(candidate, soldier.OrderAnchor)
                                )
                            );
                            if (
                                V2.Distance(candidate, soldier.Position) < 1.1f
                                || V2.Distance(candidate, soldier.OrderAnchor) < 1.1f
                            )
                            {
                                occupied = true;
                            }
                        }
                    }

                    if (
                        !occupied
                        && !(nearestAlly > 8.5f)
                        && ((unit.Role != Role.Medic && !unit.Sniper) || !(candidate.X < q.Defense.LineX + 1f))
                    )
                    {
                        float score =
                            (0f - V2.Distance(candidate, wanted)) * 2f
                            - travelDistance * 0.35f
                            + CombatTraining.Cover(b, unit, candidate, threat) * 14f
                            - TacticalAI.Risk(b, unit, candidate) * 2f;
                        if (
                            !rear
                            && b.Map.LineOfSight(candidate, threat)
                            && V2.Distance(candidate, threat) < b.WeaponRange(unit)
                        )
                        {
                            score += 5f;
                        }

                        if (rear && !b.Map.LineOfSight(candidate, threat))
                        {
                            score += 3f;
                        }

                        if (Covering(b, unit, candidate))
                        {
                            score += 5f;
                        }

                        list.Add(new System.Collections.Generic.KeyValuePair<float, V2>(score, candidate));
                    }
                }
            }

            list.Sort(
                (
                    System.Collections.Generic.KeyValuePair<float, V2> a,
                    System.Collections.Generic.KeyValuePair<float, V2> z
                ) => z.Key.CompareTo(a.Key)
            );
            for (int k = 0; k < Math.Min(5, list.Count); k++)
            {
                V2 value = list[k].Value;
                System.Collections.Generic.List<V2> list2 = Pathfinder.Find(
                    b.Map,
                    unit.Position,
                    value,
                    (V2 v) => TacticalAI.Risk(b, unit, v) * 2f
                );
                if (list2.Count > 0 && SupportAI.PathLength(unit.Position, list2) <= 14f)
                {
                    post = value;
                    return true;
                }
            }

            return false;
        }

        public static bool Sheltered(BattleSimulation b, V2 p, Contact threat, Soldier unit = null)
        {
            return b.Map.NearCover(p)
                && (
                    threat == null
                    || !b.Map.LineOfSight(p, threat.Position)
                    || CombatTraining.Cover(b, unit, p, threat.Position) > 0.2f
                );
        }

        private static V2 HoldPost(BattleSimulation b, Squad q, Soldier s)
        {
            Contact threat = SelectThreat(b, q);
            if (Sheltered(b, s.Position, threat, s))
            {
                return s.Position;
            }

            V2 result = s.Position;
            float bestScore = -10000f;
            for (int i = (int)s.Position.Y - 4; i <= (int)s.Position.Y + 4; i++)
            {
                for (int j = (int)s.Position.X - 4; j <= (int)s.Position.X + 4; j++)
                {
                    V2 candidate = new V2((float)j + 0.5f, (float)i + 0.5f);
                    float travelDistance = V2.Distance(s.Position, candidate);
                    if (travelDistance > 4.5f || !b.Map.Walkable(candidate) || !Sheltered(b, candidate, threat, s))
                    {
                        continue;
                    }

                    bool occupied = false;
                    foreach (Soldier soldier in b.Soldiers)
                    {
                        if (
                            soldier.Side == s.Side
                            && soldier.Id != s.Id
                            && soldier.Alive
                            && (
                                V2.Distance(candidate, soldier.Position) < 1f
                                || V2.Distance(candidate, soldier.OrderAnchor) < 1f
                            )
                        )
                        {
                            occupied = true;
                        }
                    }

                    if (occupied)
                    {
                        continue;
                    }

                    float score = 0f - travelDistance - TacticalAI.Risk(b, s, candidate) * 2f;
                    if (!(score <= bestScore))
                    {
                        System.Collections.Generic.List<V2> list = LossMemory.Find(
                            b,
                            s,
                            candidate,
                            (V2 v) => TacticalAI.Risk(b, s, v) * 2f
                        );
                        if (list.Count != 0 && !(SupportAI.PathLength(s.Position, list) > 7f))
                        {
                            result = candidate;
                            bestScore = score;
                        }
                    }
                }
            }

            return result;
        }

        public static void Plan(BattleSimulation b, Squad q, System.Collections.Generic.List<Soldier> units)
        {
            AdaptiveDefense.Plan(b, q, units);
        }
    }
}
