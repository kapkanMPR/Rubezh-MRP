// File-backed stand-in for Unity's AssetDatabase, for migration regression tests.
// This executes production C# against real temporary files, not a Unity import.
using System;
using System.IO;
using System.Collections.Generic;
namespace UnityEditor
{
    public class MenuItem:Attribute { public MenuItem(string name){} }
    public class InitializeOnLoadMethodAttribute:Attribute{}
    public enum ImportAssetOptions { ForceSynchronousImport=1 }
    public static class EditorApplication
    {
        public static bool isPlayingOrWillChangePlaymode,isCompiling;
        public static Action delayCall;
    }
    public static class BuildPipeline { public static bool isBuildingPlayer; }
    public static class PlayerSettings
    {
        public static string bundleVersion;
        public static class Android { public static int bundleVersionCode; }
    }
    public static class AssetDatabase
    {
        public static bool FailMove,FailDelete;
        public static void Refresh(ImportAssetOptions options=0){}
        public static void ImportAsset(string path,ImportAssetOptions options){}
        public static bool IsValidFolder(string path)=>Directory.Exists(path);
        public static string[] GetAllAssetPaths()=>Directory.GetFiles("Assets","*",SearchOption.AllDirectories);
        public static string MoveAsset(string source,string target)
        {
            if(FailMove)return "Injected move failure";
            if(File.Exists(target)||Directory.Exists(target)||File.Exists(target+".meta"))return "Destination exists";
            if(Directory.Exists(source))Directory.Move(source,target);
            else File.Move(source,target);
            if(File.Exists(source+".meta"))File.Move(source+".meta",target+".meta");
            return "";
        }
        public static bool DeleteAsset(string path)
        {
            if(FailDelete)return false;
            if(Directory.Exists(path))Directory.Delete(path,true);
            else if(File.Exists(path))File.Delete(path);
            else return false;
            if(File.Exists(path+".meta"))File.Delete(path+".meta");
            return true;
        }
    }
}
namespace UnityEditor.Build
{
    public interface IPreprocessBuildWithReport {int callbackOrder{get;}void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report);}
    public class BuildFailedException:Exception {public BuildFailedException(string message):base(message){}}
}
namespace UnityEditor.Build.Reporting {public class BuildReport{}}
namespace UnityEngine
{
    public static class Debug
    {
        public static readonly List<string> Warnings=new List<string>();
        public static void Log(string message){}
        public static void LogWarning(string message){Warnings.Add(message);}
        public static void LogException(Exception exception){throw exception;}
    }
}
namespace Rubezh.UnityPort
{
    public static class RubezhVersion {public const string Current="test";public const int AndroidCode=322;}
}
namespace Rubezh.UnityPort.Editor
{
    public static class NativeMapBuildScope {public static string SourcePath(string path)=>path;}
}
