using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using Rubezh.UnityPort;
using SilentOrders.Core;
using UnityEngine;

internal static class CameraSweep
{
    const int Width = 960;
    const int Height = 540;

    static void Load(IEnumerator loader)
    {
        try
        {
            while (loader.MoveNext()) { }
        }
        finally
        {
            (loader as IDisposable)?.Dispose();
        }
    }

    static void Main()
    {
        const string levelPath = "Assets/Rubezh/Resources/Regenerated/Campaign_20.bytes";
        var level = JsonConvert
            .DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes(levelPath)))
            .Create();
        new SceneAssets(level);
        var battle = new BattleSimulation(22220);
        if (!RubezhLevelRuntime.ApplyLevel(battle, level))
            throw new InvalidDataException(RubezhLevelRuntime.LastError);
        battle.Fog.Enabled = false;
        int tick = battle.TickNumber;
        int revision = battle.Map.Revision;
        Rubezh.Graphics.ConfigurePacks();
        ExteriorSpriteLoader.Initialize();
        using var assets = new NativeBattleAssets();
        Load(assets.Load(Rubezh.Graphics.SceneSprites(battle), () => false));
        Load(ExteriorSpriteLoader.Preload(() => false));
        Load(NativeBuildingAssets.Load(battle.Map, () => false));
        var painter = new NativeBattleComposer(1);
        painter.PrepareCanvas(battle.Map);
        using var renderer = new HdGpuRenderer(painter.Canvas, true, false, true);
        int draws = 0,
            cameras = 0,
            masks = 0,
            cache = 0,
            buildings = 0,
            ground = 0;
        var staticNodeCount = renderer.GetType().GetProperty("UnityStaticRendererNodes");
        Camera.OnRender = camera =>
        {
            var call = camera.RenderCalls[camera.RenderCalls.Count - 1];
            draws += call.Draws;
            cameras++;
            if (call.Target.format != RenderTextureFormat.ARGB32)
                masks += call.Draws;
            else
            {
                if (call.Target != renderer.Texture)
                    cache += call.Draws;
                foreach (var buffers in camera.BufferLists.Values)
                foreach (var buffer in buffers)
                foreach (var draw in buffer.Draws)
                {
                    if (draw.Material.GetFloat("_BuildingMode") > .5f)
                        buildings++;
                    else if (draw.Mesh.name == "Rubezh ground 15x15")
                        ground++;
                }
            }
        };
        try
        {
            void Measure(string name, int frames, Func<int, Rect> view, bool expectCacheHit = false)
            {
                draws = cameras = masks = cache = buildings = ground = 0;
                int minDraws = int.MaxValue,
                    maxDraws = 0;
                for (int frame = 0; frame < frames; frame++)
                {
                    int previous = draws;
                    renderer.Render(painter, battle, false, -1, Width, Height, view(frame));
                    if (expectCacheHit && !renderer.UnityStaticColorCacheHit)
                        throw new InvalidOperationException("Stationary scene redrew its static prefix");
                    if (staticNodeCount != null && (int)staticNodeCount.GetValue(renderer) != 0)
                        throw new InvalidOperationException("Static GameObjects allocated on the command-buffer path");
                    minDraws = Math.Min(minDraws, draws - previous);
                    maxDraws = Math.Max(maxDraws, draws - previous);
                }
                if (draws <= 0 || cameras < frames || tick != battle.TickNumber || revision != battle.Map.Revision)
                    throw new InvalidOperationException("Invalid frozen camera trace: " + name);
                if (expectCacheHit && (masks != 0 || cache != 0 || cameras != frames))
                    throw new InvalidOperationException("Unexpected camera pass in a stationary scene");
                Console.WriteLine(
                    $"{name, -16} frames={frames, 2} commands={draws, 7} min={minDraws, 5} max={maxDraws, 5} cameraPasses={cameras, 3} "
                        + $"main={draws - masks - cache, 5} masks={masks, 5} cache={cache, 5} buildingColor={buildings, 5} groundColor={ground, 5}"
                );
            }

            float width = .5f,
                height = .5f;
            var first = new Rect(.25f, .25f, width, height);
            Console.WriteLine(
                $"Campaign 20, {battle.Map.Columns}x{battle.Map.Rows}, {battle.Soldiers.Count} units, {Width}x{Height} target."
            );
            Measure("initial", 1, _ => first);
            if (masks == 0 || buildings == 0 || ground == 0)
                throw new InvalidOperationException("Expected occluders, buildings and ground on the first frame");
            Measure("idle", 8, _ => first, expectCacheHit: true);
            // Integer target-pixel shifts at a fixed world scale, followed by subpixel motion and continuous zoom.
            Measure("pan", 48, i => new Rect(first.xMin + (i + 1) * 3 * width / Width, first.yMin, width, height));
            float panEnd = first.xMin + 48 * 3 * width / Width;
            Measure(
                "fractional-pan",
                24,
                i => new Rect(panEnd, first.yMin + (i + 1) * .75f * height / Height, width, height)
            );
            float cy = first.yMin + 24 * .75f * height / Height + .25f;
            Rect Zoom(int i)
            {
                float extent = width / (1 + (i + 1) / 72f);
                return new Rect(panEnd + .25f - extent / 2, cy - extent / 2, extent, extent);
            }
            Measure("zoom", 36, Zoom);
            Measure("settle", 1, _ => Zoom(35));
            Measure("idle-after-zoom", 8, _ => Zoom(35), expectCacheHit: true);
            if (
                renderer.UnityMaskMergedMeshes <= 0
                || renderer.UnitySceneNodes <= 0
                || Rubezh.Graphics.MissingSpriteCount != 0
            )
                throw new InvalidOperationException("Missing native scenery or sprite data");

            if (staticNodeCount != null)
            {
                void CheckBackend(bool colorCommands, bool maskCommands)
                {
                    renderer.UnityCachedStaticCommands = colorCommands;
                    renderer.UnityMergedMaskCommands = maskCommands;
                    renderer.Render(painter, battle, false, -1, Width, Height, Zoom(35));
                    int count = (int)staticNodeCount.GetValue(renderer);
                    if ((count == 0) != (colorCommands && maskCommands))
                        throw new InvalidOperationException("Static MeshRenderers must match the active camera passes");
                }
                CheckBackend(false, true);
                CheckBackend(false, false);
                CheckBackend(true, false);
                CheckBackend(true, true);
                Console.WriteLine("PASS all four renderer/mask combinations on a paused frame.");
            }
        }
        finally
        {
            Camera.OnRender = null;
            ExteriorSpriteLoader.Release();
        }
    }
}
