using System.Linq;using System.Collections.Generic;using Rubezh.UnityPort;using SilentOrders.Rendering;using UnityEngine;using System;using System.IO;using System.Reflection;using Rubezh.UnityPort.Editor;using UnityEditor;
class Preparation201Check {
 static int count;static void C(bool v,string s){if(!v)throw new Exception(s);count++;}
 static void Main(){
  string root=Directory.GetCurrentDirectory();string tmp=Path.Combine(Path.GetTempPath(),"rubezh-prep-check-"+Guid.NewGuid());Directory.CreateDirectory(tmp);
  var split=new Dictionary<int,UnityTestAtlasEntry>();
  Resources.Loader=x=>x=="RubezhHdGpu"?new Shader():null;
  Rubezh.Graphics.ConfigurePacks();
  foreach(int page in new[]{374,375}){
   Rubezh.Graphics.ReleaseSceneSprites();Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p"+page.ToString("D4")+".bytes"));
   using(var renderer=new HdGpuRenderer(new PixelCanvas(32,32),true,false))
    foreach(var e in renderer.ExportUnityCatalog().entries){e.page+=page*1000;split[e.id]=e;}
  }
  Rubezh.Graphics.ReleaseSceneSprites();
  foreach(int page in new[]{374,375})Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p"+page.ToString("D4")+".bytes"));
  using(var renderer=new HdGpuRenderer(new PixelCanvas(32,32),true,false)){
   var all=renderer.ExportUnityCatalog().entries;C(all.Length==split.Count,"split preserves sprite count");
   foreach(var e in all){C(split.ContainsKey(e.id),"split sprite id");var x=split[e.id];C(x.w==e.w&&x.h==e.h&&x.ow==e.ow&&x.oh==e.oh&&x.ox==e.ox&&x.oy==e.oy,"split preserves sprite crop");}
  }
  Rubezh.Graphics.ReleaseSceneSprites();
  try{
   Directory.SetCurrentDirectory(tmp);
   foreach(var dir in new[]{"Assets/Rubezh/Editor/LegacySources/SpritePacks","Assets/Rubezh/Core","Assets/Rubezh/Runtime","Assets/Rubezh/Editor"})Directory.CreateDirectory(dir);
   File.WriteAllText("Assets/Rubezh/Editor/LegacySources/SpritePacks/p0001.bytes","fixture");
   C(!UnityTest20Preparation.IsReady(),"no false readiness");
   bool failed=false;try{UnityTest20Preparation.EnsurePrepared();}catch(UnityEditor.Build.BuildFailedException){failed=true;}
   C(failed&&AssetDatabase.Imports==0&&!UnityTest20Preparation.Running,"build refuses without heavy work");
   UnityTest20Preparation.Start(0,false);C(UnityTest20Preparation.Running,"job scheduled");
   for(int i=0;i<10&&UnityTest20Preparation.Running;i++)EditorApplication.Tick();
   C(!UnityTest20Preparation.Running,"stage stops");C(File.Exists(UnityTest20Preparation.LogPath),"log saved");
   UnityTest20Preparation.Start(0,true);UnityTest20Preparation.Cancel();EditorApplication.Tick();C(!UnityTest20Preparation.Running,"cancel");
   UnityTest20Preparation.Start(0,true);AssemblyReloadEvents.Reload();C(!UnityTest20Preparation.Running,"reload cleanup");
   var m=typeof(UnityTest20Preparation).GetMethod("SourceStamp",BindingFlags.Static|BindingFlags.NonPublic);
   string s=(string)m.Invoke(null,null);
   string p="Assets/Rubezh/Editor/LegacySources/SpritePacks/p0001.bytes";File.AppendAllText(p,"change");C(s!=(string)m.Invoke(null,null),"cache invalidates");
   Directory.CreateDirectory(UnityTest20Preparation.Folder);string target=UnityTest20Preparation.Folder+"/fixture.png";File.WriteAllText(target,"texture");
   var f=new FileInfo(target);var manifest=new UnityTest20Preparation.Manifest{stamp=(string)m.Invoke(null,null),files=new[]{target},sizes=new[]{f.Length},times=new[]{f.LastWriteTimeUtc.Ticks}};
   File.WriteAllText(UnityTest20Preparation.Folder+"/ready-v2.json",UnityEngine.JsonUtility.ToJson(manifest));
   C(UnityTest20Preparation.IsReady(),"ready manifest");UnityTest20Preparation.EnsurePrepared();C(AssetDatabase.Imports==0,"ready build read only");
   File.AppendAllText(target,"bad");C(!UnityTest20Preparation.IsReady(),"changed output rejected");
   EditorApplication.isPlayingOrWillChangePlaymode=true;UnityTest20Preparation.Start(0,true);C(!UnityTest20Preparation.Running,"play guard");
   Console.WriteLine("PASS "+count+" preparation lifecycle/cache/build-guard checks. Editor contracts, no native import.");
  }finally{Directory.SetCurrentDirectory(root);Directory.Delete(tmp,true);}
 }
}
