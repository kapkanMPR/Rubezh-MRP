#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Texture pixels are sampled only by Unity shaders, never copied back to CPU.
    public sealed class NativeSurfaceImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            bool roof = assetPath == "Assets/Rubezh/Resources/RoofSurface.png";
            bool wood = assetPath == "Assets/Rubezh/Resources/ClosureWood.png";
            bool crater = assetPath == "Assets/Rubezh/Resources/Effects/terrain-crater.png";
            if (!roof && !wood && !crater)
                return;
            var importer = (TextureImporter)assetImporter;
            int maximum =
                roof ? 4096
                : wood ? 1024
                : 2048;
            importer.isReadable = false;
            importer.mipmapEnabled = false;
            importer.textureType = TextureImporterType.Default;
            importer.maxTextureSize = maximum;
            importer.filterMode = FilterMode.Bilinear;
            importer.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = maximum,
                    format = TextureImporterFormat.ASTC_4x4,
                    compressionQuality = 100,
                }
            );
            importer.SetPlatformTextureSettings(
                new TextureImporterPlatformSettings
                {
                    name = "Standalone",
                    overridden = true,
                    maxTextureSize = maximum,
                    format = TextureImporterFormat.BC7,
                    compressionQuality = 100,
                }
            );
        }
    }
}
#endif
