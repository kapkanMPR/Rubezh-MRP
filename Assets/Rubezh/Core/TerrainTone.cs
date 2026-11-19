using System;

namespace SilentOrders.Core
{
    // Reduce only local luminance contrast. Coverage, geometry and hue remain unchanged.
    public static class TerrainTone
    {
        public static void Calm(ProceduralPicture image)
        {
            if (image == null)
                return;
            int w = image.PixelWidth,
                h = image.PixelHeight;
            if (w < 1 || h < 1)
                return;
            SmoothGrain(image);
            var pixels = image.Pixels;
            var horizontal = new ushort[pixels.Length];
            const int radius = 5;
            for (int y = 0; y < h; y++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += Luma(pixels[y * w + Math.Max(0, Math.Min(w - 1, k))]);
                for (int x = 0; x < w; x++)
                {
                    horizontal[y * w + x] = (ushort)(sum / 11);
                    sum -= Luma(pixels[y * w + Math.Max(0, x - radius)]);
                    sum += Luma(pixels[y * w + Math.Min(w - 1, x + radius + 1)]);
                }
            }
            for (int x = 0; x < w; x++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++)
                    sum += horizontal[Math.Max(0, Math.Min(h - 1, k)) * w + x];
                for (int y = 0; y < h; y++)
                {
                    int i = y * w + x;
                    uint c = pixels[i];
                    int d = (sum / 11 - Luma(c)) * 2 / 3;
                    if ((c >> 24) != 0)
                        pixels[i] =
                            (c & 0xff000000u)
                            | (uint)Clamp((int)(c & 255) + d)
                            | ((uint)Clamp((int)((c >> 8) & 255) + d) << 8)
                            | ((uint)Clamp((int)((c >> 16) & 255) + d) << 16);
                    sum -= horizontal[Math.Max(0, y - radius) * w + x];
                    sum += horizontal[Math.Min(h - 1, y + radius + 1) * w + x];
                }
            }
        }

        // Small edge-aware colour filter removes chromatic grain without bleeding across roads.
        static void SmoothGrain(ProceduralPicture image)
        {
            int w = image.PixelWidth,
                h = image.PixelHeight;
            var src = image.Pixels;
            var dst = new uint[src.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int index = y * w + x;
                uint c = src[index];
                if ((c >> 24) == 0)
                {
                    dst[index] = c;
                    continue;
                }
                int cr = (int)(c & 255),
                    cg = (int)((c >> 8) & 255),
                    cb = (int)((c >> 16) & 255),
                    sum = 0,
                    rr = 0,
                    gg = 0,
                    bb = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = Math.Max(0, Math.Min(w - 1, x + dx)),
                        yy = Math.Max(0, Math.Min(h - 1, y + dy));
                    uint q = src[yy * w + xx];
                    if ((q >> 24) == 0)
                        continue;
                    int r = (int)(q & 255),
                        g = (int)((q >> 8) & 255),
                        b = (int)((q >> 16) & 255);
                    int delta = Math.Max(Math.Abs(r - cr), Math.Max(Math.Abs(g - cg), Math.Abs(b - cb)));
                    int weight = Math.Max(0, 48 - delta) * (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                    sum += weight;
                    rr += r * weight;
                    gg += g * weight;
                    bb += b * weight;
                }
                dst[index] = (c & 0xff000000u) | (uint)(rr / sum) | ((uint)(gg / sum) << 8) | ((uint)(bb / sum) << 16);
            }
            image.Pixels = dst;
        }

        static int Luma(uint c) => (int)(((c & 255) * 77 + ((c >> 8) & 255) * 150 + ((c >> 16) & 255) * 29) >> 8);

        static int Clamp(int x) => Math.Max(0, Math.Min(255, x));
    }
}
