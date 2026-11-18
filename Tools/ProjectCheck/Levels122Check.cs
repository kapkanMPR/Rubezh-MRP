using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json;using Rubezh;using Rubezh.Camp;using Rubezh.UnityPort;using SilentOrders.Core;using SilentOrders.Rendering;using UnityEngine;
class Levels122Check {
 static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 static void Load(BattleSimulation b){var pages=Rubezh.Graphics.PagesFor(b.Map);Rubezh.Graphics.AddLookPages(pages,b);Rubezh.Graphics.KeepPages(pages);var looks=Rubezh.Graphics.LookSprites(b);Rubezh.Graphics.KeepLookSprites(looks);foreach(int p in pages)if(!Rubezh.Graphics.HasPage(p,looks))Rubezh.Graphics.LoadPage(p,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/"+SpritePackIndex.Path(p)+".bytes"),looks:looks);Rubezh.Graphics.BakeLooks(b);
 Check(Rubezh.Graphics.HasSprite(Rubezh.Graphics.VogSprite),"VOG survives BakeLooks");
 int vw,vh;var vog=Rubezh.Graphics.SpriteData(Rubezh.Graphics.VogSprite,0,out vw,out vh);
 Rubezh.Graphics.BakeLooks(b);
 Check(object.ReferenceEquals(vog,Rubezh.Graphics.SpriteData(Rubezh.Graphics.VogSprite,0,out vw,out vh)),"VOG survives repeated BakeLooks");}
 static void Main(){
 Resources.Loader=p=>p=="RubezhHdGpu"||p=="RubezhNativeDepth"?new Shader():File.Exists("Assets/Rubezh/Resources/"+p+".bytes")?new TextAsset{bytes=File.ReadAllBytes("Assets/Rubezh/Resources/"+p+".bytes")}:File.Exists("Assets/Rubezh/Resources/"+p+".bytes")?new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/"+p+".bytes")}:null;
 var catalog=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"));var camp=new CampModel(catalog,null,()=>.37);
 foreach(var template in new[]{"kedr","mg","mg-medved","mg-bury","rpg","rpg-pepel","rpg-fugas","s1","s2","s3","med","med-dobry","med-hirurg","sn","sn-morok","sn-manul"}){var l=camp.CanonicalLook(template);Check(GreenLook.ActiveHeads.Contains(l.headId),"canonical head "+template);var g=new GreenLook{headId=l.headId,lightId=l.lightId,familyId=l.familyId,heavyId=l.heavyId};
 for(int a=0;a<3;a++)for(int rig=0;rig<3;rig++)for(int f=0;f<4;f++){Check(g.Body(a,rig,f)==GreenLook.UniformBodyStart+f,"fixed body");Check(SpritePackIndex.Page[PawnAtlas.Sprite(g,false,Role.Rifleman,0,f)]>=0,"whole pawn payload");Check(Rubezh.Graphics.SoldierSprite(a,rig,true,f)==175+f,"red fixed body");Check(Rubezh.Graphics.SoldierSprite(a,rig,false,f)==171+f,"primitive fixed body");}}
 var bury=new GreenLook{headId=63,lightId=79,familyId=91};bury.Ensure(Role.MachineGunner,false);Check(bury.headId==63,"Bury head survives Ensure");
 foreach(int old in GreenLook.Heads.Where(h=>!GreenLook.ActiveHeads.Contains(h))){var l=new GreenLook{headId=old,lightId=51,familyId=53};l.Ensure(Role.Rifleman,false);Check(GreenLook.ActiveHeads.Contains(l.headId),"retired head migrated");}
 Rubezh.Graphics.ConfigurePacks();long max=0;int steps=0;
 for(int mission=1;mission<=20;mission++){
 var b=new BattleSimulation(12100+mission);Check(CampaignScenario.Apply(b,mission+",19,0,0,0,0"),"campaign "+mission);var level=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(File.ReadAllText("Validation/Current/maps/Campaign_"+mission.ToString("00")+".bytes")).Create();Check(level!=null&&RubezhLevelRuntime.ApplyLevel(b,level),"map "+mission+" "+RubezhLevelRuntime.LastError);
 var stats=b.Soldiers.Select(s=>s.ArmorLevel+","+s.RigLevel).ToArray();Load(b);Check(stats.SequenceEqual(b.Soldiers.Select(s=>s.ArmorLevel+","+s.RigLevel)),"visuals do not mutate stats");
 var exterior=new ExteriorLayout(b.Map);
 foreach(var exit in exterior.Exits)for(int t=exit.Start;t<exit.End;t++){
 float x=exit.Side==0?-.01f:exit.Side==1?b.Map.Columns+.01f:t+.5f;
 float y=exit.Side==2?-.01f:exit.Side==3?b.Map.Rows+.01f:t+.5f;
 StudioAsset surface;Check(exterior.RoadAt(x,y,0,out surface),"continuous road seam "+mission);
 }
 for(int ey=-20;ey<b.Map.Rows+20;ey+=5)for(int ex=-20;ex<b.Map.Columns+20;ex+=5)if(exterior.PropAllowed(ex,ey)){
 StudioAsset surface;Check(!exterior.RoadAt(ex+2,ey+2,0,out surface),"exterior prop excludes road");}
 Console.WriteLine("EXTERIOR "+mission+" style="+exterior.Style+" exits="+exterior.Exits.Count);
 var originalTiles=(Tile[])b.Map.Tiles.Clone();int originalRevision=b.Map.Revision;int originalColumns=b.Map.Columns,originalRows=b.Map.Rows;
 var renderer=new BattleRenderer(BattleRenderer.HighScale);renderer.Render(b);
 Check(originalTiles.SequenceEqual(b.Map.Tiles)&&originalRevision==b.Map.Revision&&originalColumns==b.Map.Columns&&originalRows==b.Map.Rows,"exterior does not mutate playable map");
 if(true){using(var image=File.Create("Validation/Current/exterior-"+mission+".ppm")){var header=System.Text.Encoding.ASCII.GetBytes("P6\n"+renderer.Canvas.Width+" "+renderer.Canvas.Height+"\n255\n");image.Write(header,0,header.Length);foreach(uint pixel in renderer.Canvas.Pixels){image.WriteByte((byte)pixel);image.WriteByte((byte)(pixel>>8));image.WriteByte((byte)(pixel>>16));}}}

 for(int n=0;n<120;n++){b.Step();steps++;}renderer.Render(b);
 if(mission==1){
 var look=b.Soldiers.First(x=>x.Side==Side.Green).Look;
 var redUnit=b.Soldiers.First(x=>x.Side==Side.Red);
 foreach(bool enemy in new[]{false,true})for(int f=0;f<4;f++){
 var baseline=new PixelCanvas(160,224);Rubezh.Graphics.PersonSprite(baseline,f,80,112,150,enemy,0,0,0,look,redUnit.Role,redUnit.Id);
 for(int armor=0;armor<3;armor++)for(int rig=0;rig<3;rig++){var sample=new PixelCanvas(160,224);Rubezh.Graphics.PersonSprite(sample,f,80,112,150,enemy,0,armor,rig,look,redUnit.Role,redUnit.Id);Check(baseline.Pixels.SequenceEqual(sample.Pixels),"pixel-identical appearance across levels");}}
 foreach(bool primitive in new[]{false,true})foreach(bool ultra in new[]{false,true}){Rubezh.Graphics.PrimitiveBattleMode=primitive;using(var gpu=new HdGpuRenderer(renderer.Canvas,ultra,primitive)){
 gpu.Render(renderer,b,false,-1,1536,691,new Rect(.25f,.25f,.333333f,.333333f));
 gpu.Sprite(Rubezh.Graphics.VogSprite,300,300,12,8,0,300,false);
 var sceneTexture=gpu.Texture;
 if(ultra){gpu.RenderForeground(renderer,b,-1);Check(gpu.ForegroundTexture!=null&&gpu.ForegroundTexture.width==1536&&gpu.ForegroundTexture.height==691,"Ultra foreground uses viewport resolution");Check(object.ReferenceEquals(sceneTexture,gpu.Texture),"foreground retains scene target");gpu.Render(renderer,b,false,-1,1536,691,new Rect(.2f,.2f,.333333f,.333333f));Check(renderer.SharpScenery,"Ultra uses original scenery sprites");}
}}Rubezh.Graphics.PrimitiveBattleMode=false;}
 max=Math.Max(max,Rubezh.Graphics.LoadedPixelBytes);Console.WriteLine("PASS mission="+mission+" soldiers="+b.Soldiers.Count+" pages="+Rubezh.Graphics.LoadedPageCount+" pixel_bytes="+Rubezh.Graphics.LoadedPixelBytes);Rubezh.Graphics.ReleaseSceneSprites();Check(Rubezh.Graphics.LoadedPixelBytes==0&&Rubezh.Graphics.LoadedPageCount==0,"campaign packs released");UnityEngine.Object.Destroy(level);GC.Collect();
 }
 for(int map=1;map<=50;map++){
 var b=new BattleSimulation(122000+map);var asset=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(Rubezh.UnityPort.PackedLevel.Decode(System.IO.File.ReadAllBytes("Assets/Rubezh/Resources/War/war-"+map.ToString("00")+".bytes"))).Create();
 Check(RubezhLevelRuntime.ApplyLevel(b,asset),"war map "+map+" "+RubezhLevelRuntime.LastError);Load(b);var painter=new BattleRenderer(BattleRenderer.HighScale);painter.Render(b);for(int n=0;n<20;n++)b.Step();painter.Render(b);Rubezh.Graphics.ReleaseSceneSprites();Check(Rubezh.Graphics.LoadedPixelBytes==0&&Rubezh.Graphics.LoadedPageCount==0,"war packs released "+map);Console.WriteLine("PASS WAR "+map+" soldiers="+b.Soldiers.Count+" released_bytes="+Rubezh.Graphics.LoadedPixelBytes);UnityEngine.Object.Destroy(asset);GC.Collect();
 }
 var stress=new BattleSimulation(121100);
 for(int i=10;i<100;i++){bool green=i<55;int index=green?i-5:i-50;var u=new Soldier{Id=i,Name="STRESS-"+i,Side=green?Side.Green:Side.Red,Role=(Role)(index%5),BuddyId=i,Element=index%2,ArmorLevel=index%3,RigLevel=(index/3)%3,Position=stress.Map.NearestOpen(new V2(green?5+index%5:44+index%5,2+index/5*3)),NextSense=i*.023f,NextDecision=i*.055f};u.OrderAnchor=u.Destination=u.Position;u.Ammo=u.MagazineSize;u.Reserve=u.ReserveCapacity;stress.Soldiers.Add(u);}
 Check(stress.Soldiers.Count(x=>x.Side==Side.Green)==50&&stress.Soldiers.Count(x=>x.Side==Side.Red)==50,"50v50 fixture");Load(stress);var sr=new BattleRenderer(BattleRenderer.HighScale);sr.Render(stress);for(int n=0;n<120;n++)stress.Step();sr.Render(stress);using(var gpu=new HdGpuRenderer(sr.Canvas))gpu.Render(sr,stress,false,-1,sr.Canvas.Width,sr.Canvas.Height);Console.WriteLine("PASS 50v50 render and 120 simulation steps; pixel_bytes="+Rubezh.Graphics.LoadedPixelBytes);
 Console.WriteLine("PASS checks="+checks+" campaign_steps="+steps+" max_loaded_pixels="+max+"; Unity API contracts, not Unity/Android execution");
 }
}
