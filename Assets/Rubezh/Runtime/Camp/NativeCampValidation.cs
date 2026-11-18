#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;
namespace Rubezh.UnityPort {
 public sealed partial class NativeCampView {
  bool validationViewport;
  public void ValidationViewport(int width,int height,RenderTexture target){validationViewport=true;panel.targetTexture=target;panel.scale=1;vw=width;vh=height;root.style.left=0;root.style.top=0;root.style.width=width;root.style.height=height;root.EnableInClassList("narrow",vw<=650);root.EnableInClassList("compact",vh<=600&&vw>650);root.EnableInClassList("short",vh<=520&&vw>=590);root.EnableInClassList("landscape",vw>=590&&vw>vh);root.EnableInClassList("small-header",vw<=900);Render();}
  public void ValidationScreen(string name){Close();selected=trainee="kedr";trainingFrom=null;screen="home";switch(name){case "shop":screen="store";storeTab="shop";break;case "warehouse":screen="store";storeTab="warehouse";break;case "recruit":screen="store";storeTab="recruit";break;case "item":screen="store";storeTab="shop";Render();ItemDetails("pkm");return;case "person":screen="barracks";Render();PersonDetails("s1");return;case "profile":Profile();break;case "avatars":AvatarPicker();break;case "nickname":RenameDialog();break;case "settings":Render();Settings();return;case "shipment":screen="operations";Render();DrawShipment(false);return;default:screen=name;break;}Render();}
  public object ValidationGeometry(){return Descendants(root).Select(e=>new{type=e.GetType().Name,name=e.name,text=(e as Label)?.text??(e as Button)?.text,classes=e.GetClasses().ToArray(),x=e.worldBound.x,y=e.worldBound.y,w=e.worldBound.width,h=e.worldBound.height,display=e.resolvedStyle.display.ToString(),enabled=e.enabledInHierarchy}).ToArray();}
 }
 // This capture runs in real Unity. It deliberately uses an isolated test save.
 public sealed class NativeCampCapture:MonoBehaviour {
  public IEnumerator Start(){
   RubezhApp original=null;float deadline=Time.realtimeSinceStartup+60;
   while(Time.realtimeSinceStartup<deadline){original=FindFirstObjectByType<RubezhApp>();if(original!=null&&original.NativeCampReady)break;yield return null;}
   if(original==null||!original.NativeCampReady){Debug.LogWarning("Для снимков открой лагерь и дождись загрузки.");Destroy(gameObject);yield break;}
   var originalView=original.gameObject.GetComponent<NativeCampView>();NativeCampView view=null;
   try {
   original.enabled=false;if(originalView!=null)originalView.Hide();
   string output=Path.Combine(Application.dataPath,"../Validation/Current/Unity");Directory.CreateDirectory(output);
   var catalog=JsonConvert.DeserializeObject<Catalog>(Resources.Load<TextAsset>("FieldCamp/catalog").text,CampModel.JsonSettings);var model=new CampModel(catalog,rng:()=>.371);
   for(int m=1;m<=20;m++)model.Win("story",1,m);for(int m=1;m<=10;m++)model.Win("war",1,m);model.S.balance=5000;foreach(var it in catalog.items)if(!CampModel.Personal(it.id))model.S.inventory[it.id]=10;model.S.rewardView=new RewardView{key="war-1-10",phase="gear",summary="Проверка выдачи наград"};
   model.S.motion=false;var store=new CampStore(Path.Combine(output,"isolated-save.json"),model);store.Save();view=gameObject.AddComponent<NativeCampView>();view.Initialize(store,true);view.GetOptions=()=>new NativeCampOptions{quality=2,effects=80,music=40,sound=true,musicOn=true,visibility=true};
   var sizes=new[]{new Vector2Int(915,412),new Vector2Int(800,360),new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(393,852),new Vector2Int(650,360)};
   foreach(var size in sizes){var target=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32);target.Create();view.ValidationViewport(size.x,size.y,target);foreach(string page in new[]{"home","shop","warehouse","recruit","barracks","personnel","squads","prepare","operations","battlePrep","training","item","person","profile","avatars","nickname","settings","shipment"}){view.ValidationScreen(page);yield return null;yield return null;yield return new WaitForEndOfFrame();string prefix=Path.Combine(output,size.x+"x"+size.y+"-"+page);var previous=RenderTexture.active;var image=new Texture2D(size.x,size.y,TextureFormat.RGBA32,false);try{RenderTexture.active=target;image.ReadPixels(new Rect(0,0,size.x,size.y),0,0);image.Apply();File.WriteAllBytes(prefix+".png",image.EncodeToPNG());}finally{RenderTexture.active=previous;Destroy(image);}File.WriteAllText(prefix+".json",JsonConvert.SerializeObject(view.ValidationGeometry(),Formatting.Indented));}target.Release();Destroy(target);}
    File.WriteAllText(Path.Combine(output,"CAPTURED.txt"),"108 screenshots rendered in Unity "+Application.unityVersion+". Inspect all captured screens; successful capture is not proof of correct layout.");Debug.Log("Снимки нативного меню: "+output);
   }finally{if(view!=null)view.Hide();if(original!=null)original.enabled=true;if(originalView!=null)originalView.Show();Destroy(gameObject);}
  }
 }
}
#endif

