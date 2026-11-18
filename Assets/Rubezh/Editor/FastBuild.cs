using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public static class FastBuild
    {
        const string Scene = "Assets/Scenes/Battle.unity";
        [MenuItem("Рубеж/Быстрая сборка APK", false, 1)]
        public static void AndroidFast()
        {
            Build(true);
        }

        [MenuItem("Рубеж/Релизный APK", false, 2)]
        public static void AndroidRelease()
        {
            Build(false);
        }

        [MenuItem("Рубеж/Открыть сцену", false, 20)]
        public static void Open()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(Scene);
        }

        [MenuItem("Рубеж/Версия разработчика/Собрать APK", false, 3)]
        public static void AndroidDeveloper()
        {
            Build(true, true);
        }

        [MenuItem("Рубеж/Версия разработчика/Включить в Play Mode", false, 4)]
        public static void ToggleDeveloper()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("Сначала остановите Play Mode.");
                return;
            }

            EditorPrefs.SetBool("Rubezh.DeveloperPreview", !EditorPrefs.GetBool("Rubezh.DeveloperPreview", false));
        }

        [MenuItem("Рубеж/Версия разработчика/Включить в Play Mode", true)]
        static bool DeveloperChecked()
        {
            Menu.SetChecked("Рубеж/Версия разработчика/Включить в Play Mode", EditorPrefs.GetBool("Rubezh.DeveloperPreview", false));
            return !EditorApplication.isPlaying;
        }

        static void Build(bool dev, bool developer = false)
        {
            string flavor=developer?"Developer":dev?"Dev":"Release";
            using(var log=new ApkBuildLog(flavor))
            using(var settings=new BuildSettingsScope())
            {
                BuildReport report=null;
                try
                {
                    if(EditorApplication.isCompiling)throw new BuildFailedException("Дождитесь компиляции скриптов.");
                    if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))throw new BuildFailedException("Установите Android Build Support, SDK/NDK и OpenJDK через Unity Hub.");
                    if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
                        throw new BuildFailedException("Переключите Build Profiles на Android, дождитесь импорта и повторите сборку. Это исключает сборку во время смены платформы.");
                    BuildResourcePreparation.Prepare();
                    NativeContentPreparation.BuildContent(log.Folder);
                    PlayerSettings.companyName="Rubezh";
                    PlayerSettings.productName="Рубеж "+RubezhVersion.Current+(developer?" DEVELOPER":dev?" (dev)":"");
                    PlayerSettings.bundleVersion=RubezhVersion.Current;
                    PlayerSettings.Android.bundleVersionCode=RubezhVersion.AndroidCode;
                    PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,developer?"net.silentorders.unitytest.ai.developer":"net.silentorders.unitytest.ai");
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
                    PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
                    PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
                    PlayerSettings.runInBackground=false;
                    // Conservative managed stripping; preserve JSON DTOs explicitly in link.xml.
                    PlayerSettings.stripEngineCode=!dev;
                    PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,ManagedStrippingLevel.Minimal);
                    EditorUserBuildSettings.buildAppBundle=false;
                    EditorUserBuildSettings.exportAsGoogleAndroidProject=false;
                    ConfigureSigning();
                    string output=Path.Combine(ApkBuildLog.ProjectRoot,"Builds","Android",flavor,"Rubezh-"+RubezhVersion.Current+(developer?"-developer":dev?"-dev":"")+".apk");
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    BuildOptions flags=BuildOptions.StrictMode|BuildOptions.DetailedBuildReport|(dev?BuildOptions.CompressWithLz4:BuildOptions.CompressWithLz4HC);
                    if(dev)flags|=BuildOptions.Development;
                    var options=new BuildPlayerOptions{scenes=new[]{Scene},locationPathName=output,target=BuildTarget.Android,options=flags,extraScriptingDefines=developer?new[]{"RUBEZH_DEVELOPER"}:new string[0]};
                    log.Settings(options);
                    using (NativeMapBuildScope.Begin(log.Folder))
                        report=BuildPipeline.BuildPlayer(options);
                    log.Finish(report);
                    if(report.summary.result!=BuildResult.Succeeded)throw new BuildFailedException("Сборка APK не завершена. Отчёт: "+log.Folder);
                    Debug.Log("RUBEZH APK: "+output+"\nПолный отчёт: "+log.Folder);
                }
                catch(Exception e){log.Finish(report,e);throw;}
            }
        }

        static void ConfigureSigning()
        {
            string file = Environment.GetEnvironmentVariable("RUBEZH_KEYSTORE"), alias = Environment.GetEnvironmentVariable("RUBEZH_KEY_ALIAS");
            if (string.IsNullOrEmpty(file))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                return;
            }

            string pass = Environment.GetEnvironmentVariable("RUBEZH_KEYSTORE_PASS"), keypass = Environment.GetEnvironmentVariable("RUBEZH_KEY_PASS");
            if (!File.Exists(file) || string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(keypass))
                throw new BuildFailedException("Signing environment is incomplete.");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = file;
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasName = alias;
            PlayerSettings.Android.keyaliasPass = keypass;
        }
    }
}
