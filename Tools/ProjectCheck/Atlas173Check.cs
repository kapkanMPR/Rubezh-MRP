using System;
using System.Collections.Generic;
using SilentOrders.Rendering;
class Atlas173Check
{
    static void Main()
    {
        bool reproduced = false;
        try { AtlasLayout.Pack(new[] { new AtlasLayout.Size(1022, 1022) }, 1024); }
        catch (NotSupportedException) { reproduced = true; }
        if (!reproduced) throw new Exception("Expected the 172 oversized tile to fail");
        var random = new Random(173);
        foreach (int device in new[] { 8, 64, 255, 512, 1023, 1024, 2048, 4096, 8192 })
        {
            int side = AtlasLayout.ProceduralTileSide(device);
            int limit = Math.Min(1024, device) / 4 * 4;
            var sizes = new List<AtlasLayout.Size> { new AtlasLayout.Size(side, side) };
            for (int i = 0; i < 80; i++) sizes.Add(new AtlasLayout.Size(random.Next(1, side + 1), random.Next(1, side + 1)));
            var pack = AtlasLayout.Pack(sizes, Math.Min(1024, device));
            for (int i = 0; i < sizes.Count; i++)
            {
                var a = pack.Items[i]; var page = pack.Pages[a.Page];
                if (page.W > limit || page.H > limit || a.X < 1 || a.Y < 1 ||
                    a.X + a.W >= page.W || a.Y + a.H >= page.H)
                    throw new Exception("Extrusion outside page");
                if (a.X - 1 < 4 && a.Y - 1 < 4) throw new Exception("White tile overlap");
                for (int j = 0; j < i; j++)
                {
                    var b = pack.Items[j];
                    if (a.Page == b.Page && a.X - 1 < b.X + b.W + 1 && b.X - 1 < a.X + a.W + 1 &&
                        a.Y - 1 < b.Y + b.H + 1 && b.Y - 1 < a.Y + a.H + 1)
                        throw new Exception("Extrusion overlaps another sprite");
                }
            }
        }
        Console.WriteLine("PASS 172 reproducer and 729 packed tiles: boundaries, extrusion, white tile, overlap");
    }
}
