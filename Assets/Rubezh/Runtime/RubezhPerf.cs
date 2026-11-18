using System.Diagnostics;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        double diagInterpolationMs,diagCanvasMs,diagVisibilityMs,diagLayoutMs,diagNativeMs,diagForegroundMs;
        bool diagCameraChanged,benchActive,benchPaused,benchExporting;float benchStart,benchZoom;
        UnityEngine.Vector2 benchPan,benchFixedPan;SilentOrders.Core.BattleSimulation benchBattle;
        FixedBenchmarkCapture benchCapture;
        bool benchPaired,benchSavedCommands,benchSavedColor,benchMoving;
        int benchPhase,benchInitialTick,benchInitialRevision;
        string benchPairId,benchMode;
        HdGpuRenderer benchRenderer;
        void StartFixedBenchmark(){BeginFixedBenchmark(false);}
        void StartMovingBenchmark(){BeginFixedBenchmark(false,true);}
        void StartPairedBenchmark(){BeginFixedBenchmark(true);}
        void BeginFixedBenchmark(bool paired,bool moving=false)
        {
            if(battle==null||screen!=2||loading||benchActive)return;
            if(paired&&hdBattle==null){message="Сначала дождись отображения боя.";return;}
            benchBattle=battle;benchPaused=paused;benchZoom=zoom;benchPan=pan;
            benchRenderer=hdBattle;
            benchSavedCommands=hdBattle!=null&&hdBattle.UnityCachedStaticCommands;
            benchSavedColor=hdBattle!=null&&hdBattle.UnityStaticColorCache;
            benchPaired=paired;benchMoving=moving;benchPhase=1;
            benchPairId=paired?System.Guid.NewGuid().ToString("N"):"";
            benchInitialTick=battle.TickNumber;benchInitialRevision=battle.Map.Revision;
            paused=true;zoom=moving?1.8f:1;pan=UnityEngine.Vector2.zero;
            LayoutMap();benchFixedPan=pan;
            benchActive=true;
            BeginBenchmarkPhase();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            CloseNativeDrawer();
#endif
        }
        void ApplyBenchmarkMode()
        {
            if(hdBattle==null||hdBattle!=benchRenderer)return;
            hdBattle.UnityCachedStaticCommands=benchPaired||benchSavedCommands;
            hdBattle.UnityStaticColorCache=benchPaired?benchPhase==1:benchSavedColor;
        }
        void BeginBenchmarkPhase()
        {
            ApplyBenchmarkMode();
            benchMode=hdBattle==null?"unavailable":!hdBattle.UnityCachedStaticCommands?"scene-renderers":hdBattle.UnityStaticColorCache?"static-color-cache":"static-commands-no-color-cache";
            if(benchMoving)benchMode+="/camera-sweep-v1";
            slowOperations.Clear();benchStart=UnityEngine.Time.realtimeSinceStartup;
            benchCapture=new FixedBenchmarkCapture(benchStart,battle.TickNumber,battle.Map.Revision,benchMode,benchPairId,benchPhase,benchMoving);
            renderedBattle=null;
            message=benchMoving?"Тест движения камеры: 30 секунд, бой на паузе. Отчёт сохранится автоматически.":benchPaired?"Сравнение рендеров: этап "+benchPhase+"/2, 30 секунд. Бой остаётся на паузе.":"Тест 30 секунд: камера закреплена, бой на паузе. Отчёт сохранится автоматически.";
        }
        void MaintainFixedBenchmark()
        {
            if(!benchActive)return;
            if(benchBattle!=battle||screen!=2||loading||hdBattle!=benchRenderer){FinishFixedBenchmark(false);return;}
            if((benchPaired||benchMoving)&&(battle.TickNumber!=benchInitialTick||battle.Map.Revision!=benchInitialRevision))
            {FinishFixedBenchmark(false);message="Сравнение отменено: состояние карты изменилось.";return;}
            // Pause can also be changed by dialogues and settings. benchActive is
            // the simulation lock and stays true throughout BOTH paired phases.
            paused=true;
            if(benchMoving)
            {
                float elapsed=UnityEngine.Time.realtimeSinceStartup-benchStart;
                // Same real-time path at every frame rate: gentle pans and continuous zoom.
                float phase=(float)(elapsed*System.Math.PI*2/12.0);
                zoom=1.8f+.15f*(float)System.Math.Sin(phase*.5f);
                pan=new UnityEngine.Vector2(benchFixedPan.x+W*.12f*(float)System.Math.Sin(phase),
                    benchFixedPan.y+H*.1f*(float)System.Math.Sin(phase*.7f));
            }
            else {zoom=1;pan=benchFixedPan;}
            ApplyBenchmarkMode();
        }
        void FinishFixedBenchmark(bool completed)
        {
            if(!benchActive)return;
            bool sameBattle=benchBattle==battle,continuePair=false;
            benchCapture.Finish(UnityEngine.Time.realtimeSinceStartup,
                sameBattle?battle.TickNumber:-1,sameBattle?battle.Map.Revision:-1,completed);
            try
            {
                bool exported=false;
                if(completed&&sameBattle){benchExporting=true;exported=TryExportUnityTestReport();}
                if(exported&&benchPaired&&benchPhase==1&&sameBattle&&screen==2&&!loading&&
                    battle.TickNumber==benchInitialTick&&battle.Map.Revision==benchInitialRevision)
                {
                    // Export is outside phase two's measurement window. No simulation
                    // unlock or camera/pause restoration occurs between phases.
                    benchPhase=2;benchExporting=false;BeginBenchmarkPhase();continuePair=true;
                }
            }
            finally
            {
                benchExporting=false;
                if(!continuePair)
                {
                    benchActive=false;
                    if(hdBattle!=null&&hdBattle==benchRenderer)
                    {hdBattle.UnityCachedStaticCommands=benchSavedCommands;hdBattle.UnityStaticColorCache=benchSavedColor;}
                    if(sameBattle){zoom=benchZoom;pan=benchPan;if(screen==2&&!loading)paused=benchPaused;}
                    renderedBattle=null;
                }
            }
        }
        float simStepMs, graphicsFrameMs;
        float rawStepMs, rawRenderMs, maxStepMs, maxRenderMs;
        readonly System.Collections.Generic.Queue<PerformanceSpike> slowOperations = new System.Collections.Generic.Queue<PerformanceSpike>();
        void RecordOperation(string stage,float ms,int gc) {
            if(ms<20&&!benchActive)return;
            var spike=new PerformanceSpike{stage=stage,ms=ms,time=UnityEngine.Time.realtimeSinceStartup,tick=battle==null?-1:battle.TickNumber,gcCollections=System.GC.CollectionCount(0)-gc};
            if(stage=="render preparation"){spike.mapRevision=battle.Map.Revision;spike.zoom=zoom;spike.panX=pan.x;spike.panY=pan.y;spike.cameraChanged=diagCameraChanged;spike.interpolationMs=diagInterpolationMs;spike.canvasMs=diagCanvasMs;spike.visibilityMs=diagVisibilityMs;spike.layoutMs=diagLayoutMs;spike.nativeMs=diagNativeMs;spike.foregroundMs=diagForegroundMs;if(hdBattle!=null){spike.composeMs=hdBattle.ComposeMs;spike.submitMs=hdBattle.SubmitMs;spike.materialMs=hdBattle.MaterialSyncMs;spike.sceneMs=hdBattle.SceneSubmissionMs;spike.maskMs=hdBattle.MaskRenderMs;spike.cameraMs=hdBattle.CameraRenderMs;spike.sceneNodes=hdBattle.UnitySceneNodes;spike.staticColorRenderMs=hdBattle.UnityStaticColorRenderMs;spike.staticCommandsMs=hdBattle.UnityStaticCommandsMs;spike.staticDraws=hdBattle.UnityExecutedStaticDraws;spike.staticColorCacheHit=hdBattle.UnityStaticColorCacheHit;spike.staticColorDeferred=hdBattle.UnityStaticColorDeferred;spike.objectMaskDraws=hdBattle.UnityObjectMaskDraws;spike.roofMaskDraws=hdBattle.UnityRoofMaskDraws;}}
            if(benchActive&&stage=="render preparation")benchCapture.Record(spike);
            slowOperations.Enqueue(spike);
            while(slowOperations.Count>256)slowOperations.Dequeue();
        }
        int measuredSteps, measuredFrames;
        void StepMeasured()
        {
            if(benchActive)return;
            painter?.ObserveTick(battle);
            BehaviorBeforeStep();
            int gc = System.GC.CollectionCount(0);
            long start = Stopwatch.GetTimestamp();
            if (WatchingInteresting)
                interestingSession.Step();
            else
                battle.Step();
            painter?.ObserveTick(battle);
            importantCommands.Observe(battle);
            float ms = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            rawStepMs=ms;maxStepMs=System.Math.Max(maxStepMs,ms);RecordOperation("simulation",ms,gc);
            simStepMs = measuredSteps++ == 0 ? ms : simStepMs * .94f + ms * .06f;
        }

        SilentOrders.Core.BattleSimulation renderedBattle;
        int renderedRosterRevision = -1, renderedTick = -1, renderedSelection = -1, renderedTier = -99, renderedMapRevision = -1;
        bool renderedDebug;
        float lastZoom, renderedInterpolation = -1;
        UnityEngine.Vector2 lastPan;
        UnityEngine.Rect lastSafe;
        bool NeedsBattleRender()
        {
            painter?.ObserveTick(battle);
            return battle != null && (benchActive || renderedBattle != battle || painter != null && renderedRosterRevision != painter.RosterRevision || painter != null && painter.InterpolationActive && renderedInterpolation != painter.InterpolationAlpha || renderedTick != battle.TickNumber || renderedSelection != selected || renderedTier != graphicsTier || renderedMapRevision != battle.Map.Revision || renderedDebug != debug || (zoom != lastZoom || pan != lastPan || UnityEngine.Screen.safeArea != lastSafe));
        }

        void RenderMeasured()
        {
            int gc = System.GC.CollectionCount(0);
            long start = Stopwatch.GetTimestamp();
            MaintainFixedBenchmark();
            diagInterpolationMs=diagCanvasMs=diagVisibilityMs=diagLayoutMs=diagNativeMs=diagForegroundMs=0;
            RenderOutput();
            // Compare the final, constrained camera that was actually rendered.
            diagCameraChanged=zoom!=lastZoom||pan!=lastPan||UnityEngine.Screen.safeArea!=lastSafe;
            lastZoom = zoom;
            lastPan = pan;
            lastSafe = UnityEngine.Screen.safeArea;
            renderedInterpolation = painter == null ? 1f : painter.InterpolationAlpha;
            renderedRosterRevision = painter == null ? -1 : painter.RosterRevision;
            renderedBattle = battle;
            renderedTick = battle.TickNumber;
            renderedSelection = selected;
            renderedTier = graphicsTier;
            renderedMapRevision = battle.Map.Revision;
            renderedDebug = debug;
            float ms = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            rawRenderMs=ms;maxRenderMs=System.Math.Max(maxRenderMs,ms);RecordOperation("render preparation",ms,gc);
            graphicsFrameMs = measuredFrames++ == 0 ? ms : graphicsFrameMs * .9f + ms * .1f;
            if(benchActive&&UnityEngine.Time.realtimeSinceStartup-benchStart>=30)FinishFixedBenchmark(true);
        }

        float perfSeconds, perfMemorySeconds;
        int perfFrames;
        float perfWorstMs;
        bool perfMemoryReady;
        string performanceText = "Производительность: сбор данных…";
        long perfUsed, perfReserved, perfManaged, perfGraphics;
        SilentOrders.Core.BattleSimulation perfAiBattle;
        float perfAiTime;
        long perfAiPlans, perfAiDecisions;
        void ResetAiSample()
        {
            perfAiBattle = battle;
            perfAiTime = battle == null ? 0 : battle.Time;
            perfAiPlans = battle == null ? 0 : battle.SquadPlanCalls;
            perfAiDecisions = battle == null ? 0 : battle.SoldierDecisionCalls;
        }

        string AiPerformanceSample()
        {
            if (battle == null)
                return "ИИ: нет боя";
            float elapsed = battle.Time - perfAiTime;
            string rates = elapsed > 0 ? "планы " + ((battle.SquadPlanCalls - perfAiPlans) / elapsed).ToString("F1") + " · решения " + ((battle.SoldierDecisionCalls - perfAiDecisions) / elapsed).ToString("F1") + " / игровую с" : "пауза";
            string result = "ИИ: " + rates + " · двойки зелёных, всего: передачи " + battle.Green.CommandPlan.Transfers + " / прерывания " + battle.Green.CommandPlan.Interrupted;
            ResetAiSample();
            return result;
        }

        static string MemoryMiB(long bytes) => bytes > 0 ? (bytes / 1048576d).ToString("F1") + " МиБ" : "н/д";
        void UpdatePerformance()
        {
            if (!debug || screen != 2 || loading)
            {
                perfSeconds = perfMemorySeconds = 0;
                perfFrames = 0;
                perfWorstMs = 0;
                perfMemoryReady = false;
                perfAiBattle = null;
                performanceText = "Производительность: сбор данных…";
                return;
            }

            float dt = UnityEngine.Time.unscaledDeltaTime;
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt))
                return;
            if (perfAiBattle != battle || battle != null && battle.Time < perfAiTime)
                ResetAiSample();
            perfSeconds += dt;
            perfMemorySeconds += dt;
            perfFrames++;
            perfWorstMs = System.Math.Max(perfWorstMs, dt * 1000f);
            if (perfMemorySeconds >= 1f || !perfMemoryReady)
            {
                perfMemorySeconds = 0;
                perfMemoryReady = true;
                perfUsed = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
                perfReserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
                perfManaged = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    perfGraphics=UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
#else
                perfGraphics = 0;
#endif
            }

            if (perfSeconds < .5f)
                return;
            string mode = hdGpuUnavailable ? "Unity: ошибка отображения" : graphicsTier < 0 ? "Unity · Пиксели" : "Unity";
            performanceText = "FPS: " + (perfFrames / perfSeconds).ToString("F1") + " · кадр: " + (perfSeconds * 1000f / perfFrames).ToString("F1") + " мс · макс: " + perfWorstMs.ToString("F1") + " мс" + "\nCPU тик: " + simStepMs.ToString("F2") + " мс · подготовка: " + graphicsFrameMs.ToString("F1") + " мс · " + mode + "\nRAM Unity: " + MemoryMiB(perfUsed) + " · резерв: " + MemoryMiB(perfReserved) + " · C#: " + MemoryMiB(perfManaged) + "\nГрафика ≈ " + MemoryMiB(perfGraphics) + " · спрайты CPU: " + MemoryMiB(Rubezh.Graphics.LoadedPixelBytes) + "\n" + AiPerformanceSample();
            perfSeconds = 0;
            perfFrames = 0;
            perfWorstMs = 0;
        }

        string PerformanceStatus => performanceText;
    }
}
