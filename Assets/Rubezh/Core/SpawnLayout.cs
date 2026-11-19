using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Runs once after level/loadout/formation setup, before the first visible frame.
    public static class SpawnLayout
    {
        public static void Apply(BattleSimulation b)
        {
            var occupied = new List<V2>();
            var axis = PositionLogic.Axis(b);
            var lateral = new V2(-axis.Y, axis.X);
            V2 anchor = b.Map.Entry;
            foreach (var s in b.Soldiers)
                if (s.Alive && s.Deployed && s.Side == Side.Green)
                {
                    anchor = s.Position;
                    break;
                }
            for (int side = 0; side < 2; side++)
                foreach (var s in b.Soldiers)
                {
                    if (!s.Alive || !s.Deployed || (s.Side == Side.Green) != (side == 1))
                        continue;
                    V2 wanted = s.Position;
                    if (s.Side == Side.Green)
                    {
                        var q = b.Team(s);
                        var f = q.Formation;
                        int slot = s.Id - 1;
                        if (f != null && f.Valid())
                            for (int i = 0; i < 4; i++)
                                if (f.DisplaySlot(i) == s.Id)
                                {
                                    slot = i;
                                    break;
                                }
                        if (s.Id == q.LeaderId)
                            wanted = anchor;
                        else
                        {
                            // Two pairs, each with a front/back slot; manual role offsets remain meaningful.
                            float lane = (slot < 2 ? -1 : 1) * 2.2f * FormationAI.LateralMultiplier(q);
                            float depth =
                                ((slot % 2 == 0 ? .8f : -1.1f) - 2.4f * FormationAI.Position(q, s))
                                * FormationAI.DepthMultiplier(q);
                            wanted = anchor + lateral * lane + axis * depth;
                        }
                    }
                    var point = Find(b, s.Position, wanted, occupied);
                    s.Position = s.Destination = s.OrderAnchor = point;
                    s.Path.Clear();
                    s.PathCursor = 0;
                    occupied.Add(point);
                }
        }

        static V2 Find(BattleSimulation b, V2 origin, V2 wanted, List<V2> occupied)
        {
            var m = b.Map;
            var start = m.NearestOpen(origin);
            int cell = m.Index(start);
            var seen = new bool[m.Columns * m.Rows];
            var queue = new Queue<int>();
            queue.Enqueue(cell);
            seen[cell] = true;
            float bestScore = float.MaxValue;
            V2 best = start;
            bool found = false;
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                var p = m.Center(n);
                bool free = true;
                foreach (var used in occupied)
                    if (V2.Distance(p, used) < 1.5f)
                    {
                        free = false;
                        break;
                    }
                float score = V2.Distance(p, wanted);
                if (free && score < bestScore)
                {
                    best = p;
                    bestScore = score;
                    found = true;
                }
                int x = n % m.Columns,
                    y = n / m.Columns;
                for (int d = 0; d < 4; d++)
                {
                    int nx =
                        x
                        + (
                            d == 0 ? 1
                            : d == 1 ? -1
                            : 0
                        );
                    int ny =
                        y
                        + (
                            d == 2 ? 1
                            : d == 3 ? -1
                            : 0
                        );
                    if (nx < 0 || ny < 0 || nx >= m.Columns || ny >= m.Rows)
                        continue;
                    int next = ny * m.Columns + nx;
                    if (seen[next] || !m.Walkable(nx, ny))
                        continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            if (!found)
                throw new InvalidOperationException(
                    "Недостаточно свободного места для размещения бойцов без наложений."
                );
            return best;
        }
    }
}
