using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        Texture2D closureWoodTexture;
        int roofPage = -1;
        readonly System.Collections.Generic.List<V2> roofActors = new System.Collections.Generic.List<V2>();

        void ReleaseRoof()
        {
            ReleaseNativeRevealMask();
            if (closureWoodTexture != null)
            {
                Resources.UnloadAsset(closureWoodTexture);
                closureWoodTexture = null;
            }
            revealBattle = null;
            revealAt = -1;
            if (roofPage < 0)
                return;
            Resources.UnloadAsset(atlases[roofPage]);
            atlases[roofPage] = null;
            roofPage = -1;
        }

        public void RoofVisibility(BattleSimulation b)
        {
            if (roofPage < 0)
            {
                var texture = Resources.Load<Texture2D>("RoofSurface");
                if (texture == null)
                    throw new InvalidOperationException("RoofSurface missing");
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Repeat;
                var material = new Material(shader);
                material.SetTexture("_MainTex", texture);
                var wood = closureWoodTexture = Resources.Load<Texture2D>("ClosureWood");
                if (wood != null)
                {
                    wood.wrapMode = TextureWrapMode.Repeat;
                    wood.filterMode = FilterMode.Bilinear;
                }
                material.SetTexture("_ClosureWood", wood);
                material.SetTexture("_Occlusion", occlusion);
                material.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
                roofPage = atlases.Count;
                atlases.Add(texture);
                materials.Add(material);
            }
            materials[roofPage].SetTexture("_RoofOcclusion", nativeRoofDepth);
            SilentOrders.Core.RoofVisibility.Actors(b, roofActors);
            UpdateNativeRevealMask(b);
            materials[roofPage].SetTexture("_RevealMask", nativeRevealMask);
            materials[roofPage].SetVector("_RevealBounds", new Vector4(-8, -8, b.Map.Columns + 16, b.Map.Rows + 16));
        }

        BattleSimulation revealBattle;
        float revealAt = -1;
        public int RevealMaskBuilds;

        public void ClosureFace(
            V2 a,
            V2 b,
            V2 c,
            V2 d,
            V2 ua,
            V2 ub,
            V2 uc,
            V2 ud,
            float ha,
            float hb,
            float hc,
            float hd,
            int kind
        )
        {
            int start = depths.Count;
            RoofFace(a, b, c, d, ua, ub, uc, ud, 1);
            if (depths.Count < start + 4)
                return;
            depths[start] = new Vector2(ha, kind == 0 ? 4 : 5);
            depths[start + 1] = new Vector2(hb, kind == 0 ? 4 : 5);
            depths[start + 2] = new Vector2(hc, kind == 0 ? 4 : 5);
            depths[start + 3] = new Vector2(hd, kind == 0 ? 4 : 5);
        }

        public void RoofFace(V2 a, V2 b, V2 c, V2 d, V2 ua, V2 ub, V2 uc, V2 ud, float shade)
        {
            if (DisableRoofs && !NativeScene)
                return;
            int start = depths.Count;
            byte light = (byte)(255 * shade);
            Quad(
                a.X,
                a.Y,
                b.X,
                b.Y,
                c.X,
                c.Y,
                d.X,
                d.Y,
                0,
                0,
                1,
                1,
                new Color32(light, light, light, 255),
                0,
                true,
                roofPage
            );
            if (uvs.Count < start + 4)
                return;
            uvs[start] = new Vector2(ua.X, ua.Y);
            uvs[start + 1] = new Vector2(ub.X, ub.Y);
            uvs[start + 2] = new Vector2(uc.X, uc.Y);
            uvs[start + 3] = new Vector2(ud.X, ud.Y);
            for (int i = start; i < depths.Count; i++)
                depths[i] = new Vector2(0, 3);
        }

        public void RoofPatch(
            float x0,
            float y0,
            float x1,
            float y1,
            float x2,
            float y2,
            float x3,
            float y3,
            float u0,
            float v0,
            float u1,
            float v1
        )
        {
            if (DisableRoofs && !NativeScene)
                return;
            int start = depths.Count;
            Quad(x0, y0, x1, y1, x2, y2, x3, y3, u0, v0, u1, v1, new Color32(255, 255, 255, 255), 0, true, roofPage);
            for (int i = start; i < depths.Count; i++)
                depths[i] = new Vector2(0, 3);
        }
    }
}
