using System;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public enum DefenderBehavior
    {
        Hold,
        Attack,
        Pullback,
    }

    [Serializable]
    public sealed class DefenderSlot
    {
        public string name = "Боец";
        public Kit kit = new Kit();
    }

    [Serializable]
    public sealed class DefenderSpawn
    {
        public string name = "Группа обороны";
        public Vector2Int position = new Vector2Int(20, 12);
        public float arrivalSeconds,
            holdRadius = 6f;
        public bool legacyReserveTrigger;
        public bool vehicleArrival; // Red: permitted only for delayed groups. At most one active in a level.
        public DefenderBehavior behavior;
        public List<DefenderSlot> soldiers = new List<DefenderSlot> { new DefenderSlot() };
    }

    public static class DefenderLoadout
    {
        public static readonly int[] Weapons = { 0, 1, 2, 3, 4, 5 };
        public static readonly string[] WeaponNames =
        {
            "M4 с прицелом",
            "M249",
            "АК-74 с подствольником",
            "M16 с подствольником",
            "СВД",
            "M24",
        };
        public static readonly string[] Extras =
        {
            "Без дополнительного снаряжения",
            "РПГ",
            "Медицинский рюкзак",
            "Рюкзак снабжения",
            "РШГ",
        };
        public static readonly string[] BagNames =
        {
            "Магазины",
            "Короба пулемёта",
            "Запасные РПГ-26",
            "Дымы",
            "Осколочные гранаты",
            "Выстрелы подствольника",
            "Жгуты",
            "Аптечки",
            "Ф-1",
            "РГН",
            "РГО",
            "Запасные РШГ",
        };

        public static int WeaponPrice(int w)
        {
            return w == 5 ? Kit.WeaponPrice(4)
                : w == 3 ? Kit.WeaponPrice(2)
                : Kit.WeaponPrice(w);
        }

        public static int Cost(Kit k)
        {
            return WeaponPrice(k.weapon) + Kit.ExtraPrice(k.extra) + k.rig * 50 + k.armor * 100 + k.PocketCost;
        }

        public static bool Valid(Kit k)
        {
            if (
                k == null
                || k.armor < 0
                || k.armor > 2
                || k.weapon < 0
                || k.weapon > 5
                || k.extra < 0
                || k.extra > 4
                || k.rig < 0
                || k.rig > 2
                || k.grenade < 0
                || k.grenade > 5
                || k.count < 0
                || k.count > 3
                || k.bag == null
                || (k.bag.Length != 8 && k.bag.Length != 9 && k.bag.Length != 11 && k.bag.Length != 12)
            )
                return false;
            if (!k.PocketsValid)
                return false;
            if ((k.weapon == 1 || k.weapon >= 4) && k.extra != 0)
                return false;
            for (int i = 0; i < k.bag.Length; i++)
                if (k.bag[i] < 0 || k.bag[i] > Kit.Caps[i] || k.bag[i] > 0 && !k.Allows(i))
                    return false;
            return k.BagCost <= CampaignProgression.BagCapacity(k.extra);
        }

        public static void Normalize(Kit k)
        {
            k.weapon = Mathf.Clamp(k.weapon, 0, 5);
            k.extra = Mathf.Clamp(k.extra, 0, 4);
            if ((k.weapon == 1 || k.weapon >= 4) && k.extra != 0)
                k.SetExtra(0);
            k.armor = Mathf.Clamp(k.armor, 0, 2);
            k.rig = Mathf.Clamp(k.rig, 0, 2);
            k.grenade = Mathf.Clamp(k.grenade, 0, 5);
            k.count = Mathf.Clamp(k.count, 0, 3);
            if (k.grenade == 2 && k.weapon != 2 && k.weapon != 3)
            {
                k.grenade = 0;
                k.count = 0;
            }

            if (k.grenade2 == 2 && k.weapon != 2 && k.weapon != 3)
            {
                k.grenade2 = 0;
                k.count2 = 0;
            }

            if (k.grenade3 == 2 && k.weapon != 2 && k.weapon != 3)
            {
                k.grenade3 = 0;
                k.count3 = 0;
            }

            k.NormalizePockets();
            k.NormalizeBag();
            for (int i = 0; i < k.bag.Length; i++)
                k.bag[i] = k.Allows(i) ? Mathf.Clamp(k.bag[i], 0, Kit.Caps[i]) : 0;
            // Do not silently trim an overfull bag; validation explains the limit.
        }

        public static Soldier Build(DefenderSlot slot, GameplayRules rules = null)
        {
            Kit k = slot.kit;
            if (!Valid(k))
                throw new ArgumentException("Недопустимое снаряжение: " + slot.name);
            var s = new Soldier(rules)
            {
                Side = Side.Red,
                Name = slot.name,
                WeaponVariant = k.weapon,
                RigLevel = k.rig,
                ArmorLevel = k.armor,
                Role =
                    k.weapon >= 4 ? Role.Sniper
                    : k.weapon == 1 ? Role.MachineGunner
                    : k.extra == 2 ? Role.Medic
                    : k.extra == 1 || k.extra == 4 ? Role.Grenadier
                    : Role.Rifleman,
                SupplyPack = k.extra == 3,
                LauncherKind = k.extra == 4 ? 1 : 0,
                GrenadeType = k.grenade,
                Tourniquets = 1 + k.bag[6],
                Medkits = k.bag[7],
                Rockets = k.extra == 1 || k.extra == 4 ? 1 : 0,
                SmokeCharges = k.grenade == 0 ? k.count : 0,
                GrenadeCharges = k.grenade != 0 ? k.count : 0,
                PackRshg = k.RshgStock,
                PackRGN = k.RGNStock,
                PackRGO = k.RGOStock,
                PackF1 = k.F1Stock,
                PackMagazines = k.bag[0],
                PackBoxes = k.bag[1],
                PackRockets = k.bag[2],
                PackSmokes = k.bag[3],
                PackGrenades = k.bag[4],
                PackUnderbarrel = k.bag[5],
            };
            GrenadePockets.Equip(s, k.grenade, k.count, k.grenade2, k.count2, k.grenade3, k.count3);
            s.Ammo = s.MagazineSize;
            s.Reserve = s.ReserveCapacity;
            return s;
        }

        public static Kit Read(Soldier s)
        {
            return new Kit
            {
                weapon = s.WeaponVariant,
                extra =
                    s.SupplyPack ? 3
                    : s.Role == Role.Medic ? 2
                    : s.Rockets > 0 ? (s.LauncherKind == 1 ? 4 : 1)
                    : 0,
                armor = s.ArmorLevel,
                rig = s.RigLevel,
                grenade = s.GrenadeType,
                count = s.GrenadeType == 0 ? s.SmokeCharges : s.GrenadeCharges,
                grenade2 = s.SmokeCharges > 0 && s.GrenadeType != 0 ? 0 : s.OtherGrenadeType,
                count2 = s.GrenadeType != 0 && s.SmokeCharges > 0 ? s.SmokeCharges : s.OtherGrenadeCharges,
                grenade3 = s.SmokeCharges > 0 && s.GrenadeType != 0 ? s.OtherGrenadeType : s.ThirdGrenadeType,
                count3 = s.SmokeCharges > 0 && s.GrenadeType != 0 ? s.OtherGrenadeCharges : s.ThirdGrenadeCharges,
                bag = new[]
                {
                    s.PackMagazines,
                    s.PackBoxes,
                    s.PackRockets,
                    s.PackSmokes,
                    s.PackGrenades,
                    s.PackUnderbarrel,
                    Math.Max(0, s.Tourniquets - 1),
                    s.Medkits,
                    s.PackF1,
                    s.PackRGN,
                    s.PackRGO,
                    s.PackRshg,
                },
            };
        }
    }
}
