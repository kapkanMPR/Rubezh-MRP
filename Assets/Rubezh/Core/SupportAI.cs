using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SupportAI
    {
        public static void FollowSquad(BattleSimulation b, Squad q)
        {
            V2 v = default(V2);
            int groupSize = 0;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (
                    q.Contains(soldier)
                    && soldier.Available
                    && soldier.Role != Role.Medic
                    && (!q.EnvelopmentActive || !q.SupportIds.Contains(soldier.Id))
                )
                {
                    v += soldier.Position;
                    groupSize++;
                }
            }

            if (groupSize == 0 || q.Phase == ManeuverPhase.Prepare || q.Style == CombatStyle.Withdraw)
            {
                return;
            }

            v /= (float)groupSize;
            V2 threat = SquadBrain.Threat(b, q);
            foreach (Soldier medic in b.Soldiers)
            {
                if (
                    !q.Contains(medic)
                    || medic.Role != Role.Medic
                    || !medic.Available
                    || medic.Broken
                    || medic.Suppression > 0.65f
                    || medic.Health < 35f
                    || q.ManeuverIds.Contains(medic.Id)
                    || q.SupportIds.Contains(medic.Id)
                    || b.Time < medic.OrderUntil
                )
                {
                    continue;
                }

                float distanceToGroup = V2.Distance(medic.Position, v);
                if (distanceToGroup <= 6f || (medic.Moving && V2.Distance(medic.OrderAnchor, v) < distanceToGroup - 1f))
                {
                    continue;
                }

                bool hasCoveringAlly = false;
                foreach (Soldier teammate in b.Soldiers)
                {
                    if (q.Contains(teammate) && teammate.Id != medic.Id && SquadBrain.Ready(b, teammate, q))
                    {
                        hasCoveringAlly = true;
                        break;
                    }
                }

                if (
                    q.Contacts.Count <= 0
                    || hasCoveringAlly
                    || CommanderPriorities.SmokeSafe(b, medic)
                    || TacticalAI.SafeApproach(b, medic)
                )
                {
                    V2 wanted = v + (medic.Position - v).Normalized * 2.5f;
                    V2 medicPost = SquadBrain.FindPost(b, medic, wanted, threat, firing: false);
                    if (b.Map.NearCover(medicPost) && !(V2.Distance(medicPost, v) > distanceToGroup - 1.5f))
                    {
                        if (!SquadBrain.GiveOrder(b, q, medic, Job.Reposition, medicPost, authorized: true))
                            continue;
                        medic.Order = "Подтянуться за группой к соседнему укрытию, сохраняя готовность помочь";
                    }
                }
            }
        }

        public static bool NeedsEvacuation(BattleSimulation b, Soldier s)
        {
            return s.Alive
                && (
                    s.Incapacitated
                    || (s.Broken && b.Time - s.LastIncoming < 7f)
                    || (s.Suppression > 0.8f && s.ImmobileSince >= 0f && b.Time - s.ImmobileSince > 4f)
                );
        }

        public static void Plan(BattleSimulation b, Squad q)
        {
            if (TacticalOrders.Emergency(q))
                return;
            // Reservations belong to a patient/helper, not to the whole squad.
            // An occupied helper is excluded by Available; other helpers can save others.

            Soldier soldier = null;
            Soldier selectedHelper = null;
            float bestPriority = -10000f;
            V2 rescueDestination = default(V2);
            System.Collections.Generic.List<V2> path = null;
            bool selectedEvacuation = false;
            foreach (Soldier casualty in b.Soldiers)
            {
                if (RedCounterattack.Retreating(b, casualty))
                    continue;
                if (
                    !q.Contains(casualty)
                    || !casualty.Alive
                    || !casualty.Deployed
                    || casualty.RescuerId >= 0
                    || casualty.Busy
                    || b.Time < casualty.RescueCooldown
                )
                {
                    continue;
                }

                bool needsEvacuation = NeedsEvacuation(b, casualty);
                bool needsTreatment = casualty.Health < 65f && b.Time >= casualty.AidCooldown;
                if (!needsEvacuation && !needsTreatment)
                {
                    continue;
                }

                bool shouldEvacuate =
                    needsEvacuation && (b.Time - casualty.LastIncoming < 7f || !b.Map.NearCover(casualty.Position));
                V2 result = casualty.Position;
                if (shouldEvacuate && !FindSafeZone(b, casualty, out result))
                {
                    // Stop a life-threatening bleed in place when evacuation cannot
                    // find cover; do not abandon a nearby patient to death.
                    result = casualty.Position;
                    shouldEvacuate = false; // Treat in place if supplies exist; cover is not a prerequisite to aid.
                }

                foreach (Soldier h in b.Soldiers)
                {
                    if (RedCounterattack.Retreating(b, h))
                        continue;
                    if (
                        !q.Contains(h)
                        || !h.Deployed
                        || h.SupplyReceiverId >= 0
                        || h.HandsBusyUntil > b.Time
                        || h.Id == casualty.Id
                        || !h.Available
                        || h.Broken
                        || h.Health < 35f
                        || h.Suppression > 0.65f
                        || b.Time < h.RescueCooldown
                    )
                    {
                        continue;
                    }

                    if (
                        !(h.Role == Role.Medic && (casualty.Incapacitated || casualty.Bleeding))
                        && !TacticalOrders.LocalService(b, h, casualty)
                    )
                        continue;
                    float distanceToCasualty = V2.Distance(h.Position, casualty.Position);
                    if (
                        distanceToCasualty > (h.Role == Role.Medic ? 12f : 8f)
                        || (
                            !needsEvacuation
                            && (
                                MedicalInventory.Choose(h, casualty) == 0
                                || (distanceToCasualty > 3f && (h.Role != Role.Medic || distanceToCasualty > 8f))
                            )
                        )
                        || (!shouldEvacuate && MedicalInventory.Choose(h, casualty) == 0)
                    )
                    {
                        continue;
                    }

                    System.Collections.Generic.List<V2> list = Pathfinder.Find(
                        b.Map,
                        h.Position,
                        casualty.Position,
                        (V2 v) => TacticalAI.Risk(b, h, v) * 3f
                    );
                    if (
                        PathLength(h.Position, list) > (h.Role == Role.Medic ? 18f : 12f)
                        || (list.Count == 0 && distanceToCasualty > 0.85f)
                    )
                    {
                        continue;
                    }

                    float peakPathRisk = 0f;
                    foreach (V2 item in list)
                    {
                        peakPathRisk = Math.Max(peakPathRisk, TacticalAI.Risk(b, h, item));
                    }

                    if (peakPathRisk > 1.15f)
                    {
                        TrySmoke(b, q, casualty.Position, onCasualty: true);
                        continue;
                    }

                    int helperPriority = (
                        (h.Role == Role.Medic)
                            ? 4
                            : ((h.Id == q.LeaderId) ? 2 : ((h.Role == Role.MachineGunner) ? 1 : 3))
                    );
                    bool nearbyBleed =
                        casualty.Bleeding && distanceToCasualty <= 2.6f && MedicalInventory.Choose(h, casualty) == 1;
                    float candidatePriority =
                        (casualty.Incapacitated ? 10000f : (needsEvacuation ? 5000f : 0f))
                        + helperPriority * 100f
                        - distanceToCasualty * 2f
                        + (nearbyBleed ? 20000f / (1f + distanceToCasualty) : 0f);
                    if (candidatePriority > bestPriority)
                    {
                        bestPriority = candidatePriority;
                        soldier = casualty;
                        selectedHelper = h;
                        rescueDestination = nearbyBleed ? casualty.Position : result;
                        path = list;
                        selectedEvacuation = shouldEvacuate && !nearbyBleed;
                    }
                }
            }

            if (soldier != null)
            {
                selectedHelper.RescueTargetId = soldier.Id;
                soldier.RescuerId = selectedHelper.Id;
                selectedHelper.RescuePhase = RescuePhase.Approach;
                selectedHelper.RescueSince = b.Time;
                selectedHelper.RescueDestination = rescueDestination;
                selectedHelper.RescueOrigin = soldier.Position;
                selectedHelper.Path = path;
                selectedHelper.PathCursor = 0;
                selectedHelper.Destination = soldier.Position;
                selectedHelper.LastMoved = b.Time;
                selectedHelper.SupportReady = false;
                soldier.SupportReady = false;
                TacticalAI.Stop(soldier);
                b.Stats.RescueAttempts++;
                Dialogue.Say(b, selectedHelper, 22, urgent: true);
                b.Log(
                    q.Side,
                    selectedHelper.Name
                        + ": помогаю "
                        + soldier.Name
                        + (selectedEvacuation ? ", эвакуация к ближайшему укрытию." : ", помощь на месте.")
                );
                TrySmoke(b, q, soldier.Position, onCasualty: true);
            }
        }

        public static float PathLength(V2 start, System.Collections.Generic.List<V2> path)
        {
            float length = 0f;
            foreach (V2 item in path)
            {
                length += V2.Distance(start, item);
                start = item;
            }

            return length;
        }

        public static bool FindSafeZone(BattleSimulation b, Soldier patient, out V2 result)
        {
            V2 position = patient.Position;
            V2 threatPosition = SquadBrain.Threat(b, b.Team(patient));
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>> list =
                new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>>();
            for (int i = Math.Max(0, (int)position.Y - 7); i <= Math.Min(b.Map.Rows - 1, (int)position.Y + 7); i++)
            {
                for (
                    int j = Math.Max(0, (int)position.X - 7);
                    j <= Math.Min(b.Map.Columns - 1, (int)position.X + 7);
                    j++
                )
                {
                    V2 v3 = new V2((float)j + 0.5f, (float)i + 0.5f);
                    float evacuationDistance = V2.Distance(position, v3);
                    if (
                        evacuationDistance < 1.3f
                        || evacuationDistance > 6.5f
                        || !b.Map.Walkable(v3)
                        || !b.Map.NearCover(v3)
                    )
                    {
                        continue;
                    }

                    float coverRating = CombatTraining.Cover(b, patient, v3, threatPosition);
                    bool concealed = !b.Map.LineOfSight(v3, threatPosition);
                    if (!concealed && coverRating < 0.5f)
                    {
                        continue;
                    }

                    float distanceGained = V2.Distance(v3, threatPosition) - V2.Distance(position, threatPosition);
                    if (!concealed && distanceGained < 0.5f)
                    {
                        continue;
                    }

                    bool occupied = false;
                    foreach (Soldier soldier in b.Soldiers)
                    {
                        if (soldier.Alive && soldier.Id != patient.Id && V2.Distance(soldier.Position, v3) < 0.85f)
                        {
                            occupied = true;
                        }
                    }

                    if (!occupied)
                    {
                        float key =
                            (float)(concealed ? 7 : 0)
                            + coverRating * 5f
                            + distanceGained * 0.65f
                            - evacuationDistance * 0.8f
                            - TacticalAI.Risk(b, patient, v3) * 3f;
                        list.Add(new System.Collections.Generic.KeyValuePair<float, V2>(key, v3));
                    }
                }
            }

            list.Sort(
                (
                    System.Collections.Generic.KeyValuePair<float, V2> a,
                    System.Collections.Generic.KeyValuePair<float, V2> c
                ) => c.Key.CompareTo(a.Key)
            );
            for (int k = 0; k < Math.Min(8, list.Count); k++)
            {
                V2 value = list[k].Value;
                System.Collections.Generic.List<V2> list2 = Pathfinder.Find(
                    b.Map,
                    position,
                    value,
                    (V2 v) => TacticalAI.Risk(b, patient, v) * 3f
                );
                if (list2.Count > 0 && PathLength(position, list2) <= 9f)
                {
                    result = value;
                    return true;
                }
            }

            result = position;
            return false;
        }

        public static bool Protected(BattleSimulation b, Soldier s)
        {
            return b.Time - s.LastIncoming > 2f
                || (
                    b.Map.NearCover(s.Position)
                    && (
                        b.Map.Protection(s.Position, SquadBrain.Threat(b, b.Team(s))) > 0.5f
                        || b.SmokeTransmission(s.Position, SquadBrain.Threat(b, b.Team(s))) < 0.3f
                    )
                );
        }

        public static bool TrySmoke(BattleSimulation b, Squad q, V2 around, bool onCasualty = false)
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

            return subject != null
                && SmokeTactics.Request(b, q, subject, around, onCasualty ? SmokePurpose.Rescue : SmokePurpose.Route);
        }

        public static bool Update(BattleSimulation b, Soldier s, float dt)
        {
            if (DuoCarry.Hold(b, s))
                return true;
            if (s.RescuerId >= 0)
            {
                Soldier soldier = b.Soldiers[s.RescuerId];
                if (soldier.Alive && soldier.RescueTargetId == s.Id)
                {
                    if (
                        s.Bleeding
                        && MedicalInventory.HasAid(s)
                        && soldier.RescuePhase == RescuePhase.Approach
                        && s.Health < V2.Distance(soldier.Position, s.Position) / (b.Config.MoveSpeed * 0.85f) + 5f
                    )
                    {
                        Release(b, soldier, complete: false);
                        CasualtyAI.Update(b, s, dt);
                        return true;
                    }

                    s.Moving = false;
                    s.SupportReady = false;
                    s.Crouched = true;
                    s.State = (
                        (soldier.RescuePhase == RescuePhase.Drag)
                            ? ActionState.Dragged
                            : (
                                s.Incapacitated
                                    ? ActionState.Incapacitated
                                    : (s.Broken ? ActionState.Cower : ActionState.Recover)
                            )
                    );
                    s.Reason =
                        soldier.Name
                        + (
                            (soldier.RescuePhase == RescuePhase.Drag)
                                ? " оттаскивает меня к соседнему укрытию"
                                : " помогает мне; жду под прикрытием"
                        );
                    return true;
                }

                s.RescuerId = -1;
            }

            if (s.RescueTargetId >= 0)
            {
                Soldier patient = b.Soldiers[s.RescueTargetId];
                s.SupportReady = false;
                s.Crouched = true;
                if (
                    !patient.Alive
                    || s.Incapacitated
                    || s.Broken
                    || s.Health < 28f
                    || s.Suppression > 0.93f
                    || b.Time - s.RescueSince > 35f
                )
                {
                    Release(b, s, complete: false);
                    return false;
                }

                s.State = (
                    (s.RescuePhase == RescuePhase.Drag)
                        ? ActionState.Drag
                        : ((s.RescuePhase == RescuePhase.Treat) ? ActionState.Treat : ActionState.Rescue)
                );
                if (s.RescuePhase == RescuePhase.Approach)
                {
                    s.Reason = "Иду к " + patient.Name + "; прикрытие проверяется перед выходом";
                    if (InteractionReach.Can(b, s.Position, patient.Position, .85f))
                    {
                        bool needsTransport = V2.Distance(s.RescueDestination, patient.Position) > 1f;
                        if (
                            patient.Bleeding
                            && patient.Health
                                < V2.Distance(s.Position, s.RescueDestination) / (b.Config.MoveSpeed * 0.48f) + 5f
                            && (MedicalInventory.HasAid(s) || MedicalInventory.HasAid(patient))
                        )
                        {
                            needsTransport = false;
                        }

                        if (needsTransport)
                        {
                            s.RescuePhase = RescuePhase.Drag;
                            s.Path = LossMemory.Find(
                                b,
                                s,
                                s.RescueDestination,
                                (V2 v) => TacticalAI.Risk(b, s, v) * 3f
                            );
                            s.PathCursor = 0;
                            s.Destination = s.RescueDestination;
                            s.LastMoved = b.Time;
                            if (s.Path.Count == 0)
                            {
                                Release(b, s, complete: false);
                                return true;
                            }

                            Dialogue.Say(b, s, 23, urgent: true);
                        }
                        else
                        {
                            BeginTreatment(b, s, patient);
                        }
                    }
                    else
                    {
                        Move(b, s, patient, dt, dragging: false);
                    }
                }
                else if (s.RescuePhase == RescuePhase.Drag)
                {
                    if (
                        patient.Bleeding
                        && patient.Health <= 5f
                        && (MedicalInventory.HasAid(s) || MedicalInventory.HasAid(patient))
                    )
                    {
                        BeginTreatment(b, s, patient);
                        return true;
                    }

                    s.Reason = "Оттаскиваю " + patient.Name + " к ближайшему безопасному укрытию; оружие не использую";
                    if (V2.Distance(s.Position, s.RescueDestination) < 0.25f)
                    {
                        patient.OrderAnchor = patient.Position;
                        patient.Destination = patient.Position;
                        patient.RescueCooldown = b.Time + 18f;
                        b.Stats.Evacuations++;
                        s.Evacuated++;
                        s.SavedComrades.Add(patient.Id);
                        foreach (Soldier witness in b.Soldiers)
                        {
                            if (
                                witness.Alive
                                && witness.Side == s.Side
                                && V2.Distance(witness.Position, patient.Position) < 7f
                            )
                            {
                                MoraleDynamics.Restore(b, witness, .035f);
                            }
                        }

                        b.Log(s.Side, s.Name + ": " + patient.Name + " за соседним укрытием.");
                        BeginTreatment(b, s, patient);
                    }
                    else
                    {
                        Move(b, s, patient, dt, dragging: true);
                    }
                }
                else if (s.RescuePhase == RescuePhase.Treat)
                {
                    s.Reason =
                        "Помогаю "
                        + patient.Name
                        + (s.TreatmentKind == 2 ? " аптечкой" : " жгутом")
                        + "; не стреляю и не прикрываю";
                    if (
                        !InteractionReach.Can(b, s.Position, patient.Position, 1.1f)
                        || (s.Suppression > 0.9f && !patient.Bleeding)
                    )
                    {
                        Release(b, s, complete: false);
                        return true;
                    }

                    if (b.Time >= s.TreatmentUntil)
                    {
                        CompleteTreatment(b, s, patient);
                        Release(b, s, complete: true);
                    }
                }

                return true;
            }

            if (s.Incapacitated)
            {
                CasualtyAI.Update(b, s, dt);
                return true;
            }

            if (s.TreatmentUntil > 0f)
            {
                s.State = ActionState.Treat;
                s.SupportReady = false;
                s.Crouched = true;
                TacticalAI.Stop(s);
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

            if (
                !s.Broken
                && s.Health <= 60f
                && MedicalInventory.HasAid(s)
                && b.Time >= s.AidCooldown
                && b.Map.NearCover(s.Position)
                && Protected(b, s)
                && s.Suppression < 0.65f
            )
            {
                TacticalAI.Stop(s);
                MedicalInventory.Start(b, s, s);
                s.State = ActionState.Treat;
                s.SupportReady = false;
                s.Reason =
                    s.TreatmentKind == 2 ? "Применяю аптечку за укрытием" : "Оказываю себе помощь жгутом за укрытием";
                if (s.TreatmentKind != 2)
                    Dialogue.Say(b, s, 24, urgent: true);
                return true;
            }

            return false;
        }

        private static void BeginTreatment(BattleSimulation b, Soldier helper, Soldier patient)
        {
            DuoCarry.Release(b, helper);
            TacticalAI.Stop(helper);
            if (
                patient.Health >= 75f
                || (!MedicalInventory.HasAid(helper) && !MedicalInventory.HasAid(patient))
                || (b.Time < patient.AidCooldown && !patient.Bleeding)
            )
            {
                Release(b, helper, complete: true);
                return;
            }

            helper.RescuePhase = RescuePhase.Treat;
            if (!MedicalInventory.Start(b, helper, patient))
                Release(b, helper, complete: false);
        }

        public static void CompleteTreatment(BattleSimulation b, Soldier helper, Soldier patient)
        {
            MedicalInventory.Complete(b, helper, patient);
        }

        public static void Release(BattleSimulation b, Soldier helper, bool complete)
        {
            DuoCarry.Release(b, helper);
            if (helper.RescueTargetId >= 0)
            {
                Soldier soldier = b.Soldiers[helper.RescueTargetId];
                if (soldier.RescuerId == helper.Id)
                {
                    soldier.RescuerId = -1;
                }

                soldier.NextDecision = 0f;
                soldier.RescueCooldown = soldier.Bleeding
                    ? b.Time + .75f
                    : Math.Max(soldier.RescueCooldown, b.Time + (float)(complete ? 14 : 12));
            }

            helper.RescueTargetId = -1;
            helper.RescuePhase = RescuePhase.None;
            helper.TreatmentUntil = 0f;
            helper.RescueCooldown =
                b.Time + (helper.Role == Role.Medic ? (complete ? .75f : 2f) : (float)(complete ? 5 : 6));
            helper.NextDecision = 0f;
            TacticalAI.Stop(helper);
            helper.OrderAnchor = helper.Position;
            helper.Job = ((helper.Id != b.Team(helper).LeaderId) ? Job.Cover : Job.Command);
        }

        // The final approach must not depend on a healthy covering teammate existing.
        // This relaxes coordination, not collision, blast avoidance or critical suppression.
        internal static float ApproachExposureBudget(float distance)
        {
            return .35f + .65f * (1f - Util.Clamp((distance - 2.6f) / 7.4f, 0f, 1f));
        }

        static bool MustWaitForCover(BattleSimulation b, Soldier h, Soldier patient, bool dragging)
        {
            if (
                !b.Map.NearCover(h.Position)
                || SquadBrain.MovementCovered(b, h)
                || b.SmokeTransmission(h.Position, SquadBrain.Threat(b, b.Team(h))) <= .3f
            )
                return false;
            float exposure = PositionLogic.Exposure(b, h, h.Position);
            if (exposure <= .15f && b.Time - h.LastIncoming > 2f)
                return false;
            if (h.Role != Role.Medic || dragging)
                return true;
            float distance = V2.Distance(h.Position, patient.Position);
            // A short reachable final leg may finish; actual movement safety still applies.
            if (distance <= 2.6f && h.Health >= 35f)
                return false;
            V2 next = h.PathCursor < h.Path.Count ? h.Path[h.PathCursor] : patient.Position;
            return PositionLogic.Exposure(b, h, next) > ApproachExposureBudget(distance);
        }

        private static void Move(BattleSimulation b, Soldier h, Soldier p, float dt, bool dragging)
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

            Locomotion.AdvanceWaypoint(b, h);
            V2 v = h.Path[h.PathCursor] - h.Position;
            if (dragging)
                DuoCarry.Join(b, h);
            float movementSpeed = dragging ? DuoCarry.Speed(b, h) : CombatMovement.Speed(b, h) * .85f;
            if (dragging && !DuoCarry.CanStep(b, h, v.Normalized * Math.Min(v.Length, movementSpeed * dt)))
            {
                DuoCarry.Release(b, h);
                movementSpeed = CombatMovement.Speed(b, h) * .48f;
            }

            bool step = Locomotion.TryStep(
                b,
                h,
                v.Normalized * Math.Min(v.Length, movementSpeed * dt),
                out var next,
                p.Id
            );
            if (!step && dragging && DuoCarry.Valid(b, h))
            {
                DuoCarry.Release(b, h);
                movementSpeed = CombatMovement.Speed(b, h) * .48f;
                step = Locomotion.TryStep(b, h, v.Normalized * Math.Min(v.Length, movementSpeed * dt), out next, p.Id);
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
                    b.Stats.DragDistance += V2.Distance(h.Position, next);
                }

                h.DistanceTravelled += V2.Distance(h.Position, next);
                h.Position = next;
                h.Facing = v.Normalized;
                h.Moving = true;
                h.LastMoved = b.Time;
                if (V2.Distance(next, h.Path[h.PathCursor]) < 0.04f)
                {
                    h.PathCursor++;
                }
            }
        }
    }
}
