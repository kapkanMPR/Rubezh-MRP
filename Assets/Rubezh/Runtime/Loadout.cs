using System;
using System.Linq;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class Kit
    {
        public int classCode,
            classTraining;
        public bool personalTourniquet;

        public Role Class(int slot)
        {
            return slot == 0 ? Role.Commander
                : classCode > 0 ? (Role)(classCode - 1)
                : weapon >= 4 ? Role.Sniper
                : weapon == 1 ? Role.MachineGunner
                : extra == 2 ? Role.Medic
                : (extra == 1 || extra == 4) ? Role.Grenadier
                : Role.Rifleman;
        }

        public int weapon,
            extra,
            count,
            rig,
            grenade,
            armor;
        public int grenade2,
            count2,
            grenade3,
            count3;
        public int PocketLimit
        {
            get { return GrenadePockets.Limit(rig); }
        }

        public bool PocketsValid
        {
            get
            {
                return GrenadePockets.Valid(
                    rig,
                    grenade,
                    count,
                    grenade2,
                    count2,
                    weapon == 2 || weapon == 3,
                    grenade3,
                    count3
                );
            }
        }

        public int PocketCost
        {
            get
            {
                return GrenadePrice(grenade, count) + GrenadePrice(grenade2, count2) + GrenadePrice(grenade3, count3);
            }
        }

        public int RGNStock => bag != null && bag.Length > 9 ? bag[9] : 0;
        public int RGOStock => bag != null && bag.Length > 10 ? bag[10] : 0;
        public int RshgStock => bag != null && bag.Length > 11 ? bag[11] : 0;
        public int F1Stock => bag != null && bag.Length > 8 ? bag[8] : 0;

        public void NormalizeBag()
        {
            if (bag == null)
                bag = new int[12];
            else if (bag.Length == 8 || bag.Length == 9 || bag.Length == 11)
                Array.Resize(ref bag, 12);
        }

        public void NormalizePockets()
        {
            NormalizeBag();
            count = Math.Max(0, Math.Min(count, Math.Min(3, PocketLimit)));
            count2 =
                rig >= 1 && grenade2 != grenade ? Math.Max(0, Math.Min(count2, Math.Min(3, PocketLimit - count))) : 0;
            count3 =
                rig == 2 && grenade3 != grenade && (count2 == 0 || grenade3 != grenade2)
                    ? Math.Max(0, Math.Min(count3, Math.Min(3, PocketLimit - count - count2)))
                    : 0;
        }

        public void SetRig(int value)
        {
            rig = value;
            NormalizePockets();
        }

        public int[] bag = new int[12];
        public static readonly int[] Prices = { 5, 50, 35, 20, 25, 30, 40, 60, 30, 35, 40, 50 };
        public static readonly int[] Caps = { 30, 3, 4, 7, 6, 5, 3, 2, 6, 6, 6, 4 };
        public int BagCost
        {
            get { return bag.Select((n, i) => n * Prices[i]).Sum(); }
        }

        public bool Allows(int i)
        {
            return extra == 3 ? i < 6 || i >= 8 && i <= 11 : extra == 2 && (i == 6 || i == 7);
        }

        public void SetExtra(int value)
        {
            extra = value;
            bag = new int[12];
            if (value == 3)
            {
                bag[0] = 6;
                bag[1] = 1;
                bag[2] = 2;
            }

            if (value == 2)
            {
                bag[6] = 2;
                bag[7] = 1;
            }
        }

        public void SetWeapon(int value)
        {
            weapon = value;
            if (classCode == 0 && (value == 1 || value >= 4))
                SetExtra(0);
            if (value != 2 && value != 3 && grenade == 2)
            {
                grenade = 0;
                count = 0;
            }

            if (value != 2 && value != 3 && grenade2 == 2)
            {
                grenade2 = 0;
                count2 = 0;
            }

            if (value != 2 && value != 3 && grenade3 == 2)
            {
                grenade3 = 0;
                count3 = 0;
            }

            NormalizePockets();
        }

        public string Encode()
        {
            return string.Join(
                ",",
                new[] { weapon, extra, count, rig, grenade }
                    .Concat(bag.Take(8))
                    .Concat(new[] { grenade2, count2, armor, F1Stock, RGNStock, RGOStock, grenade3, count3, RshgStock })
                    .Concat(new[] { classCode, classTraining, personalTourniquet ? 1 : 0 })
            );
        }

        public static int WeaponPrice(int value)
        {
            return value == 1 || value >= 4 ? 200
                : value == 2 ? 50
                : 0;
        }

        public static int ExtraPrice(int value)
        {
            return new[] { 0, 150, 200, 150, 200 }[value];
        }

        public static int GrenadePrice(int type, int count)
        {
            return HandGrenades.Price(type, count);
        }

        public int Cost
        {
            get { return WeaponPrice(weapon) + ExtraPrice(extra) + rig * 50 + armor * 100 + PocketCost; }
        }
    }

    [Serializable]
    public sealed class Progress
    {
        public int version = 1,
            completed,
            budgetMission;
        public Kit[] kits = FreshKits();
        public const int TrainingCount = 5;
        public int[] training = new int[TrainingCount];

        public void MigrateTraining()
        {
            if (training != null && training.Length >= 3 && training.Length < TrainingCount)
                Array.Resize(ref training, TrainingCount);
        }

        public FormationSettings formation = new FormationSettings();

        public static Kit[] FreshKits()
        {
            return Enumerable.Range(0, 5).Select(i => new Kit()).ToArray();
        }

        public string Encode()
        {
            return string.Join(";", kits.Select(k => k.Encode()));
        }

        public int Cost
        {
            get
            {
                return kits.Sum(k => k.Cost)
                    + training.Take(4).Sum(t => new[] { 0, 100, 250, 450 }[t])
                    + CombatTraining.Price(training[4]);
            }
        }

        public void MigratePockets()
        {
            if (kits != null)
                foreach (var k in kits)
                    if (k != null)
                        k.NormalizePockets();
        }

        public bool Valid()
        {
            return StructurallyValid() && WithinBudget;
        }

        public bool WithinBudget
        {
            get { return Cost - (budgetMission == 6 ? kits[4].Cost : 0) <= CampaignProgression.Budget(completed); }
        }

        // Price changes are not file corruption. Preserve the saved career and loadout.
        public bool StructurallyValid()
        {
            if (
                version != 1
                || completed < 0
                || completed > 20
                || kits == null
                || kits.Length != 5
                || training == null
                || training.Length != TrainingCount
                || training.Any(t => t < 0 || t > 3)
            )
                return false;
            foreach (var k in kits)
                if (
                    k == null
                    || k.bag == null
                    || (k.bag.Length != 8 && k.bag.Length != 9 && k.bag.Length != 11 && k.bag.Length != 12)
                )
                    return false;
            if (formation != null && !formation.Valid())
                return false;
            var b = new BattleSimulation(410);
            if (!SquadLoadout.Apply(b, Encode()))
                return false;
            return true;
        }

        public bool Award(int level, Outcome result)
        {
            if (result != Outcome.GreenCaptured || level != completed + 1 || level > 20)
                return false;
            completed++;
            return true;
        }
    }
}
