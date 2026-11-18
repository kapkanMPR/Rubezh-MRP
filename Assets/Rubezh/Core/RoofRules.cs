using System;
using SilentOrders.Rendering;

namespace SilentOrders.Core
{
    public static class RoofRules
    {
        public const float Height = 2.4f;
        public const int LiftPixels = 24;
        public static bool At(BattleMap map, V2 p)
        {
            if(map.DynamicBuildings!=null){bool owned=false;foreach(var h in map.DynamicBuildings)if(h.Contains(p)){owned=true;if(h.Roof.Contains(DynamicBuilding.Key((int)Math.Floor(p.X*8),(int)Math.Floor(p.Y*8))))return true;}if(owned)return false;}
            int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
            return map.Roof != null && map.Inside(x, y) && y * map.Columns + x < map.Roof.Length && map.Roof[y * map.Columns + x];
        }

        public static bool LowArc(BattleMap map, V2 from, V2 to, int kind)
        {
            if (kind > 2 || map.Roof == null)
                return false;
            if (At(map, from) || At(map, to))
                return true;
            // A room can have a hole at the destination, or both endpoints outside.
            int steps = Math.Max(1, (int)Math.Ceiling(V2.Distance(from, to) * 2));
            for (int i = 0; i <= steps; i++)
            {
                var p = from + (to - from) * (i / (float)steps);
                int x = (int)Math.Floor(p.X), y = (int)Math.Floor(p.Y);
                if (!map.Inside(x, y))
                    continue;
                int cell = y * map.Columns + x;
                bool generatedOpening = map.ProceduralCells != null && map.ProceduralCells[cell] && map.Tiles[cell] != Tile.Wall;
                if (!generatedOpening && !ArcOpening.Has(StudioRules.At(map, cell)))
                    continue;
                for (int yy = y - 2; yy <= y + 2; yy++)
                    for (int xx = x - 2; xx <= x + 2; xx++)
                        if (map.Inside(xx, yy) && map.Roof[yy * map.Columns + xx])
                            return true;
            }

            return false;
        }

        public static float ArcHeight(float t, int kind, bool low) => low ? ((kind == 2 ? 1.55f : 1.35f) + .32f * 4f * t * (1f - t)) * Math.Min(1f, (1f - t) * 5f) : ArcedOrdnance.Height(t, kind);
        // Intersect the horizontal ceiling in both directions; crossing a room boundary
        // below it remains legal. A segment above the roof does not enter the room.
        public static bool Crosses(BattleMap map, V2 from, float h0, V2 to, float h1, out V2 hit)
        {
            hit = to;
            if(map.Roof==null)return false;
            int steps=Math.Max(1,(int)Math.Ceiling(V2.Distance(from,to)*16));
            for(int i=0;i<steps;i++) {
                float t0=i/(float)steps,t1=(i+1f)/steps;
                var mid=from+(to-from)*((t0+t1)*.5f);
                if(!At(map,mid))continue;
                DynamicBuilding house=null;
                if(map.DynamicBuildings!=null)foreach(var candidate in map.DynamicBuildings)if(candidate.Contains(mid)){house=candidate;break;}
                var p0=from+(to-from)*t0;var p1=from+(to-from)*t1;
                float z0=house==null?Height:house.RoofHeight(p0),z1=house==null?Height:house.RoofHeight(p1);
                float d0=h0+(h1-h0)*t0-z0,d1=h0+(h1-h0)*t1-z1;
                if(d0*d1>0||Math.Abs(d0-d1)<.000001f)continue;
                float t=t0+(t1-t0)*d0/(d0-d1);var p=from+(to-from)*t;
                if(At(map,p)){hit=p;return true;}
            }
            return false;
        }

        public static bool Window(StudioPlaced o) => o != null && o.Asset.Medium && o.Asset.Name != null && o.Asset.Name.IndexOf("окно", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public interface IRoofGpu
    {
        void RoofDiamond(float x, float y, float halfWidth, float halfHeight);
    }

    public static class RoofDrawing
    {
        public static void Tile(PixelCanvas c, float x, float y, float size=1)
        {
            float px = x * c.Scale, py = (y - RoofRules.LiftPixels) * c.Scale;
            if (px + 11 * size * c.Scale < 0 || px - 11 * size * c.Scale >= c.Width || py + 5 * size * c.Scale < 0 || py - 5 * size * c.Scale >= c.Height)
                return;
            var roofGpu = c.Gpu as IRoofGpu;
            if (roofGpu != null)
            {
                roofGpu.RoofDiamond(px, py, 11 * size * c.Scale, 5 * size * c.Scale);
                return;
            }

            int cx = (int)Math.Round(x * c.Scale), cy = (int)Math.Round((y - RoofRules.LiftPixels) * c.Scale), hh = Math.Max(1, (int)Math.Round(5 * size * c.Scale));
            for (int dy = -hh; dy <= hh; dy++)
            {
                int half = (int)Math.Round(11 * size * c.Scale * (1 - Math.Abs(dy) / (float)hh)), yy = cy + dy, left = cx - half, right = cx + half;
                if (c.Gpu != null)
                {
                    c.Gpu.Rect(left, yy, right - left + 1, 1, 0x1A121110u, 10000, false);
                    continue;
                }

                if (yy < 0 || yy >= c.Height)
                    continue;
                for (int xx = Math.Max(0, left); xx < Math.Min(c.Width, right + 1); xx++)
                {
                    int i = yy * c.Width + xx;
                    uint p = c.Pixels[i];
                    uint r = ((p & 255) * 229 + 160 * 26) / 255, g = (((p >> 8) & 255) * 229 + 170 * 26) / 255, b = (((p >> 16) & 255) * 229 + 180 * 26) / 255;
                    c.Pixels[i] = 0xff000000u | (b << 16) | (g << 8) | r;
                }
            }
        }
    }
}
