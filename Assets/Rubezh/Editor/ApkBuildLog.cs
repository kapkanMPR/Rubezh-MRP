using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public sealed class ApkBuildLog : IDisposable
    {
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public readonly string Folder;
        readonly object gate = new object();
        readonly DateTime began = DateTime.UtcNow;
        public DateTime StartedUtc => began;
        readonly string editorLog;
        readonly long editorOffset;
        readonly string[] secrets;
        StreamWriter writer;
        bool finished;

        public ApkBuildLog(string flavor)
        {
            string name =
                RubezhVersion.Current
                + "-"
                + RubezhVersion.AndroidCode
                + "-"
                + flavor
                + "-"
                + began.ToString("yyyyMMdd-HHmmss-fff")
                + "-"
                + Guid.NewGuid().ToString("N").Substring(0, 6);
            Folder = Path.Combine(ProjectRoot, "Builds", "Logs", name);
            Directory.CreateDirectory(Folder);
            secrets = new[]
            {
                Environment.GetEnvironmentVariable("RUBEZH_KEYSTORE_PASS"),
                Environment.GetEnvironmentVariable("RUBEZH_KEY_PASS"),
                PlayerSettings.Android.keystorePass,
                PlayerSettings.Android.keyaliasPass,
            }
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToArray();
            writer = new StreamWriter(Path.Combine(Folder, "console.log"), false, new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            editorLog = Application.consoleLogPath;
            try
            {
                editorOffset = File.Exists(editorLog) ? new FileInfo(editorLog).Length : 0;
            }
            catch
            {
                editorOffset = 0;
            }
            try
            {
                Json(
                    "status.json",
                    new
                    {
                        status = "building",
                        startedUtc = began,
                        version = RubezhVersion.Current,
                        androidCode = RubezhVersion.AndroidCode,
                        flavor,
                    }
                );
                File.WriteAllText(Path.Combine(ProjectRoot, "Builds", "Logs", "latest.txt"), Folder);
                Application.logMessageReceivedThreaded += Receive;
            }
            catch
            {
                writer.Dispose();
                throw;
            }
        }

        string Safe(string text)
        {
            if (text == null)
                return "";
            foreach (string secret in secrets)
                text = text.Replace(secret, "[REDACTED]");
            return text;
        }

        void Receive(string condition, string stack, LogType type)
        {
            lock (gate)
            {
                if (writer == null)
                    return;
                writer.WriteLine(DateTime.UtcNow.ToString("O") + " [" + type + "] " + Safe(condition));
                if (!string.IsNullOrEmpty(stack))
                    writer.WriteLine(Safe(stack));
            }
        }

        void Json(string name, object data)
        {
            File.WriteAllText(Path.Combine(Folder, name), Safe(JsonConvert.SerializeObject(data, Formatting.Indented)));
        }

        void Section(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                File.AppendAllText(
                    Path.Combine(Folder, "report-errors.log"),
                    name + ": " + Safe(e.ToString()) + Environment.NewLine
                );
            }
        }

        public void Settings(BuildPlayerOptions options)
        {
            Json(
                "settings.json",
                new
                {
                    unity = Application.unityVersion,
                    host = SystemInfo.operatingSystem,
                    version = PlayerSettings.bundleVersion,
                    androidCode = PlayerSettings.Android.bundleVersionCode,
                    target = options.target.ToString(),
                    options = options.options.ToString(),
                    scenes = options.scenes,
                    defines = options.extraScriptingDefines,
                    output = options.locationPathName,
                    backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android).ToString(),
                    architectures = PlayerSettings.Android.targetArchitectures.ToString(),
                    minimumSdk = PlayerSettings.Android.minSdkVersion.ToString(),
                    targetSdk = PlayerSettings.Android.targetSdkVersion.ToString(),
                    stripEngineCode = PlayerSettings.stripEngineCode,
                    managedStripping = PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Android).ToString(),
                    applicationId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android),
                    customSigning = PlayerSettings.Android.useCustomKeystore,
                    appBundle = EditorUserBuildSettings.buildAppBundle,
                    exportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject,
                    note = "No passwords or environment dump. Packed sizes are not the compressed APK contribution.",
                }
            );
        }

        public void Finish(BuildReport report, Exception error = null)
        {
            if (finished)
                return;
            finished = true;
            Section(
                "summary",
                () =>
                {
                    Json(
                        "summary.json",
                        report == null
                            ? (object)
                                new
                                {
                                    result = "Failed before BuildReport",
                                    error = error?.ToString(),
                                    startedUtc = began,
                                    finishedUtc = DateTime.UtcNow,
                                }
                            : new
                            {
                                result = report.summary.result.ToString(),
                                startedUtc = began,
                                finishedUtc = DateTime.UtcNow,
                                seconds = report.summary.totalTime.TotalSeconds,
                                bytes = report.summary.totalSize,
                                warnings = report.summary.totalWarnings,
                                errors = report.summary.totalErrors,
                                output = report.summary.outputPath,
                                platform = report.summary.platform.ToString(),
                                options = report.summary.options.ToString(),
                                error = error?.ToString(),
                            }
                    );
                }
            );
            if (report != null)
            {
                Section(
                    "steps",
                    () =>
                        Json(
                            "steps-and-messages.json",
                            report.steps.Select(s => new
                            {
                                name = s.name,
                                depth = s.depth,
                                seconds = s.duration.TotalSeconds,
                                messages = s.messages.Select(m => new { type = m.type.ToString(), text = m.content }),
                            })
                        )
                );
                Section(
                    "files",
                    () =>
                        Json(
                            "build-files.json",
                            report
                                .GetFiles()
                                .Select(f => new
                                {
                                    path = f.path,
                                    role = f.role,
                                    size = f.size,
                                })
                        )
                );
                Section(
                    "packed assets",
                    () =>
                    {
                        var assets = report
                            .packedAssets.SelectMany(p =>
                                p.contents.Select(a => new
                                {
                                    container = p.shortPath,
                                    path = a.sourceAssetPath,
                                    bytes = a.packedSize,
                                    type = Convert.ToString(a.type),
                                })
                            )
                            .OrderByDescending(a => a.bytes)
                            .ToArray();
                        Json("packed-assets.json", assets);
                        var legacy = assets
                            .Where(a =>
                                (a.path ?? "").Contains("/SpritePacks/")
                                || (a.path ?? "").Contains("/UnityTestForest/")
                                || (a.path ?? "").Contains("/LegacyUnityForest/")
                                || (a.path ?? "").Contains("/LegacySources/")
                                || (a.path ?? "").Contains("/Resources/Exterior/")
                            )
                            .ToArray();
                        Json(
                            "legacy-resource-check.json",
                            new
                            {
                                passed = legacy.Length == 0,
                                entries = legacy,
                                ui = assets.Where(a => (a.path ?? "").Contains("/NativeUI/")).ToArray(),
                                nativePresentation = assets
                                    .Where(a =>
                                        (a.path ?? "").Contains("/NativeContent/Buildings/")
                                        || (a.path ?? "").Contains("/NativeExterior/")
                                    )
                                    .ToArray(),
                                note = "Packed assets report only; compare final APK sizes separately.",
                            }
                        );
                        Json("largest-assets.json", assets.Take(100));
                        Json(
                            "asset-groups.json",
                            assets
                                .GroupBy(a => string.Join("/", (a.path ?? "").Split('/').Take(4)))
                                .Select(g => new
                                {
                                    path = g.Key,
                                    bytes = g.Sum(a => (long)a.bytes),
                                    entries = g.Count(),
                                })
                                .OrderByDescending(g => g.bytes)
                        );
                        using (
                            var csv = new StreamWriter(Path.Combine(Folder, "packed-assets.csv"), false, Encoding.UTF8)
                        )
                        {
                            csv.WriteLine("bytes,source,container,type");
                            foreach (var a in assets)
                                csv.WriteLine(
                                    a.bytes + "," + Quote(a.path) + "," + Quote(a.container) + "," + Quote(a.type)
                                );
                        }
                    }
                );
                if (report.summary.result == BuildResult.Succeeded)
                    Section("APK", () => InspectApk(report.summary.outputPath));
            }
            Section(
                "resource inventory",
                () =>
                {
                    var files = Directory
                        .GetFiles(Path.Combine(ProjectRoot, "Assets"), "*", SearchOption.AllDirectories)
                        .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                        .Select(p => new
                        {
                            path = p.Substring(ProjectRoot.Length + 1).Replace('\\', '/'),
                            bytes = new FileInfo(p).Length,
                        })
                        .ToArray();
                    Json("source-inventory.json", files.OrderByDescending(f => f.bytes));
                    Json(
                        "texture-import.json",
                        files
                            .Where(f =>
                                new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(f.path).ToLowerInvariant())
                            )
                            .Select(f =>
                            {
                                var t = AssetImporter.GetAtPath(f.path) as TextureImporter;
                                var android = t == null ? null : t.GetPlatformTextureSettings("Android");
                                return new
                                {
                                    f.path,
                                    f.bytes,
                                    readable = t?.isReadable,
                                    mipmaps = t?.mipmapEnabled,
                                    maxSize = t?.maxTextureSize,
                                    compression = t?.textureCompression.ToString(),
                                    androidOverride = android?.overridden,
                                    androidFormat = android?.format.ToString(),
                                    androidMax = android?.maxTextureSize,
                                };
                            })
                    );
                    Json(
                        "source-hashes.json",
                        files
                            .Where(f =>
                                new[] { ".cs", ".asmdef", ".json", ".bytes" }.Contains(Path.GetExtension(f.path))
                            )
                            .Select(f => new
                            {
                                f.path,
                                f.bytes,
                                sha256 = BuildResourcePreparation.Hash(Path.Combine(ProjectRoot, f.path)),
                            })
                    );
                }
            );
            Section(
                "packages",
                () =>
                {
                    foreach (string name in new[] { "manifest.json", "packages-lock.json" })
                    {
                        string file = Path.Combine(ProjectRoot, "Packages", name);
                        if (File.Exists(file))
                            File.Copy(file, Path.Combine(Folder, "packages-" + name), true);
                    }
                }
            );
            Section(
                "editor log",
                () =>
                {
                    if (!File.Exists(editorLog))
                        return;
                    using (
                        var file = new FileStream(
                            editorLog,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete
                        )
                    )
                    {
                        if (file.Length >= editorOffset)
                            file.Seek(editorOffset, SeekOrigin.Begin);
                        using (var reader = new StreamReader(file))
                            File.WriteAllText(Path.Combine(Folder, "editor-build.log"), Safe(reader.ReadToEnd()));
                    }
                }
            );
            Section(
                "status",
                () =>
                    Json(
                        "status.json",
                        new
                        {
                            status = report?.summary.result.ToString() ?? "Failed",
                            finishedUtc = DateTime.UtcNow,
                            reportComplete = !File.Exists(Path.Combine(Folder, "report-errors.log")),
                        }
                    )
            );
            File.WriteAllText(
                Path.Combine(Folder, "README.txt"),
                "APK build diagnostics\nsummary.json: result/time/size; settings.json: actual build options\npacked-assets.csv/json: assets included by Unity, largest-assets.json: top 100\napk-entries.json: actual ZIP compressed/uncompressed entries; apk.json: SHA-256\nsteps-and-messages.json, console.log, editor-build.log: build stages and messages\nsource-inventory.json, source-hashes.json, texture-import.json: input evidence\nreport-errors.log exists only if a diagnostic section failed\nNo APK inspection is attempted on failure, to avoid reporting an old APK.\n"
            );
        }

        public static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

        void InspectApk(string path)
        {
            if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                Json("apk.json", new { available = false, path });
                return;
            }
            Json(
                "apk.json",
                new
                {
                    available = true,
                    path,
                    bytes = new FileInfo(path).Length,
                    sha256 = BuildResourcePreparation.Hash(path),
                }
            );
            using (var zip = ZipFile.OpenRead(path))
            {
                var entries = zip
                    .Entries.Select(e => new
                    {
                        name = e.FullName,
                        compressed = e.CompressedLength,
                        uncompressed = e.Length,
                    })
                    .OrderByDescending(e => e.compressed)
                    .ToArray();
                Json("apk-entries.json", entries);
                Json(
                    "apk-groups.json",
                    entries
                        .GroupBy(e => string.Join("/", e.name.Split('/').Take(2)))
                        .Select(g => new
                        {
                            path = g.Key,
                            compressed = g.Sum(e => e.compressed),
                            uncompressed = g.Sum(e => e.uncompressed),
                            entries = g.Count(),
                        })
                );
            }
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= Receive;
            lock (gate)
            {
                writer?.Dispose();
                writer = null;
            }
            try
            {
                ZipFile.CreateFromDirectory(
                    Folder,
                    Folder + ".zip",
                    System.IO.Compression.CompressionLevel.Optimal,
                    false
                );
            }
            catch (Exception e)
            {
                File.AppendAllText(Path.Combine(Folder, "report-errors.log"), "Report archive: " + Safe(e.ToString()));
            }
        }
    }
}
