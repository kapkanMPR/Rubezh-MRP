using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rubezh;
using Rubezh.Camp;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

class Unity200Check
{
    static int n;
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Check(bool value, string s)
    {
        if (!value)
            throw new Exception(s);
        n++;
    }

    static string Fingerprint(BattleSimulation b) =>
        string.Join(
            "|",
            b.Soldiers.Select(s =>
                s.Id
                + ":"
                + s.Position.X.ToString("R")
                + ","
                + s.Position.Y.ToString("R")
                + ":"
                + s.Health.ToString("R")
                + ":"
                + s.Ammo
                + ":"
                + s.Reserve
            )
        );

    static void Main()
    {
        Rubezh.UnityPort.Editor.UnityTest20Preparation.EnsurePrepared();
        Resources.Loader = p => p == "RubezhHdGpu" || p == "RubezhUnityTest" ? new Shader() : LoadAsset(p);
        var level = JsonUtility
            .FromJson<BuiltinCampaign.Map>(
                PackedLevel.Decode(File.ReadAllBytes("Assets/Rubezh/Resources/War/war-01.bytes"))
            )
            .Create();
        Check(level != null, "actual campaign20 loaded");
        // Portable test excludes only PNG visual layers; physical walls and combat cells are retained.
        level.proceduralVisuals.Clear();
        foreach (var house in level.dynamicBuildings)
            house.layers.Clear();
        var catalog = JsonConvert.DeserializeObject<Catalog>(
            File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json")
        );
        var model = new CampModel(catalog, null, () => .37);
        string before = JsonConvert.SerializeObject(model.S);
        Check(
            model.MissionOpen("test", 1, 20) && model.MissionOpen("test-reference", 1, 20),
            "test available without campaign completion"
        );
        Check(!model.MissionOpen("test", 1, 19) && !model.MissionOpen("test", 2, 20), "only mission20");
        Check(!model.Win("test", 1, 20) && !model.Win("test-reference", 1, 20), "test awards blocked");
        Check(before == JsonConvert.SerializeObject(model.S), "career untouched");
        var units = Enumerable
            .Range(0, 5)
            .Select(i => new CampUnit
            {
                name = "Test" + i,
                fighterId = "t" + i,
                role = i + 1,
                weapon = i == 4 ? 1 : 0,
                rig = 1,
                armor = 1,
                skills = new string[0],
                pockets = new string[0],
                bag = new string[0],
            })
            .ToArray();
        var q = new CampOrder
        {
            mode = "test",
            block = 1,
            mission = 20,
            units = units,
            formation = new FormationSettings(),
        };
        var b = RubezhApp.BuildCampSimulation(q);
        q.mode = "test-reference";
        var reference = RubezhApp.BuildCampSimulation(q);
        Check(b.CampaignLevel == reference.CampaignLevel, "campaign20 rules");
        Check(b.Map.Columns == level.columns && b.Map.Rows == level.rows, "campaign20 actual dimensions");
        Check(b.Map.Tiles.SequenceEqual(reference.Map.Tiles), "same tiles");
        var prepared = JsonUtility.FromJson<UnityTestAtlasCatalog>(
            File.ReadAllText("Assets/Rubezh/Resources/UnityTestForest/catalog.json")
        );
        var available = prepared.entries.Select(e => e.id).ToHashSet();
        var missing = Rubezh
            .Graphics.SceneSprites(b)
            .Where(id => !available.Contains(Rubezh.SpritePackIndex.Resolve(id)))
            .ToArray();
        Console.WriteLine("Scene sprites missing from prepared catalog: " + string.Join(",", missing));
        Check(missing.Length == 0, "all scene sprites prepared");
        Check(Fingerprint(b) == Fingerprint(reference), "same initial squad");
        for (int i = 0; i < 100; i++)
        {
            b.Step();
            reference.Step();
            Check(Fingerprint(b) == Fingerprint(reference), "simulation divergence at tick " + i);
        }
        var app = new RubezhApp();
        typeof(RubezhApp).GetMethod("CampReceiptFor", F).Invoke(app, new object[] { q, true, null, null });
        Check(
            !PlayerPrefs.HasKey((string)typeof(RubezhApp).GetProperty("CampKey", F).GetValue(app) + "receipt"),
            "receipt path returns"
        );
        Rubezh.Graphics.ConfigurePacks();
        Rubezh.Graphics.LoadPage(375, File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/SpritePacks/p0375.bytes"));
        UnityTestAtlasCatalog exported;
        using (var renderer = new HdGpuRenderer(new PixelCanvas(320, 200), true))
        {
            exported = renderer.ExportUnityCatalog();
            Check(exported.entries.Length > 0, "export metadata");
            Check(exported.entries.Any(e => e.id == 7974), "rare AK included");
        }
        var imports = new System.Collections.Generic.List<Texture2D>();
        Resources.Loader = p =>
            p == "RubezhHdGpu" || p == "RubezhUnityTest" ? new Shader()
            : p == "UnityTestForest/catalog" ? new TextAsset { text = JsonUtility.ToJson(exported) }
            : p.StartsWith("UnityTestForest/atlas-") ? MakeTexture(imports)
            : null;
        int unloads = 0;
        Resources.OnUnload = o =>
        {
            if (o is Texture2D)
                unloads++;
        };
        using (var renderer = new HdGpuRenderer(new PixelCanvas(320, 200), true, false, true))
        {
            Check(renderer.ImportedUnityPages > 0, "borrow imported texture");
            Check(renderer.UnitySceneMode, "explicit test renderer");
            Check(renderer.ExportUnityCatalog().entries.Length == exported.entries.Length, "import metadata roundtrip");
        }
        Check(unloads == imports.Count, "imported texture unloading");
        Console.WriteLine(
            "PASS "
                + n
                + " Unity test integration checks, 100 matching simulation ticks; native API contracts only, no real Unity rendering."
        );
    }

    static UnityEngine.Object LoadAsset(string p)
    {
        string path = "Assets/Rubezh/Resources/" + p + ".bytes";
        return File.Exists(path) ? new TextAsset { bytes = File.ReadAllBytes(path) } : null;
    }

    static Texture2D MakeTexture(System.Collections.Generic.List<Texture2D> list)
    {
        var t = new Texture2D(4096, 4096, TextureFormat.RGBA32, false);
        list.Add(t);
        return t;
    }
}
