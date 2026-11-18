using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Rubezh.Camp;
using Rubezh.UnityPort;
using SilentOrders.Core;
class Program {
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Reject(Action a,string name){bool rejected=false;try{a();}catch{rejected=true;}Check(rejected,name);}
 static Catalog Cat()=>JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("base179/source/Assets/Rubezh/Resources/FieldCamp/catalog.json"));
 static CampModel New()=>new CampModel(Cat(),rng:()=>.5);
 static void Main(){
 var m=New();
 Check(m.WeaponStock==5&&m.Owned("ak")==5,"five rifles");
 Check(m.S.active.All(m.Armed),"starter equipped");
 Check(m.S.gear.Values.All(g=>g.armor==null&&g.rig==null&&g.extra==null&&g.tourniquet==null&&g.rigSlots.Count==0&&g.bag.Count==0),"no starter extras");
 Check(m.S.inventory.Where(p=>p.Value>0).All(p=>p.Key=="ak"),"no free equipment");
 string uid=m.S.active[1];m.Strip(uid);
 Check(!m.Armed(uid)&&m.Free("ak")==1,"strip returns finite rifle");
 Reject(()=>m.Sell("ak"),"five-rifle floor");
 Check(!m.CanSellStock("ak"),"sell button floor");
 m.Equip(uid,"weapon","ak");Check(m.Free("ak")==0,"equip consumes free stock");
 var reserve=m.S.gear.Keys.FirstOrDefault(x=>!m.S.active.Contains(x));
 if(reserve!=null)Reject(()=>m.Equip(reserve,"weapon","ak"),"no infinite rifles");
 m.S.inventory["m4"]=1;m.Sell("m4");Check(m.WeaponStock==5,"sixth rifle sale allowed");
 m.S.inventory["a1"]=2;m.S.inventory["v1"]=1;m.Equip(uid,"armor","a1");m.Equip(uid,"rig","v1");
 Check(m.Used("a1")==1&&m.Free("a1")==1&&m.WarehouseCount("a1",true)==1&&m.WarehouseCount("a1",false)==2,"warehouse totals");
 Check(m.WarehouseCount("v1",true)==0,"issued only hidden");
 m.S.inventory["smoke"]=1;m.Equip(uid,"rigSlots","smoke");
 m.Equip(uid,"rig",null);Check(m.S.gear[uid].rigSlots.Count==0&&m.Free("smoke")==1,"unrig frees pocket contents");
 Reject(()=>m.Equip(uid,"rigSlots","smoke"),"no pockets without rig");
 m.S.inventory["band"]=1;m.Equip(uid,"tourniquet","band");Check(m.S.gear[uid].tourniquet=="band","tourniquet slot retained");
 m.Equip(uid,"armor",null);Check(m.S.gear[uid].armor==null&&m.Free("a1")==2,"remove basic armor");
 m.Normalize();Check(m.S.gear[uid].rig==null&&m.S.gear[uid].armor==null,"normalize does not auto equip");
 m.S.hideIssued=true;var restored=new CampModel(Cat(),CampModel.Decode(m.Serialize()));
 Check(restored.S.hideIssued&&restored.Owned("ak")==5&&restored.S.gear[uid].rig==null,"save roundtrip");
 // Historical unlimited gear becomes finite once; squad presets share stock.
 var legacy=New().S;legacy.finiteEquipmentVersion=0;legacy.inventory.Clear();
 foreach(var g in legacy.gear.Values){g.weapon="ak";g.armor="a1";g.rig="v1";}
 int n=legacy.gear.Count;
 var old=new CampModel(Cat(),legacy);Check(old.Owned("ak")==Math.Max(5,n)&&old.Owned("a1")==n&&old.Owned("v1")==n,"legacy items retained");
 string snapshot=old.Serialize();old.Normalize();Check(old.Serialize()==snapshot,"migration idempotence");
 old.Strip(old.S.active[0]);old.Normalize();Check(old.S.gear[old.S.active[0]].weapon==null,"no resurrection on load");
 old.CreateSquad();Check(old.S.gear.Values.All(g=>g.weapon==null&&g.armor==null&&g.rig==null),"new squad no free gear");
 old.SelectSquad(old.S.squads[0].id);Check(old.Owned("a1")==n,"preset no duplication");
 m.S.story=20;m.S.balance=10000;
 for(int story=0;story<=20;story++){
  m.S.story=story;
  foreach(var c in LootCrates.All){var p=LootCrates.Pool(m,c);
   Check(p.Count>0&&p.All(e=>m.Unlocked(e.Item)),"only unlocked");Check(p.Sum(e=>e.DisplayHundredths)==10000,"display 100%");
   Check(Math.Abs(p.Sum(e=>e.Probability)-1)<1e-12,"sum one");Check(p.SequenceEqual(p.OrderBy(e=>e.Probability).ThenBy(e=>e.Item.id)),"ascending");
  }
 }
 m.S.story=0;Check(LootCrates.Pool(m,LootCrates.Find("weapon")).Single().Item.id=="ak","initial weapon crate ak100");
 var basics=LootCrates.Pool(m,LootCrates.Find("equipment"));Check(basics.Count==2&&basics.Any(x=>x.Item.id=="a1")&&basics.Any(x=>x.Item.id=="v1"),"basic gear in drops");
 m.S.story=20;
 foreach(var c in LootCrates.All){var pool=LootCrates.Pool(m,c);var counts=pool.ToDictionary(e=>e.Item.id,e=>0);var rng=new Random(93);
  for(int i=0;i<100000;i++)counts[LootCrates.Pick(pool,rng.NextDouble()).Item.id]++;
  foreach(var e in pool){double actual=counts[e.Item.id]/100000.0;Check(Math.Abs(actual-e.Probability)<.006,"drop distribution");Console.WriteLine(c.Id+"/"+e.Item.id+": "+e.Probability.ToString("P2")+"; observed "+actual.ToString("P2"));}
 }
 string dir=Path.Combine(Path.GetTempPath(),"finite-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
 var store=new CampStore(Path.Combine(dir,"save.json"),m);int total=m.S.inventory.Values.Sum();double bal=m.S.balance;
 store.Change(()=>m.BuyLootCrate("weapon"));Check(m.S.inventory.Values.Sum()==total+1&&m.S.balance==bal-300,"atomic purchase");
 Reject(()=>store.Change(()=>m.BuyLootCrate("weapon")),"double purchase blocked");
 store.Change(()=>m.S.lootReceipts.Last().acknowledged=true);
 string before=m.Serialize();string blocked=Path.Combine(dir,"directory");Directory.CreateDirectory(blocked);
 Reject(()=>new CampStore(blocked,m).Change(()=>m.BuyLootCrate("equipment")),"save error");Check(before==m.Serialize(),"save failure rollback");
 // Real core and actual CampUnit adapter (not reimplemented rules).
 Check(Math.Abs(BodyArmor.DamageMultiplier(0)-.95f)<.00001,"light armor five percent");
 Check(BodyArmor.DamageMultiplier(-1)==1,"no armor zero protection");
 for(int i=0;i<1000;i++)Check(!BodyArmor.BlocksFragment(0,true,new Rng(i+1)),"light armor no fragment immunity");
 foreach(int variant in new[]{0,1,2,3,4,5}){
  var b=new BattleSimulation(42);var u=new CampUnit{role=5,weapon=variant,rig=-1,armor=-1,skills=new string[0],pockets=new string[0],bag=new string[0],name="Test"};
  var kits=Enumerable.Range(0,5).Select(i=>i==1?u.Base().Encode():new Kit().Encode());
  Check(SquadLoadout.Apply(b,string.Join(";",kits)),"base kit schema");var soldier=b.Soldiers[1];u.Apply(b,soldier);
  Check(soldier.Ammo==soldier.MagazineSize,"loaded magazine retained without rig");
  Check(soldier.Reserve==(variant==1?0:soldier.MagazineSize),"no rig exact one spare by weapon");
  Check(soldier.RigLevel==-1&&soldier.ArmorLevel==-1,"absent levels applied");
  Check(!GrenadePockets.CanAdd(soldier,0),"no grenade pocket refill");
  u.noWeapon=true;u.Apply(b,soldier);Check(!soldier.Deployed&&!soldier.Alive&&soldier.Ammo==0&&soldier.Reserve==0,"unarmed never deploys");
 }
 int battleTicks=0;
 for(int seed=1;seed<=20;seed++){
  var b=new BattleSimulation(seed);
  var units=Enumerable.Range(0,5).Select(i=>new CampUnit{role=i==0?1:5,weapon=i%2==0?0:1,rig=-1,armor=i%2==0?-1:0,noWeapon=i==seed%5,name="Test"+i,skills=new string[0],pockets=new string[0],bag=new string[0]}).ToArray();
  Check(SquadLoadout.Apply(b,string.Join(";",units.Select(u=>u.Base().Encode()))),"battle loadout");
  for(int i=0;i<5;i++)units[i].Apply(b,b.Soldiers[i]);
  OperationMode.Apply(b);
  while(b.Outcome==0&&b.Time<600){b.Step();battleTicks++;}
  Check(!b.Soldiers[seed%5].Deployed&&!b.Soldiers[seed%5].Alive,"unarmed stays out after battle");
 }
 Console.WriteLine("20 battle runs; " + battleTicks + " simulation ticks; no exceptions.");
 m.Reset();Check(m.WeaponStock==5&&m.Owned("a1")==0&&m.S.gear.Values.All(g=>g.armor==null),"new game reset");
 Directory.Delete(dir,true);Console.WriteLine("PASS: "+checks+" checks; 300000 draws. Model + real combat core + CampUnit adapter.");
 }
}
