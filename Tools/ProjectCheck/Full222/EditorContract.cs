using System.Text.Json;
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
        public static readonly Dictionary<string,UnityEngine.Object> MainAssets=new Dictionary<string,UnityEngine.Object>();
        public static UnityEngine.Object LoadMainAssetAtPath(string path)=>MainAssets.TryGetValue(path,out var value)?value:null;
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

// Portable API shapes only. Image encoding/import are deliberately not simulated.
namespace UnityEngine {
 public class Object {public static void DestroyImmediate(Object o){} }
 public enum TextureFormat {RGBA32}
 public enum FilterMode {Bilinear}
 public enum TextureWrapMode {Clamp}
 public class Texture2D:Object {
  public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear=false){}
  public void LoadRawTextureData(byte[] bytes){}
  public void SetPixelData(uint[] pixels,int mip){} public void Apply(bool a=false,bool b=false){}
  public byte[] EncodeToPNG()=>new byte[]{80,78,71};
 }
 public static class JsonUtility {
  static readonly JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
  public static T FromJson<T>(string s)=>JsonSerializer.Deserialize<T>(s,options);
  public static string ToJson(object o,bool pretty)=>JsonSerializer.Serialize(o,options);
 }
}
namespace UnityEditor {
 public class AssetImporter {public string path;public static readonly System.Collections.Generic.Dictionary<string,AssetImporter> Importers=new System.Collections.Generic.Dictionary<string,AssetImporter>();public static AssetImporter GetAtPath(string p)=>Importers.TryGetValue(p,out var value)?value:null;}
 public class AssetPostprocessor {public string assetPath;public AssetImporter assetImporter;public virtual int GetPostprocessOrder()=>0;public virtual uint GetVersion()=>0;}
 public enum TextureImporterType {Default,Sprite}
 public enum TextureImporterNPOTScale {None}
 public enum TextureImporterShape {Texture2D,Cube}
 public enum TextureImporterFormat {ASTC_4x4,BC7}
 public class TextureImporterPlatformSettings {public string name;public bool overridden;public int maxTextureSize,compressionQuality;public TextureImporterFormat format;}
 public class TextureImporter:AssetImporter {
  public TextureImporterType textureType;public TextureImporterShape textureShape;public bool mipmapEnabled,isReadable,sRGBTexture,alphaIsTransparency;
  public TextureImporterNPOTScale npotScale;public int maxTextureSize;public UnityEngine.FilterMode filterMode;public UnityEngine.TextureWrapMode wrapMode;
  public int reimports;public void SaveAndReimport(){reimports++;AssetDatabase.MainAssets[path]=textureShape==TextureImporterShape.Texture2D?(UnityEngine.Object)new UnityEngine.Texture2D(2,2,UnityEngine.TextureFormat.RGBA32,false):new UnityEngine.Object();}
  public void SetPlatformTextureSettings(TextureImporterPlatformSettings s){}
 }
}
namespace Rubezh.UnityPort {
 public static class ExteriorSpriteLoader {
  [Serializable] public class Entry {public string sourceSha256,key;public int page,x,y,w,h;public float left,top,width,height;}
  [Serializable] public class Catalog {public int version=1,size=2048;public Entry[] entries;}
 }
}

namespace Rubezh.UnityPort {
 public static class NativeUiArt {public class Entry {public int page;}public class Catalog {public int version;public Entry[] entries;}}
 public static class NativeUiRecipe {public const int Size=2048;public static readonly string[] Icons=new string[0];public static NativeUiArt.Catalog Bake(System.Action<int,uint[]> save,System.Action<int> ensure)=>throw new System.NotSupportedException("Compile-only UI recipe contract");}
 public static class Rubezh {public static class GreenLook {public static readonly int[] ActiveHeads=new int[0];}public static class Graphics {public static void ReleaseSceneSprites(){}}}
}
namespace Rubezh.UnityPort.Editor {public static class EditorSpritePacks {public static void Ensure(int id){}}}
