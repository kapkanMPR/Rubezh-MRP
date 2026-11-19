using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Rubezh.UnityPort.Editor
{
    // Applies to both the custom build button and Unity's regular Build command.
    public sealed class LoadingSetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            BuildBranding.Apply();
        }
    }

    public sealed class LoadingArtImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (
                !assetPath.StartsWith("Assets/Rubezh/Resources/Loading/") && !assetPath.StartsWith("Assets/Rubezh/Art/")
            )
                return;
            if (assetPath.StartsWith(BuildResourcePreparation.Source + "/", System.StringComparison.Ordinal))
                return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
        }
    }
}
