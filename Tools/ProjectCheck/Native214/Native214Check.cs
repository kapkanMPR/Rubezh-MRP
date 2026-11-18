using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using SilentOrders.Core;
using SilentOrders.Rendering;
using Rubezh.UnityPort;
using UnityEngine;
using UnityEngine.AddressableAssets;
class Native214Check {
static void Drain(IEnumerator e){try{while(e.MoveNext()){} }finally{(e as IDisposable)?.Dispose();}}
static int Count(object o,string field){var f=o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic);return f?.GetValue(o) is ICollection c?c.Count:-1;}
static string State(BattleSimulation b)=>JsonConvert.SerializeObject(new{b.Time,b.TickNumber,b.Outcome,rng=typeof(Rng).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(b.Random),voice=typeof(Rng).GetField("state",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(b.VoiceRandom),units=b.Soldiers.Select(x=>new{x.Id,px=x.Position.X,py=x.Position.Y,fx=x.Facing.X,fy=x.Facing.Y,x.Health,x.Ammo,x.Deployed,x.Moving,x.LastFire})});
static Dictionary<string,object> Metrics(object value)=>value.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.PropertyType==typeof(int)&&p.GetIndexParameters().Length==0).ToDictionary(p=>p.Name,p=>p.GetValue(value));
static void Main(string[] args){
bool actual=args.Contains("--battle");
  var catalog=File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json");
  var entries=JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(catalog).entries;
  Resources.Loader=path=>path=="Fonts/RubezhSans"?(UnityEngine.Object)new Font():path=="NativeUI/catalog"?new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/NativeUI/catalog.json")}:path.StartsWith("NativeUI/page-")?new Texture2D(2048,2048,TextureFormat.RGBA32,false):path=="NativeExterior/catalog"?new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/NativeExterior/catalog.json")}:path.StartsWith("NativeExterior/page-")?new Texture2D(2048,2048,TextureFormat.RGBA32,false):(path=="RubezhNativeEffectMaterial"||path=="RubezhNativeSceneMaterial")?(UnityEngine.Object)new Material(new Shader()):path=="RoofSurface"||path=="ClosureWood"||path=="Effects/terrain-crater"?(UnityEngine.Object)new Texture2D(16,16,TextureFormat.RGBA32,false):path.StartsWith("Rubezh")?new Shader():null;
  Addressables.Loader=key=>{
   if(key=="rubezh/native/catalog")return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new TextAsset{text=catalog}};
   if(key=="rubezh/native/visual")return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new GameObject("visual")};
   int page=int.Parse(key.Substring(key.LastIndexOf('/')+1));var atlas=new UnityEngine.U2D.SpriteAtlas();
   var texture=new Texture2D(8192,8192,TextureFormat.RGBA32,false);
   foreach(var e in entries.Where(e=>e.page==page))atlas.Items["s"+e.id.ToString("D5")]=new Sprite{texture=texture,textureRect=new Rect(0,0,e.w,e.h)};
   return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=atlas};
  };

