using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

class Render199Check
{
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    static int n;

    static object Get(object o, string k) => o.GetType().GetField(k, F).GetValue(o);

    static void Set(object o, string k, object v) => o.GetType().GetField(k, F).SetValue(o, v);

    static void Call(object o, string k, params object[] a) => o.GetType().GetMethod(k, F).Invoke(o, a);

    static void Check(bool b, string s)
    {
        if (!b)
            throw new Exception(s);
        n++;
    }

    static void Main()
    {
        // Compare the original rectangle predicate with the procedural fragment mask.
        for (int phase = 0; phase < 7; phase++)
        for (int q = 0; q <= 100; q++)
        for (int y = 0; y < 15; y++)
        for (int x = 0; x < 23; x++)
        {
            int l = x * 2 - 22,
                k = y * 2 - 14;
            float density = q / 100f;
            bool old =
                (float)(l * l) / 484f + (float)(k * k) / 196f <= 1f && ((l + k + phase) % 7 + 7) % 7 <= 4 * density;
            int encoded = (int)(4 * density) * 63;
            int threshold = (int)Math.Floor((encoded / 255f) * 255 / 63 + .1f);
            bool fresh =
                (float)(l * l) / 484f + (float)(k * k) / 196f <= 1f && ((l + k + phase) % 7 + 7) % 7 <= threshold;
            Check(old == fresh, "smoke mask differs");
        }
        var random = new Random(199);
        for (int trial = 0; trial < 100; trial++)
        {
            int w = 40,
                h = 30;
            var data = new byte[w * h * 4];
            for (int i = 0; i < 20; i++)
            {
                int x = random.Next(9, 25),
                    y = random.Next(7, 23);
                data[(y * w + x) * 4 + 3] = (byte)random.Next(1, 256);
            }
            var c = HdGpuRenderer.AlphaCrop(data, w, h);
            Check(c[2] < w && c[3] < h, "transparent margins removed");
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (data[(y * w + x) * 4 + 3] != 0)
                    Check(x >= c[0] && x < c[0] + c[2] && y >= c[1] && y < c[1] + c[3], "alpha pixel lost");
            foreach (float angle in new[] { 0f, .3f, 1.5f, 3.14f })
            {
                double cos = Math.Cos(angle),
                    sin = Math.Sin(angle);
                double ox = c[0] + c[2] * .5 - w * .5,
                    oy = c[1] + c[3] * .5 - h * .5;
                double px = cos * ox - sin * oy - cos * c[2] * .5 + sin * c[3] * .5,
                    py = sin * ox + cos * oy - sin * c[2] * .5 - cos * c[3] * .5;
                Check(
                    Math.Abs(px - (cos * (c[0] - w * .5) - sin * (c[1] - h * .5))) < 1e-6
                        && Math.Abs(py - (sin * (c[0] - w * .5) + cos * (c[1] - h * .5))) < 1e-6,
                    "rotated pivot changed"
                );
            }
        }
        Check(HdGpuRenderer.AlphaCrop(new byte[1600], 20, 20)[2] == 1, "empty sprite");
        Resources.Loader = s => new Shader();
        Rubezh.Graphics.ConfigurePacks();
        using (var g = new HdGpuRenderer(new PixelCanvas(320, 200), true, true))
        {
            Set(g, "visiblePixels", new Rect(-10000, -10000, 20000, 20000));
            for (int j = 0; j < 5; j++)
                g.SmokePuff(j * 10, 0, 1, j, 4, false, 256);
            Check(g.SmokePuffs == 5, "five quads per cloud");
            g.DisableSmoke = true;
            g.SmokePuff(0, 0, 1, 0, 4, false, 256);
            Check(g.SmokePuffs == 5, "smoke diagnostic toggle");
            var c = new PixelCanvas(320, 200);
            var order = new OrderedCanvas();
            order.Begin(c, null);
            Check(!order.SmokePuff(0, 0, 1, 0, 4, false, 256), "CPU fallback retained");
            order.Begin(c, g);
            Check(order.SmokePuff(0, 0, 1, 0, 4, false, 256), "GPU queue accepts smoke");
            g.DisableSmoke = false;
            order.Flush();
            Check(g.SmokePuffs == 6, "queue replay");
            var b = new BattleSimulation(199);
            var actors = (List<V2>)Get(g, "roofActors");
            actors.Add(new V2(10, 10));
            actors.Add(new V2(12, 10));
            Call(g, "UpdateRevealMask", b);
            var pixels = (uint[])Get(g, "revealPixels");
            int w = (b.Map.Columns + 16) * 4;
            for (int y = 0; y < (b.Map.Rows + 16) * 4; y++)
            for (int x = 0; x < w; x++)
            {
                int expected = (int)
                    Math.Round(RoofVisibility.Opacity(new V2((x + .5f) / 4 - 8, (y + .5f) / 4 - 8), actors) * 255);
                Check(Math.Abs((int)(pixels[y * w + x] & 255) - expected) <= 1, "roof opacity");
            }
            Call(g, "UpdateRevealMask", b);
            Check(g.RevealMaskBuilds == 1, "mask cadence");
            Call(g, "UpdateRevealMask", new BattleSimulation(200));
            Check(g.RevealMaskBuilds == 2, "battle identity refresh");
            g.BeginScene(b.Map);
            g.CacheObject(0, 0, int.MaxValue, true, false, () => { });
            g.EndScene();
            int resident = g.ResidentObjectChunks;
            for (int rev = 1; rev < 60; rev++)
            {
                g.BeginScene(b.Map);
                g.CacheObject(0, 0, int.MaxValue, true, false, () => { });
                g.EndScene();
                Check(g.ResidentObjectChunks == resident, "version duplicates retained");
            }
        }
        // Real packed sprites, original and cropped packing measured with identical layout algorithm.
        var battle = new BattleSimulation(199);
        var required = Rubezh.Graphics.SceneSprites(battle);
        foreach (int page in Rubezh.Graphics.CommonPages())
        {
            string p = "Assets/Rubezh/Editor/LegacySources/SpritePacks/p" + page.ToString("D4") + ".bytes";
            if (File.Exists(p))
                Rubezh.Graphics.LoadPage(page, File.ReadAllBytes(p), required: required);
        }
        using (var g = new HdGpuRenderer(new PixelCanvas(320, 200), true, false))
        {
            var before = new List<AtlasLayout.Size>();
            var after = new List<AtlasLayout.Size>();
            foreach (Array list in (Array)Get(g, "regions"))
            {
                if (list == null)
                    continue;
                foreach (object region in list)
                {
                    var t = region.GetType();
                    int ow = (int)t.GetField("OW").GetValue(region),
                        oh = (int)t.GetField("OH").GetValue(region),
                        w = (int)t.GetField("W").GetValue(region),
                        h = (int)t.GetField("H").GetValue(region);
                    before.Add(new AtlasLayout.Size(ow, oh));
                    after.Add(new AtlasLayout.Size(w, h));
                }
            }
            long oldBytes = 0,
                newBytes = 0;
            foreach (var p in AtlasLayout.Pack(before, SystemInfo.maxTextureSize).Pages)
                oldBytes += (long)p.W * p.H * 4;
            foreach (var p in AtlasLayout.Pack(after, SystemInfo.maxTextureSize).Pages)
                newBytes += (long)p.W * p.H * 4;
            Check(newBytes == g.SpriteAtlasBytes, "atlas bytes accounting");
            Check(newBytes <= oldBytes, "atlas allocation regression");
            Console.WriteLine(
                "Real sprite atlases original="
                    + oldBytes
                    + " trimmed="
                    + newBytes
                    + " transparent payload removed="
                    + g.AtlasTransparentBytesSaved
                    + " entries="
                    + before.Count
            );
        }
        Console.WriteLine("PASS " + n + " render checks. No native Unity shader/GPU/FPS execution.");
    }
}

// API-shape contract only: no native timing is simulated.
namespace UnityEngine
{
    public struct FrameTiming
    {
        public double cpuFrameTime,
            gpuFrameTime;
    }

    public static class FrameTimingManager
    {
        public static void CaptureFrameTimings() { }

        public static uint GetLatestTimings(uint count, FrameTiming[] timings) => 0;
    }
}
