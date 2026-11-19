#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public sealed class NativeBuildingImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (
                !assetPath.StartsWith(NativeBuildingPreparation.Root + "/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".png")
            )
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = 8192,
                    format = TextureImporterFormat.ASTC_4x4,
                }
            );
            importer.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Standalone",
                    overridden = true,
                    maxTextureSize = 8192,
                    format = TextureImporterFormat.BC7,
                }
            );
        }
    }

    public static class NativeBuildingPreparation
    {
        public const string Root = "Assets/Rubezh/NativeContent/Buildings";
        const string Recipe = "native-buildings-223-draw-order";

        [Serializable]
        public sealed class Receipt
        {
            public string recipe,
                source,
                target;
            public string[] keys;
        }

        public static bool Current(string source, string target)
        {
            if (!File.Exists(target) || !File.Exists(target + ".buildings.json"))
                return false;
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(target + ".buildings.json"));
            if (
                receipt == null
                || receipt.recipe != Recipe
                || receipt.source != BuildResourcePreparation.Hash(source)
                || receipt.target != BuildResourcePreparation.Hash(target)
            )
                return false;
            foreach (var key in receipt.keys ?? Array.Empty<string>())
            {
                string folder = Root + "/" + key;
                if (!File.Exists(folder + "/catalog.json"))
                    return false;
                var catalog = JsonUtility.FromJson<NativeBuildingCatalog>(File.ReadAllText(folder + "/catalog.json"));
                for (int i = 0; i < catalog.pages; i++)
                    if (!File.Exists(folder + "/" + i + ".png") || !File.Exists(folder + "/" + i + "-depth.asset"))
                        return false;
            }
            return true;
        }

        static IEnumerable<ProceduralVisualData> Visuals(BuiltinCampaign.Map map)
        {
            if (map.dynamicBuildings != null && map.dynamicBuildings.Count > 0)
            {
                foreach (var building in map.dynamicBuildings)
                foreach (var layer in building.layers)
                    yield return layer.picture;
            }
            else if (map.proceduralVisuals != null)
                foreach (var visual in map.proceduralVisuals)
                    yield return visual;
        }

        static void Register(string key, AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            string directory = Root + "/" + key;
            var data = JsonUtility.FromJson<NativeBuildingCatalog>(File.ReadAllText(directory + "/catalog.json"));
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(directory + "/catalog.json"), group).address =
                "rubezh/native/building/" + key + "/catalog";
            for (int i = 0; i < data.pages; i++)
            {
                settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(directory + "/" + i + ".png"), group).address =
                    "rubezh/native/building/" + key + "/" + i + "/color";
                settings
                    .CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(directory + "/" + i + "-depth.asset"), group)
                    .address = "rubezh/native/building/" + key + "/" + i + "/depth";
            }
        }

        public static void RemoveStaleEntries(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var map in NativeContentAudit.Maps())
            {
                if (!File.Exists(map.target + ".buildings.json"))
                    throw new InvalidOperationException("Missing building preparation receipt: " + map.target);
                var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(map.target + ".buildings.json"));
                foreach (var key in receipt.keys ?? Array.Empty<string>())
                    expected.Add(key);
            }
            foreach (var entry in group.entries.ToArray())
            {
                const string prefix = "rubezh/native/building/";
                if (
                    entry.address.StartsWith(prefix, StringComparison.Ordinal)
                    && !expected.Contains(entry.address.Substring(prefix.Length).Split('/')[0])
                )
                    settings.RemoveAssetEntry(entry.guid);
            }
        }

        sealed class Piece
        {
            public SilentOrders.Core.ProceduralPicture Image;
            public string Key;
            public int X,
                Y,
                W,
                H;
        }

        public static IEnumerator PrepareMap(
            string source,
            string target,
            AddressableAssetSettings settings,
            AddressableAssetGroup group
        )
        {
            Directory.CreateDirectory(Root);
            if (Current(source, target))
            {
                var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(target + ".buildings.json"));
                foreach (var key in receipt.keys ?? Array.Empty<string>())
                    Register(key, settings, group);
                yield break;
            }
            string catalogKey;
            using (var hash = SHA256.Create())
                catalogKey = BitConverter
                    .ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Recipe + BuildResourcePreparation.Hash(source))))
                    .Replace("-", "")
                    .ToLowerInvariant();
            string folder = Root + "/" + catalogKey;
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
            }
            var map = JsonUtility.FromJson<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes(source)));
            var regions = new List<NativeBuildingRegion>();
            int pages = 0,
                index = 0;
            var pieces = new List<Piece>();
            var sizes = new List<SilentOrders.Rendering.AtlasLayout.Size>();
            long pendingBytes = 0;
            Action flush = () =>
            {
                if (pieces.Count == 0)
                    return;
                var pack = SilentOrders.Rendering.AtlasLayout.PackBuildingPages(sizes);
                for (int page = 0; page < pack.Pages.Length; page++)
                {
                    var size = pack.Pages[page];
                    var colors = new uint[size.W * size.H];
                    var depths = new float[colors.Length];
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        var placement = pack.Items[i];
                        if (placement.Page != page)
                            continue;
                        var piece = pieces[i];
                        for (int y = -1; y <= piece.H; y++)
                        for (int x = -1; x <= piece.W; x++)
                        {
                            int sx = piece.X + Math.Max(0, Math.Min(piece.W - 1, x)),
                                sy = piece.Y + Math.Max(0, Math.Min(piece.H - 1, y));
                            int src = sy * piece.Image.PixelWidth + sx,
                                dst = (placement.Y + y) * size.W + placement.X + x;
                            uint c = piece.Image.Pixels[src],
                                alpha = c >> 24;
                            colors[dst] =
                                (alpha << 24)
                                | ((c & 255) * alpha / 255)
                                | ((((c >> 8) & 255) * alpha / 255) << 8)
                                | ((((c >> 16) & 255) * alpha / 255) << 16);
                            depths[dst] = piece.Image.Depth[src];
                        }
                        regions.Add(
                            new NativeBuildingRegion
                            {
                                key = piece.Key,
                                page = pages,
                                x = placement.X,
                                y = placement.Y,
                                w = piece.W,
                                h = piece.H,
                                px = piece.X,
                                py = piece.Y,
                            }
                        );
                    }
                    for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        colors[y * size.W + x] = 0xffffffff;
                    var color = new Texture2D(size.W, size.H, TextureFormat.RGBA32, false, true);
                    var depth = new Texture2D(size.W, size.H, TextureFormat.RFloat, false, true);
                    string colorPath = folder + "/" + pages + ".png",
                        depthPath = folder + "/" + pages + "-depth.asset";
                    try
                    {
                        color.SetPixelData(colors, 0);
                        color.Apply(false, false);
                        File.WriteAllBytes(colorPath, color.EncodeToPNG());
                        AssetDatabase.ImportAsset(colorPath);
                        depth.SetPixelData(depths, 0);
                        depth.filterMode = FilterMode.Point;
                        depth.wrapMode = TextureWrapMode.Clamp;
                        depth.Apply(false, true);
                        if (File.Exists(depthPath))
                            AssetDatabase.DeleteAsset(depthPath);
                        AssetDatabase.CreateAsset(depth, depthPath);
                        depth = null;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(color);
                        if (depth != null)
                            UnityEngine.Object.DestroyImmediate(depth);
                    }
                    pages++;
                }
                pieces.Clear();
                sizes.Clear();
                pendingBytes = 0;
            };
            try
            {
                foreach (var visual in Visuals(map))
                {
                    var picture = visual.Decode();
                    string key = catalogKey + ":" + (index++);
                    // Bounded batches: do not retain a full city's decoded images in the Editor.
                    if (pendingBytes > 32L * 1024 * 1024)
                        flush();
                    pendingBytes += picture.Pixels.LongLength * 8;
                    for (int y = 0; y < picture.PixelHeight; y += 512)
                    for (int x = 0; x < picture.PixelWidth; x += 512)
                    {
                        var piece = new Piece
                        {
                            Image = picture,
                            Key = key,
                            X = x,
                            Y = y,
                            W = Math.Min(512, picture.PixelWidth - x),
                            H = Math.Min(512, picture.PixelHeight - y),
                        };
                        pieces.Add(piece);
                        sizes.Add(new SilentOrders.Rendering.AtlasLayout.Size(piece.W, piece.H));
                    }
                    visual.nativeKey = key;
                    visual.pixelWidth = picture.PixelWidth;
                    visual.pixelHeight = picture.PixelHeight;
                    visual.left = picture.Left;
                    visual.top = picture.Top;
                    visual.width = picture.Width;
                    visual.height = picture.Height;
                    visual.png = null;
                    visual.depthPng = null;
                    visual.recipe = null;
                    visual.clipTable = null;
                    yield return null;
                }
                flush();
                File.WriteAllText(
                    folder + "/catalog.json",
                    JsonUtility.ToJson(
                        new NativeBuildingCatalog
                        {
                            version = 1,
                            pages = pages,
                            regions = regions.ToArray(),
                        }
                    )
                );
                AssetDatabase.ImportAsset(folder + "/catalog.json");
                Register(catalogKey, settings, group);
                map.terrainVisual = null;
                if (map.dynamicBuildings != null && map.dynamicBuildings.Count > 0)
                    map.proceduralVisuals = null;
                using (var output = File.Create(target))
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                using (var writer = new StreamWriter(gzip, new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(map));
                AssetDatabase.ImportAsset(target);
                File.WriteAllText(
                    target + ".buildings.json",
                    JsonUtility.ToJson(
                        new Receipt
                        {
                            recipe = Recipe,
                            source = BuildResourcePreparation.Hash(source),
                            target = BuildResourcePreparation.Hash(target),
                            keys = new[] { catalogKey },
                        },
                        true
                    )
                );
            }
            finally
            {
                BuildingRecipe.ClearMaterials();
            }
        }
    }
}
#endif
