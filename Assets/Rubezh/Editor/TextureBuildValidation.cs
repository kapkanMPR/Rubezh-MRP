#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public sealed class TextureBuildValidation : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (
                string file in System.IO.Directory.GetFiles(
                    "Assets/Rubezh/Resources/FieldCamp/Art",
                    "*.png",
                    System.IO.SearchOption.AllDirectories
                )
            )
            {
                string path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureShape != TextureImporterShape.Texture2D)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    importer = AssetImporter.GetAtPath(path) as TextureImporter;
                }
                if (
                    importer == null
                    || importer.textureShape != TextureImporterShape.Texture2D
                    || AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null
                )
                    throw new BuildFailedException("Изображение не импортировано как Texture2D: " + path);
            }
        }
    }
}
#endif
