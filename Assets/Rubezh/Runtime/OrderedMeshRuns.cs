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
        struct Box
        {
            internal float L,
                T,
                R,
                B;

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
                bounds[i] = box;
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
            return count - runs.Items.Count;
        }
    }
}
