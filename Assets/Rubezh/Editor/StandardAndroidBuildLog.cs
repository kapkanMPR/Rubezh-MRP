using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace Rubezh.UnityPort.Editor
{
    // Also cover Build / Build And Run in Unity's standard window.
    static class StandardAndroidBuildLog
    {
        [InitializeOnLoadMethod] static void Register(){BuildPlayerWindow.RegisterBuildPlayerHandler(Build);}
        static void Build(BuildPlayerOptions options)
        {
            if(options.target!=BuildTarget.Android){BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);return;}
            using(var log=new ApkBuildLog("BuildProfiles"))
            using(var settings=new BuildSettingsScope())
            {
                BuildReport report=null;
                var previous=BuildReport.GetLatestReport();
                try
                {
                    BuildResourcePreparation.Prepare();
                    PlayerSettings.bundleVersion=RubezhVersion.Current;PlayerSettings.Android.bundleVersionCode=RubezhVersion.AndroidCode;
                    bool dev=(options.options&BuildOptions.Development)!=0;
                    PlayerSettings.stripEngineCode=!dev;
                    PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Android,ManagedStrippingLevel.Minimal);
                    options.options&=~(BuildOptions.CompressWithLz4|BuildOptions.CompressWithLz4HC);
                    options.options|=BuildOptions.DetailedBuildReport|(dev?BuildOptions.CompressWithLz4:BuildOptions.CompressWithLz4HC);
                    log.Settings(options);
                    // Keep Unity's built-in install/run handling intact.
                    BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
                    report=BuildReport.GetLatestReport();
                    if(report==previous||(report!=null&&report.summary.buildStartedAt.ToUniversalTime()<log.StartedUtc.AddSeconds(-1)))report=null;
                    log.Finish(report);
                }
                catch(Exception e){var latest=BuildReport.GetLatestReport();if(latest!=null&&latest!=previous&&latest.summary.buildStartedAt.ToUniversalTime()>=log.StartedUtc.AddSeconds(-1))report=latest;log.Finish(report,e);throw;}
            }
        }
    }
}
