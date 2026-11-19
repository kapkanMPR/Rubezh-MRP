#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Rebuild from immutable legacy source packages, never recolour an already baked PNG.
    public static class NativeWallMaterialPreparation
    {
        const string Root = "Assets/Rubezh/NativeContent";
        public const string Recipe = "wall-material-211-v1";

        [Serializable]
        public sealed class Manifest
        {
            public string recipe,
                materialHash;
            public Item[] entries;
        }

        [Serializable]
        public sealed class Item
        {
            public int id;
            public string sourceHash,
                pngHash;
        }

        [Serializable]
        sealed class Catalog
        {
            public UnityTestAtlasEntry[] entries;
        }

        public static IEnumerator Prepare()
        {
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "/catalog.json"));
            var required = StandaloneWallMaterials.AllSourceSprites();
            var entries = catalog.entries.Where(e => required.Contains(e.id)).OrderBy(e => e.id).ToArray();
            if (entries.Length != required.Count)
                throw new BuildFailedException("Wall bake: missing native sprite catalog entries");
            string materialPath = BuildResourcePreparation.Packed + "/" + StandaloneWallMaterials.MaterialId + ".bytes";
            string materialHash = BuildResourcePreparation.Hash(materialPath);
            string manifestPath = Root + "/wall-materials.json";
            var previous = File.Exists(manifestPath)
                ? JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath))
                : null;
            var old = previous?.entries?.ToDictionary(e => e.id) ?? new Dictionary<int, Item>();
            uint[] material = null;
            int mw = 0,
                mh = 0;
            var result = new List<Item>();
            foreach (var page in entries.GroupBy(e => Rubezh.SpritePackIndex.Page[e.id]))
            {
                string package = EditorSpritePacks.SourcePath(page.Key);
                string sourceHash = BuildResourcePreparation.Hash(package);
                var stale = page.Where(e =>
                        previous == null
                        || previous.recipe != Recipe
                        || previous.materialHash != materialHash
                        || !old.ContainsKey(e.id)
                        || old[e.id].sourceHash != sourceHash
                        || !File.Exists(Png(e))
                        || old[e.id].pngHash != BuildResourcePreparation.Hash(Png(e))
                    )
                    .ToArray();
                if (stale.Length > 0)
                {
                    if (material == null)
                    {
                        var source = BuildingMaterialPixels.Read(StandaloneWallMaterials.MaterialId);
                        mw = source.width;
                        mh = source.height;
                        material = new uint[mw * mh];
                        for (int y = 0; y < mh; y++)
                        for (int x = 0; x < mw; x++)
                        {
                            var c = source.pixels[(mh - 1 - y) * mw + x];
                            material[y * mw + x] = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
                        }
                    }
                    var wanted = stale.ToDictionary(e => e.id);
                    using (var file = File.OpenRead(package))
                    using (var zip = new GZipStream(file, CompressionMode.Decompress))
                    using (var reader = new BinaryReader(zip))
                    {
                        int format = reader.ReadInt32(),
                            count = reader.ReadInt32();
                        bool legacy = format == 0x50475152;
                        if (!legacy && format != 0x32475152)
                            throw new InvalidDataException("Invalid wall source package");
                        for (int n = 0; n < count; n++)
                        {
                            int id = reader.ReadInt32(),
                                levels = reader.ReadInt32();
                            for (int mip = 0; mip < levels; mip++)
                            {
                                int w = reader.ReadInt32(),
                                    h = reader.ReadInt32();
                                int length = checked(w * h * 4);
                                var pixels = reader.ReadBytes(length);
                                if (pixels.Length != length)
                                    throw new EndOfStreamException(package);
                                UnityTestAtlasEntry e;
                                if (mip != 0 || !wanted.TryGetValue(id, out e))
                                    continue;
                                if (w != e.ow || h != e.oh)
                                    throw new BuildFailedException("Wall source size mismatch: " + id);
                                if (legacy)
                                    for (int k = 0; k < pixels.Length; k += 4)
                                    {
                                        int a = pixels[k + 3];
                                        if (id < 139 && pixels[k] <= a && pixels[k + 1] <= a && pixels[k + 2] <= a)
                                            continue;
                                        for (int c = 0; c < 3; c++)
                                            pixels[k + c] = (byte)((pixels[k + c] * a + 127) / 255);
                                    }
                                StandaloneWallMaterials.BakePixels(pixels, w, h, material, mw, mh);
                                var cropped = new byte[e.w * e.h * 4];
                                for (int y = 0; y < e.h; y++)
                                    Array.Copy(pixels, ((y + e.oy) * w + e.ox) * 4, cropped, y * e.w * 4, e.w * 4);
                                // SpritePacks/LoadRawTextureData are bottom-up; EncodeToPNG writes top-down.
                                // Do NOT flip cropped here (the portable PIL encoder flips exactly once).
                                var texture = new Texture2D(e.w, e.h, TextureFormat.RGBA32, false, true);
                                try
                                {
                                    texture.LoadRawTextureData(cropped);
                                    texture.Apply();
                                    File.WriteAllBytes(Png(e), texture.EncodeToPNG());
                                }
                                finally
                                {
                                    UnityEngine.Object.DestroyImmediate(texture);
                                }
                                AssetDatabase.ImportAsset(Png(e), ImportAssetOptions.ForceUpdate);
                                wanted.Remove(id);
                            }
                        }
                    }
                    if (wanted.Count != 0)
                        throw new BuildFailedException("Wall source missing sprites: " + string.Join(",", wanted.Keys));
                }
                foreach (var e in page)
                    result.Add(
                        new Item
                        {
                            id = e.id,
                            sourceHash = sourceHash,
                            pngHash = BuildResourcePreparation.Hash(Png(e)),
                        }
                    );
                yield return null;
            }
            string json = JsonUtility.ToJson(
                new Manifest
                {
                    recipe = Recipe,
                    materialHash = materialHash,
                    entries = result.OrderBy(e => e.id).ToArray(),
                },
                true
            );
            if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != json)
            {
                File.WriteAllText(manifestPath, json);
                AssetDatabase.ImportAsset(manifestPath);
            }
        }

        public static IEnumerable<string> Inputs()
        {
            yield return "Assets/Rubezh/Editor/NativeWallMaterialPreparation.cs";
            yield return "Assets/Rubezh/Runtime/Levels/StandaloneWallMaterials.cs";
            yield return BuildResourcePreparation.Packed + "/" + StandaloneWallMaterials.MaterialId + ".bytes";
            foreach (
                int page in StandaloneWallMaterials
                    .AllSourceSprites()
                    .Select(id => Rubezh.SpritePackIndex.Page[id])
                    .Distinct()
            )
                yield return EditorSpritePacks.SourcePath(page);
        }

        public static void Validate(List<string> errors, bool compareHashes)
        {
            string path = Root + "/wall-materials.json";
            if (!File.Exists(path))
            {
                errors.Add("Missing baked wall material manifest");
                return;
            }
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (manifest == null || manifest.recipe != Recipe || manifest.entries == null)
            {
                errors.Add("Stale baked wall material recipe");
                return;
            }
            var required = StandaloneWallMaterials.AllSourceSprites();
            if (manifest.entries.Length != required.Count || !required.SetEquals(manifest.entries.Select(e => e.id)))
            {
                errors.Add("Baked wall material coverage mismatch");
                return;
            }
            if (!compareHashes)
                return;
            string material = BuildResourcePreparation.Packed + "/" + StandaloneWallMaterials.MaterialId + ".bytes";
            if (!File.Exists(material) || manifest.materialHash != BuildResourcePreparation.Hash(material))
                errors.Add("Stale baked wall material source");
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "/catalog.json"));
            var entries = catalog.entries.ToDictionary(e => e.id);
            var hashes = new Dictionary<int, string>();
            foreach (var item in manifest.entries)
            {
                UnityTestAtlasEntry e;
                if (!entries.TryGetValue(item.id, out e))
                {
                    errors.Add("Wall sprite absent: " + item.id);
                    continue;
                }
                int page = Rubezh.SpritePackIndex.Page[item.id];
                string hash;
                if (!hashes.TryGetValue(page, out hash))
                {
                    string package = EditorSpritePacks.SourcePath(page);
                    hash = File.Exists(package) ? BuildResourcePreparation.Hash(package) : "missing";
                    hashes[page] = hash;
                }
                if (
                    hash != item.sourceHash
                    || !File.Exists(Png(e))
                    || BuildResourcePreparation.Hash(Png(e)) != item.pngHash
                )
                    errors.Add("Stale baked wall sprite: " + item.id);
            }
        }

        static string Png(UnityTestAtlasEntry e) =>
            Root + "/Sprites/p" + e.page.ToString("D4") + "/s" + e.id.ToString("D5") + ".png";
    }
}
#endif
