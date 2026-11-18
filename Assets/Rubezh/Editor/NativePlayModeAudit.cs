#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Rubezh.UnityPort.Editor
{
    // Explicit native-Unity checks. Portable API contracts cannot validate cameras, atlases or UI.
    [InitializeOnLoad]
    public static class NativePlayModeAudit
    {
        [Serializable] sealed class Frame
        {
            public double seconds;
            public float deltaMs;
            public long managedBytes, textureBytes, meshBytes;
            public int textures, meshes, loadedAtlases;
            public bool nativeLease;
            public string screenshot, performance;
        }
        [Serializable] sealed class Audit
        {
            public string version, unity, platform, utc, kind, status, note;
            public List<Frame> samples = new List<Frame>();
            public List<string> passedMaps = new List<string>(), errors = new List<string>();
        }
        static Audit report;
        static string folder;
        static double started, nextSample;
        static bool mapsMode;
        static IEnumerator<NativeContentAudit.MapEntry> maps;
        static NativeContentAudit.MapEntry current;
        static AsyncOperationHandle<TextAsset> mapHandle;
        static bool ownsHandle;
        static NativePlayModeAudit()
        {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.ExitingPlayMode && report != null) Finish("Cancelled: exited Play Mode");
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => { if (report != null) Finish("Cancelled: domain reload"); };
        }
        [MenuItem("Рубеж/Unity/Play Mode — проверить загрузку 70 карт")]
        public static void CheckMaps()
        {
            if (!Begin("MapLoadRelease")) return;
            mapsMode = true;maps = NativeContentAudit.Maps().GetEnumerator();
            report.note = "Sequential real Addressables load, level validation and release. Does not simulate/render all maps or prove GPU release timing.";
        }
        [MenuItem("Рубеж/Unity/Play Mode — записать 10 секунд текущего боя")]
        public static void CaptureBattle()
        {
            if (!Begin("BattleCapture")) return;
            if (NativeBattleAssets.Active == null) { report.errors.Add("Open a native battle before capture");Finish("Failed");return; }
            mapsMode = false;
            report.note = "Current game view, 10 seconds; screenshots at 0/5/10 seconds. Texture/mesh numbers are whole-Editor totals, not isolated player memory. Compare repeated capture and post-exit snapshots.";
        }
        [MenuItem("Рубеж/Unity/Play Mode — проверить освобождение после боя")]
        public static void CheckReleased()
        {
            if (!Begin("PostBattleRelease")) return;
            report.note = "Lease ownership check after returning to camp. Addressables unload may be deferred; process memory need not immediately shrink.";
            report.samples.Add(Snapshot());
            if (NativeBattleAssets.Active != null) report.errors.Add("Native battle lease remains active. First leave the battle and wait for loading to finish.");
            var app = UnityEngine.Object.FindFirstObjectByType<RubezhApp>();
            if (app != null)
            {
                foreach (string name in new[] { "hdBattle", "nativeBattle", "nativeAssets", "testHudObject" })
                {
                    var field = typeof(RubezhApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field != null && field.GetValue(app) != null) report.errors.Add("Battle-owned reference remains: " + name);
                }
            }
            Finish(report.errors.Count == 0 ? "Passed" : "Failed");
        }
        [MenuItem("Рубеж/Unity/Остановить проверку Play Mode")]
        public static void Cancel() { if (report != null) Finish("Cancelled by user"); }
        static bool Begin(string kind)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) { Debug.LogWarning("Запустите Play Mode и снимите паузу редактора.");return false; }
            if (report != null) { Debug.LogWarning("Дождитесь проверки или остановите её через меню Рубеж/Unity.");return false; }
            folder = NativeContentAudit.ReportFolder(kind);
            report = new Audit { version = RubezhVersion.Current, unity = Application.unityVersion,
                platform = Application.platform.ToString(), utc = DateTime.UtcNow.ToString("O"), kind = kind, status = "Running" };
            started = EditorApplication.timeSinceStartup;nextSample = started;
            Application.logMessageReceived += Log;EditorApplication.update += Tick;
            Save();return true;
        }
        static void Log(string condition, string trace, LogType type)
        {
            if (report != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                report.errors.Add(condition + "\n" + trace);
        }
        static void Tick()
        {
            if (report == null) return;
            try
            {
                if (!EditorApplication.isPlaying) { Finish("Cancelled: not playing");return; }
                if (mapsMode) { MapTick();return; }
                double now = EditorApplication.timeSinceStartup;
                if (now < nextSample) return;
                nextSample = now + 1;
                var sample = Snapshot();report.samples.Add(sample);
                if (report.samples.Count == 1 || report.samples.Count == 6 || report.samples.Count == 11)
                {
                    sample.screenshot = "frame-" + (report.samples.Count - 1).ToString("D2") + ".png";
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(folder, sample.screenshot)));
                }
                Save();
                if (report.samples.Count >= 11) Finish(report.errors.Count == 0 ? "Captured" : "Captured with errors");
            }
            catch (Exception e) { report.errors.Add(e.ToString());Finish("Failed"); }
        }
        static void MapTick()
        {
            if (!ownsHandle)
            {
                if (!maps.MoveNext()) { Finish(report.errors.Count == 0 ? "Passed" : "Failed");return; }
                current = maps.Current;mapHandle = Addressables.LoadAssetAsync<TextAsset>(current.address);ownsHandle = true;
                nextSample = EditorApplication.timeSinceStartup + 60;
                return;
            }
            if (!mapHandle.IsDone)
            {
                if (EditorApplication.timeSinceStartup > nextSample) throw new TimeoutException("Map loading timed out: " + current.address);
                return;
            }
            RubezhLevel level = null;
            try
            {
                if (mapHandle.Status != AsyncOperationStatus.Succeeded) throw new InvalidOperationException(current.address, mapHandle.OperationException);
                level = JsonUtility.FromJson<BuiltinCampaign.Map>(PackedLevel.Read(mapHandle.Result)).Create();
                string error;
                if (!LevelValidation.Valid(level, out error)) throw new InvalidDataException(current.address + ": " + error);
                report.passedMaps.Add(current.address);
            }
            finally
            {
                if (level != null) UnityEngine.Object.Destroy(level);
                ReleaseMap();
            }
            Save();
        }
        static Frame Snapshot()
        {
            var frame = new Frame { seconds = EditorApplication.timeSinceStartup - started, deltaMs = Time.unscaledDeltaTime * 1000,
                managedBytes = GC.GetTotalMemory(false), nativeLease = NativeBattleAssets.Active != null,
                loadedAtlases = NativeBattleAssets.Active == null ? 0 : NativeBattleAssets.Active.LoadedAtlases };
            foreach (var texture in Resources.FindObjectsOfTypeAll<Texture>()) { frame.textures++;frame.textureBytes += Profiler.GetRuntimeMemorySizeLong(texture); }
            foreach (var mesh in Resources.FindObjectsOfTypeAll<Mesh>()) { frame.meshes++;frame.meshBytes += Profiler.GetRuntimeMemorySizeLong(mesh); }
            var app = UnityEngine.Object.FindFirstObjectByType<RubezhApp>();
            if (app != null)
            {
                var capture = typeof(RubezhApp).GetMethod("CapturePerformance", BindingFlags.Instance | BindingFlags.NonPublic);
                if (capture != null) frame.performance = JsonUtility.ToJson(capture.Invoke(app, null), false);
            }
            return frame;
        }
        static void ReleaseMap()
        {
            if (ownsHandle && mapHandle.IsValid()) Addressables.Release(mapHandle);
            ownsHandle = false;
        }
        static void Save() { File.WriteAllText(Path.Combine(folder, "native-playmode.json"), JsonUtility.ToJson(report, true)); }
        static void Finish(string status)
        {
            if (report == null) return;
            ReleaseMap();maps?.Dispose();maps = null;current = null;
            report.status = status;Save();report = null;
            EditorApplication.update -= Tick;Application.logMessageReceived -= Log;
            Debug.Log("Native Unity audit: " + status + "\n" + Path.GetFullPath(folder));
        }
    }
}
#endif
