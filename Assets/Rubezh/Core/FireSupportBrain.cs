using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class FireSupportBrain
    {
        public static Contact Threat(BattleSimulation b, Squad q)
        {
            Contact result = null;
            float bestScore = -10000f;
            foreach (Contact value in q.Contacts.Values)
            {
                if (b.Time - value.SeenAt > 9f || !value.Visual)
                {
                    continue;
                }

                float nearestMoverDistance = 100f;
                foreach (int maneuverId in q.ManeuverIds)
                {
                    if (b.Soldiers[maneuverId].Alive)
                    {
                        nearestMoverDistance = Math.Min(
                            nearestMoverDistance,
                            V2.Distance(b.Soldiers[maneuverId].Position, value.Position)
                        );
                    }
                }

                float score =
                    value.Confidence * 5f
                    - nearestMoverDistance * 0.3f
                    - (b.Time - value.SeenAt) * 0.6f
                    + (float)((b.Time - value.FireSeenAt < 3f) ? 3 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    result = value;
                }
            }

            return result;
        }

        public static bool FindRidge(BattleSimulation b, Squad q, Soldier s, Contact threat, out V2 result)
        {
            result = s.Position;
            System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>> list =
                new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, V2>>();
            V2 b2 = default(V2);
            int groupSize = 0;
            foreach (int maneuverId in q.ManeuverIds)
            {
                if (b.Soldiers[maneuverId].Alive)
                {
                    b2 += b.Soldiers[maneuverId].Position;
                    groupSize++;
                }
            }

            if (groupSize == 0)
            {
                foreach (Soldier soldier in b.Soldiers)
                {
                    if (soldier.Side == s.Side && soldier.Available)
                    {
                        b2 += soldier.Position;
                        groupSize++;
                    }
                }
            }

            b2 /= (float)Math.Max(1, groupSize);
            float searchRadius = (
                (q.Style == CombatStyle.Aggressive) ? 22 : ((q.Style == CombatStyle.Passive) ? 12 : 17)
            );
            for (
                int i = Math.Max(1, (int)s.Position.Y - 9);
                (float)i < Math.Min(b.Map.Rows - 1, s.Position.Y + 9f);
                i++
            )
            {
                for (
                    int j = Math.Max(1, (int)s.Position.X - 9);
                    (float)j < Math.Min(b.Map.Columns - 1, s.Position.X + 9f);
                    j++
                )
                {
                    V2 candidate = new V2((float)j + 0.5f, (float)i + 0.5f);
                    float travelDistance = V2.Distance(s.Position, candidate);
                    float threatDistance = V2.Distance(candidate, threat.Position);
                    if (
                        travelDistance < 1.6f
                        || travelDistance > 9f
                        || !b.Map.Walkable(candidate)
                        || !b.Map.NearCover(candidate)
                        || threatDistance > b.WeaponRange(s) - 0.5f
                        || threatDistance < 4f
                        || V2.Distance(candidate, b2) > searchRadius
                        || !b.Map.LineOfSight(candidate, threat.Position)
                        || b.SmokeTransmission(candidate, threat.Position) < 0.3f
                    )
                    {
                        continue;
                    }

                    bool occupied = false;
                    foreach (Soldier ally in b.Soldiers)
                    {
                        if (
                            ally.Id != s.Id
                            && ally.Alive
                            && (
                                V2.Distance(candidate, ally.Position) < 1.1f
                                || V2.Distance(candidate, ally.OrderAnchor) < 1.1f
                            )
                        )
                        {
                            occupied = true;
                        }
                    }

                    if (
                        !occupied
                        && (!(b.Time < s.SupportFailedUntil) || !(V2.Distance(candidate, s.SupportFailedPost) < 2f))
                    )
                    {
                        float key =
                            CombatTraining.Cover(b, s, candidate, threat.Position) * 6f
                            - travelDistance * 0.7f
                            - TacticalAI.Risk(b, s, candidate) * 2f
                            - Math.Abs(threatDistance - b.WeaponRange(s) * 0.7f) * 0.2f;
                        list.Add(new System.Collections.Generic.KeyValuePair<float, V2>(key, candidate));
                    }
                }
            }

            list.Sort(
                (
                    System.Collections.Generic.KeyValuePair<float, V2> a,
                    System.Collections.Generic.KeyValuePair<float, V2> z
                ) => z.Key.CompareTo(a.Key)
            );
            for (int k = 0; k < Math.Min(6, list.Count); k++)
            {
                V2 value = list[k].Value;
                System.Collections.Generic.List<V2> list2 = LossMemory.Find(
                    b,
                    s,
                    value,
                    (V2 v) => TacticalAI.Risk(b, s, v) * 3f
                );
                if (list2.Count > 0 && SupportAI.PathLength(s.Position, list2) <= 14f)
                {
                    result = value;
                    return true;
                }
            }

            float initialRange = V2.Distance(s.Position, threat.Position);
            float bestScore = -10000f;
            for (
                int l = Math.Max(1, (int)s.Position.Y - 8);
                (float)l < Math.Min(b.Map.Rows - 1, s.Position.Y + 9f);
                l++
            )
            {
                for (
                    int m = Math.Max(1, (int)s.Position.X - 8);
                    (float)m < Math.Min(b.Map.Columns - 1, s.Position.X + 9f);
                    m++
                )
                {
                    V2 v3 = new V2((float)m + 0.5f, (float)l + 0.5f);
                    float travelDistanceCandidate = V2.Distance(s.Position, v3);
                    float rangeGain = initialRange - V2.Distance(v3, threat.Position);
                    if (
                        travelDistanceCandidate < 2f
                        || travelDistanceCandidate > 8f
                        || rangeGain < 3f
                        || !b.Map.Walkable(v3)
                        || !b.Map.NearCover(v3)
                        || TacticalAI.Risk(b, s, v3) > 0.25f
                    )
                    {
                        continue;
                    }

                    bool occupiedCandidate = false;
                    foreach (Soldier allyAtPost in b.Soldiers)
                    {
                        if (
                            allyAtPost.Id != s.Id
                            && allyAtPost.Alive
                            && (
                                V2.Distance(v3, allyAtPost.Position) < 1.4f
                                || V2.Distance(v3, allyAtPost.OrderAnchor) < 1.4f
                            )
                        )
                        {
                            occupiedCandidate = true;
                        }
                    }

                    if (occupiedCandidate)
                    {
                        continue;
                    }

                    float candidateScore = rangeGain - travelDistanceCandidate * 0.25f;
                    if (candidateScore <= bestScore)
                    {
                        continue;
                    }

                    System.Collections.Generic.List<V2> list3 = LossMemory.Find(
                        b,
                        s,
                        v3,
                        (V2 v) => TacticalAI.Risk(b, s, v) * 3f
                    );
                    if (list3.Count == 0 || SupportAI.PathLength(s.Position, list3) > 12f)
                    {
                        continue;
                    }

                    bool safeRoute = true;
                    foreach (V2 item in list3)
                    {
                        if (TacticalAI.Risk(b, s, item) > 0.25f)
                        {
                            safeRoute = false;
                        }
                    }

                    if (safeRoute)
                    {
                        result = v3;
                        bestScore = candidateScore;
                    }
                }
            }

            return bestScore > -10000f;
        }

        public static void PressureSupport(BattleSimulation b, Squad q)
        {
            if (q.SupportMovePhase != 0 || q.Style == CombatStyle.Withdraw)
            {
                return;
            }

            foreach (Soldier s in b.Soldiers)
            {
                if (
                    !q.Contains(s)
                    || s.Role != Role.MachineGunner
                    || !s.Available
                    || s.Broken
                    || s.Moving
                    || s.Health < 35f
                    || s.Suppression > 0.65f
                    || s.Ammo + s.Reserve < 10
                    || s.ReloadUntil > b.Time
                    || b.Time < s.NextPressureMove
                    || q.ManeuverIds.Contains(s.Id)
                )
                {
                    continue;
                }

                bool squadEngaged = false;
                foreach (Soldier soldier in b.Soldiers)
                {
                    if (
                        soldier.Side == s.Side
                        && soldier.Alive
                        && (b.Time - soldier.LastFire < 6f || b.Time - soldier.LastIncoming < 6f)
                    )
                    {
                        squadEngaged = true;
                    }
                }

                if (!squadEngaged)
                {
                    continue;
                }

                Contact contact = null;
                foreach (Contact value in q.Contacts.Values)
                {
                    if (
                        value.Visual
                        && value.Confidence > 0.35f
                        && b.Time - value.SeenAt < 9f
                        && (contact == null || value.FireSeenAt > contact.FireSeenAt)
                    )
                    {
                        contact = value;
                    }
                }

                if (contact == null || CombatCoordination.Effective(b, s, contact))
                {
                    continue;
                }

                s.NextPressureMove = b.Time + 3f;
                if (!FindRidge(b, q, s, contact, out var result))
                {
                    continue;
                }

                System.Collections.Generic.List<V2> list = LossMemory.Find(
                    b,
                    s,
                    result,
                    (V2 p) => TacticalAI.Risk(b, s, p) * 3f
                );
                bool safeRoute = true;
                foreach (V2 item in list)
                {
                    if (TacticalAI.Risk(b, s, item) > 0.25f)
                    {
                        safeRoute = false;
                    }
                }

                if (!safeRoute && !DefenseBrain.Covering(b, s, result))
                {
                    continue;
                }

                if (!SquadBrain.GiveOrder(b, q, s, Job.Cover, result, authorized: true))
                    continue;
                s.PressureMoveUntil = b.Time + 10f;
                s.Order = "Сменить рубеж и подавить огневую точку, которая прижала наших";
                b.Stats.SupportRelocations++;
                b.Say(s, "Смещаюсь. Прикрою наших!", urgent: true);
                break;
            }
        }

        public static void Reset(BattleSimulation b, Squad q)
        {
            CombatCoordination.PauseMovers(b, q, pause: false);
            q.SupportMoverId = (q.SupportFollowerId = -1);
            q.SupportMovePhase = 0;
            q.LostSectorSince = -1f;
        }

        public static bool Update(BattleSimulation b, Squad q)
        {
            if (!q.EnvelopmentActive)
            {
                if (q.SupportMovePhase != 0)
                {
                    Reset(b, q);
                }

                return false;
            }

            Contact contact = Threat(b, q);
            bool hasSector = false;
            bool firingReady = false;
            int supportCount = 0;
            foreach (int supportId in q.SupportIds)
            {
                Soldier soldier = b.Soldiers[supportId];
                if (!soldier.Available || soldier.Broken || soldier.Ammo + soldier.Reserve <= 0)
                {
                    continue;
                }

                supportCount++;
                if (contact != null && CombatCoordination.Effective(b, soldier, contact))
                {
                    hasSector = true;
                    if (b.Time - soldier.LastFire < 2f)
                    {
                        firingReady = true;
                    }
                }
            }

            q.SupportStatus = (
                firingReady
                    ? "Подавляет"
                    : (
                        hasSector
                            ? "Сектор доступен"
                            : (
                                (supportCount == 0)
                                    ? "Нет боеспособной поддержки"
                                    : ((contact == null) ? "Контакт потерян" : "Ищет рубеж огня")
                            )
                    )
            );
            if (q.SupportMovePhase != 0)
            {
                Soldier supportMover = b.Soldiers[q.SupportMoverId];
                bool abandonMove = !supportMover.Available || supportMover.Broken || b.Time - q.SupportMoveSince > 18f;
                bool reachedAnchor = V2.Distance(supportMover.Position, supportMover.OrderAnchor) < 1.2f;
                if (abandonMove)
                {
                    supportMover.SupportFailedPost = supportMover.OrderAnchor;
                    supportMover.SupportFailedUntil = b.Time + 30f;
                    TacticalAI.Stop(supportMover);
                    supportMover.OrderAnchor = supportMover.Position;
                    q.NextSupportMove = b.Time + 6f;
                    Reset(b, q);
                    return false;
                }

                if (reachedAnchor)
                {
                    SquadBrain.GiveOrder(b, q, supportMover, Job.Cover, supportMover.Position, authorized: true);
                    if (q.SupportMovePhase == 1 && q.SupportFollowerId >= 0 && contact != null)
                    {
                        Soldier follower = b.Soldiers[q.SupportFollowerId];
                        if (
                            follower.Available
                            && !follower.Broken
                            && FindRidge(b, q, follower, contact, out var result)
                        )
                        {
                            if (!SquadBrain.GiveOrder(b, q, follower, Job.Cover, result, authorized: true))
                                return true;
                            q.SupportMoverId = follower.Id;
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
                CombatCoordination.PauseMovers(b, q, pause: true);
                return true;
            }

            if (hasSector)
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
                    q.Phase = ManeuverPhase.Regroup;
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

            Soldier firstSupport = null;
            Soldier secondSupport = null;
            foreach (int supportId2 in q.SupportIds)
            {
                Soldier candidateSupport = b.Soldiers[supportId2];
                if (
                    candidateSupport.Available
                    && !candidateSupport.Broken
                    && !(candidateSupport.Health < 35f)
                    && !(candidateSupport.Suppression > 0.65f)
                )
                {
                    if (firstSupport == null || candidateSupport.Role == Role.Rifleman)
                    {
                        secondSupport = firstSupport;
                        firstSupport = candidateSupport;
                    }
                    else
                    {
                        secondSupport = candidateSupport;
                    }
                }
            }

            if (firstSupport == null)
            {
                return false;
            }

            if (!FindRidge(b, q, firstSupport, contact, out var result2))
            {
                q.NextSupportMove = b.Time + 5f;
                q.DelayReason = "Нет доступного рубежа подавления";
                return false;
            }

            if (!SquadBrain.GiveOrder(b, q, firstSupport, Job.Cover, result2, authorized: true))
                return false;
            q.SupportMoverId = firstSupport.Id;
            q.SupportFollowerId = secondSupport?.Id ?? (-1);
            q.SupportMovePhase = 1;
            q.SupportMoveSince = b.Time;
            q.SupportRally = result2;
            CombatCoordination.PauseMovers(b, q, pause: true);
            firstSupport.Order = "Занять новый рубеж подавления; напарник и группа прикрывают";
            b.Stats.SupportRelocations++;
            q.SupportStatus = "Смена рубежа по частям";
            b.Say(firstSupport, "Сектор потерян. Меняем рубеж прикрытия", urgent: true);
            return true;
        }
    }
}
