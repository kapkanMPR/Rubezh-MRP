using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Rubezh.UnityPort;
using Rubezh.UnityPort.Hybrid;
using UnityEngine;

// Runs production mesh generation under API contracts, with real vector mathematics.
// Does not replace a Unity Editor import/shader compile or an Android GPU run.
public static class Hybrid221Check
{
    static int checks;

    static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new Exception(label);
    }

    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    static bool Near(float a, float b, float epsilon = .0001f) => Math.Abs(a - b) <= epsilon;

    static bool Inside(float a, float min, float max, float epsilon = .001f) =>
        a >= min - epsilon && a <= max + epsilon;

    static void Verify(HybridMeshData data, string label)
    {
        Check(data.Vertices.Count > 0, label + ": vertices");
        Check(data.Vertices.Count == data.Normals.Count, label + ": normals");
        Check(data.Vertices.Count == data.UV.Count, label + ": uv");
        Check(data.Vertices.Count == data.Colors.Count, label + ": colors");
        Check(data.Triangles.Count > 0 && data.Triangles.Count % 3 == 0, label + ": triangles");
        for (int i = 0; i < data.Vertices.Count; i++)
        {
            var p = data.Vertices[i];
            var n = data.Normals[i];
            var uv = data.UV[i];
            Check(Finite(p.x) && Finite(p.y) && Finite(p.z), label + ": finite position " + i);
            Check(Finite(uv.x) && Finite(uv.y), label + ": finite UV " + i);
            Check(Near(n.magnitude, 1, .002f), label + ": unit normal " + i + " " + n.magnitude);
        }
        for (int t = 0; t < data.Triangles.Count; t += 3)
        {
            int a = data.Triangles[t],
                b = data.Triangles[t + 1],
                c = data.Triangles[t + 2];
            Check(
                a >= 0
                    && b >= 0
                    && c >= 0
                    && a < data.Vertices.Count
                    && b < data.Vertices.Count
                    && c < data.Vertices.Count,
                label + ": indices " + t
            );
            var normal = Vector3.Cross(data.Vertices[b] - data.Vertices[a], data.Vertices[c] - data.Vertices[a]);
            Check(normal.sqrMagnitude > 1e-12f, label + ": nondegenerate triangle " + t);
            Check(
                Vector3.Dot(normal, data.Normals[a]) > 0
                    && Vector3.Dot(normal, data.Normals[b]) > 0
                    && Vector3.Dot(normal, data.Normals[c]) > 0,
                label + ": winding " + t
            );
        }
        var mesh = data.CreateMesh(label);
        Check(mesh.normals.Length == data.Vertices.Count, label + ": uploaded normals");
        Check(mesh.uv.Length == data.Vertices.Count, label + ": uploaded UV");
        Check(mesh.triangles.Length == data.Triangles.Count, label + ": uploaded triangles");
        foreach (var p in data.Vertices)
            Check(
                Inside(p.x, mesh.bounds.min.x, mesh.bounds.max.x)
                    && Inside(p.y, mesh.bounds.min.y, mesh.bounds.max.y)
                    && Inside(p.z, mesh.bounds.min.z, mesh.bounds.max.z),
                label + ": bounds"
            );
    }

    // Moller-Trumbore ray intersection. Independent of generation and no Unity Physics mocks.
    static List<float> Hits(HybridMeshData data, Vector3 origin, Vector3 direction, float max = 100)
    {
        var hits = new List<float>();
        for (int t = 0; t < data.Triangles.Count; t += 3)
        {
            var a = data.Vertices[data.Triangles[t]];
            var b = data.Vertices[data.Triangles[t + 1]];
            var c = data.Vertices[data.Triangles[t + 2]];
            var e1 = b - a;
            var e2 = c - a;
            var h = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, h);
            if (Math.Abs(det) < 1e-7f)
                continue;
            float inv = 1 / det;
            var s = origin - a;
            float u = inv * Vector3.Dot(s, h);
            if (u < 0 || u > 1)
                continue;
            var q = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(direction, q);
            if (v < 0 || u + v > 1)
                continue;
            float distance = inv * Vector3.Dot(e2, q);
            if (distance >= 0 && distance <= max && !hits.Any(x => Near(x, distance)))
                hits.Add(distance);
        }
        hits.Sort();
        return hits;
    }

    static float Area(HybridMeshData data)
    {
        float result = 0;
        for (int t = 0; t < data.Triangles.Count; t += 3)
        {
            var a = data.Vertices[data.Triangles[t]];
            var b = data.Vertices[data.Triangles[t + 1]];
            var c = data.Vertices[data.Triangles[t + 2]];
            result += Vector3.Cross(b - a, c - a).magnitude * .5f;
        }
        return result;
    }

    static float Volume(HybridMeshData data)
    {
        float result = 0;
        for (int t = 0; t < data.Triangles.Count; t += 3)
        {
            var a = data.Vertices[data.Triangles[t]];
            var b = data.Vertices[data.Triangles[t + 1]];
            var c = data.Vertices[data.Triangles[t + 2]];
            result += Vector3.Dot(a, Vector3.Cross(b, c)) / 6;
        }
        return result;
    }

    static void Aperture(List<HybridPart> parts, float x, float y, float z, float distance, bool open, string label)
    {
        int hits = 0;
        foreach (var part in parts.Where(p => !p.Roof))
            hits += Hits(part.Geometry, new Vector3(x, y, z), Vector3.forward, distance).Count;
        Check(open ? hits == 0 : hits >= 2, label + ": " + hits + " intersections");
    }

    static void Primitives()
    {
        var box = new HybridMeshData();
        HybridGeometry.Box(box, Vector3.zero, new Vector3(1, 2, 3), HybridGeometry.White);
        Verify(box, "box");
        Check(Near(Volume(box), 6), "Box outward winding has correct enclosed volume");
        var bevel = new HybridMeshData();
        HybridGeometry.BevelBox(bevel, Vector3.zero, new Vector3(1, 2, 3), .08f, HybridGeometry.White);
        Verify(bevel, "bevel");
        Check(Volume(bevel) > 5 && Volume(bevel) < 6, "Bevel outward enclosed volume");
        var cylinder = new HybridMeshData();
        HybridGeometry.Cylinder(cylinder, Vector3.zero, 1, 2, HybridGeometry.White, 20);
        Verify(cylinder, "cylinder");
        Check(
            Near(Volume(cylinder), (float)(20 * Math.Sin(2 * Math.PI / 20)), .001f),
            "Cylinder cap and side winding volume"
        );
        var roof = new HybridMeshData();
        HybridGeometry.Roof(roof, 0, 4, 0, 4, 3, 4, HybridGeometry.White);
        Verify(roof, "roof");
        Check(Volume(roof) > 0, "Roof closed slab outward winding");
    }

    static void Layout()
    {
        var parts = HybridLayout.Build();
        Check(parts.Count > 8 && parts.Count < 100, "Chunked renderer count remains bounded");
        Check(parts.Any(x => x.Roof) && parts.Any(x => !x.Roof), "Independent roof visibility");
        Check(
            parts.Any(x => x.Surface == HybridSurface.Wood)
                && parts.Any(x => x.Surface == HybridSurface.Metal)
                && parts.Any(x => x.Surface == HybridSurface.Brick),
            "Detailed object materials"
        );
        foreach (var part in parts)
        {
            Verify(part.Geometry, part.Name);
            foreach (var v in part.Geometry.Vertices)
                Check(
                    Inside(v.x, 0, 36) && Inside(v.z, 0, 24) && Inside(v.y, -1, 10),
                    part.Name + ": level01 footprint"
                );
        }
        Aperture(parts, 27.5f, 1.1f, 9.5f, .8f, true, "Main walk-through doorway");
        Aperture(parts, 24.6f, 1.5f, 9.5f, .8f, true, "Main open window between mullion/jamb");
        Aperture(parts, 23.5f, 1.5f, 9.5f, .8f, false, "Main solid wall");
        Aperture(parts, 8.5f, 1.1f, 13.8f, .8f, true, "Workshop walk-through doorway");
        Aperture(parts, 6.1f, 1.5f, 13.8f, .8f, true, "Workshop window");
        Aperture(parts, 5.4f, 1.5f, 13.8f, .8f, false, "Workshop solid wall");
        int roadTriangles = parts.Where(p => p.Surface == HybridSurface.Road).Sum(p => p.Geometry.TriangleCount);
        Check(roadTriangles == 6, "Road planes are emitted once across three chunks");
        var exportParts = new List<object>();
        Action<string, string, bool, HybridMeshData> add = (name, surface, roof, data) =>
            exportParts.Add(
                new
                {
                    name,
                    surface,
                    roof,
                    vertices = data.Vertices.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                    normals = data.Normals.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
                    uv = data.UV.Select(v => new[] { v.x, v.y }).ToArray(),
                    colors = data.Colors.Select(v => new[] { v.r, v.g, v.b, v.a }).ToArray(),
                    triangles = data.Triangles.ToArray(),
                }
            );
        foreach (var part in parts)
            add(part.Name, part.Surface.ToString(), part.Roof, part.Geometry);
        var demo = new HybridMeshData();
        demo.Append(new HybridWallState().BuildMesh(), HybridLayout.DemoWallPosition);
        add("demo-wall", "Brick", false, demo);
        string output =
            Environment.GetEnvironmentVariable("RUBEZH_TEST_OUTPUT") ?? "Build/Reports/AI-0.12.221-Hybrid/Current";
        Directory.CreateDirectory(output);
        File.WriteAllText(
            Path.Combine(output, "world.json"),
            System.Text.Json.JsonSerializer.Serialize(new { parts = exportParts })
        );
        exportParts.RemoveAt(exportParts.Count - 1);
        var damagedState = new HybridWallState();
        damagedState.Damage(4, 1.2f, 1.2f);
        var damaged = new HybridMeshData();
        damaged.Append(damagedState.BuildMesh(), HybridLayout.DemoWallPosition);
        add("demo-wall", "Brick", false, damaged);
        File.WriteAllText(
            Path.Combine(output, "world-damaged.json"),
            System.Text.Json.JsonSerializer.Serialize(new { parts = exportParts })
        );
        int triangles = parts.Sum(x => x.Geometry.Triangles.Count / 3),
            vertices = parts.Sum(x => x.Geometry.Vertices.Count);
        Check(triangles < 100000, "Prototype geometry budget");
        Console.WriteLine(
            "Layout: " + parts.Count + " material/chunk groups; " + vertices + " vertices; " + triangles + " triangles."
        );
    }

    static void Wall()
    {
        var wall = new HybridWallState();
        Check(wall.Solid.Length == wall.Columns * wall.Rows, "Wall Solid dimensions");
        Check(wall.Solid.All(x => x), "Wall starts intact");
        var intact = wall.BuildMesh();
        Verify(intact, "intact wall");
        Check(intact.TriangleCount == 12, "Intact wall greedily merges to six faces");
        Check(
            Near(Volume(intact), wall.Columns * wall.Rows * wall.CellSize * wall.CellSize * wall.Thickness, .001f),
            "Intact wall exact volume"
        );
        VerifyWallCells(wall, intact);
        int initial = wall.Solid.Count(x => x);
        int changed = wall.Damage(4, 1.5f, .8f);
        Check(changed > 0 && changed < initial, "Localized breach");
        Check(initial - wall.Solid.Count(x => x) == changed, "Damage count equals removed solid cells");
        Check(wall.Damage(4, 1.5f, .8f) == 0, "Repeated explosion is idempotent");
        Check(wall.Damage(-100, -100, .8f) == 0, "Outside damage changes nothing");
        var breached = wall.BuildMesh();
        Verify(breached, "breached wall");
        VerifyWallCells(wall, breached);
        Check(
            Near(Volume(breached), wall.Solid.Count(x => x) * wall.CellSize * wall.CellSize * wall.Thickness, .001f),
            "Breach removes only occupied-cell volume"
        );
        int boundaryEdges = 0;
        for (int y = 0; y < wall.Rows; y++)
        for (int x = 0; x < wall.Columns; x++)
            if (wall.IsSolid(x, y))
            {
                if (!wall.IsSolid(x - 1, y))
                    boundaryEdges++;
                if (!wall.IsSolid(x + 1, y))
                    boundaryEdges++;
                if (!wall.IsSolid(x, y - 1))
                    boundaryEdges++;
                if (!wall.IsSolid(x, y + 1))
                    boundaryEdges++;
            }
        float expectedArea =
            2 * wall.Solid.Count(x => x) * wall.CellSize * wall.CellSize
            + boundaryEdges * wall.CellSize * wall.Thickness;
        Check(
            Near(Area(breached), expectedArea, .002f),
            "Exactly exposed inner/outer surfaces; no missing reveals or interior faces"
        );
        int revision = wall.Revision;
        Check(
            wall.Damage(float.NaN, 0, 1) == 0
                && wall.Damage(1, 1, float.PositiveInfinity) == 0
                && wall.Revision == revision,
            "Invalid damage does not alter state"
        );
        wall.Damage(4, 1.5f, 100);
        Check(
            wall.Solid.All(x => !x) && wall.BuildMesh().Vertices.Count == 0,
            "Complete destruction has empty geometry"
        );
        wall.Reset();
        Check(wall.Solid.All(x => x), "Reset repairs every cell");
        Console.WriteLine(
            "Wall: " + wall.Columns + "x" + wall.Rows + " cells; local breach removed " + changed + " cells."
        );
    }

    static void VerifyWallCells(HybridWallState wall, HybridMeshData mesh)
    {
        for (int y = 0; y < wall.Rows; y++)
        for (int x = 0; x < wall.Columns; x++)
        {
            var hits = Hits(
                mesh,
                new Vector3((x + .37f) * wall.CellSize, (y + .61f) * wall.CellSize, -2),
                Vector3.forward,
                4
            );
            Check(
                hits.Count == (wall.IsSolid(x, y) ? 2 : 0),
                "Grid occupancy matches through-hole ray at " + x + "," + y
            );
        }
    }

    static readonly Dictionary<string, UnityEngine.Object> resources = new Dictionary<string, UnityEngine.Object>();
    static readonly List<UnityEngine.Object> unloaded = new List<UnityEngine.Object>();

    static void ResourcesForTest(string missing = null, bool corrupt = false)
    {
        resources.Clear();
        unloaded.Clear();
        UnityEngine.Object.TrackDestroyed = true;
        UnityEngine.Object.DestroyedObjects.Clear();
        Resources.OnUnload = o => unloaded.Add(o);
        Resources.Loader = path =>
        {
            if (path == missing)
                return null;
            if (resources.TryGetValue(path, out var known))
                return known;
            UnityEngine.Object value = null;
            if (path == "HybridRange/SurfaceTemplate")
                value = new Material(new Shader());
            else if (path == "HybridRange/HybridSprite")
                value = new Shader();
            else if (path.StartsWith("HybridRange/surface-"))
                value = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            else if (path == "HybridRange/actors-layout")
                value = new TextAsset
                {
                    text = corrupt ? "{}" : File.ReadAllText("Assets/Rubezh/Resources/HybridRange/actors-layout.json"),
                };
            else if (path == "HybridRange/actors")
            {
                var data = JsonUtility.FromJson<HybridActors.AtlasData>(
                    File.ReadAllText("Assets/Rubezh/Resources/HybridRange/actors-layout.json")
                );
                value = new Texture2D(data.width, data.height, TextureFormat.RGBA32, true);
            }
            else if (path == "FieldCamp/CampTheme")
                value = new UnityEngine.UIElements.ThemeStyleSheet();
            if (value != null)
                resources[path] = value;
            return value;
        };
    }

    static T Field<T>(object obj, string name) =>
        (T)
            obj.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .GetValue(obj);

    static void SetField(object obj, string name, object value) =>
        obj.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .SetValue(obj, value);

    static void Call(object obj, string method, params object[] args) =>
        obj.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(obj, args);

    static void Materials()
    {
        ResourcesForTest();
        var materials = new HybridMaterials();
        Check(materials.MaterialCount == 8 && materials.TextureCount == 4, "Eight shared materials, four textures");
        var originals = Enum.GetValues(typeof(HybridSurface)).Cast<HybridSurface>().Select(materials.Get).ToArray();
        foreach (var surface in Enum.GetValues(typeof(HybridSurface)).Cast<HybridSurface>())
            Check(
                ReferenceEquals(materials.Get(surface), materials.Get(surface)),
                "Shared material identity " + surface
            );
        Check(
            ReferenceEquals(
                materials.Get(HybridSurface.Ground).mainTexture,
                materials.Get(HybridSurface.Concrete).mainTexture
            ),
            "Shared rough texture"
        );
        materials.Dispose();
        materials.Dispose();
        Check(originals.All(x => x.Destroyed), "All cloned materials released");
        Check(unloaded.Count == 5 && unloaded.Distinct().Count() == 5, "Four surfaces and template unloaded once");
        bool rejected = false;
        try
        {
            materials.Get(HybridSurface.Ground);
        }
        catch (ObjectDisposedException)
        {
            rejected = true;
        }
        Check(rejected, "Disposed materials reject use");
        ResourcesForTest("HybridRange/surface-brick");
        rejected = false;
        try
        {
            new HybridMaterials();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        Check(rejected, "Missing material surface fails clearly");
        Check(
            unloaded.Count == 2
                && unloaded.Contains(resources["HybridRange/surface-rough"])
                && unloaded.Contains(resources["HybridRange/SurfaceTemplate"]),
            "Constructor failure unloads borrowed resources"
        );
        Check(
            UnityEngine.Object.DestroyedObjects.OfType<Material>().Count() == 3,
            "Constructor failure releases partial cloned materials"
        );
        Console.WriteLine("Materials: sharing, normal disposal, repeated disposal and failure cleanup verified.");
    }

    static void Actors()
    {
        ResourcesForTest();
        var material = new Material(new Shader());
        var actors = new HybridActors(new GameObject("test parent").transform, material, 27);
        Check(actors.Count == 10, "Default actor count");
        Check(unloaded.Count == 1 && unloaded[0] is TextAsset, "Actor metadata released after parsing");
        var rotation = Quaternion.Euler(35.2643897f, 225, 0);
        actors.Update(0, rotation);
        var previewMesh = Field<Mesh>(actors, "mesh");
        var actorExport = new
        {
            name = "actors",
            surface = "Actor",
            roof = false,
            vertices = previewMesh.vertices.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
            normals = previewMesh.normals.Select(v => new[] { v.x, v.y, v.z }).ToArray(),
            uv = previewMesh.uv.Select(v => new[] { v.x, v.y }).ToArray(),
            colors = previewMesh.colors32.Select(v => new[] { v.r, v.g, v.b, v.a }).ToArray(),
            triangles = previewMesh.triangles,
        };
        string previewOutput =
            Environment.GetEnvironmentVariable("RUBEZH_TEST_OUTPUT") ?? "Build/Reports/AI-0.12.221-Hybrid/Current";
        foreach (string source in new[] { "world", "world-damaged" })
        {
            using (
                var document = System.Text.Json.JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(previewOutput, source + ".json"))
                )
            )
            {
                var parts = document
                    .RootElement.GetProperty("parts")
                    .EnumerateArray()
                    .Select(v => (object)v.Clone())
                    .ToList();
                parts.Add(actorExport);
                File.WriteAllText(
                    Path.Combine(previewOutput, source + "-actors.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { parts })
                );
            }
        }
        foreach (int count in new[] { 10, 50, 100, 10, 0, 100 })
        {
            actors.SetCount(count);
            actors.Update(1.25f, rotation);
            var mesh = Field<Mesh>(actors, "mesh");
            Check(
                actors.Count == count && mesh.vertices.Length == count * 4 && mesh.triangles.Length == count * 6,
                "Actor mesh count " + count
            );
            for (int i = 0; i < count; i++)
            {
                var p = actors.Position(i);
                Check(Inside(p.x, 0, 36) && Inside(p.z, 0, 24) && Near(p.y, .035f), "Actor ground position " + i);
                for (int n = 0; n < 4; n++)
                {
                    var uv = mesh.uv[i * 4 + n];
                    Check(Inside(uv.x, 0, 1) && Inside(uv.y, 0, 1), "Actor atlas UV " + i);
                }
                float pixelHeight =
                    (mesh.uv[i * 4 + 2].y - mesh.uv[i * 4].y) * Field<HybridActors.AtlasData>(actors, "data").height;
                float pixelWidth =
                    (mesh.uv[i * 4 + 1].x - mesh.uv[i * 4].x) * Field<HybridActors.AtlasData>(actors, "data").width;
                float unitScale = 1.8f / Field<HybridActors.AtlasData>(actors, "data").pixelsPerBodyHeight;
                Check(
                    mesh.vertices[i * 4].y >= .035f - .0001f
                        && Near(mesh.vertices[i * 4 + 2].y - mesh.vertices[i * 4].y, pixelHeight * unitScale, .001f),
                    "Vertical pose preserves body scale and stays above ground " + i
                );
                Check(
                    Near(
                        Vector3.Distance(mesh.vertices[i * 4], mesh.vertices[i * 4 + 1]),
                        pixelWidth * unitScale,
                        .001f
                    ),
                    "Pose aspect stays at authored pixel ratio " + i
                );
                var normal = Vector3.Cross(
                    mesh.vertices[i * 4 + 2] - mesh.vertices[i * 4],
                    mesh.vertices[i * 4 + 1] - mesh.vertices[i * 4]
                );
                Check(Vector3.Dot(normal, mesh.normals[i * 4]) > 0, "Actor winding " + i);
                if (i > 0)
                {
                    var prior = (mesh.vertices[(i - 1) * 4] + mesh.vertices[(i - 1) * 4 + 1]) * .5f;
                    var current = (mesh.vertices[i * 4] + mesh.vertices[i * 4 + 1]) * .5f;
                    Check(
                        Vector3.Dot(prior, rotation * Vector3.forward) + .0001f
                            >= Vector3.Dot(current, rotation * Vector3.forward),
                        "Transparent actor order far to near " + i
                    );
                }
            }
        }
        int updates = actors.MeshUpdates;
        for (int i = 0; i < 50; i++)
            actors.Update(1.25f, rotation);
        Check(actors.MeshUpdates == updates, "Paused camera pan/zoom does not rewrite actor mesh");
        actors.Update(1.5f, rotation);
        Check(actors.MeshUpdates == updates + 1, "Moving actor updates one shared mesh");
        var actorMesh = Field<Mesh>(actors, "mesh");
        var actorRoot = Field<GameObject>(actors, "root");
        actors.Dispose();
        actors.Dispose();
        Check(
            actorMesh.Destroyed && actorRoot.Destroyed && material.mainTexture == null,
            "Actor mesh/root released and borrowed material detached"
        );
        Check(unloaded.Count == 2 && unloaded.Distinct().Count() == 2, "Actor metadata and texture each unloaded once");
        foreach (var missing in new[] { "HybridRange/actors-layout", "HybridRange/actors" })
        {
            ResourcesForTest(missing);
            bool failed = false;
            try
            {
                new HybridActors(new GameObject("test").transform, new Material(new Shader()), 27);
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Check(failed, "Missing actor resource fails " + missing);
            Check(
                resources.Values.Where(v => v is Texture2D || v is TextAsset).All(unloaded.Contains),
                "Missing actor resource does not leak others " + missing
            );
        }
        ResourcesForTest(null, true);
        bool invalid = false;
        try
        {
            new HybridActors(new GameObject("test").transform, new Material(new Shader()), 27);
        }
        catch (InvalidOperationException)
        {
            invalid = true;
        }
        Check(invalid, "Malformed actor metadata rejected");
        Check(resources.Values.All(unloaded.Contains), "Malformed actor metadata cleanup");
        Console.WriteLine(
            "Actors: exact atlas layout, 0/10/50/100, depth order, grounded quads, pause reuse and resource lifecycle verified."
        );
    }

    static void Lifecycle()
    {
        ResourcesForTest();
        string exported = null;
        bool allowExport = false;
        int exports = 0;
        var range = new HybridRange(
            () => { },
            json =>
            {
                exported = json;
                exports++;
                return allowExport;
            }
        );
        var camera = Field<Camera>(range, "camera");
        var target = Field<RenderTexture>(range, "target");
        Check(
            !camera.enabled && camera.cullingMask == (1 << 27) && camera.targetTexture == target,
            "Isolated manual camera and layer"
        );
        Check(
            target.width == (int)Screen.safeArea.width
                && target.height > 0
                && target.height < Screen.safeArea.height
                && target.IsCreated(),
            "Native pixel width with toolbar-excluded viewport target"
        );
        var meshes = Field<List<Mesh>>(range, "meshes").ToArray();
        var originalWall = Field<Mesh>(range, "wallMesh");
        var actors = Field<HybridActors>(range, "actors");
        actors.SetCount(50);
        SetField(range, "moving", false);
        SetField(range, "autoCamera", true);
        SetField(range, "center", new Vector3(11, 0, 7));
        SetField(range, "size", 5f);
        Call(range, "ToggleBenchmark");
        Check(
            actors.Count == 100 && Field<bool>(range, "moving") && !Field<bool>(range, "autoCamera"),
            "Benchmark sets declared conditions"
        );
        int ticks = 0;
        while (Field<bool>(range, "benchmark") && ticks++ < 1900)
            range.Tick(1f / 60);
        Check(
            !Field<bool>(range, "benchmark")
                && actors.Count == 50
                && !Field<bool>(range, "moving")
                && Field<bool>(range, "autoCamera"),
            "Benchmark restores actor and camera choices"
        );
        Check(
            Vector3.Distance(Field<Vector3>(range, "center"), new Vector3(11, 0, 7)) < .0001f
                && Near(Field<float>(range, "size"), 5),
            "Benchmark restores exact view and zoom"
        );
        Check(exported != null, "Completed benchmark attempts export even if saving fails");
        string testedReport = exported;
        allowExport = true;
        Call(range, "SaveCurrentOrLastReport");
        Check(exports == 2 && exported == testedReport, "Failed export can retry exact test data after state restore");
        using (var json = System.Text.Json.JsonDocument.Parse(exported))
        {
            var r = json.RootElement;
            Check(
                r.GetProperty("completed").GetBoolean() && r.GetProperty("soldiers").GetInt32() == 100,
                "Report captures tested state before restore"
            );
            var phases = r.GetProperty("frames")
                .EnumerateArray()
                .Select(x => x.GetProperty("phase").GetString())
                .Distinct()
                .ToArray();
            Check(phases.SequenceEqual(new[] { "still", "pan", "zoom" }), "All camera phases sampled");
        }
        var image = Field<UnityEngine.UIElements.Image>(range, "view");
        image.worldBound = new Rect(0, 0, 1280, 720);
        var right = new Vector3(-.70710678f, 0, .70710678f);
        var groundUp = new Vector3(-1.22474487f, 0, -1.22474487f);
        var worldAnchor = Field<Vector3>(range, "center") + (right * 160 + groundUp * 80) * (10f / 720);
        Call(range, "ZoomAt", new Vector2(800, 280), 4f);
        var afterAnchor = Field<Vector3>(range, "center") + (right * 160 + groundUp * 80) * (8f / 720);
        Check(Vector3.Distance(worldAnchor, afterAnchor) < .001f, "Zoom preserves world location beneath gesture");
        var beforePan = Field<Vector3>(range, "center");
        Call(range, "Pan", new Vector2(100, 80));
        Check(
            Vector3.Distance(
                Field<Vector3>(range, "center"),
                beforePan + right * (-100 * 8f / 720) + groundUp * (80 * 8f / 720)
            ) < .001f,
            "Drag maps panel delta onto ground plane"
        );
        Check(
            Field<List<Mesh>>(range, "meshes").SequenceEqual(meshes)
                && ReferenceEquals(originalWall, Field<Mesh>(range, "wallMesh"))
                && Field<int>(range, "wallBuilds") == 1,
            "Camera movement never rebuilds static meshes"
        );
        Call(range, "ToggleBenchmark");
        range.Tick(.1f);
        range.Suspend(true);
        Check(!Field<bool>(range, "benchmark") && actors.Count == 50, "Losing focus cancels and restores benchmark");
        range.Suspend(false);
        int oldWidth = Screen.width;
        var oldSafe = Screen.safeArea;
        Screen.width = 1600;
        Screen.safeArea = new Rect(0, 0, 1600, 900);
        range.Tick(.016f);
        Check(
            !target.IsCreated() && target.Destroyed && Field<RenderTexture>(range, "target").width == 1600,
            "Resize releases old render target"
        );
        Screen.width = oldWidth;
        Screen.safeArea = oldSafe;
        var lastTarget = Field<RenderTexture>(range, "target");
        range.Dispose();
        range.Dispose();
        Check(
            !lastTarget.IsCreated() && lastTarget.Destroyed && camera.targetTexture == null,
            "Range disposal releases render target and camera binding"
        );
        Check(
            meshes.All(m => m.Destroyed) && originalWall.Destroyed,
            "Range disposal releases all generated static meshes"
        );
        Check(unloaded.Where(x => x is Texture2D).Count() == 5, "Range releases exactly its five textures");
        ResourcesForTest("HybridRange/HybridSprite");
        bool failed = false;
        try
        {
            new HybridRange(() => { }, json => true);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }
        Check(failed, "Partial world construction failure reported");
        Check(
            UnityEngine.Object.DestroyedObjects.OfType<Mesh>().Count() == 36,
            "Partial world failure releases all built meshes"
        );
        Check(unloaded.Count == 5, "Partial world failure releases material resources");
        Console.WriteLine(
            "Range: full-resolution RT, automatic 3-phase report, state restore, focus cancellation, resize and failure/disposal verified."
        );
    }

    public static int Main()
    {
        Mesh.RecordData = true; // Opt into mesh-buffer recording only for this geometry fixture.
        Primitives();
        Layout();
        Wall();
        Materials();
        Actors();
        Lifecycle();
        Console.WriteLine(
            "PASS "
                + checks
                + " hybrid geometry checks; portable API contracts only, Unity GPU/build validation pending."
        );
        return 0;
    }
}
