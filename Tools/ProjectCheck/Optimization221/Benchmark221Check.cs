using System;
using System.Reflection;
using System.IO;
using Rubezh.UnityPort;
using SilentOrders.Core;
using UnityEngine;
class Benchmark221Check
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool condition,string label){checks++;if(!condition)throw new Exception(label);}
    static object Get(object target,string name)=>target.GetType().GetField(name,Flags).GetValue(target);
    static void Set(object target,string name,object value)=>target.GetType().GetField(name,Flags).SetValue(target,value);
    static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Flags).Invoke(target,args);
    internal static int Run()
    {
        var capture=new FixedBenchmarkCapture(10,25,4);
        for(int i=1;i<=1800;i++)capture.Record(new PerformanceSpike{ms=i%2==0?10:20,cameraMs=8,time=10+i/60f,tick=25,mapRevision=4,cameraChanged=i==1});
        capture.Finish(40,25,4,true);var report=capture.Snapshot();
        Check(report.completed&&report.simulationStayedPaused&&report.mapStayedUnchanged,"valid completed fixed scene");
        Check(report.renderCount==1800&&report.operations.Length==1800,"whole benchmark survives beyond rolling 256 queue");
        Check(report.render.meanMs==15&&report.render.p95Ms==20&&report.render.maxMs==20,"all render statistics");
        Check(report.camera.meanMs==8&&report.cameraChanges==0,"first view excluded from movement count");
        Check(report.rendersPerSecond==60&&report.frameIntervals.count==1799,"isolated FPS and no pre-benchmark frame");
        capture.Record(new PerformanceSpike{ms=999,time=100,tick=100});
        Check(capture.Snapshot().renderCount==1800,"completed capture immutable");
        var overflow=new FixedBenchmarkCapture(0,25,4);
        for(int i=0;i<9000;i++)overflow.Record(new PerformanceSpike{ms=2,cameraMs=1,time=i*.001f,tick=i==8999?26:25,mapRevision=4});
        overflow.Finish(9,26,4,false);var extra=overflow.Snapshot();
        Check(extra.storedOperations==8192&&extra.droppedOperations==808,"bounded storage and explicit truncation");
        Check(extra.render.totalMs==18000&&extra.render.count==9000,"totals include overflow");
        Check(!extra.completed&&!extra.simulationStayedPaused,"invalid run flagged");

        var app=new RubezhApp();var battle=new BattleSimulation(219);
        Set(app,"battle",battle);Set(app,"screen",2);Set(app,"loading",false);Set(app,"paused",false);
        Set(app,"zoom",1.7f);Set(app,"pan",new Vector2(3,9));
        Call(app,"StartFixedBenchmark");Check((bool)Get(app,"benchActive")&&(bool)Get(app,"paused"),"benchmark acquired pause");
        var lockedPan=(Vector2)Get(app,"benchFixedPan");
        Set(app,"paused",false);int tick=battle.TickNumber;
        Call(app,"AdvanceSimulation",.2f);Call(app,"StepMeasured");
        Check(battle.TickNumber==tick,"benchmark locks both simulation entry points even when UI unpauses");
        Set(app,"zoom",3f);Set(app,"pan",new Vector2(100,100));Call(app,"MaintainFixedBenchmark");
        Check((float)Get(app,"zoom")==1&&(Vector2)Get(app,"pan")==lockedPan&&(bool)Get(app,"paused"),"camera and pause held after UI changes");
        Call(app,"FinishFixedBenchmark",false);
        Check(!(bool)Get(app,"benchActive")&&!(bool)Get(app,"paused")&&(float)Get(app,"zoom")==1.7f&&(Vector2)Get(app,"pan")==new Vector2(3,9),"cancel restores caller state");
        Call(app,"StartFixedBenchmark");var replacement=new BattleSimulation(220);Set(app,"battle",replacement);Set(app,"paused",false);Set(app,"zoom",2.3f);
        Call(app,"MaintainFixedBenchmark");
        Check(!(bool)Get(app,"benchActive")&&!(bool)Get(app,"paused")&&(float)Get(app,"zoom")==2.3f,"transition does not apply old camera/pause to replacement battle");
        Set(app,"battle",battle);Set(app,"paused",false);Set(app,"zoom",2.1f);Set(app,"pan",new Vector2(17,13));
        Call(app,"StartMovingBenchmark");
        Check((bool)Get(app,"benchActive")&&(bool)Get(app,"benchMoving")&&(bool)Get(app,"paused"),"moving benchmark acquires simulation lock");
        Call(app,"MaintainFixedBenchmark");var initialPan=(Vector2)Get(app,"pan");float initialZoom=(float)Get(app,"zoom");
        Time.realtimeSinceStartup+=1.5f;Call(app,"MaintainFixedBenchmark");
        Check((float)Get(app,"zoom")!=initialZoom&&(Vector2)Get(app,"pan")!=initialPan,"moving benchmark changes pan and zoom with elapsed time");
        tick=battle.TickNumber;Set(app,"paused",false);Call(app,"AdvanceSimulation",.2f);Call(app,"StepMeasured");
        Check(battle.TickNumber==tick,"moving camera never unlocks simulation");
        var motionReport=((FixedBenchmarkCapture)Get(app,"benchCapture")).Snapshot();
        Check(motionReport.cameraMotionTest&&motionReport.mode.EndsWith("/camera-sweep-v1"),"report explicitly identifies camera motion test");
        Check(JsonUtility.ToJson(motionReport).Contains("\"cameraMotionTest\":true"),"motion identity is serialized in exported JSON");
        Call(app,"FinishFixedBenchmark",false);
        Check(!(bool)Get(app,"benchActive")&&!(bool)Get(app,"paused")&&(float)Get(app,"zoom")==2.1f&&(Vector2)Get(app,"pan")==new Vector2(17,13),"moving cancellation restores pause and camera");
        Resources.Loader=path=>new Shader();
        Rubezh.Graphics.ConfigurePacks();
        UnityEngine.AddressableAssets.Addressables.Loader=key=>new UnityEngine.ResourceManagement.AsyncOperations.State{Result=key=="rubezh/native/catalog"?(UnityEngine.Object)new TextAsset{text="{\"version\":2,\"entries\":[]}"}:new GameObject("fixture")};
        using(var assets=new NativeBattleAssets())
        {
        var load=assets.Load(new System.Collections.Generic.HashSet<int>(),()=>false);while(load.MoveNext()){}
        var composer=new NativeBattleComposer(1);
        using(var renderer=new HdGpuRenderer(composer.Canvas,true,false,true))
        {
            Set(app,"hdBattle",renderer);Set(app,"battle",battle);Set(app,"screen",2);Set(app,"loading",false);
            Set(app,"paused",false);Set(app,"zoom",1.8f);Set(app,"pan",new Vector2(4,8));
            renderer.UnityCachedStaticCommands=false;renderer.UnityStaticColorCache=false;
            string directory=Path.Combine(Path.GetTempPath(),"Rubezh-BenchmarkTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            string previousPath=Application.persistentDataPath;Application.persistentDataPath=directory;
            try
            {
                Call(app,"StartPairedBenchmark");
                Check((bool)Get(app,"benchActive")&&renderer.UnityCachedStaticCommands&&renderer.UnityStaticColorCache,"pair starts new color cache mode");
                string pairId=(string)Get(app,"benchPairId");int originalTick=battle.TickNumber;
                Time.realtimeSinceStartup+=30;
                Call(app,"FinishFixedBenchmark",true);
                Check((bool)Get(app,"benchActive")&&(int)Get(app,"benchPhase")==2&&(bool)Get(app,"paused"),"first export advances phase without unpausing");
                Check(renderer.UnityCachedStaticCommands&&!renderer.UnityStaticColorCache,"second phase is current static commands without color cache");
                Call(app,"AdvanceSimulation",.2f);Call(app,"StepMeasured");
                Check(battle.TickNumber==originalTick,"simulation cannot advance between phases");
                Time.realtimeSinceStartup+=30;Call(app,"FinishFixedBenchmark",true);
                Check(!(bool)Get(app,"benchActive")&&!(bool)Get(app,"paused")&&!renderer.UnityCachedStaticCommands&&!renderer.UnityStaticColorCache,"pair completion restores original modes and pause");
                Check((float)Get(app,"zoom")==1.8f&&(Vector2)Get(app,"pan")==new Vector2(4,8),"pair restores original camera");
                var exports=Directory.GetFiles(directory,"Rubezh-RenderPair-"+pairId+"-phase*.json");
                Check(exports.Length==2,"two separate labelled report files");
                foreach(var file in exports)
                {
                    string json=File.ReadAllText(file);
                    Check(json.Contains(pairId)&&json.Contains(file.Contains("phase1")?"static-color-cache":"static-commands-no-color-cache"),"export labels match renderer phase");
                }
                Call(app,"StartPairedBenchmark");Call(app,"FinishFixedBenchmark",false);
                Check(!(bool)Get(app,"benchActive")&&!renderer.UnityCachedStaticCommands&&!renderer.UnityStaticColorCache,"pair cancellation restores modes");
                Application.persistentDataPath=Path.Combine(directory,"does-not-exist");
                Call(app,"StartPairedBenchmark");Time.realtimeSinceStartup+=30;Call(app,"FinishFixedBenchmark",true);
                Check(!(bool)Get(app,"benchActive")&&(int)Get(app,"benchPhase")==1&&!renderer.UnityCachedStaticCommands&&!renderer.UnityStaticColorCache,"export failure aborts pair and restores modes");
            }
            finally{Application.persistentDataPath=previousPath;Directory.Delete(directory,true);}
        }
        }
        Console.WriteLine("PASS benchmark regression: "+checks+" checks");
        return checks;
    }
}
