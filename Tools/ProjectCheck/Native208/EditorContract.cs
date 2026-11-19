using System;
using UnityEngine;
namespace UnityEditor {
 public class MenuItem:Attribute { public MenuItem(string s){} }
 public static class EditorApplication {public static bool isPlayingOrWillChangePlaymode,isCompiling;public static event Action update;public static void Tick()=>update?.Invoke();}
 public static class AssemblyReloadEvents {public static event Action beforeAssemblyReload;public static void Reload()=>beforeAssemblyReload?.Invoke();}
 public static class BuildPipeline {public static bool isBuildingPlayer;}
 public enum BuildTarget {Android}
 public static class EditorUserBuildSettings {public static BuildTarget activeBuildTarget;}
 [Flags] public enum ImportAssetOptions {ForceSynchronousImport=1,ForceUpdate=2}
 public class AssetImporter {public static AssetImporter GetAtPath(string path)=>new TextureImporter();}
 public enum TextureImporterShape { Texture2D, Cube }
 public class TextureImporter:AssetImporter { public TextureImporterShape textureShape; public SpriteImportMode spriteImportMode;public float spritePixelsPerUnit;public void ReadTextureSettings(TextureImporterSettings s){}public void SetTextureSettings(TextureImporterSettings s){} public TextureImporterType textureType;public bool sRGBTexture,alphaIsTransparency,isReadable,mipmapEnabled;public TextureImporterNPOTScale npotScale;public TextureWrapMode wrapMode;public FilterMode filterMode;public int maxTextureSize; public void SetPlatformTextureSettings(TextureImporterPlatformSettings s){}public TextureImporterPlatformSettings GetPlatformTextureSettings(string s)=>new TextureImporterPlatformSettings{format=s=="Android"?TextureImporterFormat.ASTC_4x4:TextureImporterFormat.BC7};}
 public enum TextureImporterType {Default,Sprite}public enum TextureImporterNPOTScale{None}public enum TextureImporterFormat{ASTC_4x4,BC7}
 public class TextureImporterPlatformSettings {public string name;public bool overridden;public int maxTextureSize,compressionQuality;public TextureImporterFormat format;public TextureImporterCompression textureCompression;}
 public static class AssetDatabase {public static int Imports;public static void Refresh(){}public static string MoveAsset(string a,string b)=>"";public static string AssetPathToGUID(string p)=>p;public static void CreateAsset(UnityEngine.Object o,string p){}public static void SaveAssets(){}public static void ImportAsset(string p,ImportAssetOptions o=0){Imports++;}public static T LoadAssetAtPath<T>(string p) where T:UnityEngine.Object=>null;public static bool DeleteAsset(string p)=>true;}
 public class EditorWindow {public static T GetWindow<T>(string s) where T:new()=>new T();public void Show(){}public void Repaint(){}}
 public enum MessageType{Info,None}
 public static class EditorGUILayout {public static void HelpBox(string s,MessageType m){}}
 public static class EditorUtility {public static void SetDirty(UnityEngine.Object o){}public static bool DisplayCancelableProgressBar(string a,string b,float c)=>false;public static void ClearProgressBar(){}public static void RevealInFinder(string p){}}
 public class AssetPostprocessor {public string assetPath;public AssetImporter assetImporter;}
}
namespace UnityEditor.Build {public interface IPreprocessBuildWithReport{int callbackOrder{get;}void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport r);}public class BuildFailedException:Exception {public BuildFailedException(string s):base(s){}}}
namespace UnityEditor.Build.Reporting {public class BuildReport{}}
namespace UnityEngine {public static class GUILayout {public static bool Button(string s)=>false;}}
