using System;
using SilentOrders.Rendering;

namespace SilentOrders.Core
{
    public static class StudioDrawing
    {
        public static void Draw(PixelCanvas canvas, StudioPlaced o, float x, float y, float wear = 1)
        {
            var s = StudioCatalog.Sprite(o);
            if (o.Asset.Decor)
            {
                float factor = DecorRules.Scale(o), lift = DecorRules.Lift(o);
                s.X = 11 + (s.X - 11) * factor + 11 * (o.OffsetX + o.OffsetY);
                s.Y = s.Y * factor - lift + 5 * (o.OffsetY - o.OffsetX);
                s.W *= factor;
                s.H *= factor;
                DrawSprite(canvas, s, x, y);
                return;
            }

            if (o.Asset.Kind >= 109 && o.Asset.Kind <= 112 && o.Asset.Legacy)
            {
                DrawSprite(canvas, s, x, y);
                return;
            }

            if (o.Asset.Legacy)
            {
                float sx = o.ScaleX * wear, sy = o.ScaleY * wear, angle = (o.Asset.Mirrored ? -o.Angle : o.Angle) * (float)Math.PI / 180, dx = (s.X - 11) * sx, dy = s.Y * sy;
                Rubezh.Graphics.StudioSprite(canvas, s.Id, x + 11 + dx * (float)Math.Cos(angle) - dy * (float)Math.Sin(angle), y + dx * (float)Math.Sin(angle) + dy * (float)Math.Cos(angle), s.W * sx, s.H * sy, angle);
                return;
            }

            DrawSprite(canvas, s, x, y);
            foreach (var cap in StudioCatalog.CapSprites(o))
                DrawSprite(canvas, cap, x, y);
        }

        public static void DrawSprite(PixelCanvas canvas, StudioSprite s, float x, float y)
        {
            // Preserve half-pixel crop extents: integer rounding would stretch every wall independently.
            Rubezh.Graphics.StudioSprite(canvas, s.Id, x + s.X, y + s.Y, s.W, s.H);
        }
    }
}
