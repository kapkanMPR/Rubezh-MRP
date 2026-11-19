using System;
using SilentOrders.Core;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#endif
namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public double MaterialSyncMs,
            SceneSubmissionMs,
            MaskRenderMs,
            CameraRenderMs;
        public int UnitySceneNodes { get; private set; }
        public int UnityStaticRendererNodes { get; private set; }
        public bool UnityCompatibilityDraw
        {
            get => false;
            set
            {
                if (value)
                    throw new InvalidOperationException("Legacy command rendering has been removed.");
            }
        }
        public int StaticSubmissionReuse { get; private set; }
        public int MaterialCopies { get; private set; }
        public int UnityExplicitDraws { get; private set; }

        // Keep a renderer path for device comparisons; both paths use identical meshes/materials.
        public bool UnityCachedStaticCommands = true;
        public int UnityStaticCommandBuilds { get; private set; }
        public int UnityStaticVisibleNodes { get; private set; }
        public int UnityStaticCulledNodes { get; private set; }
        public int UnityStaticSubmeshes { get; private set; }
        public double UnityStaticCommandsMs { get; private set; }
        RenderTexture unityMask,
            unityRoofMask;
        public int UnityMaskBuilds { get; private set; }
        public int UnityMaskWidth => unityMask == null ? 0 : unityMask.width;
        public int UnityMaskHeight => unityMask == null ? 0 : unityMask.height;
        public long UnityMaskBytes => (long)UnityMaskWidth * UnityMaskHeight * 8;
        public bool UnityOwnedScene => UnitySceneMode && !UnityCompatibilityDraw;

        void ReleaseUnityMask()
        {
            UnityPackedMasks = false;
            if (unityRoofMask != null)
            {
                unityRoofMask.Release();
                UnityEngine.Object.Destroy(unityRoofMask);
                unityRoofMask = null;
            }
            if (unityMask != null)
            {
                unityMask.Release();
                UnityEngine.Object.Destroy(unityMask);
                unityMask = null;
            }
        }

