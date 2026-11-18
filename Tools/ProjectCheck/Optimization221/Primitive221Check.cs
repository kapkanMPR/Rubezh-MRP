using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SilentOrders.Rendering;
using UnityEngine;
using Rubezh.UnityPort;

static class Primitive221Check {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static int checks;
 static void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
 static T Get<T>(object o,string name)=>(T)o.GetType().GetField(name,Flags).GetValue(o);
 static HdGpuRenderer Fixture(bool owned){
  var renderer=(HdGpuRenderer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HdGpuRenderer));
  foreach(string name in new[]{"vertices","uvs","clips","depths","colors","indices","runs","atlases"}){
   var f=renderer.GetType().GetField(name,Flags);f.SetValue(renderer,Activator.CreateInstance(f.FieldType));
  }
  Set(renderer,"canvas",new PixelCanvas(200,200){LayerLeft=9,LayerRight=131});
  Set(renderer,"UnitySceneMode",owned);Set(renderer,"nativeAtlasMode",true);Set(renderer,"spriteAtlasPages",3);
  var atlases=Get<List<Texture2D>>(renderer,"atlases");atlases.Add(new Texture2D(4,4));atlases.Add(new Texture2D(256,256));atlases.Add(new Texture2D(512,512));
  return renderer;
 }
 static void TexturedQuad(HdGpuRenderer r,int page){
  r.GetType().GetMethod("Quad",Flags).Invoke(r,new object[]{1f,2f,21f,2f,21f,12f,1f,12f,.1f,.2f,.3f,.4f,new Color32(255,255,255,255),14f,true,page});
 }
 static bool VectorSame(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
 static bool ColorSame(Color32 a,Color32 b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
 static void FlatOnPage(HdGpuRenderer r,int page,float mode=1){
  int start=Get<List<Vector2>>(r,"depths").Count;
  r.GetType().GetMethod("Quad",Flags).Invoke(r,new object[]{1f,2f,21f,2f,21f,12f,1f,12f,-1f,-1f,-1f,-1f,new Color32(110,80,60,130),14f,true,page});
  for(int i=start;i<start+4;i++)Get<List<Vector2>>(r,"depths")[i]=new Vector2(14,mode);
 }
 static void CompactRuns(){
  var r=Fixture(true);FlatOnPage(r,0);TexturedQuad(r,1);FlatOnPage(r,0);TexturedQuad(r,2);FlatOnPage(r,0);
  var indices=Get<List<int>>(r,"indices").ToArray();var vertices=Get<List<Vector3>>(r,"vertices").ToArray();
  r.GetType().GetMethod("CompactSolidMaterialRuns",Flags).Invoke(r,null);
  var runs=Get<AtlasLayout.DrawRuns>(r,"runs").Items;
  Check(runs.Count==2&&runs[0].Page==1&&runs[0].Count==18&&runs[1].Page==2&&runs[1].Count==12,"leading/middle/trailing solid-only runs retain adjacent texture without reorder");
  Check(indices.SequenceEqual(Get<List<int>>(r,"indices"))&&vertices.SequenceEqual(Get<List<Vector3>>(r,"vertices")),"solid-run compaction never modifies vertex or triangle streams");
  foreach(float mode in new[]{3f,-2f}){
   r=Fixture(true);TexturedQuad(r,1);FlatOnPage(r,0,mode);TexturedQuad(r,1);
   r.GetType().GetMethod("CompactSolidMaterialRuns",Flags).Invoke(r,null);
   Check(Get<AtlasLayout.DrawRuns>(r,"runs").Items.Count==3,"negative roof/smoke UVs are not interpreted as flat solids");
  }
 }
 internal static int Run(){
  CompactRuns();
  var before=Fixture(false);var after=Fixture(true);
  for(int step=0;step<160;step++){
   int page=1+(step/20)%2;
   TexturedQuad(before,page);TexturedQuad(after,page);
   uint tint=(uint)(0x80402010+step);
   before.Rect(5+step,10,20,12,tint,step%3==0?-1:47,step%2==0);
   after.Rect(5+step,10,20,12,tint,step%3==0?-1:47,step%2==0);
   before.RoofDiamond(30,40,12,8);after.RoofDiamond(30,40,12,8);
  }
  var verticesBefore=Get<List<Vector3>>(before,"vertices");var verticesAfter=Get<List<Vector3>>(after,"vertices");
  Check(verticesBefore.Count==verticesAfter.Count,"primitive batching preserves all vertices");
  for(int i=0;i<verticesBefore.Count;i++){
   Check(VectorSame(verticesBefore[i],verticesAfter[i]),"primitive position exactly preserved");
   Check(Get<List<Color32>>(before,"colors")[i].Equals(Get<List<Color32>>(after,"colors")[i]),"primitive vertex color and alpha exactly preserved");
   Check(Get<List<Vector2>>(before,"depths")[i].Equals(Get<List<Vector2>>(after,"depths")[i]),"depth/mask test flags preserved");
   Check(Get<List<Vector2>>(before,"clips")[i].Equals(Get<List<Vector2>>(after,"clips")[i]),"layer clips preserved");
   var oldUV=Get<List<Vector2>>(before,"uvs")[i];var newUV=Get<List<Vector2>>(after,"uvs")[i];
   bool primitive=(i/4)%3!=0;
   Check(primitive?newUV.x<0&&newUV.y<0:oldUV==newUV,"only solid geometry receives sentinel; sprite UV unchanged");
  }
  Check(Get<List<int>>(before,"indices").SequenceEqual(Get<List<int>>(after,"indices")),"all triangle indices/painter order identical");
  var runs=Get<AtlasLayout.DrawRuns>(after,"runs");var legacy=Get<AtlasLayout.DrawRuns>(before,"runs");
  Check(runs.Items.Count==8,"contiguous sprite/solid sequences use eight actual texture runs");
  Check(legacy.Items.Count==320,"unoptimized white page reproduces 320 runs");
  Check(runs.Items.Sum(x=>x.Count)==legacy.Items.Sum(x=>x.Count),"no triangles omitted");
  int initial=verticesAfter.Count;after.Rect(1,1,0,10,0,0,false);after.Rect(1,1,10,-1,0,0,false);
  Check(verticesAfter.Count==initial,"degenerate rectangles remain omitted");
  Console.WriteLine("PASS primitive batching: synthetic alternating sprite/solid workload "+legacy.Items.Count+" -> "+runs.Items.Count+" material runs, all geometry/colors/depth/clips/order preserved.");
  return checks;
 }
}
