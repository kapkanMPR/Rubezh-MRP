#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace Rubezh.UnityPort.Editor
{
    // Asset validation does not instantiate maps, simulate a battle or decode embedded terrain PNGs.
    // That separate, deliberately expensive check is available from the Play Mode audit menu.
    public static class NativeContentAudit
    {
        public const string Root = "Assets/Rubezh/NativeContent";
        public const string Recipe = "native-223-draw-order-buildings";

        [Serializable]
        public sealed class MapEntry
        {
            public string address,
                source,
                target,
                sourceSha256,
                targetSha256;
            public long bytes;
        }

        [Serializable]
        public sealed class Report
        {
            public string version,
                unity,
                platform,
                utc,
                fingerprint;
            public bool passed;
            public int maps,
                atlases,
                sprites;
            public long duplicateMapSourceBytes;
            public string[] errors,
                warnings;
            public MapEntry[] mapEntries;
        }

        public static IEnumerable<MapEntry> Maps()
        {
            foreach (string mode in new[] { "story", "war" })
                for (int i = 1; i <= (mode == "story" ? 20 : 50); i++)
                    yield return new MapEntry
                    {
                        address = "rubezh/native/map/" + mode + "/" + i.ToString("D2"),
                        source =
                            mode == "story"
                                ? "Assets/Rubezh/Resources/Regenerated/Campaign_" + i.ToString("D2") + ".bytes"
                                : "Assets/Rubezh/Resources/War/war-" + i.ToString("D2") + ".bytes",
                        target = Root + "/Maps/" + mode + "-" + i.ToString("D2") + ".bytes",
                    };
        }

        public static string ReportFolder(string kind)
        {
            string path = Path.Combine(
                "Build",
                "Reports",
                RubezhVersion.Current,
                kind + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
            );
            Directory.CreateDirectory(path);
            return path;
        }

        public static string Fingerprint()
        {
            // Unity reimports changed files using the same file metadata. We include all map and art
            // inputs, import settings, generated atlas files, Addressables configuration and platform.
            // Full map hashes are additionally compared during validation, not on every editor tick.
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            Action<string> file = path =>
            {
                if (File.Exists(NativeMapBuildScope.SourcePath(path)))
                    paths.Add(path);
            };
            Action<string> tree = path =>
            {
                if (Directory.Exists(path))
                    foreach (string p in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        file(p.Replace('\\', '/'));
            };
            tree(Root);
            tree(NativeUiPreparation.Root);
            tree(NativeExteriorPreparation.Root);
            tree(NativeExteriorPreparation.Source);
            file("Assets/Rubezh/Editor/NativeExteriorPreparation.cs");
            file("Assets/Rubezh/Editor/NativeBuildingPreparation.cs");
            file("Assets/Rubezh/Core/AtlasLayout.cs");
            file("Assets/Rubezh/Editor/NativeSurfaceImport.cs");
            file("Assets/Rubezh/Editor/NativeUiPreparation.cs");
            file("Assets/Rubezh/Runtime/NativeUiRecipe.cs");
            if (Directory.Exists("Assets/AddressableAssetsData"))
                foreach (
                    string path in Directory.GetFiles(
                        "Assets/AddressableAssetsData",
                        "*.asset",
                        SearchOption.AllDirectories
                    )
                )
                {
                    file(path.Replace('\\', '/'));
                    file(path.Replace('\\', '/') + ".meta");
                }
            foreach (var map in Maps())
                file(map.source);
            file("Packages/manifest.json");
            file("ProjectSettings/ProjectVersion.txt");
            file("Assets/Rubezh/Editor/NativeContentPreparation.cs");
            file("Assets/Rubezh/Editor/NativeContentAudit.cs");
            file("Assets/Rubezh/Editor/NativeMapBuildScope.cs");
            foreach (string path in NativeWallMaterialPreparation.Inputs())
                file(path);
            var text = new StringBuilder(Recipe).Append('|').Append(EditorUserBuildSettings.activeBuildTarget);
            foreach (string path in paths)
            {
                var info = new FileInfo(NativeMapBuildScope.SourcePath(path));
                text.Append('\n')
                    .Append(path)
                    .Append('|')
                    .Append(info.Length)
                    .Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks);
            }
            using (var sha = SHA256.Create())
                return BitConverter
                    .ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())))
                    .Replace("-", "")
                    .ToLowerInvariant();
        }

        public static Report Inspect(bool compareHashes = true)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            try
            {
                NativeExteriorPreparation.Require();
            }
            catch (Exception e)
            {
                errors.Add(e.Message);
            }
            if (Directory.Exists(NativeExteriorPreparation.Legacy))
                errors.Add("Exterior остаётся в Resources: выполните подготовку нативного контента.");
            var report = new Report
            {
                version = RubezhVersion.Current,
                unity = Application.unityVersion,
                platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                utc = DateTime.UtcNow.ToString("O"),
            };
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings == null ? null : settings.FindGroup("Rubezh Native");
            if (group == null)
                errors.Add("Missing Addressables group: Rubezh Native");
            else
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (
                    schema == null
                    || !schema.IncludeInBuild
                    || !schema.IncludeAddressInCatalog
                    || schema.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                )
                    errors.Add("Rubezh Native must include addresses, be included in the build and use PackSeparately");
            }
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            if (settings != null)
                foreach (var g in settings.groups)
                    if (g != null)
                        foreach (var e in g.entries)
                            if (
                                e.address.StartsWith("rubezh/native/", StringComparison.Ordinal)
                                && !addresses.Add(e.address)
                            )
                                errors.Add("Duplicate native address: " + e.address);
            Action<string, string> entry = (path, address) =>
            {
                if (!File.Exists(path))
                {
                    errors.Add("Missing asset: " + path);
                    return;
                }
                string guid = AssetDatabase.AssetPathToGUID(path);
                var e = settings == null || string.IsNullOrEmpty(guid) ? null : settings.FindAssetEntry(guid);
                if (e == null || e.address != address || e.parentGroup != group)
                    errors.Add("Address mismatch: " + address + " -> " + path);
            };
            if (Directory.Exists(BuildResourcePreparation.LegacyPacked))
                errors.Add(
                    "Editor-only building source pixels remain in Resources: " + BuildResourcePreparation.LegacyPacked
                );
            var maps = Maps().ToArray();
            foreach (var map in maps)
            {
                entry(map.target, map.address);
                if (!File.Exists(NativeMapBuildScope.SourcePath(map.source)))
                {
                    errors.Add("Missing map source: " + map.source);
                    continue;
                }
                map.bytes = new FileInfo(NativeMapBuildScope.SourcePath(map.source)).Length;
                report.duplicateMapSourceBytes += map.bytes;
                if (compareHashes && File.Exists(map.target))
                {
                    map.sourceSha256 = BuildResourcePreparation.Hash(NativeMapBuildScope.SourcePath(map.source));
                    map.targetSha256 = BuildResourcePreparation.Hash(map.target);
                    if (!NativeBuildingPreparation.Current(NativeMapBuildScope.SourcePath(map.source), map.target))
                        errors.Add("Native map/building textures are stale: " + map.address);
                }
            }
            report.maps = maps.Length;
            report.mapEntries = maps;
            if (Directory.Exists(Root + "/Sprites"))
                foreach (var dir in Directory.GetDirectories(Root + "/Sprites").OrderBy(s => s, StringComparer.Ordinal))
                {
                    string page = Path.GetFileName(dir),
                        path = Root + "/Atlases/" + page + ".spriteatlas";
                    int number;
                    if (page.Length < 2 || !int.TryParse(page.Substring(1), out number))
                    {
                        errors.Add("Invalid sprite page: " + page);
                        continue;
                    }
                    entry(path, "rubezh/native/atlas/" + number);
                    var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                    if (atlas == null)
                        continue;
                    var texture = atlas.GetTextureSettings();
                    var packing = atlas.GetPackingSettings();
                    if (
                        texture.readable
                        || texture.generateMipMaps
                        || texture.sRGB
                        || packing.enableRotation
                        || packing.enableTightPacking
                    )
                        errors.Add("Atlas pixels/geometry import mismatch: " + path);
                    var android = atlas.GetPlatformSettings("Android");
                    var pc = atlas.GetPlatformSettings("Standalone");
                    if (
                        !android.overridden
                        || android.format != TextureImporterFormat.ASTC_4x4
                        || !pc.overridden
                        || pc.format != TextureImporterFormat.BC7
                    )
                        errors.Add("Atlas compression overrides missing: " + path);
                    if (atlas.IsIncludeInBuild())
                        errors.Add("Addressable atlas also included in Player: " + path);
                    var packables = atlas.GetPackables();
                    if (packables.Length != 1 || AssetDatabase.GetAssetPath(packables[0]) != dir.Replace('\\', '/'))
                        errors.Add("Atlas packable folder mismatch: " + path);
                    report.atlases++;
                }
            entry(Root + "/catalog.json", "rubezh/native/catalog");
            entry(Root + "/Prefabs/BattleVisual.prefab", "rubezh/native/visual");
            if (File.Exists(Root + "/catalog.json"))
            {
                var catalog = JsonUtility.FromJson<UnityTestAtlasCatalog>(File.ReadAllText(Root + "/catalog.json"));
                if (catalog == null || catalog.version != 2 || catalog.entries == null)
                    errors.Add("Invalid v2 sprite catalog");
                else
                {
                    var ids = new HashSet<int>();
                    foreach (var e in catalog.entries)
                    {
                        if (!ids.Add(e.id))
                            errors.Add("Duplicate sprite id: " + e.id);
                        string path = Root + "/Sprites/p" + e.page.ToString("D4") + "/s" + e.id.ToString("D5") + ".png";
                        if (!File.Exists(path) || e.w <= 0 || e.h <= 0 || e.ow <= 0 || e.oh <= 0)
                            errors.Add("Invalid sprite source: " + path);
                    }
                    report.sprites = ids.Count;
                }
            }
            NativeWallMaterialPreparation.Validate(errors, compareHashes);
            if (Directory.Exists("Assets/Rubezh/Resources/SpritePacks"))
                errors.Add("Legacy sprite packages still in Resources");
            try
            {
                NativeUiPreparation.Require();
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
            if (Directory.Exists("Assets/Rubezh/Resources/UnityTestForest"))
                errors.Add("Obsolete forest atlas duplicates still in Resources");
            foreach (string old in new[] { "forest", "story20" })
                if (File.Exists(Root + "/Maps/" + old + ".bytes"))
                    warnings.Add("Obsolete map copy outside Resources: " + old);
            warnings.Add(
                "Editor map sources are retained; FastBuild temporarily excludes them from Player Resources. Duplicate source payload excluded during build: "
                    + report.duplicateMapSourceBytes
                    + " bytes; this is not the compressed APK size."
            );
            report.errors = errors.ToArray();
            report.warnings = warnings.ToArray();
            report.passed = errors.Count == 0;
            report.fingerprint = Fingerprint();
            return report;
        }

        public static void Write(Report report, string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "native-content-audit.json"), JsonUtility.ToJson(report, true));
        }

        [MenuItem("Рубеж/Unity/Проверить все карты и нативные ресурсы")]
        public static void Menu()
        {
            string folder = ReportFolder("NativeAssets");
            try
            {
                var report = Inspect();
                Write(report, folder);
                if (!report.passed)
                    throw new BuildFailedException(string.Join("\n", report.errors));
                Debug.Log("Native assets audit passed. Report: " + folder);
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString());
                Debug.LogException(e);
            }
        }
    }
}
#endif
