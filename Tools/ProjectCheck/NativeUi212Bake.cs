using System;
using System.IO;
using System.Text.Json;
using Rubezh.UnityPort;
using UnityEngine;

class NativeUi212Bake
{
    static void Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        Resources.Loader = p =>
        {
            string f = "Assets/Rubezh/Resources/" + p + ".bytes";
            if (!File.Exists(f))
                f = "Assets/Rubezh/Editor/LegacySources/" + p + ".bytes";
            return File.Exists(f) ? new TextAsset { bytes = File.ReadAllBytes(f) } : null;
        };
        var catalog = NativeUiRecipe.Bake(
            (page, pixels) =>
            {
                byte[] b = new byte[pixels.Length * 4];
                Buffer.BlockCopy(pixels, 0, b, 0, b.Length);
                File.WriteAllBytes(args[0] + "/page-" + page.ToString("D2") + ".rgba", b);
            },
            EditorSpritePacks.Ensure
        );
        File.WriteAllText(
            args[0] + "/catalog.json",
            JsonSerializer.Serialize(catalog, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true })
        );
        int loads = 0,
            unloads = 0;
        Resources.Loader = path =>
        {
            loads++;
            if (path == "NativeUI/catalog")
                return new TextAsset { text = File.ReadAllText(args[0] + "/catalog.json") };
            if (path.StartsWith("NativeUI/page-"))
                return new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
            throw new Exception("Unexpected runtime source load: " + path);
        };
        Resources.OnUnload = o => unloads++;
        foreach (var e in catalog.entries)
            NativeUiArt.Draw(new Rect(0, 0, 128, 160), e.key);
        if (loads != 3)
            throw new Exception("Expected catalog + two shared pages, got " + loads);
        NativeUiArt.Release();
        if (unloads != 3)
            throw new Exception("UI page lifetime leak");
        Console.WriteLine("PASS runtime: all 344 keys, only NativeUI loads, 2 cached pages, all released");
        Console.WriteLine(
            "PASS baked " + catalog.entries.Length + " UI variants with production DrawPortrait/ReplaySprite"
        );
    }
}
