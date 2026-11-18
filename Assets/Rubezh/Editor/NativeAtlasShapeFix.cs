#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Catalog pages are 2D sheets, never environment cubemaps.
    public sealed class NativeAtlasShapeImport : AssetPostprocessor
    {
        public override int GetPostprocessOrder() { return 10000; }
        public override uint GetVersion() { return 1; }
        internal static bool IsPage(string path)
        {
            return (path.StartsWith("Assets/Rubezh/Resources/NativeExterior/", StringComparison.Ordinal)
                || path.StartsWith("Assets/Rubezh/Resources/NativeUI/", StringComparison.Ordinal))
                && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }
        void OnPreprocessTexture()
        {
            if (!IsPage(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
        }
    }

    public sealed class NativeAtlasShapeBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -10000; } }
        public void OnPreprocessBuild(BuildReport report) { Repair(); }

        [MenuItem("Рубеж/Unity/Исправить тип страниц атласов")]
        public static void Repair()
        {
            int checkedPages = 0, repaired = 0;
            foreach (var root in new[] {
                "Assets/Rubezh/Resources/NativeExterior",
                "Assets/Rubezh/Resources/NativeUI" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.GetFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    string path = file.Replace('\\', '/');
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                        throw new BuildFailedException("Нет TextureImporter: " + path);
                    if (importer.textureType != TextureImporterType.Default
                        || importer.textureShape != TextureImporterShape.Texture2D
                        || !(AssetDatabase.LoadMainAssetAtPath(path) is Texture2D))
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.textureShape = TextureImporterShape.Texture2D;
                        importer.SaveAndReimport();
                        repaired++;
                    }
                    if (!(AssetDatabase.LoadMainAssetAtPath(path) is Texture2D))
                        throw new BuildFailedException("Страница атласа должна быть Texture2D: " + path);
                    checkedPages++;
                }
            }
            Debug.Log("Native atlas shape: checked=" + checkedPages + ", repaired=" + repaired);
        }
    }
}
#endif
