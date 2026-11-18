using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rubezh.Camp;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        string warehouseGroup="all", warehouseSelection, shopSelection;
        void InventoryStorePage()
        {
            if (storeTab != "warehouse") { LootShopPage(); return; }
            bool warehouse=storeTab=="warehouse";
            HubBackdrop(warehouse?"warehouse-background.png":"shop-background.png");
            main.Add(V("store-shade"));
            var page=ListPage(warehouse?"Склад":"Магазин",false);
            page.AddToClassList("inventory-page");
            var body=Row(page,"inventory-body");
            var categories=Scroll(body,"inventory-categories");
            string group=warehouse?warehouseGroup:storeGroup;
            string[] ids=warehouse?new[]{"all","weapons","equipment","consumables","resources"}:new[]{"weapons","equipment","consumables"};
            string[] titles=warehouse?new[]{"Всё","Оружие","Экипировка","Расходники","Ресурсы"}:new[]{"Оружие","Снаряжение","Расходники"};
            for(int n=0;n<ids.Length;n++)
            {
                string id=ids[n];
                var tab=Button(categories,titles[n],()=>{if(warehouse)warehouseGroup=id;else storeGroup=id;Render();},group==id?"on":"");
                tab.name=(warehouse?"warehouse-group-":"shop-group-")+id;
            }
            var list=Scroll(body,"inventory-list");
            list.horizontalScrollerVisibility=ScrollerVisibility.Hidden;
            var grid=V("inventory-grid row wrap");list.Add(grid);
            grid.RegisterCallback<GeometryChangedEvent>(e=>{
                float side=Mathf.Max(1,(e.newRect.width-4-7*4)/8);
                foreach(var cell in grid.Children()){
                    cell.style.width=side;cell.style.height=side;
                    cell.style.marginRight=(grid.IndexOf(cell)+1)%8==0?0:4;
                    cell.style.marginBottom=4;
                }
            });
            var detail=V("inventory-detail");body.Add(detail);
            var content=Scroll(detail,"inventory-detail-scroll");
            var actions=V("inventory-actions");detail.Add(actions);
            var items=M.Data.items.Where(i=>!CampModel.Personal(i.id)&&(warehouse?M.WarehouseCount(i.id,S.hideIssued)>0:M.Unlocked(i))&&(group=="all"||ShopGroup(i)==group))
                .OrderByDescending(i=>i.rarity=="Редкое"?1:0).ThenByDescending(i=>i.price).ThenByDescending(i=>i.capacity).ThenBy(i=>i.name).ToArray();
            string selection=warehouse?warehouseSelection:shopSelection;
            bool resources=warehouse&&(group=="resources"||group=="all");
            string[] resourceIds={"@manuals","@books","@raidCloth","@raidParts"};
            string[] resourceNames={"Методички","Пособия опыта","Ветошь","Запчасти"};
            int[] counts={S.career.manuals,S.career.levelBooks,S.raid.cloth,S.raid.parts};
            M.SyncWeaponInstances();
            var available=items.SelectMany(i=>i.type=="weapon"?S.weaponInstances.Where(w=>w.item==i.id&&(!S.hideIssued||w.owner==null)).Select(w=>"weapon:"+w.id):new[]{i.id}).Concat(resources?resourceIds.Where((id,n)=>counts[n]>0):new string[0]).ToArray();
            if(!available.Contains(selection))selection=available.FirstOrDefault();
            if(warehouse)warehouseSelection=selection;else shopSelection=selection;
            Action<string> select=id=>
            {
                if(warehouse)warehouseSelection=id;else shopSelection=id;
                foreach(var cell in grid.Children()) { cell.RemoveFromClassList("selected");if(cell.name=="item-"+id)cell.AddToClassList("selected"); }
                content.Clear();actions.Clear();StoreItemPanel(content,actions,id,warehouse);
            };
            foreach(var item in items)
            {
                string id=item.id;
                if(warehouse && item.type=="weapon")
                {
                    M.SyncWeaponInstances();
                    foreach(var instance in S.weaponInstances.Where(x=>x.item==id&&(!S.hideIssued||x.owner==null)))
                    {
                        var copy=instance;
                        var weaponCell=Button(grid,"",()=>select("weapon:"+copy.id),"inventory-cell"+(selection=="weapon:"+copy.id?" selected":""));
                        weaponCell.name="item-weapon:"+copy.id;weaponCell.Add(OutfitWeaponArt(item,copy.rarity));
                        WorkshopColor(weaponCell,instance.rarity);
                        Label(weaponCell,"Ур. "+instance.level+(instance.owner==null?"":" · выдано"),"inventory-item-amount");
                        weaponCell.tooltip=item.name+" · "+WorkshopRank(instance.rarity);
                    }
                    continue;
                }
                var cell=Button(grid,"",()=>select(id),"inventory-cell"+(item.rarity=="Редкое"?" rare-item":"")+(selection==id?" selected":""));
                WeaponRarityCard(cell, item); cell.name="item-"+id;cell.tooltip=item.name;cell.Add(OutfitItemArt(item.img));

                Label(cell,warehouse?"×"+M.WarehouseCount(id,S.hideIssued):"◈ "+item.price,"inventory-item-amount");
            }
            if(resources)for(int n=0;n<resourceIds.Length;n++)
            {
                if(counts[n]<=0)continue;
                string id=resourceIds[n];var cell=Button(grid,"",()=>select(id),"inventory-cell"+(selection==id?" selected":""));cell.name="item-"+id;
                string resourceArt=id=="@raidCloth"?"resource-cloth.png":id=="@raidParts"?"resource-parts.png":null;
                if(resourceArt!=null)cell.Add(OutfitItemArt(resourceArt));else Label(cell,"▤","inventory-resource-icon");cell.tooltip=resourceNames[n];Label(cell,"×"+counts[n],"inventory-item-amount");
            }
            if(selection==null) { Label(list,"Нет предметов этой категории.","paragraph");Label(content,"Выберите предмет","paragraph"); }
            else StoreItemPanel(content,actions,selection,warehouse);
        }
        void StoreItemPanel(VisualElement content,VisualElement actions,string id,bool warehouse)
        {
            if(id.StartsWith("weapon:",StringComparison.Ordinal)) { WarehouseWeaponPanel(content,actions,M.WorkshopWeapon(id.Substring(7)));return; }
            if(id.StartsWith("@",StringComparison.Ordinal))
            {
                if(id.StartsWith("@raid",StringComparison.Ordinal))
                {
                    bool cloth=id=="@raidCloth",parts=id=="@raidParts";
                    if(cloth||parts){var resourceArt=Art(cloth?"resource-cloth.png":"resource-parts.png");resourceArt.style.height=180;resourceArt.style.flexShrink=0;content.Add(resourceArt);}
                    Label(content,cloth?"Ветошь":"Запчасти","h2");
                    Stat(content,"Количество",(cloth?S.raid.cloth:S.raid.parts).ToString());
                    Label(content,cloth?"Обслуживание оружия: 25 опыта за кусочек.":"Используются для тюнинга оружия.","paragraph");
                    return;
                }
                bool manual=id=="@manuals";
                Label(content,manual?"Методички":"Пособия опыта","h2");
                Stat(content,"Количество",(manual?S.career.manuals:S.career.levelBooks).ToString());
                Label(content,manual?"Используются для изучения навыков бойцов в тренировке.":"Используются для повышения уровня бойцов.","paragraph");return;
            }
            var item=M.Data.Item(id);ItemDescription(content,item);
            Stat(content,"Всего",M.WarehouseCount(id,warehouse&&S.hideIssued).ToString());if(!(warehouse&&S.hideIssued))Stat(content,"На выдаче",M.Used(id).ToString());Stat(content,"Свободно",M.Free(id).ToString());
            if(warehouse)
            {
                if(item.type=="weapon") Button(actions,"Улучшить",()=>WorkshopPicker(id),"gold");
                var holders=M.Roster().Where(u=>S.gear[u.id].All().Contains(id)).Select(u=>u.name).ToArray();
                if(holders.Length>0&&!S.hideIssued)Label(content,"Выдано: "+string.Join(", ",holders),"paragraph");
                Button(actions,"Продать · ◈ "+Money(item.price*.75),()=>Change(()=>M.Sell(id),()=>Toast("Предмет продан"))).SetEnabled(M.CanSellStock(id));
            }
            else Button(actions,"Купить · ◈ "+item.price,()=>Change(()=>M.Buy(id),()=>Toast("Предмет добавлен на склад")),"gold").SetEnabled(M.Unlocked(item)&&S.balance>=item.price);
        }
    }
}
