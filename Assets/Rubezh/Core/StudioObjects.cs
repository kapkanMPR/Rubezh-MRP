using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class StudioAsset
    {
        public string EquipmentKind;
        public int EquipmentVariant;
        public string DebrisKey;
        public bool DebrisOnly;
        public string[] DebrisExtraKeys;
        public string Id,
            Name,
            Group,
            Material,
            DisplayMaterial,
            Partner;
        public bool NoCover,
            Mirrored;
        public int BulletLimit;
        public bool Decor;
        public int DecorHits;
        public float SupportHeight,
            SupportRadius,
            DecorRadius,
            DecorHeight,
            DecorWidth;
        public int MediumMask;
        public int Width,
            Height,
            Kind,
            Strength,
            DebrisProp,
            DebrisStyle,
            Masks;
        public bool Legacy,
            Ground,
            Blocking,
            Partial,
            Indestructible,
            Medium,
            High,
            Archived;
        public int[][] Dirs;
        public float[] EndHeights;
        public float WallHeight,
            Length;
        public StudioSprite[] Sprites;
        public StudioCap[] Caps;

        public StudioAsset Clone() => (StudioAsset)MemberwiseClone();
    }

    public struct StudioCap
    {
        public int Port,
            Parity;
        public float Height;
        public StudioSprite Sprite;

        public StudioCap(int p, int parity, float h, StudioSprite s)
        {
            Port = p;
            Parity = parity;
            Height = h;
            Sprite = s;
        }
    }

    public struct StudioSprite
    {
        public int Id;
        public float X,
            Y,
            W,
            H;

        public StudioSprite(int id, float x, float y, float w, float h)
        {
            Id = id;
            X = x;
            Y = y;
            W = w;
            H = h;
        }
    }

    public sealed class StudioPlaced
    {
        public string Uid,
            ParentUid;
        public StudioPlaced Support;
        public float OffsetX,
            OffsetY;
        public int HitsLeft;
        public StudioAsset Asset;
        public int X,
            Y,
            Mask;
        public bool Destroyed;
        public int Root;
        public float Angle,
            ScaleX = 1,
            ScaleY = 1;
        public float[] Shared;
        public StudioPlaced[] Neighbors;
        public int[] NeighborPorts;
    }

    public sealed class StudioMap
    {
        public readonly List<StudioPlaced> Objects = new List<StudioPlaced>();
        public readonly List<StudioPlaced> Decorations = new List<StudioPlaced>();
        public List<StudioPlaced>[] DecorCells;
        public StudioPlaced[] Cells;
        public StudioAsset[] Ground;

        public StudioMap(int count)
        {
            Cells = new StudioPlaced[count];
            DecorCells = new List<StudioPlaced>[count];
            Ground = new StudioAsset[count];
        }

        public void Connect()
        {
            var sockets = new Dictionary<string, List<Tuple<StudioPlaced, int>>>();
            foreach (var o in Objects)
            {
                o.Mask = 0;
                o.Shared = o.Asset.Dirs == null ? null : new float[o.Asset.Dirs.Length];
                o.Neighbors = o.Asset.Dirs == null ? null : new StudioPlaced[o.Asset.Dirs.Length];
                o.NeighborPorts = o.Asset.Dirs == null ? null : new int[o.Asset.Dirs.Length];
            }

            foreach (var o in Objects)
            {
                if (o.Destroyed || o.Asset.Dirs == null)
                    continue;
                var a = o.Asset;
                for (int i = 0; i < a.Dirs.Length; i++)
                {
                    var d = a.Dirs[i];
                    float len = a.Length / 2,
                        cx = o.X + a.Width / 2f,
                        cy = o.Y + a.Height / 2f;
                    string key =
                        a.Material
                        + ":"
                        + a.WallHeight
                        + ":"
                        + (int)Math.Round((cx + d[0] * len) * 2)
                        + ","
                        + (int)Math.Round((cy + d[1] * len) * 2);
                    List<Tuple<StudioPlaced, int>> list;
                    if (!sockets.TryGetValue(key, out list))
                        sockets[key] = list = new List<Tuple<StudioPlaced, int>>();
                    foreach (var entry in list)
                    {
                        var other = entry.Item1;
                        var od = other.Asset.Dirs[entry.Item2];
                        if (d[0] == -od[0] && d[1] == -od[1])
                        {
                            o.Mask |= 1 << i;
                            other.Mask |= 1 << entry.Item2;
                            o.Neighbors[i] = other;
                            o.NeighborPorts[i] = entry.Item2;
                            other.Neighbors[entry.Item2] = o;
                            other.NeighborPorts[entry.Item2] = i;
                            o.Shared[i] = other.Shared[entry.Item2] = Math.Min(
                                a.EndHeights[i],
                                other.Asset.EndHeights[entry.Item2]
                            );
                        }
                    }

                    list.Add(Tuple.Create(o, i));
                }
            }
        }
    }

    public static partial class StudioCatalog
    {
        public static readonly Dictionary<string, StudioAsset> Assets = Create();

        public static IEnumerable<StudioSprite> CapSprites(StudioPlaced o)
        {
            if (o.Asset.Caps == null || o.Shared == null)
                yield break;
            int parity = (o.X & 1) | ((o.Y & 1) << 1);
            foreach (var c in o.Asset.Caps)
                if (
                    c.Parity == parity
                    && (o.Mask & (1 << c.Port)) != 0
                    && Math.Abs(c.Height - o.Shared[c.Port]) < .0001f
                )
                    yield return c.Sprite;
        }

        static readonly Dictionary<string, StudioAsset> reflections = new Dictionary<string, StudioAsset>();

        public static StudioAsset Find(string id, bool mirrored)
        {
            var a = Find(id);
            if (a == null || !mirrored)
                return a;
            StudioAsset b;
            if (reflections.TryGetValue(id, out b))
                return b;
            b = a.Clone();
            b.Mirrored = true;
            b.Width = a.Height;
            b.Height = a.Width;
            Func<StudioSprite, StudioSprite> flip = s => new StudioSprite(
                s.Id,
                11 * (a.Width + a.Height) - s.X,
                s.Y + 5 * (a.Width - a.Height),
                -s.W,
                s.H
            );
            b.Sprites = Array.ConvertAll(a.Sprites, s => flip(s));
            b.Caps =
                a.Caps == null
                    ? null
                    : Array.ConvertAll(a.Caps, v => new StudioCap(v.Port, v.Parity, v.Height, flip(v.Sprite)));
            b.Dirs = a.Dirs == null ? null : Array.ConvertAll(a.Dirs, d => new[] { -d[1], -d[0] });
            b.MediumMask = 0;
            for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if ((a.MediumMask & (1 << (y * a.Width + x))) != 0)
                    b.MediumMask |= 1 << ((a.Width - 1 - x) * b.Width + a.Height - 1 - y);
            reflections[id] = b;
            return b;
        }

        public static StudioAsset Find(string id)
        {
            StudioAsset a;
            return id != null && Assets.TryGetValue(id, out a) ? a : null;
        }

        public static StudioSprite Sprite(StudioPlaced o)
        {
            var a = o.Asset;
            int parity = a.Dirs == null ? 0 : ((o.X & 1) | ((o.Y & 1) << 1));
            var sp = a.Sprites[parity * a.Masks + o.Mask];
            if (o.Mask != 0 && a.Dirs != null)
            {
                bool left = false,
                    right = false;
                for (int i = 0; i < a.Dirs.Length; i++)
                    if ((o.Mask & (1 << i)) != 0 && o.Shared != null && o.Shared[i] > 0)
                    {
                        int side = a.Dirs[i][0] + a.Dirs[i][1];
                        left |= side < 0;
                        right |= side > 0;
                    }

                float l = left ? .38f : 0f,
                    r = right ? .38f : 0f;
                sp.X += (r - l) * .5f;
                sp.W += (sp.W < 0 ? -1 : 1) * (l + r);
            }

            return sp;
        }
    }

    public static class StudioRules
    {
        public static StudioPlaced At(BattleMap m, int i) =>
            m.Studio != null && i >= 0 && i < m.Studio.Cells.Length ? m.Studio.Cells[i] : null;

        public static bool Obstacle(BattleMap m, int i)
        {
            var o = At(m, i);
            return o != null && !o.Destroyed && o.Asset.NoCover && o.Asset.Blocking;
        }

        public static bool Medium(BattleMap m, int i)
        {
            if (m.ProceduralCells != null && i >= 0 && i < m.ProceduralCells.Length && m.ProceduralCells[i])
                return m.ProceduralProtection[i] > .5f;
            var o = At(m, i);
            return o != null && !o.Destroyed && MediumCell(o.Asset, i % m.Columns - o.X, i / m.Columns - o.Y)
                || m.VehicleFootprint != null && i >= 0 && i < m.VehicleFootprint.Length && m.VehicleFootprint[i] != 0;
        }

        public static bool MediumCell(StudioAsset a, int x, int y) =>
            a.Medium || (a.MediumMask & (1 << (y * a.Width + x))) != 0;

        public static Tile Cell(StudioAsset a, int x, int y) =>
            !a.Blocking || a.NoCover ? Tile.Ground
            : MediumCell(a, x, y) ? Tile.LowCover
            : a.High ? Tile.Wall
            : Tile.LowCover;

        public static bool Partial(BattleMap m, int i)
        {
            var o = At(m, i);
            return o != null ? !o.Destroyed && o.Asset.Partial : m.IsBush(i);
        }

        public static bool Permanent(BattleMap m, int i)
        {
            if (m.ProceduralCells != null && i >= 0 && i < m.ProceduralCells.Length && m.ProceduralCells[i])
                return true;
            var o = At(m, i);
            return o != null && o.Asset.Indestructible;
        }

        public static int Root(BattleMap m, int i)
        {
            var o = At(m, i);
            return o == null ? i : o.Root;
        }

        public static float Height(BattleMap m, int i, float fallback) =>
            m.ProceduralCells != null && i >= 0 && i < m.ProceduralCells.Length && m.ProceduralCells[i]
                ? m.ProceduralHeight[i]
            : Medium(m, i) ? 1.8f
            : fallback;

        public static void Health(BattleMap m, int index, float value)
        {
            var o = At(m, index);
            if (o == null)
            {
                m.CoverHealth[index] = value;
                return;
            }

            for (int y = o.Y; y < o.Y + o.Asset.Height; y++)
            for (int x = o.X; x < o.X + o.Asset.Width; x++)
                m.CoverHealth[y * m.Columns + x] = value;
        }

        public static int[] Remove(BattleMap m, int index)
        {
            var o = At(m, index);
            if (o == null)
                return new[] { index };
            var changed = new List<int> { o.Root };
            o.Destroyed = true;
            for (int y = o.Y; y < o.Y + o.Asset.Height; y++)
            for (int x = o.X; x < o.X + o.Asset.Width; x++)
            {
                int i = y * m.Columns + x;
                m.Tiles[i] = Tile.Rubble;
                m.CoverHealth[i] = 0;
                m.LevelProps[i] = m.LowCoverStyle[i] = 0;
            } // Walls are immutable. Only directly attached removable fence neighbours can change.

            if (o.Neighbors != null)
                for (int p = 0; p < o.Neighbors.Length; p++)
                {
                    var neighbor = o.Neighbors[p];
                    if (neighbor == null || neighbor.Destroyed)
                        continue;
                    int q = o.NeighborPorts[p];
                    neighbor.Mask &= ~(1 << q);
                    neighbor.Shared[q] = 0;
                    neighbor.Neighbors[q] = null;
                    changed.Add(neighbor.Root);
                }

            return changed.ToArray();
        }
    }
}
