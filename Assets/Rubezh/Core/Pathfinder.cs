using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Pathfinder
    {
#if RUBEZH_BENCH
        public static Action<BattleMap, V2, V2, Func<V2, float>> TraceRequest;
#endif

        // Per-thread A* storage. Search epochs avoid clearing the entire map per request.
        [ThreadStatic]
        private static PathScratch scratch;

        private sealed class PathScratch
        {
            public float[] Costs = Array.Empty<float>(),
                Scores = Array.Empty<float>(),
                Risks = Array.Empty<float>();
            public int[] Previous = Array.Empty<int>(),
                HeapPosition = Array.Empty<int>(),
                Insertion = Array.Empty<int>();
            public byte[] Flags = Array.Empty<byte>(),
                Geometry = Array.Empty<byte>();
            public uint[] Visited = Array.Empty<uint>(),
                GeometryVisited = Array.Empty<uint>();
            public uint Epoch;
            int nextInsertion;
            bool unorderedScores;
            public readonly System.Collections.Generic.List<int> Open = new System.Collections.Generic.List<int>(128);
            public bool Busy;

            public byte State(int cell) => Visited[cell] == Epoch ? Flags[cell] : (byte)0;

            public void Mark(int cell, byte state)
            {
                Visited[cell] = Epoch;
                Flags[cell] = state;
            }

            public void Begin(int count)
            {
                if (Costs.Length < count)
                {
                    Costs = new float[count];
                    Scores = new float[count];
                    Previous = new int[count];
                    Flags = new byte[count];
                    Geometry = new byte[count];
                    Risks = new float[count];
                    HeapPosition = new int[count];
                    Insertion = new int[count];
                    Visited = new uint[count];
                    GeometryVisited = new uint[count];
                }

                if (Epoch == uint.MaxValue)
                {
                    // Clear the full capacity, including cells outside a temporarily smaller map.
                    Array.Clear(Visited, 0, Visited.Length);
                    Array.Clear(GeometryVisited, 0, GeometryVisited.Length);
                    Epoch = 1;
                }
                else
                    Epoch++;
                Open.Clear();
                nextInsertion = 0;
                unorderedScores = false;
            }

            public bool Walk(BattleMap map, int x, int y)
            {
                if ((uint)x >= (uint)map.Columns || (uint)y >= (uint)map.Rows)
                    return false;
                int i = y * map.Columns + x;
                if (GeometryVisited[i] != Epoch)
                {
                    Geometry[i] = (byte)(map.Walkable(x, y) ? 2 : 1);
                    GeometryVisited[i] = Epoch;
                }

                return Geometry[i] == 2;
            }

            // Equal scores retain first insertion order, exactly like the previous linear list.
            bool Before(int a, int b) => Scores[a] < Scores[b] || Scores[a] == Scores[b] && Insertion[a] < Insertion[b];

            void Swap(int a, int b)
            {
                int node = Open[a];
                Open[a] = Open[b];
                Open[b] = node;
                HeapPosition[Open[a]] = a;
                HeapPosition[node] = b;
            }

            int Up(int i)
            {
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Before(Open[i], Open[parent]))
                        break;
                    Swap(i, parent);
                    i = parent;
                }

                return i;
            }

            void Down(int i)
            {
                while (true)
                {
                    int left = i * 2 + 1;
                    if (left >= Open.Count)
                        return;
                    int right = left + 1;
                    int best = right < Open.Count && Before(Open[right], Open[left]) ? right : left;
                    if (!Before(Open[best], Open[i]))
                        return;
                    Swap(i, best);
                    i = best;
                }
            }

            public void Add(int cell)
            {
                Insertion[cell] = nextInsertion++;
                HeapPosition[cell] = Open.Count;
                Open.Add(cell);
                if (float.IsNaN(Scores[cell]))
                    unorderedScores = true;
                if (!unorderedScores)
                    Up(Open.Count - 1);
            }

            public void Changed(int cell)
            {
                if (float.IsNaN(Scores[cell]))
                    unorderedScores = true;
                if (!unorderedScores)
                    Down(Up(HeapPosition[cell]));
            }

            public int Pop()
            {
                int index = 0;
                if (unorderedScores)
                {
                    // Preserve the old '<' semantics even for NaN callbacks: first inserted
                    // NaN wins only when it is the first remaining element of the old list.
                    for (int i = 1; i < Open.Count; i++)
                        if (Insertion[Open[i]] < Insertion[Open[index]])
                            index = i;
                    if (!float.IsNaN(Scores[Open[index]]))
                        for (int i = 0; i < Open.Count; i++)
                            if (Before(Open[i], Open[index]))
                                index = i;
                }

                int result = Open[index],
                    last = Open.Count - 1;
                if (index != last)
                {
                    Open[index] = Open[last];
                    HeapPosition[Open[index]] = index;
                }

                Open.RemoveAt(last);
                HeapPosition[result] = -1;
                if (!unorderedScores && index < Open.Count)
                    Down(Up(index));
                return result;
            }
        }

        public static System.Collections.Generic.List<V2> Find(
            BattleMap map,
            V2 start,
            V2 end,
            Func<V2, float> risk = null,
            Func<V2, V2, bool> edgeAllowed = null
        )
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.AI.Pathfinder"))
            {
#if RUBEZH_BENCH
                TraceRequest?.Invoke(map, start, end, risk);
#endif
                // Validate coordinates, not only their flattened index (x=-1 can alias another row).
                if (
                    !(
                        start.X >= 0
                        && start.Y >= 0
                        && start.X < map.Columns
                        && start.Y < map.Rows
                        && end.X >= 0
                        && end.Y >= 0
                        && end.X < map.Columns
                        && end.Y < map.Rows
                    )
                )
                    return new System.Collections.Generic.List<V2>();
                int startIndex = map.Index(start);
                int goalIndex = map.Index(end);
                int cellCount = map.Columns * map.Rows;
                if (
                    startIndex < 0
                    || startIndex >= cellCount
                    || goalIndex < 0
                    || goalIndex >= cellCount
                    || !map.Walkable(end)
                )
                {
                    return new System.Collections.Generic.List<V2>();
                }

                if (startIndex == goalIndex)
                {
                    if (edgeAllowed != null && !edgeAllowed(start, end))
                        return new System.Collections.Generic.List<V2>();
                    return (V2.Distance(start, end) > 0.001f)
                        ? new System.Collections.Generic.List<V2> { end }
                        : new System.Collections.Generic.List<V2>();
                }

                PathScratch workspace = scratch;
                if (workspace == null)
                {
                    workspace = new PathScratch();
                    scratch = workspace;
                }

                if (workspace.Busy)
                    workspace = new PathScratch(); // nested path evaluation
                workspace.Busy = true;
                try
                {
                    workspace.Begin(cellCount);
                    float[] costs = workspace.Costs;
                    float[] scores = workspace.Scores;
                    int[] previousCells = workspace.Previous;
                    costs[startIndex] = 0f;
                    scores[startIndex] = V2.Distance(start, end);
                    workspace.Mark(startIndex, 1);
                    workspace.Add(startIndex);
                    while (workspace.Open.Count > 0)
                    {
                        int currentIndex = workspace.Pop();
                        workspace.Mark(currentIndex, 2);
                        if (currentIndex == goalIndex)
                        {
                            System.Collections.Generic.List<V2> path = new System.Collections.Generic.List<V2>();
                            while (currentIndex != startIndex)
                            {
                                path.Add(map.Center(currentIndex));
                                currentIndex = previousCells[currentIndex];
                            }

                            path.Reverse();
                            return path;
                        }

                        int currentX = currentIndex % map.Columns;
                        int currentY = currentIndex / map.Columns;
                        for (int offsetY = -1; offsetY <= 1; offsetY++)
                        {
                            for (int offsetX = -1; offsetX <= 1; offsetX++)
                            {
                                if (
                                    (offsetX == 0 && offsetY == 0)
                                    || !workspace.Walk(map, currentX + offsetX, currentY + offsetY)
                                    || (
                                        offsetX != 0
                                        && offsetY != 0
                                        && (
                                            !workspace.Walk(map, currentX + offsetX, currentY)
                                            || !workspace.Walk(map, currentX, currentY + offsetY)
                                        )
                                    )
                                )
                                {
                                    continue;
                                }

                                int neighborIndex = (currentY + offsetY) * map.Columns + currentX + offsetX;
                                if (workspace.State(neighborIndex) == 2)
                                {
                                    continue;
                                }

                                V2 v = map.Center(neighborIndex);
                                if (
                                    edgeAllowed != null
                                    && !edgeAllowed(currentIndex == startIndex ? start : map.Center(currentIndex), v)
                                )
                                    continue;
                                // Risk callbacks describe a fixed world snapshot during one search.
                                float extra =
                                    workspace.State(neighborIndex) == 0 ? float.NaN : workspace.Risks[neighborIndex];
                                if (float.IsNaN(extra))
                                {
                                    extra = risk?.Invoke(v) ?? 0f;
                                    workspace.Risks[neighborIndex] = extra;
                                }

                                float tentativeCost =
                                    costs[currentIndex] + ((offsetX == 0 || offsetY == 0) ? 1f : 1.414214f) + extra;
                                if (
                                    !(
                                        tentativeCost
                                        >= (workspace.State(neighborIndex) == 0 ? float.MaxValue : costs[neighborIndex])
                                    )
                                )
                                {
                                    costs[neighborIndex] = tentativeCost;
                                    previousCells[neighborIndex] = currentIndex;
                                    scores[neighborIndex] = tentativeCost + V2.Distance(v, end);
                                    if (workspace.State(neighborIndex) != 1)
                                    {
                                        workspace.Mark(neighborIndex, 1);
                                        workspace.Add(neighborIndex);
                                    }
                                    else
                                        workspace.Changed(neighborIndex);
                                }
                            }
                        }
                    }

                    return new System.Collections.Generic.List<V2>();
                }
                finally
                {
                    workspace.Busy = false;
                }
            }
        }
    }
}
