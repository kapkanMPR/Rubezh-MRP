using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;using Newtonsoft.Json;using Rubezh.Camp;using Rubezh.UnityPort;using SilentOrders.Core;using UnityEngine;using UnityEngine.UIElements;
class EquipmentDetailsCheck {
 static int count;static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static void Check(bool b,string why){if(!b)throw new Exception(why);count++;}
 static void Near(float a,float b,string why){Check(Math.Abs(a-b)<.0001,why+" "+a+" != "+b);}
 static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,flags).Invoke(o,args);
 static IEnumerable<VisualElement> Walk(VisualElement v){yield return v;foreach(var c in v.Children())foreach(var n in Walk(c))yield return n;}
 static EquipmentDetails.Field Field(EquipmentDetails.Card c,string name)=>c.Left.Concat(c.Right).Single(x=>x.Name==name);
 static void Main(){
 var data=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"),CampModel.JsonSettings);var m=new CampModel(data,rng:()=>.37);var config=new Tuning();
 string save=m.Serialize();foreach(var item in data.items){var c=EquipmentDetails.Build(m,item);Check(c.Left.Count>0&&c.Right.Count>0,"two columns "+item.id);Check(c.Classes.Length>0,"classes "+item.id);Check(c.Left.Concat(c.Right).All(f=>!string.IsNullOrEmpty(f.Value)),"no absent values "+item.id);}
 Check(save==m.Serialize(),"preview does not mutate state");
 var ak=EquipmentDetails.Gun("ak");var m4=EquipmentDetails.Gun("m4");Near(Ballistics.NeutralAccuracy(config,ak),.245f,"AK no distance bonus");Near(Ballistics.NeutralAccuracy(config,m4),.2695f,"M4 no distance bonus");
 Check(Field(EquipmentDetails.Build(m,data.Item("ak")),"Точность").Value=="24,5%","AK displayed percentage");
 string commander=m.S.active[0];m.Person(commander).skills["aim"]=1;
 var aim=data.skills.Single(k=>k.id=="aim").effect.delta/100;ak.AccuracyFactor=1+Math.Abs(aim);
 var card=EquipmentDetails.Build(m,data.Item("ak"),commander);Check(Field(card,"Точность").Bonus!=null,"trained accuracy delta");Near(Ballistics.NeutralAccuracy(config,ak),config.BaseAccuracy*(1+Math.Abs(aim)),"training after conversion");
 var sim=new BattleSimulation(126);ak.Position=new V2(10,10);Near(Ballistics.Accuracy(sim,ak,new V2(10+WeaponBalance.For(ak).Range,10),false,false,ignoreCover:true),Ballistics.NeutralAccuracy(config,ak),"shared accuracy matches combat neutral point");
 m.S.story=20;m.S.balance=10000;string medic=m.Enlist("med");
 Check(Field(EquipmentDetails.Build(m,data.Item("kit"),medic),"Время использования").Bonus=="−0,9 с","medic time bonus");
 Check(Field(EquipmentDetails.Build(m,data.Item("kit"),medic),"Восполняемое здоровье").Bonus=="+5 ОЗ","medic heal bonus");
 string rifle=m.S.gear.Keys.First(id=>m.Soldier(id).role=="Стрелок");
 Check(Field(EquipmentDetails.Build(m,data.Item("rgd"),rifle),"Дальность применения").Bonus=="+1 кл.","rifleman grenade range");
 Check(Field(EquipmentDetails.Build(m,data.Item("m24"),rifle),"Точность").Bonus.StartsWith("−"),"rifleman class penalty separated from base");
 Check(Field(EquipmentDetails.Build(m,data.Item("a2")),"Защита от урона").Value=="12%","armor actual 12%");Check(Field(EquipmentDetails.Build(m,data.Item("a3")),"Защита от подавления").Value=="?","unknown not zero");
 Check(Field(EquipmentDetails.Build(m,data.Item("rgd")),"Базовый урон").Value=="32–48/9,6–14,4(4–6)","RGD fragments");Check(Field(EquipmentDetails.Build(m,data.Item("f1")),"Радиус").Value=="1,875/15 кл.","F1 radius");
 Check(Field(EquipmentDetails.Build(m,data.Item("rpg")),"Базовый урон").Value.EndsWith("/0(0)"),"RPG has no simulated fragments");
 Check(Field(EquipmentDetails.Build(m,data.Item("kit")),"Время использования").Value=="9 с","medical duration");
 Check(EquipmentDetails.Build(m,data.Item("m4")).Classes=="все","M4 classes");Check(EquipmentDetails.Build(m,data.Item("gp")).Classes=="Командир, Стрелок","GP allowed classes");
 Resources.Loader=p=>{var path="Assets/Rubezh/Resources/"+p;if(File.Exists(path+".json"))return new TextAsset{text=File.ReadAllText(path+".json")};if(File.Exists(path+".png"))return new Texture2D(1,1,TextureFormat.RGBA32,false);if(p.Contains("Fonts/"))return new Font();if(p=="FieldCamp/Camp")return new StyleSheet();if(p=="FieldCamp/CampTheme")return new ThemeStyleSheet();return null;};
 var dir=Path.Combine(Path.GetTempPath(),"rubezh-details-"+Guid.NewGuid());Directory.CreateDirectory(dir);m.S.balance=10000;m.Buy("m4");m.Equip(commander,"weapon","m4");
 var v=new NativeCampView();Screen.width=1280;Screen.height=720;Screen.safeArea=new Rect(0,0,1280,720);v.Initialize(new CampStore(Path.Combine(dir,"save.json"),m),false);v.Navigate("prepare");typeof(NativeCampView).GetField("selected",flags).SetValue(v,commander);
 foreach(var size in new[]{new[]{1280,720},new[]{800,360},new[]{393,852}}){Screen.width=size[0];Screen.height=size[1];Screen.safeArea=new Rect(0,0,size[0],size[1]);Call(v,"UpdateViewport");foreach(var item in data.items){Call(v,"ItemDetails",item.id);var labels=Walk(v.Root).OfType<Label>().Select(l=>l.text).ToArray();Check(labels.Contains("Детали снаряжения")&&labels.Contains("Очки снабжения: ??"),"unified header "+item.id);Check(labels.Contains("Базовые характеристики"),"stats section");Call(v,"Close");}}
 Call(v,"EquippedDetails","weapon",0);Check(Walk(v.Root).Any(x=>x.name=="equipment-replace"),"replace exists");Walk(v.Root).OfType<Button>().Single(x=>x.name=="equipment-replace").Click();Check(Walk(v.Root).OfType<Label>().Any(x=>x.text=="Выбрать со склада"),"replace opens picker");
 Call(v,"EquippedDetails","weapon",0);Walk(v.Root).OfType<Button>().Single(x=>x.name=="equipment-remove").Click();Check(m.S.gear[commander].weapon=="ak"&&m.Free("m4")==1,"remove returns gun to warehouse and default AK");
 m.S.story=20;m.Buy("supply");m.Equip(commander,"extra","supply");m.Buy("rpg");m.Equip(commander,"bag","rpg",0);Call(v,"EquippedDetails","bag",1);Check(Walk(v.Root).OfType<Label>().Any(x=>x.text==data.Item("rpg").name),"multi-slot tail resolves to same item");Walk(v.Root).OfType<Button>().Single(x=>x.name=="equipment-remove").Click();Check(m.S.gear[commander].bag.Take(3).All(x=>x==null)&&m.Free("rpg")==1,"remove multi-slot cleans all cells");

 m.S.balance=100000;m.S.story=20;foreach(string type in new[]{"mg","sn","rpg"})m.Enlist(type);
 foreach(string skill in new[]{"morale","morale2","morale3","optic1","optic2","optic3"})m.Person(commander).skills[skill]=1;
 foreach(var size in new[]{new[]{320,640},new[]{393,852},new[]{640,360},new[]{800,360},new[]{1280,720}}){
 Screen.width=size[0];Screen.height=size[1];Screen.safeArea=new Rect(0,0,size[0],size[1]);Call(v,"UpdateViewport");
 foreach(string owner in m.S.gear.Keys.ToArray()){
 Call(v,"FighterInfo",owner,null);var labels=Walk(v.Root).OfType<Label>().Select(l=>l.text).ToArray();
 foreach(string name in new[]{"Запас здоровья","Запас морали","Шкала подавления","Восстановление здоровья","Восстановление морали","Спад подавления","Время наведения","Скорость перезарядки","Фронтальный обзор","Вместимость сумки","Восполняемое здоровье"})Check(labels.Contains(name),"fighter stat "+owner+" "+name);
 if(owner==commander){Check(labels.Contains("+15 ед."),"trained morale capacity");Check(labels.Contains("+3 ед./с"),"trained morale recovery");Check(labels.Contains("−1,5 с"),"trained broken pause");Check(labels.Contains("+4 кл."),"trained commander vision");}
 var modal=Walk(v.Root).Single(x=>x.ClassListContains("fighter-statistics"));Check(modal.ClassListContains("equipment-phone")== (size[0]<480),"responsive column mode");
 Check(Walk(modal).OfType<ScrollView>().Any(),"statistics scroll body");Check(Walk(modal).OfType<Button>().Any(x=>x.text=="Закрыть"),"close reachable");Call(v,"Close");
 }
 Call(v,"ItemDetails","m4");var note=Walk(v.Root).OfType<Label>().Single(x=>x.ClassListContains("equipment-note"));Check(note.style.display==DisplayStyle.None,"long note initially collapsed");Walk(v.Root).OfType<Button>().Single(x=>x.name=="equipment-explain").Click();Check(note.style.display==DisplayStyle.Flex,"explanation expands");Call(v,"Close");
 }
 Call(v,"OnDestroy");Directory.Delete(dir,true);Console.WriteLine("PASS equipment detail checks="+count+"; model and UI contracts, not native Unity layout");
 }
}
