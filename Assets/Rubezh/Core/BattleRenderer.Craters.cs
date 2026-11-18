using System;
using SilentOrders.Core;
namespace SilentOrders.Rendering
{
    public interface ICraterGpu
    {
        void Crater(float x, float y, float width, float height, float depth, bool mirror);
    }
    public static class CraterArt
    {
        // Top-down rows and premultiplied RGBA. Shared by every crater, both sizes.
        public static uint[] Pixels;
        public static int Width, Height;
    }
    public abstract partial class BattleVisualRecipes
    {
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        protected void DrawCraters(BattleSimulation b)
        {
            if (Canvas.Gpu is INativeEffectsGpu effects && effects.OwnsBattleEffects) return;
            if (CraterArt.Pixels == null) return;
            var gpu = Canvas.Gpu as ICraterGpu;
            foreach (var crater in b.Craters)
            {
                if (!b.Fog.Revealed(crater.Position)) continue;
                var p = P(crater.Position);
                float width = crater.Cells * 22, height = crater.Cells * 10;
                float depth = 256 + crater.Position.Y - crater.Position.X + .015f;
                if (gpu != null) { gpu.Crater(p.X, p.Y, width, height, depth, crater.Mirror); continue; }
                if (Canvas.Gpu != null) continue;
                float left = (p.X - width / 2) * Canvas.Scale, top = (p.Y - height / 2) * Canvas.Scale;
                float w = width * Canvas.Scale, h = height * Canvas.Scale;
                int x0 = Math.Max(Canvas.ClipLeft, (int)Math.Floor(left)), y0 = Math.Max(Canvas.ClipTop, (int)Math.Floor(top));
                int x1 = Math.Min(Canvas.Width, Math.Min(Canvas.ClipRight, (int)Math.Ceiling(left + w)));
                int y1 = Math.Min(Canvas.Height, Math.Min(Canvas.ClipBottom, (int)Math.Ceiling(top + h)));
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int dst = y * Canvas.Width + x;
                        float z = depth + ((y + .5f) / Canvas.Scale - p.Y) / 5;
                        if (Canvas.Occlusion[dst] > z + .001f) continue;
                        float u = (x + .5f - left) / w, v = (y + .5f - top) / h;
                        if (crater.Mirror) u = 1 - u;
                        uint color = SampleCrater(u, v); int a = (int)(color >> 24);
                        if (a == 0) continue;
                        uint old = Canvas.Pixels[dst];
                        int red = (int)(color & 255) + (int)(old & 255) * (255 - a) / 255;
                        int green = (int)((color >> 8) & 255) + (int)((old >> 8) & 255) * (255 - a) / 255;
                        int blue = (int)((color >> 16) & 255) + (int)((old >> 16) & 255) * (255 - a) / 255;
                        Canvas.Pixels[dst] = PixelCanvas.RGB(red, green, blue);
                    }
            }
        }
#endif

        static uint SampleCrater(float u, float v)
        {
            float px = Math.Max(0, Math.Min(CraterArt.Width - 1, u * CraterArt.Width - .5f));
            float py = Math.Max(0, Math.Min(CraterArt.Height - 1, v * CraterArt.Height - .5f));
            int x = (int)px, y = (int)py, x1 = Math.Min(x + 1, CraterArt.Width - 1), y1 = Math.Min(y + 1, CraterArt.Height - 1);
            float fx = px - x, fy = py - y; uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                float a = (CraterArt.Pixels[y * CraterArt.Width + x] >> shift) & 255;
                float b = (CraterArt.Pixels[y * CraterArt.Width + x1] >> shift) & 255;
                float c = (CraterArt.Pixels[y1 * CraterArt.Width + x] >> shift) & 255;
                float d = (CraterArt.Pixels[y1 * CraterArt.Width + x1] >> shift) & 255;
                result |= (uint)((a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy) << shift;
            }
            return result;
        }
    }
}
