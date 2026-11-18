using UnityEngine;

namespace Rubezh.UnityPort
{
    // Owns only loading-screen GPU resources; no per-frame allocations or texture uploads.
    public sealed class RubezhLoadingFx : MonoBehaviour
    {
        Texture2D artwork, cold;
        Material material;
        RenderTexture frame;
        int renderedFrame = -1;
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int ClockId = Shader.PropertyToID("_Clock");

        public bool Initialize()
        {
            if (artwork != null && cold != null) return true;
            artwork = Resources.Load<Texture2D>("Loading/fire_top");
            cold = Resources.Load<Texture2D>("Loading/cold_top");
            if (artwork == null || cold == null) return false;
            var shader = Resources.Load<Shader>("Loading/FireReveal");
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture(Shader.PropertyToID("_ColdTex"), cold);
                frame = new RenderTexture(artwork.width, artwork.height, 0, RenderTextureFormat.ARGB32);
                frame.name = "Rubezh Loading Fire";
                frame.filterMode = FilterMode.Bilinear;
                frame.wrapMode = TextureWrapMode.Clamp;
                frame.Create();
            }
            return true;
        }

        public Texture Render(float progress, float clock)
        {
            if (!Initialize()) return null;
            if (material == null || frame == null) return progress >= 1 ? artwork : cold;
            if (!frame.IsCreated()) { frame.Create(); renderedFrame = -1; }
            if (renderedFrame != Time.frameCount)
            {
                material.SetFloat(ProgressId, Mathf.Clamp01(progress));
                material.SetFloat(ClockId, clock);
                var previous = RenderTexture.active;
                try { UnityEngine.Graphics.Blit(artwork, frame, material); }
                finally { RenderTexture.active = previous; }
                renderedFrame = Time.frameCount;
            }
            return frame;
        }

        public void Release()
        {
            if (frame != null) { frame.Release(); Destroy(frame); }
            if (material != null) Destroy(material);
            if (artwork != null) Resources.UnloadAsset(artwork);
            if (cold != null) Resources.UnloadAsset(cold);
            frame=null;material=null;artwork=cold=null;renderedFrame=-1;
        }

        void OnDestroy(){Release();}
    }
}
