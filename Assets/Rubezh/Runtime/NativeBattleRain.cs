#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        Mesh nativeRainMesh;
        RenderTexture nativeRainTarget;
        UnityEngine.Rendering.CommandBuffer nativeRainCommands;
        float nativeRainTime = -1, nativeRainWidth, nativeRainHeight;
        void UpdateNativeRain()
        {
            EnsureNativeMaskMaterial(); EnsureRainStreak();
            int width = (int)Math.Ceiling(W), height = (int)Math.Ceiling(H);
            if (nativeRainTarget == null || nativeRainTarget.width != width || nativeRainTarget.height != height)
            {
                ReleaseNativeRain();
                nativeRainTarget = NewMaskTarget(width, height);
                nativeRainMesh = new Mesh { name = "Rain: immutable 96 GPU animated streaks" };
                var positions = new Vector3[384]; var uv = new Vector2[384]; var ids = new Vector2[384]; var indices = new int[576];
                for (int i = 0; i < 96; i++)
                {
                    int v = i * 4, t = i * 6;
                    positions[v] = new Vector3(0, 0, 0); positions[v+1] = new Vector3(1, 0, 0); positions[v+2] = new Vector3(1, 1, 0); positions[v+3] = new Vector3(0, 1, 0);
                    uv[v] = new Vector2(0, 1); uv[v+1] = new Vector2(1, 1); uv[v+2] = new Vector2(1, 0); uv[v+3] = new Vector2(0, 0);
                    for (int k = 0; k < 4; k++) ids[v+k] = new Vector2(i, 0);
                    indices[t] = v; indices[t+1] = v+1; indices[t+2] = v+2; indices[t+3] = v; indices[t+4] = v+2; indices[t+5] = v+3;
                }
                nativeRainMesh.vertices = positions; nativeRainMesh.uv = uv; nativeRainMesh.uv2 = ids; nativeRainMesh.triangles = indices; nativeRainMesh.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
                nativeRainMesh.UploadMeshData(true);
#endif
                nativeRainCommands = new UnityEngine.Rendering.CommandBuffer { name = "GPU screen rain" };
            }
            if (nativeRainTime == battle.Time && nativeRainWidth == W && nativeRainHeight == H) return;
            nativeRainTime = battle.Time; nativeRainWidth = W; nativeRainHeight = H;
            nativeMaskMaterial.SetTexture("_RainStreak", rainStreak);
            nativeMaskMaterial.SetFloat("_RainTime", battle.Time);
            nativeMaskMaterial.SetVector("_RainScreen", new Vector4(W, H, 0, 0));
            nativeMaskMaterial.SetMatrix("_RainProjection", GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, W, H, 0, -1, 1), true));
            nativeRainCommands.Clear(); nativeRainCommands.SetRenderTarget(nativeRainTarget);
            nativeRainCommands.SetViewport(new Rect(0, 0, width, height));
            nativeRainCommands.DisableScissorRect();
            // All streaks share one tint. Store straight RGB once; blend only alpha,
            // so UI Toolkit can composite this texture without an unpremultiply target.
            nativeRainCommands.ClearRenderTarget(false, true, new Color(.68f, .79f, .88f, 0));
            nativeRainCommands.DrawMesh(nativeRainMesh, Matrix4x4.identity, nativeMaskMaterial, 0, 3);
            UnityEngine.Graphics.ExecuteCommandBuffer(nativeRainCommands);
        }
        void ReleaseNativeRain()
        {
            if (nativeRainTarget != null) { nativeRainTarget.Release(); UnityEngine.Object.Destroy(nativeRainTarget); }
            if (nativeRainMesh != null) UnityEngine.Object.Destroy(nativeRainMesh);
            if (nativeRainCommands != null) nativeRainCommands.Release();
            nativeRainTarget = null; nativeRainMesh = null; nativeRainCommands = null; nativeRainTime = -1;
        }
    }
}
#endif
