using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SupportAI
    {
        public static void FollowSquad(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            SilentOrders.Core.V2 v = default(SilentOrders.Core.V2);
            int num = 0;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (q.Contains(soldier) && soldier.Available && soldier.Role != SilentOrders.Core.Role.Medic && (!q.EnvelopmentActive || !q.SupportIds.Contains(soldier.Id)))
                {
                    v += soldier.Position;
                    num++;
                }
            }

            if (num == 0 || q.Phase == SilentOrders.Core.ManeuverPhase.Prepare || q.Style == SilentOrders.Core.CombatStyle.Withdraw)
            {
                return;
            }

            v /= (float)num;
            SilentOrders.Core.V2 threat = SilentOrders.Core.SquadBrain.Threat(b, q);
            foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
            {
                if (!q.Contains(soldier2) || soldier2.Role != SilentOrders.Core.Role.Medic || !soldier2.Available || soldier2.Broken || soldier2.Suppression > 0.65f || soldier2.Health < 35f || q.ManeuverIds.Contains(soldier2.Id) || q.SupportIds.Contains(soldier2.Id) || b.Time < soldier2.OrderUntil)
                {
                    continue;
                }

                float num2 = SilentOrders.Core.V2.Distance(soldier2.Position, v);
                if (num2 <= 6f || (soldier2.Moving && SilentOrders.Core.V2.Distance(soldier2.OrderAnchor, v) < num2 - 1f))
                {
                    continue;
                }

                bool flag = false;
                foreach (SilentOrders.Core.Soldier soldier3 in b.Soldiers)
                {
                    if (q.Contains(soldier3) && soldier3.Id != soldier2.Id && SilentOrders.Core.SquadBrain.Ready(b, soldier3, q))
                    {
                        flag = true;
                        break;
                    }
                }

                if (q.Contacts.Count <= 0 || flag || SilentOrders.Core.CommanderPriorities.SmokeSafe(b, soldier2) || SilentOrders.Core.TacticalAI.SafeApproach(b, soldier2))
                {
                    SilentOrders.Core.V2 wanted = v + (soldier2.Position - v).Normalized * 2.5f;
                    SilentOrders.Core.V2 v2 = SilentOrders.Core.SquadBrain.FindPost(b, soldier2, wanted, threat, firing: false);
                    if (b.Map.NearCover(v2) && !(SilentOrders.Core.V2.Distance(v2, v) > num2 - 1.5f))
                    {
                        if (!SilentOrders.Core.SquadBrain.GiveOrder(b, q, soldier2, SilentOrders.Core.Job.Reposition, v2, authorized: true))
                            continue;
                        soldier2.Order = "Подтянуться за группой к соседнему укрытию, сохраняя готовность помочь";
                    }
                }
            }
        }

        public static bool NeedsEvacuation(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            return s.Alive && (s.Incapacitated || (s.Broken && b.Time - s.LastIncoming < 7f) || (s.Suppression > 0.8f && s.ImmobileSince >= 0f && b.Time - s.ImmobileSince > 4f));
        }

        public static void Plan(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            if (TacticalOrders.Emergency(q))
                return;
            // Reservations belong to a patient/helper, not to the whole squad.
            // An occupied helper is excluded by Available; other helpers can save others.

            SilentOrders.Core.Soldier soldier = null;
            SilentOrders.Core.Soldier soldier2 = null;
            float num = -10000f;
            SilentOrders.Core.V2 rescueDestination = default(SilentOrders.Core.V2);
            System.Collections.Generic.List<SilentOrders.Core.V2> path = null;
            bool flag = false;
            foreach (SilentOrders.Core.Soldier soldier4 in b.Soldiers)
            {
                if (RedCounterattack.Retreating(b, soldier4))
                    continue;
                if (!q.Contains(soldier4) || !soldier4.Alive || !soldier4.Deployed || soldier4.RescuerId >= 0 || soldier4.Busy || b.Time < soldier4.RescueCooldown)
                {
                    continue;
                }

                bool flag2 = NeedsEvacuation(b, soldier4);
                bool flag3 = soldier4.Health < 65f && b.Time >= soldier4.AidCooldown;
                if (!flag2 && !flag3)
                {
                    continue;
                }

                bool flag4 = flag2 && (b.Time - soldier4.LastIncoming < 7f || !b.Map.NearCover(soldier4.Position));
                SilentOrders.Core.V2 result = soldier4.Position;
                if (flag4 && !FindSafeZone(b, soldier4, out result))
                {
                    // Stop a life-threatening bleed in place when evacuation cannot
                    // find cover; do not abandon a nearby patient to death.
                    result = soldier4.Position;
                    flag4 = false; // Treat in place if supplies exist; cover is not a prerequisite to aid.
                }

                foreach (SilentOrders.Core.Soldier h in b.Soldiers)
                {
                    if (RedCounterattack.Retreating(b, h))
                        continue;
                    if (!q.Contains(h) || !h.Deployed || h.SupplyReceiverId >= 0 || h.HandsBusyUntil > b.Time || h.Id == soldier4.Id || !h.Available || h.Broken || h.Health < 35f || h.Suppression > 0.65f || b.Time < h.RescueCooldown)
                    {
                        continue;
                    }

                    if (!(h.Role == Role.Medic && (soldier4.Incapacitated || soldier4.Bleeding)) && !TacticalOrders.LocalService(b, h, soldier4))
                        continue;
                    float num2 = SilentOrders.Core.V2.Distance(h.Position, soldier4.Position);
                    if (num2 > (h.Role == Role.Medic ? 12f : 8f) || (!flag2 && (MedicalInventory.Choose(h, soldier4) == 0 || (num2 > 3f && (h.Role != Role.Medic || num2 > 8f)))) || (!flag4 && MedicalInventory.Choose(h, soldier4) == 0))
                    {
                        continue;
                    }

                    System.Collections.Generic.List<SilentOrders.Core.V2> list = SilentOrders.Core.Pathfinder.Find(b.Map, h.Position, soldier4.Position, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, h, v) * 3f);
                    if (PathLength(h.Position, list) > (h.Role == Role.Medic ? 18f : 12f) || (list.Count == 0 && num2 > 0.85f))
                    {
                        continue;
                    }

                    float num3 = 0f;
                    foreach (SilentOrders.Core.V2 item in list)
                    {
                        num3 = System.Math.Max(num3, SilentOrders.Core.TacticalAI.Risk(b, h, item));
                    }

                    if (num3 > 1.15f)
                    {
                        TrySmoke(b, q, soldier4.Position, onCasualty: true);
                        continue;
                    }

                    int num4 = ((h.Role == SilentOrders.Core.Role.Medic) ? 4 : ((h.Id == q.LeaderId) ? 2 : ((h.Role == SilentOrders.Core.Role.MachineGunner) ? 1 : 3)));
                    bool nearbyBleed = soldier4.Bleeding && num2 <= 2.6f && MedicalInventory.Choose(h, soldier4) == 1;
                    float num5 = (soldier4.Incapacitated ? 10000f : (flag2 ? 5000f : 0f)) + num4 * 100f - num2 * 2f + (nearbyBleed ? 20000f / (1f + num2) : 0f);
                    if (num5 > num)
                    {
                        num = num5;
                        soldier = soldier4;
                        soldier2 = h;
                        rescueDestination = nearbyBleed ? soldier4.Position : result;
                        path = list;
                        flag = flag4 && !nearbyBleed;
                    }
                }
            }

            if (soldier != null)
            {
                soldier2.RescueTargetId = soldier.Id;
                soldier.RescuerId = soldier2.Id;
                soldier2.RescuePhase = SilentOrders.Core.RescuePhase.Approach;
                soldier2.RescueSince = b.Time;
                soldier2.RescueDestination = rescueDestination;
                soldier2.RescueOrigin = soldier.Position;
                soldier2.Path = path;
                soldier2.PathCursor = 0;
                soldier2.Destination = soldier.Position;
                soldier2.LastMoved = b.Time;
                soldier2.SupportReady = false;
                soldier.SupportReady = false;
                SilentOrders.Core.TacticalAI.Stop(soldier);
                b.Stats.RescueAttempts++;
                SilentOrders.Core.Dialogue.Say(b, soldier2, 22, urgent: true);
                b.Log(q.Side, soldier2.Name + ": помогаю " + soldier.Name + (flag ? ", эвакуация к ближайшему укрытию." : ", помощь на месте."));
                TrySmoke(b, q, soldier.Position, onCasualty: true);
            }
        }

        public static float PathLength(SilentOrders.Core.V2 start, System.Collections.Generic.List<SilentOrders.Core.V2> path)
        {
            float num = 0f;
            foreach (SilentOrders.Core.V2 item in path)
            {
                num += SilentOrders.Core.V2.Distance(start, item);
                start = item;
            }

            return num;
        }

        public static bool FindSafeZone(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier patient, out SilentOrders.Core.V2 result)
        {
            SilentOrders.Core.V2 position = patient.Position;
            SilentOrders.Core.V2 v2 = SilentOrders.Core.SquadBrain.Threat(b, b.Team(patient));
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>> list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>>();
            for (int i = System.Math.Max(0, (int)position.Y - 7); i <= System.Math.Min(b.Map.Rows - 1, (int)position.Y + 7); i++)
            {
                for (int j = System.Math.Max(0, (int)position.X - 7); j <= System.Math.Min(b.Map.Columns - 1, (int)position.X + 7); j++)
                {
                    SilentOrders.Core.V2 v3 = new SilentOrders.Core.V2((float)j + 0.5f, (float)i + 0.5f);
                    float num = SilentOrders.Core.V2.Distance(position, v3);
                    if (num < 1.3f || num > 6.5f || !b.Map.Walkable(v3) || !b.Map.NearCover(v3))
                    {
                        continue;
                    }

                    float num2 = CombatTraining.Cover(b, patient, v3, v2);
                    bool flag = !b.Map.LineOfSight(v3, v2);
                    if (!flag && num2 < 0.5f)
                    {
                        continue;
                    }

                    float num3 = SilentOrders.Core.V2.Distance(v3, v2) - SilentOrders.Core.V2.Distance(position, v2);
                    if (!flag && num3 < 0.5f)
                    {
                        continue;
                    }

                    bool flag2 = false;
                    foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
                    {
                        if (soldier.Alive && soldier.Id != patient.Id && SilentOrders.Core.V2.Distance(soldier.Position, v3) < 0.85f)
                        {
                            flag2 = true;
                        }
                    }

                    if (!flag2)
                    {
                        float key = (float)(flag ? 7 : 0) + num2 * 5f + num3 * 0.65f - num * 0.8f - SilentOrders.Core.TacticalAI.Risk(b, patient, v3) * 3f;
                        list.Add(new System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2>(key, v3));
                    }
                }
            }

            list.Sort((System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> a, System.Collections.Generic.KeyValuePair<float, SilentOrders.Core.V2> c) => c.Key.CompareTo(a.Key));
            for (int k = 0; k < System.Math.Min(8, list.Count); k++)
            {
                SilentOrders.Core.V2 value = list[k].Value;
                System.Collections.Generic.List<SilentOrders.Core.V2> list2 = SilentOrders.Core.Pathfinder.Find(b.Map, position, value, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, patient, v) * 3f);
                if (list2.Count > 0 && PathLength(position, list2) <= 9f)
                {
                    result = value;
                    return true;
                }
            }

            result = position;
            return false;
        }

        public static bool Protected(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s)
        {
            return b.Time - s.LastIncoming > 2f || (b.Map.NearCover(s.Position) && (b.Map.Protection(s.Position, SilentOrders.Core.SquadBrain.Threat(b, b.Team(s))) > 0.5f || b.SmokeTransmission(s.Position, SilentOrders.Core.SquadBrain.Threat(b, b.Team(s))) < 0.3f));
        }

        public static bool TrySmoke(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q, SilentOrders.Core.V2 around, bool onCasualty = false)
        {
            Soldier subject = null;
            float distance = float.MaxValue;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Alive && s.Deployed)
                {
                    float d = V2.Distance(s.Position, around);
                    if (d < distance)
                    {
                        distance = d;
                        subject = s;
                    }
                }

            return subject != null && SmokeTactics.Request(b, q, subject, around, onCasualty ? SmokePurpose.Rescue : SmokePurpose.Route);
        }

        public static bool Update(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier s, float dt)
        {
            if (DuoCarry.Hold(b, s))
                return true;
            if (s.RescuerId >= 0)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[s.RescuerId];
                if (soldier.Alive && soldier.RescueTargetId == s.Id)
                {
                    if (s.Bleeding && MedicalInventory.HasAid(s) && soldier.RescuePhase == SilentOrders.Core.RescuePhase.Approach && s.Health < SilentOrders.Core.V2.Distance(soldier.Position, s.Position) / (b.Config.MoveSpeed * 0.85f) + 5f)
                    {
                        Release(b, soldier, complete: false);
                        SilentOrders.Core.CasualtyAI.Update(b, s, dt);
                        return true;
                    }

                    s.Moving = false;
                    s.SupportReady = false;
                    s.Crouched = true;
                    s.State = ((soldier.RescuePhase == SilentOrders.Core.RescuePhase.Drag) ? SilentOrders.Core.ActionState.Dragged : (s.Incapacitated ? SilentOrders.Core.ActionState.Incapacitated : (s.Broken ? SilentOrders.Core.ActionState.Cower : SilentOrders.Core.ActionState.Recover)));
                    s.Reason = soldier.Name + ((soldier.RescuePhase == SilentOrders.Core.RescuePhase.Drag) ? " оттаскивает меня к соседнему укрытию" : " помогает мне; жду под прикрытием");
                    return true;
                }

                s.RescuerId = -1;
            }

            if (s.RescueTargetId >= 0)
            {
                SilentOrders.Core.Soldier soldier2 = b.Soldiers[s.RescueTargetId];
                s.SupportReady = false;
                s.Crouched = true;
                if (!soldier2.Alive || s.Incapacitated || s.Broken || s.Health < 28f || s.Suppression > 0.93f || b.Time - s.RescueSince > 35f)
                {
                    Release(b, s, complete: false);
                    return false;
                }

                s.State = ((s.RescuePhase == SilentOrders.Core.RescuePhase.Drag) ? SilentOrders.Core.ActionState.Drag : ((s.RescuePhase == SilentOrders.Core.RescuePhase.Treat) ? SilentOrders.Core.ActionState.Treat : SilentOrders.Core.ActionState.Rescue));
                if (s.RescuePhase == SilentOrders.Core.RescuePhase.Approach)
                {
                    s.Reason = "Иду к " + soldier2.Name + "; прикрытие проверяется перед выходом";
                    if (InteractionReach.Can(b, s.Position, soldier2.Position, .85f))
                    {
                        bool flag = SilentOrders.Core.V2.Distance(s.RescueDestination, soldier2.Position) > 1f;
                        if (soldier2.Bleeding && soldier2.Health < SilentOrders.Core.V2.Distance(s.Position, s.RescueDestination) / (b.Config.MoveSpeed * 0.48f) + 5f && (MedicalInventory.HasAid(s) || MedicalInventory.HasAid(soldier2)))
                        {
                            flag = false;
                        }

                        if (flag)
                        {
                            s.RescuePhase = SilentOrders.Core.RescuePhase.Drag;
                            s.Path = SilentOrders.Core.LossMemory.Find(b, s, s.RescueDestination, (SilentOrders.Core.V2 v) => SilentOrders.Core.TacticalAI.Risk(b, s, v) * 3f);
                            s.PathCursor = 0;
                            s.Destination = s.RescueDestination;
                            s.LastMoved = b.Time;
                            if (s.Path.Count == 0)
                            {
                                Release(b, s, complete: false);
                                return true;
                            }

                            SilentOrders.Core.Dialogue.Say(b, s, 23, urgent: true);
                        }
                        else
                        {
                            BeginTreatment(b, s, soldier2);
                        }
                    }
                    else
                    {
                        Move(b, s, soldier2, dt, dragging: false);
                    }
                }
                else if (s.RescuePhase == SilentOrders.Core.RescuePhase.Drag)
                {
                    if (soldier2.Bleeding && soldier2.Health <= 5f && (MedicalInventory.HasAid(s) || MedicalInventory.HasAid(soldier2)))
                    {
                        BeginTreatment(b, s, soldier2);
                        return true;
                    }

                    s.Reason = "Оттаскиваю " + soldier2.Name + " к ближайшему безопасному укрытию; оружие не использую";
                    if (SilentOrders.Core.V2.Distance(s.Position, s.RescueDestination) < 0.25f)
                    {
                        soldier2.OrderAnchor = soldier2.Position;
                        soldier2.Destination = soldier2.Position;
                        soldier2.RescueCooldown = b.Time + 18f;
                        b.Stats.Evacuations++;
                        s.Evacuated++;
                        s.SavedComrades.Add(soldier2.Id);
                        foreach (SilentOrders.Core.Soldier soldier3 in b.Soldiers)
                        {
                            if (soldier3.Alive && soldier3.Side == s.Side && SilentOrders.Core.V2.Distance(soldier3.Position, soldier2.Position) < 7f)
                            {
                                MoraleDynamics.Restore(b, soldier3, .035f);
                            }
                        }

                        b.Log(s.Side, s.Name + ": " + soldier2.Name + " за соседним укрытием.");
                        BeginTreatment(b, s, soldier2);
                    }
                    else
                    {
                        Move(b, s, soldier2, dt, dragging: true);
                    }
                }
                else if (s.RescuePhase == SilentOrders.Core.RescuePhase.Treat)
                {
                    s.Reason = "Помогаю " + soldier2.Name + (s.TreatmentKind == 2 ? " аптечкой" : " жгутом") + "; не стреляю и не прикрываю";
                    if (!InteractionReach.Can(b, s.Position, soldier2.Position, 1.1f) || (s.Suppression > 0.9f && !soldier2.Bleeding))
                    {
                        Release(b, s, complete: false);
                        return true;
                    }

                    if (b.Time >= s.TreatmentUntil)
                    {
                        CompleteTreatment(b, s, soldier2);
                        Release(b, s, complete: true);
                    }
                }

                return true;
            }

            if (s.Incapacitated)
            {
                SilentOrders.Core.CasualtyAI.Update(b, s, dt);
                return true;
            }

            if (s.TreatmentUntil > 0f)
            {
                s.State = SilentOrders.Core.ActionState.Treat;
                s.SupportReady = false;
                s.Crouched = true;
                SilentOrders.Core.TacticalAI.Stop(s);
                if (s.Broken || s.Suppression > 0.85f)
                {
                    s.TreatmentUntil = 0f;
                    s.AidCooldown = b.Time + 6f;
                    return true;
                }

                if (b.Time >= s.TreatmentUntil)
                {
                    CompleteTreatment(b, s, s);
                    s.TreatmentUntil = 0f;
                    s.NextDecision = 0f;
                }

                return true;
            }

            if (!s.Broken && s.Health <= 60f && MedicalInventory.HasAid(s) && b.Time >= s.AidCooldown && b.Map.NearCover(s.Position) && Protected(b, s) && s.Suppression < 0.65f)
            {
                SilentOrders.Core.TacticalAI.Stop(s);
                MedicalInventory.Start(b, s, s);
                s.State = SilentOrders.Core.ActionState.Treat;
                s.SupportReady = false;
                s.Reason = s.TreatmentKind == 2 ? "Применяю аптечку за укрытием" : "Оказываю себе помощь жгутом за укрытием";
                if (s.TreatmentKind != 2)
                    SilentOrders.Core.Dialogue.Say(b, s, 24, urgent: true);
                return true;
            }

            return false;
        }

        private static void BeginTreatment(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier helper, SilentOrders.Core.Soldier patient)
        {
            DuoCarry.Release(b, helper);
            TacticalAI.Stop(helper);
            if (patient.Health >= 75f || (!MedicalInventory.HasAid(helper) && !MedicalInventory.HasAid(patient)) || (b.Time < patient.AidCooldown && !patient.Bleeding))
            {
                Release(b, helper, complete: true);
                return;
            }

            helper.RescuePhase = RescuePhase.Treat;
            if (!MedicalInventory.Start(b, helper, patient))
                Release(b, helper, complete: false);
        }

        public static void CompleteTreatment(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier helper, SilentOrders.Core.Soldier patient)
        {
            MedicalInventory.Complete(b, helper, patient);
        }

        public static void Release(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier helper, bool complete)
        {
            DuoCarry.Release(b, helper);
            if (helper.RescueTargetId >= 0)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[helper.RescueTargetId];
                if (soldier.RescuerId == helper.Id)
                {
                    soldier.RescuerId = -1;
                }

                soldier.NextDecision = 0f;
                soldier.RescueCooldown = soldier.Bleeding ? b.Time + .75f : System.Math.Max(soldier.RescueCooldown, b.Time + (float)(complete ? 14 : 12));
            }

            helper.RescueTargetId = -1;
            helper.RescuePhase = SilentOrders.Core.RescuePhase.None;
            helper.TreatmentUntil = 0f;
            helper.RescueCooldown = b.Time + (helper.Role == Role.Medic ? (complete ? .75f : 2f) : (float)(complete ? 5 : 6));
            helper.NextDecision = 0f;
            SilentOrders.Core.TacticalAI.Stop(helper);
            helper.OrderAnchor = helper.Position;
            helper.Job = ((helper.Id != b.Team(helper).LeaderId) ? SilentOrders.Core.Job.Cover : SilentOrders.Core.Job.Command);
        }

        // The final approach must not depend on a healthy covering teammate existing.
        // This relaxes coordination, not collision, blast avoidance or critical suppression.
        internal static float ApproachExposureBudget(float distance)
        {
            return .35f + .65f * (1f - Util.Clamp((distance - 2.6f) / 7.4f, 0f, 1f));
        }

        static bool MustWaitForCover(BattleSimulation b, Soldier h, Soldier patient, bool dragging)
        {
            if (!b.Map.NearCover(h.Position) || SquadBrain.MovementCovered(b, h) ||
                b.SmokeTransmission(h.Position, SquadBrain.Threat(b, b.Team(h))) <= .3f)
                return false;
            float exposure = PositionLogic.Exposure(b, h, h.Position);
            if (exposure <= .15f && b.Time - h.LastIncoming > 2f)
                return false;
            if (h.Role != Role.Medic || dragging) return true;
            float distance = V2.Distance(h.Position, patient.Position);
            // A short reachable final leg may finish; actual movement safety still applies.
            if (distance <= 2.6f && h.Health >= 35f) return false;
            V2 next = h.PathCursor < h.Path.Count ? h.Path[h.PathCursor] : patient.Position;
            return PositionLogic.Exposure(b, h, next) > ApproachExposureBudget(distance);
        }

        private static void Move(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier h, SilentOrders.Core.Soldier p, float dt, bool dragging)
        {
            if (h.PathCursor >= h.Path.Count)
            {
                return;
            }

            if (h.Suppression > 0.9f || MustWaitForCover(b, h, p, dragging))
            {
                h.Reason = "Эвакуация ждет прикрытия или дыма";
                return;
            }

            SilentOrders.Core.Locomotion.AdvanceWaypoint(b, h);
            SilentOrders.Core.V2 v = h.Path[h.PathCursor] - h.Position;
            if (dragging)
                DuoCarry.Join(b, h);
            float num = dragging ? DuoCarry.Speed(b, h) : CombatMovement.Speed(b, h) * .85f;
            if (dragging && !DuoCarry.CanStep(b, h, v.Normalized * System.Math.Min(v.Length, num * dt)))
            {
                DuoCarry.Release(b, h);
                num = CombatMovement.Speed(b, h) * .48f;
            }

            bool step = SilentOrders.Core.Locomotion.TryStep(b, h, v.Normalized * System.Math.Min(v.Length, num * dt), out var next, p.Id);
            if (!step && dragging && DuoCarry.Valid(b, h))
            {
                DuoCarry.Release(b, h);
                num = CombatMovement.Speed(b, h) * .48f;
                step = SilentOrders.Core.Locomotion.TryStep(b, h, v.Normalized * System.Math.Min(v.Length, num * dt), out next, p.Id);
            }

            if (step)
            {
                if (dragging)
                {
                    DuoCarry.Moved(b, h, next - h.Position);
                    p.Position = h.Position;
                    p.Facing = h.Facing;
                    p.Path.Clear();
                    p.PathCursor = 0;
                    b.Stats.DragDistance += SilentOrders.Core.V2.Distance(h.Position, next);
                }

                h.DistanceTravelled += SilentOrders.Core.V2.Distance(h.Position, next);
                h.Position = next;
                h.Facing = v.Normalized;
                h.Moving = true;
                h.LastMoved = b.Time;
                if (SilentOrders.Core.V2.Distance(next, h.Path[h.PathCursor]) < 0.04f)
                {
                    h.PathCursor++;
                }
            }
        }
    }
}
