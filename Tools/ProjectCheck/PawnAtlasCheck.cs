using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Rubezh;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using Rubezh.UnityPort;
class PawnAtlasCheck
{
 static int checks;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 class Sink:ICanvasGpu
 {
  public List<int> Ids=new List<int>();
  public void Background(uint[] p,float[] d,object m,int r){}
  public void Rect(float x,float y,float w,float h,uint c,float d,bool t){}
  public void Sprite(int id,float x,float y,int w,int h,float a,float d,bool t){Ids.Add(id);}
 }
 static void Load(BattleSimulation sim)
 {
  var ids=Rubezh.Graphics.LookSprites(sim);var pages=Rubezh.Graphics.PagesFor(sim.Map);
  Rubezh.Graphics.AddLookPages(pages,sim);Rubezh.Graphics.KeepPages(pages);Rubezh.Graphics.KeepLookSprites(ids);
  foreach(int page in pages)if(!Rubezh.Graphics.HasPage(page,ids))Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/"+SpritePackIndex.Path(page)+".bytes"),looks:ids);
  Rubezh.Graphics.BakeLooks(sim);
 }
 static void Main()
 {
  Resources.Loader=p=>p=="RubezhHdGpu"||p=="RubezhNativeDepth"?new Shader():null;
  var sim=new BattleSimulation(144);sim.Soldiers.Clear();
  int n=0;
  foreach(int h in GreenLook.ActiveHeads)
  {
   var s=new Soldier{Id=h==82?0:++n,Side=Side.Green,Role=h==82?Role.Commander:new[]{88,106,109}.Contains(h)?Role.Medic:new[]{62,69,143}.Contains(h)?Role.Sniper:Role.Rifleman,Look=new GreenLook{headId=h,lightId=51,familyId=89}};
   sim.Soldiers.Add(s);
  }
  for(int role=0;role<6;role++)for(int variant=0;variant<2;variant++)sim.Soldiers.Add(new Soldier{Id=100+role*2+variant,Side=Side.Red,Role=(Role)role});
  Rubezh.Graphics.ConfigurePacks();Load(sim);
  var wanted=Rubezh.Graphics.LookSprites(sim);Check(wanted.Count==112,"all 28 characters / four directions loaded");
  var sink=new Sink();var canvas=new PixelCanvas(640,896);canvas.Gpu=sink;
  foreach(var s in sim.Soldiers)for(int frame=0;frame<4;frame++)
  {
   int id=PawnAtlas.Sprite(s,frame),w,h;var pixels=Rubezh.Graphics.SpriteData(id,0,out w,out h);
   Check(h>=720&&h<=896&&w<=640,"HD trimmed dimensions "+id);
   int top=h,bottom=-1;
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[(y*w+x)*4+3]>=64){top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
   Check(bottom-top+1>=720,"visible figure >=720px "+id);
   sink.Ids.Clear();Rubezh.Graphics.PersonSprite(canvas,frame,320,448,640,s.Side==Side.Red,0,0,0,s.Look,s.Role,s.Id);
   Check(sink.Ids.Count==1&&sink.Ids[0]==id,"one whole sprite, no separate head/body "+id);
   for(int armor=0;armor<3;armor++)for(int rig=0;rig<3;rig++)
   {
    sink.Ids.Clear();Rubezh.Graphics.PersonSprite(canvas,frame,320,448,640,s.Side==Side.Red,0,armor,rig,s.Look,s.Role,s.Id);
    Check(sink.Ids.Count==1&&sink.Ids[0]==id,"appearance independent of gear level "+id);
   }
   canvas.Gpu=null;canvas.Clear(0);Rubezh.Graphics.OriginalSampling(true);Rubezh.Graphics.PersonSprite(canvas,frame,320,448,640,s.Side==Side.Red,0,0,0,s.Look,s.Role,s.Id);
   Check(canvas.Pixels.Any(p=>p!=0),"CPU Ultra whole pawn "+id);canvas.Gpu=sink;
  }
  Rubezh.Graphics.OriginalSampling(false);
  canvas.Gpu=null;
  var painter=new BattleRenderer(BattleRenderer.HighScale);
  foreach(bool ultra in new[]{false,true}) using(var gpu=new HdGpuRenderer(painter.Canvas,ultra,false))
  {
   gpu.Render(painter,sim,false,-1,1280,720,new Rect(0,0,1,1));
   foreach(int id in wanted){gpu.Sprite(id,100,100,36,52,0,100,false);checks++;}
   if(ultra){gpu.RenderForeground(painter,sim,-1);Check(gpu.ForegroundTexture.width==1280&&gpu.ForegroundTexture.height==720,"whole pawns in full-resolution foreground");}
  }
  for(int id=GreenLook.BakedStart;id<GreenLook.BakedEnd;id++)Check(!Rubezh.Graphics.HasSprite(id),"no runtime baked duplicate "+id);
  Check(!Rubezh.Graphics.HasSprite(GreenLook.UniformBodyStart),"unused separate body not loaded");
  Check(!Rubezh.Graphics.HasSprite(175),"old red pawn not loaded");
  Console.WriteLine("ALL CHARACTERS pixel bytes="+Rubezh.Graphics.LoadedPixelBytes);
  // Same class and direction must expose both deterministic variants.
  for(int role=0;role<6;role++)Check(PawnAtlas.Sprite(null,true,(Role)role,0,0)!=PawnAtlas.Sprite(null,true,(Role)role,1,0),"two variants "+role);
  // Change squads without a global release: old appearances must leave memory.
  var keep=sim.Soldiers[0];sim.Soldiers.Clear();sim.Soldiers.Add(keep);Load(sim);
  Check(Rubezh.Graphics.LookSprites(sim).Count==4,"one character after squad switch");
  Check(!Rubezh.Graphics.HasSprite(PawnAtlas.RedStart),"red atlas released on squad switch");
  Check(Rubezh.Graphics.HasSprite(Rubezh.Graphics.VogSprite),"VOG retained");
  Rubezh.Graphics.ReleaseSceneSprites();Check(Rubezh.Graphics.LoadedPixelBytes==0,"all scene graphics released");
  Console.WriteLine("PASS whole pawn checks="+checks+"; portable rendering/API contracts, not native Unity");
 }
}