#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        // Layer IDs are shared by scene submissions, mask passes and camera culling.
        const int ObjectLayer = 23;
        const int TerrainLayer = 24;
        const int EffectLayer = 25;
        const int RoofMaskLayer = 26;
        const int RoofLayer = 27;
        const int ActorLayer = 28;
        const int GroundLayer = 29;
        const int SceneryLayer = 30;
        const int DynamicSceneLayers =
            (1 << SceneryLayer) | (1 << GroundLayer) | (1 << ActorLayer) | (1 << EffectLayer);
        const int StaticSceneLayers = (1 << RoofLayer) | (1 << TerrainLayer) | (1 << ObjectLayer);

        sealed class SceneNode
        {
            public GameObject Object;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public int[] Pages;
            public Mesh Source;
            public string Label;
            public int LastFrame;
            public int Layer = -1,
                Order = -1;
            public bool Active,
                Initialized,
                Instanced;
            public Transform Parent;
            public Vector3 Origin,
                Scale;
            public float Angle;
            public Vector4 InstanceData,
                Tint;
            public MaterialPropertyBlock Properties;
        }

        readonly List<SceneNode> unityNodes = new List<SceneNode>();
        readonly Dictionary<long, SceneNode> unityActorNodes = new Dictionary<long, SceneNode>();
        readonly List<long> expiredActorNodes = new List<long>();
        int unitySubmissionOrder;
        float submittedActorInterpolation;
        readonly List<Material> unityMaterialSources = new List<Material>();
        BattleMap unityMaterialMap;
        int unityMaterialWidth = -1,
            unityMaterialHeight = -1;

        [Flags]
        enum StaticRendererTargets
        {
            None = 0,
            Color = 1,
            Masks = 2,
        }

        StaticRendererTargets submittedStaticTargets;
        int staticPrefixSerial = -1,
            staticPrefixCount,
            staticPrefixOrder;
        int staticSuffixSerial = -1,
            staticSuffixStart = -1,
            staticSuffixStartOrder = -1,
            staticSuffixCount,
            staticSuffixOrder,
            staticSuffixRoof = -1;
        bool staticSuffixDrawRoof,
            staticPrefixDisabled,
            staticSuffixDisabled,
            staticSuffixRoofsDisabled;

        void TouchStaticNodes(int start, int count)
        {
            for (int i = start; i < start + count; i++)
                unityNodes[i].LastFrame = pictureFrame;
        }

        CommandBuffer unityStaticCommands,
            unityRoofCommands,
            unityBeforeAlphaCommands,
            unityAfterPrefixCommands;
        int unityStaticMaterialRevision,
            unityRoofCommandDraws;
        bool unityCommandsAttached;
        int unityCommandsGeometry = -1,
            unityCommandsRoof = -1,
            unityCommandsWidth = -1,
            unityCommandsHeight = -1;
        Rect unityCommandsView,
            unityCommandsCoverage;
        bool unityCommandsSceneryDisabled,
            unityCommandsRoofsDisabled,
            unityCommandsDrawRoof;

        void AttachStaticCommands(bool attach)
        {
            if (unityCommandsAttached == attach)
                return;
            if (attach)
            {
                unityCamera.AddCommandBuffer(
                    CameraEvent.BeforeForwardAlpha,
                    unityBeforeAlphaCommands ?? unityStaticCommands
                );
                if (unityAfterPrefixCommands != null)
                    unityCamera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, unityAfterPrefixCommands);
                unityCamera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, unityRoofCommands);
            }
            else
            {
                unityCamera.RemoveCommandBuffer(
                    CameraEvent.BeforeForwardAlpha,
                    unityBeforeAlphaCommands ?? unityStaticCommands
                );
                if (unityAfterPrefixCommands != null)
                    unityCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, unityAfterPrefixCommands);
                unityCamera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, unityRoofCommands);
            }
            unityCommandsAttached = attach;
        }

        void SelectStaticPrefix(CommandBuffer prefix, CommandBuffer capture)
        {
            if (unityBeforeAlphaCommands == prefix && unityAfterPrefixCommands == capture)
                return;
            bool attached = unityCommandsAttached;
            if (attached)
                AttachStaticCommands(false);
            unityBeforeAlphaCommands = prefix;
            unityAfterPrefixCommands = capture;
            if (attached)
                AttachStaticCommands(true);
        }

        void ReleaseStaticCommands()
        {
            if (unityCamera != null)
                AttachStaticCommands(false);
            ReleaseStaticColorCache();
            unityBeforeAlphaCommands = unityAfterPrefixCommands = null;
            if (unityStaticCommands != null)
            {
                unityStaticCommands.Release();
                unityStaticCommands = null;
            }
            if (unityRoofCommands != null)
            {
                unityRoofCommands.Release();
                unityRoofCommands = null;
            }
            unityCommandsGeometry = unityCommandsRoof = -1;
        }

        void AppendStaticCommands(CommandBuffer commands, Mesh mesh, int[] pages, Rect pixels) =>
            AppendBoundedStaticCommands(commands, mesh, pages, pixels, null);

        void AppendBoundedStaticCommands(CommandBuffer commands, Mesh mesh, int[] pages, Rect pixels, Rect[] submeshes)
        {
            if (mesh == null || pages == null || pages.Length == 0)
                return;
            UnityStaticSubmeshes += pages.Length;
            // Static meshes are baked in canvas space and have identity transforms.
            var bounds = mesh.bounds;
            if (
                bounds.max.x < pixels.xMin
                || bounds.min.x > pixels.xMax
                || bounds.max.y < pixels.yMin
                || bounds.min.y > pixels.yMax
            )
            {
                UnityStaticCulledNodes++;
                return;
            }
            int drawsBefore = UnityExplicitDraws;
            for (int i = 0; i < pages.Length; i++)
            {
                if (submeshes != null)
                {
                    var area = submeshes[i];
                    if (
                        area.xMax < pixels.xMin
                        || area.xMin > pixels.xMax
                        || area.yMax < pixels.yMin
                        || area.yMin > pixels.yMax
                    )
                        continue;
                }
                commands.DrawMesh(mesh, Matrix4x4.identity, unityMaterials[pages[i]], i, 0);
                UnityExplicitDraws++;
            }
            if (UnityExplicitDraws == drawsBefore)
                UnityStaticCulledNodes++;
            else
                UnityStaticVisibleNodes++;
        }

        void UpdateStaticCommands(Rect uv, int width, int height)
        {
            UnityEngine.Profiling.Profiler.BeginSample("Rubezh.StaticCommands");
            long start = Stopwatch.GetTimestamp();
            try
            {
                UpdateStaticCommandsCore(uv, width, height);
            }
            finally
            {
                UnityStaticCommandsMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                UnityEngine.Profiling.Profiler.EndSample();
            }
        }

        // Commands contain world-space meshes. Unity projects and clips them using the current
        // camera every frame, so the same buffer is valid throughout this region, even for
        // fractional moves. Keep its extra area bounded both in output pixels and at close zoom.
        static Rect ExpandCommandCoverage(Rect pixels, int width, int height)
        {
            float padX = pixels.width * Math.Min(1f / 16, 64f / Math.Max(1, width));
            float padY = pixels.height * Math.Min(1f / 16, 64f / Math.Max(1, height));
            return new Rect(pixels.xMin - padX, pixels.yMin - padY, pixels.width + 2 * padX, pixels.height + 2 * padY);
        }

        static bool CoversCommandView(Rect coverage, Rect pixels) =>
            coverage.xMin <= pixels.xMin
            && coverage.yMin <= pixels.yMin
            && coverage.xMax >= pixels.xMax
            && coverage.yMax >= pixels.yMax
            && coverage.width <= pixels.width * 1.25f
            && coverage.height <= pixels.height * 1.25f;

        void UpdateStaticCommandsCore(Rect uv, int width, int height)
        {
            if (!UnityCachedStaticCommands)
            {
                AttachStaticCommands(false);
                UnityExplicitDraws = 0;
                return;
            }
            if (unityStaticCommands == null)
            {
                unityStaticCommands = new CommandBuffer { name = "Rubezh retained static colour" };
                unityRoofCommands = new CommandBuffer { name = "Rubezh retained roof colour" };
            }
            unityCommandsView = uv;
            float edgeX = Math.Abs(uv.width) * canvas.Width / Math.Max(1, width) * 2;
            float edgeY = Math.Abs(uv.height) * canvas.Height / Math.Max(1, height) * 2;
            var viewPixels = new Rect(
                uv.xMin * canvas.Width - edgeX,
                uv.yMin * canvas.Height - edgeY,
                uv.width * canvas.Width + 2 * edgeX,
                uv.height * canvas.Height + 2 * edgeY
            );
            bool changed =
                unityCommandsGeometry != geometrySerial
                || unityCommandsRoof != roofGeometryRevision
                || !CoversCommandView(unityCommandsCoverage, viewPixels)
                || unityCommandsWidth != width
                || unityCommandsHeight != height
                || unityCommandsSceneryDisabled != DisableScenery
                || unityCommandsRoofsDisabled != DisableRoofs
                || unityCommandsDrawRoof != drawNativeRoof;
            // Rebuild on a transition from the renderer fallback as its counters were cleared.
            if (changed || !unityCommandsAttached)
            {
                unityStaticCommands.Clear();
                unityRoofCommands.Clear();
                UnityExplicitDraws = UnityStaticVisibleNodes = UnityStaticCulledNodes = UnityStaticSubmeshes = 0;
                var pixels = ExpandCommandCoverage(viewPixels, width, height);
                if (!DisableScenery)
                {
                    foreach (var batch in groundDraws)
                        AppendStaticCommands(unityStaticCommands, batch.Mesh, batch.Pages, pixels);
                    foreach (var batch in sceneExteriorChunks)
                        AppendStaticCommands(unityStaticCommands, batch.Mesh, batch.Pages, pixels);
                    AppendBoundedStaticCommands(
                        unityStaticCommands,
                        pictureBatch.Mesh,
                        pictureBatch.Pages,
                        pixels,
                        pictureBatch.SubmeshBounds
                    );
                    foreach (var batch in sceneObjectChunks)
                        AppendStaticCommands(unityStaticCommands, batch.Mesh, batch.Pages, pixels);
                }
                int beforeRoof = UnityExplicitDraws;
                if (drawNativeRoof && !DisableRoofs)
                    AppendStaticCommands(unityRoofCommands, roofBatch.Mesh, roofBatch.Pages, pixels);
                unityRoofCommandDraws = UnityExplicitDraws - beforeRoof;
                unityCommandsGeometry = geometrySerial;
                unityCommandsRoof = roofGeometryRevision;
                unityCommandsCoverage = pixels;
                unityCommandsWidth = width;
                unityCommandsHeight = height;
                unityCommandsSceneryDisabled = DisableScenery;
                unityCommandsRoofsDisabled = DisableRoofs;
                unityCommandsDrawRoof = drawNativeRoof;
                UnityStaticCommandBuilds++;
            }
            AttachStaticCommands(true);
        }

        readonly List<Material> unityMaterials = new List<Material>();
        Material unityTemplate;
        Scene unityScene;
        GameObject unityRoot;
        Camera unityCamera,
            unityMaskCamera;
        Shader unityShader,
            unityMaskShader;
        int usedUnityNodes;
        int unityMaterialFrame = -1,
            unitySubmittedFrame = -1,
            unityEffectFrame = -1;

        BattleMap unityMaskMap;
        int unityMaskRevision = -1;
        Rect unityMaskView;
        bool unityMaskSceneryDisabled;

        void EnsureUnityCamera()
        {
            if (unityRoot != null)
                return;
            unityTemplate = Resources.Load<Material>("RubezhNativeSceneMaterial");
            unityShader = unityTemplate == null ? null : unityTemplate.shader;
            if (unityShader == null || !unityShader.isSupported)
                throw new InvalidOperationException("Native scene instancing material unavailable");
            unityMaskShader = Resources.Load<Shader>("RubezhUnityForestMask");
            if (unityMaskShader == null || !unityMaskShader.isSupported)
                throw new InvalidOperationException("Unity forest mask shader unavailable");
            unityScene = SceneManager.CreateScene("Rubezh-TestForest-" + GetHashCode());
            unityRoot = new GameObject("Unity Forest · rendering");
            SceneManager.MoveGameObjectToScene(unityRoot, unityScene);
            var go = new GameObject("Battle camera");
            go.transform.SetParent(unityRoot.transform, false);
            unityCamera = go.AddComponent<Camera>();
            unityCamera.enabled = false;
            unityCamera.orthographic = true;
            unityCamera.clearFlags = CameraClearFlags.SolidColor;
            unityCamera.backgroundColor = new Color(58 / 255f, 68 / 255f, 45 / 255f, 1);
            unityCamera.allowHDR = false;
            unityCamera.allowMSAA = false;
            unityCamera.useOcclusionCulling = false;
            unityCamera.cullingMask = DynamicSceneLayers | StaticSceneLayers;
            unityCamera.nearClipPlane = .1f;
            unityCamera.farClipPlane = 100f;
            var maskGo = new GameObject("Forest occlusion camera");
            maskGo.transform.SetParent(unityRoot.transform, false);
            unityMaskCamera = maskGo.AddComponent<Camera>();
            unityMaskCamera.enabled = false;
            unityMaskCamera.orthographic = true;
            unityMaskCamera.clearFlags = CameraClearFlags.SolidColor;
            unityMaskCamera.backgroundColor = Color.clear;
            unityMaskCamera.allowHDR = false;
            unityMaskCamera.allowMSAA = false;
            unityMaskCamera.useOcclusionCulling = false;
            unityMaskCamera.cullingMask = (1 << SceneryLayer) | (1 << ObjectLayer);
            unityMaskCamera.nearClipPlane = .1f;
            unityMaskCamera.farClipPlane = 100f;
        }

        void SubmitStaticPrefix(StaticRendererTargets targets)
        {
            if (staticPrefixSerial == geometrySerial && staticPrefixDisabled == DisableScenery)
            {
                TouchStaticNodes(0, staticPrefixCount);
                usedUnityNodes = staticPrefixCount;
                unitySubmissionOrder = staticPrefixOrder;
                StaticSubmissionReuse++;
                return;
            }
            if (!DisableScenery && targets != StaticRendererTargets.None)
            {
                if ((targets & StaticRendererTargets.Color) != 0)
                {
                    foreach (var ground in groundDraws)
                        AddUnityMesh(ground.Mesh, ground.Pages, "Ground chunk", TerrainLayer);
                    foreach (var exterior in sceneExteriorChunks)
                        AddUnityMesh(exterior.Mesh, exterior.Pages, "Exterior chunk", TerrainLayer);
                }
                AddUnityMesh(pictureBatch.Mesh, pictureBatch.Pages, "Dynamic surfaces", ObjectLayer);
                foreach (var scenery in sceneObjectChunks)
                    AddUnityMesh(scenery.Mesh, scenery.Pages, "Object chunk", ObjectLayer);
            }
            staticPrefixSerial = geometrySerial;
            staticPrefixDisabled = DisableScenery;
            staticPrefixCount = usedUnityNodes;
            staticPrefixOrder = unitySubmissionOrder;
        }

        void SubmitStaticSuffix(StaticRendererTargets targets)
        {
            if (
                staticSuffixSerial == geometrySerial
                && staticSuffixStart == usedUnityNodes
                && staticSuffixStartOrder == unitySubmissionOrder
                && staticSuffixDrawRoof == drawNativeRoof
                && staticSuffixRoof == roofGeometryRevision
                && staticSuffixDisabled == DisableScenery
                && staticSuffixRoofsDisabled == DisableRoofs
            )
            {
                TouchStaticNodes(usedUnityNodes, staticSuffixCount);
                usedUnityNodes += staticSuffixCount;
                unitySubmissionOrder += staticSuffixOrder;
                StaticSubmissionReuse++;
                return;
            }
            staticSuffixStart = usedUnityNodes;
            staticSuffixStartOrder = unitySubmissionOrder;
            if ((targets & StaticRendererTargets.Color) != 0 && drawNativeRoof && !DisableRoofs)
                AddUnityMesh(roofBatch.Mesh, roofBatch.Pages, "Roofs", RoofLayer);
            if ((targets & StaticRendererTargets.Masks) != 0 && !DisableScenery)
            {
                AddUnityMesh(pictureBatch.Mesh, pictureBatch.Pages, "Roof mask surfaces", RoofMaskLayer);
                foreach (var occluder in sceneRoofChunks)
                    AddUnityMesh(occluder.Mesh, occluder.Pages, "Roof mask occluder", RoofMaskLayer);
            }
            staticSuffixSerial = geometrySerial;
            staticSuffixRoof = roofGeometryRevision;
            staticSuffixDrawRoof = drawNativeRoof;
            staticSuffixDisabled = DisableScenery;
            staticSuffixRoofsDisabled = DisableRoofs;
            staticSuffixCount = usedUnityNodes - staticSuffixStart;
            staticSuffixOrder = unitySubmissionOrder - staticSuffixStartOrder;
        }

        void AddUnityMesh(
            Mesh source,
            int[] pages,
            string label,
            int layer = SceneryLayer,
            Vector3 origin = default(Vector3),
            Vector3? scale = null,
            float angle = 0,
            Vector4? instance = null,
            Vector4? tint = null,
            long? persistentKey = null,
            Transform parent = null
        )
        {
            if (source == null || pages == null || pages.Length == 0)
                return;
            SceneNode node = null;
            if (persistentKey.HasValue)
                unityActorNodes.TryGetValue(persistentKey.Value, out node);
            else if (usedUnityNodes < unityNodes.Count)
                node = unityNodes[usedUnityNodes];
            if (node == null)
            {
                GameObject go;
#if UNITY_5_3_OR_NEWER
                if (NativeBattleAssets.Active?.VisualPrefab != null)
                    go = UnityEngine.Object.Instantiate(NativeBattleAssets.Active.VisualPrefab);
                else
                    go = new GameObject(label);
                go.name = label;
                go.layer = SceneryLayer;
                go.transform.SetParent(unityRoot.transform, false);
                node = new SceneNode
                {
                    Object = go,
                    Filter = go.GetComponent<MeshFilter>() ?? go.AddComponent<MeshFilter>(),
                    Renderer = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>(),
                };
#else
    go=new GameObject(label);go.layer=SceneryLayer;go.transform.SetParent(unityRoot.transform,false);
    node=new SceneNode{Object=go,Filter=go.AddComponent<MeshFilter>(),Renderer=go.AddComponent<MeshRenderer>()};
#endif
                node.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                node.Renderer.receiveShadows = false;
                node.Renderer.lightProbeUsage = LightProbeUsage.Off;
                node.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                if (persistentKey.HasValue)
                    unityActorNodes.Add(persistentKey.Value, node);
                else
                    unityNodes.Add(node);
            }
            var size = scale ?? new Vector3(1, 1, 1);
            if (node.Label != label)
            {
                node.Object.name = label;
                node.Label = label;
            }
            if (node.Parent != parent)
            {
                node.Object.transform.SetParent(parent ?? unityRoot.transform, false);
                node.Parent = parent;
                node.Initialized = false;
                node.Instanced = false;
            }
            if (!node.Initialized || !Same(node.Origin, origin))
            {
                node.Object.transform.localPosition = origin;
                node.Origin = origin;
            }
            if (!node.Initialized || !Same(node.Scale, size))
            {
                node.Object.transform.localScale = size;
                node.Scale = size;
            }
            if (!node.Initialized || node.Angle != angle)
            {
                node.Object.transform.localRotation = Quaternion.Euler(0, 0, angle);
                node.Angle = angle;
            }
            if (node.Layer != layer)
            {
                node.Object.layer = layer;
                node.Layer = layer;
            }
            if (!node.Active)
            {
                node.Object.SetActive(true);
                node.Active = true;
            }
            if (node.Source != source)
            {
                node.Filter.sharedMesh = source;
                node.Source = source;
            }
            int order = layer == RoofLayer ? 30000 : unitySubmissionOrder * 2;
            unitySubmissionOrder++;
            if (node.Order != order)
            {
                node.Renderer.sortingOrder = order;
                node.Order = order;
            }
            if (instance.HasValue)
            {
                var data = instance.Value;
                var color = tint ?? new Vector4(1, 1, 1, 1);
                if (
                    !node.Instanced
                    || node.InstanceData.x != data.x
                    || node.InstanceData.y != data.y
                    || node.InstanceData.z != data.z
                    || node.InstanceData.w != data.w
                    || node.Tint.x != color.x
                    || node.Tint.y != color.y
                    || node.Tint.z != color.z
                    || node.Tint.w != color.w
                )
                {
                    if (node.Properties == null)
                        node.Properties = new MaterialPropertyBlock();
                    node.Properties.SetFloat("_NativeInstance", 1);
                    node.Properties.SetFloat("_ActorRootDepth", parent != null ? 1 : 0);
                    node.Properties.SetVector("_InstanceData", data);
                    node.Properties.SetVector("_InstanceTint", color);
                    node.Renderer.SetPropertyBlock(node.Properties);
                    node.InstanceData = data;
                    node.Tint = color;
                    node.Instanced = true;
                }
            }
            else if (node.Instanced)
            {
                node.Renderer.SetPropertyBlock(null);
                node.Instanced = false;
            }
            node.Initialized = true;
            node.LastFrame = pictureFrame;
            bool same = node.Pages != null && node.Pages.Length == pages.Length;
            for (int i = 0; same && i < pages.Length; i++)
                same = node.Pages[i] == pages[i];
            if (!same)
            {
                var mats = new Material[pages.Length];
                for (int i = 0; i < pages.Length; i++)
                    mats[i] = unityMaterials[pages[i]];
                node.Renderer.sharedMaterials = mats;
                node.Pages = (int[])pages.Clone();
            }
            if (!persistentKey.HasValue)
                usedUnityNodes++;
        }
