using UnityEditor;

namespace Rubezh.UnityPort.Editor
{
    // Source images are editor assets only; runtime reads the encoded .bytes payload.
    public sealed class BuildingMaterialImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(BuildResourcePreparation.Source + "/", System.StringComparison.Ordinal))
                return;
            var t = (TextureImporter)assetImporter;
            t.isReadable = false;
            t.mipmapEnabled = false;
            t.maxTextureSize = 1024;
            t.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
