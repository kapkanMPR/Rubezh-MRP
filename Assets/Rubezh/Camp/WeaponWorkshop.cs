using System;
using System.Collections.Generic;
using System.Linq;

namespace Rubezh.Camp
{
    [Serializable]
    public sealed class WeaponInstance
    {
        public string id,
            item,
            owner;
        public int initialRarity,
            rarity,
            level = 1,
            xp;

        // Preview resources are never real inventory investments and cannot be refunded.
        public bool freeUpgrade = false;
        public int[] paidCloth = new int[4];
        public int paidParts;
        public List<WeaponInstance> paidCopies = new List<WeaponInstance>();
    }

    public sealed partial class CampModel
    {
        public const bool FreeWeaponWorkshop = false;

        public static int WeaponInitialRarity(string item) => LootCrates.Foreign(item) ? 2 : 1;

        public static int WeaponCap(int rarity) =>
            rarity == 1 ? 20
            : rarity == 2 ? 30
            : 40;

        public static int WeaponXpCost(int level) => 50 + level * 10;

        public void SyncWeaponInstances()
        {
            if (S.weaponInstances == null)
                S.weaponInstances = new List<WeaponInstance>();
            foreach (var weapon in S.weaponInstances)
                if (
                    weapon.owner != null
                    && (!S.gear.ContainsKey(weapon.owner) || S.gear[weapon.owner].weapon != weapon.item)
                )
                    weapon.owner = null;
            foreach (var item in Data.items.Where(x => x.type == "weapon"))
            {
                var copies = S.weaponInstances.Where(x => x.item == item.id).ToList();
                while (copies.Count < Owned(item.id))
                {
                    var weapon = new WeaponInstance
                    {
                        id = Guid.NewGuid().ToString("N"),
                        item = item.id,
                        initialRarity = WeaponInitialRarity(item.id),
                        rarity = WeaponInitialRarity(item.id),
                    };
                    copies.Add(weapon);
                    S.weaponInstances.Add(weapon);
                }
                while (copies.Count > Owned(item.id))
                {
                    var weapon = copies
                        .Where(x => x.owner == null)
                        .OrderBy(x => x.rarity)
                        .ThenBy(x => x.level)
                        .FirstOrDefault();
                    if (weapon == null)
                        break;
                    copies.Remove(weapon);
                    S.weaponInstances.Remove(weapon);
                }
            }
            foreach (var pair in S.gear.OrderBy(x => x.Key))
            {
                if (string.IsNullOrEmpty(pair.Value.weapon))
                    continue;
                if (S.weaponInstances.Any(weapon => weapon.owner == pair.Key && weapon.item == pair.Value.weapon))
                    continue;
                var free = S.weaponInstances.FirstOrDefault(weapon =>
                    weapon.owner == null && weapon.item == pair.Value.weapon
                );
                if (free != null)
                    free.owner = pair.Key;
            }
        }

        public WeaponInstance FighterWeapon(string owner)
        {
            SyncWeaponInstances();
            return S.weaponInstances.FirstOrDefault(weapon => weapon.owner == owner);
        }

        public WeaponInstance WorkshopWeapon(string id)
        {
            SyncWeaponInstances();
            var weapon = S.weaponInstances.FirstOrDefault(x => x.id == id);
            if (weapon == null)
                throw new InvalidOperationException("Экземпляр больше недоступен");
            return weapon;
        }

        public void SellWeaponInstance(string id)
        {
            var weapon = WorkshopWeapon(id);
            Check(
                weapon.owner == null && CanSellStock(weapon.item),
                "Продать можно только свободное оружие, оставив минимум 5 образцов"
            );
            Check(Owned(weapon.item) > MaxSquadUse(weapon.item), "Этот экземпляр нужен комплекту другого отряда");
            S.weaponInstances.Remove(weapon);
            S.inventory[weapon.item]--;
            S.balance += Data.Item(weapon.item).price * .75;
        }

        void RemoveWeaponInstanceForSale(string item)
        {
            SyncWeaponInstances();
            var weapon = S
                .weaponInstances.Where(x => x.item == item && x.owner == null)
                .OrderBy(x => x.rarity)
                .ThenBy(x => x.level)
                .FirstOrDefault();
            if (weapon == null)
                throw new InvalidOperationException("Нет свободного экземпляра");
            S.weaponInstances.Remove(weapon);
        }

