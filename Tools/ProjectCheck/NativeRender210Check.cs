using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
class NativeRender210Check {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static int n;
 static object Get(object o,string key)=>o.GetType().GetField(key,F).GetValue(o);
 static void Set(object o,string key,object value)=>o.GetType().GetField(key,F).SetValue(o,value);
 static void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
 class Pages {public Page[] pages;}class Page {public int id,width,height;}
 static void Main(){
  var catalog=JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(File.ReadAllText("Assets/Rubezh/Resources/UnityTestForest/catalog.json"));
  var pages=JsonConvert.DeserializeObject<Pages>(File.ReadAllText("Assets/Rubezh/Resources/UnityTestForest/prepared.json"));
  Resources.Loader=key=>{
   if(key=="RubezhHdGpu"||key=="RubezhUnityTest"||key=="RubezhUnityForestMask")return new Shader();
   if(key=="UnityTestForest/catalog")return new TextAsset{text=JsonUtility.ToJson(catalog)};
   if(key.StartsWith("UnityTestForest/atlas-")){int id=int.Parse(key.Substring(key.LastIndexOf('-')+1));var p=pages.pages.First(v=>v.id==id);return new Texture2D(p.width,p.height,TextureFormat.RGBA32,false);}
   return null;
  };
  var level=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes("Assets/Rubezh/Resources/War/war-01.bytes"))).Create();
  level.proceduralVisuals.Clear();foreach(var h in level.dynamicBuildings)h.layers.Clear();
  var battle=new BattleSimulation(210);Check(RubezhLevelRuntime.ApplyLevel(battle,level),"forest applies");
  var required=Rubezh.Graphics.SceneSprites(battle);var atlasPages=required.Select(id=>Rubezh.SpritePackIndex.Page[id]).Distinct().ToArray();
  Rubezh.Graphics.ConfigurePacks();
  foreach(int page in atlasPages)Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p"+page.ToString("D4")+".bytes"),required:required);
  using(var g=new HdGpuRenderer(new PixelCanvas(320,200),true,false,true)){
   Set(g,"visiblePixels",new Rect(-10000,-10000,20000,20000));
   int id=required.First(i=>i>=Rubezh.PawnAtlas.RedStart&&Rubezh.Graphics.HasSprite(i));
   foreach(float angle in new[]{0f,.31f,1.4f,-2.2f}){
    var verts=(List<Vector3>)Get(g,"vertices");var uv=(List<Vector2>)Get(g,"uvs");verts.Clear();uv.Clear();
    Set(g,"capturingNativeActors",false);g.Sprite(id,100.2f,90.1f,47,39,angle,258.23f,true);
    var expected=verts.ToArray();var expectedUV=uv.ToArray();Check(expected.Length==4,"reference quad");
    verts.Clear();uv.Clear();Set(g,"capturingNativeActors",true);g.BeginVisual(3,100,100);g.Sprite(id,100.2f,90.1f,47,39,angle,258.23f,true);g.EndVisual();
    Check(verts.Count==0,"native sprite never enters dynamic vertex buffer");
    var poses=(IList)Get(g,"nativeSpritePoses");var pose=poses[poses.Count-1];var type=pose.GetType();
    var pos=(Vector3)type.GetField("Position").GetValue(pose);var scale=(Vector3)type.GetField("Scale").GetValue(pose);
    var data=(Vector4)type.GetField("Data").GetValue(pose);var geometry=type.GetField("Geometry").GetValue(pose);var mesh=(Mesh)geometry.GetType().GetField("Mesh").GetValue(geometry);
    for(int i=0;i<4;i++){
     float x=mesh.vertices[i].x*scale.x,y=mesh.vertices[i].y*scale.y;
     float tx=pos.x+x*(float)Math.Cos(angle)-y*(float)Math.Sin(angle),ty=pos.y+x*(float)Math.Sin(angle)+y*(float)Math.Cos(angle);
     Check(Math.Abs(tx-expected[i].x)<.0001f&&Math.Abs(ty-expected[i].y)<.0001f,"native transformed quad parity");
     Check(mesh.uv[i].x==expectedUV[i].x&&mesh.uv[i].y==expectedUV[i].y,"native atlas UV parity");
    }
    Check(data.x==258.23f&&data.y==1,"native depth/test parameters");
   }
   Check(g.NativeSpriteMeshes==1,"same sprite keeps one immutable mesh across poses");
  }
  var painter=new BattleRenderer(1);painter.PrepareCanvas(battle.Map,1);
  using(var g=new HdGpuRenderer(painter.Canvas,true,false,true)){
   g.Render(painter,battle,false,0,512,384,new Rect(0,0,1,1));
   Check(g.NativeSpriteInstances>0,"battle uses native sprite instances");
   int built=g.NativeVisualUploads,parts=g.NativeSpriteMeshes,staticBuilds=g.ObjectChunkBuilds;
   for(int i=0;i<12;i++)g.Render(painter,battle,false,0,512,384,new Rect(i*.001f,0,1,1));
   Check(g.NativeCompositionHits==12,"camera movement skips old composition");
   Check(g.NativeVisualUploads==built,"camera movement performs no actor mesh uploads");
   Check(g.NativeSpriteMeshes==parts,"camera movement allocates no sprite meshes");
   Check(g.ObjectChunkBuilds==staticBuilds,"camera movement rebuilds no static chunks");
   g.Render(painter,battle,false,1,512,384);Check(g.NativeCompositionHits==12,"selection invalidates composition");
   battle.Step();g.Render(painter,battle,false,1,512,384);Check(g.NativeCompositionHits==12,"simulation invalidates composition");
   int uploads=g.NativeVisualUploads;
   g.RenderForeground(painter,battle,1);g.Render(painter,battle,false,1,512,384);Check(g.NativeCompositionHits==13,"foreground preserves retained composition");
   Check(g.NativeForegroundFrames==1&&g.NativeVisualUploads==uploads,"foreground uses camera, no second actor mesh pass");
   Console.WriteLine("native parts="+g.NativeSpriteInstances+" immutable meshes="+g.NativeSpriteMeshes+" geometry hits="+g.NativeVisualHits+" uploads="+g.NativeVisualUploads);
  }
  Console.WriteLine("PASS "+n+" native render checks; no Unity GPU/device execution.");
 }
}
