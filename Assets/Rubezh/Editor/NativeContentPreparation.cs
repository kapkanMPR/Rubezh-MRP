#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.U2D;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.U2D;

namespace Rubezh.UnityPort.Editor
{
    public sealed class NativeSpriteImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Rubezh/NativeContent/Sprites/", StringComparison.Ordinal))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 8192;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
        }
    }

    [InitializeOnLoad]
    public static class NativeContentPreparation
    {
        const string Root = "Assets/Rubezh/NativeContent";
        const string Stamp = "Library/RubezhNativeContent.ready";
        static IEnumerator work;

        static NativeContentPreparation()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        [MenuItem("Рубеж/Unity/Отменить подготовку контента")]
        public static void CancelPreparation()
        {
            if (Running)
            {
                Stop();
                Debug.Log("Подготовка отменена. Готовые импортированные страницы сохранены.");
            }
        }

        public static bool Running => work != null;

        [MenuItem("Рубеж/Тест Unity/Подготовить нативный контент")]
        public static void Prepare()
        {
            if (Running || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            work = Steps();
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Stop();
                Debug.LogWarning("Подготовка остановлена перед Play Mode. Завершите её после выхода.");
                return;
            }
            try
            {
                if (work.MoveNext())
                    return;
                Stop();
                Debug.Log(
                    File.Exists(Stamp)
                        ? "Нативный контент подготовлен. Можно запускать тест или собирать APK."
                        : "Подготовка отменена. Повторный запуск продолжит работу с существующими ресурсами."
                );
            }
            catch (Exception e)
            {
                Stop();
                Debug.LogException(e);
            }
        }

        static void Stop()
        {
            (work as IDisposable)?.Dispose();
            work = null;
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
        }

        static IEnumerator Steps()
        {
            if (File.Exists(Stamp))
                File.Delete(Stamp);
            NativeUiPreparation.ArchiveSources();
            BuildResourcePreparation.Prepare();
            NativeExteriorPreparation.Prepare();
            EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOnAtlas;
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            string folder = Root + "/Atlases";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            yield return null;
            var group =
                settings.FindGroup("Rubezh Native")
                ?? settings.CreateGroup(
                    "Rubezh Native",
                    false,
                    false,
                    false,
                    null,
                    typeof(BundledAssetGroupSchema),
                    typeof(ContentUpdateGroupSchema)
                );
            var bundle = group.GetSchema<BundledAssetGroupSchema>();
            bundle.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
            bundle.IncludeInBuild = true;
            bundle.IncludeAddressInCatalog = true;
            bundle.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundle.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            var walls = NativeWallMaterialPreparation.Prepare();
            try
            {
                while (walls.MoveNext())
                    yield return walls.Current;
            }
            finally
            {
                (walls as IDisposable)?.Dispose();
            }
            var dirs = Directory.GetDirectories(Root + "/Sprites").OrderBy(x => x).ToArray();
            for (int i = 0; i < dirs.Length; i++)
            {
                if (
                    EditorUtility.DisplayCancelableProgressBar(
                        "Нативные атласы Unity",
                        Path.GetFileName(dirs[i]) + " · " + (i + 1) + " / " + dirs.Length,
                        (float)i / dirs.Length
                    )
                )
                    yield break;
                string page = Path.GetFileName(dirs[i]),
                    path = folder + "/" + page + ".spriteatlas";
                var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null)
                {
                    atlas = new SpriteAtlas();
                    AssetDatabase.CreateAsset(atlas, path);
                }
                // Unchanged pages retain their imported cache; do not dirty 281 atlases on every run.
                string packFolder = dirs[i].Replace('\\', '/');
                var packing = atlas.GetPackingSettings();
                var texture = atlas.GetTextureSettings();
                var android = atlas.GetPlatformSettings("Android");
                var pc = atlas.GetPlatformSettings("Standalone");
                var packables = atlas.GetPackables();
                bool changed =
                    packing.enableRotation
                    || packing.enableTightPacking
                    || packing.padding != 4
                    || texture.readable
                    || texture.generateMipMaps
                    || texture.sRGB
                    || texture.filterMode != FilterMode.Bilinear
                    || !android.overridden
                    || android.format != TextureImporterFormat.ASTC_4x4
                    || android.maxTextureSize != 8192
                    || !pc.overridden
                    || pc.format != TextureImporterFormat.BC7
                    || pc.maxTextureSize != 8192
                    || atlas.IsIncludeInBuild()
                    || packables.Length != 1
                    || AssetDatabase.GetAssetPath(packables[0]) != packFolder;
                if (changed)
                {
                    atlas.SetPackingSettings(
                        new SpriteAtlasPackingSettings
                        {
                            blockOffset = 1,
                            padding = 4,
                            enableRotation = false,
                            enableTightPacking = false,
                        }
                    );
                    atlas.SetTextureSettings(
                        new SpriteAtlasTextureSettings
                        {
                            readable = false,
                            generateMipMaps = false,
                            sRGB = false,
                            filterMode = FilterMode.Bilinear,
                        }
                    );
                    atlas.SetPlatformSettings(
                        new TextureImporterPlatformSettings
                        {
                            name = "DefaultTexturePlatform",
                            maxTextureSize = 8192,
                            textureCompression = TextureImporterCompression.Compressed,
                        }
                    );
                    atlas.SetPlatformSettings(
                        new TextureImporterPlatformSettings
                        {
                            name = "Android",
                            overridden = true,
                            maxTextureSize = 8192,
                            format = TextureImporterFormat.ASTC_4x4,
                            compressionQuality = 50,
                        }
                    );
                    atlas.SetPlatformSettings(
                        new TextureImporterPlatformSettings
                        {
                            name = "Standalone",
                            overridden = true,
                            maxTextureSize = 8192,
                            format = TextureImporterFormat.BC7,
                            compressionQuality = 50,
                        }
                    );
                    atlas.SetIncludeInBuild(false);
                    if (packables.Length > 0)
                        atlas.Remove(packables);
                    atlas.Add(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<DefaultAsset>(packFolder) });
                    EditorUtility.SetDirty(atlas);
                }
                var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group);
                entry.address = "rubezh/native/atlas/" + int.Parse(page.Substring(1));
                yield return null;
            }
            Directory.CreateDirectory(Root + "/Maps");
            int mapIndex = 0;
            foreach (var map in NativeContentAudit.Maps())
            {
                if (
                    EditorUtility.DisplayCancelableProgressBar(
                        "Карты Unity",
                        map.address + " · " + (++mapIndex) + " / 70",
                        mapIndex / 70f
                    )
                )
                    yield break;
                if (!File.Exists(map.source))
                    throw new BuildFailedException("Нет исходной карты: " + map.source);
                var buildings = NativeBuildingPreparation.PrepareMap(map.source, map.target, settings, group);
                try
                {
                    while (buildings.MoveNext())
                        yield return buildings.Current;
                }
                finally
                {
                    (buildings as IDisposable)?.Dispose();
                }
                settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(map.target), group).address = map.address;
                yield return null;
            }
            NativeBuildingPreparation.RemoveStaleEntries(settings, group);
            // Old diagnostic aliases are not separate content; the loader resolves them to the 70 maps.
            foreach (var old in new[] { "forest", "story20" })
            {
                string oldPath = Root + "/Maps/" + old + ".bytes";
                string guid = AssetDatabase.AssetPathToGUID(oldPath);
                if (!string.IsNullOrEmpty(guid))
                    settings.RemoveAssetEntry(guid);
                if (File.Exists(oldPath))
                    AssetDatabase.DeleteAsset(oldPath);
            }
            Directory.CreateDirectory(Root + "/Prefabs");
            AssetDatabase.Refresh();
            string prefabPath = Root + "/Prefabs/BattleVisual.prefab";
            if (!File.Exists(prefabPath))
            {
                var visual = new GameObject("Battle visual");
                visual.AddComponent<MeshFilter>();
                visual.AddComponent<MeshRenderer>();
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(visual, prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(visual);
                }
            }
            settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(prefabPath), group).address =
                "rubezh/native/visual";
            var catalog = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(Root + "/catalog.json"), group);
            catalog.address = "rubezh/native/catalog";
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayProgressBar("Проверка ресурсов Unity", "Адреса, карты, настройки импорта", 1);
            yield return null;
            var audit = NativeContentAudit.Inspect();
            NativeContentAudit.Write(audit, NativeContentAudit.ReportFolder("NativePreparation"));
            if (!audit.passed)
                throw new BuildFailedException(string.Join("\n", audit.errors));
            File.WriteAllText(Stamp, audit.fingerprint);
        }

        public static void RequirePrepared()
        {
            if (Running || !File.Exists(Stamp) || File.ReadAllText(Stamp) != NativeContentAudit.Fingerprint())
                throw new BuildFailedException(
                    "Сначала выполните Рубеж → Тест Unity → Подготовить нативный контент. Подготовка выполняется по этапам, её можно отменить."
                );
        }

        [Serializable]
        sealed class ContentBuildReport
        {
            public string version,
                platform,
                error;
            public double seconds;
            public int atlasCount,
                spriteCount;
            public long sourcePngBytes;
        }

        [MenuItem("Рубеж/Тест Unity/Собрать нативный контент")]
        public static void BuildMenu()
        {
            BuildContent(null);
        }

        public static void BuildContent(string reportFolder = null)
        {
            if (reportFolder == null)
                reportFolder = NativeContentAudit.ReportFolder("NativeBuild");
            RequirePrepared();
            var audit = NativeContentAudit.Inspect();
            NativeContentAudit.Write(audit, reportFolder);
            if (!audit.passed)
                throw new BuildFailedException(string.Join("\n", audit.errors));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = new ContentBuildReport
            {
                version = RubezhVersion.Current,
                platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                atlasCount = Directory.GetFiles(Root + "/Atlases", "*.spriteatlas").Length,
            };
            var pngs = Directory.GetFiles(Root + "/Sprites", "*.png", SearchOption.AllDirectories);
            report.spriteCount = pngs.Length;
            report.sourcePngBytes = pngs.Sum(p => new FileInfo(p).Length);
            try
            {
                UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult result;
                AddressableAssetSettings.BuildPlayerContent(out result);
                if (result == null || !string.IsNullOrEmpty(result.Error))
                    throw new BuildFailedException("Addressables: " + result?.Error);
                // Building can update Addressables bookkeeping assets. Recheck the actual content
                // before publishing a fresh stamp so the following Player preprocessor stays valid.
                AssetDatabase.SaveAssets();
                var after = NativeContentAudit.Inspect();
                NativeContentAudit.Write(after, reportFolder);
                if (!after.passed)
                    throw new BuildFailedException(string.Join("\n", after.errors));
                File.WriteAllText(Stamp, after.fingerprint);
            }
            catch (Exception error)
            {
                report.error = error.ToString();
                throw;
            }
            finally
            {
                report.seconds = watch.Elapsed.TotalSeconds;
                if (reportFolder != null)
                {
                    Directory.CreateDirectory(reportFolder);
                    File.WriteAllText(
                        Path.Combine(reportFolder, "native-content.json"),
                        JsonUtility.ToJson(report, true)
                    );
                }
            }
        }
    }
}
#endif
