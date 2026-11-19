using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SupplyAI
    {
        public static int Need(Soldier donor, Soldier receiver, bool passive)
        {
            if (
                (receiver.Role == Role.Grenadier || receiver.Role == Role.Commander)
                && receiver.Rockets == 0
                && CombatTraining.AvailableLauncher(donor, receiver) >= 0
            )
                return 1;
            if (
                AmmoLogistics.Box(receiver)
                && receiver.Reserve + 100 <= receiver.ReserveCapacity
                && receiver.Reserve < (passive ? 100 : 40)
                && AmmoLogistics.PackCount(donor, receiver, 2) > 0
                && AmmoLogistics.PackCompatible(donor, receiver, 2)
            )
                return 2;
            if (
                !AmmoLogistics.Box(receiver)
                && receiver.Reserve + receiver.MagazineSize <= receiver.ReserveCapacity
                && receiver.Reserve < (passive ? receiver.MagazineSize * 4 : receiver.MagazineSize * 2)
                && AmmoLogistics.PackCount(donor, receiver, 3) > 0
                && AmmoLogistics.PackCompatible(donor, receiver, 3)
            )
                return 3;
            if (
                receiver != donor
                && receiver.Tourniquets == 0
                && donor.Role == Role.Medic
                && donor.Tourniquets > 1
                && passive
            )
                return 4;
            if (
                receiver != donor
                && receiver.Role == Role.Medic
                && receiver.Medkits == 0
                && donor.Medkits > 1
                && passive
            )
                return 8;
            if (
                GrenadePockets.CanAdd(receiver, 0)
                && GrenadePockets.Count(receiver, 0) < (passive ? 3 : 1)
                && donor.PackSmokes > 0
            )
                return 5;
            if (
                GrenadePockets.CanAdd(receiver, 4)
                && GrenadePockets.Count(receiver, 4) < (passive ? 3 : 1)
                && donor.PackRGN > 0
            )
                return 11;
            if (
                GrenadePockets.CanAdd(receiver, 5)
                && GrenadePockets.Count(receiver, 5) < (passive ? 3 : 1)
                && donor.PackRGO > 0
            )
                return 12;
            if (
                GrenadePockets.CanAdd(receiver, 3)
                && GrenadePockets.Count(receiver, 3) < (passive ? 3 : 1)
                && donor.PackF1 > 0
            )
                return 10;
            if (
                GrenadePockets.CanAdd(receiver, 1)
                && GrenadePockets.Count(receiver, 1) < (passive ? 3 : 1)
                && donor.PackGrenades > 0
            )
                return 6;
            if (
                receiver.Underbarrel
                && GrenadePockets.CanAdd(receiver, 2)
                && GrenadePockets.Count(receiver, 2) < (passive ? 3 : 1)
                && donor.PackUnderbarrel > 0
            )
                return 7;
            if (AmmoLogistics.NeedsShare(donor, receiver, passive))
                return 9;
            return 0;
        }

        public static bool Transfer(BattleSimulation b, Soldier donor, Soldier receiver, int kind)
        {
            if (
                !donor.Alive
                || !receiver.Alive
                || donor.Side != receiver.Side
                || donor.Incapacitated
                || receiver.Incapacitated
                || donor.Broken
                || receiver.Broken
                || donor.RescuerId >= 0
                || receiver.RescuerId >= 0
                || donor.RescueTargetId >= 0
                || receiver.RescueTargetId >= 0
                || donor.TreatmentUntil > 0
                || receiver.TreatmentUntil > 0
                || !InteractionReach.Can(b, donor.Position, receiver.Position, 1.4f)
                || donor.HandsBusyUntil > b.Time
                || receiver.HandsBusyUntil > b.Time
            )
                return false;
            if (kind == 9)
                return AmmoLogistics.Share(b, donor, receiver, true);
            if ((kind == 2 || kind == 3) && AmmoLogistics.Pending(b, receiver))
                return false;
            string item;
            if (
                kind == 1
                && CombatTraining.AvailableLauncher(donor, receiver) >= 0
                && (receiver.Role == Role.Grenadier || receiver.Role == Role.Commander)
                && receiver.Rockets == 0
            )
            {
                receiver.LauncherKind = CombatTraining.AvailableLauncher(donor, receiver);
                CombatTraining.TakeLauncher(donor, receiver.LauncherKind);
                receiver.Rockets++;
                item = receiver.LauncherKind == 1 ? "запасной РШГ" : "запасной РПГ";
            }
            else if (
                kind == 2
                && AmmoLogistics.PackCount(donor, receiver, 2) > 0
                && AmmoLogistics.PackCompatible(donor, receiver, 2)
                && AmmoLogistics.Box(receiver)
                && receiver.Reserve < 100
                && receiver.Reserve + 100 <= receiver.ReserveCapacity
            )
            {
                if (!AmmoLogistics.TakePackAmmo(donor, receiver, 2))
                    return false;
                receiver.Reserve += 100;
                item = "короб на 100 патронов";
            }
            else if (
                kind == 3
                && AmmoLogistics.PackCount(donor, receiver, 3) > 0
                && AmmoLogistics.PackCompatible(donor, receiver, 3)
                && !AmmoLogistics.Box(receiver)
                && receiver.Reserve < receiver.MagazineSize * 4
                && receiver.Reserve + receiver.MagazineSize <= receiver.ReserveCapacity
            )
            {
                if (!AmmoLogistics.TakePackAmmo(donor, receiver, 3))
                    return false;
                receiver.Reserve += receiver.MagazineSize;
                item = "магазин на " + receiver.MagazineSize + " патронов";
            }
            else if (
                kind == 4
                && donor != receiver
                && donor.Role == Role.Medic
                && donor.Tourniquets > 1
                && receiver.Tourniquets == 0
            )
            {
                donor.Tourniquets--;
                receiver.Tourniquets++;
                item = "жгут";
            }
            else if (kind == 5 && donor.PackSmokes > 0 && GrenadePockets.CanAdd(receiver, 0))
            {
                donor.PackSmokes--;
                GrenadePockets.Add(receiver, 0);
                item = "дым";
            }
            else if (kind == 11 && donor.PackRGN > 0 && GrenadePockets.CanAdd(receiver, 4))
            {
                donor.PackRGN--;
                GrenadePockets.Add(receiver, 4);
                item = "РГН";
            }
            else if (kind == 12 && donor.PackRGO > 0 && GrenadePockets.CanAdd(receiver, 5))
            {
                donor.PackRGO--;
                GrenadePockets.Add(receiver, 5);
                item = "РГО";
            }
            else if (kind == 10 && donor.PackF1 > 0 && GrenadePockets.CanAdd(receiver, 3))
            {
                donor.PackF1--;
                GrenadePockets.Add(receiver, 3);
                item = "Ф-1";
            }
            else if (kind == 6 && donor.PackGrenades > 0 && GrenadePockets.CanAdd(receiver, 1))
            {
                donor.PackGrenades--;
                GrenadePockets.Add(receiver, 1);
                item = "РГД-5";
            }
            else if (
                kind == 7
                && donor.PackUnderbarrel > 0
                && receiver.Underbarrel
                && GrenadePockets.CanAdd(receiver, 2)
            )
            {
                donor.PackUnderbarrel--;
                GrenadePockets.Add(receiver, 2);
                item = "выстрел подствольника";
            }
            else if (
                kind == 8
                && donor != receiver
                && receiver.Role == Role.Medic
                && receiver.Medkits == 0
                && donor.Medkits > 1
            )
            {
                donor.Medkits--;
                receiver.Medkits++;
                item = "аптечка";
            }
            else
                return false;
            if (donor != receiver)
                donor.HelpActions++;
            b.Stats.SupplyTransfers++;
            b.Log(
                donor.Side,
                donor.Name + (donor == receiver ? ": беру из рюкзака " : " → " + receiver.Name + ": передан ") + item
            );
            b.Say(receiver, "Запас получен: " + item);
            return true;
        }

        public static void Plan(BattleSimulation b, Squad q)
        {
            if (TacticalOrders.Emergency(q))
                return;
            foreach (Soldier assignedMember in b.Soldiers)
            {
                if (q.Contains(assignedMember) && assignedMember.SupplyDonorId >= 0)
                {
                    return;
                }
            }

            bool passive = q.Style == CombatStyle.Passive;
            foreach (Soldier receiver in b.Soldiers)
            {
                if (RedCounterattack.Retreating(b, receiver))
                    continue;
                if (
                    !q.Contains(receiver)
                    || !receiver.Available
                    || receiver.Broken
                    || receiver.Health < 45f
                    || receiver.Suppression > 0.6f
                    || b.Time < receiver.SupplyCooldown
                )
                {
                    continue;
                }

                foreach (Soldier donor in b.Soldiers)
                {
                    if (RedCounterattack.Retreating(b, donor))
                        continue;
                    if (!q.Contains(donor) || !donor.Available || donor.Broken || donor.Suppression > 0.6f)
                    {
                        continue;
                    }

                    if (!TacticalOrders.LocalService(b, donor, receiver))
                        continue;
                    int supplyKind = Need(donor, receiver, passive);
                    bool quiet =
                        ConsolidationService.SupplyReady(b, receiver) && ConsolidationService.SupplyReady(b, donor);
                    if (passive && !quiet)
                    {
                        supplyKind = Need(donor, receiver, false);
                    }

                    if (
                        receiver.ReloadUntil > b.Time
                        || donor.ReloadUntil > b.Time
                        || TacticalOrders.ServiceActive(receiver)
                        || TacticalOrders.ServiceActive(donor)
                        || receiver.Busy
                        || donor.Busy
                    )
                        continue;
                    if (supplyKind == 1)
                    {
                        // A loaded, reachable launcher on the ground takes priority over resupply.
                        if (LauncherLogistics.ReachableOnGround(b, receiver, 7f))
                            continue;
                        if (LauncherLogistics.TrySupplierThrow(b, donor, receiver))
                        {
                            receiver.SupplyCooldown = b.Time + 2f;
                            return;
                        }
                    }

                    if ((supplyKind == 2 || supplyKind == 3 || supplyKind == 9) && AmmoLogistics.Pending(b, receiver))
                        continue;
                    if (supplyKind == 0)
                    {
                        continue;
                    }

                    if (Transfer(b, donor, receiver, supplyKind))
                    {
                        receiver.SupplyCooldown = b.Time + 3f;
                        return;
                    }

                    if ((supplyKind == 2 || supplyKind == 3) && AmmoLogistics.Throw(b, donor, receiver, supplyKind))
                        return;
                    if (
                        q.EnvelopmentActive
                        || (!quiet && (q.Phase == ManeuverPhase.Prepare || q.Phase == ManeuverPhase.Move))
                        || q.Style == CombatStyle.Withdraw
                        || passive && !quiet
                    )
                    {
                        continue;
                    }

                    float transferDistance = V2.Distance(donor.Position, receiver.Position);
                    if (transferDistance > 8f || donor.Moving || receiver.Moving)
                    {
                        continue;
                    }

                    Soldier actor = ((AmmoLogistics.Box(receiver) || receiver.Id == q.LeaderId) ? donor : receiver);
                    Soldier soldier = ((actor == donor) ? receiver : donor);
                    if (actor.Id == q.LeaderId && b.Alive(q.Side) > 2)
                    {
                        continue;
                    }

                    System.Collections.Generic.List<V2> list = Pathfinder.Find(
                        b.Map,
                        actor.Position,
                        soldier.Position,
                        (V2 p) => TacticalAI.Risk(b, actor, p) * 4f
                    );
                    float peakRouteRisk = 0f;
                    foreach (V2 item in list)
                    {
                        peakRouteRisk = Math.Max(peakRouteRisk, TacticalAI.Risk(b, actor, item));
                    }

                    if (
                        list.Count == 0
                        || SupportAI.PathLength(actor.Position, list) > (passive ? 6f : 12f)
                        || peakRouteRisk > (passive ? .12f : 1f)
                        || passive && !ProgressRecovery.SafeRoute(b, actor, list)
                    )
                    {
                        continue;
                    }

                    actor.SupplyDonorId = donor.Id;
                    actor.SupplyReceiverId = receiver.Id;
                    actor.SupplyKind = supplyKind;
                    actor.SupplySince = b.Time;
                    actor.SupplyAnchor = actor.OrderAnchor;
                    actor.SupplyJob = actor.Job;
                    actor.Path = list;
                    actor.PathCursor = 0;
                    actor.Destination = actor.OrderAnchor = soldier.Position;
                    actor.MoveAuthorized = true;
                    actor.ManeuverPaused = false;
                    actor.SupportReady = false;
                    actor.Order =
                        "Снабжение: "
                        + (
                            (actor == donor)
                                ? ("доставить припасы " + receiver.Name)
                                : ("получить припасы у " + donor.Name)
                        );
                    b.Say(actor, (actor == donor) ? "Поднесу запас" : "Подойду за запасом");
                    return;
                }
            }
        }

        public static void Cancel(BattleSimulation b, Soldier actor)
        {
            actor.SupplyDonorId = -1;
            actor.SupplyReceiverId = -1;
            actor.SupplyCooldown = b.Time + 8f;
            TacticalAI.Stop(actor);
            actor.OrderAnchor = actor.SupplyAnchor;
            actor.Job = actor.SupplyJob;
            actor.NextDecision = 0f;
            actor.Order = "Возвращаюсь на назначенную позицию после снабжения";
        }

        public static bool Update(BattleSimulation b, Soldier actor, float dt)
        {
            if (actor.SupplyDonorId < 0)
            {
                return false;
            }

            Soldier soldier = b.Soldiers[actor.SupplyDonorId];
            Soldier receiver = b.Soldiers[actor.SupplyReceiverId];
            Soldier counterpart = ((actor == soldier) ? receiver : soldier);
            Squad squad = b.Team(actor);
            if (
                !actor.Alive
                || actor.Incapacitated
                || actor.Broken
                || actor.Health < 35f
                || actor.Suppression > 0.72f
                || !counterpart.Available
                || counterpart.Broken
                || squad.Style == CombatStyle.Withdraw
                || b.Time - actor.SupplySince > 18f
                || V2.Distance(actor.Position, counterpart.Position) > 9f
            )
            {
                Cancel(b, actor);
                return false;
            }

            if (
                squad.Style == CombatStyle.Passive
                && (!ConsolidationService.Quiet(b, actor) || !ConsolidationService.Quiet(b, counterpart))
            )
            {
                Cancel(b, actor);
                return false;
            }

            actor.SupportReady = false;
            actor.Crouched = true;
            actor.Reason = actor.Order;
            actor.State = ActionState.Move;
            if (Transfer(b, soldier, receiver, actor.SupplyKind))
            {
                Cancel(b, actor);
                return true;
            }

            if (counterpart.Moving || actor.PathCursor >= actor.Path.Count)
            {
                Cancel(b, actor);
                return false;
            }

            if (b.Map.NearCover(actor.Position) && !SquadBrain.MovementCovered(b, actor))
            {
                actor.State = ActionState.WaitCover;
                actor.Reason = "Снабжение: жду прикрытие перед выходом";
                return true;
            }

            Locomotion.AdvanceWaypoint(b, actor);
            V2 v = actor.Path[actor.PathCursor] - actor.Position;
            if (
                Locomotion.TryStep(
                    b,
                    actor,
                    v.Normalized * Math.Min(v.Length, CombatMovement.Speed(b, actor) * 0.85f * dt),
                    out var next
                )
            )
            {
                actor.DistanceTravelled += V2.Distance(actor.Position, next);
                actor.Position = next;
                actor.Facing = v.Normalized;
                actor.Moving = true;
                if (V2.Distance(next, actor.Path[actor.PathCursor]) < 0.04f)
                {
                    actor.PathCursor++;
                }
            }

            return true;
        }
    }
}
