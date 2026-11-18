// Compile-only editor API surface. Does not claim execution in Unity.
using System;using UnityEngine;
namespace UnityEditor {
 public class DefaultAsset:UnityEngine.Object{}
 public enum SpriteImportMode{Single}
 public enum TextureImporterCompression{Compressed}
 public class TextureImporterSettings{public SpriteMeshType spriteMeshType;}
 public static class PrefabUtility{public static GameObject SaveAsPrefabAsset(GameObject go,string path)=>go;}
}
namespace UnityEngine {public enum SpriteMeshType{FullRect}}
namespace UnityEditor.U2D {
 public struct SpriteAtlasPackingSettings{public int blockOffset,padding;public bool enableRotation,enableTightPacking;}
 public struct SpriteAtlasTextureSettings{public bool readable,generateMipMaps,sRGB;public FilterMode filterMode;}
 public static class SpriteAtlasExtensions {
 public static void SetPackingSettings(this UnityEngine.U2D.SpriteAtlas a,SpriteAtlasPackingSettings s){}
 public static void SetTextureSettings(this UnityEngine.U2D.SpriteAtlas a,SpriteAtlasTextureSettings s){}
 public static void SetPlatformSettings(this UnityEngine.U2D.SpriteAtlas a,UnityEditor.TextureImporterPlatformSettings s){}
 public static void SetIncludeInBuild(this UnityEngine.U2D.SpriteAtlas a,bool b){}
 public static UnityEngine.Object[] GetPackables(this UnityEngine.U2D.SpriteAtlas a)=>new UnityEngine.Object[0];
 public static void Remove(this UnityEngine.U2D.SpriteAtlas a,UnityEngine.Object[] o){}
 public static void Add(this UnityEngine.U2D.SpriteAtlas a,UnityEngine.Object[] o){}
 }
}
namespace UnityEditor.AddressableAssets {
 public static class AddressableAssetSettingsDefaultObject {public static Settings.AddressableAssetSettings Settings;public static Settings.AddressableAssetSettings GetSettings(bool create)=>new Settings.AddressableAssetSettings();}
}
namespace UnityEditor.AddressableAssets.Settings {
 public class AddressableAssetSettings:ScriptableObject {
  public const string kLocalBuildPath="",kLocalLoadPath="";
  public AddressableAssetGroup FindGroup(string s)=>null;
  public AddressableAssetGroup CreateGroup(string a,bool b,bool c,bool d,System.Collections.Generic.List<AddressableAssetGroupSchema> schemas,params Type[] types)=>new AddressableAssetGroup();
  public AddressableAssetEntry CreateOrMoveEntry(string guid,AddressableAssetGroup group)=>new AddressableAssetEntry();
  public static void BuildPlayerContent(out Build.AddressablesPlayerBuildResult r){r=new Build.AddressablesPlayerBuildResult();}
 }
 public class AddressableAssetEntry {public string address;}
 public class AddressableAssetGroup:ScriptableObject{public T GetSchema<T>() where T:new()=>new T();}
 public class AddressableAssetGroupSchema:ScriptableObject{}
 public class ProfileValueReference{public void SetVariableByName(AddressableAssetSettings s,string n){}}
}
namespace UnityEditor.AddressableAssets.Settings.GroupSchemas {
 public class BundledAssetGroupSchema:AddressableAssetGroupSchema{public enum BundlePackingMode{PackSeparately}public BundlePackingMode BundleMode;public ProfileValueReference BuildPath=new ProfileValueReference(),LoadPath=new ProfileValueReference();}
 public class ContentUpdateGroupSchema:AddressableAssetGroupSchema{}
}
namespace UnityEditor.AddressableAssets.Build {public class AddressablesPlayerBuildResult{public string Error;}}
namespace Rubezh.UnityPort.Editor{public static class BuildResourcePreparation{public static string Hash(string p)=>p;}}

namespace UnityEditor { public enum SpritePackerMode { AlwaysOnAtlas=4 } public static class EditorSettings { public static SpritePackerMode spritePackerMode; } }
