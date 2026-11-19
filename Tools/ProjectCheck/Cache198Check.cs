using System;
using System.Collections.Generic;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

class Cache198Check
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
    static int n;

    static object Get(object o, string k) => o.GetType().GetField(k, F).GetValue(o);

    static void Set(object o, string k, object v) => o.GetType().GetField(k, F).SetValue(o, v);

    static void Call(object o, string k, params object[] a) => o.GetType().GetMethod(k, F).Invoke(o, a);

    static void Check(bool ok, string s)
    {
        if (!ok)
            throw new Exception(s);
        n++;
    }

    static void Main()
    {
        Resources.Loader = s => new Shader();
        Rubezh.Graphics.ConfigurePacks();
        using (var g = new HdGpuRenderer(new PixelCanvas(320, 200), true, true))
        {
            var v = new Rect(.2f, .2f, .25f, .25f);
            Call(g, "PrepareDepthCoverage", v, 1920, 1080);
            var original = (Rect)Get(g, "depthCoverage");
            Set(g, "depthDirty", false);
            for (int i = 1; i <= 200; i++)
            {
                Call(g, "PrepareDepthCoverage", new Rect(v.x + i * v.width / 1920, v.y, v.width, v.height), 1920, 1080);
                Check(!(bool)Get(g, "depthDirty"), "pan retained coverage " + i);
            }
            Call(g, "PrepareDepthCoverage", new Rect(v.x + 300 * v.width / 1920, v.y, v.width, v.height), 1920, 1080);
            Check((bool)Get(g, "depthDirty"), "large pan refresh");
            Set(g, "depthDirty", false);
            Call(g, "PrepareDepthCoverage", new Rect(v.x, v.y, .125f, .125f), 1920, 1080);
            Check((bool)Get(g, "depthDirty"), "zoom refresh");
            Call(g, "PrepareDepthCoverage", v, 8190, 8190);
            Check(g.NativeDepthWidth <= 8192 && g.NativeDepthHeight <= 8192, "hardware texture limit");
            var b = new BattleSimulation(17);
            g.BeginScene(b.Map);
            Set(g, "visiblePixels", new Rect(-100000, -100000, 200000, 200000));
            var cells = new HashSet<string>();
            int duplicates = 0;
            int calls = 0;
            g.GroundChunks(
                b.Map,
                (l, t, r, z) =>
                {
                    calls++;
                    Check(l % 15 == 0 && t % 15 == 0, "chunk alignment including negatives");
                    Check(r - l <= 15 && z - t <= 15, "chunk dimensions");
                    for (int y = t; y < z; y++)
                    for (int x = l; x < r; x++)
                        if (!cells.Add(x + "," + y))
                            duplicates++;
                }
            );
            Check(duplicates == 0, "no overlapping chunks");
            for (int y = 0; y < b.Map.Rows; y++)
            for (int x = 0; x < b.Map.Columns; x++)
                Check(cells.Contains(x + "," + y), "map fully covered");
            int builds = g.GroundChunkBuilds;
            g.GroundChunks(
                b.Map,
                (l, t, r, z) =>
                {
                    throw new Exception("cached terrain rebuilt");
                }
            );
            Check(g.GroundChunkBuilds == builds, "terrain reused");
            Set(g, "depthDirty", false);
            g.CacheObject(-123, 0, int.MaxValue, false, true, () => { });
            Check(!(bool)Get(g, "depthDirty"), "exterior does not dirty depth");
            g.EndScene();
            Set(g, "depthDirty", false);
            g.CacheObject(-124, 0, int.MaxValue, false, true, () => { });
            g.EndScene();
            Check(!(bool)Get(g, "depthDirty"), "exterior chunk does not dirty depth");
            g.CacheObject(0, 0, int.MaxValue, true, false, () => { });
            Check((bool)Get(g, "depthDirty"), "interior invalidates depth");
            g.EndScene();
            Check(g.ReuseScene(b.Map), "unchanged geometry reused");
            Call(g, "PrepareSceneColor", b.Map, Matrix4x4.identity, Matrix4x4.identity);
            Check(g.SceneColorBuilds == 1, "first cache image built");
            for (int frame = 0; frame < 120; frame++)
                Call(g, "PrepareSceneColor", b.Map, Matrix4x4.identity, Matrix4x4.identity);
            Check(g.SceneColorBuilds == 1, "120 frames reuse cached image");
            ((RenderTexture)Get(g, "sceneColor")).Release();
            Call(g, "PrepareSceneColor", b.Map, Matrix4x4.identity, Matrix4x4.identity);
            Check(g.SceneColorBuilds == 2, "lost color texture restored");
            ((RenderTexture)Get(g, "nativeDepth")).Release();
            Call(g, "EnsureNativeDepth", 8192, 8192);
            Check(((RenderTexture)Get(g, "nativeDepth")).IsCreated(), "lost depth restored");
            int serial = (int)Get(g, "geometrySerial");
            for (int k = 0; k < 120; k++)
                Check(g.ReuseScene(b.Map), "steady scene reuse");
            Check((int)Get(g, "geometrySerial") == serial, "no geometry rebuild during reuse");
            b.Map.Revision++;
            Check(!g.ReuseScene(b.Map), "destruction invalidates cache");
            g.BeginScene(b.Map);
            g.EndScene();
            Check(g.ReuseScene(b.Map), "revised scene reusable");
            var other = new BattleSimulation(18);
            Check(!g.ReuseScene(other.Map), "new raid map invalidates cache");
            Set(g, "depthCoverage", new Rect(.7f, .7f, .1f, .1f));
            Check(!g.ReuseScene(b.Map), "camera coverage invalidates cache");
            Check(g.SceneGeometryHits >= 122, "cache diagnostics");
            Console.WriteLine("ground chunks=" + calls + " cells=" + cells.Count);
        }
        var battle = new BattleSimulation(42);
        battle.Time = 10;
        int comparisons = 0,
            nonzero = 0;
        foreach (var soldier in battle.Soldiers)
        {
            if (soldier.Side != Side.Green)
                continue;
            soldier.Deployed = true;
            soldier.VehicleRally = false;
            soldier.VehicleDisperse = false;
            foreach (var enemy in battle.Soldiers)
                if (enemy.Side != soldier.Side)
                    soldier.Contacts[enemy.Id] = new Contact
                    {
                        EnemyId = enemy.Id,
                        Position = enemy.Position,
                        SeenAt = 10,
                        Confidence = 1,
                        Visual = true,
                    };
            var evaluator = new GroupPositionUtility.Evaluation(battle, soldier);
            for (int y = 0; y < battle.Map.Rows; y++)
            for (int x = 0; x < battle.Map.Columns; x++)
            {
                var v = new V2(x + .5f, y + .5f);
                float old = Baseline198.Delta(battle, soldier, v),
                    now = evaluator.Delta(v);
                Check(Math.Abs(old - now) < .000001f, "group score preserved");
                comparisons++;
                if (old != 0)
                    nonzero++;
            }
        }
        Check(nonzero > 0, "nontrivial group scores tested");
        Console.WriteLine("group score comparisons=" + comparisons + " nonzero=" + nonzero);
        Console.WriteLine("PASS " + n + " checks; Unity API contracts, not native GPU/FPS");
    }
}

