using System;

namespace SilentOrders.Core
{
    public static class SupplyRecovery
    {
        public static bool Active(Soldier s) => s.Side == Side.Green && s.Mechanics.RecoveryTarget >= 0;
        static Soldier Unit(BattleSimulation b, int id) => id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;
        public static bool Medical(Soldier s) => ((!s.ClassKitAssigned && s.Role == Role.Medic) || s.Mechanics.MedicalBag) && !s.Mechanics.MedicalTaken;
        public static bool FreeSlot(Soldier s) => !s.SupplyPack && !Medical(s) && s.Rockets == 0;
        public static int Kind(Soldier s, Soldier corpse)
        {
            if (SquadLoadout.ClassKit(s.Role, 0, 3) && FreeSlot(s) && corpse.SupplyPack && Stock(corpse) > 0)
                return 0;
            if (SquadLoadout.ClassKit(s.Role, 0, 2) && !s.SupplyPack && !s.Mechanics.MedicalBag && s.Rockets == 0 && Medical(corpse) && (corpse.Tourniquets > 1 || corpse.Medkits > 0))
                return 1;
            if (s.Rockets == 0 && !s.Mechanics.HandLauncher && corpse.Rockets > 0 && (corpse.LauncherKind != 1 || SquadLoadout.ClassKit(s.Role, 0, 4)))
                return 2;
            return -1;
        }

        public static int Stock(Soldier s) => AmmoLogistics.TypedStock(s) + s.PackRshg + s.PackMagazines + s.PackBoxes + s.PackRockets + s.PackSmokes + s.PackGrenades + s.PackUnderbarrel + s.PackF1 + s.PackRGN + s.PackRGO;
        static bool Body(BattleSimulation b, Soldier s) => s != null && CombatTraining.Skill(s, 2, "pack") && s.Side == Side.Green && s.Available && s.Deployed && !s.Broken && !s.Bleeding && s.BuddyAction.IncomingHelper < 0 && !s.GrenadeEscape.Active && !s.VehicleRally && !s.VehicleDisperse && !BuddyActions.Active(s) && !PairExchange.Active(b, s) && s.RescueTargetId < 0 && s.RescuerId < 0 && s.SupplyDonorId < 0 && s.SupplyReceiverId < 0 && s.HandsBusyUntil <= b.Time && s.ReloadUntil <= b.Time && s.Suppression < .45f && !TacticalOrders.Emergency(b.Green) && b.Green.Style != CombatStyle.Withdraw;
        static V2 Endpoint(BattleSimulation b, Soldier s, Soldier corpse) => InteractionReach.Can(b, s.Position, corpse.Position, 1.4f) ? s.Position : corpse.Position + (s.Position - corpse.Position).Normalized * 1.1f;
        static bool Safe(BattleSimulation b, Soldier s, V2 point)
        {
            if (!CommandPosture.Inside(b, s, point) || !StraightAdvance.Inside(b, s, point) || !Locomotion.WalkableSegment(b, s.Position, point) || PositionLogic.Occupied(b, s, point))
                return false;
            for (int i = 0; i <= 8; i++)
                if (PositionLogic.Exposure(b, s, s.Position + (point - s.Position) * (i / 8f)) > .35f)
                    return false;
            return true;
        }

        public static bool CanStart(BattleSimulation b, Soldier s, Soldier corpse)
        {
            if ((GreenMechanics.FeatureMask & 2) == 0 || !Body(b, s) || s.Mechanics.HandLauncher || Active(s) || b.Time < s.Mechanics.RecoveryNext || b.Time - s.LastIncoming < 3 || s.PathCursor < s.Path.Count || ExecutionReview.ProtectedTask(b, s) || corpse == null || corpse.Alive || !corpse.Deployed || Kind(s, corpse) < 0 || corpse.Mechanics.PackOwner >= 0 || V2.Distance(s.Position, corpse.Position) > 6)
                return false;
            if (Kind(s, corpse) == 2 && s.Contacts.Count == 0)
                return false;
            V2 point = Endpoint(b, s, corpse);
            return InteractionReach.Can(b, point, corpse.Position, 1.4f) && Safe(b, s, point) && GroupMovement.Count(b, b.Green) < GroupMovement.Maximum;
        }

