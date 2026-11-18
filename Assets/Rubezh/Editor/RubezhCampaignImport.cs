#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace Rubezh.UnityPort.Editor {
 public sealed partial class RubezhLevelEditorWindow {
  void ImportCampaign(){
   if(!CanSwitch())return;string path=EditorUtility.OpenFilePanel("Импорт всей кампании","","json");if(path.Length==0)return;
   var staged=new List<RubezhLevel>();
   try{
    string json=File.ReadAllText(path);var bundle=JsonUtility.FromJson<CampaignTransfer>(LevelJson.ToUnity(json));
    if(bundle==null||bundle.missions==null||bundle.missions.Count!=20)throw new Exception("Нужны 20 миссий кампании.");
    var slots=new HashSet<int>();
    foreach(var data in bundle.missions){var l=data.Create();staged.Add(l);if(!json.Contains("\"count2\"")&&l.defenders!=null)foreach(var g in l.defenders)foreach(var slot in g.soldiers)if(slot.kit!=null)slot.kit.NormalizePockets();string error;
     if(l.campaignMission<1||l.campaignMission>20||!slots.Add(l.campaignMission)||!LevelValidation.Valid(l,out error))throw new Exception("Некорректная миссия "+l.campaignMission);}
    if(!EditorUtility.DisplayDialog("Импорт кампании","Заменить активные карты всех 20 миссий импортированными?","Импортировать","Отмена"))return;
    EnsureFolders();
    foreach(var l in staged){
     string assetPath=LevelFolder+"/Campaign_"+l.campaignMission.ToString("00")+".asset";
     foreach(var guid in AssetDatabase.FindAssets("t:RubezhLevel",new[]{LevelFolder})){
      string otherPath=AssetDatabase.GUIDToAssetPath(guid);var other=AssetDatabase.LoadAssetAtPath<RubezhLevel>(otherPath);
      if(otherPath!=assetPath&&other.campaignMission==l.campaignMission){Undo.RecordObject(other,"Импорт кампании");other.enabledInGame=false;EditorUtility.SetDirty(other);}}
     var target=AssetDatabase.LoadAssetAtPath<RubezhLevel>(assetPath);
     if(target==null){target=Instantiate(l);AssetDatabase.CreateAsset(target,assetPath);}else{Undo.RecordObject(target,"Импорт кампании");EditorUtility.CopySerialized(l,target);}
     target.name=Path.GetFileNameWithoutExtension(assetPath);EditorUtility.SetDirty(target);
    }
    var catalogAsset=Resources.Load<TextAsset>("FieldCamp/catalog");
    if(catalogAsset!=null){
     var catalog=Newtonsoft.Json.Linq.JObject.Parse(catalogAsset.text);
     var missions=(Newtonsoft.Json.Linq.JArray)catalog["missions"];
     foreach(var l in staged){var m=missions[l.campaignMission-1];m["name"]=l.DisplayName;if(l.overrideBriefing)m["brief"]=l.operationDescription;}
     File.WriteAllText(AssetDatabase.GetAssetPath(catalogAsset),catalog.ToString());
     campaignData=Newtonsoft.Json.JsonConvert.DeserializeObject<Rubezh.Camp.Catalog>(catalog.ToString());AssetDatabase.Refresh();
    }
    AssetDatabase.SaveAssets();OpenCampaign(false);ShowNotification(new GUIContent("Кампания импортирована"));
   }catch(Exception e){EditorUtility.DisplayDialog("Ошибка импорта",e.Message,"OK");}
   finally{foreach(var l in staged)DestroyImmediate(l);}
  }
  void ExportCampaign(){
   var bundle=new CampaignTransfer();
   for(int i=1;i<=20;i++){var l=RubezhLevelRuntime.Find(true,i);string error;if(l==null||!LevelValidation.Valid(l,out error)){EditorUtility.DisplayDialog("Кампания","Проверьте карту миссии "+i,"OK");return;}bundle.missions.Add(LevelTransfer.Read(l));}
   string path=EditorUtility.SaveFilePanel("Экспорт кампании","","Rubezh-Campaign","json");if(path.Length>0)File.WriteAllText(path,LevelJson.ToPortable(JsonUtility.ToJson(bundle,true)));
  }
 }
}
#endif

