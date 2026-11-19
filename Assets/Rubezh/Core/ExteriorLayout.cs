using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Render-only plan. Coordinates outside the map never become simulation cells.
    public sealed class ExteriorLayout
    {
        public struct Exit
        {
            public int Side,
                Start,
                End;
            public float Slope;
            public StudioAsset Surface;
        }

        public readonly List<Exit> Exits = new List<Exit>();
        readonly BattleMap map;
        readonly bool grassLandscape;
        readonly Dictionary<int, StudioAsset> edgeSurfaces = new Dictionary<int, StudioAsset>();
        public readonly string Style;
        public readonly bool Streets;
        public readonly int StreetX,
            StreetY;
        public const int Block = 24,
            StreetWidth = 4;

        public ExteriorLayout(BattleMap map)
        {
            this.map = map;
            int grass = 0;
            if (map.Studio != null)
                foreach (var a in map.Studio.Ground)
                    if (a != null && a.Id.StartsWith("S16-"))
                        grass++;
            grassLandscape = grass > map.Columns * map.Rows / 4;
            string[] styles =
            {
                "",
                "forest",
                "village",
                "road",
                "checkpoint",
                "road",
                "hospital",
                "depot",
                "village",
                "forest",
                "base",
                "town",
                "ruins",
                "park",
                "construction",
                "industrial",
                "industrial",
                "industrial",
                "depot",
                "town",
                "town",
            };
            Style =
                map.ExteriorMission > 0 && map.ExteriorMission < styles.Length ? styles[map.ExteriorMission]
                : map.Theme == 0 ? "forest"
                : map.Theme == 4 ? "park"
                : map.Theme == 5 ? "industrial"
                : map.Theme == 3 ? "depot"
                : map.Theme == 2 ? "town"
                : "village";
            Streets =
                Style == "town"
                || Style == "ruins"
                || Style == "hospital"
                || Style == "construction"
                || Style == "industrial"
                || Style == "depot"
                || Style == "base";
            for (int side = 0; side < 4; side++)
            {
                int length = side < 2 ? map.Rows : map.Columns;
                for (int i = 0; i < length; i++)
                {
                    if (!Road(side, i, 0))
                        continue;
                    int start = i;
                    while (i + 1 < length && Road(side, i + 1, 0))
                        i++;
                    // A broad paved yard is not a road continuing across the entire exterior.
                    int end = i;
                    if (Streets && end - start + 1 > 16)
                        continue;
                    float center = (start + end) * .5f,
                        inner = 0;
                    int count = 0;
                    for (int t = Math.Max(0, start - 3); t <= Math.Min(length - 1, end + 3); t++)
                        if (Road(side, t, 3))
                        {
                            inner += t;
                            count++;
                        }
                    float slope = count == 0 ? 0 : Math.Max(-.5f, Math.Min(.5f, (center - inner / count) / 3f));
                    int idx = EdgeIndex(side, (start + end) / 2, 0);
                    Exits.Add(
                        new Exit
                        {
                            Side = side,
                            Start = start,
                            End = end + 1,
                            Slope = slope,
                            Surface = map.Studio == null ? null : map.Studio.Ground[idx],
                        }
                    );
                }
            }
            // Older road/checkpoint maps painted the entire ground as dirt (kind Ground).
            // Continue the authored entry-to-objective axis when no explicit road exits exist.
            if (Exits.Count == 0 && (Style == "road" || Style == "checkpoint"))
            {
                bool horizontal = Math.Abs(map.Objective.X - map.Entry.X) >= Math.Abs(map.Objective.Y - map.Entry.Y);
                int length = horizontal ? map.Rows : map.Columns;
                float middle = horizontal
                    ? (map.Entry.Y + map.Objective.Y) * .5f
                    : (map.Entry.X + map.Objective.X) * .5f;
                int start = Math.Max(0, Math.Min(length - 4, (int)Math.Floor(middle) - 2));
                for (int side = horizontal ? 0 : 2; side < (horizontal ? 2 : 4); side++)
                {
                    int index = EdgeIndex(side, start + 2, 0);
                    Exits.Add(
                        new Exit
                        {
                            Side = side,
                            Start = start,
                            End = Math.Min(length, start + 4),
                            Surface = map.Studio == null ? null : map.Studio.Ground[index],
                        }
                    );
                }
            }
            int wx = -1,
                wy = -1;
            StreetX = map.Columns / 2;
            StreetY = map.Rows / 2;
            foreach (var e in Exits)
            {
                int w = e.End - e.Start;
                if (e.Side < 2 && w > wy)
                {
                    StreetY = e.Start;
                    wy = w;
                }
                if (e.Side >= 2 && w > wx)
                {
                    StreetX = e.Start;
                    wx = w;
                }
            }
        }

        int EdgeIndex(int side, int t, int depth)
        {
            int x =
                side == 0 ? depth
                : side == 1 ? map.Columns - 1 - depth
                : t;
            int y =
                side == 2 ? depth
                : side == 3 ? map.Rows - 1 - depth
                : t;
            return Math.Max(0, Math.Min(map.Rows - 1, y)) * map.Columns + Math.Max(0, Math.Min(map.Columns - 1, x));
        }

        bool IsRoad(int id)
        {
            var a = map.Studio == null ? null : map.Studio.Ground[id];
            return map.Tiles[id] == Tile.Road
                || (a != null && (a.Id.StartsWith("S22-") || (grassLandscape && a.Id.StartsWith("S05-"))));
        }

        bool Road(int side, int t, int depth)
        {
            return IsRoad(EdgeIndex(side, t, depth));
        }

        public bool RoadAt(float x, float y, float margin, out StudioAsset surface)
        {
            surface = null;
            foreach (var e in Exits)
            {
                float depth =
                    e.Side == 0 ? -x
                    : e.Side == 1 ? x - map.Columns
                    : e.Side == 2 ? -y
                    : y - map.Rows;
                if (depth < 0)
                    continue;
                float t = (e.Side < 2 ? y : x) - e.Slope * depth;
                if (t >= e.Start - margin && t < e.End + margin)
                {
                    surface = e.Surface;
                    return true;
                }
            }
            if (Streets && Math.Max(Math.Max(-x, x - map.Columns), Math.Max(-y, y - map.Rows)) >= 6)
            {
                float X = ((x - StreetX) % Block + Block) % Block,
                    Y = ((y - StreetY) % Block + Block) % Block;
                if (X < StreetWidth + margin || X > Block - margin || Y < StreetWidth + margin || Y > Block - margin)
                {
                    foreach (var e in Exits)
                        if (e.Surface != null)
                        {
                            surface = e.Surface;
                            break;
                        }
                    return true;
                }
            }
            return false;
        }

        public bool LotAllowed(int x, int y, int width = 15, int height = 15)
        {
            if (x < map.Columns + 2 && x + width > -2 && y < map.Rows + 2 && y + height > -2)
                return false;
            for (int yy = y - 1; yy <= y + height; yy++)
            for (int xx = x - 1; xx <= x + width; xx++)
            {
                StudioAsset unused;
                if (RoadAt(xx + .5f, yy + .5f, 1, out unused))
                    return false;
            }
            return true;
        }

        public bool PropAllowed(int x, int y)
        {
            // Reserve the complete building/tree footprint, including its shadow and a verge.
            for (int dy = -1; dy <= 5; dy++)
            for (int dx = -1; dx <= 5; dx++)
            {
                StudioAsset unused;
                if (RoadAt(x + dx + .5f, y + dy + .5f, 1.5f, out unused))
                    return false;
            }
            return true;
        }

        public StudioAsset SurfaceAt(int x, int y)
        {
            StudioAsset surface;
            if (RoadAt(x + .5f, y + .5f, 0, out surface))
                return surface;
            if (map.Studio == null)
                return null;
            int bx = Math.Max(0, Math.Min(map.Columns - 1, x)),
                by = Math.Max(0, Math.Min(map.Rows - 1, y));
            int key = by * map.Columns + bx;
            if (edgeSurfaces.TryGetValue(key, out surface))
                return surface;
            surface = map.Studio.Ground[key];
            // Do not stretch indoor wooden floors, or a road from a clamped corner, into the landscape.
            if (surface != null && !surface.Id.StartsWith("floor-") && !IsRoad(key))
            {
                edgeSurfaces[key] = surface;
                return surface;
            }
            for (int r = 1; r <= 12; r++)
            for (int d = -r; d <= r; d++)
                foreach (int sign in new[] { -1, 1 })
                {
                    int px = Math.Max(0, Math.Min(map.Columns - 1, bx + d)),
                        py = Math.Max(0, Math.Min(map.Rows - 1, by + sign * r));
                    int id = py * map.Columns + px;
                    var a = map.Studio.Ground[id];
                    if (a != null && !a.Id.StartsWith("floor-") && !IsRoad(id))
                    {
                        edgeSurfaces[key] = a;
                        return a;
                    }
                }
            edgeSurfaces[key] = null;
            return null;
        }

        public int TreesNear(int x, int y)
        {
            int bx = Math.Max(0, Math.Min(map.Columns - 1, x)),
                by = Math.Max(0, Math.Min(map.Rows - 1, y)),
                n = 0;
            if (map.Studio != null)
                foreach (var o in map.Studio.Objects)
                    if (
                        o.Asset != null
                        && (
                            o.Asset.Id == "legacy-101"
                            || o.Asset.Id == "legacy-102"
                            || o.Asset.Id == "legacy-103"
                            || o.Asset.Id == "legacy-104"
                        )
                        && Math.Abs(o.X - bx) <= 8
                        && Math.Abs(o.Y - by) <= 8
                    )
                        n++;
            return n;
        }
    }
}
