using System;using System.Collections.Generic;using System.Reflection;using Rubezh.UnityPort;using SilentOrders.Core;using SilentOrders.Rendering;using UnityEngine;
class Camera196Check {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;static int n;
 static object Get(object o,string k)=>o.GetType().GetField(k,F).GetValue(o);
 static void Set(object o,string k,object v)=>o.GetType().GetField(k,F).SetValue(o,v);
 static void Call(object o,string k,params object[] a)=>o.GetType().GetMethod(k,F).Invoke(o,a);
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);n++;}
 static void Main(){Resources.Loader=s=>new Shader();Rubezh.Graphics.ConfigurePacks();
 using(var g=new HdGpuRenderer(new PixelCanvas(320,200),true,true)){
  var v=new Rect(.2f,.2f,.25f,.25f);Call(g,"PrepareDepthCoverage",v,1920,1080);var original=(Rect)Get(g,"depthCoverage");
  Set(g,"depthDirty",false);
  for(int i=1;i<=200;i++){Call(g,"PrepareDepthCoverage",new Rect(v.x+i*v.width/1920,v.y,v.width,v.height),1920,1080);Check(!(bool)Get(g,"depthDirty"),"pan retained coverage "+i);}
  Call(g,"PrepareDepthCoverage",new Rect(v.x+300*v.width/1920,v.y,v.width,v.height),1920,1080);Check((bool)Get(g,"depthDirty"),"large pan refresh");
  Set(g,"depthDirty",false);Call(g,"PrepareDepthCoverage",new Rect(v.x,v.y,.125f,.125f),1920,1080);Check((bool)Get(g,"depthDirty"),"zoom refresh");
  Call(g,"PrepareDepthCoverage",v,8190,8190);Check(g.NativeDepthWidth<=8192&&g.NativeDepthHeight<=8192,"hardware texture limit");
  var b=new BattleSimulation(17);g.BeginScene(b.Map);Set(g,"visiblePixels",new Rect(-100000,-100000,200000,200000));
  var cells=new HashSet<string>();int duplicates=0;int calls=0;
  g.GroundChunks(b.Map,(l,t,r,z)=>{calls++;Check(l%15==0&&t%15==0,"chunk alignment including negatives");Check(r-l<=15&&z-t<=15,"chunk dimensions");for(int y=t;y<z;y++)for(int x=l;x<r;x++)if(!cells.Add(x+","+y))duplicates++;});
  Check(duplicates==0,"no overlapping chunks");for(int y=0;y<b.Map.Rows;y++)for(int x=0;x<b.Map.Columns;x++)Check(cells.Contains(x+","+y),"map fully covered");
  int builds=g.GroundChunkBuilds;g.GroundChunks(b.Map,(l,t,r,z)=>{throw new Exception("cached terrain rebuilt");});Check(g.GroundChunkBuilds==builds,"terrain reused");
  Set(g,"depthDirty",false);g.CacheObject(-123,0,int.MaxValue,false,true,()=>{});Check(!(bool)Get(g,"depthDirty"),"exterior does not dirty depth");g.EndScene();Set(g,"depthDirty",false);g.CacheObject(-124,0,int.MaxValue,false,true,()=>{});g.EndScene();Check(!(bool)Get(g,"depthDirty"),"exterior chunk does not dirty depth");
  g.CacheObject(0,0,int.MaxValue,true,false,()=>{});Check((bool)Get(g,"depthDirty"),"interior invalidates depth");
  Console.WriteLine("ground chunks="+calls+" cells="+cells.Count);
 }Console.WriteLine("PASS "+n+" checks; Unity API contracts, not native GPU/FPS");}
}
