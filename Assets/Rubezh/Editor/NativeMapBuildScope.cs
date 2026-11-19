#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Source maps remain available to editor tooling, but must not be embedded twice in the Player.
    public sealed class NativeMapBuildScope : IDisposable, IPreprocessBuildWithReport
    {
        const string JournalPath = "Library/RubezhNativeMapMoves.json";
        const string Holding = "Assets/Rubezh/Editor/NativeBuildSources";

        [Serializable]
        public sealed class Move
        {
            public string source,
                target;
            public long bytes;
        }

        [Serializable]
        public sealed class Journal
        {
            public string utc,
                reportFolder;
            public Move[] moves;
        }

        static Journal journal;
        static bool active;
        public int callbackOrder => -3000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!active)
                throw new BuildFailedException(
                    "Для безопасного исключения дубликатов карт используйте Рубеж → Быстрая сборка APK / Релизный APK / Версия разработчика → Собрать APK. Обычная Build не переносит исходники автоматически."
                );
        }

        static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        public static string SourcePath(string original)
        {
            if (journal != null)
                foreach (var move in journal.moves)
                    if (original == move.source || original.StartsWith(move.source + "/", StringComparison.Ordinal))
                    {
                        string moved = move.target + original.Substring(move.source.Length);
                        if (Exists(moved))
                            return moved;
                    }
            return original;
        }

        public static NativeMapBuildScope Begin(string reportFolder)
        {
            if (active)
                throw new BuildFailedException("Native map build scope is already active");
            Recover();
            NativeContentPreparation.RequirePrepared();
            Directory.CreateDirectory(Holding);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var paths = new[] { "Regenerated", "War", "Levels", "CampaignBaseline.bytes" };
            var pending = new Journal
            {
                utc = DateTime.UtcNow.ToString("O"),
                reportFolder = reportFolder,
                moves = paths
                    .Select(name => new Move
                    {
                        source = "Assets/Rubezh/Resources/" + name,
                        target = Holding + "/" + name,
                    })
                    .Where(m => Exists(m.source))
                    .ToArray(),
            };
            foreach (var move in pending.moves)
            {
                if (Exists(move.target))
                    throw new BuildFailedException("Build holding path already exists: " + move.target);
                move.bytes = File.Exists(move.source)
                    ? new FileInfo(move.source).Length
                    : Directory
                        .GetFiles(move.source, "*", SearchOption.AllDirectories)
                        .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                        .Sum(p => new FileInfo(p).Length);
            }
            Directory.CreateDirectory("Library");
            journal = pending;
            File.WriteAllText(JournalPath, JsonUtility.ToJson(journal, true)); // Persist intent before the first move.
            try
            {
                foreach (var move in journal.moves)
                    MoveAsset(move.source, move.target);
                active = true;
                Directory.CreateDirectory(reportFolder);
                File.WriteAllText(
                    Path.Combine(reportFolder, "excluded-map-sources.json"),
                    JsonUtility.ToJson(journal, true)
                );
                NativeContentPreparation.RequirePrepared();
                return new NativeMapBuildScope();
            }
            catch
            {
                Restore();
                throw;
            }
        }

        static void MoveAsset(string from, string to)
        {
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error))
                throw new BuildFailedException("Cannot move " + from + " to " + to + ": " + error);
        }

        public void Dispose()
        {
            Restore();
        }

        static void Restore()
        {
            active = false;
            if (journal == null)
                return;
            foreach (var move in journal.moves.Reverse())
            {
                if (!Exists(move.target))
                {
                    if (!Exists(move.source))
                        throw new BuildFailedException("Both map source and backup missing: " + move.source);
                    continue;
                }
                if (Exists(move.source))
                    throw new BuildFailedException(
                        "Recovery stopped to preserve both map copies: " + move.source + " and " + move.target
                    );
                MoveAsset(move.target, move.source);
            }
            File.Delete(JournalPath);
            journal = null;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        [MenuItem("Рубеж/Сборка/Восстановить исходники карт после сбоя")]
        public static void Recover()
        {
            if (active)
                throw new BuildFailedException("Cannot recover map sources during a build");
            if (!File.Exists(JournalPath))
                return;
            journal = JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
            if (journal == null || journal.moves == null)
                throw new BuildFailedException("Invalid map recovery journal: " + JournalPath);
            Restore();
            Debug.Log("RUBEZH: исходники карт восстановлены после прерванной сборки.");
        }

        [InitializeOnLoadMethod]
        static void ScheduleRecovery()
        {
            EditorApplication.delayCall += RecoverWhenIdle;
        }

        static void RecoverWhenIdle()
        {
            if (active)
                return;
            if (
                EditorApplication.isCompiling
                || EditorApplication.isPlayingOrWillChangePlaymode
                || BuildPipeline.isBuildingPlayer
            )
            {
                EditorApplication.delayCall += RecoverWhenIdle;
                return;
            }
            try
            {
                Recover();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
#endif
