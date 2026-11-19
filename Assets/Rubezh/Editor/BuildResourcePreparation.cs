using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public sealed class BuildResourcePreparation : IPreprocessBuildWithReport
    {
        public const string Legacy = "Assets/Rubezh/Resources/BuildingMaterials";
        public const string Source = "Assets/Rubezh/Art/BuildingMaterialsSource";
        public const string Packed = "Assets/Rubezh/Editor/LegacySources/BuildingMaterialData";
        public const string LegacyPacked = "Assets/Rubezh/Resources/BuildingMaterialData";
        static bool busy;
        public int callbackOrder => -2000;

        [InitializeOnLoadMethod]
        static void ScheduleMigration()
        {
            EditorApplication.delayCall += () =>
            {
                if (
                    EditorApplication.isPlayingOrWillChangePlaymode
                    || EditorApplication.isCompiling
                    || BuildPipeline.isBuildingPlayer
                )
                    return;
                PlayerSettings.bundleVersion = RubezhVersion.Current;
                PlayerSettings.Android.bundleVersionCode = RubezhVersion.AndroidCode;
                if (
                    Directory.Exists(Legacy)
                    || Directory.Exists(LegacyPacked)
                    || File.Exists("Assets/Rubezh/Resources/CampaignBaseline.json")
                )
                    try
                    {
                        Prepare();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
            };
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            Prepare();
            ValidateResourceKeys();
        }

        [MenuItem("Рубеж/Сборка/Подготовить ресурсы")]
        public static void Prepare()
        {
            if (busy)
                return;
            busy = true;
            try
            {
                MigrateSourceFolder(Legacy, Source);
                MigratePackedMaterials();
                Directory.CreateDirectory(Packed);
                string input = Directory.Exists(Legacy) ? Legacy : Source;
                if (Directory.Exists(input))
                {
                    var files = Directory
                        .GetFiles(input)
                        .Where(p =>
                            new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(p).ToLowerInvariant())
                        );
                    foreach (var group in files.GroupBy(Path.GetFileNameWithoutExtension))
                    {
                        // Lossless PNG is canonical when its JPEG export also exists.
                        string file = group
                            .OrderBy(p =>
                                Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase) ? 0 : 1
                            )
                            .ThenBy(p => p, StringComparer.Ordinal)
                            .First();
                        string name = group.Key;
                        if (name.Length != 24 || name.Any(c => !Uri.IsHexDigit(c)))
                            throw new BuildFailedException("Invalid material filename: " + file);
                        string target = Packed + "/" + name + ".bytes";
                        if (!File.Exists(target) || Hash(file) != Hash(target))
                            File.Copy(file, target, true);
                    }
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Directory.Exists(Legacy))
                {
                    if (Directory.Exists(Source))
                        throw new BuildFailedException(
                            "Both material source folders exist. Preserve and merge them before building: "
                                + Legacy
                                + " and "
                                + Source
                        );
                    string error = AssetDatabase.MoveAsset(Legacy, Source);
                    if (!string.IsNullOrEmpty(error))
                        throw new BuildFailedException(error);
                }
                const string baseline = "Assets/Rubezh/Resources/CampaignBaseline.json";
                if (File.Exists(baseline))
                {
                    string target = "Assets/Rubezh/Resources/CampaignBaseline.bytes";
                    using (var output = File.Create(target))
                    using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                    using (var source = File.OpenRead(baseline))
                        source.CopyTo(gzip);
                    Directory.CreateDirectory("Assets/Rubezh/EditorData");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    string error = AssetDatabase.MoveAsset(baseline, "Assets/Rubezh/EditorData/CampaignBaseline.json");
                    if (!string.IsNullOrEmpty(error))
                        throw new BuildFailedException(error);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Directory.Exists(Legacy))
                    throw new BuildFailedException("Uncompressed legacy materials remain in Resources");
                for (int i = 1; i <= 20; i++)
                    if (
                        !File.Exists(
                            NativeMapBuildScope.SourcePath(
                                "Assets/Rubezh/Resources/Regenerated/Campaign_" + i.ToString("00") + ".bytes"
                            )
                        )
                    )
                        throw new BuildFailedException("Missing campaign: " + i);
                for (int i = 1; i <= 50; i++)
                    if (
                        !File.Exists(
                            NativeMapBuildScope.SourcePath(
                                "Assets/Rubezh/Resources/War/war-" + i.ToString("00") + ".bytes"
                            )
                        )
                    )
                        throw new BuildFailedException("Missing war map: " + i);
            }
            finally
            {
                busy = false;
            }
        }

        // Patch overlays can restore the old Resources folder after an earlier
        // build moved it into Editor. Keep the active Editor copies; original
        // PNG/JPEG files in Source are still applied by Prepare() afterwards.
        static void MigratePackedMaterials()
        {
            MigrateSourceFolder(LegacyPacked, Packed);
        }

        public static void MigrateSourceFolder(string from, string to)
        {
            if (!Directory.Exists(from))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (!Directory.Exists(to))
            {
                MoveAssetChecked(from, to);
                return;
            }

            // Outside Assets: backup pixels must never enter Resources or APKs.
            // Preserve both trees and their GUIDs before any merge/removal.
            string backup =
                "Build/Backups/MaterialMigration/"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")
                + "-"
                + Guid.NewGuid().ToString("N");
            string oldBackup = backup + "/Legacy";
            BackupTree(from, oldBackup);
            BackupTree(to, backup + "/Current");
            string log = backup + "/migration.txt";
            File.WriteAllText(
                log,
                "Resource migration (221b)\nExisting Editor files and GUIDs win duplicate paths.\nPrepare then regenerates packed pixels from available Source PNG/JPEG originals.\n"
            );
            MergePackedContents(from, to, log);
            // Do not discard a file that appeared or changed during migration.
            foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string saved = oldBackup + file.Substring(from.Length);
                if (!File.Exists(saved) || Hash(file) != Hash(saved))
                    throw new BuildFailedException(
                        "Material changed during migration; retained for retry: " + file + ". Backup: " + backup
                    );
            }
            if (!AssetDatabase.DeleteAsset(from) || Directory.Exists(from))
                throw new BuildFailedException(
                    "Could not remove backed-up legacy material folder: " + from + ". Backup: " + backup
                );
            File.AppendAllText(log, "Completed. Legacy Resources folder removed.\n");
            Debug.Log("Рубеж: объединены " + from + " и " + to + ". Резервная копия обеих папок и журнал: " + backup);
        }

        static void BackupTree(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string folder in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(target + folder.Substring(source.Length));
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                BackupFile(file, target + file.Substring(source.Length));
            if (File.Exists(source + ".meta"))
                BackupFile(source + ".meta", target + ".meta");
        }

        static void BackupFile(string source, string target)
        {
            File.Copy(source, target, false);
            if (Hash(source) != Hash(target))
                throw new BuildFailedException("Material backup verification failed: " + source);
        }

        static void MergePackedContents(string source, string target, string log)
        {
            foreach (string directory in Directory.GetDirectories(source).OrderBy(p => p, StringComparer.Ordinal))
            {
                string destination = target + "/" + Path.GetFileName(directory);
                if (File.Exists(destination))
                    throw new BuildFailedException("Material folder/file conflict: " + destination + ". See " + log);
                if (Directory.Exists(destination))
                    MergePackedContents(directory, destination, log);
                else
                    MoveAssetChecked(directory, destination);
            }
            foreach (string file in Directory.GetFiles(source).OrderBy(p => p, StringComparer.Ordinal))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                string destination = target + "/" + Path.GetFileName(file);
                if (Directory.Exists(destination))
                    throw new BuildFailedException("Material file/folder conflict: " + destination + ". See " + log);
                if (File.Exists(destination))
                {
                    bool same = Hash(file) == Hash(destination);
                    File.AppendAllText(
                        log,
                        (same ? "Identical: " : "Different; kept Editor copy: ") + destination + "\n"
                    );
                    if (!same)
                        Debug.LogWarning(
                            "Рубеж: разные копии материала "
                                + Path.GetFileName(file)
                                + "; сохранена рабочая копия Editor. Обе исходные копии: "
                                + log
                        );
                }
                else if (File.Exists(destination + ".meta"))
                {
                    // An overlay may contain only the destination .meta. Keep
                    // that canonical GUID, restore the missing payload, and
                    // retain the old payload/meta in the verified backup.
                    File.Copy(file, destination, false);
                    AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
                    File.AppendAllText(log, "Restored payload using existing Editor GUID: " + destination + "\n");
                }
                else
                {
                    MoveAssetChecked(file, destination);
                    File.AppendAllText(log, "Moved with GUID: " + destination + "\n");
                }
            }
        }

        static void MoveAssetChecked(string source, string target)
        {
            string error = AssetDatabase.MoveAsset(source.Replace('\\', '/'), target.Replace('\\', '/'));
            if (!string.IsNullOrEmpty(error))
                throw new BuildFailedException(error);
        }

        public static string Hash(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        public static void ValidateResourceKeys()
        {
            var paths = AssetDatabase
                .GetAllAssetPaths()
                .Where(p =>
                    p.StartsWith("Assets/", StringComparison.Ordinal)
                    && p.Contains("/Resources/")
                    && !AssetDatabase.IsValidFolder(p)
                );
            var duplicates = paths
                .GroupBy(
                    p =>
                        Path.ChangeExtension(
                            p.Substring(p.LastIndexOf("/Resources/", StringComparison.Ordinal) + 11),
                            null
                        ),
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(g => g.Count() > 1)
                .ToArray();
            if (duplicates.Length > 0)
                throw new BuildFailedException(
                    "Duplicate Resources keys:\n"
                        + string.Join("\n", duplicates.Select(g => g.Key + ": " + string.Join(", ", g)))
                );
        }
    }
}
