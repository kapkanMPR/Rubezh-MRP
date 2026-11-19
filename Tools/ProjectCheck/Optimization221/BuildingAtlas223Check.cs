using System;
using System.Collections.Generic;
using System.Linq;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

partial class Optimization221Check
{
    static void BuildingPagePacking()
    {
        var rng = new Random(223);
        foreach (int count in new[] { 0, 1, 31, 233, 2700 })
        {
            var sizes = new List<AtlasLayout.Size>();
            for (int i = 0; i < count; i++)
                sizes.Add(
                    count > 1000
                        ? new AtlasLayout.Size(16, 32)
                        : new AtlasLayout.Size(rng.Next(10, 513), rng.Next(10, 513))
                );
            var dense = AtlasLayout.Pack(sizes, 1024);
            var layout = AtlasLayout.PackBuildingPages(sizes);
            Check(layout.Pages.Length <= dense.Pages.Length, "bounded atlas page count");
            Check(
                layout.PixelCount <= dense.PixelCount + dense.PixelCount / 16,
                "bounded allocated color/depth texels"
            );
            Check(layout.Items.Length == sizes.Count, "all source regions have placements");
            var groups = layout.Items.GroupBy(item => item.Page);
            foreach (var group in groups)
            {
                var size = layout.Pages[group.Key];
                Check(
                    size.W <= 2048 && size.H <= 2048 && size.W % 4 == 0 && size.H % 4 == 0,
                    "dimensions fit platform and compression alignment"
                );
                var coverage = new bool[size.W * size.H];
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    coverage[y * size.W + x] = true;
                foreach (var item in group)
                {
                    Check(
                        item.X > 0 && item.Y > 0 && item.X + item.W < size.W && item.Y + item.H < size.H,
                        "original image and its extrusion fit inside the page"
                    );
                    for (int y = item.Y - 1; y <= item.Y + item.H; y++)
                    for (int x = item.X - 1; x <= item.X + item.W; x++)
                    {
                        int offset = y * size.W + x;
                        if (coverage[offset])
                            throw new Exception(
                                "Atlas regions overlap their neighbors or the reserved primitive pixels"
                            );
                        coverage[offset] = true;
                    }
                }
            }
            for (int i = 0; i < count; i++)
                Check(
                    layout.Items[i].W == sizes[i].W && layout.Items[i].H == sizes[i].H,
                    "source texel dimensions survive"
                );
        }
    }

    static void BuildingMaskColumns(int limit)
    {
        var r = Fixture();
        foreach (
            string name in new[] { "buildingPages", "buildingPictures", "pictureDepthPages", "atlases", "materials" }
        )
        {
            var field = typeof(HdGpuRenderer).GetField(name, F);
            field.SetValue(r, Activator.CreateInstance(field.FieldType));
        }
        var pictures = (Dictionary<string, ProceduralPicture>)Get(r, "buildingPictures");
        var source = new NativeBuildingAssets.Page { Color = new Texture2D(16, 16), Depth = new Texture2D(16, 16) };
        var walls = new List<DynamicBuilding.Wall>();
        var structure = new StructureWall { height = 2.8f, bx = 2 };
        for (int x = 0; x < 5; x++)
            structure.columns.Add(
                new StructureColumn
                {
                    spans = new List<StructureSpan>
                    {
                        new StructureSpan { lo = 0, hi = 2.8f },
                    },
                }
            );
        // Repeat a real-sized wall until it needs another texture column. The final
        // region references the same wall to cover two pieces of one original layer.
        int nz = new DynamicBuilding.Wall(structure).NZ;
        for (int i = 0; i <= limit / nz; i++)
        {
            string key = i.ToString();
            var wall = new DynamicBuilding.Wall(structure);
            walls.Add(wall);
            pictures.Add(
                key,
                new ProceduralPicture
                {
                    NativeWall = wall,
                    PixelWidth = 1,
                    PixelHeight = 1,
                }
            );
            source.Regions.Add(
                new NativeBuildingRegion
                {
                    key = key,
                    x = 1,
                    y = 1,
                    w = 1,
                    h = 1,
                }
            );
        }
        source.Regions.Add(
            new NativeBuildingRegion
            {
                key = (walls.Count - 1).ToString(),
                x = 3,
                y = 1,
                w = 1,
                h = 1,
            }
        );
        int previous = SystemInfo.maxTextureSize;
        SystemInfo.maxTextureSize = limit;
        try
        {
            var page = Call(r, "BuildingAtlas", source);
            var origins = (Dictionary<DynamicBuilding.Wall, Vector2Int>)Get(page, "MaskOrigins");
            var texture = (Texture2D)Get(page, "Mask");
            var last = walls[walls.Count - 1];
            var origin = origins[last];
            int row = (walls.Count - 1) * 32;
            var metadata = (float[])Get(page, "MetaFloats");
            Check(origin.x > 0 && origins.Count == walls.Count, "each distinct wall gets its own mask space");
            Check(texture.width <= limit && texture.height <= limit, "damage texture fits the simulated device");
            Check(
                metadata[row + 14] == origin.x
                    && metadata[row + 15] == origin.y
                    && metadata[row + 46] == origin.x
                    && metadata[row + 47] == origin.y,
                "both regions sample the same wall through shader metadata"
            );
            int changed = r.NativeBuildingMaskUpdates;
            last.Solid[3 * last.NX + 2] = false;
            last.Revision++;
            Call(r, "UpdateBuildingMask", page, false);
            var bytes = (byte[])Get(page, "MaskBytes");
            Check(
                bytes[(origin.y + 3) * texture.width + origin.x + 2] == 255
                    && bytes.Count(value => value != 0) == 1
                    && r.NativeBuildingMaskUpdates == changed + 1,
                "damaging a wall changes exactly its addressed mask cell"
            );
            Call(r, "UpdateBuildingMask", page, false);
            Check(r.NativeBuildingMaskUpdates == changed + 1, "stable wall data is reused");
        }
        finally
        {
            SystemInfo.maxTextureSize = previous;
            Call(r, "ReleaseNativeBuildings");
            Call(r, "ReleaseStaticCommands");
        }
    }
}
