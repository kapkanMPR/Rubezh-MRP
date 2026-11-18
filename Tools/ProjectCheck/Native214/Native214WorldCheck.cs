using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;

static class Native214WorldCheck
{
    static int checks;
    static void Check(bool value,string message) { checks++; if(!value) throw new Exception("World items: "+message); }
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static IDictionary Groups(HdGpuRenderer renderer) => (IDictionary)typeof(HdGpuRenderer).GetField("directGroups",Flags).GetValue(renderer);
    public static void Run(HdGpuRenderer renderer, NativeBattleComposer painter, BattleSimulation sim)
    {
        // Runs only shared world recipes: no AI or actor animation obscures invalidation counts.
        var begin = typeof(NativeBattleComposer).GetMethod("BeginWorldItems",Flags);
        var end = typeof(NativeBattleComposer).GetMethod("EndWorldItems",Flags);
        var blood = typeof(BattleVisualRecipes).GetMethod("DrawBloodTraces",Flags);
        var equipment = typeof(BattleVisualRecipes).GetMethod("DrawGroundEquipment",Flags);
        var debris = typeof(BattleVisualRecipes).GetMethod("DrawCoverDebris",Flags);
        var stage = typeof(BattleVisualRecipes).GetMethod("SetCompositionStage",Flags);
        var submit=typeof(HdGpuRenderer).GetMethod("AddDirectVisuals",Flags);
        var submissionOrder=typeof(HdGpuRenderer).GetField("unitySubmissionOrder",Flags);
        var capture=typeof(HdGpuRenderer).GetField("capturingNativeActors",Flags);
        Action frame = () => {
            var sink=painter.Canvas.Gpu; painter.Canvas.Gpu=renderer;
            bool wasCapturing=(bool)capture.GetValue(renderer);capture.SetValue(renderer,true);
            try {
            begin.Invoke(painter,new object[]{renderer,false}); renderer.BeginDynamic();
            stage.Invoke(painter,new object[]{renderer,-1}); blood.Invoke(painter,new object[]{sim});
            stage.Invoke(painter,new object[]{renderer,0}); equipment.Invoke(painter,new object[]{sim,false}); debris.Invoke(painter,new object[]{sim,false});
            stage.Invoke(painter,new object[]{renderer,1}); equipment.Invoke(painter,new object[]{sim,true}); debris.Invoke(painter,new object[]{sim,true});
            end.Invoke(painter,null); renderer.EndDynamic();
            submissionOrder.SetValue(renderer,0);submit.Invoke(renderer,null);
            } finally { painter.Canvas.Gpu=sink;capture.SetValue(renderer,wasCapturing); }
        };
        sim.Fog.Enabled = false; sim.Time = 10;
        sim.BloodTraces.Clear(); sim.GroundAmmo.Clear(); sim.GroundWeapons.Clear(); sim.GroundLaunchers.Clear(); sim.GroundTourniquets.Clear();
        sim.CoverDebris.Clear(); sim.ShellCasings.Clear(); sim.CoverRemnants.Clear();
        foreach(var s in sim.Soldiers) { s.Deployed=false; s.WeaponDropped=false; }
        var point = sim.Map.Objective;
        var mark = new BloodTrace {Position=point}; sim.BloodTraces.Add(mark);
        var part = new CoverDebrisPiece {From=point,To=point,Duration=1,Born=0,SpriteId=37,Width=3,HeightPixels=1}; sim.CoverDebris.Add(part);
        var ammo = new GroundAmmo {From=point,To=point,Duration=1,Born=0,Family=0}; sim.GroundAmmo.Add(ammo);
        frame(); int built=painter.WorldItemRebuilds, reused=painter.WorldItemReuseHits;
        Check(painter.RetainedWorldItems==3,"one retained identity per mark, fragment and magazine");
        var persistentNodes=(IDictionary)typeof(HdGpuRenderer).GetField("unityActorNodes",Flags).GetValue(renderer);
        var initialNodes=new Dictionary<long,object>();
        foreach(DictionaryEntry entry in persistentNodes) if(((long)entry.Key >> 32) < -3) initialNodes.Add((long)entry.Key,entry.Value);
        for(int i=0;i<100;i++){sim.Time+=.1f;frame();}
        foreach(var node in initialNodes) Check(ReferenceEquals(persistentNodes[node.Key],node.Value),"stable world item keeps its Unity node identity");
        Check(painter.WorldItemRebuilds==built && painter.WorldItemReuseHits==reused+300,"100 settled frames run zero world recipes");
        sim.Fog.Enabled=true;sim.Fog.Current=null;frame();
        Check(painter.RetainedWorldItems==0,"fog hides every ground item and removes cached nodes");
        sim.Fog.Enabled=false;frame();
        Check(painter.RetainedWorldItems==3,"revealed world items regenerate without stale hidden graphics");
        built=painter.WorldItemRebuilds;
        bool primitive=Rubezh.Graphics.PrimitiveBattleMode;
        Rubezh.Graphics.PrimitiveBattleMode=!primitive;frame();
        Check(painter.WorldItemRebuilds==built+3,"quality artwork switch invalidates retained recipes");
        Rubezh.Graphics.PrimitiveBattleMode=primitive;frame();built=painter.WorldItemRebuilds;
        ammo.Family=6;frame();Check(painter.WorldItemRebuilds==built+1,"changed magazine artwork invalidates only that item");built++;
        ammo.Collected=true;frame();Check(painter.RetainedWorldItems==2,"pickup removes retained item");
        sim.BloodTraces.RemoveAt(0);sim.BloodTraces.Add(new BloodTrace{Position=point});frame();
        Check(painter.RetainedWorldItems==2 && painter.WorldItemRebuilds==built+1,"front-evicted mark does not reuse another mark identity");
        part.From=point;part.To=point+new V2(2,0);part.Born=sim.Time;part.Duration=1;part.Peak=2;frame();built=painter.WorldItemRebuilds;
        sim.Time+=.5f;frame();Check(painter.WorldItemRebuilds==built+1,"flying fragment updates arc");
        sim.Time+=1;frame();built=painter.WorldItemRebuilds;sim.Time+=1;frame();Check(painter.WorldItemRebuilds==built,"landed fragment becomes immutable");
        sim.CoverDebris.Clear();frame();Check(painter.RetainedWorldItems==1,"destroyed/remnant eviction releases retained group");
        var gun = new GroundWeapon {From=point,To=point,Duration=0,Variant=0};sim.GroundWeapons.Add(gun);frame();Check(painter.RetainedWorldItems==2,"new dropped firearm appears");
        gun.Collected=true;frame();Check(painter.RetainedWorldItems==1,"claimed firearm disappears");
        var thrownAmmo=new GroundAmmo {From=point,To=point+new V2(2,0),Born=sim.Time,Duration=1,Family=0};
        sim.GroundAmmo.Add(thrownAmmo);frame();int flyingParts=renderer.DirectInstanceCount;
        sim.Time+=2;frame();
        Check(renderer.DirectInstanceCount==flyingParts-1,"landing removes airborne magazine shadow immediately");
        thrownAmmo.Collected=true;frame();
        var soldier=sim.Soldiers[0];soldier.Deployed=true;soldier.Bleeding=true;frame();built=painter.WorldItemRebuilds;
        soldier.Position+=new V2(1,0);frame();Check(painter.WorldItemRebuilds==built+1,"dragged casualty pool follows position");
        soldier.Bleeding=false;soldier.Deployed=false;sim.BloodTraces.Clear();frame();
        Check(painter.RetainedWorldItems==0,"recovered casualty and removed traces leave no cache entries");
        int tombstones=0;foreach(DictionaryEntry entry in Groups(renderer))if((int)entry.Key<=-100)tombstones++;
        Check(tombstones==0,"removed items leave no DirectGroup tombstones");
        int staleNodes=0;
        var nodes=(IDictionary)typeof(HdGpuRenderer).GetField("unityActorNodes",Flags).GetValue(renderer);
        foreach(DictionaryEntry entry in nodes) if(((long)entry.Key >> 32) < -3) staleNodes++;
        Check(staleNodes==0,"removed items immediately destroy their retained Unity nodes");
        Console.WriteLine("PASS Native214WorldCheck: "+checks+" checks; stable 100-frame recipe count, dynamic arcs, mutation and removal.");
    }
}
