using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class PerformanceSpike
    {
        public string stage;
        public float ms,
            time;
        public int tick,
            gcCollections,
            mapRevision,
            sceneNodes;
        public float zoom,
            panX,
            panY;
        public bool cameraChanged,
            staticColorCacheHit,
            staticColorDeferred;
        public int staticDraws,
            objectMaskDraws,
            roofMaskDraws;
        public double staticColorRenderMs,
            staticCommandsMs;
        public double interpolationMs,
            canvasMs,
            visibilityMs,
            layoutMs,
            nativeMs,
            foregroundMs,
            composeMs,
            submitMs,
            materialMs,
            sceneMs,
            maskMs,
            cameraMs;
    }

    [Serializable]
    public sealed class PerformanceTexture
    {
        public string name;
        public int width,
            height;
        public long bytes;
    }

    [Serializable]
    public sealed class PerformanceSample
    {
        public int nativeWorldItemRebuilds,
            nativeWorldItemReuseHits,
            nativeWorldItemPruned,
            retainedWorldItems;
        public int trackedActors,
            prunedActors;
        public int nativeActorRebuilds,
            nativeActorReuseHits,
            nativeActorPoseUpdates,
            nativeActorStatusRebuilds,
            nativeActorTransformUpdates,
            nativeInterpolationFrames,
            nativeInterpolatedOrderChanges,
            battleTick;
        public float battleTime;
        public double nativeActorReuseFraction;
        public double updateCpuMs,
            guiCpuMs,
            cpuFrameMs = -1,
            gpuFrameMs = -1;
        public bool smokeDisabled,
            roofsDisabled,
            sceneryDisabled;
        public int smokePuffs;
        public float time,
            frameMs,
            simMs,
            renderMs;
        public int objectChunkBuilds,
            objectChunkHits,
            residentObjectChunks,
            nativeDepthRebuilds,
            pictureEvictions,
            picturePagesUploaded,
            pictureDemandPages,
            picturePrefetchPages;
        public double objectChunksMs,
            pictureFrameUploadMs,
            maxPicturePageMs;
        public long unityUsed,
            unityReserved,
            managed,
            graphics = -1,
            spriteCpu,
            processWorkingSet = -1;
        public int gc0,
            gc1,
            gc2,
            screen,
            quality;
        public bool loading;
    }

    [Serializable]
    public sealed class PerformanceReport
    {
        public int groundPaletteMaterials;
        public long groundPaletteRunsSaved;
        public int roofVerticesBefore,
            roofVerticesAfter,
            sharedRoofMaskChunks,
            groundRunsBefore,
            groundRunsAfter;
        public long staticRunsBefore,
            staticRunsAfter;
        public double groundRunCompactionMs;
        public bool directNativeComposition;
        public string uiTimingScope =
            "Retained UI script updates, excluding Unity Toolkit internal layout/render; guiCpuMs retained for compatibility";
        public int trackedActors,
            prunedActors;
        public int nativeActorRebuilds,
            nativeActorReuseHits,
            nativeBuildingMaskUpdates,
            nativeBuildingPages;
        public double nativeActorReuseFraction;
        public int nativeActorPoseUpdates,
            nativeActorStatusRebuilds,
            nativeActorTransformUpdates,
            nativeInterpolationFrames,
            nativeInterpolatedOrderChanges;
        public string actorCounterScope =
            "Body recipe rebuilds versus reuse; transform/pose/status updates counted separately. Cumulative for composer lifetime; history permits per-interval comparisons. Reuse fraction is not an FPS improvement measurement.";
        public string worldItemCounterScope =
            "Recipe rebuild/reuse for blood, dropped gear and debris. Visible item signatures are still scanned; reuse does not mean zero CPU cost.";
        public long nativeBuildingMaskBytes;
        public int nativeWorldItemRebuilds,
            nativeWorldItemReuseHits,
            nativeWorldItemPruned,
            retainedWorldItems;
        public int directInstanceCount,
            directCompositionBuilds,
            nativeEffectNodes,
            nativeEffectPoolSize,
            nativeEffectMeshBuilds,
            nativeEffectUpdates;
        public int nativeVisualMeshes,
            addressableAtlases;
        public int nativeCompositionHits,
            nativeForegroundFrames,
            nativeSpriteInstances,
            nativeSpriteMeshes,
            nativeVisualUploads,
            nativeVisualHits;
        public int roofSolidCells,
            roofPanels,
            roofPanelRebuilds;
        public long roofCompactPayloadBytes;
        public bool unityMergedMaskCommands,
            unityPackedMasks;
        public int unityMaskMergedMeshes;
        public string unityStaticColorFailure;
        public double unityStaticColorRenderMs;
        public int unityStaticColorCompositeCount,
            unityObjectMaskDraws,
            unityRoofMaskDraws,
            unityMaskGeometryBuilds;
        public bool unityStaticColorDeferred;
        public bool unityStaticColorCache,
            unityStaticColorCacheActive,
            unityStaticColorCacheHit;
        public int unityStaticColorCopyCount,
            unityExecutedStaticDraws;
        public bool unityCachedStaticCommands;
        public int unityStaticCommandBuilds,
            unityStaticVisibleNodes,
            unityStaticCulledNodes,
            unityStaticSubmeshes,
            unityObjectMaskBuilds,
            unityRoofMaskBuilds;
        public double unityStaticCommandsMs;
        public double materialSyncMs,
            sceneSubmissionMs,
            maskRenderMs,
            cameraRenderMs;
        public int staticSubmissionReuse,
            materialCopies;
        public bool unitySceneTest;
        public int unitySceneNodes,
            unityStaticRendererNodes,
            importedUnityPages;
        public int unityExplicitDraws;
        public bool unityCompatibilityDraw;
        public int unityMaskBuilds,
            unityMaskWidth,
            unityMaskHeight;
        public long unityMaskBytes;
        public string testError;
        public long spriteAtlasBytes,
            atlasTransparentBytesSaved;
        public int smokePuffs,
            revealMaskBuilds;
        public double updateCpuMs,
            guiCpuMs,
            cpuFrameMs = -1;
        public bool smokeDisabled,
            roofsDisabled,
            sceneryDisabled;
        public int sceneColorBuilds,
            sceneColorHits,
            sceneGeometryHits,
            renderWidth,
            renderHeight;
        public long sceneColorBytes;
        public double composeMs,
            submitMs;
        public long proceduralGpuBytes;
        public int proceduralGpuImages;
        public string firstHdGpuError;
        public double lastWallCollapseMs,
            lastRoofCollapseMs,
            lastStructureRefreshMs;
        public double lastBlastMs,
            maxBlastMs,
            lastRoofSupportMs,
            maxRoofSupportMs;
        public long roofSupportVisits;
        public int roofSupportCells,
            blastCount;
        public float rawStepMs,
            rawRenderMs,
            maxStepMs,
            maxRenderMs;
        public PerformanceSpike[] slowOperations;
        public long wallQueries,
            wallCandidates;
        public int indexedWalls;
        public long pictureFullUploads,
            pictureRegionUploads,
            pictureUploadedBytes;
        public double pictureUploadMs,
            pictureIndexMs;
        public int groundChunkBuilds,
            groundResidentChunks,
            groundVisibleChunks;
        public int staticMeshBuilds,
            staticResidentObjects,
            nativeDepthWidth,
            nativeDepthHeight;
        public long nativeDepthBytes,
            cpuCanvasBytes;
        public int missingSpriteCount,
            lastMissingSprite;
        public int objectChunkBuilds,
            objectChunkHits,
            residentObjectChunks,
            nativeDepthRebuilds,
            pictureEvictions,
            picturePagesUploaded,
            pictureDemandPages,
            picturePrefetchPages;
        public double objectChunksMs,
            pictureFrameUploadMs,
            maxPicturePageMs;
        public FixedBenchmarkReport benchmark;
        public bool fixedCameraBenchmark;
        public float benchmarkStartRealtime,
            benchmarkElapsed;
        public int schema = 21;
        public int sceneChunks,
            visibleChunks,
            lastDirtyChunks,
            roofDrawPieces;
        public long chunkUpdates;
        public int fullSceneryBuilds,
            partialSceneryBuilds,
            lastSceneryPixels,
            gpuQuads,
            gpuMapUploads;
        public string gameVersion,
            utc,
            device,
            os,
            cpu,
            gpu,
            graphicsApi,
            unityVersion,
            renderMode;
        public string notes =
            "Bytes unless named otherwise; graphics=-1 means unavailable. Frame percentiles cover last 3600 active frames; memory history sampled once/second (last 180 seconds). Sampled peaks since app start; sub-second spikes may be missed. CPU render time is preparation, not GPU time. simMs/renderMs are smoothed; raw/max fields are unsmoothed operations since app start. slowOperations retains last 256 operations >=20ms, or recent operations during fixed-camera benchmark. The independent benchmark object contains whole-test statistics and bounded full operation samples; stage timings belong to that operation. cameraMs is summed CPU wall duration of main and static-cache Camera.Render; staticColorRenderMs is its cache-camera subset. Mask cameras are in maskMs. These are not GPU timestamps. staticDraws counts static scene and roof draw submissions, excluding one cache composite draw; objectMaskDraws/roofMaskDraws count submitted static mask batches for this frame, excluding vehicles; -1 means fallback renderer draw count is unavailable. Texture inventory is collected only at export. No forced GC. Device identifiers are not collected.";
        public int cpuCount,
            systemRamMiB,
            gpuRamMiB,
            width,
            height,
            targetFps,
            quality,
            lowMemoryWarnings,
            frameCount,
            over33ms,
            over50ms,
            over100ms,
            textureCount,
            spriteCount;
        public float uiScale,
            zoom;
        public double gpuFrameMs = -1;
        public double frameP50,
            frameP95,
            frameP99,
            frameMax,
            fps,
            simMs,
            renderMs;
        public long peakUnityUsed,
            peakUnityReserved,
            peakManaged,
            peakGraphics = -1,
            peakSpriteCpu,
            textureRuntimeBytes;
        public long buildingMaterialBytes,
            buildingGeneratedBytes;
        public int buildingMaterials,
            buildingLayers;
        public double buildingGenerationMs;
        public string lastLevelError;
        public string[] recentErrors;
        public double lastLoadMs,
            maxLoadMs;
        public long peakProcessWorkingSet = -1;
        public PerformanceTexture[] largestTextures;
        public long currentBuildingPixelBytes;
        public int currentBuildingCount;
        public PerformanceSample current;
        public PerformanceSample[] history;
    }

    public sealed partial class RubezhApp
    {
        readonly float[] diagnosticFrames = new float[3600];
        int diagnosticIndex,
            diagnosticCount,
            diagnosticLowMemory;
        readonly Queue<PerformanceSample> diagnosticHistory = new Queue<PerformanceSample>();
        float diagnosticNext;
        readonly Queue<string> diagnosticErrors = new Queue<string>();
        bool diagnosticWasLoading;
        float diagnosticLoadStart;
        double diagnosticLastLoad,
            diagnosticMaxLoad;
        long diagnosticPeakProcess = -1;

#if UNITY_5_3_OR_NEWER
        void DiagnosticLog(string text, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            string message = text + "\n" + stack;
            if (message.Length > 4096)
                message = message.Substring(0, 4096);
            lock (diagnosticErrors)
            {
                diagnosticErrors.Enqueue(message);
                while (diagnosticErrors.Count > 32)
                    diagnosticErrors.Dequeue();
            }
        }
#endif

        long diagnosticPeakUsed,
            diagnosticPeakReserved,
            diagnosticPeakManaged,
            diagnosticPeakGraphics = -1,
            diagnosticPeakSprites;

        void DiagnosticLowMemory()
        {
            diagnosticLowMemory++;
            BuildingRecipe.ClearMaterials();
            if (hdBattle != null)
                hdBattle.TrimRenderCache();
        }

        double measuredCpuFrame = -1,
            measuredGpuFrame = -1;
#if UNITY_2020_2_OR_NEWER
        readonly FrameTiming[] nativeFrameTiming = new FrameTiming[1];
#endif

        void CaptureNativeFrameTiming()
        {
#if UNITY_2020_2_OR_NEWER
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, nativeFrameTiming);
            measuredCpuFrame =
                count > 0 && nativeFrameTiming[0].cpuFrameTime > 0 ? nativeFrameTiming[0].cpuFrameTime : -1;
            measuredGpuFrame =
                count > 0 && nativeFrameTiming[0].gpuFrameTime > 0 ? nativeFrameTiming[0].gpuFrameTime : -1;
#endif
        }

        void UpdateDiagnostics()
        {
            CaptureNativeFrameTiming();
            if (loading && !diagnosticWasLoading)
                diagnosticLoadStart = Time.realtimeSinceStartup;
            if (!loading && diagnosticWasLoading)
            {
                diagnosticLastLoad = (Time.realtimeSinceStartup - diagnosticLoadStart) * 1000;
                diagnosticMaxLoad = Math.Max(diagnosticMaxLoad, diagnosticLastLoad);
            }
            diagnosticWasLoading = loading;
            float dt = Time.unscaledDeltaTime;
            if (dt > 0 && !float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                diagnosticFrames[diagnosticIndex++ % diagnosticFrames.Length] = dt * 1000;
                diagnosticCount = Math.Min(diagnosticCount + 1, diagnosticFrames.Length);
            }
            if (Time.realtimeSinceStartup < diagnosticNext)
                return;
            diagnosticNext = Time.realtimeSinceStartup + 1;
            var s = DiagnosticSample();
            diagnosticHistory.Enqueue(s);
            while (diagnosticHistory.Count > 180)
                diagnosticHistory.Dequeue();
            diagnosticPeakProcess = Math.Max(diagnosticPeakProcess, s.processWorkingSet);
            diagnosticPeakUsed = Math.Max(diagnosticPeakUsed, s.unityUsed);
            diagnosticPeakReserved = Math.Max(diagnosticPeakReserved, s.unityReserved);
            diagnosticPeakManaged = Math.Max(diagnosticPeakManaged, s.managed);
            diagnosticPeakGraphics = Math.Max(diagnosticPeakGraphics, s.graphics);
            diagnosticPeakSprites = Math.Max(diagnosticPeakSprites, s.spriteCpu);
        }

        double NativeActorReuseFraction()
        {
            long rebuilds = painter == null ? 0 : painter.ActorRebuilds,
                reuse = painter == null ? 0 : painter.ActorReuseHits;
            return rebuilds + reuse > 0 ? (double)reuse / (rebuilds + reuse) : 0;
        }

        PerformanceSample DiagnosticSample()
        {
            var s = new PerformanceSample
            {
                trackedActors = painter == null ? 0 : painter.TrackedActorCount,
                prunedActors = painter == null ? 0 : painter.ActorPruned,
                nativeInterpolatedOrderChanges = hdBattle == null ? 0 : hdBattle.InterpolatedOrderChanges,
                nativeActorPoseUpdates = painter == null ? 0 : painter.ActorPoseUpdates,
                nativeActorStatusRebuilds = painter == null ? 0 : painter.ActorStatusRebuilds,
                nativeActorTransformUpdates = painter == null ? 0 : painter.ActorTransformUpdates,
                nativeInterpolationFrames = painter == null ? 0 : painter.InterpolationFrames,
                nativeWorldItemRebuilds = painter == null ? 0 : painter.WorldItemRebuilds,
                nativeWorldItemReuseHits = painter == null ? 0 : painter.WorldItemReuseHits,
                nativeWorldItemPruned = painter == null ? 0 : painter.WorldItemPruned,
                retainedWorldItems = painter == null ? 0 : painter.RetainedWorldItems,
                nativeActorRebuilds = painter == null ? 0 : painter.ActorRebuilds,
                nativeActorReuseHits = painter == null ? 0 : painter.ActorReuseHits,
                nativeActorReuseFraction = NativeActorReuseFraction(),
                battleTick = battle == null ? -1 : battle.TickNumber,
                battleTime = battle == null ? 0 : battle.Time,
                updateCpuMs = fullUpdateCpuMs,
                guiCpuMs = guiCpuMs,
                cpuFrameMs = measuredCpuFrame,
                gpuFrameMs = measuredGpuFrame,
                smokeDisabled = diagDisableSmoke,
                roofsDisabled = diagDisableRoofs,
                sceneryDisabled = diagDisableScenery,
                smokePuffs = hdBattle == null ? 0 : hdBattle.SmokePuffs,
                objectChunkBuilds = hdBattle == null ? 0 : hdBattle.ObjectChunkBuilds,
                objectChunkHits = hdBattle == null ? 0 : hdBattle.ObjectChunkHits,
                residentObjectChunks = hdBattle == null ? 0 : hdBattle.ResidentObjectChunks,
                nativeDepthRebuilds = hdBattle == null ? 0 : hdBattle.NativeDepthRebuilds,
                pictureEvictions = hdBattle == null ? 0 : hdBattle.PictureEvictions,
                picturePagesUploaded = hdBattle == null ? 0 : hdBattle.PicturePagesUploaded,
                pictureDemandPages = hdBattle == null ? 0 : hdBattle.PictureDemandPages,
                picturePrefetchPages = hdBattle == null ? 0 : hdBattle.PicturePrefetchPages,
                objectChunksMs = hdBattle == null ? 0 : hdBattle.ObjectChunksMs,
                pictureFrameUploadMs = hdBattle == null ? 0 : hdBattle.PictureFrameUploadMs,
                maxPicturePageMs = hdBattle == null ? 0 : hdBattle.MaxPicturePageMs,
                time = Time.realtimeSinceStartup,
                frameMs = Time.unscaledDeltaTime * 1000,
                simMs = simStepMs,
                renderMs = graphicsFrameMs,
                screen = screen,
                quality = graphicsTier,
                loading = loading,
                unityUsed = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(),
                unityReserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong(),
                managed = GC.GetTotalMemory(false),
                spriteCpu = Rubezh.Graphics.LoadedPixelBytes,
                gc0 = GC.CollectionCount(0),
                gc1 = GC.CollectionCount(1),
                gc2 = GC.CollectionCount(2),
            };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            s.graphics = UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver();
#endif
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                    s.processWorkingSet = process.WorkingSet64;
            }
            catch { }
            return s;
        }

        PerformanceReport CapturePerformance()
        {
            var values = new float[diagnosticCount];
            Array.Copy(diagnosticFrames, values, diagnosticCount);
            Array.Sort(values);
            double sum = 0;
            int a = 0,
                b = 0,
                c = 0;
            foreach (float ms in values)
            {
                sum += ms;
                if (ms > 33.333)
                    a++;
                if (ms > 50)
                    b++;
                if (ms > 100)
                    c++;
            }
            var r = new PerformanceReport
            {
                groundPaletteMaterials = hdBattle == null ? 0 : hdBattle.GroundPaletteMaterials,
                groundPaletteRunsSaved = hdBattle == null ? 0 : hdBattle.GroundPaletteRunsSaved,
                unityStaticColorDeferred = hdBattle != null && hdBattle.UnityStaticColorDeferred,
                unityMergedMaskCommands = hdBattle != null && hdBattle.UnityMergedMaskCommands,
                unityPackedMasks = hdBattle != null && hdBattle.UnityPackedMasks,
                unityMaskMergedMeshes = hdBattle == null ? 0 : hdBattle.UnityMaskMergedMeshes,
                unityStaticColorFailure = hdBattle == null ? null : hdBattle.UnityStaticColorFailure,
                unityStaticColorRenderMs = hdBattle == null ? 0 : hdBattle.UnityStaticColorRenderMs,
                unityStaticColorCompositeCount = hdBattle == null ? 0 : hdBattle.UnityStaticColorCompositeCount,
                unityObjectMaskDraws = hdBattle == null ? 0 : hdBattle.UnityObjectMaskDraws,
                unityRoofMaskDraws = hdBattle == null ? 0 : hdBattle.UnityRoofMaskDraws,
                unityMaskGeometryBuilds = hdBattle == null ? 0 : hdBattle.UnityMaskGeometryBuilds,
                unityStaticColorCache = hdBattle != null && hdBattle.UnityStaticColorCache,
                unityStaticColorCacheActive = hdBattle != null && hdBattle.UnityStaticColorCacheActive,
                unityStaticColorCacheHit = hdBattle != null && hdBattle.UnityStaticColorCacheHit,
                unityStaticColorCopyCount = hdBattle == null ? 0 : hdBattle.UnityStaticColorCopyCount,
                unityExecutedStaticDraws = hdBattle == null ? 0 : hdBattle.UnityExecutedStaticDraws,
                staticRunsBefore = hdBattle == null ? 0 : hdBattle.StaticRunsBefore,
                staticRunsAfter = hdBattle == null ? 0 : hdBattle.StaticRunsAfter,
                groundRunsBefore = hdBattle == null ? 0 : hdBattle.GroundRunsBefore,
                groundRunsAfter = hdBattle == null ? 0 : hdBattle.GroundRunsAfter,
                groundRunCompactionMs = hdBattle == null ? 0 : hdBattle.GroundRunCompactionMs,
                roofVerticesBefore = hdBattle == null ? 0 : hdBattle.RoofVerticesBefore,
                roofVerticesAfter = hdBattle == null ? 0 : hdBattle.RoofVerticesAfter,
                sharedRoofMaskChunks = hdBattle == null ? 0 : hdBattle.SharedRoofMaskChunks,
                unityCachedStaticCommands = hdBattle != null && hdBattle.UnityCachedStaticCommands,
                unityStaticCommandBuilds = hdBattle == null ? 0 : hdBattle.UnityStaticCommandBuilds,
                unityStaticVisibleNodes = hdBattle == null ? 0 : hdBattle.UnityStaticVisibleNodes,
                unityStaticCulledNodes = hdBattle == null ? 0 : hdBattle.UnityStaticCulledNodes,
                unityStaticSubmeshes = hdBattle == null ? 0 : hdBattle.UnityStaticSubmeshes,
                unityStaticCommandsMs = hdBattle == null ? 0 : hdBattle.UnityStaticCommandsMs,
                unityObjectMaskBuilds = hdBattle == null ? 0 : hdBattle.UnityObjectMaskBuilds,
                unityRoofMaskBuilds = hdBattle == null ? 0 : hdBattle.UnityRoofMaskBuilds,
                benchmark = benchBattle == battle && benchCapture != null ? benchCapture.Snapshot() : null,
                fixedCameraBenchmark = benchExporting && !benchMoving,
                benchmarkStartRealtime = benchStart,
                benchmarkElapsed = benchExporting ? UnityEngine.Time.realtimeSinceStartup - benchStart : 0,
                materialSyncMs = hdBattle == null ? 0 : hdBattle.MaterialSyncMs,
                sceneSubmissionMs = hdBattle == null ? 0 : hdBattle.SceneSubmissionMs,
                maskRenderMs = hdBattle == null ? 0 : hdBattle.MaskRenderMs,
                cameraRenderMs = hdBattle == null ? 0 : hdBattle.CameraRenderMs,
                staticSubmissionReuse = hdBattle == null ? 0 : hdBattle.StaticSubmissionReuse,
                materialCopies = hdBattle == null ? 0 : hdBattle.MaterialCopies,
                trackedActors = painter == null ? 0 : painter.TrackedActorCount,
                prunedActors = painter == null ? 0 : painter.ActorPruned,
                nativeInterpolatedOrderChanges = hdBattle == null ? 0 : hdBattle.InterpolatedOrderChanges,
                nativeActorPoseUpdates = painter == null ? 0 : painter.ActorPoseUpdates,
                nativeActorStatusRebuilds = painter == null ? 0 : painter.ActorStatusRebuilds,
                nativeActorTransformUpdates = painter == null ? 0 : painter.ActorTransformUpdates,
                nativeInterpolationFrames = painter == null ? 0 : painter.InterpolationFrames,
                nativeWorldItemRebuilds = painter == null ? 0 : painter.WorldItemRebuilds,
                nativeWorldItemReuseHits = painter == null ? 0 : painter.WorldItemReuseHits,
                nativeWorldItemPruned = painter == null ? 0 : painter.WorldItemPruned,
                retainedWorldItems = painter == null ? 0 : painter.RetainedWorldItems,
                nativeActorReuseFraction = NativeActorReuseFraction(),
                nativeBuildingPages = NativeBuildingAssets.LoadedPageCount,
                nativeBuildingMaskBytes = hdBattle == null ? 0 : hdBattle.NativeBuildingMaskBytes,
                nativeBuildingMaskUpdates = hdBattle == null ? 0 : hdBattle.NativeBuildingMaskUpdates,
                nativeActorRebuilds = painter == null ? 0 : painter.ActorRebuilds,
                nativeActorReuseHits = painter == null ? 0 : painter.ActorReuseHits,
                nativeEffectUpdates = hdBattle == null ? 0 : hdBattle.NativeEffectUpdates,
                directNativeComposition = hdBattle != null && hdBattle.DirectComposition,
                directInstanceCount = hdBattle == null ? 0 : hdBattle.DirectInstanceCount,
                directCompositionBuilds = hdBattle == null ? 0 : hdBattle.DirectCompositionBuilds,
                nativeEffectNodes = hdBattle == null ? 0 : hdBattle.NativeEffectNodes,
                nativeEffectPoolSize = hdBattle == null ? 0 : hdBattle.NativeEffectPoolSize,
                nativeEffectMeshBuilds = hdBattle == null ? 0 : hdBattle.NativeEffectMeshBuilds,
                nativeCompositionHits = hdBattle == null ? 0 : hdBattle.NativeCompositionHits,
                nativeForegroundFrames = hdBattle == null ? 0 : hdBattle.NativeForegroundFrames,
                nativeSpriteInstances = hdBattle == null ? 0 : hdBattle.NativeSpriteInstances,
                nativeSpriteMeshes = hdBattle == null ? 0 : hdBattle.NativeSpriteMeshes,
                nativeVisualUploads = hdBattle == null ? 0 : hdBattle.NativeVisualUploads,
                nativeVisualHits = hdBattle == null ? 0 : hdBattle.NativeVisualHits,
                nativeVisualMeshes = hdBattle == null ? 0 : hdBattle.NativeVisualMeshes,
                addressableAtlases = nativeAssets == null ? 0 : nativeAssets.LoadedAtlases,
                unityMaskBuilds = hdBattle == null ? 0 : hdBattle.UnityMaskBuilds,
                unityMaskWidth = hdBattle == null ? 0 : hdBattle.UnityMaskWidth,
                unityMaskHeight = hdBattle == null ? 0 : hdBattle.UnityMaskHeight,
                unityMaskBytes = hdBattle == null ? 0 : hdBattle.UnityMaskBytes,
                unityExplicitDraws = hdBattle == null ? 0 : hdBattle.UnityExplicitDraws,
                unityCompatibilityDraw = hdBattle != null && hdBattle.UnityCompatibilityDraw,
                unitySceneTest = unityTestBattle,
                unitySceneNodes = hdBattle == null ? 0 : hdBattle.UnitySceneNodes,
                unityStaticRendererNodes = hdBattle == null ? 0 : hdBattle.UnityStaticRendererNodes,
                importedUnityPages = hdBattle == null ? 0 : hdBattle.ImportedUnityPages,
                testError = unityTestError,
                updateCpuMs = fullUpdateCpuMs,
                guiCpuMs = guiCpuMs,
                cpuFrameMs = measuredCpuFrame,
                gpuFrameMs = measuredGpuFrame,
                smokeDisabled = diagDisableSmoke,
                roofsDisabled = diagDisableRoofs,
                sceneryDisabled = diagDisableScenery,
                smokePuffs = hdBattle == null ? 0 : hdBattle.SmokePuffs,
                spriteAtlasBytes = hdBattle == null ? 0 : hdBattle.SpriteAtlasBytes,
                atlasTransparentBytesSaved = hdBattle == null ? 0 : hdBattle.AtlasTransparentBytesSaved,
                revealMaskBuilds = hdBattle == null ? 0 : hdBattle.RevealMaskBuilds,
                sceneColorBuilds = hdBattle == null ? 0 : hdBattle.SceneColorBuilds,
                sceneColorHits = hdBattle == null ? 0 : hdBattle.SceneColorHits,
                sceneGeometryHits = hdBattle == null ? 0 : hdBattle.SceneGeometryHits,
                sceneColorBytes = hdBattle == null ? 0 : hdBattle.SceneColorBytes,
                renderWidth = hdBattle == null ? 0 : hdBattle.RenderWidth,
                renderHeight = hdBattle == null ? 0 : hdBattle.RenderHeight,
                composeMs = hdBattle == null ? 0 : hdBattle.ComposeMs,
                submitMs = hdBattle == null ? 0 : hdBattle.SubmitMs,
                objectChunkBuilds = hdBattle == null ? 0 : hdBattle.ObjectChunkBuilds,
                objectChunkHits = hdBattle == null ? 0 : hdBattle.ObjectChunkHits,
                residentObjectChunks = hdBattle == null ? 0 : hdBattle.ResidentObjectChunks,
                nativeDepthRebuilds = hdBattle == null ? 0 : hdBattle.NativeDepthRebuilds,
                pictureEvictions = hdBattle == null ? 0 : hdBattle.PictureEvictions,
                picturePagesUploaded = hdBattle == null ? 0 : hdBattle.PicturePagesUploaded,
                pictureDemandPages = hdBattle == null ? 0 : hdBattle.PictureDemandPages,
                picturePrefetchPages = hdBattle == null ? 0 : hdBattle.PicturePrefetchPages,
                objectChunksMs = hdBattle == null ? 0 : hdBattle.ObjectChunksMs,
                pictureFrameUploadMs = hdBattle == null ? 0 : hdBattle.PictureFrameUploadMs,
                maxPicturePageMs = hdBattle == null ? 0 : hdBattle.MaxPicturePageMs,
                missingSpriteCount = Rubezh.Graphics.MissingSpriteCount,
                lastMissingSprite = Rubezh.Graphics.LastMissingSprite,
                staticMeshBuilds = hdBattle == null ? 0 : hdBattle.StaticMeshBuilds,
                staticResidentObjects = hdBattle == null ? 0 : hdBattle.StaticResidentObjects,
                nativeDepthWidth = hdBattle == null ? 0 : hdBattle.NativeDepthWidth,
                nativeDepthHeight = hdBattle == null ? 0 : hdBattle.NativeDepthHeight,
                nativeDepthBytes = hdBattle == null ? 0 : hdBattle.NativeDepthBytes,
                cpuCanvasBytes = painter == null ? 0 : painter.Canvas.AllocatedPixelBytes,
                groundChunkBuilds = hdBattle == null ? 0 : hdBattle.GroundChunkBuilds,
                groundResidentChunks = hdBattle == null ? 0 : hdBattle.GroundResidentChunks,
                groundVisibleChunks = hdBattle == null ? 0 : hdBattle.GroundVisibleChunks,
                pictureFullUploads = hdBattle == null ? 0 : hdBattle.PictureFullUploads,
                pictureRegionUploads = hdBattle == null ? 0 : hdBattle.PictureRegionUploads,
                pictureUploadedBytes = hdBattle == null ? 0 : hdBattle.PictureUploadedBytes,
                pictureUploadMs = hdBattle == null ? 0 : hdBattle.PictureUploadMs,
                pictureIndexMs = hdBattle == null ? 0 : hdBattle.PictureIndexMs,
                rawStepMs = rawStepMs,
                rawRenderMs = rawRenderMs,
                maxStepMs = maxStepMs,
                maxRenderMs = maxRenderMs,
                slowOperations = slowOperations.ToArray(),
                firstHdGpuError = firstHdGpuError,
                lastWallCollapseMs = SilentOrders.Core.DynamicStructures.LastWallCollapseMs,
                lastRoofCollapseMs = SilentOrders.Core.DynamicStructures.LastRoofCollapseMs,
                lastStructureRefreshMs = SilentOrders.Core.DynamicStructures.LastRefreshMs,
                lastBlastMs = SilentOrders.Core.DynamicStructures.LastBlastMilliseconds,
                maxBlastMs = SilentOrders.Core.DynamicStructures.MaxBlastMilliseconds,
                blastCount = SilentOrders.Core.DynamicStructures.BlastCount,
                lastRoofSupportMs = SilentOrders.Core.RoofSupport.LastMilliseconds,
                maxRoofSupportMs = SilentOrders.Core.RoofSupport.MaxMilliseconds,
                roofSupportVisits = SilentOrders.Core.RoofSupport.LastVisits,
                roofSupportCells = SilentOrders.Core.RoofSupport.LastCells,
                proceduralGpuBytes = hdBattle == null ? 0 : hdBattle.ProceduralGpuBytes,
                proceduralGpuImages = hdBattle == null ? 0 : hdBattle.ProceduralGpuImages,
                sceneChunks = painter == null ? 0 : painter.ChunkCount,
                visibleChunks = painter == null ? 0 : painter.VisibleChunks,
                lastDirtyChunks = painter == null ? 0 : painter.LastDirtyChunks,
                roofDrawPieces = painter == null ? 0 : painter.RoofDrawPieces,
                chunkUpdates = painter == null ? 0 : painter.ChunkUpdates,
                fullSceneryBuilds = painter == null ? 0 : painter.FullSceneryBuilds,
                partialSceneryBuilds = painter == null ? 0 : painter.PartialSceneryBuilds,
                lastSceneryPixels = painter == null ? 0 : painter.LastSceneryPixels,
                gpuQuads = hdBattle == null ? 0 : hdBattle.QuadCount,
                gpuMapUploads = hdBattle == null ? 0 : hdBattle.MapUploads,
                gameVersion = RubezhVersion.Current,
                utc = DateTime.UtcNow.ToString("O"),
                current = DiagnosticSample(),
                history = diagnosticHistory.ToArray(),
                frameCount = values.Length,
                frameP50 = Percentile(values, .5),
                frameP95 = Percentile(values, .95),
                frameP99 = Percentile(values, .99),
                frameMax = values.Length == 0 ? 0 : values[values.Length - 1],
                fps = sum > 0 ? values.Length * 1000 / sum : 0,
                over33ms = a,
                over50ms = b,
                over100ms = c,
                simMs = simStepMs,
                renderMs = graphicsFrameMs,
                peakUnityUsed = diagnosticPeakUsed,
                peakUnityReserved = diagnosticPeakReserved,
                peakManaged = diagnosticPeakManaged,
                peakGraphics = diagnosticPeakGraphics,
                peakSpriteCpu = diagnosticPeakSprites,
                lowMemoryWarnings = diagnosticLowMemory,
                quality = graphicsTier,
                uiScale = PlayerPrefs.GetInt("rubezh.uiScale", 100),
                zoom = zoom,
                renderMode =
                    unityTestBattle
                        ? (
                            hdBattle != null && hdBattle.UnityCompatibilityDraw
                                ? "Unity imported atlases / explicit CommandBuffer"
                                : "Unity scene / camera masks"
                        )
                    : hdGpuUnavailable ? "Unity rendering unavailable"
                    : "Unity retained scene",
                buildingMaterialBytes = BuildingRecipe.MaterialBytes,
                buildingGeneratedBytes = BuildingRecipe.GeneratedBytes,
                buildingMaterials = BuildingRecipe.MaterialCount,
                buildingLayers = BuildingRecipe.GeneratedLayers,
                buildingGenerationMs = BuildingRecipe.GenerationMilliseconds,
            };
#if UNITY_5_3_OR_NEWER
            r.device = SystemInfo.deviceModel;
            r.os = SystemInfo.operatingSystem;
            r.cpu = SystemInfo.processorType;
            r.cpuCount = SystemInfo.processorCount;
            r.systemRamMiB = SystemInfo.systemMemorySize;
            r.gpu = SystemInfo.graphicsDeviceName;
            r.gpuRamMiB = SystemInfo.graphicsMemorySize;
            r.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            r.unityVersion = Application.unityVersion;
            r.width = Screen.width;
            r.height = Screen.height;
            r.targetFps = Application.targetFrameRate;
            var textures = Resources.FindObjectsOfTypeAll<Texture>();
            r.textureCount = textures.Length;
            var sizes = new List<PerformanceTexture>();
            foreach (var t in textures)
            {
                long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);
                r.textureRuntimeBytes += bytes;
                sizes.Add(
                    new PerformanceTexture
                    {
                        name = t.name,
                        width = t.width,
                        height = t.height,
                        bytes = bytes,
                    }
                );
            }
            sizes.Sort((lhs, rhs) => rhs.bytes.CompareTo(lhs.bytes));
            if (sizes.Count > 32)
                sizes.RemoveRange(32, sizes.Count - 32);
            r.largestTextures = sizes.ToArray();
            r.spriteCount = Resources.FindObjectsOfTypeAll<Sprite>().Length;
