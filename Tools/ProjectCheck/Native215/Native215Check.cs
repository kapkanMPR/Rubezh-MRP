using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.AddressableAssets;

class Native215Check
{
    static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks;

    static void Check(bool ok, string message)
    {
        checks++;
        if (!ok)
            throw new Exception("Roster215: " + message);
    }

    static object Get(object o, string name) => o.GetType().GetField(name, F).GetValue(o);

    static IDictionary Dict(object o, string name) => (IDictionary)Get(o, name);

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

    static Soldier Copy(Soldier source, int id)
    {
        var s = JsonConvert.DeserializeObject<Soldier>(
            JsonConvert.SerializeObject(
                source,
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }
            )
        );
        s.Id = id;
        s.Deployed = true;
        s.Health = 100;
        s.Moving = false;
        s.DefenderGroup = null;
        s.RescuerId = -1;
        s.RescueTargetId = -1;
        s.Side = Side.Green;
        s.Position = new V2(10 + id % 3, 10);
        return s;
    }

    static void Main()
    {
        var catalog = File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json");
        var entries = JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(catalog).entries;
        Resources.Loader = path =>
            path == "Fonts/RubezhSans" ? (UnityEngine.Object)new Font()
            : path == "NativeUI/catalog"
                ? new TextAsset { text = File.ReadAllText("Assets/Rubezh/Resources/NativeUI/catalog.json") }
            : path.StartsWith("NativeUI/page-") ? new Texture2D(2048, 2048, TextureFormat.RGBA32, false)
            : path == "NativeExterior/catalog"
                ? new TextAsset { text = File.ReadAllText("Assets/Rubezh/Resources/NativeExterior/catalog.json") }
            : path.StartsWith("NativeExterior/page-") ? new Texture2D(2048, 2048, TextureFormat.RGBA32, false)
            : (path == "RubezhNativeEffectMaterial" || path == "RubezhNativeSceneMaterial")
                ? (UnityEngine.Object)new Material(new Shader())
            : path == "RoofSurface" || path == "ClosureWood" || path == "Effects/terrain-crater"
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

        var level = JsonConvert
            .DeserializeObject<BuiltinCampaign.Map>(
                PackedLevel.Decode(File.ReadAllBytes("Assets/Rubezh/Resources/War/war-01.bytes"))
            )
            .Create();
        level.terrainVisual = null;
        level.proceduralVisuals.Clear();
        foreach (var house in level.dynamicBuildings)
            house.layers.Clear();
        var sim = new BattleSimulation(215);
        Check(RubezhLevelRuntime.ApplyLevel(sim, level), "fixture map applies");
        sim.Fog.Enabled = false;
        var prototype = sim.Soldiers.First(s => s.Side == Side.Green);
        sim.Soldiers.Clear();
        sim.Soldiers.Add(Copy(prototype, 0));
        sim.Green.LeaderId = 0;
        Rubezh.Graphics.ReleaseSceneSprites();
        using (var lease = new NativeBattleAssets())
        {
            Drain(lease.Load(Rubezh.Graphics.SceneSprites(sim), () => false));
            Drain(ExteriorSpriteLoader.Preload(() => false));
            var painter = new NativeBattleComposer(1);
            painter.PrepareCanvas(sim.Map);
            using (var renderer = new HdGpuRenderer(painter.Canvas, true, false, true))
            {
                Action draw = () =>
                {
                    painter.SetInterpolation(0f);
                    renderer.Render(painter, sim, false, -1, 960, 540);
                };
                Action<string> bounded = label =>
                {
                    Check(Dict(painter, "frames").Count == sim.Soldiers.Count, label + " exact observation roster");
                    Check(Dict(painter, "actors").Count == sim.Soldiers.Count, label + " actor cache bounded");
                    Check(
                        Dict(renderer, "actorTransforms").Count == sim.Soldiers.Count,
                        label + " actor roots bounded"
                    );
                    var groups = Dict(renderer, "directGroups");
                    foreach (DictionaryEntry pair in groups)
                        if ((int)pair.Key >= 0)
                            Check(sim.Soldiers.Any(s => s.Id == (int)pair.Key / 4), label + " no stale direct group");
                    foreach (DictionaryEntry pair in Dict(renderer, "unityActorNodes"))
                    {
                        long group = ((long)pair.Key >> 32) - 1;
                        if (group >= 0)
                            Check(sim.Soldiers.Any(s => s.Id == group / 4), label + " no stale Unity part node");
                    }
                };
                painter.ObserveTick(sim);
                draw();
                bounded("initial");
                var root = (GameObject)Get(renderer, "unityRoot");
                int toggles = root.ActivationCalls,
                    copies = renderer.MaterialCopies,
                    reuses = renderer.StaticSubmissionReuse;
                for (int n = 0; n < 50; n++)
                {
                    sim.TickNumber++;
                    sim.Time += .05f;
                    draw();
                }
                Check(root.ActivationCalls == toggles && root.activeSelf, "steady scene stays registered");
                Check(renderer.MaterialCopies - copies <= 50, "only mutable roof material refreshed per composition");
                Check(renderer.StaticSubmissionReuse > reuses, "unchanged static submissions reused across ticks");
                Console.WriteLine(
                    "RETAINED scene: root toggles="
                        + (root.ActivationCalls - toggles)
                        + ", material copies="
                        + (renderer.MaterialCopies - copies)
                        + ", static segment reuses="
                        + (renderer.StaticSubmissionReuse - reuses)
                );

                var commander = Copy(prototype, 1);
                commander.Role = Role.Commander;
                commander.State = ActionState.Reload;
                sim.Green.LeaderId = 1;
                sim.Soldiers.Add(commander);
                draw();
                bounded("add same tick");
                Check(
                    Dict(painter, "actors").Contains(1) && Dict(renderer, "directGroups").Contains(1 * 4 + 1),
                    "new actor body composed same tick"
                );
                Check(Dict(painter, "frames").Contains(1), "new commander/reloading observed after priorObserveTick");
                var replacedRoot = ((Transform)Get(Dict(renderer, "actorTransforms")[1], "Root")).gameObject;
                var replacement = Copy(prototype, 1);
                replacement.Position = new V2(23, 18);
                replacement.State = ActionState.Reload;
                sim.Soldiers[1] = replacement;
                draw();
                bounded("sameID reference replacement");
                Check(!replacedRoot.activeSelf, "replaced root deactivated before deferred Destroy");
                Check(
                    ReferenceEquals(Get(Dict(painter, "actors")[1], "Soldier"), replacement),
                    "replacement body references new Soldier"
                );
                Check(
                    Math.Abs(painter.VisualPosition(replacement).X - 23) < .001,
                    "replacement snaps without interpolation ghost"
                );
                sim.Soldiers[1] = Copy(prototype, 71);
                sim.Green.LeaderId = 71;
                draw();
                bounded("same count different ID");
                var removedRoot = ((Transform)Get(Dict(renderer, "actorTransforms")[0], "Root")).gameObject;
                sim.Soldiers.RemoveAt(0);
                draw();
                Check(!removedRoot.activeSelf, "removed root deactivated before deferred Destroy");
                bounded("remove same tick");
                sim.Soldiers.Clear();
                draw();
                bounded("remove all same tick");
                Check(Dict(renderer, "actorTransforms").Count == 0, "all actor roots removed");
                int maxFrames = 0,
                    maxActors = 0,
                    maxRoots = 0,
                    maxGroups = 0,
                    maxNodes = 0;
                for (int wave = 0; wave < 100; wave++)
                {
                    sim.Soldiers.Clear();
                    for (int i = 0; i < 5; i++)
                    {
                        int id = wave % 2 == 0 ? i : 1000 + wave * 5 + i;
                        var s = Copy(prototype, id);
                        s.State = i == 0 ? ActionState.Reload : prototype.State;
                        s.Position = new V2(10 + i, 10 + wave % 3);
                        sim.Soldiers.Add(s);
                    }
                    sim.Green.LeaderId = sim.Soldiers[0].Id;
                    // Tick deliberately unchanged: future roster events can occur between simulation steps.
                    draw();
                    bounded("cycle" + wave);
                    maxFrames = Math.Max(maxFrames, Dict(painter, "frames").Count);
                    maxActors = Math.Max(maxActors, Dict(painter, "actors").Count);
                    maxRoots = Math.Max(maxRoots, Dict(renderer, "actorTransforms").Count);
                    maxGroups = Math.Max(maxGroups, Dict(renderer, "directGroups").Count);
                    maxNodes = Math.Max(maxNodes, Dict(renderer, "unityActorNodes").Count);
                    foreach (var soldier in sim.Soldiers)
                        Check(
                            Math.Abs(painter.VisualPosition(soldier).X - soldier.Position.X) < .001,
                            "cycle insertion no previous actor interpolation"
                        );
                    sim.Soldiers.Clear();
                    draw();
                    bounded("empty cycle" + wave);
                }
                for (int wave = 0; wave < 10; wave++)
                {
                    var next = new BattleSimulation(215 + wave);
                    Check(RubezhLevelRuntime.ApplyLevel(next, level), "new battle level applies");
                    next.Soldiers.Clear();
                    next.Soldiers.Add(Copy(prototype, 0));
                    next.Soldiers[0].Position = new V2(6 + wave, 12);
                    next.Green.LeaderId = 0;
                    next.Fog.Enabled = false;
                    sim = next;
                    draw();
                    bounded("new simulation" + wave);
                    Check(
                        Math.Abs(painter.VisualPosition(sim.Soldiers[0]).X - (6 + wave)) < .001,
                        "new battle has no previous battle interpolation"
                    );
                }
                sim.Soldiers.Clear();
                draw();
                bounded("final empty");
                Console.WriteLine(
                    JsonConvert.SerializeObject(
                        new
                        {
                            result = "PASS",
                            checks,
                            cycles = 100,
                            newSimulationCycles = 10,
                            maxFrames,
                            maxActors,
                            maxRoots,
                            maxGroups,
                            maxNodes,
                            finalFrames = Dict(painter, "frames").Count,
                            finalActors = Dict(painter, "actors").Count,
                            finalRoots = Dict(renderer, "actorTransforms").Count,
                            finalGroups = Dict(renderer, "directGroups").Count,
                            finalNodes = Dict(renderer, "unityActorNodes").Count,
                            finalActorNodes = Dict(renderer, "unityActorNodes")
                                .Keys.Cast<long>()
                                .Count(k => (k >> 32) - 1 >= 0),
                            note = "Synthetic same-BattleSimulation roster stress; actual raid creates a new simulation each wave.",
                        },
                        Formatting.Indented
                    )
                );
            }
        }
    }
}
