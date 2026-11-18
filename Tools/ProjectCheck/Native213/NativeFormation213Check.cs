using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using UnityEngine;
using UnityEngine.UIElements;
public static class NativeFormation213Check {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static int checks;
 static object Get(object a,string n)=>a.GetType().GetField(n,F).GetValue(a);
 static void Set(object a,string n,object v)=>a.GetType().GetField(n,F).SetValue(a,v);
 static object Call(object a,string n)=>a.GetType().GetMethod(n,F).Invoke(a,null);
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static IEnumerable<VisualElement> All(VisualElement root){yield return root;foreach(var c in root.Children())foreach(var e in All(c))yield return e;}
 static Button Button(RubezhApp app,string text)=>All((VisualElement)Get(app,"formationRoot")).OfType<Button>().First(b=>b.text==text);
 static void Click(RubezhApp app,string text){Console.WriteLine("Click: "+text+" screen="+Get(app,"screen"));Button(app,text).Click();Call(app,"UpdateNativeFormationMenu");}
 public static void Main()=>Run();
 public static void Run(){
  var loader=Resources.Loader;string path=Application.persistentDataPath;
  Application.persistentDataPath=Path.Combine(Path.GetTempPath(),"rubezh-formation-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Application.persistentDataPath);
  var app=new RubezhApp();
  try{
   Resources.Loader=p=>p=="Fonts/RubezhSans"?(UnityEngine.Object)new Font():p=="FieldCamp/CampTheme"?new ThemeStyleSheet():p=="NativeBattle/Hud"?new StyleSheet():p=="NativeUI/catalog"?new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/NativeUI/catalog.json")}:p.StartsWith("NativeUI/page-")?new Texture2D(2048,2048,TextureFormat.RGBA32,false):p.StartsWith("FieldCamp/Art/canonical/")?new Texture2D(128,160,TextureFormat.RGBA32,false):null;
   Set(app,"loading",false);Set(app,"screen",6);Set(app,"campaignMode",false);Call(app,"UpdateNativeFormationMenu");
   var root=Get(app,"formationRoot");var main=Get(app,"formationMain");Call(app,"UpdateNativeFormationMenu");Check(ReferenceEquals(root,Get(app,"formationRoot"))&&ReferenceEquals(main,Get(app,"formationMain")),"formation idle keeps retained visual tree");
   Click(app,"Шире");Check(((FormationSettings)Get(app,"quickFormation")).width==2,"width button writes model");
   Click(app,"РАБОТАТЬ ГРОМКО");Check(((FormationSettings)Get(app,"quickFormation")).quietWork,"quiet mode button writes model");
   Click(app,"Планирование");var library=(BehaviorPlanLibrary)Get(app,"behaviorLibrary");Check(library.planning,"planning toggles model");
   var draft=(BehaviorPlan)Get(app,"behaviorDraft");int count=draft.rules.Count;
   Click(app,"+ Добавить блок");Click(app,"Мало боеприпасов");Check(draft.rules.Count==count+1&&draft.rules[count].when=="lowammo","event picker adds block");
   Click(app,"+");Check(draft.rules[count].delay==1,"delay increases");
   Click(app,"По возможности вместе");Check(draft.rules[count].mode=="sequence","sequence selection");
   Click(app,"+ Добавить действие");Click(app,"Помочь раненым");Check(draft.rules[count].actions.Count==2&&draft.rules[count].actions[1].type=="rescue","action picker adds action");
   Click(app,"Кто: Группа  ▾");Click(app,"Медик");Check(draft.rules[count].actions[1].who=="medic","executor picker writes role");
   Click(app,"+ Новая схема");Check(Get(app,"behaviorPendingNavigation")!=null,"unsaved navigation requires choice");
   Click(app,"Вернуться к редактированию");Check(ReferenceEquals(draft,Get(app,"behaviorDraft")),"cancel preserves draft");
   Click(app,"Сохранить схему *");Check(File.Exists(Path.Combine(Application.persistentDataPath,"behavior-plans.json")),"plan is persisted");
   Click(app,"ПОСТРОЕНИЕ\nДвойки · дистанция · роли");Click(app,"СОБРАТЬ ДВОЙКИ");
   Set(app,"formationPickerSlot",0);Set(app,"formationRebuild",true);Call(app,"UpdateNativeFormationMenu");
   var overlay=(VisualElement)Get(app,"formationOverlay");Check(overlay!=null&&!((VisualElement)Get(app,"formationMain")).enabledInHierarchy,"manual picker blocks underlying controls");
   Click(app,"Освободить ячейку");Check(((FormationSettings)Get(app,"quickFormation")).DisplaySlot(0)<0,"slot clearing updates model");
   Set(app,"screen",0);Call(app,"UpdateNativeFormationMenu");Check(Get(app,"formationViewObject")==null&&Get(app,"formationFont")==null,"screen exit releases document and font");
   Console.WriteLine("PASS native formation/planning interaction checks="+checks+"; portable UI contracts, not layout/device validation.");
  }finally{Call(app,"DisposeNativeFormationMenu");Call(app,"ReleaseImportedPortraits");Resources.Loader=loader;Directory.Delete(Application.persistentDataPath,true);Application.persistentDataPath=path;}
 }
}
