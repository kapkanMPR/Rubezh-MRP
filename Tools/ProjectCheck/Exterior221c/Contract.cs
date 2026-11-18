// Portable API shapes only. Image encoding/import are deliberately not simulated.
using System;
using System.Text.Json;
namespace UnityEngine {
 public class Object {public static void DestroyImmediate(Object o){} }
 public enum TextureFormat {RGBA32}
 public enum FilterMode {Bilinear}
 public enum TextureWrapMode {Clamp}
 public class Texture2D:Object {
  public Texture2D(int w,int h,TextureFormat f,bool mip,bool linear){}
  public void SetPixelData(uint[] pixels,int mip){} public void Apply(bool a,bool b){}
  public byte[] EncodeToPNG()=>new byte[]{80,78,71};
 }
 public static class JsonUtility {
  static readonly JsonSerializerOptions options=new JsonSerializerOptions{IncludeFields=true};
  public static T FromJson<T>(string s)=>JsonSerializer.Deserialize<T>(s,options);
  public static string ToJson(object o,bool pretty)=>JsonSerializer.Serialize(o,options);
 }
}
namespace UnityEditor {
 public class AssetImporter {}
 public class AssetPostprocessor {protected string assetPath;protected AssetImporter assetImporter;}
 public enum TextureImporterType {Default}
 public enum TextureImporterNPOTScale {None}
 public enum TextureImporterShape {Texture2D,Cube}
 public enum TextureImporterFormat {ASTC_4x4,BC7}
 public class TextureImporterPlatformSettings {public string name;public bool overridden;public int maxTextureSize,compressionQuality;public TextureImporterFormat format;}
 public class TextureImporter:AssetImporter {
  public TextureImporterType textureType;public TextureImporterShape textureShape;public bool mipmapEnabled,isReadable,sRGBTexture,alphaIsTransparency;
  public TextureImporterNPOTScale npotScale;public int maxTextureSize;public UnityEngine.FilterMode filterMode;public UnityEngine.TextureWrapMode wrapMode;
  public void SetPlatformTextureSettings(TextureImporterPlatformSettings s){}
 }
}
namespace Rubezh.UnityPort {
 public static class ExteriorSpriteLoader {
  [Serializable] public class Entry {public string sourceSha256,key;public int page,x,y,w,h;public float left,top,width,height;}
  [Serializable] public class Catalog {public int version=1,size=2048;public Entry[] entries;}
 }
}