#endif
            if (battle != null && battle.Map.DynamicBuildings != null)
            {
                r.currentBuildingCount = battle.Map.DynamicBuildings.Count;
                foreach (var building in battle.Map.DynamicBuildings)
                foreach (var picture in building.Pictures)
                    if (picture != null)
                        r.currentBuildingPixelBytes +=
                            (picture.Pixels == null ? 0 : picture.Pixels.LongLength * 4)
                            + (picture.Depth == null ? 0 : picture.Depth.LongLength * 4);
            }
            if (battle != null && battle.Map.DynamicBuildings != null)
                foreach (var house in battle.Map.DynamicBuildings)
                {
                    r.roofSolidCells += house.Roof.Count;
                    r.roofPanels += house.RoofShape.CachedPanelCount;
                    r.roofPanelRebuilds += house.RoofShape.RebuildCount;
                    r.roofCompactPayloadBytes += house.RoofShape.StorageBytes;
                }
            r.lastLevelError = RubezhLevelRuntime.LastError;
            r.lastLoadMs = diagnosticLastLoad;
            r.maxLoadMs = diagnosticMaxLoad;
            r.peakProcessWorkingSet = diagnosticPeakProcess;
            lock (diagnosticErrors)
                r.recentErrors = diagnosticErrors.ToArray();
            if (battle != null && battle.Map.DynamicBuildings != null)
            {
                var wi = SilentOrders.Core.DynamicWallIndex.For(battle.Map);
                r.wallQueries = wi.Queries;
                r.wallCandidates = wi.Candidates;
                r.indexedWalls = wi.WallCount;
            }
            return r;
        }

        static double Percentile(float[] v, double p) =>
            v.Length == 0 ? 0 : v[Math.Min(v.Length - 1, (int)Math.Ceiling(v.Length * p) - 1)];
    }
}
