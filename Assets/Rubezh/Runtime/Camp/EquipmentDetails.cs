using System;
using System.Linq;
using System.Collections.Generic;
using Rubezh.Camp;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    // Read-only projections; no battle, atlases, equipment changes or save writes.
    public static class EquipmentDetails
    {
        public sealed class Field
        {
            public string Name, Value, Bonus;
            public Field(string name, string value, string bonus = null) { Name=name; Value=value; Bonus=bonus; }
        }
        public sealed class Card
        {
            public string Type, Classes, Note;
            public List<Field> Left = new List<Field>(), Right = new List<Field>();
        }
        static string N(float x) => x.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        static string P(float x) => N(x*100)+"%";
        static string Delta(float value, float baseline, string unit)
        {
            float d=value-baseline;
            return Math.Abs(d)<.00005f ? null : (d>0?"+":"−")+N(Math.Abs(d))+unit;
        }
        public static Soldier Gun(string id, Role role = Role.Sniper)
        {
            int variant=id=="pkm"||id=="m249"?1:id=="gp"?2:id=="m4gp"?3:id=="svd"?4:id=="m24"?5:0;
            return new Soldier { Side=Side.Green, Role=role, WeaponVariant=variant,
                WesternGun=id=="m4"||id=="m4gp"||id=="m249"||id=="m24",
                GunRoleOverride=(int)(variant>=4?Role.Sniper:variant==1?Role.MachineGunner:Role.Rifleman),
                AccuracyFactor=1, ReloadFactor=1, Health=100, Morale=1, AidSeconds=3 };
        }
        static Role RoleOf(string role) => role=="Командир"?Role.Commander:role=="Пулемётчик"?Role.MachineGunner:role=="Медик"?Role.Medic:role=="Гранатомётчик"?Role.Grenadier:role=="Стрелок"?Role.Rifleman:Role.Sniper;
        public static Card Build(CampModel model, Item item, string owner=null)
        {
            var card=new Card();
            if (item == null) { card.Type = "Не надето"; card.Classes = "все"; return card; }
            bool launcher=item.id=="rpg"||item.id=="rshg";
            card.Type=item.type=="weapon"?"Основное оружие":launcher?"Дополнительное оружие":item.type=="grenade"||item.type=="medical"||item.type=="ammo"?"Расходник":"Снаряжение";
            string[] roles={"Командир","Пулемётчик","Медик","Гранатомётчик","Стрелок","Снайпер"};
            var allowed=roles.Where(r=>model.Allowed(new Unit{role=r},item)).ToArray();
            card.Classes=allowed.Length==roles.Length?"все":string.Join(", ",allowed);
            var left=card.Left;var right=card.Right;var tuning=new Tuning();
            var skills=owner==null?new string[0]:model.Person(owner).skills.Where(p=>p.Value>0).Select(p=>p.Key).ToArray();
            Func<string,float> stat=key=>model.Data.skills.Where(k=>skills.Contains(k.id)&&k.effect!=null&&k.effect.stat==key).Sum(k=>Math.Abs(k.effect.delta)/100);
            var fighter=owner==null?null:Gun(item.type=="weapon"?item.id:model.S.gear[owner].weapon,RoleOf(model.Soldier(owner).role));
            if(fighter!=null) { var instance=model.FighterWeapon(owner);if(instance!=null&&instance.item==model.S.gear[owner].weapon)fighter.WeaponUpgrade=WeaponProgression.At(WeaponBalance.Index(fighter),instance.rarity,instance.level); }
            if(fighter!=null) { fighter.AccuracyFactor*=1+stat("accuracy");fighter.ReloadFactor*=1-stat("reloadTime"); }
            if(item.type=="weapon")
            {
                var baseline=Gun(item.id);
                if(fighter!=null&&model.S.gear[owner].weapon==item.id)baseline.WeaponUpgrade=fighter.WeaponUpgrade;
                var w=WeaponBalance.For(baseline);
                float accuracy=Ballistics.NeutralAccuracy(tuning,baseline), reload=WeaponBalance.ReloadSeconds(baseline);
                left.Add(new Field("Базовый урон",N(WeaponBalance.BulletDamage(baseline,tuning.DamageMin))+"–"+N(WeaponBalance.BulletDamage(baseline,tuning.DamageMax))));
                left.Add(new Field("Точность",P(accuracy),fighter==null?null:Delta(Ballistics.NeutralAccuracy(tuning,fighter)*100,accuracy*100," п.п.")));
                left.Add(new Field("Урон морали",N(w.Morale)));
                left.Add(new Field("Урон подавлению",N(w.Suppression)));
                right.Add(new Field("Скорость перезарядки",N(reload)+" с",fighter==null?null:Delta(WeaponBalance.ReloadSeconds(fighter),reload," с")));
                right.Add(new Field("Объём магазина",w.Magazine+" патр."));
                right.Add(new Field("Время наведения",N(w.AimSeconds)+" с"));
                if(item.id=="gp"||item.id=="m4gp")right.Add(new Field("Наведение подствольника",N(ArcedOrdnance.UnderbarrelAimSeconds)+" с"));
                right.Add(new Field("Дальность стрельбы",N(w.Range)+" кл.",fighter==null?null:Delta(WeaponBalance.For(fighter).Range,w.Range," кл.")));
                card.Note="Наведение стрелкового оружия добавляется к реакции на новую цель (0,5–1,5 с), а не к каждому выстрелу. Точность: без влияния дистанции, укрытия и состояния бойца. База — без навыков и классовых поправок; рядом — изменение у выбранного бойца. Урон — за пулю без критического попадания и защиты цели. Мораль и подавление — доли шкалы 0–1.";
            }
            else if(item.type=="armor")
            {
                int level=item.capacity-1;
                left.Add(new Field("Защита от урона",P(1-BodyArmor.DamageMultiplier(level))));
                left.Add(new Field("Игнорирование осколков",level>=2?"50%":level==1?"15%":"0%"));
                left.Add(new Field("Защита от подавления","?"));right.Add(new Field("Защита морали","?"));
                right.Add(new Field("Скорость передвижения",Delta(BodyArmor.Multiplier(level)*100,100,"%")??"0%"));right.Add(new Field("Заметность","?"));
            }
            else if(item.type=="rig")
            {
                int level=int.Parse(item.id.Substring(1))-1;
                string magazines=(3+level*2)+"; пулемёт: "+(level+1);
                if(fighter!=null){fighter.RigLevel=level;magazines=(fighter.ReserveCapacity/fighter.MagazineSize).ToString();}
                left.Add(new Field("Кол-во магазинов",magazines));left.Add(new Field("Слоты снаряжения",item.capacity.ToString()));
                right.Add(new Field("Скорость передвижения","?"));right.Add(new Field("Заметность","?"));
                card.Note="Указаны запасные магазины / короба. Заряженный магазин оружия не учитывается.";
            }
            else if(item.type=="grenade"||launcher)
            {
                bool smoke=item.id=="smoke",thermo=item.id=="rshg",heavy=item.id=="f1"||item.id=="rgo",ugl=item.id=="vog";
                float min=smoke?0:thermo?50:launcher?SpecialWeapons.DamageMin:item.id=="rgo"?36:heavy?48:ugl?30:item.id=="rgn"?28.8f:32;
                float max=smoke?0:thermo?65:launcher?SpecialWeapons.DamageMax:item.id=="rgo"?54:heavy?72:ugl?42:item.id=="rgn"?43.2f:48;
                float radius=smoke?2.8f:thermo?4.3f:launcher?SpecialWeapons.BlastRadius:heavy?ArcedOrdnance.F1Radius:ugl?ArcedOrdnance.UnderbarrelRadius:ArcedOrdnance.HandRadius;
                float fragmentRange=smoke||launcher?0:item.id=="rgo"?13:heavy?15:ugl?radius*2:5;
                float factor=heavy?.5f:.3f;
                string wave=smoke?"0":thermo?"65→50":N(min)+"–"+N(max);
                string shards=smoke||launcher?"0(0)":N(min*factor)+"–"+N(max*factor)+"("+(heavy?"8–12":ugl?"5–7":"4–6")+")";
                left.Add(new Field("Базовый урон",wave+"/"+shards));left.Add(new Field("Точность","?"));
                left.Add(new Field("Урон морали",smoke?"0":thermo?"0,7→0,49":launcher?"0,4":"0,42"));
                left.Add(new Field("Урон подавлению",smoke?"0":thermo?"0,95":launcher?"0,85":"0,65"));
                float range=launcher?(tuning.WeaponRange+3)*1.2f:ugl?(tuning.WeaponRange+3)*1.2f-2:heavy?8:9;
                right.Add(new Field("Скорость перезарядки","?"));
                right.Add(new Field("Объём магазина",launcher?"1 выстрел":"?"));
                right.Add(new Field("Дальность применения",N(range)+" кл.",fighter!=null&&fighter.Role==Role.Rifleman&&!launcher&&!ugl?"+1 кл.":null));
                right.Add(new Field("Задержка взрыва",item.id=="rgd"||item.id=="f1"?"0,5–1,5 с после падения":smoke?"При приземлении":"При попадании"));
                right.Add(new Field("Радиус",smoke?"0/0 кл.":N(radius)+"/"+N(fragmentRange)+" кл."));
                if(smoke)right.Add(new Field("Дымовое облако",N(radius)+" кл. · 20 с"));
                right.Add(new Field("Время наведения",N(launcher?SpecialWeapons.LauncherAimSeconds:ugl?ArcedOrdnance.UnderbarrelAimSeconds:ArcedOrdnance.HandAimSeconds)+" с"));
                card.Note="Урон: волна/осколок(количество). Радиус: волна/осколки. Урон волны указан в центре, до защиты цели; уменьшается к краю. "+(item.id=="rpg"?"Прямое попадание: 80–95. ":"")+"Мораль и подавление — доли шкалы 0–1.";
            }
            else if(item.type=="medical")
            {
                float time=item.id=="kit"?9:3,health=item.id=="kit"?50:22;
                float actualTime=time,actualHealth=health;
                if(fighter!=null){fighter.AidSeconds=3-.2f*skills.Count(x=>x=="aid"||x=="aid2"||x=="aid3");int rank=Enumerable.Range(1,3).Where(n=>skills.Contains("medheal"+n)).DefaultIfEmpty(0).Max();actualTime=MedicalInventory.AidTime(fighter)*(item.id=="kit"?3:1);actualHealth=health*(fighter.Role==Role.Medic?1.1f+.05f*rank:1);}
                left.Add(new Field("Время использования",N(time)+" с",Delta(actualTime,time," с")));
                right.Add(new Field("Восполняемое здоровье",N(health)+" ОЗ",Delta(actualHealth,health," ОЗ")));
                card.Note="Восстановление ограничено недостающим здоровьем.";
            }
            else if(item.type=="ammo")
            {
                left.Add(new Field("Объём",item.id=="belt"?"100 патр.":"30 патр."));right.Add(new Field("Место в сумке",CampModel.Width(item.id)+" слот"));
            }
            else
            {
                left.Add(new Field("Защита от урона","?"));left.Add(new Field("Игнорирование осколков","?"));left.Add(new Field("Защита от подавления","?"));
                right.Add(new Field("Защита морали","?"));
                left.Add(new Field("Слоты снаряжения","6",owner==null?null:Delta(model.BagLimit(owner),6," сл.")));
                right.Add(new Field("Скорость передвижения","?"));right.Add(new Field("Заметность","?"));
                card.Note=item.desc;
            }
            return card;
        }
    }
}
