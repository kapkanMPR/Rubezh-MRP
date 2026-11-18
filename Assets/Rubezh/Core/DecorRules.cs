using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class DecorRules
    {
        public static void Bind(StudioMap studio, int columns)
        {
            foreach (var d in studio.Decorations)
            {
                d.Support = null;
                if (!string.IsNullOrEmpty(d.ParentUid))
                    d.Support = studio.Objects.Find(p => p.Uid == d.ParentUid);
                int i = d.Y * columns + d.X;
                if (studio.DecorCells[i] == null)
                    studio.DecorCells[i] = new List<StudioPlaced>();
                studio.DecorCells[i].Add(d);
            }
        }

        public static float Lift(StudioPlaced d) => d.Support != null && !d.Support.Destroyed ? d.Support.Asset.SupportHeight : 0;
        public static float Scale(StudioPlaced d)
        {
            if (d.Support == null)
                return 1;
            return Math.Min(1, d.Support.Asset.SupportRadius * 44 / d.Asset.DecorWidth);
        }

        public static float Depth(StudioPlaced d)
        {
            var p = d.Support ?? d;
            return 256 + p.Y - p.X + (p.Asset.Height - p.Asset.Width) * .5f + (d.Support == null ? d.OffsetY - d.OffsetX : .002f + (d.OffsetY - d.OffsetX) * .0001f);
        }

        public static bool Hit(BattleMap map, StudioPlaced d)
        {
            if (d == null || d.Destroyed || d.Asset.DebrisOnly || !string.IsNullOrEmpty(d.Asset.EquipmentKind))
                return false;
            if (--d.HitsLeft > 0)
                return false;
            d.Destroyed = true;
            map.BrokenCovers.Add(new CoverBreakEvent { Index = d.Y * map.Columns + d.X, RemnantKey = -1 - map.Studio.Decorations.IndexOf(d), HasCenter = true, Decor = true, Destroyed = true, SizePixels = (int)Math.Min(18, Math.Max(4, d.Asset.DecorWidth * .8f)), DebrisKey = d.Asset.DebrisKey, Center = new V2(d.X + .5f + d.OffsetX, d.Y + .5f + d.OffsetY) });
            map.SceneryChanges.Record(map, new[] { map.Tiles.Length + map.Studio.Decorations.IndexOf(d) });
            return true;
        }

        public static void RemoveSupported(BattleMap map, int root)
        {
            if (map.Studio == null)
                return;
            foreach (var d in map.Studio.Decorations)
                if (!d.Destroyed && d.Support != null && d.Support.Root == root)
                {
                    if (!string.IsNullOrEmpty(d.Asset.EquipmentKind))
                    {
                        d.Support = null;
                        d.ParentUid = "";
                        map.SceneryChanges.Record(map, new[] { map.Tiles.Length + map.Studio.Decorations.IndexOf(d) });
                    }
                    else
                    {
                        d.HitsLeft = 1;
                        Hit(map, d);
                    }
                }
        }

        // Nonblocking hit observation: decoration never modifies the projectile collision or damage.
        public static void Trace(BattleMap map, V2 from, float z0, V2 to, float z1, HashSet<StudioPlaced> hit)
        {
            if (map.Studio == null || map.Studio.Decorations.Count == 0)
                return;
            float dx = to.X - from.X, dy = to.Y - from.Y, len = dx * dx + dy * dy;
            if (len < .000001f)
                return;
            int x0 = Math.Max(0, (int)Math.Floor(Math.Min(from.X, to.X) - .5f)), x1 = Math.Min(map.Columns - 1, (int)Math.Floor(Math.Max(from.X, to.X) + .5f)), y0 = Math.Max(0, (int)Math.Floor(Math.Min(from.Y, to.Y) - .5f)), y1 = Math.Min(map.Rows - 1, (int)Math.Floor(Math.Max(from.Y, to.Y) + .5f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var list = map.Studio.DecorCells[y * map.Columns + x];
                    if (list == null)
                        continue;
                    foreach (var d in list)
                    {
                        if (d.Destroyed || hit.Contains(d))
                            continue;
                        float cx = d.X + .5f + d.OffsetX, cy = d.Y + .5f + d.OffsetY, t = Math.Max(0, Math.Min(1, ((cx - from.X) * dx + (cy - from.Y) * dy) / len)), px = from.X + t * dx - cx, py = from.Y + t * dy - cy, scale = Scale(d), radius = d.Asset.DecorRadius * scale, z = z0 + (z1 - z0) * t, baseZ = Lift(d) / 10f;
                        if (px * px + py * py > radius * radius || z < baseZ || z > baseZ + d.Asset.DecorHeight * scale)
                            continue;
                        hit.Add(d);
                        Hit(map, d);
                    }
                }
        }
    }
}
