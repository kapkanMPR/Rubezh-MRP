using System;

namespace SilentOrders.Core
{
    public sealed class GroundAmmo
    {
        public int Id,
            FromSoldierId,
            IntendedFor,
            Family,
            Rounds;
        public Side Side;
        public bool Box,
            Collected,
            Cache,
            Empty;
        public V2 From,
            To;
        public float Born,
            Duration;

        public bool Flying(float now) => now < Born + Duration;

        public V2 Position(float now) => From + (To - From) * Util.Clamp((now - Born) / Duration, 0, 1);

        public float Height(float now) =>
            Flying(now) ? (float)Math.Sin(Util.Clamp((now - Born) / Duration, 0, 1) * Math.PI) * 1.35f : 0;
    }

    public static class AmmoLogistics
    {
        public const float PickupRadius = 1.4f;

        // Use the carried weapon profile, never the fighter's original duty. Underbarrel
        // variants share rifle magazines; belts and the two sniper magazines do not.
        public static int Family(Soldier s)
        {
            int id = WeaponBalance.Index(s);
            int v = id % 6;
            return id - v + (v == 2 || v == 3 ? 0 : v);
        }

        public static bool Compatible(Soldier a, Soldier b) =>
            Family(a) == Family(b) && a.MagazineSize == b.MagazineSize;

        public static bool Box(Soldier s) => Family(s) % 6 == 1;

        // Explicit purchased ammunition follows the carried weapon, not faction or class.
        public static int TypedCount(Soldier donor, Soldier receiver)
        {
            int family = Family(receiver);
            return family == 6 ? donor.PackM4Magazines
                : family == 7 ? donor.PackM249Boxes
                : family == 11 ? donor.PackM24Magazines
                : 0;
        }

        static bool LegacyCompatible(Soldier donor, Soldier receiver, int kind) =>
            Family(receiver) / 6 == (donor.Side == Side.Red ? 1 : 0)
            && (kind == 2 ? Box(receiver) : kind == 3 && !Box(receiver));

        public static int PackCount(Soldier donor, Soldier receiver, int kind)
        {
            if (donor.Side != receiver.Side || (kind != 2 && kind != 3) || (kind == 2) != Box(receiver))
                return 0;
            return TypedCount(donor, receiver)
                + (LegacyCompatible(donor, receiver, kind) ? (kind == 2 ? donor.PackBoxes : donor.PackMagazines) : 0);
        }

        public static bool PackCompatible(Soldier donor, Soldier receiver, int kind) =>
            PackCount(donor, receiver, kind) > 0;

        public static bool TakePackAmmo(Soldier donor, Soldier receiver, int kind)
        {
            if (PackCount(donor, receiver, kind) <= 0)
                return false;
            if (TypedCount(donor, receiver) > 0)
            {
                switch (Family(receiver))
                {
                    case 6:
                        donor.PackM4Magazines--;
                        break;
                    case 7:
                        donor.PackM249Boxes--;
                        break;
                    case 11:
                        donor.PackM24Magazines--;
                        break;
                }
            }
            else if (kind == 2)
                donor.PackBoxes--;
            else
                donor.PackMagazines--;
            return true;
        }

        public static int TypedStock(Soldier s) => s.PackM4Magazines + s.PackM24Magazines + s.PackM249Boxes;

        public static void ClearTyped(Soldier s)
        {
            s.PackM4Magazines = s.PackM24Magazines = s.PackM249Boxes = 0;
        }

        public static float ThrowRange(BattleSimulation b, bool box) => ArcedOrdnance.Range(b, 1) * (box ? .5f : .75f);

        static bool Ready(BattleSimulation b, Soldier s, bool supplyJob = false) =>
            (supplyJob ? s.Alive && !s.Incapacitated : s.Available)
            && !s.Broken
            && !s.Bleeding
            && s.Suppression < .65f
            && s.HandsBusyUntil <= b.Time
            && s.ReloadUntil <= b.Time
            && !s.GrenadeEscape.Active
            && s.RescuerId < 0
            && s.RescueTargetId < 0
            && s.TreatmentUntil <= 0
            && (supplyJob || !s.Busy && !TacticalOrders.ServiceActive(s));

        public static bool NeedsShare(Soldier donor, Soldier receiver, bool consolidate)
        {
            if (donor == receiver || donor.Side != receiver.Side || !Compatible(donor, receiver) || Box(receiver))
                return false;
            int mag = receiver.MagazineSize;
            if (donor.Ammo <= 0 || donor.Reserve < mag * 2 || receiver.Reserve + mag > receiver.ReserveCapacity)
                return false;
            return receiver.Ammo + receiver.Reserve == 0
                || consolidate
                    && receiver.Ammo + receiver.Reserve <= mag
                    && donor.Ammo + donor.Reserve - (receiver.Ammo + receiver.Reserve) >= mag * 2;
        }

        public static bool Share(BattleSimulation b, Soldier donor, Soldier receiver, bool supplyJob = false)
        {
            bool quiet = ConsolidationService.Quiet(b, donor) && ConsolidationService.Quiet(b, receiver);
            if (
                Pending(b, receiver)
                || !Ready(b, donor, supplyJob)
                || !Ready(b, receiver, supplyJob)
                || !NeedsShare(donor, receiver, quiet)
                || donor.Moving
                || receiver.Moving
                || !InteractionReach.Can(b, donor.Position, receiver.Position, PickupRadius)
            )
                return false;
            int rounds = donor.Magazines.TakeBest(donor);
            if (rounds <= 0)
                return false;
            receiver.Magazines.Add(receiver, rounds);
            donor.HandsBusyUntil = receiver.HandsBusyUntil = b.Time + .4f;
            donor.SupplyCooldown = b.Time + 4;
            receiver.SupplyCooldown = b.Time + 8;
            donor.HelpActions++;
            b.Stats.AmmunitionShares++;
            b.Log(donor.Side, donor.Name + " → " + receiver.Name + ": передан личный магазин (" + rounds + ")");
            b.Say(donor, "Держи магазин. Свой запас оставил");
            return true;
        }

