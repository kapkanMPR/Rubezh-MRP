#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Rubezh.UnityPort.Editor {
 public sealed partial class RubezhLevelEditorWindow {
  int campaignSelection=1,groupSelection,soldierSelection;
  Rubezh.Camp.Catalog campaignData;
  readonly Dictionary<int,int> recommended=new Dictionary<int,int>();
  void DrawCampaignToolbar(){
   if(campaignData==null){var data=Resources.Load<TextAsset>("FieldCamp/catalog");if(data!=null)campaignData=Newtonsoft.Json.JsonConvert.DeserializeObject<Rubezh.Camp.Catalog>(data.text);}
   var names=new string[20];for(int i=0;i<20;i++)names[i]=(i+1)+" · "+(campaignData!=null&&campaignData.missions!=null&&i<campaignData.missions.Length?campaignData.missions[i].name:"Миссия");
   EditorGUILayout.BeginHorizontal();
   if(GUILayout.Button("<",GUILayout.Width(25)))campaignSelection=Math.Max(1,campaignSelection-1);
   campaignSelection=EditorGUILayout.Popup(campaignSelection-1,names,GUILayout.MinWidth(180))+1;
   if(GUILayout.Button(">",GUILayout.Width(25)))campaignSelection=Math.Min(20,campaignSelection+1);
   if(GUILayout.Button("Открыть уровень"))OpenCampaign(false);
   if(GUILayout.Button("Базовая локация"))OpenCampaign(true);
   if(GUILayout.Button("Экспорт JSON"))ExportLocation();
   if(GUILayout.Button("Импорт JSON"))ImportLocation();
   if(GUILayout.Button("Кампания ↓"))ImportCampaign();if(GUILayout.Button("Кампания ↑"))ExportCampaign();
   EditorGUILayout.EndHorizontal();
  }
  bool CanSwitch(){
   if(level==null||AssetDatabase.Contains(level)||!EditorUtility.IsDirty(level))return true;
   return EditorUtility.DisplayDialog("Несохранённая локация","Текущая локация ещё не сохранена. Переключиться и отказаться от изменений?","Переключиться","Остаться");
  }
  void OpenCampaign(bool original){
   if(!CanSwitch())return;
   RubezhLevel selectedLevel=null;
   if(!original)selectedLevel=RubezhLevelRuntime.Find(true,campaignSelection);
   else {var baseline=BuiltinCampaign.Find(campaignSelection);if(baseline!=null)selectedLevel=Instantiate(baseline);}
   if(selectedLevel==null){selectedLevel=LevelCampaignFactory.Create(campaignSelection);EditorUtility.SetDirty(selectedLevel);}
   ClearThumbnails();ClearStudioTextures();
   level=selectedLevel;groupSelection=soldierSelection=0;pan=Vector2.zero;zoom=1;SaveLast();Repaint();
  }
  void SaveWorkingAsset(){
   string error;if(!LevelValidation.Valid(level,out error)){EditorUtility.DisplayDialog("Рубеж",error,"OK");return;}
   EnsureFolders();string path=EditorUtility.SaveFilePanelInProject("Сохранить отдельную локацию",level.name,"asset","Файл уровня",LevelFolder);
   if(string.IsNullOrEmpty(path))return;
   if(!path.StartsWith(LevelFolder+"/",StringComparison.Ordinal)){EditorUtility.DisplayDialog("Рубеж","Для использования в игре сохраните в "+LevelFolder,"OK");return;}
   if(AssetDatabase.LoadAssetAtPath<RubezhLevel>(path)!=null){EditorUtility.DisplayDialog("Рубеж","Выберите новое имя; существующую карту можно открыть и сохранить отдельно.","OK");return;}
   if(!ResolveAssignments())return;
   level.name=Path.GetFileNameWithoutExtension(path);AssetDatabase.CreateAsset(level,path);AssetDatabase.SaveAssets();SaveLast();
  }
  bool ResolveAssignments(){
   if(!level.enabledInGame)return true;
   var conflicting=new List<RubezhLevel>();
   foreach(var guid in AssetDatabase.FindAssets("t:RubezhLevel",new[]{LevelFolder})){
    var other=AssetDatabase.LoadAssetAtPath<RubezhLevel>(AssetDatabase.GUIDToAssetPath(guid));
    if(other==null||other==level||!other.enabledInGame)continue;
    if(level.campaignMission>0&&level.campaignMission==other.campaignMission||level.useInQuickBattle&&other.useInQuickBattle)conflicting.Add(other);
   }
   if(conflicting.Count==0)return true;
   if(!EditorUtility.DisplayDialog("Назначение локации","У этого сценария уже есть карта. Переназначить сценарий на текущую? Сами файлы других карт сохранятся.","Переназначить","Отмена"))return false;
   foreach(var other in conflicting){Undo.RecordObject(other,"Переназначить сценарий");if(level.campaignMission>0&&other.campaignMission==level.campaignMission)other.campaignMission=0;if(level.useInQuickBattle)other.useInQuickBattle=false;EditorUtility.SetDirty(other);}
   return true;
  }
  void ExportLocation(){
   if(level==null)return;string error;if(!LevelValidation.Valid(level,out error)){EditorUtility.DisplayDialog("Рубеж",error,"OK");return;}
   string path=EditorUtility.SaveFilePanel("Экспорт отдельной локации","",level.name,"json");if(string.IsNullOrEmpty(path))return;
   level.formatVersion=6;File.WriteAllText(path,LevelJson.ToPortable(JsonUtility.ToJson(level,true)));ShowNotification(new GUIContent("Локация экспортирована"));
  }
  [Serializable] class FileHeader {public int formatVersion;}
  void ImportLocation(){
   if(!CanSwitch())return;string path=EditorUtility.OpenFilePanel("Импорт локации","","json");if(string.IsNullOrEmpty(path))return;
   RubezhLevel data=null;
   try {
    string json=File.ReadAllText(path);var header=JsonUtility.FromJson<FileHeader>(json);
    if(header==null||(header.formatVersion!=2&&header.formatVersion!=3&&header.formatVersion!=4&&header.formatVersion!=5&&header.formatVersion!=6))throw new InvalidDataException("Ожидается экспорт локации редактора v2–v6.");
    data=CreateInstance<RubezhLevel>();JsonUtility.FromJsonOverwrite(LevelJson.ToUnity(json),data);if(!json.Contains("\"count2\"")&&data.defenders!=null)foreach(var g in data.defenders)foreach(var slot in g.soldiers)if(slot.kit!=null)slot.kit.NormalizePockets();string error;
    if(!LevelValidation.Valid(data,out error))throw new InvalidDataException(error);
    data.name=Path.GetFileNameWithoutExtension(path);level=data;EditorUtility.SetDirty(level);groupSelection=soldierSelection=0;Repaint();
   }catch(Exception ex){if(data!=null)DestroyImmediate(data);EditorUtility.DisplayDialog("Ошибка импорта",ex.Message,"OK");}
  }
  void DrawBriefingEditor(){
   GUILayout.Label("БРИФИНГ ОПЕРАЦИИ",EditorStyles.boldLabel);
   int mission=level.campaignMission>0?level.campaignMission:campaignSelection;
   EditorGUILayout.LabelField("Миссия кампании",mission.ToString());
   if(level.campaignMission==0)EditorGUILayout.HelpBox("Для показа брифинга в игре назначьте карту на миссию 1–20.",MessageType.Warning);
   if(campaignData==null){var file=Resources.Load<TextAsset>("FieldCamp/catalog");if(file!=null)campaignData=Newtonsoft.Json.JsonConvert.DeserializeObject<Rubezh.Camp.Catalog>(file.text);}
   var m=campaignData!=null&&campaignData.missions!=null&&mission>0&&mission<=campaignData.missions.Length?campaignData.missions[mission-1]:null;
   bool custom=EditorGUILayout.ToggleLeft("Переопределить текст операции",level.overrideBriefing);
   if(custom!=level.overrideBriefing){Record("Брифинг миссии");level.overrideBriefing=custom;
    if(custom&&m!=null){if(string.IsNullOrEmpty(level.operationDescription))level.operationDescription=m.brief;
    }Changed();}
   if(!level.overrideBriefing){
    EditorGUILayout.HelpBox("Сейчас показан исходный текст кампании. Включите переопределение, чтобы редактировать брифинг именно этого уровня.",MessageType.Info);
    EditorGUILayout.TextArea(m!=null?m.brief:"Нет данных миссии",GUILayout.MinHeight(100));
    return;
   }
   EditorGUILayout.LabelField("Описание операции",EditorStyles.boldLabel);
   string info=EditorGUILayout.TextArea(level.operationDescription??"",GUILayout.MinHeight(120));
   EditorGUILayout.LabelField("Ожидаемые угрозы",EditorStyles.boldLabel);
   string risk=EditorGUILayout.TextArea(level.expectedThreats??"",GUILayout.MinHeight(120));
   if(info!=level.operationDescription||risk!=level.expectedThreats){Record("Редактировать брифинг");level.operationDescription=info;level.expectedThreats=risk;Changed();}
   EditorGUILayout.HelpBox("Изменения сохраняются вместе с .asset уровня и показываются в экране «Брифинг операции». Исходный каталог не перезаписывается.",MessageType.None);
  }
  int FinalBaselineCost(int mission){
   var file=Resources.Load<TextAsset>("CampaignBaseline");if(file!=null){var c=JsonUtility.FromJson<CampaignTransfer>(LevelJson.ToUnity(PackedLevel.Read(file)));if(c!=null&&c.missions!=null)foreach(var l in c.missions)if(l.campaignMission==mission){int n=0;foreach(var g in l.defenders)foreach(var s in g.soldiers)n+=DefenderLoadout.Cost(s.kit);return n;}}
   return LevelCampaignFactory.RecommendedCost(mission);
  }
  void DrawDefenderEditor(){
   GUILayout.Label("ГРУППЫ ОБОРОНЫ",EditorStyles.boldLabel);
   bool enabled=EditorGUILayout.Toggle("Свой состав обороны",level.customDefenders);
   if(enabled!=level.customDefenders){Record("Настроить группы");level.customDefenders=enabled;
    if(enabled&&level.defenders.Count==0){if(level.campaignMission>0)LevelCampaignFactory.Populate(level,LevelCampaignFactory.Battle(level.campaignMission));else level.defenders.Add(new DefenderSpawn{position=level.botSpawn});}
    Changed();
   }
   if(!level.customDefenders)return;
   int total=0,cost=0;foreach(var g in level.defenders)foreach(var s in g.soldiers){total++;if(DefenderLoadout.Valid(s.kit))cost+=DefenderLoadout.Cost(s.kit);}
   EditorGUILayout.LabelField("Красные",total+" / 50 бойцов · "+cost+" опыта");
   if(level.campaignMission>0){int reference;if(!recommended.TryGetValue(level.campaignMission,out reference)){reference=FinalBaselineCost(level.campaignMission);recommended[level.campaignMission]=reference;}
    EditorGUILayout.LabelField("Исходная цена уровня",reference+" опыта (ориентир)");}
   GUILayout.BeginHorizontal();
   if(GUILayout.Button("+ Точка / группа")&&total<50){
    var p=level.botSpawn;int tries=0;
    while((level.defenders.Exists(g=>g.position==p)||p==level.playerSpawn)&&tries++<level.columns*level.rows){p.x++;if(p.x>=level.columns){p.x=0;p.y=(p.y+1)%level.rows;}}
    Record("Добавить группу");level.defenders.Add(new DefenderSpawn{name="Группа "+(level.defenders.Count+1),position=p});level.SetObject(p.x,p.y,LevelObjectKind.None);groupSelection=level.defenders.Count-1;soldierSelection=0;Changed();
   }
   if(GUILayout.Button("Удалить группу")&&level.defenders.Count>1){Record("Удалить группу");level.defenders.RemoveAt(Mathf.Clamp(groupSelection,0,level.defenders.Count-1));groupSelection=0;soldierSelection=0;Changed();}
   GUILayout.EndHorizontal();
   if(level.defenders.Count==0)return;
   var labels=new string[level.defenders.Count];for(int i=0;i<labels.Length;i++)labels[i]=(i+1)+" · "+level.defenders[i].name+" ("+level.defenders[i].soldiers.Count+")";
   groupSelection=Mathf.Clamp(EditorGUILayout.Popup("Группа",groupSelection,labels),0,labels.Length-1);var group=level.defenders[groupSelection];
   EditorGUI.BeginChangeCheck();
   string title=EditorGUILayout.TextField("Название",group.name);
   float seconds=EditorGUILayout.FloatField("Появление, секунд",group.arrivalSeconds);
   bool legacy=EditorGUILayout.Toggle("Ранний вызов резерва",group.legacyReserveTrigger);
   int behavior=EditorGUILayout.Popup("Поведение",(int)group.behavior,new[]{"Удержание","Атака → отход при потерях","Оттяжка → оборона у флага"});
   float radius=EditorGUILayout.Slider("Радиус удержания",group.holdRadius,2,15);
   if(EditorGUI.EndChangeCheck()){Record("Настройка группы");group.name=title;group.arrivalSeconds=Mathf.Clamp(seconds,0,480);group.legacyReserveTrigger=legacy;group.behavior=(DefenderBehavior)behavior;group.holdRadius=radius;Changed();}
   bool allowed=group.arrivalSeconds>0;
   EditorGUI.BeginDisabledGroup(!allowed&&!group.vehicleArrival);
   bool transport=EditorGUILayout.ToggleLeft("Прибытие на Humvee (высадка)",group.vehicleArrival);
   EditorGUI.EndDisabledGroup();
   if(transport!=group.vehicleArrival){Record("Транспорт группы");
    if(transport&&level.defenders.Exists(other=>other!=group&&other.vehicleArrival))
     ShowNotification(new GUIContent("У красных может быть только один Humvee"));
    else {group.vehicleArrival=transport;Changed();}}
   if(group.vehicleArrival&&group.arrivalSeconds<=0f)EditorGUILayout.HelpBox("Для Humvee задайте задержку появления > 0 секунд.",MessageType.Error);
   if(!allowed&&!group.vehicleArrival)EditorGUILayout.HelpBox("Humvee доступен только группам, которые появляются позже (время > 0).",MessageType.None);
   ScenarioButton(3+groupSelection,"Поставить точку группы "+(groupSelection+1),group.position);
   if(legacy)EditorGUILayout.HelpBox("Как в базовой кампании: резерв может прийти раньше срока при подходе игрока к флагу или если осталось ≤1 красного. Выключите для строго заданного времени.",MessageType.Info);
   EditorGUILayout.HelpBox("Одна точка = одна группа, 1–5 бойцов. Атака: отход к своему спавну при потере половины состава с округлением вверх. Удержание: приоритет позиций вокруг спавна. Оттяжка: после первой своей потери или убийства своей группой — поочерёдный отход к флагу и оборона.",MessageType.None);
   int groupCost=0;foreach(var slot in group.soldiers)if(DefenderLoadout.Valid(slot.kit))groupCost+=DefenderLoadout.Cost(slot.kit);
   EditorGUILayout.LabelField("Снаряжение группы",groupCost+" опыта");
   GUILayout.BeginHorizontal();
   if(GUILayout.Button("+ Боец")&&group.soldiers.Count<5&&total<50){Record("Добавить бойца");group.soldiers.Add(new DefenderSlot{name="Боец "+(group.soldiers.Count+1)});soldierSelection=group.soldiers.Count-1;Changed();}
   if(GUILayout.Button("− Боец")&&group.soldiers.Count>1){Record("Удалить бойца");group.soldiers.RemoveAt(Mathf.Clamp(soldierSelection,0,group.soldiers.Count-1));soldierSelection=0;Changed();}
   GUILayout.EndHorizontal();
   var people=new string[group.soldiers.Count];for(int i=0;i<people.Length;i++)people[i]=(i+1)+" · "+group.soldiers[i].name;
   soldierSelection=Mathf.Clamp(EditorGUILayout.Popup("Боец",Mathf.Clamp(soldierSelection,0,people.Length-1),people),0,people.Length-1);
   DrawKit(group.soldiers[soldierSelection]);
  }
  void DrawKit(DefenderSlot slot){
   var k=slot.kit;
   var preview=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Rubezh/Art/PawnAtlases/red-rifleman-1.png");
   if(preview!=null){var rect=GUILayoutUtility.GetRect(160f,160f);GUI.DrawTextureWithTexCoords(new Rect(rect.x,rect.y,114,160),preview,new Rect(0,.5f,.5f,.5f));}
   EditorGUILayout.LabelField("Внешний вид","Бронежилет "+(k.armor+1)+" · разгрузка "+(k.rig+1));
   EditorGUI.BeginChangeCheck();string name=EditorGUILayout.TextField("Позывной",slot.name);
   var weapons=new string[6];for(int i=0;i<6;i++)weapons[i]=DefenderLoadout.WeaponNames[i]+" · "+DefenderLoadout.WeaponPrice(i);
   int weapon=EditorGUILayout.Popup("Оружие · опыт",k.weapon,weapons);
   var extras=new string[5];for(int i=0;i<5;i++)extras[i]=DefenderLoadout.Extras[i]+" · "+Kit.ExtraPrice(i);
   int extra=EditorGUILayout.Popup("Спецслот · опыт",k.extra,extras);
   if(EditorGUI.EndChangeCheck()){Record("Снаряжение бойца");slot.name=name;k.weapon=weapon;if(extra!=k.extra)k.SetExtra(extra);DefenderLoadout.Normalize(k);Changed();}
   if(k.extra==2||k.extra==3){
    GUILayout.Label("Содержимое рюкзака · "+k.BagCost+" / "+SilentOrders.Core.CampaignProgression.BagCapacity(k.extra),EditorStyles.boldLabel);
    for(int i=0;i<k.bag.Length;i++)if(k.Allows(i)){
     int value=Stepper(DefenderLoadout.BagNames[i]+" · "+Kit.Prices[i]+"/шт.",k.bag[i],Kit.Caps[i]);
     if(value!=k.bag[i] && (value<k.bag[i] || k.BagCost+(value-k.bag[i])*Kit.Prices[i]<=SilentOrders.Core.CampaignProgression.BagCapacity(k.extra))){Record("Содержимое рюкзака");k.bag[i]=value;Changed();}
    }
   }
   EditorGUI.BeginChangeCheck();int armor=EditorGUILayout.Popup("Бронежилет · опыт",k.armor,new[]{"Лёгкий · 0","Армейский · 100","Штурмовой · 200"});
   if(EditorGUI.EndChangeCheck()){Record("Бронежилет");k.armor=armor;Changed();}
   EditorGUI.BeginChangeCheck();int rig=EditorGUILayout.Popup("Разгрузка · опыт",k.rig,new[]{"Базовая · 0","Усиленная · 50","Полная · 100"});
   int[] grenadeIds=k.weapon==2||k.weapon==3?new[]{0,1,2,3,4,5}:new[]{0,1,3,4,5};
   string[] grenadeNames=k.weapon==2||k.weapon==3?new[]{"Дым","РГД-5","Подствольные","Ф-1","РГН","РГО"}:new[]{"Дым","РГД-5","Ф-1","РГН","РГО"};
   int grenade=grenadeIds[EditorGUILayout.Popup("Гранаты",System.Math.Max(0,System.Array.IndexOf(grenadeIds,k.grenade)),grenadeNames)];
   if(EditorGUI.EndChangeCheck()){Record("Разгрузка и гранаты");k.SetRig(rig);k.grenade=grenade;k.NormalizePockets();Changed();}
   int count=Stepper("Количество · "+Kit.GrenadePrice(k.grenade,k.count)+" опыта",k.count,Math.Min(3,k.PocketLimit-k.count2-k.count3));
   if(count!=k.count){Record("Количество гранат");k.count=count;Changed();}
   if(k.rig>=1){
    EditorGUI.BeginChangeCheck();
    int type2=grenadeIds[EditorGUILayout.Popup("Второй тип",System.Math.Max(0,System.Array.IndexOf(grenadeIds,k.grenade2)),grenadeNames)];
    int n2=Stepper("Второй слот",k.count2,type2==k.grenade?0:Math.Min(3,k.PocketLimit-k.count-k.count3));
    if(EditorGUI.EndChangeCheck()||n2!=k.count2){Record("Второй слот гранат");k.grenade2=type2;k.count2=n2;k.NormalizePockets();Changed();}
   }
   if(k.rig==2){EditorGUI.BeginChangeCheck();int t3=grenadeIds[EditorGUILayout.Popup("Третий тип",Math.Max(0,Array.IndexOf(grenadeIds,k.grenade3)),grenadeNames)];int n3=Stepper("Третий слот",k.count3,t3==k.grenade||k.count2>0&&t3==k.grenade2?0:Math.Min(3,k.PocketLimit-k.count-k.count2));if(EditorGUI.EndChangeCheck()||n3!=k.count3){Record("Третий слот гранат");k.grenade3=t3;k.count3=n3;k.NormalizePockets();Changed();}}
   EditorGUILayout.LabelField("Карманы",(k.rig+1)+" типа · до "+k.PocketLimit+" всего · до 3 одного типа");
   if(DefenderLoadout.Valid(k)){
    var soldier=DefenderLoadout.Build(slot);EditorGUILayout.LabelField("Боекомплект",soldier.Ammo+" + "+soldier.Reserve);
    EditorGUILayout.LabelField("Цена комплекта",DefenderLoadout.Cost(k)+" опыта");
   }else EditorGUILayout.HelpBox("Комплект некорректен. Лимит: медицинский рюкзак — 200, снабжение — 150.",MessageType.Error);
   EditorGUILayout.HelpBox("Рюкзак, РПГ и РШГ несовместимы с пулемётом/снайперской винтовкой, как у зелёных. Стоимость содержимого — ёмкость рюкзака; в цену комплекта второй раз не добавляется.",MessageType.None);
  }
  int Stepper(string label,int value,int max){
   GUILayout.BeginHorizontal();GUILayout.Label(label);GUILayout.FlexibleSpace();
   if(GUILayout.Button("−",GUILayout.Width(25)))value=Math.Max(0,value-1);
   GUILayout.Label(value.ToString(),GUILayout.Width(22));
   if(GUILayout.Button("+",GUILayout.Width(25)))value=Math.Min(max,value+1);
   GUILayout.EndHorizontal();return value;
  }
 }
}
#endif