        public static bool TryStart(BattleSimulation b, Soldier s, Soldier corpse)
        {
            if (!CanStart(b, s, corpse))
                return false;
            var a = s.Mechanics;
            V2 point = Endpoint(b, s, corpse);
            a.RecoveryKind = Kind(s, corpse);
            a.RecoveryTarget = corpse.Id;
            a.RecoveryRevision = b.Green.Revision;
            a.RecoveryUntil = b.Time + 8;
            a.UnpackAt = -1;
            a.RecoveryStarts++;
            a.GoalX = point.X;
            a.GoalY = point.Y;
            corpse.Mechanics.PackOwner = s.Id;
            CommandHierarchy.TransferAssignment(b, s, "Передано подбору снаряжения");
            s.SupportReady = false;
            s.OrderAnchor = s.Destination = point;
            s.Job = Job.Reposition;
            CombatCrouch.Rise(b, s);
            s.Reason = "Подбираю снаряжение погибшего";
            b.Diagnostics.Add(b, s, "start", "SupplyRecovery", s.Reason);
            return true;
        }

        public static void End(BattleSimulation b, Soldier s, bool complete)
        {
            if (!Active(s))
                return;
            var a = s.Mechanics;
            var corpse = Unit(b, a.RecoveryTarget);
            if (corpse != null && corpse.Mechanics.PackOwner == s.Id)
                corpse.Mechanics.PackOwner = -1;
            if (complete)
                a.RecoveryCompleted++;
            else
                a.RecoveryCancelled++;
            a.RecoveryTarget = -1;
            a.UnpackAt = -1;
            a.RecoveryNext = b.Time + 8;
            s.SupportReady = false;
            s.NextDecision = b.Time;
            s.OrderAnchor = s.Destination = s.Position;
            s.Job = Job.Hold;
            b.Diagnostics.Add(b, s, complete ? "finish" : "cancel", "SupplyRecovery", complete ? "Рюкзак подобран, снабжение восстановлено" : "Подбор прерван");
        }

        public static bool Valid(BattleSimulation b, Soldier s)
        {
            var corpse = Unit(b, s.Mechanics.RecoveryTarget);
            return Active(s) && Body(b, s) && corpse != null && !corpse.Alive && Kind(s, corpse) == s.Mechanics.RecoveryKind && corpse.Mechanics.PackOwner == s.Id && b.Green.Revision == s.Mechanics.RecoveryRevision && b.Time < s.Mechanics.RecoveryUntil && b.Time - s.LastIncoming >= 1 && CommandPosture.Inside(b, s, new V2(s.Mechanics.GoalX, s.Mechanics.GoalY));
        }