#endif

#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        void SyncUnityMaterials(BattleSimulation battle, bool maskAllocated)
        {
            bool commonChanged =
                maskAllocated
                || unityMaterialMap != battle.Map
                || unityMaterialWidth != canvas.Width
                || unityMaterialHeight != canvas.Height;
            if (!commonChanged && unityMaterialFrame == pictureFrame && unityMaterials.Count == materials.Count)
                return;

            while (unityMaterials.Count < materials.Count)
            {
                unityMaterials.Add(new Material(unityTemplate) { enableInstancing = true });
                unityMaterialSources.Add(null);
            }
            for (int i = 0; i < materials.Count; i++)
                if (materials[i] != null)
                {
                    bool copy =
                        unityMaterialSources[i] != materials[i]
                        || (i == roofPage && unityMaterialFrame != pictureFrame);
                    if (copy)
                    {
                        unityMaterials[i].CopyPropertiesFromMaterial(materials[i]);
                        unityMaterials[i].shader = unityShader;
                        unityMaterials[i].enableInstancing = true;
                        unityMaterialSources[i] = materials[i];
                        MaterialCopies++;
                        if (i != roofPage)
                            unityStaticMaterialRevision++;
                    }
                    if (copy || commonChanged)
                    {
                        unityMaterials[i].SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
                        unityMaterials[i]
                            .SetVector(
                                "_MapInfo",
                                new Vector4(
                                    battle.Map.Columns,
                                    battle.Map.Rows,
                                    SilentOrders.Rendering.BattleVisualRecipes.LogicalWidth(battle.Map),
                                    SilentOrders.Rendering.BattleVisualRecipes.LogicalHeight(battle.Map)
                                )
                            );
                        unityMaterials[i].SetTexture("_Occlusion", unityMask);
                        unityMaterials[i].SetTexture("_RoofOcclusion", UnityPackedMasks ? unityMask : unityRoofMask);
                        unityMaterials[i].SetFloat("_PackedRoofMask", UnityPackedMasks ? 1 : 0);
                    }
                }
            if (commonChanged)
                unityStaticMaterialRevision++;
            unityMaterialMap = battle.Map;
            unityMaterialWidth = canvas.Width;
            unityMaterialHeight = canvas.Height;
            unityMaterialFrame = pictureFrame;
        }
