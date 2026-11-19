using System;
using System.Collections.Generic;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // A bounded local reorder, not a texture sort. Every crossed draw must have
    // disjoint projected bounds, including the caller's rasterization guard.
    internal static class OrderedMeshRuns
    {
        internal struct Box
        {
            internal float L,
                T,
                R,
                B;

            internal Rect Rect => new Rect(L, T, R - L, B - T);

            internal bool Separate(Box b, float guard) =>
                R + guard < b.L || b.R + guard < L || B + guard < b.T || b.B + guard < T;

            internal void Include(Box b)
            {
                L = Math.Min(L, b.L);
                T = Math.Min(T, b.T);
                R = Math.Max(R, b.R);
                B = Math.Max(B, b.B);
            }
        }

        internal static Box RunBounds(List<Vector3> vertices, List<int> indices, AtlasLayout.DrawRuns.Run run)
        {
            var first = vertices[indices[run.Start]];
            var box = new Box
            {
                L = first.x,
                R = first.x,
                T = first.y,
                B = first.y,
            };
            for (int n = run.Start + 1; n < run.Start + run.Count; n++)
            {
                var v = vertices[indices[n]];
                box.L = Math.Min(box.L, v.x);
                box.R = Math.Max(box.R, v.x);
                box.T = Math.Min(box.T, v.y);
                box.B = Math.Max(box.B, v.y);
            }
            return box;
        }

        // Picture layers alternate atlas pages across many separate buildings. A layer
        // may be emitted once all overlapping earlier layers in the window are drawn.
        // Windows stay ordered, bounding both the search cost and spatial spread.
        internal static int CompactPictures(
            List<Vector3> vertices,
            List<int> indices,
            AtlasLayout.DrawRuns runs,
            float guard
        )
        {
            const int window = 64;
            int count = runs.Items.Count;
            if (count < 3)
                return 0;
            var bounds = new Box[window];
            var dependencies = new ulong[window];
            var order = new List<int>(count);
            int page = -1;
            int scheduledRuns = 0;
            for (int start = 0; start < count; start += window)
            {
                int length = Math.Min(window, count - start);
                for (int i = 0; i < length; i++)
                {
                    var run = runs.Items[start + i];
                    if (run.Count == 0 || run.Count % 6 != 0)
                        return 0;
                    bounds[i] = RunBounds(vertices, indices, run);
                    ulong earlier = 0;
                    for (int j = 0; j < i; j++)
                        if (!bounds[i].Separate(bounds[j], guard))
                            earlier |= 1UL << j;
                    dependencies[i] = earlier;
                }
                ulong pending = length == window ? ulong.MaxValue : (1UL << length) - 1;
                while (pending != 0)
                {
                    int next = -1;
                    for (int i = 0; i < length; i++)
                    {
                        if ((pending & (1UL << i)) == 0 || (pending & dependencies[i]) != 0)
                            continue;
                        if (next < 0)
                            next = i;
                        if (runs.Items[start + i].Page == page)
                        {
                            next = i;
                            break;
                        }
                    }
                    // Dependencies only point backwards. The earliest pending run is always ready.
                    pending &= ~(1UL << next);
                    order.Add(start + next);
                    int nextPage = runs.Items[start + next].Page;
                    if (nextPage != page)
                        scheduledRuns++;
                    page = nextPage;
                }
            }
            return scheduledRuns < count ? Rewrite(indices, runs, order) : 0;
        }

        internal static int Compact(List<Vector3> vertices, List<int> indices, AtlasLayout.DrawRuns runs, float guard)
        {
            int count = runs.Items.Count;
            if (count < 3)
                return 0;
            var order = new List<int>(count);
            var bounds = new Box[count];
            for (int i = 0; i < count; i++)
            {
                var run = runs.Items[i];
                if (run.Count == 0 || run.Count % 6 != 0)
                    return 0;
                bounds[i] = RunBounds(vertices, indices, run);
                order.Add(i);
            }
            bool moved = false;
            for (int i = 0; i < order.Count - 2; i++)
            {
                int page = runs.Items[order[i]].Page;
                Box crossed = bounds[order[i + 1]];
                // At most sixteen runs inspected per position; rebuilding a large
                // scene must never introduce an unbounded quadratic sorting pass.
                int limit = Math.Min(order.Count, i + 17);
                for (int j = i + 2; j < limit; j++)
                {
                    int candidate = order[j];
                    if (runs.Items[candidate].Page == page && bounds[candidate].Separate(crossed, guard))
                    {
                        for (int shift = j; shift > i + 1; shift--)
                            order[shift] = order[shift - 1];
                        order[i + 1] = candidate;
                        moved = true;
                        break;
                    }
                    crossed.Include(bounds[candidate]);
                }
            }
            if (!moved)
                return 0;
            return Rewrite(indices, runs, order);
        }

        static int Rewrite(List<int> indices, AtlasLayout.DrawRuns runs, List<int> order)
        {
            var original = runs.Items.ToArray();
            var rewritten = new List<int>(indices.Count);
            runs.Clear();
            foreach (int index in order)
            {
                var run = original[index];
                for (int n = run.Start; n < run.Start + run.Count; n++)
                    rewritten.Add(indices[n]);
                for (int n = 0; n < run.Count; n += 6)
                    runs.AddQuad(run.Page);
            }
            indices.Clear();
            indices.AddRange(rewritten);
            return original.Length - runs.Items.Count;
        }
    }
}
