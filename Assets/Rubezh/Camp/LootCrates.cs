using System;
using System.Collections.Generic;
using System.Linq;

namespace Rubezh.Camp
{
    public sealed class LootEntry
    {
        public Item Item;
        public int Weight;
        public double Probability;
        public int DisplayHundredths;
    }

    public sealed class LootCrate
    {
        public readonly string Id,
            Name,
            Art;
        public readonly int Price;
        public readonly string[] Items;
        public readonly int[] Weights;

        public LootCrate(string id, string name, string art, int price, string[] items, int[] weights)
        {
            Id = id;
            Name = name;
            Art = art;
            Price = price;
            Items = items;
            Weights = weights;
        }
    }

    [Serializable]
    public sealed class LootReceipt
    {
        public string id,
            crate,
            item;
        public int price;
        public double probability;
        public bool acknowledged;
    }

    public static class LootCrates
    {
        public static readonly LootCrate[] All =
        {
            new LootCrate(
                "weapon",
                "Оружейный кейс",
                "weapon",
                300,
                new[] { "rshg", "svd", "rpg", "pkm", "gp", "ak" },
                new[] { 8, 12, 15, 18, 22, 25 }
            ),
            new LootCrate(
                "foreign",
                "Зарубежное вооружение",
                "weapon",
                400,
                new[] { "m24", "m249", "m4gp", "m4" },
                new[] { 15, 20, 25, 40 }
            ),
            new LootCrate(
                "equipment",
                "Ящик снаряжения",
                "equipment",
                260,
                new[] { "a3", "v3", "a2", "medbag", "supply", "v2", "a1", "v1" },
                new[] { 5, 8, 12, 12, 12, 15, 20, 16 }
            ),
        };

        public static bool Foreign(string id) => id == "m4" || id == "m4gp" || id == "m249" || id == "m24";

        public static bool Direct(Item item) =>
            item != null && (item.type == "grenade" || item.type == "medical" || item.type == "ammo");

        public static void ApplyRarity(Catalog data)
        {
            foreach (var item in data.items)
            {
                if (Foreign(item.id))
                    item.rarity = "Редкое";
                else if (item.type == "weapon")
                    item.rarity = "Обычное";
                if (item.id == "ak")
                    item.name = "АК-74";
            }
        }

        public static LootCrate Find(string id) => All.FirstOrDefault(c => c.Id == id);

        public static List<LootEntry> Pool(CampModel model, LootCrate crate)
        {
            if (crate == null)
                throw new ArgumentException("Неизвестный ящик");
            var pool = new List<LootEntry>();
            for (int i = 0; i < crate.Items.Length; i++)
            {
                var item = model.Data.Item(crate.Items[i]);
                if (item != null && !CampModel.Personal(item.id) && model.Unlocked(item))
                    pool.Add(new LootEntry { Item = item, Weight = crate.Weights[i] });
            }
            int total = pool.Sum(e => e.Weight);
            if (total == 0)
                return pool;
            foreach (var e in pool)
            {
                e.Probability = (double)e.Weight / total;
                e.DisplayHundredths = (int)Math.Floor(e.Probability * 10000);
            }
            int remainder = 10000 - pool.Sum(e => e.DisplayHundredths);
            foreach (
                var e in pool.OrderByDescending(e => e.Probability * 10000 - e.DisplayHundredths)
                    .ThenBy(e => e.Item.id)
                    .Take(remainder)
            )
                e.DisplayHundredths++;
            return pool.OrderBy(e => e.Probability).ThenBy(e => e.Item.id).ToList();
        }

        public static LootEntry Pick(List<LootEntry> pool, double roll)
        {
            if (pool == null || pool.Count == 0)
                throw new InvalidOperationException("В ящике пока нет открытых предметов");
            if (double.IsNaN(roll) || roll < 0 || roll >= 1)
                throw new ArgumentOutOfRangeException(nameof(roll));
            double target = roll * pool.Sum(e => e.Weight);
            foreach (var e in pool)
            {
                target -= e.Weight;
                if (target < 0)
                    return e;
            }
            return pool[pool.Count - 1];
        }
    }

    public sealed partial class CampModel
    {
        // Must be called through CampStore.Change so payment, award and receipt commit together.
        public LootReceipt BuyLootCrate(string crateId)
        {
            var crate = LootCrates.Find(crateId);
            Check(crate != null, "Неизвестный ящик");
            Check(
                S.lootReceipts == null || !S.lootReceipts.Any(r => !r.acknowledged),
                "Сначала посмотри предыдущую награду"
            );
            var pool = LootCrates.Pool(this, crate);
            Check(pool.Count > 0, "Сначала открой хотя бы один предмет этого ящика");
            Check(S.balance >= crate.Price, "Недостаточно очков снабжения");
            var winner = LootCrates.Pick(pool, random());
            int count = Owned(winner.Item.id);
            Check(count < int.MaxValue, "Склад заполнен");
            var receipt = new LootReceipt
            {
                id = Guid.NewGuid().ToString("N"),
                crate = crateId,
                item = winner.Item.id,
                price = crate.Price,
                probability = winner.Probability,
            };
            if (S.lootReceipts == null)
                S.lootReceipts = new List<LootReceipt>();
            // Keep unacknowledged awards; they can be shown again after restarting.
            while (S.lootReceipts.Count >= 100)
            {
                int old = S.lootReceipts.FindIndex(r => r.acknowledged);
                if (old < 0)
                    break;
                S.lootReceipts.RemoveAt(old);
            }
            S.balance -= crate.Price;
            S.inventory[winner.Item.id] = count + 1;
            S.lootReceipts.Add(receipt);
            return receipt;
        }
    }
}
