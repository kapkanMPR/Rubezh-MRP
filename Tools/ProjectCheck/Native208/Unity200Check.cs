using System;using System.IO;using System.Linq;using System.Reflection;using Newtonsoft.Json;using Rubezh;using Rubezh.Camp;using Rubezh.UnityPort;using SilentOrders.Core;using SilentOrders.Rendering;using UnityEngine;
class Unity200Check {
 static int n;const BindingFlags F=BindingFlags.NonPublic|BindingFlags.Instance;static void Check(bool value,string s){if(!value)throw new Exception(s);n++;}
 static string Fingerprint(BattleSimulation b)=>string.Join("|",b.Soldiers.Select(s=>s.Id+":"+s.Position.X.ToString("R")+","+s.Position.Y.ToString("R")+":"+s.Health.ToString("R")+":"+s.Ammo+":"+s.Reserve));
 static void Main(){
  Rubezh.UnityPort.Editor.UnityTest20Preparation.EnsurePrepared();
  Resources.Loader=p=>p=="RubezhHdGpu"||p=="RubezhUnityTest"?new Shader():LoadAsset(p);
  var level=JsonUtility.FromJson<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes("Assets/Rubezh/Resources/War/war-01.bytes"))).Create();Check(level!=null,"actual campaign20 loaded");
  // Portable test excludes only PNG visual layers; physical walls and combat cells are retained.
  level.proceduralVisuals.Clear();foreach(var house in level.dynamicBuildings)house.layers.Clear();
  var catalog=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"));var model=new CampModel(catalog,null,()=>.37);string before=JsonConvert.SerializeObject(model.S);
  Check(model.MissionOpen("test",1,20)&&model.MissionOpen("test-reference",1,20),"test available without campaign completion");Check(!model.MissionOpen("test",1,19)&&!model.MissionOpen("test",2,20),"only mission20");Check(!model.Win("test",1,20)&&!model.Win("test-reference",1,20),"test awards blocked");Check(before==JsonConvert.SerializeObject(model.S),"career untouched");
  var units=Enumerable.Range(0,5).Select(i=>new CampUnit{name="Test"+i,fighterId="t"+i,role=i+1,weapon=i==4?1:0,rig=1,armor=1,skills=new string[0],pockets=new string[0],bag=new string[0]}).ToArray();
  var q=new CampOrder{mode="test",block=1,mission=1,units=units,formation=new FormationSettings()};var b=RubezhApp.BuildCampSimulation(q);q.mode="test-reference";var reference=RubezhApp.BuildCampSimulation(q);
  Check(b.CampaignLevel==reference.CampaignLevel,"campaign20 rules");Check(b.Map.Columns==level.columns&&b.Map.Rows==level.rows,"campaign20 actual dimensions");Check(b.Map.Tiles.SequenceEqual(reference.Map.Tiles),"same tiles");
  var prepared=JsonUtility.FromJson<UnityTestAtlasCatalog>(File.ReadAllText("Assets/Rubezh/Resources/UnityTestForest/catalog.json"));
  var available=prepared.entries.Select(e=>e.id).ToHashSet();
  var missing=Rubezh.Graphics.SceneSprites(b).Where(id=>!available.Contains(Rubezh.SpritePackIndex.Resolve(id))).ToArray();
  Console.WriteLine("Scene sprites missing from prepared catalog: "+string.Join(",",missing));
  Check(missing.Length==0,"all scene sprites prepared");Check(Fingerprint(b)==Fingerprint(reference),"same initial squad");
  for(int i=0;i<100;i++){b.Step();reference.Step();Check(Fingerprint(b)==Fingerprint(reference),"simulation divergence at tick "+i);}
  var cityVisual=BuiltinCampaign.Find(20); cityVisual.terrainVisual=null; cityVisual.proceduralVisuals.Clear(); foreach(var house in cityVisual.dynamicBuildings)house.layers.Clear();
  var cityOrder=new CampOrder{mode="test",block=1,mission=20,units=units,formation=new FormationSettings()};
  var city=RubezhApp.BuildCampSimulation(cityOrder);cityOrder.mode="test-reference";
  var cityReference=RubezhApp.BuildCampSimulation(cityOrder);
  Check(city.Map.Columns>level.columns||city.Map.Rows>level.rows,"story20 is the heavy map");
  Check(city.Map.Tiles.SequenceEqual(cityReference.Map.Tiles),"city A/B same map");
  Check(Fingerprint(city)==Fingerprint(cityReference),"city A/B same squad");
  Check(city.Map.DynamicBuildings.Count>0,"city includes buildings");
  for(int tick=0;tick<10;tick++){city.Step();cityReference.Step();Check(Fingerprint(city)==Fingerprint(cityReference),"city A/B same combat");}
  // Exercise real composition with portable Unity API recording, not GPU pixel correctness.
  var manifest=JsonConvert.DeserializeObject<PreparedPages>(File.ReadAllText("Assets/Rubezh/Resources/UnityTestForest/prepared.json"));
  Resources.Loader=p=>{
   if(p=="RubezhHdGpu"||p=="RubezhUnityTest"||p=="RubezhUnityForestMask")return new Shader();
   if(p=="UnityTestForest/catalog")return new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/"+p+".json")};
   if(p.StartsWith("UnityTestForest/atlas-")){int id=int.Parse(p.Substring(p.LastIndexOf('-')+1));var page=manifest.pages.First(x=>x.id==id);return new Texture2D(page.width,page.height,TextureFormat.RGBA32,false);}
   return LoadAsset(p);
  };
  Rubezh.Graphics.ConfigurePacks();
  foreach(int page in manifest.pages.Select(x=>x.id/1000).Distinct())Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/"+Rubezh.SpritePackIndex.Path(page)+".bytes"));
  var painter=new BattleRenderer(1);painter.PrepareCanvas(b.Map);
  using(var scene=new HdGpuRenderer(painter.Canvas,true,false,true)){
   scene.Render(painter,b,false,-1,960,540,new Rect(0,0,1,1));
   Check(scene.UnitySceneNodes>0,"native geometry populated");Check(scene.NativeVisualMeshes>1,"fighters and effects have pooled Unity meshes");Check(scene.UnityMaskBuilds==1,"Unity mask camera used");
   Check(scene.NativeDepthRebuilds==0,"legacy depth pass bypassed");
   Check(scene.UnityMaskBytes==960L*540*8,"two viewport-sized masks");
   int builds=scene.StaticMeshBuilds,ground=scene.GroundChunkBuilds;
   scene.Render(painter,b,false,-1,960,540,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskBuilds==2,"camera movement refreshes mask");
   Check(scene.StaticMeshBuilds==builds&&scene.GroundChunkBuilds==ground,"camera movement retains geometry");
   scene.Render(painter,b,false,-1,960,540,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskBuilds==2,"stationary mask reused");
   b.Map.Revision++;scene.Render(painter,b,false,-1,960,540,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskBuilds==3,"map revision invalidates native mask");
   Check(scene.StaticMeshBuilds>builds,"map revision refreshes geometry");
   scene.DisableScenery=true;scene.Render(painter,b,false,-1,960,540,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskBuilds==4,"layer toggle clears mask");
   scene.UnityCompatibilityDraw=true;scene.Render(painter,b,false,-1,960,540,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskBytes==0&&scene.NativeDepthRebuilds>0,"reserve switches mask ownership");
   scene.UnityCompatibilityDraw=false;scene.DisableScenery=false;scene.Render(painter,b,false,-1,800,450,new Rect(.2f,.2f,.5f,.5f));
   Check(scene.UnityMaskWidth==800&&scene.UnityMaskHeight==450,"resize recreates native mask");
  }
  Rubezh.Graphics.ReleaseSceneSprites();
  var app=new RubezhApp();
  foreach(int fps in new[]{30,60,90,120}){
   typeof(RubezhApp).GetMethod("ApplyCampOption",F).Invoke(app,new object[]{"fps",fps});
   Check(Application.targetFrameRate==fps,"FPS applied "+fps);
   Check(PlayerPrefs.GetInt("rubezh.fps")==fps,"FPS saved "+fps);
   var options=(NativeCampOptions)typeof(RubezhApp).GetMethod("ReadNativeCampOptions",F).Invoke(app,null);
   Check(options.fps==fps,"FPS exposed in settings "+fps);
   foreach(int quality in new[]{-1,0,1,2,3}){
    typeof(RubezhApp).GetField("graphicsTier",F).SetValue(app,quality);
    typeof(RubezhApp).GetMethod("ApplyFrameRate",F).Invoke(app,null);
    Check(Application.targetFrameRate==fps,"FPS independent of quality");
   }
  }
  typeof(RubezhApp).GetMethod("SetFrameRate",F).Invoke(app,new object[]{999});
  Check(Application.targetFrameRate==60,"Invalid FPS falls back to 60");
