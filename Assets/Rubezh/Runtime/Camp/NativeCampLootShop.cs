using System;
using System.Globalization;
using System.Linq;
using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        string lootCategory = "crates",
            lootSelected = "weapon";
        bool lootPurchaseBusy;
        static readonly Color LootBlue = new Color(.15f, .42f, .72f);

        static void LootRareCard(VisualElement card)
        {
            card.AddToClassList("rare-item");
            card.style.backgroundColor = new Color(.065f, .15f, .25f);
            card.style.borderTopColor =
                card.style.borderBottomColor =
                card.style.borderLeftColor =
                card.style.borderRightColor =
                    LootBlue;
            card.style.borderTopWidth =
                card.style.borderBottomWidth =
                card.style.borderLeftWidth =
                card.style.borderRightWidth =
                    2;
        }

        void LootShopPage()
        {
            HubBackdrop("shop-background.png");
            main.Add(V("store-shade"));
            var page = ListPage("Магазин", false);
            page.AddToClassList("inventory-page");
            var body = Row(page, "inventory-body");
            var categories = Scroll(body, "inventory-categories");
            Button(
                categories,
                "Ящики",
                () =>
                {
                    lootCategory = "crates";
                    Render();
                },
                lootCategory == "crates" ? "on" : ""
            );
            Button(
                categories,
                "Расходники",
                () =>
                {
                    lootCategory = "consumables";
                    Render();
                },
                lootCategory == "consumables" ? "on" : ""
            );
            var list = Scroll(body, "inventory-list");
            var grid = V("inventory-grid row wrap");
            list.Add(grid);
            var detail = V("inventory-detail");
            body.Add(detail);
            var content = Scroll(detail, "inventory-detail-scroll");
            var actions = V("inventory-actions");
            detail.Add(actions);
            if (lootCategory == "consumables")
            {
                var items = M
                    .Data.items.Where(i => LootCrates.Direct(i) && M.Unlocked(i))
                    .OrderBy(i => i.price)
                    .ThenBy(i => i.name)
                    .ToArray();
                if (!items.Any(i => i.id == lootSelected))
                    lootSelected = items.FirstOrDefault()?.id;
                foreach (var item in items)
                {
                    string id = item.id;
                    var cell = Button(
                        grid,
                        "",
                        () =>
                        {
                            lootSelected = id;
                            Render();
                        },
                        "inventory-cell" + (lootSelected == id ? " selected" : "")
                    );
                    cell.style.width = 110;
                    cell.style.height = 110;
                    cell.tooltip = item.name;
                    cell.Add(OutfitItemArt(item.img));
                    Label(cell, "◈ " + item.price, "inventory-item-amount");
                }
                if (lootSelected != null)
                    StoreItemPanel(content, actions, lootSelected, false);
                else
                    Label(content, "Пока нет доступных расходников", "paragraph");
                return;
            }
            if (LootCrates.Find(lootSelected) == null)
                lootSelected = "weapon";
            foreach (var crate in LootCrates.All)
            {
                var selected = crate;
                var cell = Button(
                    grid,
                    "",
                    () =>
                    {
                        lootSelected = selected.Id;
                        Render();
                    },
                    "card" + (lootSelected == crate.Id ? " selected" : "")
                );
                cell.style.width = 220;
                cell.style.minHeight = 230;
                if (crate.Id == "foreign")
                    LootRareCard(cell);
                var pic = Art("loot/" + crate.Art + "-closed");
                pic.style.height = 135;
                cell.Add(pic);
                Label(cell, crate.Name, "card-title");
                Label(cell, "◈ " + crate.Price, "card-footer");
            }
            LootCratePanel(content, actions, LootCrates.Find(lootSelected));
            var pending = S.lootReceipts == null ? null : S.lootReceipts.FirstOrDefault(r => !r.acknowledged);
            if (pending != null)
                Button(actions, "Показать полученный предмет", () => LootAward(pending));
        }

        void LootCratePanel(VisualElement content, VisualElement actions, LootCrate crate)
        {
            Label(content, crate.Name, "h2");
            var picture = Art("loot/" + crate.Art + "-closed");
            picture.style.height = 160;
            content.Add(picture);
            var explanation = Label(content, "Вы получите один из следующих предметов:", "paragraph");
            explanation.style.fontSize = 16;
            var pool = LootCrates.Pool(M, crate);
            if (pool.Count == 0)
                Label(content, "В этом ящике пока нет доступных предметов.", "paragraph");
            var drops = Row(content, "wrap");
            drops.style.paddingLeft = drops.style.paddingRight = 0;
            drops.RegisterCallback<GeometryChangedEvent>(e =>
            {
                const float gap = 6;
                float side = Mathf.Max(1, (e.newRect.width - gap * 3) / 4);
                int index = 0;
                foreach (var cell in drops.Children())
                {
                    cell.style.width = side;
                    cell.style.marginRight = index++ % 4 == 3 ? 0 : gap;
                    foreach (var card in cell.Children())
                    {
                        if (!(card is Button))
                            continue;
                        card.style.width = card.style.height = side;
                        foreach (var art in card.Children())
                            if (art is Image)
                                art.style.height = Mathf.Max(1, side - 10);
                    }
                }
            });
            foreach (var entry in pool)
            {
                var item = entry.Item;
                var cell = V("loot-drop-entry");
                cell.style.width = 72;
                cell.style.flexGrow = cell.style.flexShrink = 0;
                cell.style.marginBottom = 8;
                drops.Add(cell);
                var tile = Button(cell, "", () => ItemDetails(item.id), "inventory-cell");
                tile.style.width = tile.style.height = 72;
                tile.style.minWidth = tile.style.minHeight = 0;
                tile.style.flexGrow = tile.style.flexShrink = 0;
                tile.style.marginLeft = tile.style.marginTop = 0;
                tile.style.paddingLeft = tile.style.paddingRight = 3;
                tile.style.paddingTop = tile.style.paddingBottom = 3;
                tile.style.marginRight = tile.style.marginBottom = 0;
                tile.tooltip = item.name;
                WeaponRarityCard(tile, item);
                var art = OutfitItemArt(item.img);
                art.style.height = 62;
                tile.Add(art);
                var chance = Label(
                    cell,
                    (entry.DisplayHundredths / 100.0).ToString("0.##", CultureInfo.GetCultureInfo("ru-RU")) + "%",
                    "loot-drop-chance"
                );
                chance.style.fontSize = 11;
                chance.style.height = 18;
                chance.style.marginTop = 2;
                chance.style.unityTextAlign = TextAnchor.MiddleCenter;
            }
            Button(
                    actions,
                    "Купить и открыть · ◈ " + crate.Price,
                    () =>
                    {
                        if (lootPurchaseBusy)
                            return;
                        lootPurchaseBusy = true;
                        try
                        {
                            LootReceipt receipt = null;
                            Change(() => receipt = M.BuyLootCrate(crate.Id), () => LootAward(receipt));
                        }
                        finally
                        {
                            lootPurchaseBusy = false;
                        }
                    },
                    "gold"
                )
                .SetEnabled(
                    pool.Count > 0
                        && S.balance >= crate.Price
                        && !lootPurchaseBusy
                        && (S.lootReceipts == null || !S.lootReceipts.Any(r => !r.acknowledged))
                );
        }

        void LootAward(LootReceipt receipt) => DrawLootAward(receipt, true);

        void DrawLootAward(LootReceipt receipt, bool animate)
        {
            if (receipt == null)
                return;
            var item = M.Data.Item(receipt.item);
            if (item == null)
            {
                Toast("Награда сохранена, но предмет отсутствует в каталоге");
                return;
            }
            var crate = LootCrates.Find(receipt.crate);
            Close();
            reopenModal = () => DrawLootAward(receipt, false);
            overlay = V("modal-overlay");
            root.Add(overlay);
            var box = V("modal shipment");
            overlay.Add(box);
            float width = Mathf.Min(700, vw - 20),
                height = Mathf.Min(480, vh - 20);
            box.style.width = width;
            box.style.height = height;
            box.style.maxWidth = width;
            box.style.maxHeight = height;
            var header = V("shipment-head");
            box.Add(header);
            header.style.position = Position.Absolute;
            header.style.left = 16;
            header.style.right = 16;
            header.style.top = 8;
            var title = Label(header, "Снабжение · " + (crate == null ? "Получен предмет" : crate.Name), "modal-title");
            title.style.fontSize = 22;
            var stage = V("loot-reveal-stage");
            box.Add(stage);
            stage.style.position = Position.Absolute;
            stage.style.left = 16;
            stage.style.right = 16;
            stage.style.top = 64;
            stage.style.bottom = 72;
            float space = Mathf.Max(100, height - 136);
            bool wide = width >= 540;
            float cardWidth = wide ? Mathf.Min(300, width * .46f) : Mathf.Min(300, width - 64);
            float cardHeight = wide ? Mathf.Min(230, space) : Mathf.Max(100, space - 92);
            var closed = Art("loot/" + (crate == null ? "weapon" : crate.Art) + "-closed");
            var opened = Art("loot/" + (crate == null ? "weapon" : crate.Art) + "-open");
            foreach (var image in new[] { closed, opened })
            {
                stage.Add(image);
                image.style.position = Position.Absolute;
                image.style.width = wide ? width * .36f : 210;
                image.style.height = wide ? Mathf.Min(170, space) : 88;
                image.style.left = wide ? 4 : (width - 32 - 210) / 2;
                image.style.top = wide ? (space - Mathf.Min(170, space)) / 2 : 0;
                image.scaleMode = ScaleMode.ScaleToFit;
            }
            var card = V("drop-card");
            stage.Add(card);
            WeaponRarityCard(card, item);
            card.style.position = Position.Absolute;
            card.style.width = cardWidth;
            card.style.height = cardHeight;
            card.style.left = wide ? width - 48 - cardWidth : (width - 32 - cardWidth) / 2;
            card.style.top = wide ? (space - cardHeight) / 2 : 92;
            card.style.marginLeft = card.style.marginRight = card.style.marginTop = card.style.marginBottom = 0;
            var picture = OutfitItemArt(item.img);
            card.Add(picture);
            picture.style.position = Position.Absolute;
            picture.style.left = 10;
            picture.style.right = 10;
            picture.style.width = cardWidth - 20;
            picture.style.top = 8;
            picture.style.height = Mathf.Max(30, cardHeight - 74);
            picture.scaleMode = ScaleMode.ScaleToFit;
            var name = Label(card, item.name, "drop-name");
            name.style.position = Position.Absolute;
            name.style.left = 6;
            name.style.right = 6;
            name.style.bottom = 28;
            name.style.fontSize = 18;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            var rarity = Label(card, (item.rarity ?? "Обычное") + " · ×1", "drop-caption");
            rarity.style.position = Position.Absolute;
            rarity.style.left = 6;
            rarity.style.right = 6;
            rarity.style.bottom = 7;
            rarity.style.fontSize = 13;
            rarity.style.unityTextAlign = TextAnchor.MiddleCenter;
            var foot = Row(box, "shipment-foot");
            foot.style.position = Position.Absolute;
            foot.style.left = 12;
            foot.style.right = 12;
            foot.style.bottom = 8;
            Label(foot, "Добавлено на склад", "sub grow");
            bool finished = !animate || !S.motion;
            IVisualElementScheduledItem job = null;
            Action finish = () =>
            {
                finished = true;
                if (job != null)
                    job.Pause();
                closed.style.opacity = 0;
                opened.style.opacity = 1;
                card.style.opacity = 1;
                card.style.scale = new Scale(new Vector3(1, 1, 1));
                card.style.translate = new Translate(0, 0);
            };
            Button(
                foot,
                "Продолжить",
                () =>
                {
                    if (!finished)
                    {
                        finish();
                        return;
                    }
                    Change(
                        () =>
                        {
                            var saved = S.lootReceipts.FirstOrDefault(r => r.id == receipt.id);
                            if (saved != null)
                                saved.acknowledged = true;
                        },
                        Close
                    );
                },
                "gold"
            );
            if (finished)
                finish();
            else
            {
                closed.style.opacity = 1;
                opened.style.opacity = card.style.opacity = 0;
                float started = Time.unscaledTime;
                job = card
                    .schedule.Execute(() =>
                    {
                        float elapsed = Time.unscaledTime - started;
                        float open = Mathf.Clamp01((elapsed - .18f) / .32f);
                        closed.style.opacity = 1 - open;
                        opened.style.opacity = open;
                        float t = Mathf.Clamp01((elapsed - .48f) / .62f);
                        float ease = 1 - Mathf.Pow(1 - t, 3);
                        card.style.opacity = t;
                        float scale = .18f + .82f * ease;
                        card.style.scale = new Scale(new Vector3(scale, scale, 1));
                        card.style.translate = new Translate(wide ? -110 * (1 - ease) : 0, wide ? 0 : -65 * (1 - ease));
                        if (t >= 1)
                            finish();
                    })
                    .Every(16);
                modalAnimations.Add(job);
            }
            box.Focus();
        }
    }
}
