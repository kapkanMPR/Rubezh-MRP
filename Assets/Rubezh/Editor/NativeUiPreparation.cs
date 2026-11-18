#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
namespace Rubezh.UnityPort.Editor {
 public static class NativeUiPreparation {
  public const string Root="Assets/Rubezh/Resources/NativeUI";
  [MenuItem("Рубеж/Unity/Пересобрать атлас интерфейса")]
  public static void Rebuild(){
   Directory.CreateDirectory(Root);
   try{
    var catalog=NativeUiRecipe.Bake((page,pixels)=>{
     // PixelCanvas is top-down; Unity raw texture storage is bottom-up.
     int size=NativeUiRecipe.Size;var bytes=new byte[pixels.Length*4];
     for(int y=0;y<size;y++)Buffer.BlockCopy(pixels,y*size*4,bytes,(size-1-y)*size*4,size*4);
     var t=new Texture2D(size,size,TextureFormat.RGBA32,false);
     try{t.LoadRawTextureData(bytes);t.Apply();File.WriteAllBytes(Root+"/page-"+page.ToString("D2")+".png",t.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(t);}
    },EditorSpritePacks.Ensure);
    File.WriteAllText(Root+"/catalog.json",JsonUtility.ToJson(catalog,true));
    AssetDatabase.Refresh();
   }finally{Rubezh.Graphics.ReleaseSceneSprites();}
  }
  public static void Require(){
   var file=Root+"/catalog.json";if(!File.Exists(file))throw new BuildFailedException("Нет NativeUI. Пересоберите атлас интерфейса.");
   var c=JsonUtility.FromJson<NativeUiArt.Catalog>(File.ReadAllText(file));
   int expected=(Rubezh.GreenLook.ActiveHeads.Length+12)*12+NativeUiRecipe.Icons.Length;
   if(c==null||c.version!=1||c.entries==null||c.entries.Length!=expected)throw new BuildFailedException("Устарел атлас интерфейса.");
   foreach(var e in c.entries)if(!File.Exists(Root+"/page-"+e.page.ToString("D2")+".png"))throw new BuildFailedException("Нет страницы NativeUI "+e.page);
  }
  public static void ArchiveSources(){
   Require();Move("Assets/Rubezh/Resources/SpritePacks","Assets/Rubezh/Editor/LegacySources/SpritePacks");
   Move("Assets/Rubezh/Resources/UnityTestForest","Assets/Rubezh/Art/LegacyUnityForest");
  }
  static void Move(string from,string to){
   BuildResourcePreparation.MigrateSourceFolder(from,to);
  }
 }
 public sealed class NativeUiImport:AssetPostprocessor {
  void OnPreprocessTexture(){
   if(!assetPath.StartsWith(NativeUiPreparation.Root+"/",StringComparison.Ordinal)||!assetPath.EndsWith(".png",StringComparison.Ordinal))return;
   var i=(TextureImporter)assetImporter;i.textureType=TextureImporterType.Default;i.textureShape=TextureImporterShape.Texture2D;i.mipmapEnabled=false;i.isReadable=false;i.sRGBTexture=true;i.alphaIsTransparency=false;i.npotScale=TextureImporterNPOTScale.None;i.maxTextureSize=2048;i.filterMode=FilterMode.Bilinear;i.wrapMode=TextureWrapMode.Clamp;
   // Shared GPU-compressed pages; no CPU-readable copy in the Player.
   i.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Android",overridden=true,maxTextureSize=2048,format=TextureImporterFormat.ASTC_4x4,compressionQuality=100});
   i.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name="Standalone",overridden=true,maxTextureSize=2048,format=TextureImporterFormat.BC7,compressionQuality=100});
  }
 }
}
#endif
