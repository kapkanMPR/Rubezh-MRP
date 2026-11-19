using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

static class Mask221Check
{
    const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;

    static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new Exception(label);
    }

    static void Set(object o, string name, object value) => o.GetType().GetField(name, Flags).SetValue(o, value);

    static T Get<T>(object o, string name) => (T)o.GetType().GetField(name, Flags).GetValue(o);

    static object Invoke(object o, string name, params object[] args) =>
        o.GetType().GetMethod(name, Flags).Invoke(o, args);

    static object Nested(string type) =>
        Activator.CreateInstance(typeof(HdGpuRenderer).GetNestedType(type, BindingFlags.NonPublic), true);

    static object Geometry(float offset)
    {
        var data = Nested("GeometryData");
        var vertices = new Vector3[12];
        var uv = new Vector2[12];
        var depths = new Vector2[12];
        var clips = new Vector2[12];
        var colors = new Color32[12];
        var indices = new int[18];
        for (int q = 0; q < 3; q++)
        {
            for (int i = 0; i < 4; i++)
            {
                int n = q * 4 + i;
                vertices[n] = new Vector3(offset + q * 10 + (i == 1 || i == 2 ? 5 : 0), 10 + (i >= 2 ? 5 : 0), 0);
                uv[n] = new Vector2(.1f * n, .2f * n);
                depths[n] = new Vector2(100 + n, 1);
                clips[n] = new Vector2(7, 113);
                colors[n] = new Color32((byte)(10 + n), 40, 70, 99);
            }
            int v = q * 4,
                t = q * 6;
            indices[t] = v;
            indices[t + 1] = v + 1;
            indices[t + 2] = v + 2;
            indices[t + 3] = v;
            indices[t + 4] = v + 2;
            indices[t + 5] = v + 3;
        }
        Set(data, "Vertices", vertices);
        Set(data, "UVs", uv);
        Set(data, "Depths", depths);
        Set(data, "Clips", clips);
        Set(data, "Colors", colors);
        Set(data, "Indices", indices);
        Set(
            data,
            "Runs",
            new[]
            {
                new AtlasLayout.DrawRuns.Run
                {
                    Page = 1,
                    Start = 0,
                    Count = 6,
                },
                new AtlasLayout.DrawRuns.Run
                {
                    Page = 2,
                    Start = 6,
                    Count = 6,
                },
                new AtlasLayout.DrawRuns.Run
                {
                    Page = 1,
                    Start = 12,
                    Count = 6,
                },
            }
        );
        return data;
    }

    static void BuilderPreservesAttributes()
    {
        var first = Geometry(3);
        var second = Geometry(70);
        var builder = Nested("MaskMergeBuilder");
        Invoke(builder, "Add", first, 0, 6, true);
        Invoke(builder, "Add", first, 12, 6, true);
        Invoke(builder, "Add", second, 0, 6, false);
        var sourceData = new[] { first, first, second };
        var starts = new[] { 0, 12, 0 };
        var result = Get<List<int>>(builder, "Indices");
        Check(
            result.Count == 18 && Get<List<Vector3>>(builder, "Vertices").Count == 12,
            "mask merge retains every triangle, shares only same-source vertices"
        );
        for (int block = 0; block < 3; block++)
        for (int i = 0; i < 6; i++)
        {
            var data = sourceData[block];
            int old = Get<int[]>(data, "Indices")[starts[block] + i],
                next = result[block * 6 + i];
            Check(
                Get<Vector3[]>(data, "Vertices")[old].Equals(Get<List<Vector3>>(builder, "Vertices")[next]),
                "mask vertex exact"
            );
            Check(Get<Vector2[]>(data, "UVs")[old].Equals(Get<List<Vector2>>(builder, "UVs")[next]), "mask UV exact");
            Check(
                Get<Vector2[]>(data, "Depths")[old].Equals(Get<List<Vector2>>(builder, "Depths")[next]),
                "mask depth/mode exact"
            );
            Check(
                Get<Vector2[]>(data, "Clips")[old].Equals(Get<List<Vector2>>(builder, "Clips")[next]),
                "mask clipping exact"
            );
            Check(
                Get<Color32[]>(data, "Colors")[old].Equals(Get<List<Color32>>(builder, "Colors")[next]),
                "mask color/alpha exact"
            );
        }
    }

    static HdGpuRenderer Fixture()
    {
        var r = (HdGpuRenderer)
            System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(HdGpuRenderer));
        foreach (
            string name in new[]
            {
                "unityMaterials",
                "unityMaskMergedMeshes",
                "unityMergedMaskMaterials",
                "objectDraws",
                "pictureBatch",
            }
        )
        {
            var f = r.GetType().GetField(name, Flags);
            f.SetValue(r, Activator.CreateInstance(f.FieldType, true));
        }
        Set(r, "canvas", new PixelCanvas(100, 100));
        Set(r, "geometrySerial", 1);
        Set(r, "unityMaskMergedGeometry", -1);
        Set(r, "unityMaskCamera", new Camera());
        Set(r, "unityMaskShader", new Shader());
        Set(r, "unityMask", new RenderTexture(100, 100, 0, RenderTextureFormat.RFloat));
        Set(r, "unityRoofMask", new RenderTexture(100, 100, 0, RenderTextureFormat.RFloat));
        var materials = Get<List<Material>>(r, "unityMaterials");
        for (int i = 0; i < 3; i++)
            materials.Add(new Material(new Shader()));
        var batch = Nested("MeshBatch");
        Set(batch, "Data", Geometry(0));
        Set(batch, "Chunk", 5L);
        Set(batch, "BlocksRoof", true);
        Get<IList>(r, "objectDraws").Add(batch);
        r.UnityMergedMaskCommands = true;
        return r;
    }

    static void CheckPictureRanges()
    {
        var renderer = Fixture();
        Get<IList>(renderer, "objectDraws").Clear();
        Set(Get<object>(renderer, "pictureBatch"), "Data", Geometry(0));
        var battle = new BattleSimulation(223);
        battle.Fog.Enabled = false;
        void Visible(float left, float width, int commands, int submesh, int triangles)
        {
            Invoke(renderer, "RenderCachedUnityMasks", battle, new Rect(left, 0, width, 1), true);
            var draws = Get<CommandBuffer>(renderer, "unityObjectMaskCommands").Draws;
            var roof = Get<CommandBuffer>(renderer, "unityRoofMaskCommands").Draws;
            Check(draws.Count == commands && roof.Count == commands, "single draw per visible picture page");
            int count = 0;
            for (int i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                Check(
                    (submesh < 0 || draw.Submesh == submesh) && draw.Submesh == roof[i].Submesh,
                    "roof and object cameras share exactly the visible picture triangles"
                );
                count += draw.Mesh.SubmeshIndices[draw.Submesh].Length / 3;
            }
            Check(count == triangles, "correct picture triangle count at viewport boundaries");
        }
        Visible(0, .08f, 1, 1, 2);
        Visible(.2f, .08f, 1, 2, 2);
        Visible(.16f, .02f, 0, -1, 0);
        Visible(0, 1, 2, 0, 6);
        Invoke(renderer, "ReleaseUnityMaskCommands");
    }

    static void CheckPackedMasksAndFallback()
    {
        Mesh.RecordData = true;
        try
        {
            CheckPictureRanges();
            var r = Fixture();
            var objects = Get<IList>(r, "objectDraws");
            var extra = Nested("MeshBatch");
            var data = Geometry(28);
            var depths = Get<Vector2[]>(data, "Depths");
            for (int i = 0; i < 4; i++)
                depths[i].y = 0;
            // Entire second quad survives; third has one triangle wholly above the range.
            depths[8].y = depths[9].y = depths[10].y = 3;
            Set(extra, "Data", data);
            Set(extra, "Chunk", 5L);
            objects.Add(extra);
            var battle = new BattleSimulation(223);
            battle.Fog.Enabled = false;
            Check(
                (bool)Invoke(r, "EnsureUnityMaskTargets", 100, 100) && r.UnityPackedMasks,
                "blendable RG32F selects full-precision packed masks"
            );
            Check(
                Get<RenderTexture>(r, "unityMask").format == RenderTextureFormat.RGFloat
                    && Get<RenderTexture>(r, "unityRoofMask") == null,
                "only one packed target allocated"
            );
            var view = new Rect(0, 0, 1, 1);
            Invoke(r, "RenderCachedUnityMasks", battle, view, true);
            var camera = Get<Camera>(r, "unityMaskCamera");
            Check(
                camera.RenderCalls.Count == 1
                    && camera.RenderCalls[0].CullingMask == (1 << 30)
                    && camera.RenderCalls[0].Draws == 2
                    && r.UnityRoofMaskDraws == 0,
                "packed masks share one camera pass"
            );
            Check(
                Get<CommandBuffer>(r, "unityRoofMaskCommands") == null
                    && Get<IDictionary>(r, "unityMergedMaskMaterials").Count == 2,
                "packed path allocates only the object command buffer and visible mask materials"
            );
            var commands = Get<CommandBuffer>(r, "unityObjectMaskCommands");
            int roof = 0,
                other = 0;
            foreach (var draw in commands.Draws)
            foreach (int vertex in draw.Mesh.SubmeshIndices[0])
            {
                if (draw.Mesh.UvChannels[4][vertex].x == 1)
                    roof++;
                else
                    other++;
            }
            Check(
                roof == 18 && other == 9,
                "roof channel membership survives MAX batching; nonwriting triangles culled"
            );
            Invoke(r, "RenderCachedUnityMasks", battle, new Rect(.01f, 0, 1, 1), false);
            Check(camera.RenderCalls.Count == 2 && r.UnityMaskGeometryBuilds == 1, "camera pan takes one mask pass");
            var pannedView = new Rect(.01f, 0, 1, 1);
            battle.GreenVehicle = new VehicleArrivalState
            {
                Active = true,
                Side = Side.Green,
                Center = new V2(10, 10),
            };
            Invoke(r, "RenderCachedUnityMasks", battle, pannedView, false);
            Check(
                camera.RenderCalls.Count == 3
                    && r.UnityObjectMaskBuilds == 3
                    && r.UnityRoofMaskBuilds == 3
                    && r.UnityRoofMaskDraws == 0
                    && r.UnityMaskGeometryBuilds == 1,
                "vehicle arrival updates the packed target in one pass; static green channel inputs are retained"
            );
            Invoke(r, "RenderCachedUnityMasks", battle, pannedView, false);
            Check(camera.RenderCalls.Count == 3 && r.UnityObjectMaskDraws == 0, "parked vehicle reuses packed pixels");
            battle.GreenVehicle.Center = new V2(10.125f, 10);
            Invoke(r, "RenderCachedUnityMasks", battle, pannedView, false);
            Check(
                camera.RenderCalls.Count == 4 && camera.RenderCalls[3].Draws == 2 && r.UnityMaskGeometryBuilds == 1,
                "subcell vehicle motion updates both channels together without rebuilding geometry"
            );
            r.UnityMergedMaskCommands = false;
            Check(
                (bool)Invoke(r, "EnsureUnityMaskTargets", 100, 100) && !r.UnityPackedMasks,
                "renderer-based mode restores separate R32F targets"
            );
            Invoke(r, "RenderCachedUnityMasks", battle, view, true);
            Check(camera.RenderCalls.Count == 6 && r.UnityRoofMaskDraws == -1, "separate renderer passes available");

            SystemInfo.RGFloatBlendSupported = false;
            var fallback = Fixture();
            Invoke(fallback, "ReleaseUnityMask");
            Check(
                (bool)Invoke(fallback, "EnsureUnityMaskTargets", 100, 100) && !fallback.UnityPackedMasks,
                "unsupported RG32F blending selects separate targets"
            );
            Get<IList>(fallback, "objectDraws").Add(extra);
            Set(fallback, "geometrySerial", 42);
            Invoke(fallback, "RenderCachedUnityMasks", battle, view, true);
            var allDraws = Get<CommandBuffer>(fallback, "unityObjectMaskCommands").Draws;
            var roofDraws = Get<CommandBuffer>(fallback, "unityRoofMaskCommands").Draws;
            Check(allDraws.Count == 2 && roofDraws.Count == 2, "two full-resolution legacy passes");
            int allIndices = 0,
                roofIndices = 0;
            for (int i = 0; i < allDraws.Count; i++)
            {
                var total = allDraws[i];
                var subset = roofDraws[i];
                Check(
                    ReferenceEquals(total.Mesh, subset.Mesh) && subset.Submesh == 1,
                    "fallback shares mixed vertices"
                );
                allIndices += total.Mesh.SubmeshIndices[total.Submesh].Length;
                roofIndices += subset.Mesh.SubmeshIndices[subset.Submesh].Length;
            }
            Check(allIndices == 27 && roofIndices == 18, "two targets retain distinct occlusion membership");
            Invoke(fallback, "ReleaseUnityMaskCommands");
            Invoke(fallback, "ReleaseUnityMask");
            Invoke(r, "ReleaseUnityMaskCommands");
            Invoke(r, "ReleaseUnityMask");
        }
        finally
        {
            Mesh.RecordData = false;
            SystemInfo.RGFloatBlendSupported = true;
        }
    }

    internal static int Run()
    {
        BuilderPreservesAttributes();
        CheckPackedMasksAndFallback();
        var r = Fixture();
        var battle = new BattleSimulation(221);
        battle.Fog.Enabled = false;
        Invoke(r, "RenderCachedUnityMasks", battle, new Rect(0, 0, 1, 1), true);
        Check(
            r.UnityMaskGeometryBuilds == 1 && r.UnityMaskMergedMeshes == 2,
            "both mask passes reuse the same two page meshes"
        );
        Check(
            r.UnityObjectMaskDraws == 2 && r.UnityRoofMaskDraws == 2,
            "three per-object material runs become two per mask"
        );
        var camera = Get<Camera>(r, "unityMaskCamera");
        Check(camera.RenderCalls.Count == 2, "object and roof masks each render once");
        Check(
            camera.RenderCalls[0].CullingMask == (1 << 30) && camera.RenderCalls[1].CullingMask == 0,
            "vehicle layer remains live only in object mask"
        );
        Check(
            camera.RenderCalls[0].Draws == 2 && camera.RenderCalls[1].Draws == 2 && camera.BufferLists.Count == 0,
            "merged commands attached for render then detached"
        );
        Invoke(r, "RenderCachedUnityMasks", battle, new Rect(.01f, 0, 1, 1), false);
        Check(
            r.UnityMaskGeometryBuilds == 1 && camera.RenderCalls.Count == 4,
            "camera pan rerenders masks without rebuilding vertices"
        );
        Invoke(r, "RenderCachedUnityMasks", battle, new Rect(.01f, 0, 1, 1), false);
        Check(
            r.UnityMaskGeometryBuilds == 1 && camera.RenderCalls.Count == 4,
            "stationary unchanged masks do no camera work"
        );
        Set(r, "geometrySerial", 2);
        Invoke(r, "RenderCachedUnityMasks", battle, new Rect(.01f, 0, 1, 1), false);
        Check(
            r.UnityMaskGeometryBuilds == 2 && camera.RenderCalls.Count == 6,
            "geometry revision invalidates merged mask meshes and pixels"
        );
        Set(r, "unityStaticMaterialRevision", 1);
        Invoke(r, "RenderCachedUnityMasks", battle, new Rect(.01f, 0, 1, 1), false);
        Check(
            r.UnityMaskGeometryBuilds == 2 && camera.RenderCalls.Count == 8,
            "source material replacement rerenders masks without rebuilding geometry"
        );
        Invoke(r, "ReleaseUnityMaskCommands");
        Check(
            r.UnityMaskMergedMeshes == 0 && Get<object>(r, "unityObjectMaskCommands") == null,
            "merged mask resources released"
        );
        Console.WriteLine(
            "PASS grouped MAX mask tests: " + checks + " geometry, pan, invalidation, live vehicle and resource checks."
        );
        return checks;
    }
}
