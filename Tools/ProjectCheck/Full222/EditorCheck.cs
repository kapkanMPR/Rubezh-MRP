// Actual importer/prebuild helper methods under portable API contracts, not Unity import.
using System;
using System.IO;
using System.Reflection;
using Rubezh.UnityPort.Editor;
using UnityEditor;
using UnityEngine;

class Full222EditorCheck
{
    static int checks;

    static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        checks++;
    }

    static void Import(AssetPostprocessor processor, string path, TextureImporter importer)
    {
        processor.assetPath = path;
        processor.assetImporter = importer;
        processor
            .GetType()
            .GetMethod("OnPreprocessTexture", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(processor, null);
    }

    static void Main()
    {
        Check(
            NativeAtlasShapeImport.IsPage("Assets/Rubezh/Resources/NativeExterior/page-00.png"),
            "exterior recognized"
        );
        Check(NativeAtlasShapeImport.IsPage("Assets/Rubezh/Resources/NativeUI/page-00.PNG"), "UI recognized");
        Check(!NativeAtlasShapeImport.IsPage("Assets/Other/page-00.png"), "unrelated PNG excluded");
        var importer = new TextureImporter
        {
            textureType = TextureImporterType.Sprite,
            textureShape = TextureImporterShape.Cube,
        };
        Import(new NativeAtlasShapeImport(), "Assets/Rubezh/Resources/NativeExterior/page-00.png", importer);
        Check(
            importer.textureType == TextureImporterType.Default
                && importer.textureShape == TextureImporterShape.Texture2D,
            "shape override before import"
        );
        foreach (var processor in new AssetPostprocessor[] { new NativeUiImport(), new NativeExteriorImport() })
        {
            importer = new TextureImporter { textureShape = TextureImporterShape.Cube };
            Import(
                processor,
                (processor is NativeUiImport ? NativeUiPreparation.Root : NativeExteriorPreparation.Root)
                    + "/page-00.png",
                importer
            );
            Check(
                importer.textureShape == TextureImporterShape.Texture2D
                    && !importer.isReadable
                    && !importer.mipmapEnabled,
                "specific page importer explicitly selects GPU-only 2D"
            );
        }
        string cwd = Directory.GetCurrentDirectory(),
            temp = Path.Combine(Path.GetTempPath(), "rubezh-shape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Directory.SetCurrentDirectory(temp);
            foreach (string root in new[] { NativeExteriorPreparation.Root, NativeUiPreparation.Root })
            {
                Directory.CreateDirectory(root);
                string path = root + "/page-00.png";
                File.WriteAllBytes(path, new byte[] { 1 });
                AssetImporter.Importers[path] = new TextureImporter
                {
                    path = path,
                    textureType = TextureImporterType.Default,
                    textureShape = TextureImporterShape.Cube,
                };
                AssetDatabase.MainAssets[path] = new UnityEngine.Object();
            }
            NativeAtlasShapeBuildCheck.Repair();
            foreach (var entry in AssetImporter.Importers)
            {
                var texture = (TextureImporter)entry.Value;
                Check(
                    texture.reimports == 1
                        && texture.textureShape == TextureImporterShape.Texture2D
                        && AssetDatabase.LoadMainAssetAtPath(entry.Key) is Texture2D,
                    "prebuild repairs cubemap and reimports"
                );
            }
            NativeAtlasShapeBuildCheck.Repair();
            foreach (var entry in AssetImporter.Importers)
                Check(((TextureImporter)entry.Value).reimports == 1, "correct importer is not reimported repeatedly");
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);
            Directory.Delete(temp, true);
        }
        Console.WriteLine(
            "PASS Full222EditorCheck: "
                + checks
                + " atlas shape/importer checks. Portable contracts; Unity import not executed."
        );
    }
}
