using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SupplyAI
    {
        public static int Need(SilentOrders.Core.Soldier donor, SilentOrders.Core.Soldier receiver, bool passive)
        {
            if ((receiver.Role == Role.Grenadier || receiver.Role == Role.Commander) && receiver.Rockets == 0 && CombatTraining.AvailableLauncher(donor, receiver) >= 0)
                return 1;
            if (AmmoLogistics.Box(receiver) && receiver.Reserve + 100 <= receiver.ReserveCapacity && receiver.Reserve < (passive ? 100 : 40) && AmmoLogistics.PackCount(donor, receiver, 2) > 0 && AmmoLogistics.PackCompatible(donor, receiver, 2))
                return 2;
            if (!AmmoLogistics.Box(receiver) && receiver.Reserve + receiver.MagazineSize <= receiver.ReserveCapacity && receiver.Reserve < (passive ? receiver.MagazineSize * 4 : receiver.MagazineSize * 2) && AmmoLogistics.PackCount(donor, receiver, 3) > 0 && AmmoLogistics.PackCompatible(donor, receiver, 3))
                return 3;
            if (receiver != donor && receiver.Tourniquets == 0 && donor.Role == Role.Medic && donor.Tourniquets > 1 && passive)
                return 4;
            if (receiver != donor && receiver.Role == Role.Medic && receiver.Medkits == 0 && donor.Medkits > 1 && passive)
                return 8;
            if (GrenadePockets.CanAdd(receiver, 0) && GrenadePockets.Count(receiver, 0) < (passive ? 3 : 1) && donor.PackSmokes > 0)
                return 5;
            if (GrenadePockets.CanAdd(receiver, 4) && GrenadePockets.Count(receiver, 4) < (passive ? 3 : 1) && donor.PackRGN > 0)
                return 11;
            if (GrenadePockets.CanAdd(receiver, 5) && GrenadePockets.Count(receiver, 5) < (passive ? 3 : 1) && donor.PackRGO > 0)
                return 12;
            if (GrenadePockets.CanAdd(receiver, 3) && GrenadePockets.Count(receiver, 3) < (passive ? 3 : 1) && donor.PackF1 > 0)
                return 10;
            if (GrenadePockets.CanAdd(receiver, 1) && GrenadePockets.Count(receiver, 1) < (passive ? 3 : 1) && donor.PackGrenades > 0)
                return 6;
            if (receiver.Underbarrel && GrenadePockets.CanAdd(receiver, 2) && GrenadePockets.Count(receiver, 2) < (passive ? 3 : 1) && donor.PackUnderbarrel > 0)
                return 7;
            if (AmmoLogistics.NeedsShare(donor, receiver, passive))
                return 9;
            return 0;
        }

        public static bool Transfer(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier donor, SilentOrders.Core.Soldier receiver, int kind)
        {
            if (!donor.Alive || !receiver.Alive || donor.Side != receiver.Side || donor.Incapacitated || receiver.Incapacitated || donor.Broken || receiver.Broken || donor.RescuerId >= 0 || receiver.RescuerId >= 0 || donor.RescueTargetId >= 0 || receiver.RescueTargetId >= 0 || donor.TreatmentUntil > 0 || receiver.TreatmentUntil > 0 || !InteractionReach.Can(b, donor.Position, receiver.Position, 1.4f) || donor.HandsBusyUntil > b.Time || receiver.HandsBusyUntil > b.Time)
                return false;
            if (kind == 9)
                return AmmoLogistics.Share(b, donor, receiver, true);
            if ((kind == 2 || kind == 3) && AmmoLogistics.Pending(b, receiver))
                return false;
            string item;
            if (kind == 1 && CombatTraining.AvailableLauncher(donor, receiver) >= 0 && (receiver.Role == Role.Grenadier || receiver.Role == Role.Commander) && receiver.Rockets == 0)
            {
                receiver.LauncherKind = CombatTraining.AvailableLauncher(donor, receiver);
                CombatTraining.TakeLauncher(donor, receiver.LauncherKind);
                receiver.Rockets++;
                item = receiver.LauncherKind == 1 ? "запасной РШГ" : "запасной РПГ";
            }
            else if (kind == 2 && AmmoLogistics.PackCount(donor, receiver, 2) > 0 && AmmoLogistics.PackCompatible(donor, receiver, 2) && AmmoLogistics.Box(receiver) && receiver.Reserve < 100 && receiver.Reserve + 100 <= receiver.ReserveCapacity)
            {
                if (!AmmoLogistics.TakePackAmmo(donor, receiver, 2)) return false;
                receiver.Reserve += 100;
                item = "короб на 100 патронов";
            }
            else if (kind == 3 && AmmoLogistics.PackCount(donor, receiver, 3) > 0 && AmmoLogistics.PackCompatible(donor, receiver, 3) && !AmmoLogistics.Box(receiver) && receiver.Reserve < receiver.MagazineSize * 4 && receiver.Reserve + receiver.MagazineSize <= receiver.ReserveCapacity)
            {
                if (!AmmoLogistics.TakePackAmmo(donor, receiver, 3)) return false;
                receiver.Reserve += receiver.MagazineSize;
                item = "магазин на " + receiver.MagazineSize + " патронов";
            }
            else if (kind == 4 && donor != receiver && donor.Role == Role.Medic && donor.Tourniquets > 1 && receiver.Tourniquets == 0)
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
            else if (kind == 7 && donor.PackUnderbarrel > 0 && receiver.Underbarrel && GrenadePockets.CanAdd(receiver, 2))
            {
                donor.PackUnderbarrel--;
                GrenadePockets.Add(receiver, 2);
                item = "выстрел подствольника";
            }
            else if (kind == 8 && donor != receiver && receiver.Role == Role.Medic && receiver.Medkits == 0 && donor.Medkits > 1)
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
            b.Log(donor.Side, donor.Name + (donor == receiver ? ": беру из рюкзака " : " → " + receiver.Name + ": передан ") + item);
            b.Say(receiver, "Запас получен: " + item);
            return true;
        }

        public static void Plan(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Squad q)
        {
            if (TacticalOrders.Emergency(q))
                return;
            foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
            {
                if (q.Contains(soldier2) && soldier2.SupplyDonorId >= 0)
                {
                    return;
                }
            }

            bool passive = q.Style == SilentOrders.Core.CombatStyle.Passive;
            foreach (SilentOrders.Core.Soldier soldier3 in b.Soldiers)
            {
                if (RedCounterattack.Retreating(b, soldier3))
                    continue;
                if (!q.Contains(soldier3) || !soldier3.Available || soldier3.Broken || soldier3.Health < 45f || soldier3.Suppression > 0.6f || b.Time < soldier3.SupplyCooldown)
                {
                    continue;
                }

                foreach (SilentOrders.Core.Soldier soldier4 in b.Soldiers)
                {
                    if (RedCounterattack.Retreating(b, soldier4))
                        continue;
                    if (!q.Contains(soldier4) || !soldier4.Available || soldier4.Broken || soldier4.Suppression > 0.6f)
                    {
                        continue;
                    }

                    if (!TacticalOrders.LocalService(b, soldier4, soldier3))
                        continue;
                    int num = Need(soldier4, soldier3, passive);
                    bool quiet = ConsolidationService.SupplyReady(b, soldier3) && ConsolidationService.SupplyReady(b, soldier4);
                    if (passive && !quiet)
                    {
                        num = Need(soldier4, soldier3, false);
                    }

                    if (soldier3.ReloadUntil > b.Time || soldier4.ReloadUntil > b.Time || TacticalOrders.ServiceActive(soldier3) || TacticalOrders.ServiceActive(soldier4) || soldier3.Busy || soldier4.Busy)
                        continue;
                    if (num == 1)
                    {
                        // A loaded, reachable launcher on the ground takes priority over resupply.
                        if (LauncherLogistics.ReachableOnGround(b, soldier3, 7f))
                            continue;
                        if (LauncherLogistics.TrySupplierThrow(b, soldier4, soldier3))
                        {
                            soldier3.SupplyCooldown = b.Time + 2f;
                            return;
                        }
                    }

                    if ((num == 2 || num == 3 || num == 9) && AmmoLogistics.Pending(b, soldier3))
                        continue;
                    if (num == 0)
                    {
                        continue;
                    }

                    if (Transfer(b, soldier4, soldier3, num))
                    {
                        soldier3.SupplyCooldown = b.Time + 3f;
                        return;
                    }

                    if ((num == 2 || num == 3) && AmmoLogistics.Throw(b, soldier4, soldier3, num))
                        return;
                    if (q.EnvelopmentActive || (!quiet && (q.Phase == SilentOrders.Core.ManeuverPhase.Prepare || q.Phase == SilentOrders.Core.ManeuverPhase.Move)) || q.Style == SilentOrders.Core.CombatStyle.Withdraw || passive && !quiet)
                    {
                        continue;
                    }

                    float num2 = SilentOrders.Core.V2.Distance(soldier4.Position, soldier3.Position);
                    if (num2 > 8f || soldier4.Moving || soldier3.Moving)
                    {
                        continue;
                    }

                    SilentOrders.Core.Soldier actor = ((AmmoLogistics.Box(soldier3) || soldier3.Id == q.LeaderId) ? soldier4 : soldier3);
                    SilentOrders.Core.Soldier soldier = ((actor == soldier4) ? soldier3 : soldier4);
                    if (actor.Id == q.LeaderId && b.Alive(q.Side) > 2)
                    {
                        continue;
                    }

                    System.Collections.Generic.List<SilentOrders.Core.V2> list = SilentOrders.Core.Pathfinder.Find(b.Map, actor.Position, soldier.Position, (SilentOrders.Core.V2 p) => SilentOrders.Core.TacticalAI.Risk(b, actor, p) * 4f);
                    float num3 = 0f;
                    foreach (SilentOrders.Core.V2 item in list)
                    {
                        num3 = System.Math.Max(num3, SilentOrders.Core.TacticalAI.Risk(b, actor, item));
                    }

                    if (list.Count == 0 || SilentOrders.Core.SupportAI.PathLength(actor.Position, list) > (passive ? 6f : 12f) || num3 > (passive ? .12f : 1f) || passive && !ProgressRecovery.SafeRoute(b, actor, list))
                    {
                        continue;
                    }

                    actor.SupplyDonorId = soldier4.Id;
                    actor.SupplyReceiverId = soldier3.Id;
                    actor.SupplyKind = num;
                    actor.SupplySince = b.Time;
                    actor.SupplyAnchor = actor.OrderAnchor;
                    actor.SupplyJob = actor.Job;
                    actor.Path = list;
                    actor.PathCursor = 0;
                    actor.Destination = actor.OrderAnchor = soldier.Position;
                    actor.MoveAuthorized = true;
                    actor.ManeuverPaused = false;
                    actor.SupportReady = false;
                    actor.Order = "Снабжение: " + ((actor == soldier4) ? ("доставить припасы " + soldier3.Name) : ("получить припасы у " + soldier4.Name));
                    b.Say(actor, (actor == soldier4) ? "Поднесу запас" : "Подойду за запасом");
                    return;
                }
            }
        }

        public static void Cancel(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier actor)
        {
            actor.SupplyDonorId = -1;
            actor.SupplyReceiverId = -1;
            actor.SupplyCooldown = b.Time + 8f;
            SilentOrders.Core.TacticalAI.Stop(actor);
            actor.OrderAnchor = actor.SupplyAnchor;
            actor.Job = actor.SupplyJob;
            actor.NextDecision = 0f;
            actor.Order = "Возвращаюсь на назначенную позицию после снабжения";
        }

        public static bool Update(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier actor, float dt)
        {
            if (actor.SupplyDonorId < 0)
            {
                return false;
            }

            SilentOrders.Core.Soldier soldier = b.Soldiers[actor.SupplyDonorId];
            SilentOrders.Core.Soldier soldier2 = b.Soldiers[actor.SupplyReceiverId];
            SilentOrders.Core.Soldier soldier3 = ((actor == soldier) ? soldier2 : soldier);
            SilentOrders.Core.Squad squad = b.Team(actor);
            if (!actor.Alive || actor.Incapacitated || actor.Broken || actor.Health < 35f || actor.Suppression > 0.72f || !soldier3.Available || soldier3.Broken || squad.Style == SilentOrders.Core.CombatStyle.Withdraw || b.Time - actor.SupplySince > 18f || SilentOrders.Core.V2.Distance(actor.Position, soldier3.Position) > 9f)
            {
                Cancel(b, actor);
                return false;
            }

            if (squad.Style == CombatStyle.Passive && (!ConsolidationService.Quiet(b, actor) || !ConsolidationService.Quiet(b, soldier3)))
            {
                Cancel(b, actor);
                return false;
            }

            actor.SupportReady = false;
            actor.Crouched = true;
            actor.Reason = actor.Order;
            actor.State = SilentOrders.Core.ActionState.Move;
            if (Transfer(b, soldier, soldier2, actor.SupplyKind))
            {
                Cancel(b, actor);
                return true;
            }

            if (soldier3.Moving || actor.PathCursor >= actor.Path.Count)
            {
                Cancel(b, actor);
                return false;
            }

            if (b.Map.NearCover(actor.Position) && !SilentOrders.Core.SquadBrain.MovementCovered(b, actor))
            {
                actor.State = SilentOrders.Core.ActionState.WaitCover;
                actor.Reason = "Снабжение: жду прикрытие перед выходом";
                return true;
            }

            SilentOrders.Core.Locomotion.AdvanceWaypoint(b, actor);
            SilentOrders.Core.V2 v = actor.Path[actor.PathCursor] - actor.Position;
            if (SilentOrders.Core.Locomotion.TryStep(b, actor, v.Normalized * System.Math.Min(v.Length, CombatMovement.Speed(b, actor) * 0.85f * dt), out var next))
            {
                actor.DistanceTravelled += SilentOrders.Core.V2.Distance(actor.Position, next);
                actor.Position = next;
                actor.Facing = v.Normalized;
                actor.Moving = true;
                if (SilentOrders.Core.V2.Distance(next, actor.Path[actor.PathCursor]) < 0.04f)
                {
                    actor.PathCursor++;
                }
            }

            return true;
        }
    }
}
