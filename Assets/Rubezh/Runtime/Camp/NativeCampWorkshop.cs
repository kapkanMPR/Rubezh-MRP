using System;
using System.Linq;
using Rubezh.Camp;
using SilentOrders.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        string workshopCurrent;
        string[] workshopCopies = new string[3];

        int[] workshopMaterials = new int[3];
        static readonly int[] WorkshopXp = { 25, 100, 500 };
        static readonly string[] WorkshopMaterials = { "Ветошь", "Оружейное масло", "Набор обслуживания" };

        static string WorkshopRank(int r) =>
            r == 1 ? "Обычное"
            : r == 2 ? "Редкое"
            : "Эпическое";

        static int WorkshopVariant(string id) =>
            id == "pkm" ? 1
            : id == "gp" ? 2
            : id == "svd" ? 4
            : id == "m4" ? 6
            : id == "m249" ? 7
            : id == "m4gp" ? 9
            : id == "m24" ? 11
            : 0;

        static float WorkshopAccuracy(string item, int rarity, int level)
        {
            var soldier = EquipmentDetails.Gun(item);
            soldier.WeaponUpgrade = WeaponProgression.At(WorkshopVariant(item), rarity, level);
            return Ballistics.NeutralAccuracy(new Tuning(), soldier);
        }

        static void WorkshopColor(VisualElement card, int rarity)
        {
            var edge =
                rarity >= 3 ? new Color(.65f, .4f, .85f)
                : rarity == 2 ? new Color(.3f, .55f, .9f)
                : new Color(.31f, .6f, .34f);
            card.style.backgroundColor =
                rarity >= 3 ? new Color(.18f, .10f, .24f)
                : rarity == 2 ? new Color(.09f, .14f, .24f)
                : new Color(.09f, .18f, .12f);
            card.style.borderTopColor =
                card.style.borderBottomColor =
                card.style.borderLeftColor =
                card.style.borderRightColor =
                    edge;
            card.style.borderTopWidth =
                card.style.borderBottomWidth =
                card.style.borderLeftWidth =
                card.style.borderRightWidth =
                    2;
        }

        void WarehouseWeaponPanel(VisualElement content, VisualElement actions, WeaponInstance w)
        {
            var item = M.Data.Item(w.item);
            WorkshopArt(content, item, 140, w.rarity);
            Label(content, item.name, "h2");
            Stat(content, "Уровень", w.level + " / " + CampModel.WeaponCap(w.rarity));
            Stat(content, "Редкость", WorkshopRank(w.rarity));
            Stat(content, "Владелец", w.owner == null ? "Склад" : M.Soldier(w.owner).name);
            var spec = WeaponProgression.At(WorkshopVariant(w.item), w.rarity, w.level).Spec;
            float extra = WorkshopVariant(w.item) % 6 >= 4 ? 5 : 0;
            Stat(
                content,
                "Урон",
                (18 * spec.Damage + spec.DamageBonus + extra).ToString("0.##")
                    + "–"
                    + (30 * spec.Damage + spec.DamageBonus + extra).ToString("0.##")
            );
            Stat(content, "Точность", (100 * WorkshopAccuracy(w.item, w.rarity, w.level)).ToString("0.#") + "%");
            Button(actions, "Улучшить", () => WeaponWorkshop(w.id, false), "gold");
            Button(
                    actions,
                    "Продать · ◈ " + Money(item.price * .75),
                    () => Change(() => M.SellWeaponInstance(w.id), () => Toast("Предмет продан"))
                )
                .SetEnabled(w.owner == null && M.CanSellStock(w.item));
        }

        void WorkshopInstanceDetails(string instance)
        {
            var w = M.WorkshopWeapon(instance);
            SaveView();
            Modal(
                "Карточка оружия",
                (c, f) =>
                {
                    var item = M.Data.Item(w.item);
                    WorkshopArt(c, item, 150, w.rarity);
                    Label(c, item.name, "h2");
                    Stat(c, "Уровень", w.level + " / " + CampModel.WeaponCap(w.rarity));
                    Stat(c, "Редкость", WorkshopRank(w.rarity));
                    Stat(c, "Владелец", w.owner == null ? "Склад" : M.Soldier(w.owner).name);
                    var p = WeaponProgression.At(WorkshopVariant(w.item), w.rarity, w.level).Spec;
                    float extra = WorkshopVariant(w.item) % 6 >= 4 ? 5 : 0;
                    Stat(
                        c,
                        "Урон",
                        (18 * p.Damage + p.DamageBonus + extra).ToString("0.##")
                            + "–"
                            + (30 * p.Damage + p.DamageBonus + extra).ToString("0.##")
                    );
                    Stat(c, "Точность", (WorkshopAccuracy(w.item, w.rarity, w.level) * 100).ToString("0.#") + "%");
                    Button(f, "Улучшить", () => WeaponWorkshop(instance, false), "gold");
                }
            );
        }

        void WorkshopPicker(string itemId)
        {
            M.SyncWeaponInstances();
            SaveView();
            Modal(
                "Выберите экземпляр",
                (c, f) =>
                {
                    foreach (var w in S.weaponInstances.Where(x => x.item == itemId))
                    {
                        var copy = w;
                        Button(
                            c,
                            M.Data.Item(w.item).name
                                + " · ур. "
                                + w.level
                                + " · "
                                + WorkshopRank(w.rarity)
                                + (w.owner == null ? " · склад" : " · " + M.Soldier(w.owner).name),
                            () => WeaponWorkshop(copy.id, false)
                        );
                    }
                }
            );
        }

        void WorkshopArt(VisualElement parent, Item item, int size, int rarity = 1)
        {
            var card = V("drop-card");
            parent.Add(card);
            card.style.width = size;
            card.style.height = size;
            card.style.flexShrink = 0;
            card.style.alignSelf = Align.Center;
            card.style.marginRight = 0;
            card.style.marginBottom = 4;
            WorkshopColor(card, rarity);
            var art = OutfitWeaponArt(item, rarity);
            card.Add(art);
            art.style.width = size - 16;
            art.style.height = size - 16;
            art.scaleMode = ScaleMode.ScaleToFit;
        }

        void WeaponWorkshop(string instance, bool tuning)
        {
            var w = M.WorkshopWeapon(instance);
            var item = M.Data.Item(w.item);
            if (workshopCurrent != instance)
            {
                workshopCurrent = instance;
                workshopMaterials = new int[3];
                workshopCopies = new string[3];
            }
            Close();
            reopenModal = () => WeaponWorkshop(instance, tuning);
            overlay = V("modal-overlay");
            root.Add(overlay);
            var box = V("modal workshop");
            overlay.Add(box);
            // Fit in the safe area even when the global UI scale is set to 300%.
            float fit = Mathf.Min(1, Mathf.Min((vw - 16) / 620f, (vh - 16) / 360f));
            fit = Mathf.Max(.1f, fit);
            float width = Mathf.Min(1000, (vw - 16) / fit),
                height = Mathf.Min(540, (vh - 16) / fit);
            bool compact = height < 450;
            box.EnableInClassList("workshop-compact", compact);
            box.style.width = box.style.maxWidth = width;
            box.style.height = box.style.maxHeight = height;
            box.style.flexShrink = 0;
            box.style.scale = new Scale(new Vector3(fit, fit, 1));
            var title = Row(box, "workshop-head");
            Label(title, "УЛУЧШЕНИЕ · " + item.name, "modal-title grow");
            Button(title, "×", Close);
            Label(box, "Материалы и дубликаты расходуются со склада", "sub");
            var body = Row(box, "grow workshop-body");
            body.style.minHeight = 0;
            body.style.alignItems = Align.Stretch;
            var nav = V("workshop-nav");
            body.Add(nav);
            nav.style.width = width < 650 ? 115 : 150;
            nav.style.flexShrink = 0;
            Button(
                nav,
                "Обслуживание",
                () =>
                {
                    WeaponWorkshop(instance, false);
                },
                !tuning ? "gold" : ""
            );
            Button(nav, "Тюнинг", () => WeaponWorkshop(instance, true), tuning ? "gold" : "");
            Label(nav, "Откат бесплатный", "sub");
            Button(nav, "Откат уровня", () => WorkshopReset(instance, false));
            Button(nav, "Откат редкости", () => WorkshopReset(instance, true));
            var center = V("workshop-center");
            body.Add(center);
            center.style.flexGrow = 1;
            center.style.flexBasis = 0;
            center.style.minWidth = 0;
            var right = V("workshop-right");
            body.Add(right);
            right.style.flexGrow = 1;
            right.style.flexBasis = 0;
            right.style.minWidth = 0;
            workshopMaterials[0] = Math.Min(
                workshopMaterials[0],
                Math.Min(S.raid.cloth, (CampModel.MaintenanceRemaining(w) + 24) / 25)
            );
            int experience = Math.Min(CampModel.MaintenanceRemaining(w), 25 * workshopMaterials[0]);
            var next = CampModel.MaintenancePreview(w, experience);
            if (tuning)
            {
                next.level = w.level;
                next.rarity = Math.Min(3, w.rarity + 1);
                next.xp = 0;
            }
            var currentSpec = WeaponProgression.At(WorkshopVariant(w.item), w.rarity, w.level).Spec;
            var nextSpec = WeaponProgression.At(WorkshopVariant(w.item), next.rarity, next.level).Spec;
            Label(center, "ИЗМЕНЕНИЯ", "h2");
            if (next.level != w.level)
                Stat(center, "Уровень", w.level + " → " + next.level);
            if (next.rarity != w.rarity)
                Stat(center, "Редкость", WorkshopRank(w.rarity) + " → " + WorkshopRank(next.rarity));
            if (tuning && w.rarity < 3)
                Stat(center, "Предел уровня", CampModel.WeaponCap(w.rarity) + " → " + CampModel.WeaponCap(next.rarity));
            float extra = WorkshopVariant(w.item) % 6 >= 4 ? 5 : 0;
            WorkshopDifference(
                center,
                "Урон минимум",
                18 * currentSpec.Damage + currentSpec.DamageBonus + extra,
                18 * nextSpec.Damage + nextSpec.DamageBonus + extra
            );
            WorkshopDifference(
                center,
                "Урон максимум",
                30 * currentSpec.Damage + currentSpec.DamageBonus + extra,
                30 * nextSpec.Damage + nextSpec.DamageBonus + extra
            );
            WorkshopDifference(
                center,
                "Точность",
                WorkshopAccuracy(w.item, w.rarity, w.level),
                WorkshopAccuracy(w.item, next.rarity, next.level)
            );
            if (!tuning && experience == 0)
                Label(center, "Выберите материалы для сравнения характеристик.", "paragraph");
            if (tuning)
            {
                int copies = next.rarity == 2 ? 2 : 3,
                    parts = next.rarity == 2 ? 4 : 6;
                Label(right, "Запчасти · требуется " + parts, "sub");
                var partsSlot = Button(
                    right,
                    S.raid.parts + " / " + parts,
                    () => Toast("Запчасти будут списаны при тюнинге")
                );
                partsSlot.style.width = compact ? 42 : 56;
                partsSlot.style.height = compact ? 36 : 48;
                partsSlot.style.alignSelf = Align.Center;
                partsSlot.style.marginRight = 0;
                partsSlot.style.marginBottom = 4;
                partsSlot.style.fontSize = 12;
                WorkshopArt(right, item, compact ? 60 : 88, w.rarity);
                Label(right, WorkshopRank(w.rarity) + " · ур. " + w.level, "sub");
                Label(right, "ДУБЛИКАТЫ", "sub");
                var slots = Row(right, "");
                slots.style.justifyContent = Justify.Center;
                slots.style.marginBottom = 4;
                for (int i = 0; i < copies; i++)
                {
                    int n = i;
                    var chosen = M.TuningCopies(instance).FirstOrDefault(x => x.id == workshopCopies[n]);
                    var slot = Button(
                        slots,
                        chosen == null ? "+" : "Ур. " + chosen.level,
                        () => WorkshopDuplicatePicker(instance, n)
                    );
                    slot.style.width = compact ? 36 : 44;
                    slot.style.height = compact ? 32 : 40;
                    slot.style.flexShrink = 0;
                    slot.style.marginRight = i == copies - 1 ? 0 : 8;
                }

                Button(
                        right,
                        w.rarity >= 3 ? "Максимальная редкость" : "Тюнинг",
                        () => WorkshopTune(instance, copies),
                        "gold"
                    )
                    .SetEnabled(
                        w.rarity < 3
                            && w.level == CampModel.WeaponCap(w.rarity)
                            && S.raid.parts >= parts
                            && workshopCopies.Take(copies).All(x => x != null)
                    );
                if (w.rarity < 3 && w.level < CampModel.WeaponCap(w.rarity))
                    Label(right, "Требуется уровень " + CampModel.WeaponCap(w.rarity), "sub");
            }
            else
            {
                WorkshopArt(right, item, compact ? 60 : 88, w.rarity);
                Label(
                    right,
                    WorkshopRank(w.rarity) + " · ур. " + w.level + " / " + CampModel.WeaponCap(w.rarity),
                    "workshop-rank"
                );
                Label(
                    right,
                    "Опыт: "
                        + w.xp
                        + " / "
                        + CampModel.WeaponXpCost(w.level)
                        + (experience > 0 ? " → ур. " + next.level + ", " + next.xp + " опыта" : ""),
                    "paragraph"
                );
                var track = V("");
                right.Add(track);
                track.style.height = 10;
                track.style.backgroundColor = new Color(.12f, .17f, .14f);
                var fill = V("");
                track.Add(fill);
                fill.style.height = 10;
                fill.style.width = Length.Percent(
                    next.level >= CampModel.WeaponCap(next.rarity)
                        ? 100
                        : 100f * next.xp / CampModel.WeaponXpCost(next.level)
                );
                fill.style.backgroundColor = new Color(.7f, .75f, .4f);
                {
                    for (int i = 0; i < 1; i++)
                    {
                        int n = i;
                        Label(
                            right,
                            WorkshopMaterials[i] + " · " + WorkshopXp[i] + " опыта · на складе: " + S.raid.cloth,
                            "sub"
                        );
                        var line = Row(right, "");
                        Button(
                            line,
                            "−",
                            () =>
                            {
                                workshopMaterials[n] = Math.Max(0, workshopMaterials[n] - 1);
                                WeaponWorkshop(instance, false);
                            }
                        );
                        Label(line, workshopMaterials[i].ToString(), "grow");
                        Button(
                                line,
                                "+",
                                () =>
                                {
                                    if (
                                        workshopMaterials[n] < S.raid.cloth
                                        && experience < CampModel.MaintenanceRemaining(w)
                                    )
                                        workshopMaterials[n]++;
                                    WeaponWorkshop(instance, false);
                                }
                            )
                            .SetEnabled(
                                workshopMaterials[i] < S.raid.cloth && experience < CampModel.MaintenanceRemaining(w)
                            );
                    }
                    Label(right, "+" + experience + " опыта · до предела: " + CampModel.MaintenanceRemaining(w), "sub");
                    Button(
                            right,
                            "Применить",
                            () =>
                                Change(
                                    () => M.ServiceWeapon(instance, experience),
                                    () =>
                                    {
                                        workshopMaterials = new int[3];
                                        WeaponWorkshop(instance, false);
                                    }
                                ),
                            "gold"
                        )
                        .SetEnabled(experience > 0 && w.level < CampModel.WeaponCap(w.rarity));
                }
            }
        }

        void WorkshopDuplicatePicker(string id, int slot)
        {
            Modal(
                "Выберите дубликат",
                (c, f) =>
                {
                    Button(
                        c,
                        "Убрать из слота",
                        () =>
                        {
                            workshopCopies[slot] = null;
                            WeaponWorkshop(id, true);
                        }
                    );
                    foreach (var copy in M.TuningCopies(id).Where(x => !workshopCopies.Contains(x.id)))
                    {
                        var candidate = copy;
                        Button(
                            c,
                            M.Data.Item(copy.item).name + " · " + WorkshopRank(copy.rarity) + " · ур. " + copy.level,
                            () =>
                            {
                                workshopCopies[slot] = candidate.id;
                                WeaponWorkshop(id, true);
                            }
                        );
                    }
                    Button(f, "Назад", () => WeaponWorkshop(id, true));
                }
            );
        }

        void WorkshopTune(string id, int count)
        {
            var ids = workshopCopies.Take(count).ToArray();
            Modal(
                "Подтвердите тюнинг",
                (c, f) =>
                {
                    Label(
                        c,
                        "Будут потрачены запчасти и выбранные дубликаты. Улучшенные копии сначала сбросятся с возвратом реальных вложений.",
                        "paragraph"
                    );
                    Button(f, "Отмена", () => WeaponWorkshop(id, true));
                    Button(
                        f,
                        "Подтвердить",
                        () =>
                            Change(
                                () => M.TuneWeapon(id, ids, true),
                                () =>
                                {
                                    workshopCopies = new string[3];
                                    workshopMaterials = new int[3];
                                    WeaponWorkshop(id, true);
                                }
                            ),
                        "gold"
                    );
                }
            );
        }

        void WorkshopDifference(VisualElement parent, string name, float from, float to)
        {
            if (Math.Abs(from - to) < .0001f)
                return;
            var row = Row(parent, "");
            Label(row, name, "grow");
            bool percent = name == "Точность";
            var value = Label(
                row,
                (percent ? from * 100 : from).ToString("0.##")
                    + (percent ? "%" : "")
                    + " → "
                    + (percent ? to * 100 : to).ToString("0.##")
                    + (percent ? "%" : ""),
                "sub"
            );
            value.style.color = new Color(.6f, .85f, .5f);
        }

        void WorkshopReset(string id, bool rarity)
        {
            Modal(
                rarity ? "Откат редкости" : "Откат уровня",
                (c, f) =>
                {
                    Label(
                        c,
                        "Откат бесплатный. Реально вложенные материалы возвращаются; при откате редкости также возвращаются запчасти и потраченные копии. За прежние тестовые улучшения возврата нет.",
                        "paragraph"
                    );
                    Button(f, "Отмена", () => WeaponWorkshop(id, rarity));
                    Button(
                        f,
                        "Подтвердить откат",
                        () =>
                            Change(
                                () => M.ResetWeapon(id, rarity),
                                () =>
                                {
                                    workshopMaterials = new int[3];
                                    WeaponWorkshop(id, rarity);
                                }
                            ),
                        "gold"
                    );
                }
            );
        }
    }
}