        public static bool Transfer(BattleSimulation b, Soldier s)
        {
            if (!Valid(b, s) || s.Mechanics.UnpackAt < 0 || b.Time < s.Mechanics.UnpackAt)
                return false;
            var corpse = Unit(b, s.Mechanics.RecoveryTarget);
            if (!InteractionReach.Can(b, s.Position, corpse.Position, 1.4f))
                return false;
            if (s.Mechanics.RecoveryKind == 0)
            {
                s.Mechanics.RecoveredMagazines += corpse.PackMagazines + corpse.PackM4Magazines + corpse.PackM24Magazines;
                s.Mechanics.RecoveredBoxes += corpse.PackBoxes + corpse.PackM249Boxes;
                s.PackRshg += corpse.PackRshg;
                s.PackRGN += corpse.PackRGN;
                s.PackRGO += corpse.PackRGO;
                s.PackF1 += corpse.PackF1;
                s.PackMagazines += corpse.PackMagazines;
                s.PackBoxes += corpse.PackBoxes;
                s.PackM4Magazines += corpse.PackM4Magazines;
                s.PackM24Magazines += corpse.PackM24Magazines;
                s.PackM249Boxes += corpse.PackM249Boxes;
                AmmoLogistics.ClearTyped(corpse);
                s.PackRockets += corpse.PackRockets;
                s.PackSmokes += corpse.PackSmokes;
                s.PackGrenades += corpse.PackGrenades;
                s.PackUnderbarrel += corpse.PackUnderbarrel;
                corpse.PackRshg = corpse.PackRGN = corpse.PackRGO = corpse.PackF1 = corpse.PackMagazines = corpse.PackBoxes = corpse.PackRockets = corpse.PackSmokes = corpse.PackGrenades = corpse.PackUnderbarrel = 0;
                corpse.SupplyPack = false;
                s.SupplyPack = true;
            }
            else if (s.Mechanics.RecoveryKind == 1)
            {
                int bands = Math.Max(0, corpse.Tourniquets - 1);
                s.Tourniquets += bands;
                corpse.Tourniquets -= bands;
                s.Medkits += corpse.Medkits;
                corpse.Medkits = 0;
                corpse.Mechanics.MedicalTaken = true;
                s.Mechanics.MedicalBag = true;
                s.Mechanics.MedicalPickups++;
            }
            else
            {
                s.Rockets = 1;
                corpse.Rockets = 0;
                s.LauncherKind = corpse.LauncherKind;
                LauncherLogistics.ReadyReserve(corpse);
                s.Mechanics.LauncherPickups++;
                if (s.Role != Role.Grenadier)
                {
                    s.Mechanics.HandLauncher = true;
                    s.Mechanics.HandUntil = b.Time + 18;
                    s.Mechanics.HandRevision = b.Team(s).Revision;
                    s.Mechanics.HandNextPlan = b.Time;
                }
            }

            End(b, s, true);
            s.HandsBusyUntil = b.Time + .35f;
            b.Say(s, s.Mechanics.RecoveryKind == 2 ? "Гранатомёт забрал" : s.Mechanics.RecoveryKind == 1 ? "Медсумка у меня" : "Рюкзак забрал. Припасы у меня");
            return true;
        }

        public static bool Hold(BattleSimulation b, Soldier s, float dt)
        {
            if (!Active(s))
                return false;
            if (!Valid(b, s))
            {
                End(b, s, false);
                return false;
            }

            var a = s.Mechanics;
            var corpse = Unit(b, a.RecoveryTarget);
            V2 point = new V2(a.GoalX, a.GoalY);
            s.SupportReady = false;
            s.Moving = false;
            s.State = ActionState.Observe;
            s.Reason = "Подбираю рюкзак с припасами";
            if (InteractionReach.Can(b, s.Position, corpse.Position, 1.4f))
            {
                if (a.UnpackAt < 0)
                    a.UnpackAt = b.Time + 1.2f;
                if (b.Time >= a.UnpackAt)
                    Transfer(b, s);
                return true;
            }

            a.UnpackAt = -1;
            if (!Safe(b, s, point))
            {
                End(b, s, false);
                return false;
            }

            if (GroupMovement.AllowStep(b, s) && dt > 0)
            {
                V2 delta = point - s.Position, next;
                if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt), out next))
                {
                    float moved = V2.Distance(s.Position, next);
                    s.Position = next;
                    s.DistanceTravelled += moved;
                    s.Moving = moved > .00001f;
                    if (s.Moving)
                        s.LastMoved = b.Time;
                    s.State = ActionState.Move;
                }
            }

            return true;
        }

        public static void Tick(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
                if (Active(s) && !Valid(b, s))
                    End(b, s, false);
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && !Active(s) && b.Time >= s.Mechanics.RecoveryNext)
                {
                    s.Mechanics.RecoveryNext = b.Time;
                    foreach (var corpse in b.Soldiers)
                        if (TryStart(b, s, corpse))
                            break;
                    if (!Active(s))
                        s.Mechanics.RecoveryNext = b.Time + 2; // Bounded search, not every tick.
                }
        }
    }
}
