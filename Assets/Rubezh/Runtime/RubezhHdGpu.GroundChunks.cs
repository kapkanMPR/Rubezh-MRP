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
        const int GroundChunkSide = 15;
        sealed class GroundBatch
        {
            public Mesh Mesh;
            public int[] Pages;
            public Rect Bounds;
            public bool Dirty = true;
            public int LastVisible;
        }

        readonly Dictionary<long, GroundBatch> groundChunks = new Dictionary<long, GroundBatch>();
        readonly List<GroundBatch> groundDraws = new List<GroundBatch>();
        readonly List<long> groundEvictions = new List<long>();
        BattleMap groundMap;
        int groundRevision = int.MinValue;
        bool buildingGroundChunk;
        public int GroundChunkBuilds { get; private set; }
        public double GroundRunCompactionMs { get; private set; }
        public int GroundRunsBefore { get; private set; }
        public int GroundRunsAfter { get; private set; }
        public int GroundResidentChunks => groundChunks.Count;
        public int GroundVisibleChunks { get; private set; }

        public void GroundChunks(BattleMap map, Action<int, int, int, int> drawCells)
        {
            groundDraws.Clear();
            if (vertices.Count != 0)
                throw new InvalidOperationException("Ground must precede scene geometry.");
            int columns = (map.Columns + GroundChunkSide - 1) / GroundChunkSide;
            if (!ReferenceEquals(groundMap, map))
            {
                DisposeGroundChunks();
                groundMap = map;
                groundRevision = map.Revision;
            }
            else if (groundRevision != map.Revision)
            {
                HashSet<int> roots;
                if (!map.SceneryChanges.Since(map, groundRevision, out roots))
                    foreach (var batch in groundChunks.Values) batch.Dirty = true;
                else
                    foreach (int root in roots)
                    {
                        if (root < 0 || root >= map.Columns * map.Rows) continue;
                        long key = DynamicBuilding.Key(root % map.Columns / GroundChunkSide, root / map.Columns / GroundChunkSide);
                        GroundBatch batch;
                        if (groundChunks.TryGetValue(key, out batch)) batch.Dirty = true;
                    }
                groundRevision = map.Revision;
            }

            // Coordinates are shared across chunks: no independent rounding at borders.
            // Geometry has no extra color/depth texture and no resolution ceiling per map.
            bool exterior = NativeScene && staticMap == map;
            int reach = exterior ? (int)Math.Ceiling((BattleVisualRecipes.LogicalWidth(map) / 22f + 90f / 11f + BattleVisualRecipes.LogicalHeight(map) / 10f + 90f / 5f) * .5f) + 2 : 0;
            int leftEdge = exterior ? (int)Math.Floor((map.Columns / 2 - reach) / (float)GroundChunkSide) * GroundChunkSide : 0;
            int topEdge = exterior ? (int)Math.Floor((map.Rows / 2 - reach) / (float)GroundChunkSide) * GroundChunkSide : 0;
            int rightEdge = exterior ? map.Columns / 2 + reach : map.Columns;
            int bottomEdge = exterior ? map.Rows / 2 + reach : map.Rows;
            // Invert the viewport rectangle into an enclosing world rectangle.
            // The 96 logical pixels include the chunk bounds padding and near margin.
            var origin = BattleVisualRecipes.Project(new V2(0, 0), map);
            float u0 = (visiblePixels.xMin / canvas.Scale - 96 - origin.X) / 11;
            float u1 = (visiblePixels.xMax / canvas.Scale + 96 - origin.X) / 11;
            float v0 = (visiblePixels.yMin / canvas.Scale - 96 - origin.Y) / 5;
            float v1 = (visiblePixels.yMax / canvas.Scale + 96 - origin.Y) / 5;
            int firstX = Math.Max(leftEdge, (int)Math.Floor((u0 - v1) / (2 * GroundChunkSide)) * GroundChunkSide - GroundChunkSide);
            int lastX = Math.Min(rightEdge, (int)Math.Ceiling((u1 - v0) / (2 * GroundChunkSide)) * GroundChunkSide + GroundChunkSide);
            int firstY = Math.Max(topEdge, (int)Math.Floor((u0 + v0) / (2 * GroundChunkSide)) * GroundChunkSide - GroundChunkSide);
            int lastY = Math.Min(bottomEdge, (int)Math.Ceiling((u1 + v1) / (2 * GroundChunkSide)) * GroundChunkSide + GroundChunkSide);
            for (int y = firstY; y < lastY; y += GroundChunkSide)
                for (int x = firstX; x < lastX; x += GroundChunkSide)
                {
                    int right = Math.Min(rightEdge, x + GroundChunkSide);
                    int bottom = Math.Min(bottomEdge, y + GroundChunkSide);
                    var a = BattleVisualRecipes.Project(new V2(x, y), map);
                    var b = BattleVisualRecipes.Project(new V2(right, bottom), map);
                    var c = BattleVisualRecipes.Project(new V2(right, y), map);
                    var d = BattleVisualRecipes.Project(new V2(x, bottom), map);
                    float pad = 32 * canvas.Scale;
                    var bounds = new Rect(a.X * canvas.Scale - pad, c.Y * canvas.Scale - pad,
                        (b.X - a.X) * canvas.Scale + 2 * pad, (d.Y - c.Y) * canvas.Scale + 2 * pad);
                    if (!GroundNear(bounds, 64 * canvas.Scale)) continue;
                    long key = DynamicBuilding.Key(x / GroundChunkSide, y / GroundChunkSide);
                    GroundBatch batch;
                    if (!groundChunks.TryGetValue(key, out batch))
                    {
                        batch = new GroundBatch { Bounds = bounds };
                        groundChunks.Add(key, batch);
                    }
                    if (batch.Dirty) BuildGroundBatch(batch, drawCells, x, y, right, bottom);
                    batch.LastVisible = pictureFrame;
                    groundDraws.Add(batch);
                }

            GroundVisibleChunks = groundDraws.Count;
            groundEvictions.Clear();
            foreach (var pair in groundChunks)
                if (pictureFrame - pair.Value.LastVisible > 180 && !GroundNear(pair.Value.Bounds, 256 * canvas.Scale))
                    groundEvictions.Add(pair.Key);
            foreach (long key in groundEvictions)
            {
                UnityEngine.Object.Destroy(groundChunks[key].Mesh);
                groundChunks.Remove(key);
            }
        }

        bool GroundNear(Rect bounds, float margin) =>
            bounds.xMax >= visiblePixels.xMin - margin && bounds.xMin <= visiblePixels.xMax + margin &&
            bounds.yMax >= visiblePixels.yMin - margin && bounds.yMin <= visiblePixels.yMax + margin;

        void ClearGroundScratch()
        {
            vertices.Clear(); uvs.Clear(); depths.Clear(); clips.Clear();
            colors.Clear(); indices.Clear(); runs.Clear();
        }

        void BuildGroundBatch(GroundBatch batch, Action<int, int, int, int> draw,
            int left, int top, int right, int bottom)
        {
            Mesh replacement = null;
            try
            {
                buildingGroundChunk = true; // Never cache geometry clipped to today's viewport.
                draw(left, top, right, bottom);
                GroundRunsBefore += runs.Items.Count;
                // Retain a two-logical-pixel gap, conservatively above raster edge precision.
                long runStart=System.Diagnostics.Stopwatch.GetTimestamp();
                CompactSolidMaterialRuns();
                OrderedMeshRuns.Compact(vertices, indices, runs, 2 * canvas.Scale);
                var textureSlots = CompactGroundMaterialRuns();
                GroundRunCompactionMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-runStart)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                GroundRunsAfter += runs.Items.Count;
                replacement = new Mesh { name = "Rubezh ground 15x15", indexFormat = IndexFormat.UInt32 };
                replacement.SetVertices(vertices);
                replacement.SetUVs(0, uvs); replacement.SetUVs(1, depths); replacement.SetUVs(2, clips);
                if (textureSlots != null) replacement.SetUVs(3, textureSlots);
                replacement.SetColors(colors);
                replacement.subMeshCount = runs.Items.Count;
                var pages = new int[runs.Items.Count];
                for (int i = 0; i < pages.Length; i++)
                {
                    var run = runs.Items[i];
                    replacement.SetTriangles(indices, run.Start, run.Count, i, false);
                    pages[i] = run.Page;
                }
                replacement.RecalculateBounds();
                UnityEngine.Object.Destroy(batch.Mesh);
                batch.Mesh = replacement;
                replacement = null;
                batch.Pages = pages;
                batch.Dirty = false;
                GroundChunkBuilds++;
            }
            finally
            {
                if (replacement != null) UnityEngine.Object.Destroy(replacement);
                buildingGroundChunk = false;
                ClearGroundScratch();
            }
        }



        void DisposeGroundChunks()
        {
            foreach (var batch in groundChunks.Values) UnityEngine.Object.Destroy(batch.Mesh);
            groundChunks.Clear(); groundDraws.Clear();
            GroundVisibleChunks = 0;
            groundMap = null; groundRevision = int.MinValue;
        }
    }
}
