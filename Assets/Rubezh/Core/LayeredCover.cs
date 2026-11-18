using System;

namespace SilentOrders.Core
{
    // Shooter-side aiming only. PositionLogic keeps its original shelter radius.
    public static class LayeredCover
    {
        public const int MaximumObjects = 3;
        public static float Radius(float distance) => 1.55f + .2f * (float)Math.Floor(Math.Max(0, distance - 10) / 2);
        public static float Protection(BattleMap map, V2 target, V2 shooter, float virtualCover = 0, bool crouched = false, bool corners = true)
        {
            float range = V2.Distance(target, shooter);
            if (range < .01f)
                return map.Protection(target, shooter, corners);
            V2 dir = (shooter - target).Normalized;
            float radius = Radius(range), sum = 0, weight = 1;
            int count = 0;
            bool replacedPosture = false;
            // Corner cover is a primary object even when the ray just misses its wall cell.
            bool corner = corners && WallCornerCover.At(map, target, shooter);
            float stancePrimary = 0;
            if (crouched)
            {
                stancePrimary = corner ? WallCornerCover.Protection + .15f : virtualCover > 0 ? virtualCover + .15f : .3f;
                sum = stancePrimary;
                count = 1;
                weight = .35f;
            }
            else if (virtualCover > 0)
            {
                sum = corner ? Math.Max(virtualCover, WallCornerCover.Protection) : virtualCover;
                count = 1;
                weight = .35f;
            }
            else if (corner)
            {
                sum = WallCornerCover.Protection;
                count = 1;
                weight = .35f;
            }

            int x = (int)Math.Floor(target.X), y = (int)Math.Floor(target.Y), sx = dir.X >= 0 ? 1 : -1, sy = dir.Y >= 0 ? 1 : -1;
            float dx = Math.Abs(dir.X) < .000001f ? float.MaxValue : Math.Abs(1 / dir.X), dy = Math.Abs(dir.Y) < .000001f ? float.MaxValue : Math.Abs(1 / dir.Y);
            float tx = dx == float.MaxValue ? dx : ((sx > 0 ? x + 1 : x) - target.X) / dir.X, ty = dy == float.MaxValue ? dy : ((sy > 0 ? y + 1 : y) - target.Y) / dir.Y;
            var visited = new System.Collections.Generic.HashSet<int>();
            float entry = 0;
            while (entry < range && map.Inside(x, y))
            {
                float exit = Math.Min(range, Math.Min(tx, ty));
                Tile tile = map.At(new V2(x + .5f, y + .5f));
                if (exit - entry > .00001f && (tile == Tile.LowCover || tile == Tile.Wall) && visited.Add(StudioRules.Root(map, y * map.Columns + x)))
                {
                    bool replaceVirtual = (virtualCover > 0 || crouched && !replacedPosture) && count == 1 && entry <= radius + .00001f;
                    bool primary = (count == 0 || replaceVirtual) && entry <= radius + .00001f;
                    if (!(corner && entry <= 1.55f) && (primary || range - exit > 5))
                    {
                        if (replaceVirtual)
                        {
                            sum = Math.Max(virtualCover, StudioRules.Medium(map, y * map.Columns + x) ? .65f : tile == Tile.LowCover ? .50f : .75f) + (crouched ? .15f : 0);
                            stancePrimary = sum;
                            replacedPosture = true;
                            virtualCover = 0;
                            weight = .35f;
                        }
                        else
                        {
                            if (count == 0 && !primary)
                                weight = .35f;
                            sum += (crouched ? stancePrimary : (StudioRules.Medium(map, y * map.Columns + x) ? .65f : tile == Tile.LowCover ? .50f : .75f)) * weight;
                            weight *= .35f;
                            if (++count >= MaximumObjects)
                                break;
                        }
                    }
                }

                entry = exit;
                if (tx < ty)
                {
                    x += sx;
                    tx += dx;
                }
                else if (ty < tx)
                {
                    y += sy;
                    ty += dy;
                }
                else
                {
                    x += sx;
                    y += sy;
                    tx += dx;
                    ty += dy;
                }
            }

            return Util.Clamp(sum, 0, 1);
        }
    }
}