#endif

        void RenderUnityCamera(BattleSimulation battle, Rect uv, int width, int height)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT

            EnsureUnityCamera();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var previous = RenderTexture.active;
            try
            {
                bool maskAllocated = EnsureUnityMaskTargets(width, height);

                var stageWatch = System.Diagnostics.Stopwatch.StartNew();
                SyncUnityMaterials(battle, maskAllocated);
                MaterialSyncMs = stageWatch.Elapsed.TotalMilliseconds;
                stageWatch.Restart();
                // Command buffers reference the retained meshes directly. Register static
                // MeshRenderers only for a camera pass which actually culls those layers.
                var staticTargets = UnityCachedStaticCommands
                    ? StaticRendererTargets.None
                    : StaticRendererTargets.Color;
                if (!CanUseMergedMaskGeometry())
                    staticTargets |= StaticRendererTargets.Masks;
                bool targetsChanged = submittedStaticTargets != staticTargets;
                bool submitScene = unitySubmittedFrame != pictureFrame || targetsChanged;
                if (submitScene)
                {
                    if (targetsChanged)
                        staticPrefixSerial = staticSuffixSerial = -1;
                    submittedStaticTargets = staticTargets;
                    usedUnityNodes = 0;
                    unitySubmissionOrder = 0;
                    SubmitStaticPrefix(staticTargets);
                    int prefixNodes = usedUnityNodes;
                    nativeCraterOrder = unitySubmissionOrder * 2 - 1;
                    AddNativeVisualGroups();
                    int suffixStart = usedUnityNodes;
                    SubmitStaticSuffix(staticTargets);
                    UnityStaticRendererNodes = prefixNodes + usedUnityNodes - suffixStart;
                    for (int i = usedUnityNodes; i < unityNodes.Count; i++)
                        if (unityNodes[i].Active)
                        {
                            unityNodes[i].Object.SetActive(false);
                            unityNodes[i].Active = false;
                        }
                    for (int i = unityNodes.Count - 1; i >= usedUnityNodes; i--)
                    {
                        if (pictureFrame - unityNodes[i].LastFrame <= 120)
                            break;
                        UnityEngine.Object.Destroy(unityNodes[i].Object);
                        unityNodes.RemoveAt(i);
                    }
                    expiredActorNodes.Clear();
                    int activeActors = 0;
                    foreach (var pair in unityActorNodes)
                    {
                        var node = pair.Value;
                        if (node.LastFrame == pictureFrame)
                        {
                            activeActors++;
                            continue;
                        }
                        if (node.Active)
                        {
                            node.Object.SetActive(false);
                            node.Active = false;
                        }
                        if (pictureFrame - node.LastFrame > 120)
                            expiredActorNodes.Add(pair.Key);
                    }
                    foreach (var key in expiredActorNodes)
                    {
                        UnityEngine.Object.Destroy(unityActorNodes[key].Object);
                        unityActorNodes.Remove(key);
                    }
                    UnitySceneNodes = usedUnityNodes + activeActors;
                    unitySubmittedFrame = pictureFrame;
                }
                float actorInterpolation = nativeComposer?.InterpolationAlpha ?? 1f;
                if (submitScene || submittedActorInterpolation != actorInterpolation)
                {
                    nativeComposer?.ApplyActorTransforms(this);
                    UpdateInterpolatedOrder();
                    submittedActorInterpolation = actorInterpolation;
                }
                SceneSubmissionMs = stageWatch.Elapsed.TotalMilliseconds;
                stageWatch.Restart();
                unityCamera.targetTexture = target;
                unityCamera.worldToCameraMatrix = Matrix4x4.TRS(
                    new Vector3(0, 0, -10),
                    Quaternion.identity,
                    new Vector3(1, 1, 1)
                );
                unityCamera.projectionMatrix = Matrix4x4.Ortho(
                    uv.xMin * canvas.Width,
                    uv.xMax * canvas.Width,
                    uv.yMax * canvas.Height,
                    uv.yMin * canvas.Height,
                    .1f,
                    100f
                );
                unityCamera.cullingMatrix = unityCamera.projectionMatrix * unityCamera.worldToCameraMatrix;
                unityMaskCamera.targetTexture = unityMask;
                unityMaskCamera.worldToCameraMatrix = unityCamera.worldToCameraMatrix;
                unityMaskCamera.projectionMatrix = unityCamera.projectionMatrix;
                unityMaskCamera.cullingMatrix = unityCamera.cullingMatrix;
                // Cameras are manual; the app camera has cullingMask=0. Keep retained renderers registered.

                MaskRenderMs = 0;
                stageWatch.Restart();
                UnityEngine.Profiling.Profiler.BeginSample("Rubezh.OcclusionMasks");
                try
                {
                    RenderCachedUnityMasks(battle, uv, maskAllocated);
                }
                finally
                {
                    UnityEngine.Profiling.Profiler.EndSample();
                }
                MaskRenderMs = stageWatch.Elapsed.TotalMilliseconds;
                if (maskAllocated || targetsChanged || unityEffectFrame != pictureFrame)
                {
                    UpdateNativeEffects(battle);
                    unityEffectFrame = pictureFrame;
                }
                UpdateStaticCommands(uv, width, height);
                UpdateStaticColorCache();
                unityCamera.cullingMask = DynamicSceneLayers | (UnityCachedStaticCommands ? 0 : StaticSceneLayers);
                stageWatch.Restart();
                UnityEngine.Profiling.Profiler.BeginSample("Rubezh.CameraRender");
                bool renderSucceeded = false;
                try
                {
                    unityCamera.Render();
                    renderSucceeded = true;
                }
                finally
                {
                    CompleteStaticColorCache(renderSucceeded);
                    UnityEngine.Profiling.Profiler.EndSample();
                    CameraRenderMs = UnityStaticColorRenderMs + stageWatch.Elapsed.TotalMilliseconds;
                }
                SubmitMs = watch.Elapsed.TotalMilliseconds;
            }
            finally
            {
                RenderTexture.active = previous;
            }
