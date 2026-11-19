using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Rendering;
using UnityEngine;

static class GroundPalette221Check
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;

    static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new Exception(label);
    }

    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);

    static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Flags).GetValue(o);

    static object Call(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, Flags).Invoke(o, args);

    static HdGpuRenderer Fixture(int pages = 8)
    {
        var renderer = (HdGpuRenderer)
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HdGpuRenderer));
        foreach (
            string name in new[]
            {
                "vertices",
                "uvs",
                "clips",
                "depths",
                "colors",
                "indices",
                "runs",
                "atlases",
                "materials",
                "nativeTexturePages",
                "groundMaterialPalettes",
                "groundMaterialPages",
                "objectGroups",
                "objectDraws",
            }
        )
        {
            var f = renderer.GetType().GetField(name, Flags);
            f.SetValue(renderer, Activator.CreateInstance(f.FieldType));
        }
        Set(renderer, "canvas", new PixelCanvas(200, 200) { LayerLeft = 9, LayerRight = 131 });
        Set(renderer, "UnitySceneMode", true);
        Set(renderer, "nativeAtlasMode", true);
        Set(renderer, "spriteAtlasPages", pages);
        Set(renderer, "shader", new Shader());
        var atlases = Get<List<Texture2D>>(renderer, "atlases");
        var materials = Get<List<Material>>(renderer, "materials");
        var textures = Get<Dictionary<Texture2D, int>>(renderer, "nativeTexturePages");
        for (int page = 0; page < pages; page++)
        {
            var texture = new Texture2D(256 + page * 16, 256) { name = "original source " + page };
            atlases.Add(texture);
            var material = new Material(new Shader());
            material.SetTexture("_MainTex", texture);
            materials.Add(material);
            if (page != 0)
                textures.Add(texture, page);
        }
        return renderer;
    }

    static void Add(HdGpuRenderer r, int page, float mode = 0)
    {
        int start = Get<List<Vector2>>(r, "depths").Count;
        Call(
            r,
            "Quad",
            1f,
            2f,
            21f,
            2f,
            21f,
            12f,
            1f,
            12f,
            .13f,
            .27f,
            .63f,
            .77f,
            new Color32(109, 85, 61, 137),
            14f,
            false,
            page
        );
        for (int i = start; i < start + 4; i++)
            Get<List<Vector2>>(r, "depths")[i] = new Vector2(14, mode);
    }

    static void Clear(HdGpuRenderer r) => Call(r, "ClearGroundScratch");

    static List<Vector2> Compact(HdGpuRenderer r) => Call(r, "CompactGroundMaterialRuns") as List<Vector2>;

    static void VerifyMapping(HdGpuRenderer r, int[] originalPages, List<Vector2> slots)
    {
        var palettes = Get<Dictionary<int, int[]>>(r, "groundMaterialPalettes");
        var runs = Get<AtlasLayout.DrawRuns>(r, "runs");
        var indices = Get<List<int>>(r, "indices");
        Check(
            slots == null || slots.Count == Get<List<Vector3>>(r, "vertices").Count,
            "selector has exactly one value per original vertex"
        );
        foreach (var run in runs.Items)
        {
            int[] palette;
            bool packed = palettes.TryGetValue(run.Page, out palette);
            Check(!packed || palette.Length <= 4, "a grouped material uses at most four original atlases");
            if (packed)
            {
                var material = Get<List<Material>>(r, "materials")[run.Page];
                var atlases = Get<List<Texture2D>>(r, "atlases");
                Check(material.GetFloat("_GroundMultiTex") == 1, "generated material enables texture selection");
                Check(atlases[run.Page] == null, "generated material owns no duplicate borrowed atlas texture");
                string[] names = { "_MainTex", "_GroundTex1", "_GroundTex2", "_GroundTex3" };
                for (int slot = 0; slot < 4; slot++)
                    Check(
                        material.GetTexture(names[slot]) == atlases[palette[Math.Min(slot, palette.Length - 1)]],
                        "palette binds the exact original sampler texture reference"
                    );
            }
            for (int j = run.Start; j < run.Start + run.Count; j += 3)
            {
                int q = indices[j] / 4;
                int expected = originalPages[q];
                for (int k = 0; k < 3; k++)
                {
                    int v = indices[j + k],
                        slot = slots == null ? 0 : (int)slots[v].x;
                    Check(!packed || (slot >= 0 && slot < palette.Length), "slot is in palette bounds");
                    Check(
                        (packed ? palette[slot] : run.Page) == expected,
                        "every triangle vertex samples its exact original atlas"
                    );
                    Check(
                        slots == null || slots[v].x == slots[indices[j]].x,
                        "texture selector is constant across each primitive triangle"
                    );
                }
            }
        }
    }

    static void BasicMapping()
    {
        var r = Fixture();
        int[] pages = Enumerable.Range(0, 40).Select(i => 1 + i % 4).ToArray();
        foreach (int page in pages)
            Add(r, page);
        var indices = Get<List<int>>(r, "indices").ToArray();
        var vertices = Get<List<Vector3>>(r, "vertices").ToArray();
        var uv = Get<List<Vector2>>(r, "uvs").ToArray();
        var depth = Get<List<Vector2>>(r, "depths").ToArray();
        var clips = Get<List<Vector2>>(r, "clips").ToArray();
        var colors = Get<List<Color32>>(r, "colors").ToArray();
        var slots = Compact(r);
        VerifyMapping(r, pages, slots);
        Check(
            Get<AtlasLayout.DrawRuns>(r, "runs").Items.Count == 1,
            "forty alternating runs of four source atlases become one draw"
        );
        Check(
            indices.SequenceEqual(Get<List<int>>(r, "indices")),
            "palette grouping preserves every triangle and index order"
        );
        Check(
            vertices.SequenceEqual(Get<List<Vector3>>(r, "vertices"))
                && uv.SequenceEqual(Get<List<Vector2>>(r, "uvs"))
                && depth.SequenceEqual(Get<List<Vector2>>(r, "depths"))
                && clips.SequenceEqual(Get<List<Vector2>>(r, "clips"))
                && colors.SequenceEqual(Get<List<Color32>>(r, "colors")),
            "palette grouping preserves position/UV/color/alpha/depth/clips"
        );
        int materialCount = Get<List<Material>>(r, "materials").Count;
        for (int iteration = 0; iteration < 30; iteration++)
        {
            Clear(r);
            foreach (int page in pages)
                Add(r, page);
            Compact(r);
        }
        Check(
            Get<List<Material>>(r, "materials").Count == materialCount,
            "repeated ground rebuilds reuse palette materials"
        );
        Clear(r);
        var reverse = pages.Reverse().ToArray();
        foreach (int page in reverse)
            Add(r, page);
        var reverseSlots = Compact(r);
        VerifyMapping(r, reverse, reverseSlots);
        Check(
            Get<List<Material>>(r, "materials").Count == materialCount,
            "same four textures in another painter order reuse canonical palette"
        );
        Clear(r);
        pages = new[] { 1, 2, 3, 4, 5, 6, 7, 1, 2, 3 };
        foreach (int page in pages)
            Add(r, page, -1);
        slots = Compact(r);
        VerifyMapping(r, pages, slots);
        Check(
            Get<AtlasLayout.DrawRuns>(r, "runs").Items.Count >= 3,
            "palette capacity flushes without reordering five-plus source atlases"
        );
    }

    static void UnsupportedModes()
    {
        foreach (float mode in new[] { 1f, 2f, 3f, -2f })
        {
            var r = Fixture();
            int[] pages = { 1, 2, 1 };
            foreach (int page in pages)
                Add(r, page, mode);
            int count = Get<List<Material>>(r, "materials").Count;
            var slots = Compact(r);
            VerifyMapping(r, pages, slots);
            Check(
                Get<AtlasLayout.DrawRuns>(r, "runs").Items.Count == 3
                    && Get<List<Material>>(r, "materials").Count == count,
                "non-ground depth mode never adopts a ground-only shader material"
            );
        }
        var other = Fixture();
        Get<Dictionary<Texture2D, int>>(other, "nativeTexturePages").Remove(Get<List<Texture2D>>(other, "atlases")[2]);
        int[] sources = { 1, 2, 1 };
        foreach (int page in sources)
            Add(other, page);
        var result = Compact(other);
        VerifyMapping(other, sources, result);
        Check(
            Get<AtlasLayout.DrawRuns>(other, "runs").Items.Count == 3,
            "non-native procedural/building texture forces original-page boundary"
        );
    }

    static void BoundedMaterials()
    {
        var r = Fixture(30);
        int original = Get<List<Material>>(r, "materials").Count;
        for (int a = 1; a < 29; a++)
        for (int b = a + 1; b < 30; b++)
        {
            Clear(r);
            int[] sources = { a, b, a, b };
            foreach (int page in sources)
                Add(r, page);
            var slots = Compact(r);
            VerifyMapping(r, sources, slots);
        }
        int palettes = Get<Dictionary<int, int[]>>(r, "groundMaterialPalettes").Count;
        Check(
            palettes <= 256 && Get<List<Material>>(r, "materials").Count <= original + 256,
            "distinct rebuilds cannot grow palette materials beyond fixed cap"
        );
    }

    static void MeshIntegration()
    {
        var r = Fixture();
        var type = typeof(HdGpuRenderer).GetNestedType("GroundBatch", BindingFlags.NonPublic);
        var batch = Activator.CreateInstance(type, true);
        int[] pages = Enumerable.Range(0, 16).Select(i => 1 + i % 4).ToArray();
        Action<int, int, int, int> draw = (left, top, right, bottom) =>
        {
            foreach (int page in pages)
                Add(r, page);
        };
        Mesh.RecordData = true;
        try
        {
            Call(r, "BuildGroundBatch", batch, draw, 0, 0, 1, 1);
        }
        finally
        {
            Mesh.RecordData = false;
        }
        var mesh = Get<Mesh>(batch, "Mesh");
        var resultPages = Get<int[]>(batch, "Pages");
        Check(
            resultPages.Length == 1 && mesh.subMeshCount == 1,
            "ground upload creates one submesh for compatible four-atlas sequence"
        );
        Check(
            mesh.UvChannels.ContainsKey(3) && mesh.UvChannels[3].Length == 16 * 4,
            "ground upload includes full UV3 selector stream"
        );
        var palette = Get<Dictionary<int, int[]>>(r, "groundMaterialPalettes")[resultPages[0]];
        Check(mesh.SubmeshIndices[0].Length == 16 * 6, "uploaded ground submesh retains all triangles");
        for (int q = 0; q < 16; q++)
        for (int v = 0; v < 4; v++)
            Check(
                palette[(int)mesh.UvChannels[3][q * 4 + v].x] == pages[q],
                "uploaded mesh selector resolves original texture for every vertex"
            );
    }

    static void SharedVertexFallback()
    {
        var r = Fixture();
        Add(r, 1);
        Add(r, 2);
        var indices = Get<List<int>>(r, "indices");
        indices[6] = indices[0];
        var before = indices.ToArray();
        var slots = Compact(r);
        Check(
            slots == null && Get<AtlasLayout.DrawRuns>(r, "runs").Items.Count == 2 && before.SequenceEqual(indices),
            "shared vertex across source material runs keeps original draws"
        );
    }

    static void SceneryMeshIntegration()
    {
        var r = Fixture();
        int[] pages = { 1, 2, 1, 3, 1, 2, 1 };
        for (int i = 0; i < pages.Length; i++)
            Add(r, pages[i], i == 3 ? 2 : 1);
        var original = Get<AtlasLayout.DrawRuns>(r, "runs").Items.ToArray();
        var indices = Get<List<int>>(r, "indices").ToArray();
        var type = typeof(HdGpuRenderer).GetNestedType("MeshBatch", BindingFlags.NonPublic);
        var batch = Activator.CreateInstance(type, true);
        Set(r, "pictureBatch", batch);
        Mesh.RecordData = true;
        try
        {
            Call(r, "StoreGeometry", batch);
        }
        finally
        {
            Mesh.RecordData = false;
        }
        var mesh = Get<Mesh>(batch, "Mesh");
        var slots = mesh.UvChannels[3].ToList();
        VerifyMapping(r, pages, slots);
        Check(
            mesh.subMeshCount == 3 && r.SceneryPaletteRunsSaved == 4,
            "seven overlapping draws become three ordered draws around a picture depth boundary"
        );
        Check(Get<int[]>(batch, "Pages")[1] == 3, "picture depth keeps its own source material");
        var source = Get<object>(batch, "Data");
        Check(
            indices.SequenceEqual(Get<int[]>(source, "Indices"))
                && original.SequenceEqual(Get<AtlasLayout.DrawRuns.Run[]>(source, "Runs")),
            "merged mask builder still receives the original uncompacted pages and indices"
        );

        Clear(r);
        for (int i = 0; i < pages.Length; i++)
            Add(r, pages[i], i == 3 ? 2 : 1);
        bool Matches() => (bool)Call(r, "MatchesPictureGeometry", source);
        Check(Matches(), "identical picture inputs reuse the retained geometry");
        var depth = Get<List<Vector2>>(r, "depths");
        var savedDepth = depth[0];
        depth[0] = new Vector2(savedDepth.x + 1, savedDepth.y);
        Check(!Matches(), "changed depth at fixed UV and position invalidates pictures");
        depth[0] = savedDepth;
        var clips = Get<List<Vector2>>(r, "clips");
        var savedClip = clips[0];
        clips[0] = new Vector2(savedClip.x, savedClip.y - 1);
        Check(!Matches(), "changed clip span invalidates pictures");
        clips[0] = savedClip;
        var colors = Get<List<Color32>>(r, "colors");
        var savedColor = colors[0];
        colors[0] = new Color32(savedColor.r, savedColor.g, savedColor.b, 0);
        Check(!Matches(), "changed transparency invalidates pictures");
        colors[0] = savedColor;
        Get<List<int>>(r, "indices")[0] = 1;
        Check(!Matches(), "changed triangle at fixed vertex count invalidates pictures");
    }

    static void SpatialPainterOrder()
    {
        var r = Fixture();
        var source = Get<IList>(r, "objectDraws");
        var type = typeof(HdGpuRenderer).GetNestedType("MeshBatch", BindingFlags.NonPublic);
        var rng = new System.Random(223);
        const int count = 360;
        var rects = new Rect[count];
        var sequence = new Dictionary<object, int>();
        for (int i = 0; i < count; i++)
        {
            var batch = Activator.CreateInstance(type, true);
            Set(batch, "CpuOnly", true);
            Clear(r);
            Add(r, 1 + i % 4, 1);
            float x = i % 6 * 210 + rng.Next(-40, 40);
            float y = rng.Next(40, 220);
            float w = i % 41 == 0 ? 620 : rng.Next(12, 68);
            float h = rng.Next(8, 46);
            rects[i] = new Rect(x, y, w, h);
            var vertices = Get<List<Vector3>>(r, "vertices");
            vertices[0] = new Vector3(x, y, 0);
            vertices[1] = new Vector3(x + w, y, 0);
            vertices[2] = new Vector3(x + w, y + h, 0);
            vertices[3] = new Vector3(x, y + h, 0);
            Call(r, "StoreGeometry", batch);
            source.Add(batch);
            sequence.Add(batch, i);
        }
        var order = new List<int>();
        for (int build = 0; build < 2; build++)
        {
            order.Clear();
            int groups = (int)Call(r, "GroupStaticObjects", source);
            Check(groups < count / 2, "separate columns can reuse spatial mesh groups");
            foreach (var group in Get<IList>(r, "objectGroups").Cast<object>().Take(groups))
            foreach (var batch in Get<IList>(group, "Members"))
                order.Add(sequence[batch]);
            Check(
                order.Count == count && order.Distinct().Count() == count,
                "every nonempty object is submitted exactly once, including after a rebuild"
            );
            var position = new int[count];
            for (int i = 0; i < count; i++)
                position[order[i]] = i;
            for (int a = 0; a < count; a++)
            for (int b = a + 1; b < count; b++)
            {
                Rect left = rects[a],
                    right = rects[b];
                if (
                    left.xMax + 2 < right.xMin
                    || right.xMax + 2 < left.xMin
                    || left.yMax + 2 < right.yMin
                    || right.yMax + 2 < left.yMin
                )
                    continue;
                Check(position[a] < position[b], "every intersecting object pair retains its original painter order");
            }
        }
    }

    internal static int Run()
    {
        BasicMapping();
        UnsupportedModes();
        BoundedMaterials();
        MeshIntegration();
        SharedVertexFallback();
        SceneryMeshIntegration();
        SpatialPainterOrder();
        Console.WriteLine(
            "PASS ground four-atlas palette: "
                + checks
                + " source mapping, order, channel conservation, eligibility and bounded reuse checks."
        );
        return checks;
    }
}
