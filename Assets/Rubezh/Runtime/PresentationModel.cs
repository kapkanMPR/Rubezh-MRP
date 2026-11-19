using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public enum SpeechMode
    {
        Compact,
        Full,
        Important,
        Off,
    }

    public static class SpeechPresentation
    {
        public static readonly string[] ModeNames = { "Кратко", "Полностью", "Важное", "Выключено" };

        public static string Name(SpeechMode mode)
        {
            return ModeNames[(int)mode].ToLowerInvariant();
        }

        public static IEnumerable<Soldier> Choose(
            BattleSimulation b,
            int selected,
            SpeechMode mode,
            int limit,
            Func<Soldier, bool> held = null
        )
        {
            if (mode == SpeechMode.Off)
                return Enumerable.Empty<Soldier>();
            return b
                .Soldiers.Where(s =>
                    s.Deployed
                    && s.Alive
                    && ((held != null && held(s)) || s.SpeechUntil > b.Time && !string.IsNullOrEmpty(s.Speech))
                    && (
                        s.Side == Side.Green
                            && (
                                (held != null && held(s))
                                || !(s.Id == b.Green.LeaderId && s.Speech == b.Green.CommanderOrder)
                            )
                            && (
                                mode != SpeechMode.Important
                                || (held != null && held(s))
                                || s.Id == selected
                                || s.SpeechUrgent
                                || s.Id == b.Green.LeaderId
                            )
                        || s.Side == Side.Red && s.RetreatQuoteUntil > b.Time
                    )
                )
                .OrderByDescending(s =>
                    ((held != null && held(s)) ? 20 : 0)
                    + (s.CasualtyRecovery.QuoteUntil > b.Time ? 16 : 0)
                    + (s.Side == Side.Red && s.RetreatQuoteUntil > b.Time ? 12 : 0)
                    + (s.Id == selected ? 8 : 0)
                    + (s.SpeechUrgent ? 4 : 0)
                    + (s.Id == b.Green.LeaderId ? 2 : 0)
                )
                .ThenByDescending(s => s.SpeechAt)
                .ThenBy(s => s.Id)
                .Take(limit);
        }
    }

    public static class InventoryPresentation
    {
        static void Add(List<string> parts, string name, int count)
        {
            if (count > 0)
                parts.Add(name + ": " + count);
        }

        public static string SpareWeapon(Soldier s)
        {
            if (HandLauncher.Active(s))
                return "В руках: " + (s.LauncherKind == 1 ? "РШГ" : "РПГ-26") + " · основное оружие на спине";
            if (!s.HasBackWeapon)
                return "";
            string name =
                s.BackGunRole == Role.Sniper ? "СВД"
                : s.BackGunRole == Role.MachineGunner ? "ПКМ"
                : s.BackVariant == 2 || s.BackVariant == 3 ? "АК + ГП"
                : "АК";
            return "На спине: " + name + " · " + s.BackAmmo + " патр. в магазине";
        }

        public static string Personal(Soldier s)
        {
            var p = new List<string>();
            p.Add(s.ArmorLevel < 0 ? "Без бронежилета" : BodyArmor.Names[Math.Max(0, Math.Min(2, s.ArmorLevel))]);
            if (s.Ammo + s.Reserve > 0)
                p.Add("БК: " + s.Ammo + " / " + s.Reserve);
            Add(p, "Жгут", SupplyRecovery.Medical(s) ? Math.Min(1, s.Tourniquets) : s.Tourniquets);
            if (s.Role == Role.Grenadier || s.Rockets > 0)
                p.Add((s.LauncherKind == 1 ? "РШГ" : "РПГ-26") + ": " + Math.Min(1, Math.Max(0, s.Rockets)) + "/1");
            Add(p, "Дым", s.SmokeCharges);
            Add(p, "РГД-5", GrenadePockets.Count(s, 1));
            Add(p, "Ф-1", GrenadePockets.Count(s, 3));
            Add(p, "РГН", GrenadePockets.Count(s, 4));
            Add(p, "РГО", GrenadePockets.Count(s, 5));
            Add(p, "Выстрелы ГП", GrenadePockets.Count(s, 2));
            return p.Count > 0 ? string.Join("\n", p) : "Личный запас израсходован";
        }

        public static string Backpack(Soldier s)
        {
            var p = new List<string>();
            string title;
            if (s.SupplyPack)
            {
                title = "РЮКЗАК БОЕКОМПЛЕКТА";
                Add(p, "Магазины", s.PackMagazines);
                Add(p, "Короба ПКМ", s.PackBoxes);
                Add(p, "Запасные РПГ-26", s.PackRockets);
                Add(p, "Запасные РШГ", s.PackRshg);
                Add(p, "Дымы", s.PackSmokes);
                Add(p, "РГД-5", s.PackGrenades);
                Add(p, "Ф-1", s.PackF1);
                Add(p, "РГН", s.PackRGN);
                Add(p, "РГО", s.PackRGO);
                Add(p, "Выстрелы ГП", s.PackUnderbarrel);
            }
            else if (SupplyRecovery.Medical(s))
            {
                title = "МЕДИЦИНСКИЙ РЮКЗАК";
                Add(p, "Жгуты", Math.Max(0, s.Tourniquets - 1));
                Add(p, "Аптечки", s.Medkits);
            }
            else
                return "";
            return title + "\n" + (p.Count > 0 ? string.Join("\n", p) : "Пусто");
        }
    }
}
