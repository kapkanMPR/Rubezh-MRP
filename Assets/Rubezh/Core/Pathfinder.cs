using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Pathfinder
    {
#if RUBEZH_BENCH
        public static System.Action<BattleMap, V2, V2, System.Func<V2, float>> TraceRequest;
#endif
        // Per-thread A* storage. Search epochs avoid clearing the entire map per request.
        [System.ThreadStatic]
        private static PathScratch scratch;
        private sealed class PathScratch
        {
            public float[] Costs = System.Array.Empty<float>(), Scores = System.Array.Empty<float>(), Risks = System.Array.Empty<float>();
            public int[] Previous = System.Array.Empty<int>(), HeapPosition = System.Array.Empty<int>(), Insertion = System.Array.Empty<int>();
            public byte[] Flags = System.Array.Empty<byte>(), Geometry = System.Array.Empty<byte>();
            public uint[] Visited = System.Array.Empty<uint>(), GeometryVisited = System.Array.Empty<uint>();
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
                    System.Array.Clear(Visited, 0, Visited.Length);
                    System.Array.Clear(GeometryVisited, 0, GeometryVisited.Length);
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

                int result = Open[index], last = Open.Count - 1;
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

        public static System.Collections.Generic.List<SilentOrders.Core.V2> Find(SilentOrders.Core.BattleMap map, SilentOrders.Core.V2 start, SilentOrders.Core.V2 end, System.Func<SilentOrders.Core.V2, float> risk = null, System.Func<SilentOrders.Core.V2, SilentOrders.Core.V2, bool> edgeAllowed = null)
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.AI.Pathfinder"))
            {
#if RUBEZH_BENCH
                TraceRequest?.Invoke(map, start, end, risk);
#endif
                // Validate coordinates, not only their flattened index (x=-1 can alias another row).
                if (!(start.X >= 0 && start.Y >= 0 && start.X < map.Columns && start.Y < map.Rows && end.X >= 0 && end.Y >= 0 && end.X < map.Columns && end.Y < map.Rows))
                    return new System.Collections.Generic.List<SilentOrders.Core.V2>();
                int num = map.Index(start);
                int num2 = map.Index(end);
                int num3 = map.Columns * map.Rows;
                if (num < 0 || num >= num3 || num2 < 0 || num2 >= num3 || !map.Walkable(end))
                {
                    return new System.Collections.Generic.List<SilentOrders.Core.V2>();
                }

                if (num == num2)
                {
                    if (edgeAllowed != null && !edgeAllowed(start, end))
                        return new System.Collections.Generic.List<SilentOrders.Core.V2>();
                    return (SilentOrders.Core.V2.Distance(start, end) > 0.001f) ? new System.Collections.Generic.List<SilentOrders.Core.V2>
                    {
                        end
                    }

                    : new System.Collections.Generic.List<SilentOrders.Core.V2>();
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
                    workspace.Begin(num3);
                    float[] array = workspace.Costs;
                    float[] array2 = workspace.Scores;
                    int[] array3 = workspace.Previous;
                    array[num] = 0f;
                    array2[num] = SilentOrders.Core.V2.Distance(start, end);
                    workspace.Mark(num, 1);
                    workspace.Add(num);
                    while (workspace.Open.Count > 0)
                    {
                        int num4 = workspace.Pop();
                        workspace.Mark(num4, 2);
                        if (num4 == num2)
                        {
                            System.Collections.Generic.List<SilentOrders.Core.V2> list2 = new System.Collections.Generic.List<SilentOrders.Core.V2>();
                            while (num4 != num)
                            {
                                list2.Add(map.Center(num4));
                                num4 = array3[num4];
                            }

                            list2.Reverse();
                            return list2;
                        }

                        int num5 = num4 % map.Columns;
                        int num6 = num4 / map.Columns;
                        for (int k = -1; k <= 1; k++)
                        {
                            for (int l = -1; l <= 1; l++)
                            {
                                if ((l == 0 && k == 0) || !workspace.Walk(map, num5 + l, num6 + k) || (l != 0 && k != 0 && (!workspace.Walk(map, num5 + l, num6) || !workspace.Walk(map, num5, num6 + k))))
                                {
                                    continue;
                                }

                                int num7 = (num6 + k) * map.Columns + num5 + l;
                                if (workspace.State(num7) == 2)
                                {
                                    continue;
                                }

                                SilentOrders.Core.V2 v = map.Center(num7);
                                if (edgeAllowed != null && !edgeAllowed(num4 == num ? start : map.Center(num4), v))
                                    continue;
                                // Risk callbacks describe a fixed world snapshot during one search.
                                float extra = workspace.State(num7) == 0 ? float.NaN : workspace.Risks[num7];
                                if (float.IsNaN(extra))
                                {
                                    extra = risk?.Invoke(v) ?? 0f;
                                    workspace.Risks[num7] = extra;
                                }

                                float num8 = array[num4] + ((l == 0 || k == 0) ? 1f : 1.414214f) + extra;
                                if (!(num8 >= (workspace.State(num7) == 0 ? float.MaxValue : array[num7])))
                                {
                                    array[num7] = num8;
                                    array3[num7] = num4;
                                    array2[num7] = num8 + SilentOrders.Core.V2.Distance(v, end);
                                    if (workspace.State(num7) != 1)
                                    {
                                        workspace.Mark(num7, 1);
                                        workspace.Add(num7);
                                    }
                                    else
                                        workspace.Changed(num7);
                                }
                            }
                        }
                    }

                    return new System.Collections.Generic.List<SilentOrders.Core.V2>();
                }
                finally
                {
                    workspace.Busy = false;
                }
            }
        }
    }
}
