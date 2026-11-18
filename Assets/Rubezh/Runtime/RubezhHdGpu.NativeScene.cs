using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public bool NativeScene => UnitySceneMode;

        sealed class MeshBatch
        {
            public Mesh Mesh;
            public int[] Pages = new int[0];
            public bool Dirty = true, BlocksRoof, Exterior;
            public int LastFrame, Version;
            public long Chunk;
            public bool CpuOnly;
            public GeometryData Data;
            public ulong MergeHash;
        }

        sealed class GeometryData
        {
            public Vector3[] Vertices;
            public Vector2[] UVs, Depths, Clips;
            public Color32[] Colors;
            public int[] Indices;
            public AtlasLayout.DrawRuns.Run[] Runs;
        }

        struct ObjectKey : IEquatable<ObjectKey>
        {
            public int Id, L, R;
            public bool Equals(ObjectKey b) => Id == b.Id && L == b.L && R == b.R;
            public override bool Equals(object o) => o is ObjectKey && Equals((ObjectKey)o);
            public override int GetHashCode() { unchecked { return (Id * 397 ^ L) * 397 ^ R; } }
        }

        sealed class GeometryScratch
        {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>(), Depths = new List<Vector2>(), Clips = new List<Vector2>();
            public List<Color32> Colors = new List<Color32>();
            public List<int> Indices = new List<int>();
            public AtlasLayout.DrawRuns Runs = new AtlasLayout.DrawRuns();
        }

        readonly GeometryScratch captureScratch = new GeometryScratch();
        readonly Dictionary<ObjectKey, MeshBatch> staticObjects = new Dictionary<ObjectKey, MeshBatch>();
        readonly List<MeshBatch> objectDraws = new List<MeshBatch>(), exteriorDraws = new List<MeshBatch>();
        readonly List<ObjectKey> objectEvictions = new List<ObjectKey>();
        readonly Dictionary<int, int> exteriorPages = new Dictionary<int, int>();
        readonly MeshBatch pictureBatch = new MeshBatch(), roofBatch = new MeshBatch();
        sealed class ObjectChunk
        {
            public readonly MeshBatch Mesh = new MeshBatch();
            public MeshBatch[] Members;
            public int[] Versions;
            public int LastFrame;
        }
        readonly Dictionary<ulong, ObjectChunk> objectChunkCache = new Dictionary<ulong, ObjectChunk>();
        readonly List<ulong> expiredObjectChunks = new List<ulong>();
        readonly List<MeshBatch> sceneObjectChunks = new List<MeshBatch>();
        readonly List<MeshBatch> sceneRoofChunks = new List<MeshBatch>();
        readonly List<MeshBatch> sceneExteriorChunks = new List<MeshBatch>();
        public int ObjectChunkBuilds, ObjectChunkHits, NativeDepthRebuilds, SharedRoofMaskChunks;
        public double ObjectChunksMs;
        public int ResidentObjectChunks => objectChunkCache.Count;

        void ClearObjectChunks()
        {
            foreach (var c in objectChunkCache.Values) UnityEngine.Object.Destroy(c.Mesh.Mesh);
            objectChunkCache.Clear();
            sceneObjectChunks.Clear(); sceneRoofChunks.Clear(); sceneExteriorChunks.Clear();
        }
        BattleMap staticMap, roofGeometryMap;
        int staticRevision = -1, roofGeometryRevision = -1;
        ulong pictureGeometryHash;
        bool drawNativeRoof, depthDirty = true;
        RenderTexture nativeDepth, nativeRoofDepth;
        Rect depthView, depthCoverage;
        float depthZoomWidth, depthZoomHeight;
        int depthRevision = -1;
        BattleMap depthMap;
        public int StaticMeshBuilds { get; private set; }
        public long StaticRunsBefore { get; private set; }
        public long StaticRunsAfter { get; private set; }
        public int StaticResidentObjects => staticObjects.Count;
        public int NativeDepthWidth => nativeDepth == null ? 0 : nativeDepth.width;
        public int NativeDepthHeight => nativeDepth == null ? 0 : nativeDepth.height;
        public long NativeDepthBytes => UnityOwnedScene ? UnityMaskBytes : nativeDepth == null ? 0 : (long)nativeDepth.width * nativeDepth.height * 8;

        void SwapScratch()
        {
            var a = vertices; vertices = captureScratch.Vertices; captureScratch.Vertices = a;
            var b = uvs; uvs = captureScratch.UVs; captureScratch.UVs = b;
            b = depths; depths = captureScratch.Depths; captureScratch.Depths = b;
            b = clips; clips = captureScratch.Clips; captureScratch.Clips = b;
            var c = colors; colors = captureScratch.Colors; captureScratch.Colors = c;
            var d = indices; indices = captureScratch.Indices; captureScratch.Indices = d;
            var e = runs; runs = captureScratch.Runs; captureScratch.Runs = e;
        }

        void StoreGeometry(MeshBatch batch)
        {
            batch.Version++;
            // Mask batching reuses the original channels; reading Mesh vertices back
            // would allocate every channel again and lose the source run ranges.
            if (batch.CpuOnly || ReferenceEquals(batch, pictureBatch))
            {
                batch.Data = new GeometryData { Vertices=vertices.ToArray(), UVs=uvs.ToArray(), Depths=depths.ToArray(),
                    Clips=clips.ToArray(), Colors=colors.ToArray(), Indices=indices.ToArray(), Runs=runs.Items.ToArray() };
            }
            if (batch.CpuOnly)
            {
                batch.Dirty = false; StaticMeshBuilds++;
                return;
            }
            StaticRunsBefore += runs.Items.Count;
            CompactSolidMaterialRuns();
            OrderedMeshRuns.Compact(vertices, indices, runs, 2 * canvas.Scale);
            StaticRunsAfter += runs.Items.Count;
            if (batch.Mesh == null) batch.Mesh = new Mesh { name = "Rubezh cached scene", indexFormat = IndexFormat.UInt32 };
            batch.Mesh.Clear();
            batch.Mesh.SetVertices(vertices);
            batch.Mesh.SetUVs(0, uvs); batch.Mesh.SetUVs(1, depths); batch.Mesh.SetUVs(2, clips);
            batch.Mesh.SetColors(colors);
            batch.Mesh.subMeshCount = runs.Items.Count;
            if (batch.Pages.Length != runs.Items.Count) batch.Pages = new int[runs.Items.Count];
            for (int i = 0; i < runs.Items.Count; i++)
            {
                var run = runs.Items[i];
                batch.Pages[i] = run.Page;
                batch.Mesh.SetTriangles(indices, run.Start, run.Count, i, false);
            }
            batch.Mesh.RecalculateBounds();
            batch.Dirty = false;
            StaticMeshBuilds++;
        }

        void CaptureGeometry(MeshBatch batch, Action draw)
        {
            bool clipping = buildingGroundChunk, test = canvas.TestDepth, record = canvas.RecordDepth;
            float depth = canvas.Depth;
            SwapScratch();
            ClearGroundScratch();
            try
            {
                buildingGroundChunk = true;
                canvas.RecordDepth = false;
                draw();
                if (ReferenceEquals(batch, roofBatch)) CompactRoofVertices();
                StoreGeometry(batch);
            }
            finally
            {
                buildingGroundChunk = clipping;
                canvas.TestDepth = test; canvas.RecordDepth = record; canvas.Depth = depth;
                ClearGroundScratch();
                SwapScratch();
            }
        }

        public void BeginScene(BattleMap map)
        {
            objectDraws.Clear(); exteriorDraws.Clear(); drawNativeRoof = false;
            if (!ReferenceEquals(staticMap, map))
            {
                foreach (var batch in staticObjects.Values) UnityEngine.Object.Destroy(batch.Mesh);
                staticObjects.Clear();
                ClearObjectChunks();
                ReleaseExteriorPages();
                staticMap = map; staticRevision = map.Revision;
                pictureBatch.Dirty = true; depthDirty = true;
            }
            else if (staticRevision != map.Revision)
            {
                HashSet<int> roots;
                bool journal = map.SceneryChanges.Since(map, staticRevision, out roots);
                foreach (var pair in staticObjects)
                    if (!journal || pair.Key.Id >= map.Tiles.Length || roots.Contains(pair.Key.Id)) pair.Value.Dirty = true;
                staticRevision = map.Revision;
                depthDirty = true;
            }
            objectEvictions.Clear();
            foreach (var pair in staticObjects)
                if (pictureFrame - pair.Value.LastFrame > 900) objectEvictions.Add(pair.Key);
            foreach (var key in objectEvictions)
            {
                UnityEngine.Object.Destroy(staticObjects[key].Mesh);
                staticObjects.Remove(key);
            }
        }

        public bool CachedObject(int id, int clipLeft, int clipRight)
        {
            var key = new ObjectKey { Id = id, L = clipLeft, R = clipRight };
            MeshBatch batch;
            if (!staticObjects.TryGetValue(key, out batch) || batch.Dirty) return false;
            batch.LastFrame = pictureFrame;
            (batch.Exterior ? exteriorDraws : objectDraws).Add(batch);
            return true;
        }

        public void CacheObject(int id, int clipLeft, int clipRight, bool blocksRoof, bool exterior, Action draw)
        {
            var key = new ObjectKey { Id = id, L = clipLeft, R = clipRight };
            MeshBatch batch;
            if (!staticObjects.TryGetValue(key, out batch)) staticObjects[key] = batch = new MeshBatch { CpuOnly=true };
            CaptureGeometry(batch, draw);
            batch.Chunk = id >= 0 && id < staticMap.Tiles.Length
                ? DynamicBuilding.Key((id % staticMap.Columns) / GroundChunkSide, (id / staticMap.Columns) / GroundChunkSide)
                : (long)id / 16;
            batch.BlocksRoof = blocksRoof; batch.Exterior = exterior; batch.LastFrame = pictureFrame;
            (exterior ? exteriorDraws : objectDraws).Add(batch);
            if (!exterior) depthDirty = true;
        }

        public void EndScene()
        {
            // Procedural atlas updates usually change pixels, not the quad layout.
            ulong hash = 1469598103934665603UL;
            unchecked
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    hash = (hash ^ (uint)vertices[i].x.GetHashCode()) * 1099511628211UL;
                    hash = (hash ^ (uint)vertices[i].y.GetHashCode()) * 1099511628211UL;
                    hash = (hash ^ (uint)uvs[i].x.GetHashCode()) * 1099511628211UL;
                    hash = (hash ^ (uint)uvs[i].y.GetHashCode()) * 1099511628211UL;
                }
                foreach (var run in runs.Items) hash = (hash ^ (uint)(run.Page * 397 ^ run.Count)) * 1099511628211UL;
            }
            if (pictureBatch.Dirty || hash != pictureGeometryHash)
            {
                StoreGeometry(pictureBatch);
                pictureGeometryHash = hash;
                depthDirty = true;
            }
            ClearGroundScratch();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            BuildObjectChunks(objectDraws, sceneObjectChunks, false, 1);
            BuildObjectChunks(objectDraws, sceneRoofChunks, true, 2);
            BuildObjectChunks(exteriorDraws, sceneExteriorChunks, false, 3);
            expiredObjectChunks.Clear();
            foreach (var entry in objectChunkCache)
                if (pictureFrame - entry.Value.LastFrame > 180) expiredObjectChunks.Add(entry.Key);
            foreach (var key in expiredObjectChunks)
            {
                UnityEngine.Object.Destroy(objectChunkCache[key].Mesh.Mesh);
                objectChunkCache.Remove(key);
            }
            ObjectChunksMs = watch.Elapsed.TotalMilliseconds;
            geometryCoverage=depthCoverage; geometryRevision=staticMap.Revision; geometryMap=staticMap;
            geometrySerial++;
        }

        // Preserve painter order: only consecutive objects in the same spatial chunk
        // are combined. Moving a viewport edge invalidates its boundary groups only.
        void BuildObjectChunks(List<MeshBatch> source, List<MeshBatch> output, bool roofOnly, uint kind)
        {
            output.Clear();
            if (roofOnly) SharedRoofMaskChunks = 0;
            for (int start = 0; start < source.Count;)
            {
                int end = start + 1;
                while (end < source.Count && end - start < 64 && source[end].Chunk == source[start].Chunk) end++;
                // A fully roof-blocking group has identical geometry in the color and
                // roof-mask passes. Share the cache entry (and its single owner), not a
                // second mesh allocation. Mixed groups still get a filtered mask mesh.
                uint cacheKind = kind;
                if (kind == 2)
                {
                    bool allBlockRoof = true;
                    for (int i = start; i < end && allBlockRoof; i++) allBlockRoof = source[i].BlocksRoof;
                    if (allBlockRoof) { cacheKind = 1; SharedRoofMaskChunks++; }
                }
                ulong hash = 1469598103934665603UL ^ cacheKind;
                int count = 0;
                unchecked
                {
                    for (int i = start; i < end; i++)
                    {
                        var batch = source[i];
                        if (roofOnly && !batch.BlocksRoof) continue;
                        hash = (hash ^ (uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(batch)) * 1099511628211UL;
                        // Stable membership key: versions invalidate contents, not the cache slot.
                        count++;
                    }
                }
                if (count == 0) { start = end; continue; }
                ObjectChunk chunk;
                bool found = objectChunkCache.TryGetValue(hash, out chunk);
                if (found)
                {
                    int j = 0;
                    found = chunk.Members.Length == count;
                    for (int i = start; found && i < end; i++)
                    {
                        var batch = source[i];
                        if (roofOnly && !batch.BlocksRoof) continue;
                        found = ReferenceEquals(chunk.Members[j], batch) && chunk.Versions[j] == batch.Version;
                        j++;
                    }
                }
                if (!found)
                {
                    if (chunk == null) chunk = new ObjectChunk();
                    if(chunk.Members==null || chunk.Members.Length!=count){chunk.Members=new MeshBatch[count];chunk.Versions=new int[count];}
                    int j = 0;
                    for (int i = start; i < end; i++)
                    {
                        var batch = source[i];
                        if (roofOnly && !batch.BlocksRoof) continue;
                        chunk.Members[j] = batch; chunk.Versions[j++] = batch.Version;
                    }
                    var captured = chunk;
                    CaptureGeometry(chunk.Mesh, () => AppendObjectGeometry(captured.Members));
                    objectChunkCache[hash] = chunk;
                    ObjectChunkBuilds++;
                    if (kind != 3) depthDirty = true; // Exterior never writes occlusion.
                }
                else ObjectChunkHits++;
                chunk.LastFrame = pictureFrame;
                output.Add(chunk.Mesh);
                start = end;
            }
        }

        void AppendObjectGeometry(MeshBatch[] members)
        {
            foreach (var batch in members)
            {
                var data = batch.Data;
                if (data == null) continue;
                int offset = vertices.Count;
                vertices.AddRange(data.Vertices); uvs.AddRange(data.UVs); depths.AddRange(data.Depths);
                clips.AddRange(data.Clips); colors.AddRange(data.Colors);
                foreach (int index in data.Indices) indices.Add(index + offset);
                foreach (var run in data.Runs)
                    for (int i = 0; i < run.Count; i += 6) runs.AddQuad(run.Page);
            }
        }

        public void VehicleDepth(Action draw)
        { if(draw!=null)throw new InvalidOperationException("Vehicles use retained Unity instances for depth."); }

        Rect roofCoverage;
        public void RoofMesh(BattleMap map, int revision, Action draw)
        {
            if (roofGeometryMap != map || roofGeometryRevision != revision || roofBatch.Mesh == null || roofCoverage != depthCoverage)
            {
                CaptureGeometry(roofBatch, draw);
                roofGeometryMap = map; roofGeometryRevision = revision;roofCoverage=depthCoverage;
            }
            drawNativeRoof = true;
        }

        void ReleaseNativeDepth()
        {
            if (nativeDepth != null) { nativeDepth.Release(); UnityEngine.Object.Destroy(nativeDepth); }
            if (nativeRoofDepth != null) { nativeRoofDepth.Release(); UnityEngine.Object.Destroy(nativeRoofDepth); }
            nativeDepth = nativeRoofDepth = null;
        }

        void DisposeNativeScene()
        {
            ReleaseSceneColor();
            foreach (var batch in staticObjects.Values) UnityEngine.Object.Destroy(batch.Mesh);
            staticObjects.Clear(); objectDraws.Clear(); exteriorDraws.Clear();
            UnityEngine.Object.Destroy(pictureBatch.Mesh); UnityEngine.Object.Destroy(roofBatch.Mesh);
            pictureBatch.Mesh = roofBatch.Mesh = null;
            ClearObjectChunks();
            ReleaseExteriorPages(); ReleaseNativeDepth();
        }

        public void ExteriorImage(ExteriorSprite sprite, float x, float y, float scale)
        {
            if (sprite.NativePage < 0) throw new InvalidOperationException("Exterior sprite was not prepared for Unity.");
            int page;
            if (!exteriorPages.TryGetValue(sprite.NativePage, out page))
            {
                var texture = ExteriorSpriteLoader.Page(sprite.NativePage);
                var material = new Material(shader);
                material.SetTexture("_MainTex", texture);
                material.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
                page = atlases.Count; atlases.Add(texture); materials.Add(material); exteriorPages[sprite.NativePage] = page;
            }
            float l = (x + sprite.Left) * scale, t = (y + sprite.Top) * scale;
            float r = l + sprite.Width * scale, b = t + sprite.Height * scale;
            Quad(l, t, r, t, r, b, l, b, sprite.U0, sprite.V0, sprite.U1, sprite.V1, new Color32(255,255,255,255), -1, false, page);
        }

        void ReleaseExteriorPages()
        {
            foreach (int page in exteriorPages.Values)
            {
                // Imported pages belong to ExteriorSpriteLoader, released after scene disposal.
                UnityEngine.Object.Destroy(materials[page]);
                atlases[page] = null; materials[page] = null;
            }
            exteriorPages.Clear();
        }
    }
}