#else
            throw new PlatformNotSupportedException("Native Unity camera requires Unity runtime");
#endif
        }

        public int NativeForegroundFrames { get; private set; }

        bool RenderUnityForeground()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (!UnityOwnedScene || unityRoot == null || unityCamera == null || target == null)
                return false;
            if (
                foregroundRaw == null
                || foregroundRaw.width != target.width
                || foregroundRaw.height != target.height
                || !foregroundRaw.IsCreated()
            )
            {
                if (foregroundRaw != null)
                {
                    foregroundRaw.Release();
                    UnityEngine.Object.Destroy(foregroundRaw);
                }
                foregroundRaw = new RenderTexture(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear
                );
                foregroundRaw.filterMode = FilterMode.Bilinear;
                foregroundRaw.wrapMode = TextureWrapMode.Clamp;
                if (!foregroundRaw.Create())
                    throw new InvalidOperationException("Cannot create native actor foreground");
            }
            if (
                foreground == null
                || foreground.width != target.width
                || foreground.height != target.height
                || !foreground.IsCreated()
            )
            {
                if (foreground != null)
                {
                    foreground.Release();
                    UnityEngine.Object.Destroy(foreground);
                }
                foreground = new RenderTexture(
                    target.width,
                    target.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear
                );
                foreground.filterMode = FilterMode.Bilinear;
                foreground.wrapMode = TextureWrapMode.Clamp;
                if (!foreground.Create())
                    throw new InvalidOperationException("Cannot create native foreground output");
            }
            bool commandsWereAttached = unityCommandsAttached;
            AttachStaticCommands(false);
            var previous = RenderTexture.active;
            int mask = unityCamera.cullingMask;
            var color = unityCamera.backgroundColor;
            var output = unityCamera.targetTexture;
            try
            {
                // Same native pose, same occlusion shader and projection; no second CPU actor pass.
                unityCamera.cullingMask = 1 << ActorLayer;
                unityCamera.backgroundColor = Color.clear;
                unityCamera.targetTexture = foregroundRaw;
                // Cameras are manual; the app camera has cullingMask=0. Keep retained renderers registered.
                unityCamera.Render();
                UnityEngine.Graphics.Blit(foregroundRaw, foreground, backgroundMaterial, 2);
                NativeForegroundFrames++;
            }
            finally
            {
                unityCamera.cullingMask = mask;
                unityCamera.backgroundColor = color;
                unityCamera.targetTexture = output;
                RenderTexture.active = previous;
                AttachStaticCommands(commandsWereAttached);
            }
            return true;
#else
            return false;
#endif
        }

        void DisposeUnityCamera()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            ReleaseStaticCommands();
            ReleaseUnityMaskCommands();
            DisposeNativeEffects();
#endif
            ReleaseUnityMask();
            DisposeDirectVisuals();
            DisposeVisualGroups();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (unityRoot != null)
            {
                unityRoot.SetActive(false);
                UnityEngine.Object.Destroy(unityRoot);
                unityRoot = null;
            }
            foreach (var mat in unityMaterials)
                UnityEngine.Object.Destroy(mat);
            unityMaterials.Clear();
            unityMaterialSources.Clear();
            unityMaterialMap = null;
            staticPrefixSerial = staticSuffixSerial = -1;
            unityNodes.Clear();
            unityActorNodes.Clear();
            expiredActorNodes.Clear();
            unityCamera = unityMaskCamera = null;
            unityMaskMap = null;
            unityTemplate = null;
            if (unityScene.IsValid() && unityScene.isLoaded)
                SceneManager.UnloadSceneAsync(unityScene);
#endif
        }
    }
}