typeof(RubezhApp).GetMethod("CampReceiptFor",F).Invoke(app,new object[]{q,true,null,null});Check(!PlayerPrefs.HasKey((string)typeof(RubezhApp).GetProperty("CampKey",F).GetValue(app)+"receipt"),"receipt path returns");
  Rubezh.Graphics.ConfigurePacks();Rubezh.Graphics.LoadPage(375,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p0375.bytes"));
  UnityTestAtlasCatalog exported;using(var renderer=new HdGpuRenderer(new PixelCanvas(320,200),true)){exported=renderer.ExportUnityCatalog();Check(exported.entries.Length>0,"export metadata");Check(exported.entries.Any(e=>e.id==7974),"rare AK included");}
  var imports=new System.Collections.Generic.List<Texture2D>();Resources.Loader=p=>p=="RubezhHdGpu"||p=="RubezhUnityTest"?new Shader():p=="UnityTestForest/catalog"?new TextAsset{text=JsonUtility.ToJson(exported)}:p.StartsWith("UnityTestForest/atlas-")?MakeTexture(imports):null;
  int unloads=0;Resources.OnUnload=o=>{if(o is Texture2D)unloads++;};
  using(var renderer=new HdGpuRenderer(new PixelCanvas(320,200),true,false,true)){Check(renderer.ImportedUnityPages>0,"borrow imported texture");Check(renderer.UnitySceneMode,"explicit test renderer");Check(renderer.ExportUnityCatalog().entries.Length==exported.entries.Length,"import metadata roundtrip");}
  Check(unloads==imports.Count,"imported texture unloading");
  typeof(RubezhApp).GetField("battle",F).SetValue(app,b);
  typeof(RubezhApp).GetMethod("CreateUnityTestHud",F).Invoke(app,null);
  foreach(string panel in new[]{"radio","style","maneuver","journal","squad","diagnostic"}){typeof(RubezhApp).GetMethod("NativeDrawer",F).Invoke(app,new object[]{panel});Check(typeof(RubezhApp).GetField("testOverlay",F).GetValue(app)!=null,"native panel opens "+panel);}
  typeof(RubezhApp).GetMethod("HideUnityTestHud",F).Invoke(app,null);
  // Native asset lease: async success, failed page, cancellation, idempotent release.
  var addr=typeof(UnityEngine.AddressableAssets.Addressables);
  UnityEngine.AddressableAssets.Addressables.Loader=key=>{
   object result;
   if(key=="rubezh/native/catalog")result=new TextAsset{text="{\"version\":2,\"entries\":[{\"id\":7974,\"page\":375,\"w\":10,\"h\":20,\"ow\":12,\"oh\":24,\"ox\":1,\"oy\":2}]}"};
   else if(key=="rubezh/native/visual")result=new GameObject("visual");
   else {var atlas=new UnityEngine.U2D.SpriteAtlas();atlas.Items["s07974"]=new UnityEngine.Sprite{texture=new Texture2D(32,32,TextureFormat.RGBA32,false),textureRect=new Rect(2,3,10,20)};result=atlas;}
   return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=result};
  };
  int releases=UnityEngine.AddressableAssets.Addressables.Releases;
  var lease=new NativeBattleAssets();var iterator=lease.Load(new System.Collections.Generic.HashSet<int>{7974},()=>false);while(iterator.MoveNext()){}
  Check(lease.LoadedAtlases==1&&NativeBattleAssets.Active==lease,"asset lease owns requested atlas");
  Texture2D imported;UnityTestAtlasEntry region;
  Check(lease.TrySprite(7974,out imported,out region)&&region.x==2&&region.y==3&&region.ox==1,"Unity packing coordinates retain original trim");
  lease.Dispose();lease.Dispose();Check(UnityEngine.AddressableAssets.Addressables.Releases-releases==3&&NativeBattleAssets.Active==null,"all handles released exactly once");
  UnityEngine.AddressableAssets.Addressables.Loader=key=>new UnityEngine.ResourceManagement.AsyncOperations.State{Status=UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed,Error=new Exception("test failure")};
  lease=new NativeBattleAssets();bool failed=false;try{iterator=lease.Load(new System.Collections.Generic.HashSet<int>(),()=>false);while(iterator.MoveNext()){} }catch(InvalidOperationException){failed=true;}finally{lease.Dispose();}
  Check(failed&&NativeBattleAssets.Active==null,"failed catalog can be disposed");
  UnityEngine.AddressableAssets.Addressables.Loader=key=>new UnityEngine.ResourceManagement.AsyncOperations.State{Done=false};
  lease=new NativeBattleAssets();iterator=lease.Load(new System.Collections.Generic.HashSet<int>(),()=>true);Check(!iterator.MoveNext(),"pending load cancellation exits");lease.Dispose();Check(NativeBattleAssets.Active==null,"cancel releases pending handle");
  Console.WriteLine("PASS "+n+" Unity test integration checks, 100 matching simulation ticks; native API contracts only, no real Unity rendering.");
 }
 class PreparedPages {public Page[] pages;} class Page {public int id,width,height;}
 static UnityEngine.Object LoadAsset(string p){string path="Assets/Rubezh/Resources/"+p+".bytes";return File.Exists(path)?new TextAsset{bytes=File.ReadAllBytes(path)}:null;}
 static Texture2D MakeTexture(System.Collections.Generic.List<Texture2D> list){var t=new Texture2D(4096,4096,TextureFormat.RGBA32,false);list.Add(t);return t;}
}
