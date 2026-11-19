using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SilentOrders.Core;

class Wall162Check
{
    static void Main()
    {
        var j = JObject.Parse(
            Rubezh.UnityPort.PackedLevel.Decode(
                System.IO.File.ReadAllBytes("Assets/Rubezh/Resources/Regenerated/Campaign_19.bytes")
            )
        );
        var m = new BattleMap();
        m.DynamicBuildings = new List<DynamicBuilding>();
        foreach (var t in j["dynamicBuildings"])
            m.DynamicBuildings.Add(
                new DynamicBuilding(
                    t["walls"].ToObject<List<StructureWall>>(),
                    t["roof"].ToObject<List<StructureRoof>>(),
                    t["floors"].ToObject<List<StructureFloor>>(),
                    new List<StructureLayer>()
                )
            );
        var rng = new Random(162);
        for (int state = 0; state < 2; state++)
        {
            if (state == 1)
                foreach (var h in m.DynamicBuildings)
                foreach (var w in h.Walls)
                    for (int i = 0; i < w.Solid.Length; i++)
                        if (rng.Next(4) == 0)
                            w.Solid[i] = false;
            for (int n = 0; n < 10000; n++)
            {
                var a = new V2(rng.Next(-10, 100), rng.Next(-10, 100));
                var b = a + new V2(rng.Next(-15, 16), rng.Next(-15, 16));
                float z = (float)rng.NextDouble() * 3;
                V2 h1,
                    h2;
                float t1,
                    t2;
                bool x = DynamicStructures.Ray(m, a, z, b, 1.3f, out h1, out t1),
                    y = LegacyRay(m, a, z, b, 1.3f, out h2, out t2);
                if (x != y || Math.Abs(t1 - t2) > .00001f)
                    throw new Exception("Ray mismatch " + n);
            }
        }
        var ix = DynamicWallIndex.For(m);
        Console.WriteLine(
            "PASS 20000 indexed rays match exhaustive rays, intact and damaged; candidates="
                + ix.Candidates
                + " exhaustive="
                + (ix.Queries * ix.WallCount)
        );
        var sw = new StructureWall
        {
            ax = 6,
            ay = 10,
            bx = 14,
            by = 10,
            vax = 6,
            vay = 10,
            vbx = 14,
            vby = 10,
            height = 2.8f,
            thickness = .25f,
            material = "concrete",
        };
        for (int i = 0; i < 80; i++)
            sw.columns.Add(
                new StructureColumn
                {
                    spans = new List<StructureSpan>
                    {
                        new StructureSpan { lo = 0, hi = 2.8f },
                    },
                }
            );
        var house = new DynamicBuilding(
            new List<StructureWall> { sw },
            new List<StructureRoof>(),
            new List<StructureFloor>(),
            new List<StructureLayer>()
        );
        var bm = new BattleMap();
        bm.Roof = new bool[bm.Tiles.Length];
        bm.ProceduralCells = new bool[bm.Tiles.Length];
        house.Blast(bm, new V2(10, 10), 3.3f, 1, false, new V2(0, 1), 1, 1.4f);
        var wall = house.Walls[0];
        for (int i = 0; i < wall.NX; i++)
            if (Math.Abs(wall.Point((i + .5f) / wall.NX).X - 10) > .6f)
                for (float z = .2f; z < 2.6f; z += .2f)
                    if (!wall.At((i + .5f) / wall.NX, z))
                        throw new Exception("Breach extends along wall");
        Console.WriteLine("PASS localized breach: distant wall columns intact");
    }

    static bool LegacyRay(BattleMap m, V2 a, float az, V2 end, float ez, out V2 hit, out float fraction)
    {
        hit = end;
        fraction = 1;
        bool found = false;
        if (m.DynamicBuildings == null)
            return false;
        var d = end - a;
        foreach (var b in m.DynamicBuildings)
        foreach (var w in b.Walls)
        {
            var p = w.Point(0);
            var q = w.Point(1);
            float pad = Math.Max(.025f, w.Source.thickness / 3);
            if (
                Math.Max(a.X, end.X) < Math.Min(p.X, q.X) - pad
                || Math.Min(a.X, end.X) > Math.Max(p.X, q.X) + pad
                || Math.Max(a.Y, end.Y) < Math.Min(p.Y, q.Y) - pad
                || Math.Min(a.Y, end.Y) > Math.Max(p.Y, q.Y) + pad
            )
                continue;
            var e = q - p;
            float den = d.X * e.Y - d.Y * e.X;
            if (Math.Abs(den) < .000001f)
            {
                if (Math.Abs(e.X * (a.Y - p.Y) - e.Y * (a.X - p.X)) / Math.Max(.00001f, e.Length) > pad)
                    continue;
                float u0 = w.U(a),
                    u1 = w.U(end),
                    du = u1 - u0;
                float begin = .00002f,
                    finish = fraction;
                if (Math.Abs(du) > .000001f)
                {
                    begin = Math.Max(begin, Math.Min((0 - u0) / du, (1 - u0) / du));
                    finish = Math.Min(finish, Math.Max((0 - u0) / du, (1 - u0) / du));
                }
                for (int i = 0; i <= w.NX && begin <= finish; i++)
                {
                    float t = begin + (finish - begin) * i / w.NX;
                    if (w.At(u0 + du * t, az + (ez - az) * t))
                    {
                        fraction = t;
                        hit = a + d * t;
                        found = true;
                        break;
                    }
                }
                continue;
            }
            var v = p - a;
            float tt = (v.X * e.Y - v.Y * e.X) / den,
                u = (v.X * d.Y - v.Y * d.X) / den;
            if (tt > .00001f && tt <= fraction && u >= 0 && u <= 1 && w.At(u, az + (ez - az) * tt))
            {
                fraction = tt;
                hit = a + d * tt;
                found = true;
            }
        }
        return found;
    }
}
