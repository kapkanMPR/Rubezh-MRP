using System;
using System.Collections.Generic;
using System.Reflection;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Rubezh.UnityPort;

class Optimization220Check {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static object Get(object value,string field)=>value.GetType().GetField(field,F).GetValue(value);
 static void Set(object value,string field,object data)=>value.GetType().GetField(field,F).SetValue(value,data);
 static object Call(object value,string method,params object[] args)=>value.GetType().GetMethod(method,F).Invoke(value,args);
 static void IncrementProperty(object value,string name){var prop=value.GetType().GetProperty(name,F);prop.SetValue(value,(int)prop.GetValue(value)+1);}
 static Mesh MeshAt(float x,float y)=>new Mesh{bounds=new Bounds(new Vector3(x,y,0),new Vector3(10,10,0)),subMeshCount=2};
 static HdGpuRenderer Fixture(){
  var r=(HdGpuRenderer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HdGpuRenderer));
  foreach(string name in new[]{"unityMaterials","groundDraws","sceneExteriorChunks","sceneObjectChunks","pictureBatch","roofBatch"}){
   var field=typeof(HdGpuRenderer).GetField(name,F);field.SetValue(r,Activator.CreateInstance(field.FieldType,true));
  }
  Set(r,"canvas",new PixelCanvas(100,100));r.UnityCachedStaticCommands=true;r.UnityStaticColorCache=true;
  var camera=new Camera{backgroundColor=new Color(58/255f,68/255f,45/255f,1)};
  Set(r,"unityCamera",camera);Set(r,"unityMaskCamera",new Camera());Set(r,"unityMaskShader",new Shader());
  var target=new RenderTexture(100,100,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();Set(r,"target",target);camera.targetTexture=target;
  var materials=(List<Material>)Get(r,"unityMaterials");for(int i=0;i<2;i++)materials.Add(new Material(new Shader()));
  var picture=Get(r,"pictureBatch");Set(picture,"Mesh",MeshAt(50,50));Set(picture,"Pages",new[]{0,1});
  var roof=Get(r,"roofBatch");Set(roof,"Mesh",MeshAt(50,50));Set(roof,"Pages",new[]{1});Set(r,"drawNativeRoof",true);
  Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);return r;
 }
 static List<CommandBuffer> Prefix(HdGpuRenderer r)=>((Camera)Get(r,"unityCamera")).BufferLists[CameraEvent.BeforeForwardAlpha];
 static void ExpectMiss(HdGpuRenderer r,string label){
  Call(r,"UpdateStaticColorCache");var prefix=Prefix(r);var target=(RenderTexture)Get(r,"target");var cache=(RenderTexture)Get(r,"unityStaticColor");
  Check(r.UnityStaticColorCacheActive&&!r.UnityStaticColorCacheHit,label+" requests a fresh capture");
  Check(prefix.Count==2&&prefix[0].Draws.Count==2&&prefix[1].Draws.Count==0,label+" draws all static meshes before capture");
  Check(prefix[1].Copies.Count==1&&prefix[1].Copies[0].Source==target&&prefix[1].Copies[0].Destination==cache,label+" copies original rendered pixels to cache");
 }
 static void ExpectHit(HdGpuRenderer r,string label){
  Call(r,"UpdateStaticColorCache");var prefix=Prefix(r);var target=(RenderTexture)Get(r,"target");var cache=(RenderTexture)Get(r,"unityStaticColor");
  Check(r.UnityStaticColorCacheActive&&r.UnityStaticColorCacheHit,label+" reuses complete cache");
  Check(prefix.Count==1&&prefix[0].Draws.Count==0&&prefix[0].Copies.Count==1,label+" has no static DrawMesh commands");
  Check(prefix[0].Copies[0].Source==cache&&prefix[0].Copies[0].Destination==target,label+" copies full-resolution pixels back to main target");
  var camera=(Camera)Get(r,"unityCamera");Check(camera.BufferLists[CameraEvent.AfterForwardAlpha][0].Draws.Count==1,label+" keeps roofs live after actors");
 }
 static void MaskContentInvalidation(HdGpuRenderer r){
  var battle=new BattleSimulation(220);battle.Fog.Enabled=false;
  Call(r,"RenderCachedUnityMasks",battle,new Rect(0,0,1,1),false);
  int beforeObject=r.UnityObjectMaskBuilds,beforeRoof=r.UnityRoofMaskBuilds;
  IncrementProperty(r,"NativeBuildingMaskUpdates");
  Call(r,"RenderCachedUnityMasks",battle,new Rect(0,0,1,1),false);
  Check(r.UnityObjectMaskBuilds==beforeObject+1&&r.UnityRoofMaskBuilds==beforeRoof+1,"same texture/new building mask content invalidates both depth masks");
 }
 static void Main(){
  SystemInfo.copyTextureSupport=CopyTextureSupport.Basic;
  var r=Fixture();ExpectMiss(r,"initial scene");
  Call(r,"CompleteStaticColorCache",false);ExpectMiss(r,"failed frame");
  Call(r,"CompleteStaticColorCache",true);ExpectHit(r,"successful capture");
  var cache=(RenderTexture)Get(r,"unityStaticColor");
  Check(cache.width==100&&cache.height==100&&cache.format==RenderTextureFormat.ARGB32&&!cache.sRGB&&cache.antiAliasing==1,"cache preserves resolution/format/color space/sample count");
  Check(cache.filterMode==FilterMode.Point,"cache has no additional resampling");
  for(int i=0;i<100;i++){Call(r,"CompleteStaticColorCache",true);ExpectHit(r,"unchanged scene "+i);}
  IncrementProperty(r,"UnityObjectMaskBuilds");ExpectMiss(r,"moving vehicle/occlusion update");Call(r,"CompleteStaticColorCache",true);
  IncrementProperty(r,"NativeBuildingMaskUpdates");ExpectMiss(r,"building damage content update");Call(r,"CompleteStaticColorCache",true);
  Set(r,"geometrySerial",(int)Get(r,"geometrySerial")+1);Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);ExpectMiss(r,"changed static geometry");Call(r,"CompleteStaticColorCache",true);
  Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),100,100);ExpectMiss(r,"camera pan");Call(r,"CompleteStaticColorCache",true);
  Set(r,"unityStaticMaterialRevision",(int)Get(r,"unityStaticMaterialRevision")+1);ExpectMiss(r,"static material replacement");Call(r,"CompleteStaticColorCache",true);
  r.PicturePagesUploaded++;ExpectMiss(r,"atlas page upload");Call(r,"CompleteStaticColorCache",true);
  r.PictureRegionUploads++;ExpectMiss(r,"in-place atlas region upload");Call(r,"CompleteStaticColorCache",true);
  ((Camera)Get(r,"unityCamera")).backgroundColor=new Color(.1f,.2f,.3f,1);ExpectMiss(r,"background color change");Call(r,"CompleteStaticColorCache",true);
  cache.Release();ExpectMiss(r,"lost cache allocation");Call(r,"CompleteStaticColorCache",true);
  var replacement=new RenderTexture(100,100,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);replacement.Create();Set(r,"target",replacement);
  ExpectMiss(r,"replacement target");Call(r,"CompleteStaticColorCache",true);
  r.UnityStaticColorCache=false;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r).Count==1&&Prefix(r)[0].Draws.Count==2,"cache toggle restores geometry rendering");
  r.UnityStaticColorCache=true;SystemInfo.copyTextureSupport=CopyTextureSupport.None;
  int builds=r.UnityStaticCommandBuilds;for(int i=0;i<100;i++){Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),100,100);Call(r,"UpdateStaticColorCache");}
  Check(!r.UnityStaticColorCacheActive&&Prefix(r).Count==1&&Prefix(r)[0].Draws.Count==2,"unsupported copy falls back to original commands");
  Check(r.UnityStaticCommandBuilds==builds,"unsupported copy does not rebuild geometry command buffers each frame");
  SystemInfo.copyTextureSupport=CopyTextureSupport.Basic;
  replacement.sRGB=true;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r)[0].Draws.Count==2,"sRGB target uses original renderer without conversion");
  replacement.sRGB=false;replacement.antiAliasing=4;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r)[0].Draws.Count==2,"MSAA target falls back without invalid copy");
  replacement.antiAliasing=1;replacement.format=RenderTextureFormat.RFloat;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive,"nonmatching native format falls back");
  replacement.format=RenderTextureFormat.ARGB32;ExpectMiss(r,"compatible target restored");Call(r,"CompleteStaticColorCache",true);
  replacement.Release();Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive,"lost main target is never copied");
  replacement.Create();ExpectMiss(r,"main target recreated");Call(r,"CompleteStaticColorCache",true);
  var larger=new RenderTexture(200,120,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);larger.Create();Set(r,"target",larger);
  Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),200,120);ExpectMiss(r,"render resolution resize");Call(r,"CompleteStaticColorCache",true);
  var resizedCache=(RenderTexture)Get(r,"unityStaticColor");Check(resizedCache.width==200&&resizedCache.height==120&&r.UnityStaticColorBytes==200*120*4,"resize uses exact output dimensions and bytes");
  MaskContentInvalidation(r);
  var releasedCache=(RenderTexture)Get(r,"unityStaticColor");Call(r,"ReleaseStaticCommands");Check(!releasedCache.IsCreated()&&r.UnityStaticColorBytes==0,"cache texture allocation released");Check(((Camera)Get(r,"unityCamera")).BufferLists.Count==0,"release detaches all camera buffers");
  Console.WriteLine("PASS Optimization220: "+checks+" static pixel cache capture/reuse/invalidation/fallback/order checks. Portable commands only; device pixels and Vulkan execution untested.");
 }
}
