using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json;using Rubezh.Camp;using SilentOrders.Rendering;
class Looks096{
 static int n;static void Check(bool yes,string s){if(!yes)throw new Exception(s);n++;Console.WriteLine("PASS "+s);}
 static void Main(string[] args){
 var c=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(args[0]),CampModel.JsonSettings);
 foreach(string role in new[]{"Стрелок","Медик","Снайпер"}){
 var m=new CampModel(c,rng:()=>.37);var seen=new HashSet<int>(m.S.seenHeads);
 int[] allowed=role=="Снайпер"?new[]{62,69,143}:role=="Медик"?new[]{88,106,109}:new[]{57,59,63,107,116,131,133,134,136};
 var obtained=new HashSet<int>();
 for(int i=0;i<60;i++){
 var before=new HashSet<int>(m.S.seenHeads);var look=m.MakeLook(role);
 Check(!allowed.Any(h=>!before.Contains(h)) || !before.Contains(look.headId),"unseen head priority "+role);
 Check(look.headId!=82,"Kedr exclusive "+role);
 if(before.Contains(look.headId)) { // Once repeats begin, every allowed head must already be recorded.
   obtained.Add(look.headId);
 } else obtained.Add(look.headId);
 var reloaded=new CampModel(c,CampModel.Decode(m.Serialize()),()=>.37);
 Check(reloaded.S.seenHeads.Contains(look.headId),"head history persisted "+role);
 Check(reloaded.MakeLook(role,false,look).headId==look.headId,"appearance stable "+role);
 }
 Check(allowed.All(h=>m.S.seenHeads.Contains(h)),"diverse pool "+role);
 }
 var canvas=new PixelCanvas(100,100,1);canvas.SetObjectTransform(50,50,.85f,.85f,0);
 float x=70,y=30,w=24,h=7;canvas.TransformSprite(ref x,ref y,ref w,ref h);
 Check(Math.Abs(x-67)<.001&&Math.Abs(y-33)<.001&&Math.Abs(w-20.4)<.001,"sprite position and weapon size 85%");
 canvas.ClearObjectTransform();x=70;y=30;w=24;h=7;canvas.TransformSprite(ref x,ref y,ref w,ref h);
 Check(x==70&&w==24,"no transform leaks into menus/next objects");
 Console.WriteLine("PASS TOTAL "+n+" appearance/render checks");
 }
}
