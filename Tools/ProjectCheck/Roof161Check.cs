using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SilentOrders.Core;
using SilentOrders.Rendering;

class LegacyRoof
{
    public HashSet<long> Roof,
        Anchors;

    static long Key(int x, int y) => DynamicBuilding.Key(x, y);

    static int X(long k) => (int)(k >> 32);

    static int Y(long k) => (int)k;

    static int[] dx =  { 1, -1, 0, 0 },
        dy =  { 0, 0, 1, -1 };

    HashSet<long> Reach(HashSet<long> blocked, IEnumerable<long> seeds)
    {
        var seen = new HashSet<long>();
        var q = new Queue<long>();
        foreach (var k in seeds)
            if (Roof.Contains(k) && !blocked.Contains(k) && seen.Add(k))
                q.Enqueue(k);
        while (q.Count > 0)
        {
            long k = q.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                long n = Key(X(k) + dx[d], Y(k) + dy[d]);
                if (Roof.Contains(n) && !blocked.Contains(n) && seen.Add(n))
                    q.Enqueue(n);
            }
        }
        return seen;
    }

    public void CollapseRoof()
    {
        var anchors = Anchors;
        Roof.IntersectWith(Reach(new HashSet<long>(), anchors));
        for (int axis = 0; axis < 2; axis++)
            foreach (var line in Roof.ToArray().GroupBy(k => axis == 0 ? X(k) : Y(k)))
            {
                var ordered = line.OrderBy(k => axis == 0 ? Y(k) : X(k)).ToArray();
                var runs = new List<List<long>>();
                foreach (var k in ordered)
                {
                    if (
                        runs.Count == 0
                        || (axis == 0 ? Y(k) : X(k)) != (axis == 0 ? Y(runs.Last().Last()) : X(runs.Last().Last())) + 1
                    )
                        runs.Add(new List<long>());
                    runs.Last().Add(k);
                }
                foreach (var run in runs)
                {
                    if (run.Any(k => anchors.Contains(k) || !Roof.Contains(k)))
                        continue;
                    var cut = new HashSet<long>(run);
                    var visited = new HashSet<long>();
                    foreach (var k in run)
                        for (int d = 0; d < 4; d++)
                        {
                            long n = Key(X(k) + dx[d], Y(k) + dy[d]);
                            if (!Roof.Contains(n) || cut.Contains(n) || visited.Contains(n))
                                continue;
                            var side = Reach(cut, new[] { n });
                            visited.UnionWith(side);
                            if (!side.Overlaps(anchors) && side.Count > 6 * run.Count * 4)
                                Roof.ExceptWith(side);
                        }
                }
            }
        Roof.IntersectWith(Reach(new HashSet<long>(), anchors));
    }
}

class Roof161Check
{
    static void Check(bool ok, string m)
    {
        if (!ok)
            throw new Exception(m);
    }

    static int Damage(float distance, int seed, out string shape)
    {
        var wall = new StructureWall
        {
            ax = 6,
            ay = 10 + distance,
            bx = 14,
            by = 10 + distance,
            vax = 6,
            vay = 10 + distance,
            vbx = 14,
            vby = 10 + distance,
            height = 2.8f,
            thickness = .25f,
            material = "concrete",
        };
        for (int i = 0; i < 80; i++)
            wall.columns.Add(
                new StructureColumn
                {
                    spans = new List<StructureSpan>
                    {
                        new StructureSpan { lo = 0, hi = 2.8f },
                    },
                }
            );
        var h = new DynamicBuilding(
            new List<StructureWall> { wall },
            new List<StructureRoof>(),
            new List<StructureFloor>(),
            new List<StructureLayer>()
        );
        var m = new BattleMap();
        m.Roof = new bool[m.Tiles.Length];
        m.ProceduralCells = new bool[m.Tiles.Length];
        h.Blast(m, new V2(10, 10), 3.3f, 1, false, new V2(0, 1), seed, 1.4f);
        shape = string.Concat(h.Walls[0].Solid.Select(x => x ? "1" : "0"));
        return h.Walls[0].Solid.Count(x => !x);
    }

