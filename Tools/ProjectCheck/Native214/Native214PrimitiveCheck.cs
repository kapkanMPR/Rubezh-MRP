using System;
using System.Linq;
using System.Reflection;
using SilentOrders.Rendering;

static class Native214PrimitiveCheck
{
    sealed class Capture : ICanvasGpu
    {
        public uint[] Pixels;
        public int W,
            H,
            Calls;

        public Capture(int w, int h)
        {
            W = w;
            H = h;
            Pixels = new uint[w * h];
        }

        public void Rect(float x, float y, float w, float h, uint color, float depth, bool test)
        {
            Calls++;
            for (int yy = (int)y; yy < (int)(y + h); yy++)
            for (int xx = (int)x; xx < (int)(x + w); xx++)
            {
                if (xx < 0 || yy < 0 || xx >= W || yy >= H)
                    throw new Exception("unclipped primitive rectangle");
                Pixels[yy * W + xx] = color;
            }
        }

        public void Background(uint[] p, float[] d, object m, int r) => throw new Exception("unexpected background");

        public void Sprite(int id, float x, float y, int w, int h, float a, float d, bool t) =>
            throw new Exception("unexpected sprite");
    }

    public static void Run()
    {
        var method = typeof(Rubezh.Graphics).GetMethod("DrawPrimitive", BindingFlags.NonPublic | BindingFlags.Static);
        var random = new Random(214);
        int checks = 0;
        for (int n = 0; n < 1600; n++)
        {
            float scale = new[] { .5f, 1f, 1.5f, 2f }[n % 4];
            var cpu = new PixelCanvas(32, 28, scale);
            var gpu = new PixelCanvas(32, 28, scale);
            var capture = new Capture(cpu.Width, cpu.Height);
            gpu.Gpu = capture;
            int left = random.Next(cpu.Width / 3),
                top = random.Next(cpu.Height / 3),
                right = random.Next(cpu.Width / 2, cpu.Width + 1),
                bottom = random.Next(cpu.Height / 2, cpu.Height + 1);
            cpu.ClipLeft = gpu.ClipLeft = left;
            cpu.ClipTop = gpu.ClipTop = top;
            cpu.ClipRight = gpu.ClipRight = right;
            cpu.ClipBottom = gpu.ClipBottom = bottom;
            object[] args =
            {
                cpu,
                n % 2 == 0 ? 37 : 240,
                (float)random.Next(-4, 36),
                (float)random.Next(-4, 32),
                random.Next(-21, 22),
                random.Next(-21, 22),
                n % 3 == 0,
            };
            method.Invoke(null, args);
            args[0] = gpu;
            method.Invoke(null, args);
            if (!cpu.Pixels.SequenceEqual(capture.Pixels))
                throw new Exception("primitive parity failed at case " + n);
            if (gpu.AllocatedPixelBytes != 0 || capture.Calls > 3)
                throw new Exception("primitive retained allocation/rectangle bound failed");
            checks++;
        }
        Console.WriteLine(
            "PASS Native214PrimitiveCheck: "
                + checks
                + " GPU-rectangle vs CPU pixel silhouettes, odd/even sizes, scale, clipping, enemy; zero native CPU pixels."
        );
    }
}
