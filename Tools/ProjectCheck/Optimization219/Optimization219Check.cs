using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Rubezh.UnityPort;

class Optimization219Check {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static object Get(object value,string field)=>value.GetType().GetField(field,F).GetValue(value);
 static void Set(object value,string field,object data)=>value.GetType().GetField(field,F).SetValue(value,data);
 static object Call(object value,string method,params object[] args)=>value.GetType().GetMethod(method,F).Invoke(value,args);
 static void MaskCases(HdGpuRenderer r){
  var battle=new BattleSimulation(219);battle.Fog.Enabled=false;
  Set(r,"unityMaskCamera",new Camera());Set(r,"unityMaskShader",new Shader());
  var view=new Rect(0,0,1,1);
  Action<int,int,string> run=(objects,roofs,label)=>{int a=r.UnityObjectMaskBuilds,b=r.UnityRoofMaskBuilds;Call(r,"RenderCachedUnityMasks",battle,view,false);Check(r.UnityObjectMaskBuilds-a==objects&&r.UnityRoofMaskBuilds-b==roofs,label);};
  run(1,1,"first scene initializes both masks");run(0,0,"unchanged scene reuses both masks");
  battle.GreenVehicle=new VehicleArrivalState{Active=true,Stopped=true,Side=Side.Green,Center=new V2(10,10)};
  run(1,0,"appearing vehicle updates only object mask");
  for(int i=0;i<100;i++){battle.TickNumber++;battle.Time+=.05f;run(0,0,"parked vehicle does not invalidate either mask");}
  battle.GreenVehicle.Center=new V2(10.1f,10);run(1,0,"subcell vehicle movement updates object mask");
  battle.RedVehicle=new VehicleArrivalState{Active=true,Side=Side.Red,Center=new V2(20,20)};run(1,0,"second vehicle arrival updates objects");
  battle.GreenVehicle.Active=false;run(1,0,"vehicle disappearance clears old occluder");
  battle.Fog.Enabled=true;run(1,0,"fog hides remaining vehicle and clears its occluder");
  run(0,0,"hidden stationary vehicle does not invalidate masks");
  battle.Fog.Enabled=false;run(1,0,"revealing vehicle restores occluder");
  battle.Map.Revision++;run(1,1,"map damage invalidates both masks");
  view=new Rect(.1f,.1f,.8f,.8f);run(1,1,"camera view invalidates both masks");
  r.DisableScenery=true;run(1,1,"scenery switch invalidates both masks");run(0,0,"scenery disabled remains cached");
  int beforeA=r.UnityObjectMaskBuilds,beforeB=r.UnityRoofMaskBuilds;Call(r,"RenderCachedUnityMasks",battle,view,true);
  Check(r.UnityObjectMaskBuilds==beforeA+1&&r.UnityRoofMaskBuilds==beforeB+1,"target resize/reallocation redraws both masks");
  battle=new BattleSimulation(220);battle.Fog.Enabled=false;run(1,1,"different map object invalidates masks even same revision");
  r.DisableScenery=false;
 }
 static Mesh MeshAt(float x,float y,float size=10)=>new Mesh{bounds=new Bounds(new Vector3(x,y,0),new Vector3(size,size,0)),subMeshCount=3};
 static void CommandCases(HdGpuRenderer r){
  var camera=new Camera();Set(r,"unityCamera",camera);
  var materials=(List<Material>)Get(r,"unityMaterials");for(int i=0;i<3;i++)materials.Add(new Material(new Shader()));
  var commands=new CommandBuffer();var inside=MeshAt(50,50);var outside=MeshAt(200,200);
  var pages=new[]{2,0,2};var rect=new Rect(0,0,100,100);
  Call(r,"AppendStaticCommands",commands,inside,pages,rect);
  Check(commands.Draws.Count==3,"all contiguous submesh runs retained");
  for(int i=0;i<3;i++)Check(commands.Draws[i].Mesh==inside&&commands.Draws[i].Material==materials[pages[i]]&&commands.Draws[i].Submesh==i&&commands.Draws[i].Pass==0,"submesh painter order/material/pass preserved");
  Call(r,"AppendStaticCommands",commands,outside,pages,rect);Check(commands.Draws.Count==3,"outside mesh culled");
  Call(r,"AppendStaticCommands",commands,MeshAt(105,50),new[]{1},rect);Check(commands.Draws.Count==4,"touching viewport edge retained");
  var picture=Get(r,"pictureBatch");Set(picture,"Mesh",inside);Set(picture,"Pages",pages);
  var roof=Get(r,"roofBatch");var roofMesh=MeshAt(50,50);Set(roof,"Mesh",roofMesh);Set(roof,"Pages",new[]{1});Set(r,"drawNativeRoof",true);
  Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);
  Check(camera.Buffers.Count==2,"two camera command buffers attached");
  var staticCommands=camera.Buffers[CameraEvent.BeforeForwardAlpha];var roofCommands=camera.Buffers[CameraEvent.AfterForwardAlpha];
  Check(staticCommands.Draws.Count==3&&staticCommands.Draws[0].Mesh==inside,"static objects before actor alpha pass");
  Check(roofCommands.Draws.Count==1&&roofCommands.Draws[0].Mesh==roofMesh,"roof after actor alpha pass");
  int builds=r.UnityStaticCommandBuilds;
  for(int i=0;i<100;i++)Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);
  Check(r.UnityStaticCommandBuilds==builds,"stationary retained scene does not rebuild command buffers");
  Set(r,"geometrySerial",(int)Get(r,"geometrySerial")+1);Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);Check(r.UnityStaticCommandBuilds==builds+1,"geometry revision rebuilds commands");
  r.UnityCachedStaticCommands=false;Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);Check(camera.Buffers.Count==0&&r.UnityExplicitDraws==0,"fallback detaches buffers and clears command count");
  r.UnityCachedStaticCommands=true;Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);Check(camera.Buffers.Count==2&&r.UnityExplicitDraws==4,"switching back rebuilds exact command sequence");
  r.DisableRoofs=true;Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);Check(roofCommands.Draws.Count==0&&staticCommands.Draws.Count==3,"roof toggle clears only roof commands");
  r.DisableScenery=true;Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);Check(staticCommands.Draws.Count==0,"scenery toggle clears static commands");
  Call(r,"ReleaseStaticCommands");Check(camera.Buffers.Count==0&&staticCommands.Released&&roofCommands.Released,"dispose detaches and releases both buffers");
 }
 static void Main(){
  Resources.Loader=name=>new Shader();
  // Isolate retained-cache behavior from atlas decoding; production field types are retained.
  var renderer=(HdGpuRenderer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HdGpuRenderer));
  foreach(string name in new[]{"unityMaterials","groundDraws","sceneExteriorChunks","sceneObjectChunks","pictureBatch","roofBatch"}){
   var field=typeof(HdGpuRenderer).GetField(name,F);field.SetValue(renderer,Activator.CreateInstance(field.FieldType,true));
  }
  Set(renderer,"canvas",new PixelCanvas(100,100));renderer.UnityCachedStaticCommands=true;
  MaskCases(renderer);CommandCases(renderer);
  Console.WriteLine("PASS Optimization219: "+checks+" mask invalidation, command ordering, culling, retained reuse, fallback and disposal checks. Portable contracts, not GPU image/performance validation.");
 }
}
