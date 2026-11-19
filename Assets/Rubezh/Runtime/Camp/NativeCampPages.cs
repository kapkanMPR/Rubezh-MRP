using System;
using System.Collections.Generic;
using System.Linq;
using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;
using CampUnitInfo = Rubezh.Camp.Unit;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        VisualElement ListPage(string title, bool filters = true)
        {
            var page = V("list-page");
            main.Add(page);
            var heading = Row(page, "list-heading");
            Label(heading, title, "h1 grow");
            if (filters)
                Button(heading, "▽", Filters, "filter-toggle");
            return page;
        }

        VisualElement Grid(VisualElement parent, bool compactEquipment = false)
        {
            var grid = V("cards-grid row wrap");
            if (compactEquipment)
                grid.AddToClassList("equipment-grid");
            parent.Add(grid);
            grid.RegisterCallback<GeometryChangedEvent>(e =>
            {
                float width = e.newRect.width;
                if (width <= 0)
                    return;
                int columns =
                    compactEquipment ? Mathf.Max(2, Mathf.FloorToInt((width + 8) / (vh <= 600 ? 105 : 125)))
                    : vw <= 650 ? 2
                    : Mathf.Max(1, Mathf.FloorToInt((width + 16) / (vh <= 600 ? 141 : 176)));
                float gap =
                    compactEquipment ? 6
                    : vh <= 600 && vw > 650 ? 9
                    : 16;
                float cw = (width - gap * (columns - 1)) / columns;
                foreach (var child in grid.Children())
                {
                    child.style.width = cw;
                    child.style.marginRight = (grid.IndexOf(child) + 1) % columns == 0 ? 0 : gap;
                    child.style.marginBottom = gap;
                }
            });
            return grid;
        }

        string storeGroup = "equipment";

        static string ShopGroup(Item item) =>
            item.type == "weapon" || item.id == "rpg" || item.id == "rshg" ? "weapons"
            : item.type == "grenade" || item.type == "medical" || item.type == "ammo" ? "consumables"
            : "equipment";

        void StorePage()
        {
            if (storeTab != "recruit")
            {
                InventoryStorePage();
                return;
            }
            bool recruit = true;
            var page = ListPage("Вербовка · " + S.gear.Count + "/50", false);
            var scroll = Scroll(page, "list-scroll");
            var grid = Grid(scroll);
            if (recruit)
            {
                var b = Button(grid, "", ManualOffer, "card");
                Label(b, "Учебное пособие", "card-title");
                Label(b, "+1 очко навыков", "card-image centered");
                Label(b, "◈ " + ManualPrice, "card-footer");
                foreach (
                    var u in M.Data.units.Where(u =>
                        u.id != "sup" && M.Data.offers.Any(o => o.id == u.id && S.story >= o.unlock)
                    )
                )
                {
                    var unit = u;
                    var offer = M.Data.offers.First(o => o.id == u.id);
                    var card = Button(grid, "", () => RecruitOffer(unit.id), "card person");
                    Label(card, u.role, "card-title");
                    card.Add(Art("class-" + RoleIndex(u.role) + ".png", "class-image"));
                    var f = Row(card, "card-footer");
                    Label(f, u.unit, "grow");
                    Label(f, "◈ " + offer.price, "qty");
                }

                return;
            }
        }

        void StoreTab(string tab)
        {
            storeTab = tab;
            screen = "store";
            Close();
            Render();
        }

        int ManualPrice
        {
            get { return M.Data.offers.First(o => o.id == "med").price * 3; }
        }

        int RoleIndex(string role)
        {
            return Array.IndexOf(
                new[] { "Командир", "Пулемётчик", "Медик", "Гранатомётчик", "Стрелок", "Снайпер" },
                role
            );
        }

        void ItemCard(VisualElement grid, Item item, Action click, bool warehouse = false)
        {
            var card = Button(grid, "", click, "card");
            card.name = "item-" + item.id;
            WeaponRarityCard(card, item);
            if (item.rarity == "Редкое")
            {
                card.AddToClassList("rare-item");
                Label(card, "РЕДКОЕ", "item-rarity");
            }
            Label(card, item.name, "card-title");
            card.Add(Art(item.img, "card-image"));
            var footer = Row(card, "card-footer");
            Label(footer, warehouse ? "На выдаче: " + M.Used(item.id) : "◈ " + item.price, "grow");
            Label(footer, warehouse ? "Свободно: " + M.Free(item.id) : "Купить", "qty");
        }

        void PersonCard(VisualElement grid, CampUnitInfo u, Action click = null)
        {
            var card = Button(grid, "", click ?? (() => PersonDetails(u.id)), "card person");
            card.name = "person-" + u.id;
            card.AddToClassList("portrait-card");
            CardHeading(card, u);
            var face = Portrait(u);
            face.AddToClassList("card-portrait");
            card.Add(face);
            face.RegisterCallback<GeometryChangedEvent>(e =>
            {
                if (e.newRect.width > 0)
                    face.style.height = e.newRect.width;
            });
            Book(card, u.id);
        }

        void Barracks()
        {
            var page = ListPage("Личный состав · " + S.gear.Count + "/50");
            Pending(page);
            var scroll = Scroll(page, "list-scroll");
            var grid = Grid(scroll);
            var list = M.Roster()
                .Where(u =>
                    (filterRole == "" || u.role == filterRole)
                    && (filterRarity == "" || u.rarity == filterRarity)
                    && (filterUnit == "" || u.unit == filterUnit)
                );
            var ordered = list.OrderBy(u =>
                    u.id == "kedr" ? 0
                    : S.active.Contains(u.id) ? 1
                    : 2
                )
                .ThenBy(u => sort == "default" ? -M.Person(u.id).level : 0)
                .ThenBy(
                    u =>
                        sort == "role" ? u.role
                        : sort == "rarity" ? u.rarity
                        : sort == "unit" ? u.unit
                        : u.name,
                    StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), false)
                )
                .ToArray();
            foreach (var u in ordered)
                PersonCard(grid, u);
            if (ordered.Length == 0)
                Label(scroll, "Нет бойцов по этим фильтрам", "paragraph");
            var footer = Row(page, "list-footer");
            Button(footer, "Отряды →", () => Navigate("squads"), "gold");
        }

        void Pending(VisualElement parent)
        {
            if (S.pendingRecruits.Count > 0)
                Label(
                    parent,
                    "Казарма заполнена. Ждут места: "
                        + string.Join(", ", S.pendingRecruits.Select(f => f.name + " (ур. " + f.level + ")"))
                        + ". Освободи место, уволив бойца в запас.",
                    "notice"
                );
        }

        void Filters()
        {
            Modal(
                "Фильтры и сортировка",
                (c, f) =>
                {
                    if (screen == "personnel")
                    {
                        var roles = new[] { "Все классы" }
                            .Concat(M.Data.units.Select(u => u.role).Distinct())
                            .ToArray();
                        Dropdown(
                            c,
                            "Класс",
                            roles,
                            filterRole == "" ? 0 : Array.IndexOf(roles, filterRole),
                            n =>
                            {
                                filterRole = n == 0 ? "" : roles[n];
                                Render();
                            }
                        );
                        var rarities = new[] { "Любая редкость", "Обычный", "Редкий", "Уникальный" };
                        Dropdown(
                            c,
                            "Редкость",
                            rarities,
                            filterRarity == "" ? 0 : Array.IndexOf(rarities, filterRarity),
                            n =>
                            {
                                filterRarity = n == 0 ? "" : rarities[n];
                                Render();
                            }
                        );
                        Dropdown(
                            c,
                            "Подразделение",
                            new[] { "Все подразделения", "Кедр" },
                            filterUnit == "" ? 0 : 1,
                            n =>
                            {
                                filterUnit = n == 0 ? "" : "Кедр";
                                Render();
                            }
                        );
                        string[] sorts = { "default", "name", "role", "rarity", "unit" };
                        Dropdown(
                            c,
                            "Сортировка",
                            new[]
                            {
                                "Состав → резерв, по уровню",
                                "По имени",
                                "По классу",
                                "По редкости",
                                "По подразделению",
                            },
                            Array.IndexOf(sorts, sort),
                            n =>
                            {
                                sort = sorts[n];
                                Render();
                            }
                        );
                    }
                    else
                    {
                        string[] ids = { "", "weapon", "armor", "rig", "extra", "grenade", "medical", "ammo" };
                        Dropdown(
                            c,
                            "Категория снаряжения",
                            new[]
                            {
                                "Все категории",
                                "Оружие",
                                "Бронежилеты",
                                "Разгрузки",
                                "Доп. снаряжение",
                                "Гранаты",
                                "Медицина",
                                "Боеприпасы",
                            },
                            Array.IndexOf(ids, filterType),
                            n =>
                            {
                                filterType = ids[n];
                                Render();
                            }
                        );
                    }

                    Button(
                        f,
                        "Сбросить",
                        () =>
                        {
                            filterRole = filterRarity = filterUnit = filterType = "";
                            sort = "default";
                            Render();
                            Filters();
                        }
                    );
                    Button(f, "Готово", Close, "gold");
                }
            );
        }

        bool outfitDrawerOpen;

        void Prepare(bool isolated)
        {
            HubBackdrop();
            var editor = V("outfit-editor");
            main.Add(editor);
            var stage = V("outfit-stage");
            editor.Add(stage);
            if (selected == null || !S.active.Contains(selected))
                selected = S.active.FirstOrDefault(x => x != null);
            if (selected != null)
            {
                var unit = M.Soldier(selected);
                var gear = S.gear[selected];
                float cell = Mathf.Clamp((vh - 212f) / 4f, 28f, 52f);
                bool bag = gear.bag.Count > 0;
                float equipmentWidth = (cell + 5f) * (bag ? 4 : 2) + 4f;
                var equipment = V("outfit-equipment");
                stage.Add(equipment);
                equipment.style.width = equipmentWidth;
                var scroll = Scroll(equipment, "outfit-equipment-scroll");
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                var columns = Row(scroll, "outfit-columns");
                var basic = V("outfit-grid");
                columns.Add(basic);
                basic.style.width = 2 * (cell + 5f);
                OutfitSlot(basic, gear, "weapon", cell);
                OutfitSlot(basic, gear, "armor", cell);
                OutfitSlot(basic, gear, "rig", cell);
                if (M.ExtraSlots(selected) > 0)
                    OutfitSlot(basic, gear, "extra", cell);
                if (M.ExtraSlots(selected) == 2)
                    OutfitSlot(basic, gear, "extra2", cell);
                basic.Add(V("outfit-equipment-divider"));
                for (int i = 0; i < gear.rigSlots.Count; i++)
                    OutfitPocket(basic, gear.rigSlots[i], "rigSlots", i, cell);
                OutfitPocket(basic, gear.tourniquet, "tourniquet", 0, cell);
                if (bag)
                {
                    var bagGrid = V("outfit-grid");
                    columns.Add(bagGrid);
                    bagGrid.style.width = 2 * (cell + 5f);
                    // Omit continuation cells while retaining real indices for item actions.
                    for (int i = 0; i < gear.bag.Count; i++)
                        if (gear.bag[i] == null || !gear.bag[i].StartsWith("@", StringComparison.Ordinal))
                            OutfitPocket(bagGrid, gear.bag[i], "bag", i, cell);
                }
                Button(equipment, "Снять всё", () => Change(() => M.Strip(selected)), "outfit-strip");
                var hero = Portrait(unit);
                hero.AddToClassList("outfit-hero");
                stage.Add(hero);
                hero.style.left = equipmentWidth + 18f;
                var identity = V("outfit-identity");
                stage.Add(identity);
                identity.style.left = equipmentWidth + 22f;
                // Fit against the actual viewport, including the strip button and separator.
                int basicRows = (3 + M.ExtraSlots(selected) + 1) / 2 + (gear.rigSlots.Count + 2) / 2;
                int visibleBagCells = gear.bag.Count(x => x == null || !x.StartsWith("@", StringComparison.Ordinal));
                int rows = Mathf.Max(4, Mathf.Max(basicRows, (visibleBagCells + 1) / 2));
                scroll.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    float available = scroll.contentViewport.resolvedStyle.height;
                    if (float.IsNaN(available) || available <= 16f)
                        return;
                    float side = Mathf.Min(52f, Mathf.Max(16f, Mathf.Floor((available - 12f) / rows) - 5f));
                    float width = (side + 5f) * (bag ? 4 : 2) + 4f;
                    equipment.style.width = width;
                    foreach (var grid in columns.Children())
                        grid.style.width = 2 * (side + 5f);
                    columns
                        .Query<VisualElement>(className: "outfit-cell")
                        .ForEach(c =>
                        {
                            c.style.width = side;
                            c.style.height = side;
                        });
                    hero.style.left = width + 18f;
                    identity.style.left = width + 22f;
                });
                Label(identity, unit.name, "outfit-name");
                var role = Row(identity, "outfit-role");
                role.Add(Art("class-" + RoleIndex(unit.role) + ".png", "outfit-class"));
                Label(role, unit.role, "outfit-role-name");
                Label(identity, "Ур. " + M.Person(selected).level, "sub");
                var drawer = V("outfit-drawer");
                stage.Add(drawer);
                drawer.style.display = outfitDrawerOpen ? DisplayStyle.Flex : DisplayStyle.None;
                Label(drawer, "ЛИЧНОЕ ДЕЛО", "eyebrow");
                var contents = Scroll(drawer, "outfit-drawer-scroll");
                Button(contents, "Инфо", () => FighterInfo(selected), "gold");
                Button(
                    contents,
                    "Тренировка",
                    () =>
                    {
                        trainee = selected;
                        trainingFrom = screen;
                        screen = "training";
                        Render();
                    },
                    "gold"
                );
                Button(contents, "Заменить бойца", ReplacePicker).SetEnabled(selected != "kedr");
                Button(contents, "Проверить готовность", Readiness);
                Label(contents, "Комплект восстанавливается перед каждым боем.", "sub paragraph");
                var toggle = Button(
                    stage,
                    outfitDrawerOpen ? "›" : "‹",
                    () =>
                    {
                        outfitDrawerOpen = !outfitDrawerOpen;
                        Render();
                    },
                    "outfit-drawer-toggle"
                );
                toggle.tooltip = "Личное дело и действия";
                toggle.style.right = outfitDrawerOpen ? 224f : 0f;
            }
            else
                Label(stage, "Выберите командира в нижней ленте", "h2");
            var footer = Row(editor, "outfit-footer");
            var controls = V("outfit-footer-controls");
            footer.Add(controls);
            Button(controls, "‹ В лагерь", () => Navigate("home"));
            var squadCaption = Row(controls, "outfit-squad-heading");
            Label(squadCaption, M.CurrentSquad.name, "outfit-squad-caption");
            Button(squadCaption, "✎", () => RenameSquadDialog(M.CurrentSquad.id), "outfit-rename").tooltip =
                "Переименовать отряд";
            var roster = Scroll(footer, "outfit-roster", ScrollViewMode.Horizontal);
            roster.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            var strip = Row(roster, "outfit-roster-row");
            for (int i = 0; i < S.active.Count; i++)
            {
                int slot = i;
                string id = S.active[i];
                if (id == null)
                {
                    Button(strip, "＋", () => SquadSlotPicker(slot), "outfit-member");
                    continue;
                }
                var u = M.Soldier(id);
                var b = Button(
                    strip,
                    "",
                    () =>
                    {
                        selected = id;
                        Render();
                    },
                    "outfit-member" + (selected == id ? " selected" : "")
                );
                var pic = Portrait(u);
                pic.AddToClassList("outfit-thumbnail");
                b.Add(pic);
                Label(b, u.name, "outfit-member-name");
                var meta = Row(b, "outfit-member-meta");
                Label(meta, "Ур. " + M.Person(id).level, "outfit-member-level");
                meta.Add(Art("class-" + RoleIndex(u.role) + ".png", "outfit-member-class"));
                b.tooltip = u.name + " · " + u.role;
            }
            var formation = Button(
                strip,
                "",
                () =>
                {
                    SaveView();
                    if (StartOperation == null)
                        throw new InvalidOperationException("Построение недоступно");
                    var order = CreateOrder();
                    order.op = "edit-formation";
                    StartOperation(order);
                },
                "outfit-formation"
            );
            formation.name = "action-squad-formation";
            formation.tooltip = M.SquadReady ? "Построение этого отряда" : "Сначала заполните пять мест отряда";
            formation.SetEnabled(M.SquadReady);
            var emblem = Art("menu101/formation-icon.png", "outfit-formation-icon");
            emblem.pickingMode = PickingMode.Ignore;
            formation.Add(emblem);
            Label(formation, "Построение", "outfit-formation-caption").pickingMode = PickingMode.Ignore;
        }

        Image OutfitWeaponArt(Item item, int rarity) =>
            OutfitItemArt(Rubezh.WeaponAppearance.ItemPath(item.id, item.img, rarity));

        Image OutfitItemArt(string path)
        {
            var image = Art(path, "outfit-item-art");
            // Ignore transparent source margins without changing shared item textures.
            switch (path)
            {
                case "a1.png":
                    image.uv = new Rect(0.19583333f, 0.11250000f, 0.60416667f, 0.77916667f);
                    break;
                case "a2.png":
                    image.uv = new Rect(0.15000000f, 0.09583333f, 0.70000000f, 0.80833333f);
                    break;
                case "a3.png":
                    image.uv = new Rect(0.11666667f, 0.09583333f, 0.76666667f, 0.80833333f);
                    break;
                case "medical.png":
                    image.uv = new Rect(0.10937500f, 0.06250000f, 0.78125000f, 0.78125000f);
                    break;
                case "supply.png":
                    image.uv = new Rect(0.10937500f, 0.06250000f, 0.78125000f, 0.87500000f);
                    break;
                case "v1.png":
                    image.uv = new Rect(0.11666667f, 0.11250000f, 0.76250000f, 0.77500000f);
                    break;
                case "v2.png":
                    image.uv = new Rect(0.12916667f, 0.11666667f, 0.74166667f, 0.76666667f);
                    break;
                case "v3.png":
                    image.uv = new Rect(0.09583333f, 0.11250000f, 0.80416667f, 0.77500000f);
                    break;
            }
            return image;
        }

        void OutfitSlot(VisualElement parent, Gear gear, string slot, float size)
        {
            var item = M.Data.Item(gear.Get(slot));
            var b = Button(parent, "", () => EquippedDetails(slot, 0), "outfit-cell");
            b.style.width = size;
            b.style.height = size;
            if (item != null)
            {
                WeaponRarityCard(b, item);
                b.Add(OutfitWeaponArt(item, item.type == "weapon" ? (M.FighterWeapon(selected)?.rarity ?? 1) : 1));
                b.tooltip = item.name;
            }
            else
            {
                Label(b, "+", "outfit-empty");
                b.tooltip = "Выбрать снаряжение";
            }
        }

        void OutfitPocket(VisualElement parent, string id, string slot, int index, float size)
        {
            bool tail = id != null && id.StartsWith("@", StringComparison.Ordinal);
            string actual = tail
                ? (slot == "bag" ? S.gear[selected].bag : S.gear[selected].rigSlots)[int.Parse(id.Substring(1))]
                : id;
            var item = M.Data.Item(actual);
            var b = Button(parent, "", () => EquippedDetails(slot, index), "outfit-cell");
            b.style.width = size;
            b.style.height = size;
            if (tail)
                Label(b, "↳", "outfit-empty");
            else if (item != null)
                b.Add(OutfitItemArt(item.img));
            else
                Label(b, "+", "outfit-empty");
            b.tooltip = item != null ? item.name : "Свободный слот";
        }

        void FighterInfo(string id, Action returnToCard = null) => ShowFighterStatistics(id, returnToCard);

        void Stat(VisualElement p, string key, string value)
        {
            var row = Row(p, "stat");
            Label(row, key, "grow");
            Label(row, value, "stat-value");
        }

        string ClassInfo(CampUnitInfo u, Gear g)
        {
            var r = u.role;
            Func<string, int> ranks = prefix => Enumerable.Range(1, 3).Count(i => M.Has(u.id, prefix + i));
            if (r == "Медик")
                return "Дополнительный жгут · перевязка быстрее на 10% · объём помощи +"
                    + (10 + 5 * ranks("medheal"))
                    + "%.";
            if (r == "Снайпер")
                return "Без доп. снаряжения и штурмовой брони · фронтальный обзор: "
                    + (20 + ranks("vision"))
                    + " клеток.";
            if (r == "Командир")
                return "РШГ и сумка обеспечения · обзор "
                    + (
                        M.Has(u.id, "optic3") ? 20
                        : M.Has(u.id, "optic2") ? 18
                        : M.Has(u.id, "optic1") ? 17
                        : 16
                    )
                    + " клеток.";
            if (r == "Стрелок")
                return "Рюкзак снабжения и ГП · ручные гранаты и дым: дальность +1, разброс −1 клетка."
                    + (
                        (g.weapon == "svd" || g.weapon == "m24")
                            ? " Винтовка: с места обзор до 18, половина бонуса точности и шанс крита "
                                + (g.weapon == "m24" ? "20%" : "10%")
                                + "."
                            : ""
                    );
            return r == "Гранатомётчик" ? "Два доп. слота только для РПГ-26 / РШГ · разброс выстрела −1 клетка."
                : r == "Пулемётчик" ? "Агрессивное подавление пулемётом · доп. слот недоступен."
                : u.trait;
        }

        string OperationName()
        {
            var o = S.operation;
            return (o.mode == "test" || o.mode == "test-reference") ? "Тест · гибридный полигон"
                : o.mode == "raid" ? "Рейд · уровень " + M.S.raid.Start
                : o.mode == "story" ? M.Data.missions[o.mission - 1].name
                : "Блок " + o.block + " · " + M.Data.war[(o.block - 1) * 10].terrain;
        }

        void Operations()
        {
            var o = S.operation;
            bool war = o.mode == "war";
            var board = V("operations");
            main.Add(board);
            var switches = Row(board, "op-switch");
            Button(switches, "Сюжет", () => SelectMode("story"), o.mode == "story" ? "on" : "");
            Button(switches, "Бесконечный режим", () => SelectMode("war"), war ? "on" : "");
            Button(switches, "Рейд", () => SelectMode("raid"), o.mode == "raid" ? "on" : "");
            Button(switches, "Тест", () => SelectMode("test"), o.mode.StartsWith("test") ? "on" : "");
            if (o.mode.StartsWith("test"))
            {
                Label(board, "ГИБРИДНЫЙ ПОЛИГОН · 36 × 24", "op-title");
                Label(
                    board,
                    "Объёмные здания, крыши и предметы со спрайтовыми бойцами. Размер поля — как у первой миссии.",
                    "paragraph"
                );
                Label(
                    board,
                    "Осмотр, движение камеры, 10 / 50 / 100 демонстрационных бойцов и пролом стены. Это проверка графики, без боя, наград и изменения прогресса.",
                    "paragraph"
                );
                var tests = Row(board, "op-actions");
                Button(tests, "Открыть полигон", () => StartHybridRange?.Invoke(), "gold");
                return;
            }
            if (o.mode == "raid")
            {
                RaidOperationCard(board);
                return;
            }
            var scroll = Scroll(board, "sector-scroll");
            var sector = V("sector" + (war ? " war-sector" : " story-sector"));
            scroll.Add(sector);
            Label(sector, war ? "РАЙОН 0" + o.block + " / 05" : "СЮЖЕТНАЯ КАМПАНИЯ", "eyebrow centered");
            Label(sector, war ? M.Data.war[(o.block - 1) * 10].terrain : "Операции «Кедра»", "op-title");
            if (war)
            {
                var carousel = Row(sector, "sector-carousel");
                Button(carousel, "‹", () => SelectBlock(o.block - 1), "arrow").SetEnabled(M.BlockOpen(o.block - 1));
                carousel.Add(Art("sector-" + o.block + ".png", "sector-seal"));
                Button(carousel, "›", () => SelectBlock(o.block + 1), "arrow").SetEnabled(M.BlockOpen(o.block + 1));
            }
            else
                sector.Add(Art("badge.png", "op-badge"));
            var dots = Row(sector, "op-track wrap");
            for (int i = 1; i <= (war ? 10 : 20); i++)
            {
                int n = i;
                var b = Button(
                    dots,
                    n.ToString(),
                    () => Change(() => S.operation.mission = n),
                    "op-dot"
                        + (S.completed.Contains(o.mode + "-" + o.block + "-" + i) ? " won" : "")
                        + (i == o.mission ? " selected" : "")
                );
                b.SetEnabled(M.MissionOpen(o.mode, o.block, n));
                b.RegisterCallback<GeometryChangedEvent>(e => b.style.height = e.newRect.width);
            }

            Label(sector, "МИССИЯ " + o.mission.ToString("00") + " · ИЗ " + (war ? 10 : 20), "eyebrow centered");
            Label(
                sector,
                war ? M.Data.war[(o.block - 1) * 10 + o.mission - 1].name : M.Data.missions[o.mission - 1].name,
                "h2 centered"
            );
            Label(
                sector,
                war
                    ? (
                        o.block < 5 && !M.BlockOpen(o.block + 1)
                            ? "Следующий район откроется после 10-й миссии."
                            : "Выбери миссию района."
                    )
                    : M.Data.missions[o.mission - 1].brief,
                "paragraph centered"
            );
            Label(
                sector,
                S.completed.Contains(o.mode + "-" + o.block + "-" + o.mission)
                    ? "Миссия уже пройдена · повторной награды нет"
                    : "+100 опыта · +1 пособие · ◈ "
                        + CampModel.Payout(o.mode, o.block, o.mission)
                        + (war && o.mission % 5 == 0 ? " · кейс" : ""),
                "gold-text centered"
            );
            if (war && o.mission == 10)
            {
                var low = CampModel.RecruitRange(o.block, int.MaxValue);
                var high = CampModel.RecruitRange(o.block, int.MaxValue, true);
                Label(
                    board,
                    "Учебный центр: 3 бойца ур. "
                        + string.Join("–", low)
                        + " и 1 подготовленный ур. "
                        + string.Join("–", high.Distinct())
                        + ". Не выше уровня игрока.",
                    "sub centered"
                );
            }

            var actions = Row(board, "op-actions");
            Button(actions, "В БОЙ!", () => Navigate("battlePrep"), "battle-red");
            Button(actions, "Брифинг", Briefing);
            if (Developer)
                Button(
                        board,
                        "!!!пройти уровень!!!",
                        () => CompleteOperation(o.mode, o.block, o.mission, S.active.ToArray()),
                        "dev-pass"
                    )
                    .SetEnabled(M.MissionOpen(o.mode, o.block, o.mission));
        }

        void SelectMode(string mode)
        {
            Change(() =>
            {
                S.operation = new Operation { mode = mode };
                if (mode == "test")
                {
                    S.operation.mission = 1;
                    S.operation.block = 1;
                    return;
                }
                if (mode == "raid")
                {
                    S.operation.mission = S.raid.Start;
                    return;
                }
                if (mode == "war")
                {
                    int b = 1;
                    while (b < 5 && S.completed.Contains("war-" + b + "-10"))
                        b++;
                    SelectBlockState(b);
                }
                else
                    S.operation.mission = Enumerable
                        .Range(1, 20)
                        .FirstOrDefault(m => !S.completed.Contains("story-1-" + m));
                if (S.operation.mission == 0)
                    S.operation.mission = 20;
            });
        }

        void SelectBlockState(int b)
        {
            CampModel.Check(M.BlockOpen(b), "Район ещё не открыт");
            S.operation.block = b;
            S.operation.mission = Enumerable
                .Range(1, 10)
                .FirstOrDefault(m => !S.completed.Contains("war-" + b + "-" + m));
            if (S.operation.mission == 0)
                S.operation.mission = 10;
        }

        void SelectBlock(int b)
        {
            Change(() => SelectBlockState(b));
        }

        void Briefing()
        {
            Modal(
                OperationName(),
                (c, f) =>
                {
                    var o = S.operation;
                    if (o.mode.StartsWith("test"))
                    {
                        Label(
                            c,
                            "Гибридный полигон: объёмное окружение и демонстрационные бойцы. Прогресс и имущество не изменяются.",
                            "paragraph"
                        );
                        Button(f, "Открыть полигон", () => StartHybridRange?.Invoke(), "gold");
                        return;
                    }
                    if (o.mode == "raid")
                    {
                        Label(
                            c,
                            "100 боёв на 50 локациях. Состояние отряда сохраняется между боями. Поддержка каждые 5 уровней; контрольные точки каждые 10. Повторной добычи нет.",
                            "paragraph"
                        );
                        Button(f, "Подготовить отряд", () => Navigate("battlePrep"), "gold");
                        return;
                    }
                    if (o.mode == "story")
                        Label(c, M.Data.missions[o.mission - 1].brief, "paragraph");
                    else
                    {
                        var map = M.Data.war[(o.block - 1) * 10 + o.mission - 1];
                        Label(
                            c,
                            map.name
                                + " · "
                                + map.w
                                + "×"
                                + map.h
                                + " клеток · противников: "
                                + map.red
                                + " · групп: "
                                + map.waves
                                + (map.reserve > 0 ? " · резерв через " + map.reserve + " с" : ""),
                            "paragraph"
                        );
                        c.Add(Art("plans/" + map.n + ".png", "plan"));
                    }

                    Label(c, "Выбрано сражение " + o.mission + ".", "paragraph");
                    Button(f, "Подготовить отряд", () => Navigate("battlePrep"), "gold");
                    Button(f, "Проверить готовность", Readiness);
                }
            );
        }

        void Readiness()
        {
            if (!M.SquadReady)
            {
                Toast("Заполните все 5 слотов отряда");
                return;
            }
            Modal(
                S.active.All(M.Armed) ? "Отряд готов к выходу" : "Есть бойцы без оружия",
                (c, f) =>
                {
                    foreach (string id in S.active)
                        Stat(
                            c,
                            M.Soldier(id).name + " · " + M.Soldier(id).role,
                            M.ItemName(S.gear[id].weapon, "Без оружия — не выходит")
                        );
                    Button(f, "Выбрать операцию", () => Navigate("operations"), "gold");
                }
            );
        }
    }
}
