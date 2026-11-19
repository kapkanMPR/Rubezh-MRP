#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public static class NativeExteriorPreparation
    {
        public const string Root = "Assets/Rubezh/Resources/NativeExterior";
        public const string Legacy = "Assets/Rubezh/Resources/Exterior";
        public const string Source = "Assets/Rubezh/Editor/LegacySources/Exterior";
        const int Size = 2048,
            Padding = 2;

        [MenuItem("Рубеж/Unity/Пересобрать атлас окружения")]
        public static void Rebuild()
        {
            BuildResourcePreparation.MigrateSourceFolder(Legacy, Source);
            string source = Source;
            if (!Directory.Exists(source))
                throw new BuildFailedException("Нет исходников Exterior: " + source);
            var files = Directory
                .GetFiles(source, "*.bytes")
                .OrderBy(x => Path.GetFileName(x), StringComparer.Ordinal)
                .ToArray();
            if (files.Length != 96)
                throw new BuildFailedException(
                    "Неполный набор Exterior: " + files.Length + " / 96. Исходники: " + source
                );
            Directory.CreateDirectory(Root);
            var entries = new List<ExteriorSpriteLoader.Entry>();
            int page = 0,
                x = Padding,
                y = Padding,
                row = 0;
            var pixels = new uint[Size * Size];
            Action save = () =>
            {
                var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                try
                {
                    t.SetPixelData(pixels, 0);
                    t.Apply(false, false);
                    File.WriteAllBytes(Root + "/page-" + page.ToString("D2") + ".png", t.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(t);
                }
            };
            foreach (string file in files)
                using (var stream = File.OpenRead(file))
                using (var b = new BinaryReader(stream))
                {
                    int w = b.ReadInt32(),
                        h = b.ReadInt32();
                    if (w < 1 || h < 1 || w > 256 || h > 256 || stream.Length != 24 + w * h * 4)
                        throw new BuildFailedException("Повреждён Exterior: " + file);
                    float left = b.ReadSingle(),
                        top = b.ReadSingle(),
                        width = b.ReadSingle(),
                        height = b.ReadSingle();
                    if (x + w + Padding > Size)
                    {
                        x = Padding;
                        y += row + Padding * 2;
                        row = 0;
                    }
                    if (y + h + Padding > Size)
                    {
                        save();
                        page++;
                        pixels = new uint[Size * Size];
                        x = y = Padding;
                        row = 0;
                    }
                    var src = new uint[w * h];
                    for (int i = 0; i < src.Length; i++)
                    {
                        uint c = b.ReadUInt32(),
                            a = c >> 24;
                        src[i] =
                            (a << 24)
                            | ((c & 255) * a / 255)
                            | ((((c >> 8) & 255) * a / 255) << 8)
                            | ((((c >> 16) & 255) * a / 255) << 16);
                    }
                    for (int dy = -Padding; dy < h + Padding; dy++)
                    for (int dx = -Padding; dx < w + Padding; dx++)
                        pixels[(y + dy) * Size + x + dx] = src[
                            Math.Max(0, Math.Min(h - 1, dy)) * w + Math.Max(0, Math.Min(w - 1, dx))
                        ];
                    entries.Add(
                        new ExteriorSpriteLoader.Entry
                        {
                            sourceSha256 = Hash(file),
                            key = Path.GetFileNameWithoutExtension(file),
                            page = page,
                            x = x,
                            y = y,
                            w = w,
                            h = h,
                            left = left,
                            top = top,
                            width = width,
                            height = height,
                        }
                    );
                    x += w + Padding * 2;
                    row = Math.Max(row, h);
                }
            save();
            File.WriteAllText(
                Root + "/catalog.json",
                JsonUtility.ToJson(new ExteriorSpriteLoader.Catalog { entries = entries.ToArray() }, true)
            );
            AssetDatabase.Refresh();
        }

        public static void Prepare()
        {
            BuildResourcePreparation.MigrateSourceFolder(Legacy, Source);
            try
            {
                Require();
            }
            catch (BuildFailedException)
            {
                // Validate/rebuild only after both source folders have been reconciled.
                // Rebuild errors propagate; never bypass the final content check.
                Rebuild();
                Require();
            }
        }

        static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        public static void Require()
        {
            string file = Root + "/catalog.json";
            if (!File.Exists(file))
                throw new BuildFailedException("Нет NativeExterior. Пересоберите атлас окружения.");
            var c = JsonUtility.FromJson<ExteriorSpriteLoader.Catalog>(File.ReadAllText(file));
            if (c == null || c.version != 1 || c.size != Size || c.entries == null || c.entries.Length != 96)
                throw new BuildFailedException("Некорректный каталог NativeExterior");
            foreach (var e in c.entries)
                if (!File.Exists(Root + "/page-" + e.page.ToString("D2") + ".png"))
                    throw new BuildFailedException("Нет страницы NativeExterior " + e.page);
            string source = Directory.Exists(Legacy) ? Legacy : Source;
            if (Directory.Exists(source))
            {
                if (Directory.GetFiles(source, "*.bytes").Length != c.entries.Length)
                    throw new BuildFailedException("Exterior изменён: пересоберите атлас окружения.");
                foreach (var e in c.entries)
                {
                    string raw = source + "/" + e.key + ".bytes";
                    if (!File.Exists(raw) || Hash(raw) != e.sourceSha256)
                        throw new BuildFailedException(
                            "Exterior изменён: " + e.key + ". Пересоберите атлас окружения."
                        );
                }
            }
        }
    }

    public sealed class NativeExteriorImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (
                !assetPath.StartsWith(NativeExteriorPreparation.Root + "/", StringComparison.Ordinal)
                || !assetPath.EndsWith(".png", StringComparison.Ordinal)
            )
                return;
            var i = (TextureImporter)assetImporter;
            i.textureType = TextureImporterType.Default;
            i.textureShape = TextureImporterShape.Texture2D;
            i.mipmapEnabled = false;
            i.isReadable = false;
            i.sRGBTexture = false;
            i.alphaIsTransparency = false;
            i.npotScale = TextureImporterNPOTScale.None;
            i.maxTextureSize = 2048;
            i.filterMode = FilterMode.Bilinear;
            i.wrapMode = TextureWrapMode.Clamp;
            i.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = 2048,
                    format = TextureImporterFormat.ASTC_4x4,
                    compressionQuality = 100,
                }
            );
            i.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Standalone",
                    overridden = true,
                    maxTextureSize = 2048,
                    format = TextureImporterFormat.BC7,
                    compressionQuality = 100,
                }
            );
        }
    }
}
#endif
