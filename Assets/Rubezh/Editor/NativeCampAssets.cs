#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace Rubezh.UnityPort.Editor
{
    // Keep geometry/UV dimensions unchanged. Android uses GPU texture compression.
    public sealed class NativeCampAssets:AssetPostprocessor
    {
        public override uint GetVersion()=>187;
        void OnPreprocessTexture()
        {
            if(!assetPath.StartsWith("Assets/Rubezh/Resources/FieldCamp/Art/",System.StringComparison.Ordinal))return;
            var t=(TextureImporter)assetImporter;
            bool uiSprite=assetPath.Contains("/menu101/")&&!assetPath.EndsWith("/ground.png")&&!assetPath.EndsWith("/interior.png");
            bool item=assetPath.Contains("/weapons/")||assetPath.Contains("/loot/");
            t.textureType=uiSprite?TextureImporterType.Sprite:TextureImporterType.Default;
            if(uiSprite)t.spriteImportMode=SpriteImportMode.Single;
            t.textureShape=TextureImporterShape.Texture2D;t.sRGBTexture=true;t.alphaSource=TextureImporterAlphaSource.FromInput;
            t.alphaIsTransparency=true;t.mipmapEnabled=false;t.isReadable=false;t.npotScale=TextureImporterNPOTScale.None;
            t.maxTextureSize=item?2048:4096;t.textureCompression=TextureImporterCompression.Uncompressed;
            t.filterMode=FilterMode.Bilinear;t.wrapMode=TextureWrapMode.Clamp;
            var android=t.GetPlatformTextureSettings("Android");android.overridden=true;android.maxTextureSize=t.maxTextureSize;
            android.format=item?TextureImporterFormat.ASTC_6x6:TextureImporterFormat.ASTC_4x4;
            t.SetPlatformTextureSettings(android);
        }
    }
}
#endif
