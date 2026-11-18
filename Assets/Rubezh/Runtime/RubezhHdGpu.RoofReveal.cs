using System;
using SilentOrders.Core;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        RenderTexture nativeRevealMask;
        Material nativeRevealMaterial;
        Mesh nativeRevealQuad;
        UnityEngine.Rendering.CommandBuffer nativeRevealCommands;
        Color[] nativeRevealActorData;
        int nativeRevealActorCount = -1;
        void UpdateNativeRevealMask(BattleSimulation battle)
        {
            int width = (battle.Map.Columns + 16) * 4, height = (battle.Map.Rows + 16) * 4;
            if (nativeRevealMask == null || nativeRevealMask.width != width || nativeRevealMask.height != height)
            {
                ReleaseNativeRevealMask();
                var maskShader = Resources.Load<Shader>("RubezhBattleMasks");
                if (maskShader == null || !maskShader.isSupported) throw new InvalidOperationException("Native roof reveal shader unavailable");
                nativeRevealMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
                var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
                nativeRevealMask = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear);
                nativeRevealMask.filterMode = FilterMode.Bilinear; nativeRevealMask.wrapMode = TextureWrapMode.Clamp;
                nativeRevealMask.Create(); revealAt = -1;
                nativeRevealQuad = new Mesh { name = "Shared roof reveal stamp" };
                nativeRevealQuad.vertices = new[] { new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0) };
                nativeRevealQuad.uv = new[] { new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1) };
                nativeRevealQuad.triangles = new[] { 0,1,2,0,2,3 }; nativeRevealQuad.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
                nativeRevealQuad.UploadMeshData(true);
#endif
                nativeRevealCommands = new UnityEngine.Rendering.CommandBuffer { name = "Roof reveal bounded GPU stamps" };
            }
            if (revealBattle == battle && revealAt >= 0 && battle.Time >= revealAt && battle.Time - revealAt < .1f) return;
            bool unchanged = revealBattle == battle && nativeRevealActorCount == roofActors.Count;
            if (unchanged) for (int i = 0; i < roofActors.Count; i++)
                if (nativeRevealActorData[i].r != roofActors[i].X || nativeRevealActorData[i].g != roofActors[i].Y) { unchanged = false; break; }
            if (unchanged) { revealAt = battle.Time; return; }
            if (nativeRevealActorData == null || nativeRevealActorData.Length < roofActors.Count)
                nativeRevealActorData = new Color[Math.Max(1, roofActors.Count)];
            var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0, battle.Map.Columns + 16, 0, battle.Map.Rows + 16, -1, 1), true);
            nativeRevealCommands.Clear();
            nativeRevealCommands.SetRenderTarget(nativeRevealMask);
            nativeRevealCommands.SetViewport(new Rect(0, 0, width, height));
            nativeRevealCommands.DisableScissorRect();
            nativeRevealCommands.ClearRenderTarget(false, true, Color.white);
            for (int i = 0; i < roofActors.Count; i++)
            {
                var actor = roofActors[i];
                nativeRevealActorData[i] = new Color(actor.X, actor.Y, 0, 0);
                var stamp = Matrix4x4.TRS(new Vector3(actor.X + 8, actor.Y + 8, 0), Quaternion.identity, new Vector3(10, 10, 1));
                // Radius five at four texels per cell: <=1600 fragments per actor,
                // regardless of map dimensions. Min blending composes overlapping stamps.
                nativeRevealCommands.DrawMesh(nativeRevealQuad, projection * stamp, nativeRevealMaterial, 0, 2);
            }
            UnityEngine.Graphics.ExecuteCommandBuffer(nativeRevealCommands);
            nativeRevealActorCount = roofActors.Count;
            revealBattle = battle; revealAt = battle.Time; RevealMaskBuilds++;
        }
        void ReleaseNativeRevealMask()
        {
            if (nativeRevealMask != null) { nativeRevealMask.Release(); UnityEngine.Object.Destroy(nativeRevealMask); }
            if (nativeRevealMaterial != null) UnityEngine.Object.Destroy(nativeRevealMaterial);
            if (nativeRevealQuad != null) UnityEngine.Object.Destroy(nativeRevealQuad);
            if (nativeRevealCommands != null) nativeRevealCommands.Release();
            nativeRevealMask = null; nativeRevealMaterial = null; nativeRevealQuad = null; nativeRevealCommands = null; nativeRevealActorData = null; nativeRevealActorCount = -1;
        }
    }
}
