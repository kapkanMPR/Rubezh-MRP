using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Rubezh.UnityPort;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.U2D;

// Portable API contracts have no importer. Supply the atlas identities, dimensions and
// building placements required by the production compositor, without allocating pixel payloads.
internal sealed class SceneAssets
{
    readonly Dictionary<string, UnityEngine.Object> assets = new Dictionary<string, UnityEngine.Object>();
    readonly List<AtlasLayout.Size> buildingPages = new List<AtlasLayout.Size>();

    public SceneAssets(RubezhLevel level)
    {
        string catalog = File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json");
        assets["rubezh/native/catalog"] = new TextAsset { text = catalog };
        assets["rubezh/native/visual"] = new GameObject("Battle visual");
        var pages = JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(catalog).entries.ToLookup(e => e.page);
        var buildingCatalog = PrepareBuildingLayout(level);
        const string buildings = "rubezh/native/building/mission20/";
        assets[buildings + "catalog"] = new TextAsset { text = JsonConvert.SerializeObject(buildingCatalog) };
        for (int i = 0; i < buildingPages.Count; i++)
        {
            var size = buildingPages[i];
            assets[buildings + i + "/color"] = new Texture2D(size.W, size.H);
            assets[buildings + i + "/depth"] = new Texture2D(size.W, size.H);
        }
        Addressables.Loader = key =>
        {
            if (assets.TryGetValue(key, out var result))
                return new State { Result = result };
            int page = int.Parse(key.Substring(key.LastIndexOf('/') + 1));
            if (!pages.Contains(page))
                throw new InvalidDataException("Unknown atlas " + key);
            var atlas = new SpriteAtlas();
            // The test double represents a packed sprite page. UV origins are irrelevant to command counts.
            var texture = new Texture2D(8192, 8192);
            foreach (var e in pages[page])
                atlas.Items["s" + e.id.ToString("D5")] = new Sprite
                {
                    texture = texture,
                    textureRect = new Rect(0, 0, e.w, e.h),
                };
            return new State { Result = atlas };
        };
        Resources.Loader = path =>
            path == "NativeExterior/catalog"
                ? new TextAsset { text = File.ReadAllText("Assets/Rubezh/Resources/NativeExterior/catalog.json") }
            : path.StartsWith("NativeExterior/page-") ? new Texture2D(2048, 2048)
            : path == "RoofSurface" || path == "ClosureWood" || path == "Effects/terrain-crater" ? new Texture2D(16, 16)
            : path == "RubezhNativeSceneMaterial" || path == "RubezhNativeEffectMaterial" ? new Material(new Shader())
            : path.StartsWith("Rubezh") ? (UnityEngine.Object)new Shader()
            : null;
    }

    NativeBuildingCatalog PrepareBuildingLayout(RubezhLevel level)
    {
        var regions = new List<NativeBuildingRegion>();
        var pending = new List<NativeBuildingRegion>();
        long pendingBytes = 0;
        void Flush()
        {
            if (pending.Count == 0)
                return;
            var sizes = pending.Select(r => new AtlasLayout.Size(r.w, r.h)).ToList();
            // Run the baker layout shipped in the selected source revision.
            var pack = typeof(AtlasLayout).GetMethod("PackBuildingPages");
            var layout =
                pack == null
                    ? AtlasLayout.Pack(sizes, 1024)
                    : (AtlasLayout.Layout)pack.Invoke(null, new object[] { sizes });
            for (int i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                var p = layout.Items[i];
                r.page = buildingPages.Count + p.Page;
                r.x = p.X;
                r.y = p.Y;
                regions.Add(r);
            }
            buildingPages.AddRange(layout.Pages);
            pending.Clear();
            pendingBytes = 0;
        }

        int count = 0;
        Span<byte> header = stackalloc byte[24];
        foreach (var visual in level.dynamicBuildings.SelectMany(h => h.layers).Select(layer => layer.picture))
        {
            // Match the PNG sizing in ProceduralVisualData.Decode and the tile and batch sizes in
            // NativeBuildingPreparation.PrepareMap. This fixture needs only 24 header bytes per PNG.
            if (!Convert.TryFromBase64Chars(visual.png.AsSpan(0, 32), header, out int length) || length != 24)
                throw new InvalidDataException("Invalid building PNG header");
            int width = BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4));
            int height = BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4));
            int w = Math.Min(width, Math.Max(1, (int)Math.Ceiling(visual.width * 4.8f)));
            int h = Math.Min(height, Math.Max(1, (int)Math.Ceiling(visual.height * 4.8f)));
            if (pendingBytes > 32L * 1024 * 1024)
                Flush();
            pendingBytes += (long)w * h * 8;
            visual.nativeKey = "mission20:" + count++;
            visual.pixelWidth = w;
            visual.pixelHeight = h;
            visual.png = visual.depthPng = null;
            for (int y = 0; y < h; y += 512)
            for (int x = 0; x < w; x += 512)
                pending.Add(
                    new NativeBuildingRegion
                    {
                        key = visual.nativeKey,
                        px = x,
                        py = y,
                        w = Math.Min(512, w - x),
                        h = Math.Min(512, h - y),
                    }
                );
        }
        Flush();
        Console.WriteLine(
            $"Asset metadata: {count} building layers, {regions.Count} regions, {buildingPages.Count} pages, {buildingPages.Sum(p => (long)p.W * p.H)} texels per color/depth set."
        );
        return new NativeBuildingCatalog
        {
            version = 1,
            pages = buildingPages.Count,
            regions = regions.ToArray(),
        };
    }
}
