using System;
using System.Collections.Generic;
using System.Reflection;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Rubezh.UnityPort;

class Optimization221Check {
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
  Set(r,"unityRoot",new GameObject("root"));Set(r,"unityCamera",camera);Set(r,"unityMaskCamera",new Camera());Set(r,"unityMaskShader",new Shader());
  var target=new RenderTexture(100,100,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();Set(r,"target",target);camera.targetTexture=target;
  var materials=(List<Material>)Get(r,"unityMaterials");for(int i=0;i<2;i++)materials.Add(new Material(new Shader()));
  var picture=Get(r,"pictureBatch");Set(picture,"Mesh",MeshAt(50,50));Set(picture,"Pages",new[]{0,1});
  var roof=Get(r,"roofBatch");Set(roof,"Mesh",MeshAt(50,50));Set(roof,"Pages",new[]{1});Set(r,"drawNativeRoof",true);
  Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);return r;
 }
 static List<CommandBuffer> Prefix(HdGpuRenderer r)=>((Camera)Get(r,"unityCamera")).BufferLists[CameraEvent.BeforeForwardAlpha];
 static Camera CacheCamera(HdGpuRenderer r){
  foreach(var field in typeof(HdGpuRenderer).GetFields(F))if(field.FieldType==typeof(Camera)&&field.Name!="unityCamera"&&field.Name!="unityMaskCamera"){
   var camera=(Camera)field.GetValue(r);if(camera!=null)return camera;
  }
  return null;
 }
 static int CacheRenders(HdGpuRenderer r)=>CacheCamera(r)?.RenderCalls.Count??0;
 static void CheckComposite(HdGpuRenderer r,string label){
  var prefix=Prefix(r);
  Check(prefix.Count==1&&prefix[0].Draws.Count==1&&prefix[0].Copies.Count==0,label+" main pass draws one fullscreen composite without CopyTexture");
  var draw=prefix[0].Draws[0];Check(draw.Mesh!=((Mesh)Get(Get(r,"pictureBatch"),"Mesh")),label+" static geometry not submitted again in main pass");
  var camera=(Camera)Get(r,"unityCamera");Check(camera.BufferLists[CameraEvent.AfterForwardAlpha][0].Draws.Count==1,label+" keeps roofs live after actors");
 }
 static void ExpectMiss(HdGpuRenderer r,string label){
  var cameraBefore=CacheCamera(r);int renders=CacheRenders(r);
  Call(r,"UpdateStaticColorCache");
  Check(r.UnityStaticColorCacheActive&&!r.UnityStaticColorCacheHit,label+" requests a fresh static render");
  var camera=CacheCamera(r);Check(camera!=null,label+" owns a dedicated static camera");
  Check(camera.RenderCalls.Count==(camera==cameraBefore?renders+1:1),label+" one cache render completed before main pass");
  var call=camera.RenderCalls[camera.RenderCalls.Count-1];
  Check(call.Target==Get(r,"unityStaticColor")&&call.Target!=Get(r,"target"),label+" draws static geometry directly into separate cache target");
  Check(call.CullingMask==0&&call.ClearFlags==CameraClearFlags.SolidColor,label+" cache render excludes live actors/roofs and clears its own target");
  Check(call.Draws==2&&call.Copies==0,label+" cache pass executes static commands without in-pass texture copies");
  CheckComposite(r,label);
 }
 static void ExpectHit(HdGpuRenderer r,string label){
  int before=CacheRenders(r);Call(r,"UpdateStaticColorCache");
  Check(r.UnityStaticColorCacheActive&&r.UnityStaticColorCacheHit,label+" reuses complete cache");
  Check(CacheRenders(r)==before,label+" no extra static camera rendering");
  CheckComposite(r,label);
 }
 static void MaskContentInvalidation(HdGpuRenderer r){
  var battle=new BattleSimulation(221);battle.Fog.Enabled=false;
  Call(r,"RenderCachedUnityMasks",battle,new Rect(0,0,1,1),false);
  int beforeObject=r.UnityObjectMaskBuilds,beforeRoof=r.UnityRoofMaskBuilds;
  IncrementProperty(r,"NativeBuildingMaskUpdates");
  Call(r,"RenderCachedUnityMasks",battle,new Rect(0,0,1,1),false);
  Check(r.UnityObjectMaskBuilds==beforeObject+1&&r.UnityRoofMaskBuilds==beforeRoof+1,"same texture/new building mask content invalidates both depth masks");
 }
 static void FailureFallbacks(){
  Resources.Loader=path=>null;var missing=Fixture();Call(missing,"UpdateStaticColorCache");
  Check(!missing.UnityStaticColorCacheActive&&Prefix(missing).Count==1&&Prefix(missing)[0].Draws.Count==2,"missing composite shader falls back to full geometry");
  Check(Get(missing,"unityStaticColor")==null,"missing shader leaves no allocated cache");
  Resources.Loader=path=>new Shader();var failed=Fixture();
  int failures=0;Camera.OnRender=camera=>{failures++;throw new InvalidOperationException("injected cache render failure");};
  try {Call(failed,"UpdateStaticColorCache");}finally{Camera.OnRender=null;}
  Check(failures==1&&!failed.UnityStaticColorCacheActive,"cache camera failure is caught");
  Check(Prefix(failed).Count==1&&Prefix(failed)[0].Draws.Count==2&&Prefix(failed)[0].Copies.Count==0,"cache render failure uses full original static geometry this frame");
  Check(Get(failed,"unityStaticColor")==null&&CacheCamera(failed)==null,"cache render failure disposes partial target and camera");
  Call(failed,"UpdateStaticColorCache");
  Check(!failed.UnityStaticColorCacheActive&&CacheCamera(failed)==null,"failed cache path remains disabled until renderer recreation");
  Call(missing,"ReleaseStaticCommands");Call(failed,"ReleaseStaticCommands");
 }
 static void Main(){
  Resources.Loader=path=>new Shader();SystemInfo.copyTextureSupport=CopyTextureSupport.None;
  var r=Fixture();ExpectMiss(r,"initial scene");
  Call(r,"CompleteStaticColorCache",false);ExpectMiss(r,"failed main frame");
  Call(r,"CompleteStaticColorCache",true);ExpectHit(r,"successful render");
  var cache=(RenderTexture)Get(r,"unityStaticColor");
  Check(cache.width==100&&cache.height==100&&cache.format==RenderTextureFormat.ARGB32&&!cache.sRGB&&cache.antiAliasing==1,"cache preserves resolution/format/color space/sample count");
  Check(cache.filterMode==FilterMode.Point,"cache uses point sampling at output pixel centres");
  for(int i=0;i<20;i++){Call(r,"CompleteStaticColorCache",true);ExpectHit(r,"unchanged scene "+i);}
  IncrementProperty(r,"UnityObjectMaskBuilds");ExpectMiss(r,"moving vehicle/occlusion update");Call(r,"CompleteStaticColorCache",true);
  IncrementProperty(r,"NativeBuildingMaskUpdates");ExpectMiss(r,"building damage content update");Call(r,"CompleteStaticColorCache",true);
  Set(r,"geometrySerial",(int)Get(r,"geometrySerial")+1);Call(r,"UpdateStaticCommands",new Rect(0,0,1,1),100,100);ExpectMiss(r,"changed static geometry");Call(r,"CompleteStaticColorCache",true);
  int beforePanRenders=CacheRenders(r);Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),100,100);Call(r,"UpdateStaticColorCache");
  Check(r.UnityStaticColorDeferred&&!r.UnityStaticColorCacheActive&&!r.UnityStaticColorCacheHit,"camera movement defers cache and invalidates old pixels");
  Check(Prefix(r).Count==1&&Prefix(r)[0].Draws.Count==2&&Prefix(r)[0].Copies.Count==0&&CacheRenders(r)==beforePanRenders,"moving camera draws full static scene once directly, without extra camera or stale cache");
  Call(r,"CompleteStaticColorCache",true);
  Call(r,"UpdateStaticCommands",new Rect(.02f,0,1,1),100,100);Call(r,"UpdateStaticColorCache");
  Check(r.UnityStaticColorDeferred&&CacheRenders(r)==beforePanRenders&&Prefix(r)[0].Draws.Count==2,"continued pan never rerenders intermediate cache");
  Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),100,100);Call(r,"UpdateStaticColorCache");
  ExpectMiss(r,"camera stopped after pan");Call(r,"CompleteStaticColorCache",true);ExpectHit(r,"stable view after new capture");
  Set(r,"unityStaticMaterialRevision",(int)Get(r,"unityStaticMaterialRevision")+1);ExpectMiss(r,"static material replacement");Call(r,"CompleteStaticColorCache",true);
  r.PicturePagesUploaded++;ExpectMiss(r,"atlas page upload");Call(r,"CompleteStaticColorCache",true);
  r.PictureRegionUploads++;ExpectMiss(r,"in-place atlas region upload");Call(r,"CompleteStaticColorCache",true);
  ((Camera)Get(r,"unityCamera")).backgroundColor=new Color(.1f,.2f,.3f,1);ExpectMiss(r,"background color change");Call(r,"CompleteStaticColorCache",true);
  cache.Release();ExpectMiss(r,"lost cache allocation");Call(r,"CompleteStaticColorCache",true);
  var replacement=new RenderTexture(100,100,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);replacement.Create();Set(r,"target",replacement);
  ExpectMiss(r,"replacement target");Call(r,"CompleteStaticColorCache",true);
  r.UnityStaticColorCache=false;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r).Count==1&&Prefix(r)[0].Draws.Count==2,"cache toggle restores geometry rendering");
  r.UnityStaticColorCache=true;ExpectMiss(r,"cache reenabled without CopyTexture capability");Call(r,"CompleteStaticColorCache",true);
  replacement.sRGB=true;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r)[0].Draws.Count==2,"sRGB target uses original renderer without conversion");
  replacement.sRGB=false;replacement.antiAliasing=4;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive&&Prefix(r)[0].Draws.Count==2,"MSAA target uses original commands");
  replacement.antiAliasing=1;replacement.format=RenderTextureFormat.RFloat;Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive,"nonmatching native format falls back");
  replacement.format=RenderTextureFormat.ARGB32;ExpectMiss(r,"compatible target restored");Call(r,"CompleteStaticColorCache",true);
  replacement.Release();Call(r,"UpdateStaticColorCache");Check(!r.UnityStaticColorCacheActive,"lost main target is never sampled/written");
  replacement.Create();ExpectMiss(r,"main target recreated");Call(r,"CompleteStaticColorCache",true);
  var larger=new RenderTexture(200,120,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);larger.Create();Set(r,"target",larger);
  Call(r,"UpdateStaticCommands",new Rect(.01f,0,1,1),200,120);ExpectMiss(r,"render resolution resize");Call(r,"CompleteStaticColorCache",true);
  var resizedCache=(RenderTexture)Get(r,"unityStaticColor");Check(resizedCache.width==200&&resizedCache.height==120&&r.UnityStaticColorBytes==200*120*4,"resize uses exact output dimensions and bytes");
  MaskContentInvalidation(r);
  var releasedCache=(RenderTexture)Get(r,"unityStaticColor");var releasedCamera=CacheCamera(r);Call(r,"ReleaseStaticCommands");
  Check(!releasedCache.IsCreated()&&r.UnityStaticColorBytes==0,"cache texture allocation released");Check(((Camera)Get(r,"unityCamera")).BufferLists.Count==0,"release detaches main camera buffers");Check(releasedCamera.BufferLists.Count==0,"release detaches cache camera buffer before releasing shared commands");
  FailureFallbacks();
  checks+=Primitive221Check.Run();
  checks+=Benchmark221Check.Run();
  checks+=Mask221Check.Run();
  checks+=GroundPalette221Check.Run();
  Console.WriteLine("PASS Optimization221: "+checks+" contract checks. No actual Unity rendering, shader compilation, Vulkan pixels or FPS measured.");
 }
}
