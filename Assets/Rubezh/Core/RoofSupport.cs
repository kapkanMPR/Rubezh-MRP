using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Dense scratch grid: no hash sets or queues allocated for every candidate neck.
    public static class RoofSupport
    {
        [ThreadStatic]
        static bool[] solidBuffer,
            anchorBuffer;

        [ThreadStatic]
        static int[] marksBuffer,
            anchoredBuffer,
            cutBuffer,
            queueBuffer;

        static void PrepareBuffers(int n)
        {
            if (solidBuffer == null || solidBuffer.Length < n)
            {
                solidBuffer = new bool[n];
                anchorBuffer = new bool[n];
                marksBuffer = new int[n];
                anchoredBuffer = new int[n];
                cutBuffer = new int[n];
                queueBuffer = new int[n];
            }
            else
            {
                Array.Clear(solidBuffer, 0, n);
                Array.Clear(anchorBuffer, 0, n);
                Array.Clear(marksBuffer, 0, n);
                Array.Clear(anchoredBuffer, 0, n);
                Array.Clear(cutBuffer, 0, n);
            }
        }

        public static double MaxMilliseconds;
        public static long LastVisits;
        public static double LastMilliseconds;
        public static int LastCells;

        public static void Collapse(ICollection<long> roof, HashSet<long> anchors)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            LastVisits = 0;
            LastCells = roof.Count;
            if (roof.Count == 0)
            {
                LastMilliseconds = 0;
                return;
            }
            int minX = int.MaxValue,
                minY = int.MaxValue,
                maxX = int.MinValue,
                maxY = int.MinValue;
            foreach (long k in roof)
            {
                int x = (int)(k >> 32),
                    y = (int)k;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            minX--;
            minY--;
            int w = maxX - minX + 2,
                h = maxY - minY + 2,
                n = checked(w * h);
            PrepareBuffers(n);
            var solid = solidBuffer;
            var anchor = anchorBuffer;
            var marks = marksBuffer;
            var anchored = anchoredBuffer;
            var cut = cutBuffer;
            var queue = queueBuffer;
            int stamp = 1;
            int[] delta = { 1, -1, w, -w };
            foreach (long k in roof)
                solid[((int)k - minY) * w + (int)(k >> 32) - minX] = true;
            int tail = 0;
            foreach (long k in anchors)
            {
                int x = (int)(k >> 32) - minX,
                    y = (int)k - minY;
                if (x <= 0 || x >= w - 1 || y <= 0 || y >= h - 1)
                    continue;
                int i = y * w + x;
                if (solid[i] && !anchor[i])
                {
                    anchor[i] = true;
                    marks[i] = stamp;
                    queue[tail++] = i;
                }
            }
            for (int head = 0; head < tail; head++)
            {
                LastVisits++;
                int i = queue[head];
                foreach (int d in delta)
                {
                    int j = i + d;
                    if (solid[j] && marks[j] != stamp)
                    {
                        marks[j] = stamp;
                        queue[tail++] = j;
                    }
                }
            }
            for (int i = 0; i < n; i++)
                if (solid[i] && marks[i] != stamp)
                    solid[i] = false;
            for (int axis = 0; axis < 2; axis++)
            {
                int outer = axis == 0 ? w : h,
                    inner = axis == 0 ? h : w,
                    step = axis == 0 ? w : 1;
                for (int line = 1; line < outer - 1; line++)
                for (int pos = 1; pos < inner - 1; )
                {
                    int start = axis == 0 ? pos * w + line : line * w + pos;
                    if (!solid[start])
                    {
                        pos++;
                        continue;
                    }
                    int length = 0;
                    bool supported = false;
                    while (pos + length < inner - 1 && solid[start + length * step])
                    {
                        supported |= anchor[start + length * step];
                        length++;
                    }
                    pos += length;
                    if (supported)
                        continue;
                    stamp++;
                    for (int j = 0; j < length; j++)
                        cut[start + j * step] = stamp;
                    for (int j = 0; j < length; j++)
                        foreach (int d in delta)
                        {
                            int seed = start + j * step + d;
                            if (!solid[seed] || cut[seed] == stamp || marks[seed] == stamp)
                                continue;
                            int head = 0;
                            tail = 1;
                            queue[0] = seed;
                            marks[seed] = stamp;
                            bool reachesAnchor = false;
                            while (head < tail && !reachesAnchor)
                            {
                                int i = queue[head++];
                                LastVisits++;
                                if (anchor[i])
                                {
                                    reachesAnchor = true;
                                    break;
                                }
                                foreach (int dd in delta)
                                {
                                    int next = i + dd;
                                    if (!solid[next] || cut[next] == stamp)
                                        continue;
                                    if (anchored[next] == stamp)
                                    {
                                        reachesAnchor = true;
                                        break;
                                    }
                                    if (marks[next] != stamp)
                                    {
                                        marks[next] = stamp;
                                        queue[tail++] = next;
                                    }
                                }
                            }
                            if (reachesAnchor)
                            {
                                for (int q = 0; q < tail; q++)
                                    anchored[queue[q]] = stamp;
                            }
                            else if (tail > 6 * length * 4)
                            {
                                for (int q = 0; q < tail; q++)
                                    solid[queue[q]] = false;
                            }
                        }
                }
            }
            // Final support pass also removes necks detached by earlier collapses.
            stamp++;
            tail = 0;
            for (int i = 0; i < n; i++)
                if (solid[i] && anchor[i])
                {
                    marks[i] = stamp;
                    queue[tail++] = i;
                }
            for (int head = 0; head < tail; head++)
            {
                int i = queue[head];
                LastVisits++;
                foreach (int d in delta)
                {
                    int j = i + d;
                    if (solid[j] && marks[j] != stamp)
                    {
                        marks[j] = stamp;
                        queue[tail++] = j;
                    }
                }
            }
            Predicate<long> remove = k => marks[((int)k - minY) * w + (int)(k >> 32) - minX] != stamp;
            if (roof is RoofMask)
                ((RoofMask)roof).RemoveWhere(remove);
            else if (roof is HashSet<long>)
                ((HashSet<long>)roof).RemoveWhere(remove);
            else
            {
                var lost = new List<long>();
                foreach (long k in roof)
                    if (remove(k))
                        lost.Add(k);
                foreach (long k in lost)
                    roof.Remove(k);
            }
            LastMilliseconds = watch.Elapsed.TotalMilliseconds;
            MaxMilliseconds = Math.Max(MaxMilliseconds, LastMilliseconds);
        }
    }
}
