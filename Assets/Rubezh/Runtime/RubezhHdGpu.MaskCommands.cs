using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.Rendering;
#endif

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public bool UnityMergedMaskCommands = true;
        public int UnityObjectMaskDraws { get; private set; }
        public int UnityRoofMaskDraws { get; private set; }
        public int UnityMaskGeometryBuilds { get; private set; }
        public int UnityMaskMergedMeshes { get; private set; }
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        // Mask output uses component-wise MAX, not alpha compositing. It is therefore
        // safe to collect nonconsecutive triangles of the same material. Keep spatial
        // chunks so a small viewport does not submit all the occluders on a large map.
        struct MaskMergeKey : IEquatable<MaskMergeKey>
        {
            public long Chunk;
            public int Page;

            public bool Equals(MaskMergeKey b) => Chunk == b.Chunk && Page == b.Page;

            public override bool Equals(object b) => b is MaskMergeKey && Equals((MaskMergeKey)b);

            public override int GetHashCode()
            {
                unchecked
                {
                    return Chunk.GetHashCode() * 397 ^ Page;
                }
            }
        }

        sealed class MaskMergedMesh
        {
            public Mesh Mesh;
            public int Page;
            public int RoofSubmesh;
            public Bounds Bounds;
            public Bounds[] SplitBounds;

            static bool Intersects(Bounds bounds, Rect pixels) =>
                bounds.max.x >= pixels.xMin
                && bounds.min.x <= pixels.xMax
                && bounds.max.y >= pixels.yMin
                && bounds.min.y <= pixels.yMax;

            public int VisibleSubmesh(Rect pixels)
            {
                if (!Intersects(Bounds, pixels))
                    return -1;
                if (SplitBounds == null)
                    return 0;
                bool first = Intersects(SplitBounds[0], pixels);
                bool second = Intersects(SplitBounds[1], pixels);
                if (first && second)
                    return 0;
                if (first)
                    return 1;
                return second ? 2 : -1;
            }
        }

        sealed class MaskMergeBuilder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> UVs = new List<Vector2>(),
                Depths = new List<Vector2>(),
                Clips = new List<Vector2>();
            public readonly List<Color32> Colors = new List<Color32>();
            public readonly List<int> Indices = new List<int>();
            readonly Dictionary<int, int> remap = new Dictionary<int, int>();
            readonly List<Vector2> membership = new List<Vector2>();
            GeometryData lastData;
            bool lastRoof;
            int roofIndexCount;

            public void Add(GeometryData data, int start, int count, bool roof)
            {
                if (!ReferenceEquals(lastData, data) || lastRoof != roof)
                {
                    remap.Clear();
                    lastData = data;
                    lastRoof = roof;
                }
                for (int i = start; i < start + count; i += 3)
                {
                    int a = data.Indices[i],
                        b = data.Indices[i + 1],
                        c = data.Indices[i + 2];
                    // The shader accepts the closed mode interval [.5, 2.5]. Interpolated
                    // modes that cross a boundary must remain available to the fragment shader.
                    float ma = data.Depths[a].y,
                        mb = data.Depths[b].y,
                        mc = data.Depths[c].y;
                    if ((ma < .5f && mb < .5f && mc < .5f) || (ma > 2.5f && mb > 2.5f && mc > 2.5f))
                        continue;
                    Indices.Add(MapVertex(data, a, roof));
                    Indices.Add(MapVertex(data, b, roof));
                    Indices.Add(MapVertex(data, c, roof));
                    if (roof)
                        roofIndexCount += 3;
                }
            }

            int MapVertex(GeometryData data, int source, bool roof)
            {
                if (remap.TryGetValue(source, out int index))
                    return index;
                index = Vertices.Count;
                remap.Add(source, index);
                Vertices.Add(data.Vertices[source]);
                UVs.Add(data.UVs[source]);
                Depths.Add(data.Depths[source]);
                Clips.Add(data.Clips[source]);
                Colors.Add(data.Colors[source]);
                membership.Add(new Vector2(roof ? 1 : 0, 0));
                return index;
            }

            // Picture pages can span distant buildings. Select one of two smaller index
            // ranges when only that half is visible; both halves together still cost one draw.
            Bounds[] AddPictureRanges(Mesh mesh)
            {
                if (Indices.Count < 12)
                    return null;
                var whole = mesh.bounds;
                bool splitX = whole.size.x >= whole.size.y;
                var ranges = new[] { new List<int>(), new List<int>() };
                var bounds = new Bounds[2];
                for (int i = 0; i < Indices.Count; i += 3)
                {
                    var triangle = new Bounds(Vertices[Indices[i]], Vector3.zero);
                    triangle.Encapsulate(Vertices[Indices[i + 1]]);
                    triangle.Encapsulate(Vertices[Indices[i + 2]]);
                    bool second = splitX ? triangle.center.x >= whole.center.x : triangle.center.y >= whole.center.y;
                    int half = second ? 1 : 0;
                    if (ranges[half].Count == 0)
                        bounds[half] = triangle;
                    else
                        bounds[half].Encapsulate(triangle);
                    ranges[half].Add(Indices[i]);
                    ranges[half].Add(Indices[i + 1]);
                    ranges[half].Add(Indices[i + 2]);
                }
                if (ranges[0].Count == 0 || ranges[1].Count == 0)
                    return null;
                mesh.subMeshCount = 3;
                mesh.SetTriangles(ranges[0], 1, false);
                mesh.SetTriangles(ranges[1], 2, false);
                return bounds;
            }

            public MaskMergedMesh Upload(int page, bool packed, bool picture)
            {
                var mesh = new Mesh
                {
                    name = picture ? "Rubezh picture MAX occluders" : "Rubezh merged MAX occluders",
                    indexFormat = Vertices.Count <= ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32,
                };
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, UVs);
                mesh.SetUVs(1, Depths);
                mesh.SetUVs(2, Clips);
                if (packed)
                    mesh.SetUVs(4, membership);
                mesh.SetColors(Colors);
                bool separateRoofRange = !packed && roofIndexCount > 0 && roofIndexCount < Indices.Count;
                mesh.subMeshCount = separateRoofRange ? 2 : 1;
                mesh.SetTriangles(Indices, 0, false);
                if (separateRoofRange)
                {
                    var roofIndices = new List<int>(roofIndexCount);
                    foreach (int index in Indices)
                        if (membership[index].x > .5f)
                            roofIndices.Add(index);
                    mesh.SetTriangles(roofIndices, 1, false);
                }
                mesh.RecalculateBounds();
                var bounds = mesh.bounds;
                var splitBounds = picture ? AddPictureRanges(mesh) : null;
                mesh.UploadMeshData(true);
                return new MaskMergedMesh
                {
                    Mesh = mesh,
                    Page = page,
                    RoofSubmesh =
                        roofIndexCount == 0 ? -1
                        : separateRoofRange ? 1
                        : 0,
                    Bounds = bounds,
                    SplitBounds = splitBounds,
                };
            }
        }

        sealed class MaskPageMaterial
        {
            public Material Material;
            public int Revision = -1;
        }

        readonly List<MaskMergedMesh> unityMaskMergedMeshes = new List<MaskMergedMesh>();
        readonly Dictionary<int, MaskPageMaterial> unityMergedMaskMaterials = new Dictionary<int, MaskPageMaterial>();
        CommandBuffer unityObjectMaskCommands,
            unityRoofMaskCommands;
        int unityMaskMergedGeometry = -1,
            unityMaskAvailabilitySerial = -1;
        bool unityMaskDataAvailable,
            unityMaskMeshesPacked;

        Material MaskMaterialForPage(int page)
        {
            if (!unityMergedMaskMaterials.TryGetValue(page, out var entry))
            {
                entry = new MaskPageMaterial { Material = new Material(unityMaskShader) };
                unityMergedMaskMaterials.Add(page, entry);
            }
            if (entry.Revision != unityStaticMaterialRevision)
            {
                entry.Material.CopyPropertiesFromMaterial(unityMaterials[page]);
                entry.Material.shader = unityMaskShader;
                entry.Material.SetFloat("_NativeInstance", 0);
                entry.Material.SetFloat("_ActorRootDepth", 0);
                entry.Revision = unityStaticMaterialRevision;
            }
            return entry.Material;
        }

        void ReleaseUnityMaskCommands()
        {
            // Commands are attached only around their camera.Render call and detached in finally.
            if (unityObjectMaskCommands != null)
            {
                unityObjectMaskCommands.Release();
                unityObjectMaskCommands = null;
            }
            if (unityRoofMaskCommands != null)
            {
                unityRoofMaskCommands.Release();
                unityRoofMaskCommands = null;
            }
            foreach (var batch in unityMaskMergedMeshes)
                UnityEngine.Object.Destroy(batch.Mesh);
            unityMaskMergedMeshes.Clear();
            foreach (var entry in unityMergedMaskMaterials.Values)
                UnityEngine.Object.Destroy(entry.Material);
            unityMergedMaskMaterials.Clear();
            unityMaskMergedGeometry = unityMaskAvailabilitySerial = -1;
            UnityMaskMergedMeshes = UnityObjectMaskDraws = UnityRoofMaskDraws = 0;
        }

        static void AddMergedMaskData(
            Dictionary<MaskMergeKey, MaskMergeBuilder> groups,
            GeometryData data,
            long chunk,
            bool roof
        )
        {
            if (data == null)
                return;
            foreach (var run in data.Runs)
            {
                if (run.Count == 0)
                    continue;
                var key = new MaskMergeKey { Chunk = chunk, Page = run.Page };
                MaskMergeBuilder builder;
                if (!groups.TryGetValue(key, out builder))
                {
                    builder = new MaskMergeBuilder();
                    groups.Add(key, builder);
                }
                builder.Add(data, run.Start, run.Count, roof);
            }
        }

        bool CanUseMergedMaskGeometry()
        {
            if (!UnityMergedMaskCommands)
                return false;
            if (unityMaskAvailabilitySerial != geometrySerial)
            {
                unityMaskDataAvailable =
                    pictureBatch.Mesh == null || pictureBatch.Pages.Length == 0 || pictureBatch.Data != null;
                foreach (var batch in objectDraws)
                    if (batch.Data == null)
                    {
                        unityMaskDataAvailable = false;
                        break;
                    }
                unityMaskAvailabilitySerial = geometrySerial;
            }
            return unityMaskDataAvailable;
        }

        bool PrepareUnityMaskCommands(Rect view)
        {
            if (!CanUseMergedMaskGeometry())
                return false;
            if (unityMaskMergedGeometry != geometrySerial || unityMaskMeshesPacked != UnityPackedMasks)
            {
                var groups = new Dictionary<MaskMergeKey, MaskMergeBuilder>();
                AddMergedMaskData(groups, pictureBatch.Data, long.MinValue, true);
                foreach (var batch in objectDraws)
                    AddMergedMaskData(groups, batch.Data, batch.Chunk, batch.BlocksRoof);
                foreach (var batch in unityMaskMergedMeshes)
                    UnityEngine.Object.Destroy(batch.Mesh);
                unityMaskMergedMeshes.Clear();
                foreach (var entry in groups)
                    if (entry.Value.Indices.Count > 0)
                        unityMaskMergedMeshes.Add(
                            entry.Value.Upload(entry.Key.Page, UnityPackedMasks, entry.Key.Chunk == long.MinValue)
                        );
                unityMaskMeshesPacked = UnityPackedMasks;
                unityMaskMergedGeometry = geometrySerial;
                UnityMaskGeometryBuilds++;
                UnityMaskMergedMeshes = unityMaskMergedMeshes.Count;
            }
            if (unityObjectMaskCommands == null)
                unityObjectMaskCommands = new CommandBuffer { name = "Rubezh grouped object MAX mask" };
            if (!UnityPackedMasks && unityRoofMaskCommands == null)
                unityRoofMaskCommands = new CommandBuffer { name = "Rubezh grouped roof MAX mask" };
            unityObjectMaskCommands.Clear();
            unityRoofMaskCommands?.Clear();
            UnityObjectMaskDraws = UnityRoofMaskDraws = 0;
            if (DisableScenery)
                return true;
            float padX = Math.Abs(view.width) * canvas.Width / Math.Max(1, UnityMaskWidth) * 2;
            float padY = Math.Abs(view.height) * canvas.Height / Math.Max(1, UnityMaskHeight) * 2;
            var pixels = new Rect(
                view.xMin * canvas.Width - padX,
                view.yMin * canvas.Height - padY,
                view.width * canvas.Width + 2 * padX,
                view.height * canvas.Height + 2 * padY
            );
            foreach (var batch in unityMaskMergedMeshes)
            {
                int submesh = batch.VisibleSubmesh(pixels);
                if (submesh < 0)
                    continue;
                var material = MaskMaterialForPage(batch.Page);
                unityObjectMaskCommands.DrawMesh(batch.Mesh, Matrix4x4.identity, material, submesh, 0);
                UnityObjectMaskDraws++;
                if (!UnityPackedMasks && batch.RoofSubmesh >= 0)
                {
                    int roofSubmesh = batch.RoofSubmesh == 0 ? submesh : batch.RoofSubmesh;
                    unityRoofMaskCommands.DrawMesh(batch.Mesh, Matrix4x4.identity, material, roofSubmesh, 0);
                    UnityRoofMaskDraws++;
                }
            }
            return true;
        }

        void RenderUnityMergedMask(CommandBuffer commands, RenderTexture output, bool vehicles)
        {
            unityMaskCamera.targetTexture = output;
            unityMaskCamera.cullingMask = vehicles ? 1 << SceneryLayer : 0;
            unityMaskCamera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            try
            {
                unityMaskCamera.RenderWithShader(unityMaskShader, "");
            }
            finally
            {
                unityMaskCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, commands);
            }
        }
#endif
    }
}