var map=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes("Assets/Rubezh/Resources/War/war-01.bytes"))).Create();map.terrainVisual=null;map.proceduralVisuals.Clear();foreach(var house in map.dynamicBuildings)house.layers.Clear();
var sim=new BattleSimulation(214);if(!RubezhLevelRuntime.ApplyLevel(sim,map))throw new Exception(RubezhLevelRuntime.LastError);
var templates=sim.Soldiers.ToArray();sim.Soldiers.Clear();
for(int i=0;i<50;i++){
var s=JsonConvert.DeserializeObject<Soldier>(JsonConvert.SerializeObject(templates[i%templates.Length],new JsonSerializerSettings{ReferenceLoopHandling=ReferenceLoopHandling.Ignore}));
s.Id=i;s.Side=i<25?Side.Green:Side.Red;s.Deployed=true;s.Health=100;s.Position=new V2(8+i%5*2,5+(i/5)%10*1.5f);s.Facing=new V2(1,0);s.Moving=true;s.LastFire=-100;s.DefenderGroup=null;if(actual){s.Position=new V2((i<25?5:24)+i%5*1.2f,5+(i%25)/5*2);s.Moving=false;}sim.Soldiers.Add(s);
}
sim.Green.LeaderId=0;sim.Red.LeaderId=25;sim.CustomDefenders=false;sim.DefenderGroups.Clear();sim.InitializeCombatRandom(214);sim.Fog.Enabled=false;Rubezh.Graphics.ReleaseSceneSprites();
using(var lease=new NativeBattleAssets()){
Drain(lease.Load(Rubezh.Graphics.SceneSprites(sim),()=>false));Drain(ExteriorSpriteLoader.Preload(()=>false));
var painter=new NativeBattleComposer(1);painter.PrepareCanvas(sim.Map);
using(var renderer=new HdGpuRenderer(painter.Canvas,true,false,true)){
long renderTicks=0,observationTicks=0,stepTicks=0;var watch=System.Diagnostics.Stopwatch.StartNew();var samples=new List<object>();
for(int tick=0;tick<(actual?(args.Contains("--long")?2400:200):1200);tick++){
if(actual){long stamp=System.Diagnostics.Stopwatch.GetTimestamp();sim.Step();stepTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-stamp;}else {sim.TickNumber=tick;sim.Time=tick*.05f;
for(int i=0;i<50;i++){var s=sim.Soldiers[i];s.Position=new V2(8+i%5*2+(float)Math.Sin(tick*.005)*2,5+(i/5)%10*1.5f);s.Moving=true;if(tick%20==i%20)s.LastFire=sim.Time;if(i%7==0)s.Health=70-(tick%100)*.01f;}
}
long observeStart=System.Diagnostics.Stopwatch.GetTimestamp();typeof(NativeBattleComposer).GetMethod("ObserveTick")?.Invoke(painter,new object[]{sim});typeof(NativeBattleComposer).GetMethod("SetInterpolation")?.Invoke(painter,new object[]{1f});observationTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-observeStart;var immutable=State(sim);long renderStart=System.Diagnostics.Stopwatch.GetTimestamp();renderer.Render(painter,sim,false,0,960,540,new Rect(0,0,1,1));renderTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-renderStart;if(immutable!=State(sim))throw new Exception("Render mutated simulation state");
if(tick%200==199)samples.Add(new{tick,rebuilds=renderer.NativeActorRebuilds,reuses=renderer.NativeActorReuseHits,sorts=renderer.DirectSorts,instances=renderer.DirectInstanceCount,nodes=renderer.UnitySceneNodes,allocatedGeneric=Count(renderer,"unityNodes"),allocatedActors=Count(renderer,"unityActorNodes"),retainedGroups=Count(renderer,"directGroups")});
}
watch.Stop();Console.WriteLine(JsonConvert.SerializeObject(new{scenario=actual?"actual deterministic BattleSimulation.Step25v25;seed214":"fixed synthetic25v25 trace;1200ticks60simulationseconds moving/firing/wounded;noAIbenchmark",requestedTicks=actual?(args.Contains("--long")?2400:200):1200,simulationTime=sim.Time,outcome=sim.Outcome.ToString(),survivors=sim.Soldiers.Count(x=>x.Alive),state=string.Join(";",sim.Soldiers.Select(x=>x.Id+":"+x.Position.X+":"+x.Position.Y+":"+x.Health+":"+x.Ammo)),renderContractMs=renderTicks*1000.0/System.Diagnostics.Stopwatch.Frequency,observationContractMs=observationTicks*1000.0/System.Diagnostics.Stopwatch.Frequency,stepContractMs=stepTicks*1000.0/System.Diagnostics.Stopwatch.Frequency,painterCounters=Metrics(painter),rendererCounters=Metrics(renderer),elapsedContractMs=watch.ElapsedMilliseconds,rebuilds=renderer.NativeActorRebuilds,reuses=renderer.NativeActorReuseHits,reuseRatio=(double)renderer.NativeActorReuseHits/Math.Max(1,renderer.NativeActorRebuilds+renderer.NativeActorReuseHits),samples},Formatting.Indented));
if(!actual)typeof(Native214Check).Assembly.GetType("Native214PrimitiveCheck")?.GetMethod("Run").Invoke(null,null);
if(!actual)typeof(Native214Check).Assembly.GetType("Native214PoseCheck")?.GetMethod("Run").Invoke(null,new object[]{renderer,painter,sim});
if(!actual)typeof(Native214Check).Assembly.GetType("Native214WorldCheck")?.GetMethod("Run").Invoke(null,new object[]{renderer,painter,sim});
}
}
}
}