    static void Main()
    {
        string s0,
            s1,
            s2,
            s3;
        int d0 = Damage(0, 1, out s0),
            d1 = Damage(1, 1, out s1),
            d2 = Damage(2.8f, 1, out s2),
            d3 = Damage(3.4f, 1, out s3);
        Check(d0 > d1 && d1 > d2 && d3 == 0, "blast distance falloff");
        Damage(0, 2, out s1);
        Check(s0 != s1, "ragged variation");
        Console.WriteLine(
            "PASS radial wall blast falloff=" + d0 + "," + d1 + "," + d2 + "," + d3 + "; different impact contours"
        );
        var rng = new Random(161);
        for (int n = 0; n < 120; n++)
        {
            var roof = new HashSet<long>();
            var anchors = new HashSet<long>();
            for (int y = -2; y < 22; y++)
            for (int x = -2; x < 24; x++)
                if (rng.Next(100) > 12)
                {
                    long k = DynamicBuilding.Key(x, y);
                    roof.Add(k);
                    if ((x == -2 || x == 23 || y == -2 || y == 21) && rng.Next(3) == 0)
                        anchors.Add(k);
                }
            var old = new LegacyRoof { Roof = new HashSet<long>(roof), Anchors = anchors };
            old.CollapseRoof();
            RoofSupport.Collapse(roof, anchors);
            Check(roof.SetEquals(old.Roof), "roof support equivalence case " + n);
        }
        Console.WriteLine("PASS 120 roof collapse comparisons against version 160");
        var b = new BattleSimulation();
        var preload = Rubezh.Graphics.LookSprites(b);
        for (int role = 0; role < 6; role++)
        for (int id = 0; id < 100; id++)
        for (int frame = 0; frame < 4; frame++)
            Check(preload.Contains(Rubezh.PawnAtlas.Sprite(null, true, (Role)role, id, frame)), "reinforcement sprite");
        Check(preload.Contains(7973), "7973 missing");
        Console.WriteLine("PASS 2400 reinforcement appearance combinations including 7973");
        var canvas = new PixelCanvas(32, 32);
        canvas.Occlusion = new float[1024];
        canvas.RoofOcclusion = new float[1024];
        canvas.RoofBlocker = true;
        canvas.StoreDepth(1, 10);
        canvas.RoofBlocker = false;
        canvas.StoreDepth(1, 20);
        Check(canvas.Occlusion[1] == 20 && canvas.RoofOcclusion[1] == 10, "interior prop roof mask");
        canvas.RoofBlocker = true;
        canvas.StoreDepth(1, 30);
        Check(canvas.RoofOcclusion[1] == 30, "outside blocker retained");
        Console.WriteLine("PASS independent roof depth: indoor prop excluded, outdoor foreground retained");
        var map = JObject.Parse(
            Rubezh.UnityPort.PackedLevel.Decode(
                System.IO.File.ReadAllBytes("Assets/Rubezh/Resources/Regenerated/Campaign_20.bytes")
            )
        );
        double worst = 0;
        int cells = 0,
            buildings = 0;
        foreach (var token in map["dynamicBuildings"])
        {
            var h = new DynamicBuilding(
                token["walls"].ToObject<List<StructureWall>>(),
                token["roof"].ToObject<List<StructureRoof>>(),
                token["floors"].ToObject<List<StructureFloor>>(),
                new List<StructureLayer>()
            );
            cells += h.Roof.Count;
            var watch = Stopwatch.StartNew();
            typeof(DynamicBuilding)
                .GetMethod("CollapseRoof", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(h, null);
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            buildings++;
        }
        var battle20 = new BattleSimulation();
        var m = battle20.Map;
        m.Columns = (int)map["columns"];
        m.Rows = (int)map["rows"];
        int count = m.Columns * m.Rows;
        m.Tiles = new Tile[count];
        m.Roof = new bool[count];
        m.ProceduralCells = Enumerable.Repeat(true, count).ToArray();
        m.ProceduralProtection = new float[count];
        m.ProceduralHeight = new float[count];
        m.DynamicBuildings = new List<DynamicBuilding>();
        foreach (var token in map["dynamicBuildings"])
            m.DynamicBuildings.Add(
                new DynamicBuilding(
                    token["walls"].ToObject<List<StructureWall>>(),
                    token["roof"].ToObject<List<StructureRoof>>(),
                    token["floors"].ToObject<List<StructureFloor>>(),
                    new List<StructureLayer>()
                )
            );
        double maxBlast = 0;
        int shots = 0;
        foreach (var building in m.DynamicBuildings)
        {
            var target = building.Walls[0].Point(.5f);
            for (int kind = 0; kind < 3; kind++)
            {
                DynamicStructures.Blast(battle20, target, kind == 2 ? 4.3f : 3.3f, kind, false, new V2(1, 0), 1.4f);
                Console.WriteLine(
                    "blast "
                        + shots
                        + " ms="
                        + DynamicStructures.LastBlastMilliseconds.ToString("F1")
                        + " walls="
                        + DynamicStructures.LastWallCollapseMs.ToString("F1")
                        + " roof="
                        + DynamicStructures.LastRoofCollapseMs.ToString("F1")
                        + " refresh="
                        + DynamicStructures.LastRefreshMs.ToString("F1")
                );
                maxBlast = Math.Max(maxBlast, DynamicStructures.LastBlastMilliseconds);
                shots++;
                m.BrokenCovers.Clear();
            }
        }
        Console.WriteLine(
            "PASS mission20 "
                + shots
                + " mixed structural explosions: max_ms="
                + maxBlast.ToString("F1")
                + "; without original wall pixels / native GPU uploads"
        );
        Console.WriteLine(
            "PASS mission20 roof support: buildings="
                + buildings
                + " microcells="
                + cells
                + " worst_ms="
                + worst.ToString("F1")
                + " (host .NET, not Android)"
        );
    }
}
