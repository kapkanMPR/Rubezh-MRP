using System;
using System.IO;
using Newtonsoft.Json;
using Rubezh.UnityPort;

class Packed164Check
{
    static void Main()
    {
        int count = 0;
        foreach (var dir in new[] { "Regenerated", "War" })
        foreach (var file in Directory.GetFiles("Assets/Rubezh/Resources/" + dir, "*.bytes"))
        {
            string text = PackedLevel.Decode(File.ReadAllBytes(file));
            var data = JsonConvert.DeserializeObject<BuiltinCampaign.Map>(text);
            var level = data.Create();
            string error;
            if (!LevelValidation.Valid(level, out error))
                throw new Exception(file + ": " + error);
            if (text.Contains("\"terrainVisual\""))
                throw new Exception("Unused terrain retained");
            UnityEngine.Object.Destroy(level);
            count++;
        }
        if (count != 70)
            throw new Exception("Missing levels " + count);
        if (PackedLevel.Decode(System.Text.Encoding.UTF8.GetBytes("{}")) != "{}")
            throw new Exception("Legacy JSON");
        Console.WriteLine(
            "PASS all 70 packed maps decoded, deserialized and validated; legacy JSON accepted; unused terrain absent"
        );
    }
}
