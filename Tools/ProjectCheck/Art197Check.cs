using System;using System.IO;using Rubezh;using SilentOrders.Core;using SilentOrders.Rendering;
class Art197Check {
 static int n;static void Check(bool b,string s){if(!b)throw new Exception(s);n++;}
 static void Main(){
  Check(WeaponAppearance.Sprite(0,null)==-1,"base retains original");
  foreach(int rarity in new[]{1,2,3,2,1}){
   var up=WeaponProgression.At(0,rarity,1);int id=WeaponAppearance.Sprite(0,up);
   Check(id==(rarity==1?-1:rarity==2?7974:7975),"rarity and rollback "+rarity);
   Check(WeaponAppearance.ItemPath("ak","original",rarity)==(rarity==1?"original":"weapons/ak-"+(rarity==2?"rare":"epic")+".png"),"UI rarity");
   foreach(int v in new[]{1,2,3,4,5,6,7,9,11})Check(WeaponAppearance.Sprite(v,WeaponProgression.At(v,rarity,1))==-1,"other weapons unchanged");
  }
  Graphics.ConfigurePacks();var b=new BattleSimulation(197);var required=Graphics.SceneSprites(b);Check(required.Contains(7974)&&required.Contains(7975),"required textures");Check(Graphics.CommonPages().Contains(375),"pack residency");
  Graphics.LoadPage(375,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p0375.bytes"),required:required);
  foreach(int id in new[]{7974,7975}){Check(Graphics.HasSprite(id),"loaded");int w,h;var bytes=Graphics.SpriteData(id,0,out w,out h);Check(w>1500&&h>400&&bytes.Length==w*h*4,"full resolution");Check(Math.Abs((float)h/w-WeaponAppearance.Aspect(id))<.0001,"aspect");Check(Graphics.LevelCount(id)>5,"mips");}
  var c=new PixelCanvas(300,180);Graphics.LooseItem(c,0,150,90,0,5,WeaponProgression.At(0,3,1));Check(Graphics.MissingSpriteCount==0,"ground art loaded");
  foreach(int rarity in new[]{2,3}){var s=b.Soldiers[0];s.WeaponVariant=0;s.WeaponUpgrade=WeaponProgression.At(0,rarity,1);s.Facing=new V2(1,0);BattleRenderer.WeaponPose(b,s,false,out var center,out var muzzle,out var angle,out var weapon,out var left);Check(!float.IsNaN(muzzle.X)&&!float.IsNaN(muzzle.Y)&&weapon==8,"muzzle pose");}
  Graphics.ReleaseSceneSprites();Check(!Graphics.HasSprite(7974),"release");Console.WriteLine("PASS "+n+" appearance checks; portable compilation, not Unity device rendering");
 }
}
