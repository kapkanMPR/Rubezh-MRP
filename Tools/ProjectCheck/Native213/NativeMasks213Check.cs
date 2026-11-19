using System;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using UnityEngine;

class NativeMasks213Check
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);

    static object Get(object o, string name) => o.GetType().GetField(name, Flags).GetValue(o);

    static void Call(object o, string name) => o.GetType().GetMethod(name, Flags).Invoke(o, null);

    static void Check(bool v, string s)
    {
        if (!v)
            throw new Exception(s);
    }

    static float Sample(float[] field, int w, int h, float x, float y)
    {
        x -= .5f;
        y -= .5f;
        int ix = (int)Math.Floor(x),
            iy = (int)Math.Floor(y);
        float fx = x - ix,
            fy = y - iy;
        Func<int, int, float> at = (a, b) => a < 0 || b < 0 || a >= w || b >= h ? 0 : field[b * w + a];
        float a0 = at(ix, iy) * (1 - fx) + at(ix + 1, iy) * fx,
            a1 = at(ix, iy + 1) * (1 - fx) + at(ix + 1, iy + 1) * fx;
        return a0 * (1 - fy) + a1 * fy;
    }

    static void Main()
    {
        int width = 31,
            height = 27;
        var fog = new BattleVisibility();
        Set(fog, "columns", width);
        Set(fog, "rows", height);
        fog.Clear = new float[width * height];
        fog.ExploredBlend = new float[width * height];
        var rng = new Random(211);
        for (int i = 0; i < fog.Clear.Length; i++)
        {
            fog.Clear[i] = (float)rng.NextDouble();
            fog.ExploredBlend[i] = (float)rng.NextDouble();
        }
        for (int i = 0; i < 17; i++)
            fog.Danger.Add(
                new DangerPatch
                {
                    Position = new V2((float)rng.NextDouble() * width, (float)rng.NextDouble() * height),
                    Radius = 1 + (float)rng.NextDouble() * 7,
                    Strength = (float)rng.NextDouble() * .4f,
                }
            );
        int maxDelta = 0;
        for (int n = 0; n < 20000; n++)
        {
            fog.Hardcore = n % 2 == 0;
            var point = new V2((float)rng.NextDouble() * (width + 4) - 2, (float)rng.NextDouble() * (height + 4) - 2);
            uint expected = fog.Overlay(point);
            float light = Sample(fog.Clear, width, height, point.X, point.Y),
                explored = Sample(fog.ExploredBlend, width, height, point.X, point.Y),
                danger = 0;
            foreach (var p in fog.Danger)
            {
                float x = point.X - p.Position.X,
                    y = point.Y - p.Position.Y,
                    d2 = (x * x + y * y) / (p.Radius * p.Radius);
                if (d2 < 1)
                    danger = Math.Max(danger, p.Strength * (1 - d2) * (1 - d2) * (1 - light));
            }
            float alpha = fog.Hardcore ? 1 - explored * (1 - .63f * (1 - light)) : .63f * (1 - light);
            bool known = !(fog.Hardcore && explored <= 0);
            int[] channels =
            {
                (int)(255 * danger),
                known ? (int)(255 * .015f * (1 - danger)) : 0,
                known ? (int)(255 * .025f * (1 - danger)) : 0,
                (int)(255 * Math.Max(alpha, danger)),
            };
            for (int c = 0; c < 4; c++)
            {
                int delta = Math.Abs((int)((expected >> (8 * c)) & 255) - channels[c]);
                maxDelta = Math.Max(maxDelta, delta);
                Check(delta <= 1, "GPU-equivalent fog formula differs by >1 byte");
            }
        }
        var roofActors = new System.Collections.Generic.List<V2> { new V2(1, 1), new V2(5, 5), new V2(12, 9) };
        for (int n = 0; n < 20000; n++)
        {
            var point = new V2((float)rng.NextDouble() * 20 - 4, (float)rng.NextDouble() * 20 - 4);
            float opacity = 1;
            foreach (var actor in roofActors)
            {
                float dx = point.X - actor.X,
                    dy = point.Y - actor.Y,
                    t = Math.Max(0, Math.Min(1, ((float)Math.Sqrt(dx * dx + dy * dy) - 3.25f) / 1.75f));
                opacity = Math.Min(opacity, .5f + .5f * t * t * (3 - 2 * t));
            }
            int expected = (int)Math.Round(RoofVisibility.Opacity(point, roofActors) * 255),
                actual = (int)Math.Floor(opacity * 255 + .5f);
            Check(Math.Abs(expected - actual) <= 1, "roof opacity preserves radius/fade/overlap");
        }
        Console.WriteLine(
            "PASS: 20000 roof reveal opacity comparisons against Core.RoofVisibility including overlapping actors."
        );
        Resources.Loader = key => key == "RubezhBattleMasks" ? new Shader() : null;
        var battle = new BattleSimulation(211);
        battle.Fog.Enabled = true;
        battle.Fog.Update(battle);
        var app = new RubezhApp();
        Set(app, "battle", battle);
        Call(app, "UpdateNativeFogMask");
        var target = (RenderTexture)Get(app, "nativeFogTarget");
        Check(target != null && target.IsCreated(), "fog RT created");
        Check(
            typeof(RubezhApp).GetField("fogMask", Flags) == null
                && typeof(RubezhApp).GetField("fogPixels", Flags) == null,
            "native path must not allocate CPU fog mask"
        );
        Call(app, "UpdateNativeFogMask");
        Check(ReferenceEquals(target, Get(app, "nativeFogTarget")), "unchanged frame reuses target");
        Call(app, "UpdateNativeNightMask");
        Check(
            typeof(RubezhApp).GetField("nightMask", Flags) == null
                && typeof(RubezhApp).GetField("nightPixels", Flags) == null,
            "native path must not allocate CPU night mask"
        );
        Call(app, "UpdateNativeRain");
        var rainMesh = Get(app, "nativeRainMesh");
        var rainTarget = (RenderTexture)Get(app, "nativeRainTarget");
        Check(rainMesh != null && rainTarget.IsCreated(), "GPU rain mesh/target created");
        Call(app, "UpdateNativeRain");
        Check(ReferenceEquals(rainMesh, Get(app, "nativeRainMesh")), "paused rain reuses mesh");
        battle.Time += .05f;
        Call(app, "UpdateNativeRain");
        Check(ReferenceEquals(rainMesh, Get(app, "nativeRainMesh")), "moving rain never rebuilds geometry");
        Call(app, "ReleaseNativeMasks");
        Check(!rainTarget.IsCreated() && Get(app, "nativeRainMesh") == null, "rain releases alongside masks");
        Check(
            !target.IsCreated() && Get(app, "nativeKnowledge") == null && Get(app, "nativeLightData") == null,
            "release clears masks and buffers"
        );
        Console.WriteLine(
            "PASS: 20000 fog formula samples (normal/hardcore/border/danger), maximum byte difference="
                + maxDelta
                + "; GPU target/cache/no CPU raster allocation/release contracts."
        );
    }
}
