using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    public abstract partial class BattleVisualRecipes
    {
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        protected void EnsureScenery(BattleMap map)
        {
            if (
                cachedMap == map
                && cachedRevision == map.Revision
                && sceneryDepth != null
                && cachedSharpScenery == SharpScenery
            )
                return;
            var sink = Canvas.Gpu;
            Canvas.Gpu = null;
            try
            {
                CacheScenery(map);
            }
            finally
            {
                Canvas.Gpu = sink;
            }
        }
#endif

        protected uint[] basePixels;
        protected float[] sceneryDepth;
        protected List<Item> sceneryItems;
        protected readonly Dictionary<int, Area> sceneryBounds = new Dictionary<int, Area>();
        public int FullSceneryBuilds { get; private set; }
        public int PartialSceneryBuilds { get; private set; }
        public int LastSceneryPixels { get; private set; }

        protected struct Area
        {
            public int X,
                Y,
                R,
                B;

            public bool Intersects(Area a) => X < a.R && R > a.X && Y < a.B && B > a.Y;

            public void Include(Area a)
            {
                X = Math.Min(X, a.X);
                Y = Math.Min(Y, a.Y);
                R = Math.Max(R, a.R);
                B = Math.Max(B, a.B);
            }
        }

        Area Bounds(BattleMap m, Item item)
        {
            var o = item.Decoration ?? StudioRules.At(m, item.TileId);
            var p = P(o == null ? m.Center(item.TileId) : new V2(o.X, o.Y));
            float left = p.X - 35,
                right = p.X + 35,
                top = p.Y - 90,
                bottom = p.Y + 35;
            if (o != null)
            {
                left = right = p.X;
                top = bottom = p.Y;
                Extend(StudioCatalog.Sprite(o), o, p, ref left, ref top, ref right, ref bottom);
                if (!o.Asset.Legacy && !o.Asset.Decor)
                    foreach (var cap in StudioCatalog.CapSprites(o))
                        Extend(cap, o, p, ref left, ref top, ref right, ref bottom);
            }
            else
            {
                int prop = m.LevelProps == null ? 0 : m.LevelProps[item.TileId];
                int w = 100,
                    h = 150;
                if (prop != 0)
                    Rubezh.Graphics.PropSize(prop, out w, out h);
                float radius = (float)Math.Sqrt(w * w + h * h) * 2.5f + 12;
                left = p.X - radius;
                right = p.X + radius;
                top = p.Y - radius;
                bottom = p.Y + radius;
            }

            return new Area
            {
                X = Math.Max(0, (int)Math.Floor(left * Canvas.Scale) - 4),
                Y = Math.Max(0, (int)Math.Floor(top * Canvas.Scale) - 4),
                R = Math.Min(Canvas.Width, (int)Math.Ceiling(right * Canvas.Scale) + 4),
                B = Math.Min(Canvas.Height, (int)Math.Ceiling(bottom * Canvas.Scale) + 4),
            };
        }

        static void Extend(StudioSprite s, StudioPlaced o, V2 p, ref float l, ref float t, ref float r, ref float b)
        {
            float cx = p.X + s.X,
                cy = p.Y + s.Y,
                hw = Math.Abs(s.W) / 2,
                hh = Math.Abs(s.H) / 2;
            if (o.Asset.Decor)
            {
                float f = DecorRules.Scale(o);
                cx = p.X + 11 + (s.X - 11) * f + 11 * (o.OffsetX + o.OffsetY);
                cy = p.Y + s.Y * f - DecorRules.Lift(o) + 5 * (o.OffsetY - o.OffsetX);
                hw *= Math.Abs(f);
                hh *= Math.Abs(f);
            }
            else if (o.Asset.Legacy && !(o.Asset.Kind >= 109 && o.Asset.Kind <= 112))
            {
                float a = (o.Asset.Mirrored ? -o.Angle : o.Angle) * (float)Math.PI / 180,
                    cos = (float)Math.Cos(a),
                    sin = (float)Math.Sin(a),
                    dx = (s.X - 11) * o.ScaleX,
                    dy = s.Y * o.ScaleY;
                cx = p.X + 11 + dx * cos - dy * sin;
                cy = p.Y + dx * sin + dy * cos;
                float w = hw * Math.Abs(o.ScaleX),
                    h = hh * Math.Abs(o.ScaleY);
                hw = w * Math.Abs(cos) + h * Math.Abs(sin);
                hh = w * Math.Abs(sin) + h * Math.Abs(cos);
                // Wear shrinks around the ground anchor, so include the anchor as well.
                l = Math.Min(l, p.X + 11);
                r = Math.Max(r, p.X + 11);
                t = Math.Min(t, p.Y);
                b = Math.Max(b, p.Y);
            }
            l = Math.Min(l, cx - hw);
            r = Math.Max(r, cx + hw);
            t = Math.Min(t, cy - hh);
            b = Math.Max(b, cy + hh);
        }

        void RememberBounds(BattleMap map)
        {
            sceneryBounds.Clear();
            foreach (var item in sceneryItems)
                sceneryBounds[item.TileId] = Bounds(map, item);
            LastSceneryPixels = Canvas.Width * Canvas.Height;
        }

#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        void CopyRegion<T>(T[] from, T[] to, Area a)
        {
            for (int y = a.Y; y < a.B; y++)
                Array.Copy(from, y * Canvas.Width + a.X, to, y * Canvas.Width + a.X, a.R - a.X);
        }
#endif

#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        bool TryPatchScenery(BattleMap map) => PatchChunks(map);
#endif
    }
}
