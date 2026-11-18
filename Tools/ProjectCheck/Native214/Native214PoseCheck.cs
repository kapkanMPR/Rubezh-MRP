using System;
using Newtonsoft.Json;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SilentOrders.Core;
using Rubezh.UnityPort;
using UnityEngine;
static class Native214PoseCheck {
static readonly BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
static int checks;
static object GetProperty(object o,string f)=>o.GetType().GetProperty(f,F).GetValue(o);
static object Get(object o,string f)=>o.GetType().GetField(f,F).GetValue(o);
static void Check(bool yes,string why){checks++;if(!yes)throw new Exception("Pose214: "+why);}
static void Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,F)?.Invoke(o,args);
static V2 Position(NativeBattleComposer p,Soldier s)=>(V2)p.GetType().GetMethod("VisualPosition",F).Invoke(p,new object[]{s});
static bool Near(float a,float b)=>Math.Abs(a-b)<.001f;
static Dictionary<long,float[]> Parts(HdGpuRenderer r){
var draws=((IEnumerable)Get(r,"nativeDraws")).Cast<object>().ToDictionary(d=>(long)Get(d,"Key"),d=>(bool)Get(d,"Sprite"));
var answer=new Dictionary<long,float[]>();var nodes=(IDictionary)Get(r,"unityActorNodes");
foreach(DictionaryEntry item in nodes){long key=(long)item.Key;long group=(key>>32)-1;if(group<0||group>(typeof(NativeBattleComposer).GetMethod("ObserveTick")!=null?2:0)||!(bool)Get(item.Value,"Active"))continue;
var node=item.Value;var go=(GameObject)Get(node,"Object");var point=(Vector3)Get(node,"Origin");var scale=(Vector3)Get(node,"Scale");float angle=(float)Get(node,"Angle");float z=point.z;
for(var parent=go.transform.parent;parent!=null;parent=parent.parent){
var shift=parent.localPosition;if(shift.x==0&&shift.y==0&&shift.z==0)shift=parent.position;
var a=parent.localRotation.zDegrees!=0?parent.localRotation.zDegrees:parent.rotation.zDegrees;float rad=a*(float)Math.PI/180,c=(float)Math.Cos(rad),s=(float)Math.Sin(rad),x=point.x*parent.localScale.x,y=point.y*parent.localScale.y;
point=new Vector3(x*c-y*s+shift.x,x*s+y*c+shift.y,point.z+shift.z);angle+=a;scale.x*=parent.localScale.x;scale.y*=parent.localScale.y;
}
var data=(Vector4)Get(node,"InstanceData");answer[key]=new[]{point.x,point.y,angle,scale.x,scale.y,data.x+(point.z-z)*1024,draws.ContainsKey(key)&&draws[key]?1:0};
}return answer;
}
public static void Run(HdGpuRenderer renderer,NativeBattleComposer painter,BattleSimulation sim){
bool modern=typeof(NativeBattleComposer).GetMethod("ObserveTick")!=null;var goldens=new List<object>();
var s=sim.Soldiers[0];sim.Fog.Enabled=false;s.Deployed=true;s.Health=100;s.Moving=false;s.LastFire=-100;s.Position=new V2(5,5);s.Facing=new V2(1,0);sim.TickNumber++;sim.Time+=.05f;
Call(painter,"ObserveTick",sim);Call(painter,"SetInterpolation",1f);renderer.Render(painter,sim,false,0,960,540);
s.Position=new V2(6,5);sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);
Call(painter,"SetInterpolation",0f);if(modern)Check(Near(Position(painter,s).X,5),"alpha0 previous position");
Call(painter,"SetInterpolation",.5f);if(modern)Check(Near(Position(painter,s).X,5.5f),"alpha half midpoint");
Call(painter,"SetInterpolation",1f);if(modern)Check(Near(Position(painter,s).X,6),"alpha1 current position");
var changes=new Action[]{()=>{},()=>s.Facing=new V2(0,1),()=>s.LastFire=sim.Time,()=>s.Crouched=!s.Crouched,()=>s.Health=55,()=>s.WeaponVariant=2,()=>s.Position=new V2(7.25f,5.7f)};
foreach(var change in changes){change();sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);Call(painter,"SetInterpolation",1f);renderer.Render(painter,sim,false,0,960,540);var retained=Parts(renderer);goldens.Add(retained.Values.OrderBy(x=>x[0]).ThenBy(x=>x[1]).ToArray());
using(var fresh=new HdGpuRenderer(painter.Canvas,true,false,true)){fresh.Render(painter,sim,false,0,960,540);var actual=Parts(fresh);Check(actual.Count>0&&actual.Count==retained.Count,"fresh topology matches retained: "+retained.Count+" vs "+actual.Count+"; retained="+string.Join(",",retained.Keys)+" fresh="+string.Join(",",actual.Keys));foreach(var pair in retained){Check(actual.ContainsKey(pair.Key),"stable body part identity");for(int i=0;i<pair.Value.Length;i++)Check(Near(pair.Value[i],actual[pair.Key][i]),"retained/fresh transform+depth component "+i);}}
}
if(modern){
var other=sim.Soldiers[1];s.Position=new V2(10,10);other.Position=new V2(10,12);other.Deployed=true;other.Health=100;s.Moving=other.Moving=false;sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);Call(painter,"SetInterpolation",1f);renderer.Render(painter,sim,false,0,960,540);
s.Position=new V2(10,13);other.Position=new V2(10,11);sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);Call(painter,"SetInterpolation",0f);renderer.Render(painter,sim,false,0,960,540);
int before=renderer.NativeActorRebuilds;
Func<int,int> index=group=>((IEnumerable)Get(renderer,"nativeDraws")).Cast<object>().ToList().FindIndex(d=>(int)Get(d,"Actor")==group);
Check(index(1)<index(5),"initial depth order actor0 before actor1");Call(painter,"SetInterpolation",.25f);renderer.Render(painter,sim,false,0,960,540);Check(index(1)<index(5),"order unchanged before interpolated crossing");
Call(painter,"SetInterpolation",.75f);renderer.Render(painter,sim,false,0,960,540);Check(index(1)>index(5),"interpolated crossing reverses body order");Check(renderer.NativeActorRebuilds==before,"interpolated depth crossing does not rebuild bodies");
}
s.Position=new V2(30,20);sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);Call(painter,"SetInterpolation",0f);if(modern)Check(Near(Position(painter,s).X,30),"teleport snaps instead of sweeping across map");
System.IO.File.WriteAllText(System.IO.Path.Combine(Environment.GetEnvironmentVariable("RUBEZH_TEST_OUTPUT")??".","pose-goldens.json"),JsonConvert.SerializeObject(goldens));
Call(painter,"SetInterpolation",1f);
if(modern){
s.Position=new V2(29.5f,20);sim.TickNumber++;sim.Time+=.05f;Call(painter,"ObserveTick",sim);
var app=new RubezhApp();Action<string,object> set=(name,value)=>typeof(RubezhApp).GetField(name,F).SetValue(app,value);
set("battle",sim);set("painter",painter);set("hdBattle",renderer);set("testBattle",true);set("fullVisibility",true);set("accumulator",sim.Config.Tick*.25f);set("paused",false);
Call(app,"UpdatePresentationInterpolation");Check(Near((float)GetProperty(painter,"InterpolationAlpha"),.25f),"app accumulator controls interpolation");
set("paused",true);Call(app,"UpdatePresentationInterpolation");Check(Near((float)GetProperty(painter,"InterpolationAlpha"),1),"pause forces current endpoint");
set("paused",false);s.Position=new V2(29.4f,20);sim.TickNumber++;sim.Time+=.05f;set("speed",4f);set("accumulator",sim.Config.Tick*.75f);Call(app,"UpdatePresentationInterpolation");Check(Near((float)GetProperty(painter,"InterpolationAlpha"),.75f),"speed not double-applied to accumulated fraction");
Call(app,"RenderMeasured");Check(!(bool)typeof(RubezhApp).GetMethod("NeedsBattleRender",F).Invoke(app,null),"same rendered interpolation skips frame");
set("accumulator",sim.Config.Tick*.5f);Call(app,"UpdatePresentationInterpolation");Check((bool)typeof(RubezhApp).GetMethod("NeedsBattleRender",F).Invoke(app,null),"between-tick interpolation schedules frame");
set("paused",true);Call(app,"RenderMeasured");Check(!(bool)typeof(RubezhApp).GetMethod("NeedsBattleRender",F).Invoke(app,null),"paused unchanged frame skips work");
set("paused",false);set("accumulator",sim.Config.Tick*.1f);Call(app,"UpdatePresentationInterpolation");Check(Near((float)GetProperty(painter,"InterpolationAlpha"),1),"resume without new tick does not jump backward");
s.Position=new V2(29,20);sim.TickNumber++;sim.Time+=.05f;Call(app,"UpdatePresentationInterpolation");Check(Near((float)GetProperty(painter,"InterpolationAlpha"),.1f),"next tick resumes interpolation after pause");
}
Console.WriteLine("PASS Native214PoseCheck: "+checks+" interpolation endpoint/fresh-retained transform/depth invariants (contract transform model; GPU pending).");
}
}
