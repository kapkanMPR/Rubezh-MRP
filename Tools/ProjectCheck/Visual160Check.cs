using System;
using System.Collections.Generic;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

class Visual160Check
{
    static void Check(bool ok, string m)
    {
        if (!ok)
            throw new Exception(m);
    }

    static void Main()
    {
        var actors = new List<V2> { new V2(10, 10) };
        float previous = 0;
        for (int i = 0; i <= 600; i++)
        {
            float a = RoofVisibility.Opacity(new V2(10 + i * .01f, 10), actors);
            Check(a >= previous - 1e-6 && a >= .5f && a <= 1, "opacity monotonic/bounds");
            Check(a - previous < .009f || i == 0, "opacity step");
            previous = a;
        }
        Check(RoofVisibility.Opacity(new V2(15, 10), actors) == 1, "five cells opaque");
        Check(Math.Abs(RoofVisibility.Opacity(new V2(14, 10), actors) - .70f) < .05, "soft edge");
        actors.Clear();
        Check(RoofVisibility.Opacity(new V2(10, 10), actors) == 1, "empty building opaque");
        var battle = new BattleSimulation();
        battle.Soldiers.Clear();
        battle.Map.DynamicBuildings = new List<DynamicBuilding>
        {
            new DynamicBuilding(
                new List<StructureWall>(),
                new List<StructureRoof>(),
                new List<StructureFloor>
                {
                    new StructureFloor { x = 10, y = 10 },
                },
                new List<StructureLayer>()
            ),
        };
        var soldier = new Soldier
        {
            Position = new V2(10.25f, 10.25f),
            Side = Side.Green,
            Health = 100,
            Deployed = true,
        };
        battle.Soldiers.Add(soldier);
        RoofVisibility.Actors(battle, actors);
        Check(actors.Count == 1, "inside soldier");
        soldier.Position = new V2(11, 10);
        RoofVisibility.Actors(battle, actors);
        Check(actors.Count == 0, "outside soldier");
        soldier.Position = new V2(10.25f, 10.25f);
        soldier.Health = 0;
        RoofVisibility.Actors(battle, actors);
        Check(actors.Count == 0, "dead soldier");
        soldier.Health = 100;
        soldier.Side = Side.Red;
        battle.Fog.Enabled = true;
        RoofVisibility.Actors(battle, actors);
        Check(actors.Count == 0, "hidden enemy cannot reveal roof");
        Console.WriteLine("PASS roof: 601 fade samples, 5-cell boundary, inside/outside/dead/hidden actors");
        var p = new ProceduralPicture
        {
            PixelWidth = 64,
            PixelHeight = 64,
            Pixels = new uint[4096],
        };
        var rng = new Random(160);
        double before = 0;
        for (int i = 0; i < p.Pixels.Length; i++)
        {
            int n = rng.Next(-20, 21);
            p.Pixels[i] = PixelCanvas.RGB(110 + n, 115 + n, 95 + n);
            before += n * n;
        }
        TerrainTone.Calm(p);
        double after = 0;
        foreach (uint c in p.Pixels)
        {
            Check(c >> 24 == 255, "terrain alpha");
            double d = (c & 255) - 110.0;
            after += d * d;
        }
        Check(after < before * .4, "terrain noise reduced");
        Console.WriteLine("PASS terrain luminance variance ratio=" + (after / before).ToString("F3"));
        Rubezh.Graphics.ConfigurePacks();
        Resources.Loader = s => s == "RubezhHdGpu" ? (UnityEngine.Object)new Shader() : new Texture2D(16, 16);
        var gpu = new HdGpuRenderer(new PixelCanvas(320, 200), true);
        typeof(HdGpuRenderer)
            .GetField("visiblePixels", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(gpu, new Rect(0, 0, 320, 200));
        p = new ProceduralPicture
        {
            Left = 0,
            Top = 0,
            Width = 10,
            Height = 10,
            PixelWidth = 16,
            PixelHeight = 16,
            Pixels = new uint[256],
            Depth = new float[256],
        };
        for (int i = 0; i < 256; i++)
        {
            p.Pixels[i] = 0xffffffff;
            p.Depth[i] = 256;
        }
        var images = new[] { p };
        gpu.Pictures(images, 20, 20, 1);
        long bytes = gpu.ProceduralGpuBytes;
        Check(bytes > 0 && gpu.ProceduralGpuImages == 1, "upload");
        for (int i = 0; i < 100; i++)
            gpu.Pictures(images, 20, 20, 1);
        Check(bytes == gpu.ProceduralGpuBytes, "no repeated upload");
        var replaced = new ProceduralPicture
        {
            Left = p.Left,
            Top = p.Top,
            Width = p.Width,
            Height = p.Height,
            PixelWidth = p.PixelWidth,
            PixelHeight = p.PixelHeight,
            Pixels = (uint[])p.Pixels.Clone(),
            Depth = p.Depth,
        };
        replaced.Pixels[0] = 0;
        long uploads = gpu.PictureFullUploads;
        images = new[] { replaced };
        gpu.Pictures(images, 20, 20, 1);
        Check(gpu.PictureFullUploads == uploads && gpu.PictureRegionUploads == 1, "localized atlas update");
        Check(UnityEngine.Graphics.RegionCopies == 2, "color/depth region copies");
        gpu.Pictures(images, -150, 20, 1);
        Check(gpu.ProceduralGpuBytes == bytes, "retention margin");
        gpu.Pictures(images, 10000, 10000, 1);
        Check(gpu.ProceduralGpuBytes == 0 && gpu.ProceduralGpuImages == 0, "offscreen eviction");
        gpu.Pictures(images, 20, 20, 1);
        Check(gpu.ProceduralGpuBytes == bytes, "reentry");
        gpu.RoofVisibility(battle);
        gpu.Pictures(new ProceduralPicture[0], 0, 0, 1);
        Check(gpu.ProceduralGpuBytes == 0, "map invalidation");
        gpu.Dispose();
        Check(gpu.ProceduralGpuBytes == 0, "dispose");
        Console.WriteLine("PASS GPU cache lifecycle against API contracts (not native rendering)");
    }
}
