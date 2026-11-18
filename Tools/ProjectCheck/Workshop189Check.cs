using System;using System.IO;using System.Linq;using Newtonsoft.Json;using Rubezh.Camp;
class Workshop189Check{
static void Check(bool x,string s){if(!x)throw new Exception(s);}
static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection");}
static void Main(){
var data=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"));
var m=new CampModel(data);var item=data.items.First(x=>x.type=="weapon"&&CampModel.WeaponInitialRarity(x.id)==1).id;
m.S.inventory[item]=15;m.SyncWeaponInstances();var w=m.S.weaponInstances.First(x=>x.item==item);m.S.raid.cloth=0;
Reject(()=>m.ServiceWeapon(w.id,25));Check(w.level==1&&w.xp==0,"shortage mutation");
m.S.raid.cloth=1000;m.ServiceWeapon(w.id,25);Check(m.S.raid.cloth==999&&w.xp==25,"paid service");
m.ResetWeapon(w.id,false);Check(m.S.raid.cloth==1000&&w.level==1,"refund");
m.ServiceWeapon(w.id,CampModel.MaintenanceRemaining(w));int after=m.S.raid.cloth;Reject(()=>m.ServiceWeapon(w.id,25));Check(m.S.raid.cloth==after,"max spend");
var copies=m.TuningCopies(w.id).Take(2).ToArray();m.ServiceWeapon(copies[0].id,25);
var ids=copies.Select(x=>x.id).ToArray();m.S.raid.parts=3;Reject(()=>m.TuneWeapon(w.id,ids,true));Check(w.rarity==1,"parts shortage");
m.S.raid.parts=10;Reject(()=>m.TuneWeapon(w.id,ids));int count=m.Owned(item);m.TuneWeapon(w.id,ids,true);
Check(w.rarity==2&&m.S.raid.parts==6&&m.Owned(item)==count-2&&m.S.raid.cloth==after,"tune refund consume");
m=new CampModel(data,CampModel.Decode(m.Serialize()));w=m.WorkshopWeapon(w.id);
m.ResetWeapon(w.id,true);Check(m.S.raid.cloth==1000&&m.S.raid.parts==10&&m.Owned(item)==count,"persist refund");
m.ResetWeapon(w.id,true);Check(m.S.raid.cloth==1000&&m.S.raid.parts==10&&m.Owned(item)==count,"double refund");
w.rarity=3;w.level=40;m.ResetWeapon(w.id,true);Check(m.S.raid.cloth==1000&&m.S.raid.parts==10,"legacy free refund");
Console.WriteLine("PASS: shortages, paid maintenance, level cap, duplicate warning, paid tuning, donor refund, save/load, free resets, double-refund prevention, legacy free upgrades.");
}}