        public static WeaponInstance MaintenancePreview(WeaponInstance w, int experience)
        {
            var next = new WeaponInstance
            {
                id = w.id,
                item = w.item,
                owner = w.owner,
                initialRarity = w.initialRarity,
                rarity = w.rarity,
                level = w.level,
                xp = w.xp,
            };
            int pool = Math.Max(0, experience) + next.xp;
            while (next.level < WeaponCap(next.rarity) && pool >= WeaponXpCost(next.level))
            {
                pool -= WeaponXpCost(next.level);
                next.level++;
            }
            next.xp = next.level == WeaponCap(next.rarity) ? 0 : pool;
            return next;
        }

        public static int MaintenanceRemaining(WeaponInstance w)
        {
            if (w.level >= WeaponCap(w.rarity))
                return 0;
            int remaining = -w.xp;
            for (int level = w.level; level < WeaponCap(w.rarity); level++)
                remaining += WeaponXpCost(level);
            return Math.Max(0, remaining);
        }

        public void ServiceWeapon(string id, int experience)
        {
            if (experience <= 0 || experience > 100000)
                throw new InvalidOperationException("Выберите материалы обслуживания");
            var weapon = WorkshopWeapon(id);
            if (experience > MaintenanceRemaining(weapon))
                throw new InvalidOperationException("Опыт превышает остаток до предела уровня");
            int cloth = (experience + 24) / 25;
            Check(S.raid.cloth >= cloth, "Недостаточно ветоши");
            var next = MaintenancePreview(weapon, experience);
            if (weapon.paidCloth == null || weapon.paidCloth.Length != 4)
                weapon.paidCloth = new int[4];
            S.raid.cloth -= cloth;
            weapon.paidCloth[weapon.rarity] += cloth;
            weapon.level = next.level;
            weapon.xp = next.xp;
        }

        public List<WeaponInstance> TuningCopies(string id)
        {
            var weapon = WorkshopWeapon(id);
            return S.weaponInstances.Where(x => x.id != id && x.item == weapon.item && x.owner == null).ToList();
        }

        public void TuneWeapon(string id, string[] duplicateIds = null, bool confirmReset = false)
        {
            var weapon = WorkshopWeapon(id);
            Check(weapon.rarity < 3, "Максимальная редкость");
            Check(weapon.level == WeaponCap(weapon.rarity), "Сначала достигните предельного уровня");
            int count = weapon.rarity == 1 ? 2 : 3,
                parts = weapon.rarity == 1 ? 4 : 6;
            Check(S.raid.parts >= parts, "Недостаточно запчастей");
            Check(
                duplicateIds != null && duplicateIds.Length == count && duplicateIds.Distinct().Count() == count,
                "Выберите дубликаты"
            );
            var available = TuningCopies(id);
            var copies = duplicateIds
                .Select(copyId => available.FirstOrDefault(candidate => candidate.id == copyId))
                .ToList();
            Check(copies.All(x => x != null), "Дубликат недоступен или выдан бойцу");
            Check(
                S.weaponInstances.Count - count >= 5 && Owned(weapon.item) - count >= MaxSquadUse(weapon.item),
                "Необходимо оставить минимум 5 образцов и оружие для отрядов"
            );
            Check(
                confirmReset || copies.All(x => x.level == 1 && x.rarity == x.initialRarity && x.xp == 0),
                "Улучшения дубликатов будут сброшены. Подтвердите расход"
            );
            if (weapon.paidCopies == null)
                weapon.paidCopies = new List<WeaponInstance>();
            foreach (var copy in copies)
            {
                ResetWeapon(copy.id, true);
                S.weaponInstances.Remove(copy);
                S.inventory[copy.item]--;
                weapon.paidCopies.Add(copy);
            }
            S.raid.parts -= parts;
            weapon.paidParts += parts;
            weapon.rarity++;
            weapon.xp = 0;
        }

        public void ResetWeapon(string id, bool rarity)
        {
            var weapon = WorkshopWeapon(id);
            if (weapon.paidCloth != null)
                for (int i = 0; i < weapon.paidCloth.Length; i++)
                    if (rarity || i == weapon.rarity)
                    {
                        S.raid.cloth += weapon.paidCloth[i];
                        weapon.paidCloth[i] = 0;
                    }
            if (rarity)
            {
                S.raid.parts += weapon.paidParts;
                weapon.paidParts = 0;
                if (weapon.paidCopies != null)
                {
                    foreach (var copy in weapon.paidCopies)
                    {
                        copy.owner = null;
                        S.weaponInstances.Add(copy);
                        S.inventory[copy.item]++;
                    }
                    weapon.paidCopies.Clear();
                }
                weapon.rarity = weapon.initialRarity;
            }
            weapon.level =
                weapon.rarity == weapon.initialRarity ? 1
                : weapon.rarity == 2 ? 20
                : 30;
            weapon.xp = 0;
        }
    }
}
