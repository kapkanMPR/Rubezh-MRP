using System;
using System.IO;
using System.IO.Compression;

namespace SilentOrders.Rendering
{
    public static partial class PawnSprites
    {
        public sealed class Sprite
        {
            public readonly int W;
            public readonly int H;
            public readonly byte[] RGBA;

            public Sprite(int w, int h, string bytes)
            {
                W = w;
                H = h;
                using (var compressed = new MemoryStream(Convert.FromBase64String(bytes)))
                using (var decoder = new GZipStream(compressed, CompressionMode.Decompress))
                using (var pixels = new MemoryStream())
                {
                    decoder.CopyTo(pixels);
                    RGBA = pixels.ToArray();
                }
            }
        }

        public static readonly Sprite[] Atlas = CreateAtlas();
        public static readonly float[] BarrelY = new float[6]
        {
            -0.1372f,
            -0.16883f,
            -0.04018f,
            -0.12987f,
            -0.16883f,
            -0.05417f,
        };

        public static void Draw(
            PixelCanvas canvas,
            int id,
            float cx,
            float cy,
            int width,
            int height,
            bool enemy = false,
            float angle = 0f
        )
        {
            Rubezh.Graphics.Pawn(canvas, id, cx, cy, width, height, enemy, angle);
        }
    }
}
