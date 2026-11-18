using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class DefenseBrain
    {
        public static SilentOrders.Core.Contact SelectThreat(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            SilentOrders.Core.Contact result = null;
            float num = -10000f;
            foreach (SilentOrders.Core.Contact value in q.Contacts.Values)
            {
                float num2 = b.Time - value.SeenAt;
                if (value.Visual && !(num2 > 9f) && !(value.Confidence < 0.35f))
                {
                    float num3 = 20f - SilentOrders.Core.V2.Distance(value.Position, q.DefenseCenter(b)) + value.Confidence * 4f - num2 * 0.65f + (float)((b.Time - value.FireSeenAt < 4f) ? 5 : 0);
                    if (num3 > num)
                    {
                        num = num3;
                        result = value;
                    }
                }
            }

            return result;
        }

        public static bool Covering(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier mover, SilentOrders.Core.V2 post)
        {
            foreach (var s in b.Soldiers)
                if (s.Id != mover.Id && s.Side == mover.Side && PositionLogic.UsefulSupport(b, s, mover, post))
                    return true;
            return false;
        }

        public static bool FindPost(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.Soldier unit, SilentOrders.Core.V2 wanted, SilentOrders.Core.V2 threat, bool rear, out SilentOrders.Core.V2 post)
        {
            SilentOrders.Core.Contact contact = SelectThreat(b, q);
            post = unit.Position;
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>> list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>>();
            for (int i = System.Math.Max(1, (int)wanted.Y - 4); i <= System.Math.Min(b.Map.Rows - 2, (int)wanted.Y + 4); i++)
            {
                for (int j = System.Math.Max(q.DefenderGroup == null ? b.Map.Columns - 20 : 0, (int)wanted.X - 4); j <= System.Math.Min(b.Map.Columns - 2, (int)wanted.X + 4); j++)
                {
                    SilentOrders.Core.V2 v2 = new SilentOrders.Core.V2((float)j + 0.5f, (float)i + 0.5f);
                    float num = SilentOrders.Core.V2.Distance(unit.Position, v2);
                    if (num < 0.6f || num > 9f || !b.Map.Walkable(v2) || !b.Map.NearCover(v2) || SilentOrders.Core.V2.Distance(v2, q.DefenseCenter(b)) > 18f || (contact != null && !Sheltered(b, v2, contact, unit)) || (b.Time < unit.RejectedUntil && SilentOrders.Core.V2.Distance(v2, unit.RejectedPost) < 2f))
                    {
                        continue;
                    }

                    float num2 = 100f;
                    bool flag = false;
                    foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                    {
                        if (soldier.Id != unit.Id && soldier.Side == unit.Side && soldier.Alive)
                        {
                            num2 = System.Math.Min(num2, System.Math.Min(SilentOrders.Core.V2.Distance(v2, soldier.Position), SilentOrders.Core.V2.Distance(v2, soldier.OrderAnchor)));
                            if (SilentOrders.Core.V2.Distance(v2, soldier.Position) < 1.1f || SilentOrders.Core.V2.Distance(v2, soldier.OrderAnchor) < 1.1f)
                            {
                                flag = true;
                            }
                        }
                    }

                    if (!flag && !(num2 > 8.5f) && ((unit.Role != SilentOrders.Core.Role.Medic && !unit.Sniper) || !(v2.X < q.Defense.LineX + 1f)))
                    {
                        float num3 = (0f - SilentOrders.Core.V2.Distance(v2, wanted)) * 2f - num * 0.35f + CombatTraining.Cover(b, unit, v2, threat) * 14f - SilentOrders.Core.TacticalAI.Risk(b, unit, v2) * 2f;
                        if (!rear && b.Map.LineOfSight(v2, threat) && SilentOrders.Core.V2.Distance(v2, threat) < b.WeaponRange(unit))
                        {
                            num3 += 5f;
                        }

                        if (rear && !b.Map.LineOfSight(v2, threat))
                        {
                            num3 += 3f;
                        }

                        if (Covering(b, unit, v2))
                        {
                            num3 += 5f;
                        }

                        list.Add(new System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>(num3, v2));
                    }
                }
            }

            list.Sort((System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> a, System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> z) => z.Key.CompareTo(a.Key));
            for (int k = 0; k < System.Math.Min(5, list.Count); k++)
            {
                SilentOrders.Core.V2 value = list[k].Value;
                System.Collections.Generic.List<SilentOrders.Core.V2> list2 = SilentOrders.Core.Pathfinder.Find(b.Map, unit.Position, value, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, unit, v) * 2f);
                if (list2.Count > 0 && SilentOrders.Core.SupportAI.PathLength(unit.Position, list2) <= 14f)
                {
                    post = value;
                    return true;
                }
            }

            return false;
        }

        public static bool Sheltered(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.V2 p, SilentOrders.Core.Contact threat, SilentOrders.Core.Soldier unit = null)
        {
            return b.Map.NearCover(p) && (threat == null || !b.Map.LineOfSight(p, threat.Position) || CombatTraining.Cover(b, unit, p, threat.Position) > 0.2f);
        }

        private static SilentOrders.Core.V2 HoldPost(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.Soldier s)
        {
            SilentOrders.Core.Contact threat = SelectThreat(b, q);
            if (Sheltered(b, s.Position, threat, s))
            {
                return s.Position;
            }

            SilentOrders.Core.V2 result = s.Position;
            float num = -10000f;
            for (int i = (int)s.Position.Y - 4; i <= (int)s.Position.Y + 4; i++)
            {
                for (int j = (int)s.Position.X - 4; j <= (int)s.Position.X + 4; j++)
                {
                    SilentOrders.Core.V2 v2 = new SilentOrders.Core.V2((float)j + 0.5f, (float)i + 0.5f);
                    float num2 = SilentOrders.Core.V2.Distance(s.Position, v2);
                    if (num2 > 4.5f || !b.Map.Walkable(v2) || !Sheltered(b, v2, threat, s))
                    {
                        continue;
                    }

                    bool flag = false;
                    foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                    {
                        if (soldier.Side == s.Side && soldier.Id != s.Id && soldier.Alive && (SilentOrders.Core.V2.Distance(v2, soldier.Position) < 1f || SilentOrders.Core.V2.Distance(v2, soldier.OrderAnchor) < 1f))
                        {
                            flag = true;
                        }
                    }

                    if (flag)
                    {
                        continue;
                    }

                    float num3 = 0f - num2 - SilentOrders.Core.TacticalAI.Risk(b, s, v2) * 2f;
                    if (!(num3 <= num))
                    {
                        System.Collections.Generic.List<SilentOrders.Core.V2> list = SilentOrders.Core.LossMemory.Find(b, s, v2, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, s, v) * 2f);
                        if (list.Count != 0 && !(SilentOrders.Core.SupportAI.PathLength(s.Position, list) > 7f))
                        {
                            result = v2;
                            num = num3;
                        }
                    }
                }
            }

            return result;
        }

        public static void Plan(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, System.Collections.Generic.List<SilentOrders.Core.Soldier> units)
        {
            AdaptiveDefense.Plan(b, q, units);
        }
    }
}
