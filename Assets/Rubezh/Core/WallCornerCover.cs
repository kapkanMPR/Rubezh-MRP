using System;

namespace SilentOrders.Core
{
    // Exposed convex corners are firing cover; the underlying cell remains a high wall.
    public static class WallCornerCover
    {
        public const float Protection = .75f;

        public sealed class Cache
        {
            public readonly int Revision,
                Columns,
                Rows;
            public readonly byte[] Masks;

            public Cache(BattleMap map)
            {
                Revision = map.Revision;
                Columns = map.Columns;
                Rows = map.Rows;
                Masks = new byte[Columns * Rows];
                for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Columns; x++)
                {
                    if (!map.Walkable(x, y))
                        continue;
                    int bit = 0;
                    for (int sy = -1; sy <= 1; sy += 2)
                    for (int sx = -1; sx <= 1; sx += 2, bit++)
                    {
                        if (!map.Inside(x + sx, y + sy) || map.At(new V2(x + sx + .5f, y + sy + .5f)) != Tile.Wall)
                            continue;
                        if (map.Walkable(x + sx, y) && map.Walkable(x, y + sy))
                            Masks[y * Columns + x] |= (byte)(1 << bit);
                    }
                }
            }
        }

        public static bool At(BattleMap map, V2 position, V2 threat)
        {
            if (!map.Walkable(position))
                return false;
            V2 direction = (threat - position).Normalized;
            if (direction.Length < .1f)
                return false;
            int px = (int)Math.Floor(position.X),
                py = (int)Math.Floor(position.Y);
            var cache = map.CornerCache;
            if (
                cache == null
                || cache.Revision != map.Revision
                || cache.Columns != map.Columns
                || cache.Rows != map.Rows
            )
            {
                cache = new Cache(map);
                map.CornerCache = cache;
            }

            byte mask = cache.Masks[py * map.Columns + px];
            if (mask == 0)
                return false;
            int bit = 0;
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sx = -1; sx <= 1; sx += 2, bit++)
            {
                if ((mask & (1 << bit)) == 0)
                    continue;
                V2 corner = new V2(px + (sx > 0 ? 1 : 0), py + (sy > 0 ? 1 : 0));
                V2 offset = position - corner;
                if (offset.X * (-sx) < .1f || offset.Y * (-sy) < .1f || offset.Length > .85f)
                    continue;
                V2 toward = corner - position;
                float along = V2.Dot(toward, direction);
                if (along <= .1f || V2.Distance(position, threat) < along)
                    continue;
                float lateral = Math.Abs(toward.X * direction.Y - toward.Y * direction.X);
                if (lateral <= .6f)
                    return true;
            }

            return false;
        }
    }
}
