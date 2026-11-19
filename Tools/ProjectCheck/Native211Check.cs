// Portable checks: simulated Unity APIs, real map/core/model data. No native renderer claims.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rubezh.Camp;
using Rubezh.UnityPort;
using SilentOrders.Core;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

class Native211Check
{
    static int checks;
    const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Check(bool ok, string why)
    {
        checks++;
        if (!ok)
            throw new Exception(why);
    }

    static void Set(object app, string key, object value) => app.GetType().GetField(key, F).SetValue(app, value);

    static object Get(object app, string key) => app.GetType().GetField(key, F).GetValue(app);

    static object Call(object app, string key, params object[] values) =>
        app.GetType().GetMethod(key, F).Invoke(app, values);

    static void Drain(IEnumerator e)
    {
        try
        {
            while (e.MoveNext()) { }
        }
        finally
        {
            (e as IDisposable)?.Dispose();
        }
    }

    static void Main()
    {
        for (int wave = 1; wave <= 100; wave++)
            Check(
                NativeBattleMap.Address("raid", 1, wave) == "rubezh/native/map/war/" + ((wave + 1) / 2).ToString("D2"),
                "raid two battles per map"
            );
        for (int block = 1; block <= 5; block++)
        for (int mission = 1; mission <= 10; mission++)
            Check(
                NativeBattleMap.Address("war", block, mission)
                    == "rubezh/native/map/war/" + ((block - 1) * 10 + mission).ToString("D2"),
                "endless unique map"
            );
        Check(
            NativeBattleMap.Address("test-reference", 1, 1) == NativeBattleMap.Address("test", 1, 1),
            "saved forest reference alias maps to native content"
        );
        Check(
            NativeBattleMap.Address("test-reference", 1, 20) == NativeBattleMap.Address("test", 1, 20),
            "saved heavy reference alias maps to native content"
        );
        Check(
            NativeBattleMap.Address("test", 1, 1) == NativeBattleMap.Address("war", 1, 1),
            "forest uses same content"
        );
        Check(
            NativeBattleMap.Address("test", 1, 20) == NativeBattleMap.Address("story", 1, 20),
            "heavy test uses same content"
        );
        foreach (var bad in new[] { 0, 21 })
        {
            bool failed = false;
            try
            {
                NativeBattleMap.Address("story", 1, bad);
            }
            catch (ArgumentOutOfRangeException)
            {
                failed = true;
            }
            Check(failed, "invalid story rejected");
        }
        CheckMetadata();
        CheckLease();
        CheckRetry();
        CheckReportsOnly();
        CheckExteriorOwnership();
        CheckAllMaps();
        CheckComposition();
        Console.WriteLine(
            "PASS Native211Check: " + checks + " checks. Native Unity visual/import/device validation still required."
        );
    }

    static void CheckReportsOnly()
    {
        var app = new RubezhApp();
        var battle = new BattleSimulation(210);
        Set(app, "battle", battle);
        Set(app, "paused", false);
        var map = battle.Map;
        var tiles = (Tile[])map.Tiles.Clone();
        var rotations = map.LevelPropRotation;
        var xs = map.LevelPropScaleX;
        var ys = map.LevelPropScaleY;
        int mechanics = battle.Rules.MechanicsMask;
        Call(app, "BeginBattleDiagnostics");
        Call(app, "OpenDeveloperReports");
        Check(battle.Diagnostics.Enabled && battle.Outcomes.Enabled, "reports retain diagnostics capture");
        Check(!((bool)Get(app, "paused")), "opening reports does not freeze battle");
        Check(
            tiles.SequenceEqual(map.Tiles)
                && ReferenceEquals(rotations, map.LevelPropRotation)
                && ReferenceEquals(xs, map.LevelPropScaleX)
                && ReferenceEquals(ys, map.LevelPropScaleY),
            "diagnostics do not allocate or mutate visual overrides"
        );
        Check(mechanics == battle.Rules.MechanicsMask, "reports do not mutate mechanics");
        Check(
            ((string[])Get(app, "pickOptions")).SequenceEqual(
                new[] { "Отчёт ИИ", "Производительность", "Боевой журнал" }
            ),
            "legacy reports menu contains exports only"
        );
        var root = new UnityEngine.UIElements.VisualElement();
        Call(app, "NativeDeveloperReports", root);
        Check(
            root.Children()
                .OfType<UnityEngine.UIElements.Button>()
                .Select(b => b.text)
                .SequenceEqual(new[] { "Производительность и рендер", "Поведение ИИ", "Журнал боя" }),
            "native reports menu contains exports only"
        );
        Check(
            typeof(RubezhApp).GetField("visualSession", F) == null
                && typeof(RubezhApp).Assembly.GetType("Rubezh.UnityPort.VisualSession") == null,
            "runtime object editor engine removed"
        );
    }

