using System;
using Rubezh.UnityPort;

class RecipeCheck
{
    static DrawingPath Box(float a, float b, float c, float d) =>
        new DrawingPath { points = new[] { a, b, c, b, c, d, a, d } };

    static void Main()
    {
        var v = new ProceduralVisualData
        {
            left = 0,
            top = 0,
            width = 2,
            height = 2,
            clipTable = new[]
            {
                new DrawingClip { evenOdd = true, paths = new[] { Box(0, 0, 2, 2), Box(.5f, .5f, 1.5f, 1.5f) } },
            },
            recipe = new[]
            {
                new DrawingOp
                {
                    paths = new[] { Box(0, 0, 2, 2) },
                    clipIds = new[] { 0 },
                    rgba = new[] { 50, 100, 150, 255 },
                    alpha = 1,
                    depth = 256,
                },
            },
        };
        var p = v.Decode();
        if ((p.Pixels[5 * p.PixelWidth + 5] >> 24) != 0 || (p.Pixels[0] >> 24) != 255 || p.Depth[0] != 256)
            throw new Exception("Hole/color/depth mismatch");
        v.recipe[0].clipIds = new[] { 9 };
        bool rejected = false;
        try
        {
            v.Decode();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        if (!rejected)
            throw new Exception("Invalid clip accepted");
        v.recipe = new DrawingOp[0];
        p = v.Decode();
        foreach (uint c in p.Pixels)
            if (c != 0)
                throw new Exception("Empty recipe not transparent");
        Console.WriteLine(
            "PASS procedural raster: even-odd transparent holes, opaque material, depth, invalid clips, empty layers"
        );
    }
}
