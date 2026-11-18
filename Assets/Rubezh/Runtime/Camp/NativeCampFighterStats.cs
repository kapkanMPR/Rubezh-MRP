using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.UIElements;
using Rubezh.Camp;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        static EquipmentDetails.Field InfoStat(string name,string value,string bonus=null) => new EquipmentDetails.Field(name,value,bonus);
        static string InfoNumber(float n) => n.ToString("0.###",System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        void InfoSection(VisualElement parent,string title,IEnumerable<EquipmentDetails.Field> left,IEnumerable<EquipmentDetails.Field> right)
        {
            Label(parent,title,"equipment-section");
            var columns=Row(parent,"equipment-columns");var a=V("equipment-column");var b=V("equipment-column");columns.Add(a);columns.Add(b);
            foreach(var field in left)EquipmentField(a,field);
            foreach(var field in right)EquipmentField(b,field);
        }
        void ShowFighterStatistics(string id,Action returnToCard)
        {
            var unit=M.Soldier(id);var gear=S.gear[id];var person=M.Person(id);
            bool Has(string skill)=>M.Has(id,skill);
            int Ranks(string prefix)=>Enumerable.Range(1,3).Count(n=>Has(prefix+n));
            int morale=(Has("morale")?1:0)+(Has("morale2")?1:0)+(Has("morale3")?1:0);
            int armor=CampModel.GearLevel(gear.armor);
            var config=new Tuning();
            var gun=EquipmentDetails.Gun(gear.weapon);gun.RigLevel=CampModel.GearLevel(gear.rig);
            var weaponInstance=M.FighterWeapon(id);if(weaponInstance!=null)gun.WeaponUpgrade=WeaponProgression.At(WeaponBalance.Index(gun),weaponInstance.rarity,weaponInstance.level);
            var weapon=EquipmentDetails.Build(M,M.Data.Item(gear.weapon),id);
            float sight=unit.role=="Снайпер"?20:unit.role=="Пулемётчик"?config.SightRange+6:unit.role=="Гранатомётчик"?config.SightRange+9:unit.role=="Командир"?16:config.SightRange+2;
            int sightRank=unit.role=="Командир"?Enumerable.Range(1,3).Where(n=>Has("optic"+n)).DefaultIfEmpty(0).Max():unit.role=="Снайпер"?Enumerable.Range(1,3).Where(n=>Has("vision"+n)).DefaultIfEmpty(0).Max():0;
            int sightBonus=unit.role=="Командир"&&sightRank==3?4:sightRank;
            Modal(unit.name+" · Инфо",(c,f)=>
            {
                Label(c,unit.role+" · Уровень "+person.level,"fighter-role");
                Label(c,"Параметры на начало боя. База и изменения от навыков, класса и экипировки показаны раздельно. Ситуативные эффекты указаны в пояснениях.","equipment-note");
                InfoSection(c,"Здоровье, мораль и подавление",new[]{
                    InfoStat("Запас здоровья",InfoNumber(gun.Health)+" ОЗ"),
                    InfoStat("Запас морали","100 ед.",morale>0?"+"+(morale*5)+" ед.":null),
                    InfoStat("Шкала подавления","0–100 ед.")
                },new[]{
                    InfoStat("Восстановление здоровья","0 ОЗ/с"),
                    InfoStat("Восстановление морали","1,2 ед./с",morale>0?"+"+morale+" ед./с":null),
                    InfoStat("Спад подавления","8 ед./с; в укрытии 16 ед./с"),
                    InfoStat("Пауза восстановления после слома","2 с",morale>0?"−"+InfoNumber(morale*.5f)+" с":null)
                });
                Label(c,"Здоровье восстанавливается лечением. Мораль восстанавливается вне входящего огня; союзник ближе 6 клеток добавляет 0,8 ед./с, командир — 2,5 ед./с. После слома нужен также перерыв во входящем огне более 3 с. Подавление накапливается от огня и снижается со временем; это не запас защиты.","equipment-note");
                if (M.Armed(id)) {
                InfoSection(c,"Оружие · "+M.ItemName(gear.weapon,"Не выдано"),weapon.Left,weapon.Right);
                InfoSection(c,"Дополнительные параметры огня",new[]{
                    InfoStat("Интервал между выстрелами",InfoNumber(WeaponBalance.For(gun).FireSeconds)+" с"),
                    InfoStat("Разброс относительно базового",InfoNumber(WeaponBalance.For(gun).Spread*100)+"%"),
                    InfoStat("Реакция на новую цель","0,5–1,5 с")
                },new[]{
                    InfoStat("Критическое попадание",gun.GunSniper?InfoNumber((unit.role=="Стрелок"?.1f:.2f)*100+WeaponBalance.For(gun).CriticalBonus*100)+"%":"0% обычно"),
                    InfoStat("Урон критического попадания","×1,5"),
                    InfoStat("Запас патронов",gun.ReserveCapacity+" + "+gun.MagazineSize+" в оружии")
                });
                Label(c,"Точность без дистанционных бонусов и боевых штрафов. Наведение прибавляется к реакции на новую цель. Очереди, подавление и команды могут менять фактический темп огня; особые приёмы могут давать критические попадания.","equipment-note");
                } else Label(c,"Без оружия — не выходит в бой", "paragraph");
                var defense=EquipmentDetails.Build(M,M.Data.Item(gear.armor),id);
                InfoSection(c,"Защита · "+M.ItemName(gear.armor,"Без бронежилета"),defense.Left,defense.Right);
                InfoSection(c,"Движение и наблюдение",new[]{
                    InfoStat("Базовая скорость",InfoNumber(config.MoveSpeed)+" кл./с",armor>0?"−"+InfoNumber(config.MoveSpeed*(1-BodyArmor.Multiplier(armor)))+" кл./с":null),
                    InfoStat("Фронтальный обзор",InfoNumber(sight)+" кл.",sightBonus>0?"+"+sightBonus+" кл.":null)
                },new[]{
                    InfoStat("Разворот","180°/с; при подавлении >60 — 85°/с"),
                    InfoStat("Заметность","?")
                });
                if(unit.role=="Стрелок"&&gun.GunSniper)Label(c,"Снайперская винтовка у стрелка: обзор с места постепенно растёт с 16 до 18 клеток. Подавление и сектора обзора изменяют видимость в бою.","equipment-note");
                else Label(c,"Указан базовый темп передвижения с бронёй. Бег, приседание, контузия и выбранный способ движения применяются в бою. Обзор зависит от сектора и подавления.","equipment-note");
                var rig=EquipmentDetails.Build(M,M.Data.Item(gear.rig),id);
                InfoSection(c,"Боекомплект · "+M.ItemName(gear.rig,"Без разгрузки"),rig.Left,rig.Right);
                if (gear.rig == null) Label(c,"Карманы: 0. Запас: " + (!M.Armed(id) ? "нет оружия" : gun.GunMachineGunner ? "0 коробов" : "1 магазин") + ". Отдельное место под жгут сохраняется.", "paragraph");
                var bagBonus=(unit.role=="Стрелок"?Ranks("rifpack"):unit.role=="Медик"?Ranks("medpack"):0)*2;
                bool bag=gear.extra=="medbag"||gear.extra=="supply";
                InfoSection(c,"Дополнительное снаряжение",new[]{
                    InfoStat("Дополнительные слоты",M.ExtraSlots(id).ToString()),
                    InfoStat("Вместимость сумки",bag?"6 сл.":"0",bag&&bagBonus>0?"+"+bagBonus+" сл.":null)
                },new[]{
                    InfoStat("Жгуты всего",((gear.tourniquet=="band"?(unit.role=="Медик"?2:1):0)+gear.bag.Count(x=>x=="band")).ToString()),
                    InfoStat("Аптечки",gear.bag.Count(x=>x=="kit").ToString()),
                    InfoStat("Патроны в сумке",(gear.bag.Count(x=>x=="ammo")*30+gear.bag.Count(x=>x=="belt")*100)+" патр."),
                    InfoStat("Подствольный выстрел в оружии",gear.weapon=="gp"||gear.weapon=="m4gp"?"1":"0")
                });
                var band=EquipmentDetails.Build(M,M.Data.Item("band"),id);var kit=EquipmentDetails.Build(M,M.Data.Item("kit"),id);
                InfoSection(c,"Лечение · жгут",band.Left,band.Right);
                InfoSection(c,"Лечение · аптечка",kit.Left,kit.Right);
                Label(c,"Показана эффективность лечения этим бойцом; нужен соответствующий расходник. Восстановление ограничено недостающим здоровьем.","equipment-note");
                foreach(var itemId in gear.All().Where(x=>x!=null&&!x.StartsWith("@",StringComparison.Ordinal)).Distinct())
                {
                    var item=M.Data.Item(itemId);
                    if(item.type!="grenade"&&itemId!="rpg"&&itemId!="rshg")continue;
                    var detail=EquipmentDetails.Build(M,item,id);InfoSection(c,item.name+" · "+gear.All().Count(x=>x==itemId)+" шт.",detail.Left,detail.Right);
                }
                Label(c,"Изученные навыки и приёмы","equipment-section");
                var learned=LearnedSkillIcons(id).ToArray();
                if(learned.Length==0)Label(c,"Пока нет изученных навыков","sub");
                foreach(var skillId in learned){var skill=M.Data.skills.First(x=>x.id==skillId);Button(c,skill.name,()=>{trainee=id;NodeInfo(skill.id,()=>FighterInfo(id,returnToCard));},"equipment-skill");}
                if(returnToCard!=null)Button(f,"← К бойцу",returnToCard);
                Button(f,"Закрыть",Close);
            },"equipment-modal fighter-statistics");
        }
    }
}
