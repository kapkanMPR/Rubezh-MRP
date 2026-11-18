using System;
using System.Collections.Generic;
using System.Linq;
namespace Rubezh.Camp
{
    [Serializable]
    public sealed class WeaponInstance
    {
        public string id, item, owner;
        public int initialRarity, rarity, level = 1, xp;
        // Preview resources are never real inventory investments and cannot be refunded.
        public bool freeUpgrade = false;
        public int[] paidCloth = new int[4];
        public int paidParts;
        public List<WeaponInstance> paidCopies = new List<WeaponInstance>();
    }
    public sealed partial class CampModel
    {
        public const bool FreeWeaponWorkshop = false;
        public static int WeaponInitialRarity(string item) => item == "m4" || item == "m4gp" || item == "m249" || item == "m24" ? 2 : 1;
        public static int WeaponCap(int rarity) => rarity == 1 ? 20 : rarity == 2 ? 30 : 40;
        public static int WeaponXpCost(int level) => 50 + level * 10;
        public void SyncWeaponInstances()
        {
            if(S.weaponInstances == null) S.weaponInstances = new List<WeaponInstance>();
            foreach(var w in S.weaponInstances)
                if(w.owner != null && (!S.gear.ContainsKey(w.owner) || S.gear[w.owner].weapon != w.item)) w.owner = null;
            foreach(var item in Data.items.Where(x => x.type == "weapon"))
            {
                var copies = S.weaponInstances.Where(x => x.item == item.id).ToList();
                while(copies.Count < Owned(item.id))
                {
                    var w = new WeaponInstance { id = Guid.NewGuid().ToString("N"), item = item.id,
                        initialRarity = WeaponInitialRarity(item.id), rarity = WeaponInitialRarity(item.id) };
                    copies.Add(w); S.weaponInstances.Add(w);
                }
                while(copies.Count > Owned(item.id))
                {
                    var w = copies.Where(x => x.owner == null).OrderBy(x => x.rarity).ThenBy(x => x.level).FirstOrDefault();
                    if(w == null) break;
                    copies.Remove(w); S.weaponInstances.Remove(w);
                }
            }
            foreach(var pair in S.gear.OrderBy(x => x.Key))
            {
                if(string.IsNullOrEmpty(pair.Value.weapon)) continue;
                if(S.weaponInstances.Any(w => w.owner == pair.Key && w.item == pair.Value.weapon)) continue;
                var free = S.weaponInstances.FirstOrDefault(w => w.owner == null && w.item == pair.Value.weapon);
                if(free != null) free.owner = pair.Key;
            }
        }
        public WeaponInstance FighterWeapon(string owner)
        { SyncWeaponInstances(); return S.weaponInstances.FirstOrDefault(w => w.owner == owner); }
        public WeaponInstance WorkshopWeapon(string id)
        { SyncWeaponInstances(); var w=S.weaponInstances.FirstOrDefault(x=>x.id==id); if(w==null)throw new InvalidOperationException("Экземпляр больше недоступен"); return w; }
        public void SellWeaponInstance(string id)
        {
            var w=WorkshopWeapon(id);
            Check(w.owner==null && CanSellStock(w.item), "Продать можно только свободное оружие, оставив минимум 5 образцов");
            Check(Owned(w.item)>MaxSquadUse(w.item), "Этот экземпляр нужен комплекту другого отряда");
            S.weaponInstances.Remove(w);S.inventory[w.item]--;S.balance+=Data.Item(w.item).price*.75;
        }
        void RemoveWeaponInstanceForSale(string item)
        {
            SyncWeaponInstances();
            var w=S.weaponInstances.Where(x=>x.item==item&&x.owner==null).OrderBy(x=>x.rarity).ThenBy(x=>x.level).FirstOrDefault();
            if(w==null)throw new InvalidOperationException("Нет свободного экземпляра");
            S.weaponInstances.Remove(w);
        }
        public static WeaponInstance MaintenancePreview(WeaponInstance w, int experience)
        {
            var next=new WeaponInstance {id=w.id,item=w.item,owner=w.owner,initialRarity=w.initialRarity,rarity=w.rarity,level=w.level,xp=w.xp};
            int pool=Math.Max(0,experience)+next.xp;
            while(next.level<WeaponCap(next.rarity)&&pool>=WeaponXpCost(next.level)) {pool-=WeaponXpCost(next.level);next.level++;}
            next.xp=next.level==WeaponCap(next.rarity)?0:pool;
            return next;
        }
        public static int MaintenanceRemaining(WeaponInstance w)
        {
            if(w.level>=WeaponCap(w.rarity))return 0;
            int remaining=-w.xp;
            for(int level=w.level;level<WeaponCap(w.rarity);level++)remaining+=WeaponXpCost(level);
            return Math.Max(0,remaining);
        }
        public void ServiceWeapon(string id, int experience)
        {
            if(experience<=0||experience>100000)throw new InvalidOperationException("Выберите материалы обслуживания");
            var w=WorkshopWeapon(id);
            if(experience>MaintenanceRemaining(w))throw new InvalidOperationException("Опыт превышает остаток до предела уровня");
            int cloth=(experience+24)/25;
            Check(S.raid.cloth>=cloth,"Недостаточно ветоши");
            var next=MaintenancePreview(w,experience);
            if(w.paidCloth==null||w.paidCloth.Length!=4)w.paidCloth=new int[4];
            S.raid.cloth-=cloth;w.paidCloth[w.rarity]+=cloth;
            w.level=next.level;w.xp=next.xp;
        }
        public List<WeaponInstance> TuningCopies(string id)
        {
            var w=WorkshopWeapon(id);
            return S.weaponInstances.Where(x=>x.id!=id&&x.item==w.item&&x.owner==null).ToList();
        }
        public void TuneWeapon(string id, string[] duplicateIds = null, bool confirmReset = false)
        {
            var w=WorkshopWeapon(id);
            Check(w.rarity<3,"Максимальная редкость");
            Check(w.level==WeaponCap(w.rarity),"Сначала достигните предельного уровня");
            int count=w.rarity==1?2:3,parts=w.rarity==1?4:6;
            Check(S.raid.parts>=parts,"Недостаточно запчастей");
            Check(duplicateIds!=null&&duplicateIds.Length==count&&duplicateIds.Distinct().Count()==count,"Выберите дубликаты");
            var available=TuningCopies(id);
            var copies=duplicateIds.Select(x=>available.FirstOrDefault(v=>v.id==x)).ToList();
            Check(copies.All(x=>x!=null),"Дубликат недоступен или выдан бойцу");
            Check(S.weaponInstances.Count-count>=5&&Owned(w.item)-count>=MaxSquadUse(w.item),"Необходимо оставить минимум 5 образцов и оружие для отрядов");
            Check(confirmReset||copies.All(x=>x.level==1&&x.rarity==x.initialRarity&&x.xp==0),"Улучшения дубликатов будут сброшены. Подтвердите расход");
            if(w.paidCopies==null)w.paidCopies=new List<WeaponInstance>();
            foreach(var copy in copies)
            {
                ResetWeapon(copy.id,true);
                S.weaponInstances.Remove(copy);S.inventory[copy.item]--;
                w.paidCopies.Add(copy);
            }
            S.raid.parts-=parts;w.paidParts+=parts;
            w.rarity++;w.xp=0;
        }
        public void ResetWeapon(string id, bool rarity)
        {
            var w=WorkshopWeapon(id);
            if(w.paidCloth!=null)
                for(int i=0;i<w.paidCloth.Length;i++)
                    if(rarity||i==w.rarity){S.raid.cloth+=w.paidCloth[i];w.paidCloth[i]=0;}
            if(rarity)
            {
                S.raid.parts+=w.paidParts;w.paidParts=0;
                if(w.paidCopies!=null)
                {
                    foreach(var copy in w.paidCopies){copy.owner=null;S.weaponInstances.Add(copy);S.inventory[copy.item]++;}
                    w.paidCopies.Clear();
                }
                w.rarity=w.initialRarity;
            }
            w.level=w.rarity==w.initialRarity?1:w.rarity==2?20:30;w.xp=0;
        }
    }
}