    static void CheckExteriorOwnership()
    {
        var loader = Resources.Loader;
        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(1);
                writer.Write(1);
                writer.Write(0f);
                writer.Write(0f);
                writer.Write(1f);
                writer.Write(1f);
                writer.Write(0xffffffffu);
            }
            bytes = stream.ToArray();
        }
        try
        {
            Resources.Loader = path => new TextAsset { bytes = bytes };
            ExteriorSpriteLoader.Initialize();
            var first = ExteriorSprites.Load("test0");
            for (int i = 1; i < 32; i++)
                ExteriorSprites.Load("test" + i);
            Check(
                ReferenceEquals(first, ExteriorSprites.Load("test0")),
                "exterior sprite identity survives >24 lookups without duplicate GPU uploads"
            );
            Check(ExteriorSpriteLoader.CachedCount == 32, "exterior lookup owned by current scene");
            Call(new RubezhApp(), "ReleaseSceneGraphics");
            Check(
                ExteriorSpriteLoader.CachedCount == 0 && ExteriorSprites.Load == null,
                "scene exit releases exterior cache and delegate"
            );
            Check(BuildingRecipe.MaterialCount == 0, "scene exit releases procedural source material cache");
        }
        finally
        {
            Resources.Loader = loader;
            ExteriorSpriteLoader.Release();
        }
    }

    static void CheckProceduralRetention(
        HdGpuRenderer renderer,
        SilentOrders.Rendering.BattleRenderer painter,
        BattleSimulation sim
    )
    {
        var image = new ProceduralPicture
        {
            Left = 0,
            Top = 0,
            Width = 16,
            Height = 16,
            PixelWidth = 16,
            PixelHeight = 16,
            Pixels = Enumerable.Repeat(0xffaabbccu, 256).ToArray(),
            Depth = Enumerable.Repeat(256f, 256).ToArray(),
        };
        sim.Map.ProceduralPictures = new[] { image };
        sim.Map.Revision++;
        renderer.Render(painter, sim, false, 0, 960, 540, new Rect(0, 0, 1, 1));
        long full = renderer.PictureFullUploads,
            bytes = renderer.PictureUploadedBytes;
        int builds = renderer.DirectCompositionBuilds;
        Check(
            full > 0 && renderer.ProceduralGpuBytes > 0,
            "real procedural pixels uploaded once into retained GPU pages"
        );
        renderer.Render(painter, sim, false, 0, 960, 540, new Rect(.1f, .1f, .7f, .7f));
        Check(
            renderer.PictureFullUploads == full
                && renderer.PictureUploadedBytes == bytes
                && renderer.DirectCompositionBuilds == builds,
            "camera move reuses picture pages and dynamic instances"
        );
        sim.Map.Revision++;
        renderer.Render(painter, sim, false, 0, 960, 540, new Rect(0, 0, 1, 1));
        Check(
            renderer.PictureFullUploads == full && renderer.PictureUploadedBytes == bytes,
            "unrelated map revision does not reupload procedural textures"
        );
        var replacement = new ProceduralPicture
        {
            Left = 0,
            Top = 0,
            Width = 16,
            Height = 16,
            PixelWidth = 16,
            PixelHeight = 16,
            Pixels = (uint[])image.Pixels.Clone(),
            Depth = image.Depth,
        };
        replacement.Pixels[0] = 0;
        sim.Map.ProceduralPictures = new[] { replacement };
        sim.Map.Revision++;
        long regions = renderer.PictureRegionUploads;
        renderer.Render(painter, sim, false, 0, 960, 540, new Rect(0, 0, 1, 1));
        Check(
            renderer.PictureFullUploads == full && renderer.PictureRegionUploads > regions,
            "destruction updates only changed procedural region"
        );
        renderer.BeginDynamic();
        renderer.Ellipse(20, 20, 9, 4, 1, 0xffffffff, 256, true);
        renderer.EndDynamic();
        var cache = (IDictionary)Get(renderer, "nativeEllipses");
        int count = cache.Count;
        object geometry = cache.Values.Cast<object>().Last();
        renderer.BeginDynamic();
        renderer.Ellipse(40, 30, 9, 4, 2, 0xffaaaaaa, 257, true);
        renderer.EndDynamic();
        Check(
            cache.Count == count && cache.Values.Cast<object>().Any(x => ReferenceEquals(x, geometry)),
            "ellipse geometry retained across position scale and tint updates"
        );
        renderer.Dispose();
        renderer.Dispose();
        Check(
            renderer.ProceduralGpuBytes == 0 && renderer.ProceduralGpuImages == 0,
            "renderer disposal releases all generated picture pages exactly once"
        );
        Check(
            renderer.DirectInstanceCount == 0 && cache.Count == 0,
            "renderer disposal releases direct instance and ellipse caches"
        );
    }

    static void CheckMetadata()
    {
        Rubezh.Graphics.ConfigurePacks();
        Rubezh.Graphics.ReleaseSceneSprites();
        int id = 7974,
            page = Rubezh.SpritePackIndex.Page[id];
        Rubezh.Graphics.RegisterNativeSprite(id, 120, 24);
        Check(Rubezh.Graphics.HasSprite(id), "native dimensions visible to drawing");
        Check(!Rubezh.Graphics.HasSpritePixels(id), "metadata cannot masquerade as CPU pixels");
        Check(Rubezh.Graphics.LoadedPixelBytes == 0, "no CPU RGBA allocated for native metadata");
        int w,
            h;
        Rubezh.Graphics.SpriteData(id, 0, out w, out h);
        Check(w == 120 && h == 24, "native shape dimensions retained");
        // A partially loaded page containing another sprite must not make metadata CPU-ready.
        var payload = File.ReadAllBytes(
            "Assets/Rubezh/Editor/LegacySources/" + Rubezh.SpritePackIndex.Path(page) + ".bytes"
        );
        Rubezh.Graphics.LoadPage(page, payload, looks: new HashSet<int>(), required: new HashSet<int>());
        Check(
            !Rubezh.Graphics.HasPage(page, new HashSet<int>(), new HashSet<int> { id }),
            "partial page rejects metadata-only required sprite"
        );
        Resources.Loader = path => path == Rubezh.SpritePackIndex.Path(page) ? new TextAsset { bytes = payload } : null;
        EditorSpritePacks.Ensure(id);
        Check(Rubezh.Graphics.HasSpritePixels(id), "editor replaces metadata with real pixels");
        Check(Rubezh.Graphics.LoadedPixelBytes > 0, "explicit CPU load has actual pixels");
        Rubezh.Graphics.ReleaseSceneSprites();
        Check(
            Rubezh.Graphics.LoadedPixelBytes == 0 && !Rubezh.Graphics.HasSprite(id),
            "all metadata and pixels released"
        );
    }

    static void CheckLease()
    {
        Addressables.Loader = key => new UnityEngine.ResourceManagement.AsyncOperations.State
        {
            Result =
                key == "rubezh/native/catalog"
                    ? (object)
                        new TextAsset
                        {
                            text =
                                "{\"version\":2,\"entries\":[{\"id\":7974,\"page\":375,\"w\":10,\"h\":20,\"ow\":12,\"oh\":24}]}",
                        }
                : key == "rubezh/native/visual" ? new GameObject("visual")
                : new UnityEngine.U2D.SpriteAtlas(),
        };
        int releases = Addressables.Releases;
        using (var lease = new NativeBattleAssets())
        {
            Drain(lease.Load(new HashSet<int> { 7974 }, () => false));
            Check(lease.LoadedAtlases == 1, "lease loads one required atlas");
            Check(Rubezh.Graphics.LoadedPixelBytes == 0, "lease publishes dimensions only");
            Check(Addressables.Releases == releases + 1, "parsed catalog released early");
            lease.Dispose();
            lease.Dispose();
            Check(Addressables.Releases == releases + 3, "exactly once release of all acquired handles");
            Check(NativeBattleAssets.Active == null, "active lease cleared");
        }
        Addressables.Loader = key => new UnityEngine.ResourceManagement.AsyncOperations.State { Done = false };
        using (var cancelled = new NativeBattleAssets())
        {
            Drain(cancelled.Load(new HashSet<int> { 7974 }, () => true));
        }
        Check(NativeBattleAssets.Active == null, "cancelled lease detached");
        Check(Addressables.Loaded.All(s => !s.Valid), "no retained handles after cancellation");
        Rubezh.Graphics.ReleaseSceneSprites();
    }

    static void CheckRetry()
    {
        var app = new RubezhApp();
        Set(app, "testBattle", true);
        Set(app, "unityTestBattle", true);
        Set(app, "testPreviousQuality", 2);
        Set(app, "testPreviousSoftware", true);
        Set(app, "testPreviousVisibility", false);
        Set(app, "testPreviousFog", true);
        Set(app, "graphicsTier", 3);
        Set(app, "forceSoftware", false);
        Set(app, "fullVisibility", true);
        Set(app, "hardcoreFog", false);
        Set(app, "campBusy", true);
        Set(app, "raidChanging", true);
        Set(app, "raid", new RaidSession());
        Set(app, "campBattle", new CampOrder { mode = "raid" });
        Call(app, "ResetFailedBattleLoad");
        Check(
            !(bool)Get(app, "testBattle") && !(bool)Get(app, "unityTestBattle"),
            "retry clears stale native/test flags"
        );
        Check(
            (int)Get(app, "graphicsTier") == 2
                && (bool)Get(app, "forceSoftware")
                && !(bool)Get(app, "fullVisibility")
                && (bool)Get(app, "hardcoreFog"),
            "retry restores pre-test options"
        );
        Check(
            Get(app, "campBattle") == null
                && Get(app, "raid") == null
                && !(bool)Get(app, "raidChanging")
                && !(bool)Get(app, "campBusy"),
            "retry detaches incomplete raid without touching persisted progress"
        );
    }

    static void CheckComposition()
    {
        var catalog = File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json");
        var entries = JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(catalog).entries;
        Resources.Loader = path =>
            (path == "RubezhNativeEffectMaterial" || path == "RubezhNativeSceneMaterial")
                ? (UnityEngine.Object)new Material(new Shader())
            : path == "RoofSurface" || path == "ClosureWood"
                ? (UnityEngine.Object)new Texture2D(16, 16, TextureFormat.RGBA32, false)
            : path.StartsWith("Rubezh") ? new Shader()
            : null;
        Addressables.Loader = key =>
        {
            if (key == "rubezh/native/catalog")
                return new UnityEngine.ResourceManagement.AsyncOperations.State
                {
                    Result = new TextAsset { text = catalog },
                };
            if (key == "rubezh/native/visual")
                return new UnityEngine.ResourceManagement.AsyncOperations.State { Result = new GameObject("visual") };
            int page = int.Parse(key.Substring(key.LastIndexOf('/') + 1));
            var atlas = new UnityEngine.U2D.SpriteAtlas();
            var texture = new Texture2D(8192, 8192, TextureFormat.RGBA32, false);
            foreach (var e in entries.Where(e => e.page == page))
                atlas.Items["s" + e.id.ToString("D5")] = new Sprite
                {
                    texture = texture,
                    textureRect = new Rect(0, 0, e.w, e.h),
                };
            return new UnityEngine.ResourceManagement.AsyncOperations.State { Result = atlas };
        };
        foreach (
            string path in new[]
            {
                "Assets/Rubezh/Resources/War/war-01.bytes",
                "Assets/Rubezh/Resources/Regenerated/Campaign_20.bytes",
            }
        )
        {
            var map = JsonConvert
                .DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes(path)))
                .Create();
            map.terrainVisual = null;
            map.proceduralVisuals.Clear();
            foreach (var house in map.dynamicBuildings)
                house.layers.Clear();
            var sim = new BattleSimulation(210);
            Check(RubezhLevelRuntime.ApplyLevel(sim, map), "composition map applies");
            Rubezh.Graphics.ReleaseSceneSprites();
            using (var lease = new NativeBattleAssets())
            {
                Drain(lease.Load(Rubezh.Graphics.SceneSprites(sim), () => false));
                var painter = new SilentOrders.Rendering.BattleRenderer(1);
                painter.PrepareCanvas(sim.Map);
                using (var renderer = new HdGpuRenderer(painter.Canvas, true, false, true))
                {
                    renderer.Render(painter, sim, false, 0, 960, 540, new Rect(0, 0, 1, 1));
                    Check(renderer.UnitySceneNodes > 0, "native command composition contains scene nodes");
                    Check(
                        renderer.DirectComposition && renderer.DirectInstanceCount > 0,
                        "native frame retains instances without legacy command replay"
                    );
                    Check(
                        painter.Canvas.AllocatedPixelBytes == 0,
                        "native composition did not allocate software canvas"
                    );
                    Check(Get(painter, "legacyOrdered") == null, "native frame never allocates legacy OrderedCanvas");
                    Check(
                        Rubezh.Graphics.LoadedPixelBytes == 0,
                        "native composition uses metadata without CPU sprite payloads"
                    );
                    renderer.Render(painter, sim, false, 0, 960, 540, new Rect(.2f, .2f, .5f, .5f));
                    Check(Rubezh.Graphics.MissingSpriteCount == 0, "native renderer sees every required sprite");
                    NativeEffects211Check.Run(renderer, sim);
                    CheckProceduralRetention(renderer, painter, sim);
                }
                foreach (int tier in new[] { -1, 0, 1, 2, 3 })
                {
                    var app = new RubezhApp();
                    Set(app, "battle", sim);
                    Set(app, "painter", painter);
                    Set(app, "graphicsTier", tier);
                    Set(app, "forceSoftware", true);
                    Set(app, "fullVisibility", true);
                    Call(app, "RenderOutput");
                    var active = (HdGpuRenderer)Get(app, "hdBattle");
                    Check(Get(app, "unityTestError") == null && active != null, "native output renders tier " + tier);
                    Check(
                        active.DirectComposition && painter.Canvas.AllocatedPixelBytes == 0,
                        "all tiers including pixels bypass software even with stale CPU flag"
                    );
                    Check(
                        Get(app, "nativeBattle") == null && Get(app, "texture") == null,
                        "no second renderer or software output texture"
                    );
                    Call(app, "RenderMeasured");
                    Check(!(bool)Call(app, "NeedsBattleRender"), "unchanged native frame is retained at tier " + tier);
                    Set(app, "pan", new Vector2(35, 15));
                    Check((bool)Call(app, "NeedsBattleRender"), "camera move invalidates every native quality tier");
                    Call(app, "ReleaseSceneGraphics");
                }
            }
            Rubezh.Graphics.ReleaseSceneSprites();
        }
    }

    static void CheckAllMaps()
    {
        var entries = JsonConvert
            .DeserializeObject<UnityTestAtlasCatalog>(File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json"))
            .entries.ToDictionary(e => e.id);
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
        for (int pass = 0; pass < 2; pass++)
        for (int level = 1; level <= (pass == 0 ? 20 : 50); level++)
        {
            string path =
                pass == 0
                    ? "Assets/Rubezh/Resources/Regenerated/Campaign_" + level.ToString("D2") + ".bytes"
                    : "Assets/Rubezh/Resources/War/war-" + level.ToString("D2") + ".bytes";
            var map = JsonConvert
                .DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes(path)))
                .Create();
            map.terrainVisual = null;
            map.proceduralVisuals.Clear();
            foreach (var h in map.dynamicBuildings)
                h.layers.Clear();
            var q = new CampOrder
            {
                mode = pass == 0 ? "story" : "war",
                block = pass == 0 ? 1 : (level - 1) / 10 + 1,
                mission = pass == 0 ? level : (level - 1) % 10 + 1,
                units = units,
                formation = new FormationSettings(),
            };
            var sim = RubezhApp.BuildCampSimulation(q, map);
            var required = Rubezh.Graphics.SceneSprites(sim);
            var missing = required.Where(id => !entries.ContainsKey(Rubezh.SpritePackIndex.Resolve(id))).ToArray();
            Check(
                missing.Length == 0,
                "missing native sprites on " + q.mode + " " + level + ": " + string.Join(",", missing)
            );
            var cpu = StandaloneWallMaterials.SourceSprites(sim.Map);
            Check(
                cpu.All(id => required.Contains(Rubezh.SpritePackIndex.Resolve(id))),
                "CPU material subset is in scene"
            );
            Rubezh.Graphics.ConfigurePacks();
            foreach (int id in required)
            {
                var e = entries[Rubezh.SpritePackIndex.Resolve(id)];
                Rubezh.Graphics.RegisterNativeSprite(id, e.ow, e.oh);
            }
            Rubezh.Graphics.ValidateSceneSprites(required);
            Check(Rubezh.Graphics.LoadedPixelBytes == 0, "native map metadata allocates no pixel buffer");
            Rubezh.Graphics.ReleaseSceneSprites();
            Check(
                sim.Soldiers.Count >= 5 && sim.Soldiers.Take(5).All(s => s.Side == Side.Green),
                "squad remains correct"
            );
            Console.WriteLine(
                "MAP "
                    + q.mode
                    + " "
                    + level
                    + ": "
                    + sim.Map.Columns
                    + "x"
                    + sim.Map.Rows
                    + ", sprites="
                    + required.Count
                    + ", prebaked wall-source sprites="
                    + cpu.Count
            );
        }
    }
}
