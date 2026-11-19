using System;
using System.Collections.Generic;
using System.Linq;
using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        void Currencies()
        {
            Modal(
                "Валюты лагеря",
                (c, f) =>
                {
                    CurrencyEntry(c, "Очки обеспечения", Money(S.balance), "Необходимы для покупок в магазине.");
                    CurrencyEntry(
                        c,
                        "Свободный опыт",
                        S.career.freeXp.ToString(),
                        "Необходим для повышения уровня бойцов."
                    );
                    CurrencyEntry(
                        c,
                        "Методические указания",
                        S.career.manuals.ToString(),
                        "Необходимы для прокачивания навыков бойцов."
                    );
                },
                "currency-drawer"
            );
        }

        void CurrencyEntry(VisualElement parent, string title, string amount, string description)
        {
            var entry = V("currency-entry");
            parent.Add(entry);
            Label(entry, title, "currency-name");
            Label(entry, amount, "currency-amount");
            Label(entry, description, "paragraph");
        }

        void ItemDescription(VisualElement c, Item item, string owner = null)
        {
            var data = EquipmentDetails.Build(M, item, owner);
            var header = Row(c, "equipment-header");
            WeaponRarityCard(header, item);
            header.Add(
                OutfitWeaponArt(
                    item,
                    owner != null && item.type == "weapon" ? (M.FighterWeapon(owner)?.rarity ?? 1) : 1
                )
            );
            var summary = V("equipment-summary grow");
            header.Add(summary);
            Label(summary, item.name, "h3");
            if (!string.IsNullOrEmpty(item.rarity))
                Label(summary, item.rarity, "item-rarity");
            var cols = Row(summary, "equipment-columns");
            var a = V("equipment-column");
            var b = V("equipment-column");
            cols.Add(a);
            cols.Add(b);
            Label(a, "Уровень: ?", "equipment-meta");
            Label(a, "Тип: " + data.Type, "equipment-meta");
            Label(b, "Очки снабжения: ??", "equipment-meta");
            Label(b, "Класс: " + data.Classes, "equipment-meta");
            Label(c, "Базовые характеристики", "equipment-section");
            var fields = Row(c, "equipment-columns");
            var left = V("equipment-column");
            var right = V("equipment-column");
            fields.Add(left);
            fields.Add(right);
            foreach (var field in data.Left)
                EquipmentField(left, field);
            foreach (var field in data.Right)
                EquipmentField(right, field);
            if (owner != null)
                Label(c, "На бойце: " + M.Soldier(owner).name, "sub");
            if (!string.IsNullOrEmpty(data.Note))
            {
                bool expanded = false;
                var note = Label(c, data.Note, "equipment-note");
                note.style.display = DisplayStyle.None;
                var more = Button(
                    c,
                    "Как считаются характеристики ▸",
                    () =>
                    {
                        expanded = !expanded;
                        note.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                    },
                    "equipment-explain"
                );
                more.name = "equipment-explain";
            }
        }

        void EquipmentField(VisualElement parent, EquipmentDetails.Field field)
        {
            var line = V("equipment-stat");
            parent.Add(line);
            Label(line, field.Name, "equipment-stat-name");
            var values = Row(line, "equipment-values wrap");
            Label(values, field.Value, "equipment-stat-value");
            if (!string.IsNullOrEmpty(field.Bonus))
            {
                bool lessIsBetter =
                    field.Name == "Скорость перезарядки"
                    || field.Name == "Время использования"
                    || field.Name == "Пауза восстановления после слома";
                bool negative = field.Bonus.StartsWith("−", StringComparison.Ordinal);
                Label(
                    values,
                    field.Bonus,
                    "equipment-stat-bonus" + (negative != lessIsBetter ? " equipment-stat-penalty" : "")
                );
            }
        }

        void EquippedDetails(string slot, int index)
        {
            var g = S.gear[selected];
            string id =
                slot == "bag" ? g.bag[index]
                : slot == "rigSlots" ? g.rigSlots[index]
                : g.Get(slot);
            if (id != null && id.StartsWith("@", StringComparison.Ordinal))
            {
                index = int.Parse(id.Substring(1));
                id = (slot == "bag" ? g.bag : g.rigSlots)[index];
            }
            if (id == null)
            {
                PickEquipment(slot, index);
                return;
            }
            pickSlot = slot;
            pickIndex = index;
            string owner = selected;
            int cell = index;
            Modal(
                "Детали снаряжения",
                (c, f) =>
                {
                    ItemDescription(c, M.Data.Item(id), owner);
                    if (slot == "weapon")
                        Button(
                            f,
                            "Улучшить",
                            () =>
                            {
                                var w = M.FighterWeapon(owner);
                                if (w != null)
                                    WeaponWorkshop(w.id, false);
                            },
                            "gold"
                        );
                    var remove = Button(
                        f,
                        "Снять",
                        () => Change(() => M.Equip(owner, slot, null, cell), Close),
                        "equipment-remove"
                    );
                    remove.name = "equipment-remove";
                    bool basic =
                        (slot == "weapon" && id == "ak")
                        || (slot == "armor" && id == "a1")
                        || (slot == "rig" && id == "v1");
                    remove.SetEnabled(true);
                    if (slot == "weapon" || slot == "armor" || slot == "rig")
                        remove.tooltip = "Снять на склад";
                    var replace = Button(
                        f,
                        "Заменить",
                        () =>
                        {
                            selected = owner;
                            PickEquipment(slot, cell);
                        },
                        "gold"
                    );
                    replace.name = "equipment-replace";
                },
                "equipment-modal"
            );
        }

        void ItemDetails(string id)
        {
            Modal(
                "Детали снаряжения",
                (c, f) =>
                {
                    var item = M.Data.Item(id);
                    ItemDescription(c, item);
                    if (item.type == "weapon" && M.Owned(id) > 0)
                        Button(f, "Улучшить", () => WorkshopPicker(id), "gold");
                    Stat(c, "Всего", M.WarehouseCount(id, storeTab == "warehouse" && S.hideIssued).ToString());
                    if (!(storeTab == "warehouse" && S.hideIssued))
                        Stat(c, "На выдаче", M.Used(id).ToString());
                    Stat(c, "Свободно", M.Free(id).ToString());
                    var holders = M.Roster().Where(u => S.gear[u.id].All().Contains(id)).Select(u => u.name).ToArray();
                    if (holders.Length > 0 && !(storeTab == "warehouse" && S.hideIssued))
                        Label(c, "Выдано: " + string.Join(", ", holders), "sub");
                    if (storeTab == "warehouse")
                    {
                        Button(
                                f,
                                "Продать один · ◈ " + Money(item.price * .75),
                                () =>
                                    Change(
                                        () => M.Sell(id),
                                        () =>
                                        {
                                            ItemDetails(id);
                                            Toast("Продано за " + Money(item.price * .75) + " очков");
                                        }
                                    )
                            )
                            .SetEnabled(M.CanSellStock(id));
                    }
                    else if (LootCrates.Direct(item))
                        Button(
                                f,
                                "Купить экземпляр · ◈ " + item.price,
                                () =>
                                    Change(
                                        () => M.Buy(id),
                                        () =>
                                        {
                                            ItemDetails(id);
                                            Toast("Предмет добавлен на склад");
                                        }
                                    ),
                                "gold"
                            )
                            .SetEnabled(M.Unlocked(item));
                },
                "equipment-modal"
            );
        }

        void ManualOffer()
        {
            Modal(
                "Учебное пособие",
                (c, f) =>
                {
                    Label(c, "Даёт бойцу 1 очко навыков. Пособий в лагере: " + S.career.manuals, "paragraph");
                    Button(
                        f,
                        "Купить · ◈ " + ManualPrice,
                        () =>
                            Change(
                                M.BuyManual,
                                () =>
                                {
                                    Close();
                                    Toast("Пособие получено. Выбери бойца в казарме.");
                                }
                            ),
                        "gold"
                    );
                }
            );
        }

        void RecruitOffer(string id)
        {
            var u = M.Data.Template(id);
            Modal(
                "Вербовка · " + u.role,
                (c, f) =>
                {
                    c.Add(Art("class-" + RoleIndex(u.role) + ".png", "offer-class"));
                    Label(c, u.trait, "h3");
                    Label(
                        c,
                        "Новобранец получает штатные АК-74, лёгкий бронежилет и разгрузку I. Боец получит каноничные позывной и внешность и поступит в резерв. Повторки развиваются отдельно. Казарма: "
                            + S.gear.Count
                            + "/50.",
                        "paragraph"
                    );
                    Button(
                        f,
                        "Завербовать · ◈ " + M.Data.offers.First(o => o.id == id).price,
                        () =>
                        {
                            string newId = null;
                            Change(
                                () => newId = M.Enlist(id),
                                () =>
                                {
                                    Close();
                                    Toast(M.Soldier(newId).name + " зачислен в казарму");
                                }
                            );
                        },
                        "gold"
                    );
                }
            );
        }

        void PersonDetails(string id)
        {
            trainee = id;
            var u = M.Soldier(id);
            string origin = screen;
            Modal(
                u.name + " · " + u.role + " · Ур. " + M.Person(id).level,
                (c, f) =>
                {
                    var detail = Row(c, "fighter-card-layout");
                    var left = V("fighter-card-left");
                    detail.Add(left);
                    var pic = Portrait(u);
                    pic.AddToClassList("fighter-card-portrait");
                    pic.style.height = Mathf.Clamp(vh * .43f, 120, 320);
                    left.Add(pic);
                    var info = Button(
                        left,
                        "Инфо",
                        () => FighterInfo(id, () => PersonDetails(id)),
                        "gold fighter-card-action"
                    );
                    info.name = "fighter-card-info";
                    var training = Button(
                        left,
                        "Тренировка",
                        () =>
                        {
                            trainee = id;
                            trainingFrom = origin;
                            screen = "training";
                            Close();
                            Render();
                        },
                        "fighter-card-action"
                    );
                    training.name = "fighter-card-training";
                    if (id != "kedr")
                    {
                        var dismiss = Button(
                            left,
                            "Уволить в запас",
                            () => DismissDialog(id),
                            "battle-red fighter-card-action"
                        );
                        dismiss.name = "fighter-card-dismiss";
                    }
                    var bio = V("fighter-biography panel");
                    detail.Add(bio);
                    var story = V("fighter-bio-copy");
                    bio.Add(story);
                    Label(story, "БИОГРАФИЯ", "eyebrow");
                    Label(story, "Это «" + u.name + "», и это его биография.", "paragraph biography-text");
                    var learned = V("fighter-learned");
                    bio.Add(learned);
                    Label(learned, "ИЗУЧЕННЫЕ НАВЫКИ", "eyebrow");
                    var sc = Scroll(learned, "fighter-learned-scroll");
                    var icons = Row(sc, "fighter-learned-icons wrap");
                    var skillIds = LearnedSkillIcons(id).ToArray();
                    foreach (var skillId in skillIds)
                    {
                        var skill = skillId;
                        var button = Button(
                            icons,
                            "",
                            () =>
                            {
                                trainee = id;
                                NodeInfo(skill, () => PersonDetails(id));
                            },
                            "fighter-learned-icon"
                        );
                        button.name = "learned-" + skill;
                        button.Add(Art("skills103/" + skill + ".png", "fighter-learned-art"));
                    }
                    if (skillIds.Length == 0)
                        Label(icons, "Пока нет изученных навыков", "sub");
                },
                "person-modal fighter-card-modal"
            );
        }

        IEnumerable<string> LearnedSkillIcons(string fighter)
        {
            // Only ranks of the same ability supersede each other, not dependencies.
            string[][] ranks =
            {
                new[] { "aim", "aim-4", "aim-5", "aim-10", "aim-15" },
                new[] { "reload", "reload-3", "reload-4", "reload-5", "reload-10" },
                new[] { "morale", "morale2", "morale3" },
                new[] { "aid", "aid2", "aid3" },
                new[] { "vision1", "vision2", "vision3" },
                new[] { "optic1", "optic2", "optic3" },
                new[] { "medheal1", "medheal2", "medheal3" },
                new[] { "rifpack1", "rifpack2", "rifpack3" },
                new[] { "medpack1", "medpack2", "medpack3" },
            };
            var hidden = new HashSet<string>();
            foreach (var family in ranks)
            {
                int highest = Array.FindLastIndex(family, skill => M.Has(fighter, skill));
                for (int i = 0; i < highest; i++)
                    hidden.Add(family[i]);
            }
            return M.Data.skills.Where(k => M.Has(fighter, k.id) && !hidden.Contains(k.id)).Select(k => k.id);
        }

        void FighterLine(VisualElement parent, string id, bool training = false)
        {
            var p = M.Person(id);
            var line = Row(parent, "growth-line wrap");
            Label(line, "Ур. " + p.level);
            var xp = Row(line, "growth-value");
            Label(xp, "Опыт " + p.credit + "/" + CampModel.NextCost(p.level));
            if (training)
            {
                var plus = Button(xp, "+", () => Change(() => M.Invest(id, 1)), "growth-plus");
                plus.name = "invest-one";
                plus.tooltip = "Вложить 1 свободный опыт (есть " + S.career.freeXp + ")";
                plus.SetEnabled(M.InvestLimit(id) > 0);
            }
            var points = Row(line, "growth-value");
            Label(points, "ОП " + p.points + " шт.");
            if (training)
            {
                var plus = Button(points, "+", () => Change(() => M.Promote(id, "manual")), "growth-plus");
                plus.name = "manual-one";
                plus.tooltip = "Потратить 1 методичку (есть " + S.career.manuals + ")";
                plus.SetEnabled(S.career.manuals > 0);
            }
        }

        void DismissDialog(string id)
        {
            int xp = M.DismissXp(id);
            int manuals = M.RefundCount(id);
            Modal(
                "Уволить в запас?",
                (c, f) =>
                {
                    Label(
                        c,
                        M.Soldier(id).name
                            + " покинет казарму и все отряды.\n\nСвободный опыт: +"
                            + xp
                            + "\nМетодички: +"
                            + manuals
                            + (manuals == 0 ? " (не вкладывались)" : " (возврат вложенных)")
                            + "\n\nОружие, броня, разгрузка, дополнительное снаряжение и всё содержимое сумки и разгрузки вернутся на склад.\nУвольнение нельзя отменить.",
                        "paragraph"
                    );
                    string replacement = null;
                    if (S.active.Contains(id))
                    {
                        var reserve = M.Roster().Where(u => !S.active.Contains(u.id) && u.role != "Командир").ToArray();
                        if (reserve.Length > 0)
                        {
                            replacement = reserve[0].id;
                            Dropdown(
                                c,
                                "Кто займёт место в текущем отряде",
                                reserve.Select(u => u.name + " · " + u.role).ToArray(),
                                0,
                                n => replacement = reserve[n].id
                            );
                        }
                        else
                            Label(
                                c,
                                "Место в отряде останется пустым. Перед боем назначьте другого бойца.",
                                "paragraph"
                            );
                    }
                    Button(f, "Отмена", () => PersonDetails(id));
                    Button(f, "Уволить в запас", () => Change(() => M.Dismiss(id, replacement), Close), "battle-red");
                }
            );
        }

        void AssignDialog(string id)
        {
            Modal(
                "Выберите слот для " + M.Soldier(id).name,
                (c, f) =>
                {
                    Label(c, "Экипировка заменённого бойца останется у него в резерве.", "paragraph");
                    for (int i = 1; i < 5; i++)
                    {
                        int n = i;
                        Button(
                            c,
                            (i + 1) + ". " + (S.active[i] == null ? "Пустой слот" : M.Soldier(S.active[i]).name),
                            () =>
                                Change(
                                    () => M.Replace(n, id),
                                    () =>
                                    {
                                        selected = id;
                                        Navigate("prepare");
                                    }
                                )
                        );
                    }
                }
            );
        }

        void ReplacePicker()
        {
            int index = S.active.IndexOf(selected);
            Modal(
                "Заменить бойца",
                (c, f) =>
                {
                    Label(
                        c,
                        "Командирский слот закреплён. Бойцы в составе недоступны для повторного выбора.",
                        "paragraph"
                    );
                    var grid = Grid(c);
                    foreach (var u in M.Roster().Where(u => u.role != "Командир" && !S.active.Contains(u.id)))
                        PersonCard(
                            grid,
                            u,
                            () =>
                                Change(
                                    () => M.Replace(index, u.id),
                                    () =>
                                    {
                                        selected = u.id;
                                        Close();
                                        Render();
                                    }
                                )
                        );
                }
            );
        }

        void PickEquipment(string slot, int index)
        {
            pickSlot = slot;
            pickIndex = index;
            ShowEquipmentPicker();
        }

        void ShowEquipmentPicker()
        {
            Modal(
                "Выбрать со склада",
                (c, f) =>
                {
                    var g = S.gear[selected];
                    Label(c, M.Soldier(selected).name + " / ЭКИПИРОВКА", "eyebrow");
                    Label(
                        c,
                        "Штатный комплект доступен каждому бойцу. Остальное снаряжение берётся со склада. Учитывается снаряжение бойцов в резерве.",
                        "sub paragraph"
                    );
                    var grid = Grid(c);
                    var items = M
                        .Data.items.Where(i => M.CanEquip(selected, pickSlot, i) && M.Free(i.id) > 0)
                        .ToArray();
                    foreach (var it in items)
                    {
                        var card = Button(grid, "", () => InspectEquipment(it.id), "card");
                        Label(card, it.name, "card-title");
                        card.Add(Art(it.img, "card-image"));
                        var foot = Row(card, "card-footer");
                        Label(foot, pickSlot == "bag" ? CampModel.Width(it.id) + " клетк." : "", "grow");
                        Label(foot, M.Free(it.id) + " шт.");
                    }

                    if (items.Length == 0)
                        Label(c, "Нет свободных подходящих предметов на складе.", "paragraph");
                    if (
                        pickSlot == "extra"
                        || pickSlot == "extra2"
                        || pickSlot == "rigSlots"
                        || pickSlot == "bag"
                        || pickSlot == "tourniquet"
                    )
                        Button(
                            f,
                            "Снять / освободить",
                            () => Change(() => M.Equip(selected, pickSlot, null, pickIndex), Close)
                        );
                }
            );
        }

        void InspectEquipment(string id)
        {
            var it = M.Data.Item(id);
            Modal(
                "Детали снаряжения",
                (c, f) =>
                {
                    ItemDescription(c, it);
                    Button(f, "← К выбору", ShowEquipmentPicker);
                    Button(
                            f,
                            "Выдать",
                            () =>
                                Change(
                                    () => M.Equip(selected, pickSlot, id, pickIndex),
                                    () =>
                                    {
                                        Close();
                                        Toast("Снаряжение сохранено");
                                    }
                                ),
                            "gold"
                        )
                        .SetEnabled(M.Free(id) > 0 && M.CanEquip(selected, pickSlot, it));
                },
                "equipment-modal"
            );
        }

        void Settings()
        {
            Modal(
                "Настройки",
                (c, f) =>
                {
                    Button(f, "‹ Профиль", Profile);
                    var o = GetOptions != null ? GetOptions() : new NativeCampOptions();
                    var scaleLabel = Label(c, "Размер интерфейса: " + o.uiScale + "%", "setting");
                    var scaleSlider = new SliderInt(50, 300) { value = o.uiScale, name = "ui-scale-slider" };
                    scaleSlider.AddToClassList("ui-scale-slider");
                    c.Add(scaleSlider);
                    scaleSlider.RegisterValueChangedCallback(e =>
                        scaleLabel.text = "Размер интерфейса: " + e.newValue + "%"
                    );
                    var scaleButtons = Row(c, "wrap");
                    Action<int> applyScale = value =>
                    {
                        SetOption?.Invoke("uiScale", value);
                        UpdateViewport();
                    };
                    Button(scaleButtons, "Применить", () => applyScale(scaleSlider.value));
                    Button(scaleButtons, "Сбросить на 100%", () => applyScale(100));
                    Dropdown(
                        c,
                        "Качество графики",
                        new[] { "Пиксели!!!", "Низкое", "Среднее", "Высокое", "Ультра" },
                        o.quality + 1,
                        n => SetOption?.Invoke("quality", n - 1)
                    );
                    Dropdown(
                        c,
                        "Лимит FPS",
                        new[] { "30", "60", "90", "120" },
                        Array.IndexOf(new[] { 30, 60, 90, 120 }, o.fps),
                        n => SetOption?.Invoke("fps", new[] { 30, 60, 90, 120 }[n])
                    );
                    OptionToggle(c, "Звуковые эффекты", "sound", o.sound);
                    Volume(c, "Громкость эффектов", "effects", o.effects);
                    OptionToggle(c, "Музыка", "musicOn", o.musicOn);
                    Volume(c, "Громкость музыки", "music", o.music);
                    var fog = V();
                    var full = new Toggle("Полная видимость") { value = o.visibility };
                    full.AddToClassList("setting");
                    c.Add(full);
                    full.RegisterValueChangedCallback(e =>
                    {
                        SetOption?.Invoke("visibility", e.newValue ? 1 : 0);
                        fog.style.display = e.newValue ? DisplayStyle.None : DisplayStyle.Flex;
                    });
                    c.Add(fog);
                    fog.style.display = o.visibility ? DisplayStyle.None : DisplayStyle.Flex;
                    OptionToggle(fog, "Хардкорный туман войны", "fog", o.fog);
                    Label(c, "Очки обеспечения: " + Money(S.balance) + ". Сюжет: " + S.story + " / 20.", "paragraph");
                    var motion = new Toggle("Фоновое движение в лагере") { value = S.motion };
                    motion.AddToClassList("setting");
                    c.Add(motion);
                    motion.RegisterValueChangedCallback(e => Change(() => S.motion = e.newValue));
                    Button(
                        c,
                        "Полный сброс прогресса",
                        () =>
                            Modal(
                                "Удалить весь прогресс?",
                                (cc, ff) =>
                                {
                                    Label(
                                        cc,
                                        "Сюжет и бесконечная война начнутся заново. Очки обеспечения, покупки, завербованные бойцы и навыки будут потеряны. Отменить сброс нельзя.",
                                        "paragraph"
                                    );
                                    Button(ff, "Отмена", Close);
                                    Button(
                                        ff,
                                        "Удалить прогресс",
                                        () =>
                                            Try(() =>
                                            {
                                                ResetGame?.Invoke();
                                                selected = trainee = "kedr";
                                                filterRole = filterRarity = filterUnit = filterType = "";
                                                sort = "default";
                                                storeTab = "shop";
                                                Navigate("home");
                                            }),
                                        "gold"
                                    );
                                }
                            )
                    );
                    Button(c, "Перенос сохранения", ImportDialog);
                }
            );
        }

        void OptionToggle(VisualElement p, string title, string key, bool value)
        {
            var t = new Toggle(title) { value = value };
            t.AddToClassList("setting");
            p.Add(t);
            t.RegisterValueChangedCallback(e => Try(() => SetOption?.Invoke(key, e.newValue ? 1 : 0)));
        }

        void Volume(VisualElement p, string title, string key, int value)
        {
            var r = Row(p, "volume setting");
            var label = Label(r, title + "  " + value + "%", "grow");
            var slider = new SliderInt(0, 100) { value = value };
            slider.AddToClassList("volume-slider");
            r.Add(slider);
            slider.RegisterValueChangedCallback(e =>
            {
                label.text = title + "  " + e.newValue + "%";
                Try(() => SetOption?.Invoke(key, e.newValue));
            });
        }

        void ImportDialog()
        {
            Modal(
                "Перенос сохранения",
                (c, f) =>
                {
                    Label(
                        c,
                        "Вставь сохранение прежнего лагеря. Перед заменой текущее сохранение остаётся в резервной копии.",
                        "paragraph"
                    );
                    var field = new TextField { multiline = true };
                    field.AddToClassList("import-text");
                    c.Add(field);
                    Button(f, "Вставить", () => field.value = GUIUtility.systemCopyBuffer);
                    Button(
                        f,
                        "Импортировать",
                        () =>
                        {
                            string imported = field.value;
                            Modal(
                                "Заменить прогресс?",
                                (cc, ff) =>
                                {
                                    Label(
                                        cc,
                                        "Текущий прогресс будет заменён импортированным сохранением.",
                                        "paragraph"
                                    );
                                    Button(ff, "Отмена", Close);
                                    Button(
                                        ff,
                                        "Импортировать",
                                        () =>
                                        {
                                            Store.Import(imported);
                                            selected = trainee = S.active[0];
                                            screen = "home";
                                            Close();
                                            Render();
                                        },
                                        "gold"
                                    );
                                }
                            );
                        },
                        "gold"
                    );
                    Button(
                        f,
                        "Копировать текущее",
                        () =>
                        {
                            GUIUtility.systemCopyBuffer = M.Serialize();
                            Toast("Сохранение скопировано");
                        }
                    );
                }
            );
        }
    }
}