namespace SilentOrders.Core
{
    public static class Baseline198
    {
        public static V2 Future(Soldier s)
        {
            return s.MoveAuthorized && s.PathCursor < s.Path.Count ? s.Path[s.Path.Count - 1] : s.Position;
        }

        static float Crowd(V2 a, V2 b) => Math.Max(0, 2.2f - V2.Distance(a, b));

        public static float Delta(BattleSimulation b, Soldier s, V2 candidate)
        {
            if (
                s.Side != Side.Green
                || !PositionLogic.ShelterEligible(b, s)
                || FormationEnvelope.AuthorizedManeuver(b, s)
            )
                return 0;
            float value = 0;
            foreach (var mate in b.Soldiers)
            {
                if (mate == s || mate.Side != s.Side || !mate.Available || !mate.Deployed)
                    continue;
                V2 future = Future(mate);
                value += (Crowd(s.Position, future) - Crowd(candidate, future)) * 2;
                // Avoid selecting a destination inside an already committed travel corridor.
                if (V2.Distance(mate.Position, future) > 1 && V2.Distance(candidate, s.Position) > .6f)
                    value -= Math.Max(0, .8f - V2.SegmentDistance(candidate, mate.Position, future)) * 2;
            }
            foreach (var c in Knowledge.Contacts(b, s))
            {
                if (!PositionLogic.Recent(b, c))
                    continue;
                bool before = PositionLogic.CanEngage(b, s, s.Position, c),
                    after = PositionLogic.CanEngage(b, s, candidate, c);
                if (before == after)
                    continue;
                bool covered = false;
                foreach (var mate in b.Soldiers)
                {
                    if (
                        mate == s
                        || mate.Side != s.Side
                        || !mate.Available
                        || !mate.Deployed
                        || mate.Busy
                        || mate.Broken
                        || mate.Ammo < 3
                        || mate.Suppression >= .62f
                        || mate.SomaliFiring
                    )
                        continue;
                    if (PositionLogic.CanEngage(b, mate, Future(mate), c))
                    {
                        covered = true;
                        break;
                    }
                }
                // Losing the group's only sector matters more than duplicating one.
                value += (after ? 1 : -1) * (covered ? 1f : 4f);
            }
            return Math.Max(-8, Math.Min(8, value));
        }
    }
}
