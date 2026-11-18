using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public static class WebBuild
    {
        [MenuItem("Рубеж/Веб-версия/Собрать", false, 5)]
        public static void Build()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                throw new BuildFailedException("Установите Web Build Support для Unity 6000.3.16f1 через Unity Hub.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                throw new BuildFailedException("Выберите платформу Web в Build Profiles и повторите сборку. Для командной строки используйте -buildTarget WebGL.");
            string oldTemplate = PlayerSettings.WebGL.template;
            var oldCompression = PlayerSettings.WebGL.compressionFormat;
            bool oldFallback = PlayerSettings.WebGL.decompressionFallback;
            try
            {
                PlayerSettings.WebGL.template = "PROJECT:Rubezh";
                // Compression with fallback works without special server encoding headers.
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                string output = "Builds/Web";
                Directory.CreateDirectory(output);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Battle.unity" }, locationPathName = output, target = BuildTarget.WebGL, options = BuildOptions.StrictMode, extraScriptingDefines = new[] { "RUBEZH_WEB_PREVIEW" } });
                Directory.CreateDirectory("Validation");
                File.WriteAllText("Validation/last-web-build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\n" + report.summary.totalTime);
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException("Не удалось собрать Web. См. Logs/web-build.log.");
                Debug.Log("RUBEZH WEB: " + Path.GetFullPath(output));
            }
            finally
            {
                PlayerSettings.WebGL.template = oldTemplate;
                PlayerSettings.WebGL.compressionFormat = oldCompression;
                PlayerSettings.WebGL.decompressionFallback = oldFallback;
            }
        }
    }
}