        public static bool TryBuddy(BattleSimulation b, Soldier receiver)
        {
            if (receiver.Ammo + receiver.Reserve > 0 || b.Time < receiver.SupplyCooldown || !Ready(b, receiver))
                return false;
            if (
                receiver.BuddyId >= 0
                && receiver.BuddyId < b.Soldiers.Count
                && Share(b, b.Soldiers[receiver.BuddyId], receiver)
            )
                return true;
            foreach (var donor in b.Soldiers)
                if (b.Team(receiver).Contains(donor) && Share(b, donor, receiver))
                    return true;
            return false;
        }

        public static bool Pending(BattleSimulation b, Soldier receiver)
        {
            foreach (var item in b.GroundAmmo)
                if (
                    !item.Empty
                    && !item.Collected
                    && item.Side == receiver.Side
                    && item.Family == Family(receiver)
                    && item.IntendedFor == receiver.Id
                    && InteractionReach.Can(b, receiver.Position, item.To, PickupRadius)
                )
                    return true;
            return false;
        }

        public static bool Throw(BattleSimulation b, Soldier donor, Soldier receiver, int kind)
        {
            if (!CombatTraining.Skill(donor, 1, "toss"))
                return false;
            if (
                (kind != 2 && kind != 3)
                || donor == receiver
                || !donor.SupplyPack
                || !PackCompatible(donor, receiver, kind)
                || !Ready(b, donor)
                || !Ready(b, receiver)
                || donor.Moving
                || receiver.Moving
                || donor.PathCursor < donor.Path.Count
                || receiver.PathCursor < receiver.Path.Count
                || b.Time < donor.NextAmmoThrow
                || Pending(b, receiver)
            )
                return false;
            bool box = kind == 2;
            int rounds = box ? 100 : receiver.MagazineSize;
            if (
                PackCount(donor, receiver, kind) <= 0
                || receiver.Reserve + rounds > receiver.ReserveCapacity
                || receiver.Reserve >= (box ? 100 : receiver.MagazineSize * 4)
            )
                return false;
            float distance = V2.Distance(donor.Position, receiver.Position);
            if (
                distance <= PickupRadius
                || distance > ThrowRange(b, box)
                || !b.Map.Walkable(receiver.Position)
                || !b.Map.LineOfSight(donor.Position, receiver.Position)
            )
                return false;
            if (!TakePackAmmo(donor, receiver, kind))
                return false;
            b.GroundAmmo.Add(
                new GroundAmmo
                {
                    Id = b.GroundAmmo.Count,
                    FromSoldierId = donor.Id,
                    IntendedFor = receiver.Id,
                    Family = Family(receiver),
                    Rounds = rounds,
                    Side = donor.Side,
                    Box = box,
                    From = donor.Position,
                    To = receiver.Position,
                    Born = b.Time,
                    Duration = .35f + distance / 9f,
                }
            );
            donor.HandsBusyUntil = b.Time + .4f;
            donor.NextAmmoThrow = b.Time + 3;
            receiver.SupplyCooldown = b.Time + 3;
            if (box)
                b.Stats.AmmoBoxesThrown++;
            else
                b.Stats.AmmoMagazinesThrown++;
            b.Log(donor.Side, donor.Name + " → " + receiver.Name + ": брошен " + (box ? "короб" : "магазин"));
            b.Say(donor, box ? "Лови короб!" : "Лови магазин!");
            return true;
        }

        public static bool Collect(BattleSimulation b, Soldier s, GroundAmmo item)
        {
            if (item.Empty || item.Rounds <= 0)
                return false;
            if (item.Cache && (s.Reserve >= s.MagazineSize || item.FromSoldierId == s.Id && b.Time < item.Born + 10))
                return false;
            if (
                !Ready(b, s)
                || item.Collected
                || item.Flying(b.Time)
                || s.Side != item.Side
                || Family(s) != item.Family
                || s.Reserve + item.Rounds > s.ReserveCapacity
                || !InteractionReach.Can(b, s.Position, item.To, PickupRadius)
            )
                return false;
            if (item.IntendedFor != s.Id && item.IntendedFor >= 0 && item.IntendedFor < b.Soldiers.Count)
            {
                var intended = b.Soldiers[item.IntendedFor];
                if (
                    intended.Available
                    && Family(intended) == item.Family
                    && intended.Reserve + item.Rounds <= intended.ReserveCapacity
                    && b.Time < item.Born + item.Duration + 6
                    && InteractionReach.Can(b, intended.Position, item.To, PickupRadius)
                )
                    return false;
            }

            item.Collected = true;
            s.Reserve += item.Rounds;
            s.HandsBusyUntil = b.Time + .25f;
            b.Stats.AmmoDropsCollected++;
            b.Stats.SupplyTransfers++;
            if (item.FromSoldierId >= 0 && item.FromSoldierId < b.Soldiers.Count)
                b.Soldiers[item.FromSoldierId].HelpActions++;
            b.Log(s.Side, s.Name + ": подобран " + (item.Box ? "короб" : "магазин") + " (" + item.Rounds + ")");
            b.Say(s, "Боеприпасы получил");
            return true;
        }

        public static void CollectNearby(BattleSimulation b, Soldier s)
        {
            foreach (var item in b.GroundAmmo)
                if (Collect(b, s, item))
                    break;
        }
    }
}
