using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Exact coordinates, ordered endpoints; no rounding and no cached smoke/knowledge.
    // Per-map lifetime and revision invalidation keep destruction authoritative.
    public sealed class GeometryQueryCache
    {
        struct Key : IEquatable<Key>
        {
            readonly float ax, ay, bx, by;
            public Key(V2 a, V2 b)
            {
                ax = a.X;
                ay = a.Y;
                bx = b.X;
                by = b.Y;
            }

            public bool Equals(Key o)
            {
                return ax == o.ax && ay == o.ay && bx == o.bx && by == o.by;
            }

            public override bool Equals(object o)
            {
                return o is Key && Equals((Key)o);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = ax.GetHashCode();
                    h = h * 397 ^ ay.GetHashCode();
                    h = h * 397 ^ bx.GetHashCode();
                    return h * 397 ^ by.GetHashCode();
                }
            }
        }

        readonly Dictionary<Key, bool> sight = new Dictionary<Key, bool>();
        readonly Dictionary<Key, float> protection = new Dictionary<Key, float>();
        // Terrain is shared by all observers. Cache only inside a synchronous tick;
        // editor/test callers may edit Tiles directly between steps.
        bool inTick;
        byte[] near = Array.Empty<byte>();
        int[] walls = Array.Empty<int>();
        int terrainRevision, misses;
        bool haveWalls;
        internal void BeginTick(BattleMap map)
        {
            inTick = true;
            ResetTerrain(map);
        }

        internal void EndTick()
        {
            inTick = false;
        }

        void ResetTerrain(BattleMap map)
        {
            terrainRevision = map.Revision;
            haveWalls = false;
            misses = 0;
            if (near.Length != map.Tiles.Length)
                near = new byte[map.Tiles.Length];
            else
                Array.Clear(near, 0, near.Length);
        }

        internal bool Near(BattleMap map, V2 p)
        {
            int x = (int)p.X, y = (int)p.Y;
            if (!inTick || !map.Inside(x, y))
                return map.NearCoverRaw(p);
            if (terrainRevision != map.Revision)
                ResetTerrain(map);
            int i = y * map.Columns + x;
            if (near[i] == 0)
                near[i] = (byte)(map.NearCoverRaw(p) ? 2 : 1);
            return near[i] == 2;
        }

        bool NoWalls(BattleMap m, V2 a, V2 b)
        {
            if(m.DynamicBuildings!=null)return false;
            if (!inTick)
                return false;
            if (terrainRevision != m.Revision)
                ResetTerrain(m);
            int x = (int)Math.Floor(Math.Min(a.X, b.X)), y = (int)Math.Floor(Math.Min(a.Y, b.Y));
            int r = (int)Math.Floor(Math.Max(a.X, b.X)) + 1, bot = (int)Math.Floor(Math.Max(a.Y, b.Y)) + 1;
            if (x < 0 || y < 0 || r > m.Columns || bot > m.Rows)
                return false;
            int stride = m.Columns + 1;
            if (!haveWalls)
            {
                if (++misses < 32)
                    return false;
                int size = stride * (m.Rows + 1);
                if (walls.Length != size)
                    walls = new int[size];
                Array.Clear(walls, 0, stride);
                for (int j = 0; j < m.Rows; j++)
                {
                    int row = 0, offset = (j + 1) * stride;
                    walls[offset] = 0;
                    for (int i = 0; i < m.Columns; i++)
                    {
                        if (m.Tiles[j * m.Columns + i] == Tile.Wall)
                            row++;
                        walls[offset + i + 1] = walls[offset - stride + i + 1] + row;
                    }
                }

                haveWalls = true;
            }

            return walls[bot * stride + r] - walls[bot * stride + x] == walls[y * stride + r] - walls[y * stride + x];
        }

        int revision = -1, columns = -1, rows = -1;
        void Check(BattleMap map)
        {
            if (revision != map.Revision || columns != map.Columns || rows != map.Rows)
            {
                revision = map.Revision;
                columns = map.Columns;
                rows = map.Rows;
                sight.Clear();
                protection.Clear();
            }
        }

        public bool Sight(BattleMap map, V2 a, V2 b)
        {
            Check(map);
            var key = new Key(a, b);
            bool result;
            if (sight.TryGetValue(key, out result))
                return result;
            result = NoWalls(map, a, b) || map.LineOfSightRaw(a, b);
            if (sight.Count >= 32768)
                sight.Clear();
            sight[key] = result;
            return result;
        }

        public float Protection(BattleMap map, V2 a, V2 b)
        {
            Check(map);
            var key = new Key(a, b);
            float result;
            if (protection.TryGetValue(key, out result))
                return result;
            result = map.ProtectionRaw(a, b);
            if (protection.Count >= 16384)
                protection.Clear();
            protection[key] = result;
            return result;
        }
    }
}
