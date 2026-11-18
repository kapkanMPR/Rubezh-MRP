using System;
using System.Linq;

namespace Rubezh.UnityPort
{
    public sealed class EquipmentUnlock
    {
        public readonly string Kind, Name, Detail;
        public readonly int Id, After;
        public EquipmentUnlock(string kind, int id, int after, string name, string detail)
        {
            Kind = kind;
            Id = id;
            After = after;
            Name = name;
            Detail = detail;
        }
    }

    public static class CampaignUnlocks
    {
        public static readonly string[] Weapons =
        {
            "АК-74 с прицелом",
            "ПКМ",
            "АК-74 + подствольник",
            "СВД"
        };
        public static readonly string[] Extras =
        {
            "Без оборудования",
            "РПГ-26",
            "Медицинский рюкзак",
            "Рюкзак с боекомплектом",
            "РШГ"
        };
        public static readonly string[] Grenades =
        {
            "Дым",
            "РГД-5",
            "Выстрел подствольника",
            "Ф-1",
            "РГН",
            "РГО"
        };
        public static readonly EquipmentUnlock[] Items = Build();
        static EquipmentUnlock[] Build()
        {
            var items = new System.Collections.Generic.List<EquipmentUnlock>
            {
                new EquipmentUnlock("weapon", 1, 4, Weapons[1], "Основное оружие · пулемёт"),
                new EquipmentUnlock("weapon", 2, 7, Weapons[2], "Основное оружие · автомат с гранатомётом"),
                new EquipmentUnlock("weapon", 4, 8, Weapons[3], "Основное оружие · снайперская винтовка"),
                new EquipmentUnlock("extra", 1, 10, Extras[1], "Дополнительное оборудование · гранатомёт"),
                new EquipmentUnlock("extra", 2, 1, Extras[2], "Дополнительное оборудование · помощь раненым"),
                new EquipmentUnlock("extra", 3, 3, Extras[3], "Дополнительное оборудование · снабжение отряда"),
                new EquipmentUnlock("extra", 4, 14, Extras[4], "Дополнительное оборудование · гранатомёт"),
                new EquipmentUnlock("grenade", 1, 2, Grenades[1], "Ручная осколочная граната"),
                new EquipmentUnlock("grenade", 4, 11, Grenades[4], "Ручная граната: взрыв при падении"),
                new EquipmentUnlock("grenade", 5, 18, Grenades[5], "Усиленная осколочная граната: взрыв при падении"),
                new EquipmentUnlock("grenade", 3, 9, Grenades[3], "Ручная граната с усиленным осколочным действием"),
                new EquipmentUnlock("grenade", 2, 7, Grenades[2], "Боеприпас для АК-74 с подствольником"),
                new EquipmentUnlock("rig", 1, 3, "Средняя разгрузка · уровень 2", "Увеличенный запас боеприпасов и второй слот гранат"),
                new EquipmentUnlock("rig", 2, 11, "Тяжёлая разгрузка · уровень 3", "Больше боеприпасов и третий слот гранат")
            };
            items.Add(new EquipmentUnlock("armor", 1, 7, "Армейский бронежилет", "Броня уровня 2"));
            items.Add(new EquipmentUnlock("armor", 2, 18, "Штурмовой бронежилет", "Броня уровня 3"));
            for (int t = 1; t <= 3; t++)
                items.Add(new EquipmentUnlock("training", t, SilentOrders.Core.CampaignProgression.TrainingAfter(t), "Подготовка отряда · уровень " + t, "Все четыре направления подготовки"));
            for (int t = 1; t <= 3; t++)
                items.Add(new EquipmentUnlock("combatTraining", t, new[] { 0, 6, 14, 17 }[t], "Боевая тренировка · уровень " + t, "Новые навыки группы · покупка необратима"));
            int[] after =
            {
                3,
                4,
                10,
                3,
                7,
                7,
                1,
                6,
                9,
                11,
                18,
                14
            };
            for (int i = 0; i < after.Length; i++)
                items.Add(new EquipmentUnlock("bag", i, after[i], UiCatalog.BagNames[i], i == 6 || i == 7 ? "Содержимое медицинского рюкзака" : "Запас для рюкзака снабжения"));
            return items.OrderBy(i => i.After).ToArray();
        }

        public static int Required(string kind, int id)
        {
            var item = Items.FirstOrDefault(i => i.Kind == kind && i.Id == id);
            return item == null ? 0 : item.After;
        }

        public static EquipmentUnlock[] Between(int before, int after)
        {
            return after <= before ? new EquipmentUnlock[0] : Items.Where(i => i.After > before && i.After <= after).ToArray();
        }
    }
}
