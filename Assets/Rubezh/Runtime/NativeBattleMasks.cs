#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System;
using SilentOrders.Core;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        // Only presentation data crosses this boundary. LOS/contact logic remains in Core.
        Material nativeMaskMaterial;
        RenderTexture nativeFogTarget, nativeNightTarget;
        Texture2D nativeKnowledge, nativeExplored, nativeDangerRecords, nativeLightRecords;
        Color[] nativeDangerData, nativeLightData;
        float[] nativeEmptyKnowledge;
        BattleSimulation nativeMaskBattle;
        float nativeKnowledgeRevision = -1, nativeFogTime = -1, nativeNightTime = -1;
        bool nativeFogHardcore;
        int nativeDangerCount, nativeNightSourceCount = -1;
        void EnsureNativeMaskMaterial()
        {
            if (nativeMaskMaterial != null) return;
            var shader = Resources.Load<Shader>("RubezhBattleMasks");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Native battle mask shader is unavailable: RubezhBattleMasks");
            nativeMaskMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        void ResetNativeMaskBattle()
        {
            if (nativeMaskBattle == battle) return;
            ReleaseNativeMasks();
            nativeMaskBattle = battle;
        }
        static RenderTexture NewMaskTarget(int width, int height)
        {
            var result = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            result.filterMode = FilterMode.Bilinear; result.wrapMode = TextureWrapMode.Clamp;
            result.Create(); return result;
        }
        static void EnsureMaskData(ref Texture2D texture, ref Color[] data, int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height) return;
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            // Four explicit point reads implement Core's zero-border interpolation exactly,
            // including on mobile GPUs without float-texture linear filtering.
            texture.filterMode = FilterMode.Point; texture.wrapMode = TextureWrapMode.Clamp;
            data = new Color[width * height];
        }
        static Texture2D NewKnowledgeTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        }
        static int RecordCapacity(int count)
        {
            int capacity = 1; while (capacity < count) capacity *= 2; return capacity;
        }
        void SetMaskProjection()
        {
            nativeMaskMaterial.SetVector("_Map", new Vector4(MapWidth, MapHeight, battle.Map.Columns, battle.Map.Rows));
        }
        void UpdateNativeFogMask()
        {
            ResetNativeMaskBattle(); EnsureNativeMaskMaterial();
            if (nativeFogTarget == null) nativeFogTarget = NewMaskTarget(FogWidth, FogHeight);
            if (nativeFogTime >= 0 && battle.Time >= nativeFogTime && battle.Time - nativeFogTime < .15f && nativeFogHardcore == hardcoreFog) return;
            if (nativeKnowledge != null && nativeKnowledgeRevision == battle.Fog.NextUpdate && nativeFogHardcore == hardcoreFog) { nativeFogTime = battle.Time; return; }
            nativeFogTime = battle.Time; nativeFogHardcore = hardcoreFog;
            var fog = battle.Fog;
            if (nativeKnowledge == null || nativeKnowledgeRevision != fog.NextUpdate)
            {
                int columns = battle.Map.Columns, rows = battle.Map.Rows;
                if (nativeKnowledge == null)
                {
                    nativeKnowledge = NewKnowledgeTexture(columns, rows);
                    nativeExplored = NewKnowledgeTexture(columns, rows);
                }
                if ((fog.Clear == null || fog.ExploredBlend == null) && nativeEmptyKnowledge == null)
                    nativeEmptyKnowledge = new float[columns * rows];
                // Upload Core's already-computed fields directly: no pixel raster and no packing loop.
                nativeKnowledge.SetPixelData(fog.Clear ?? nativeEmptyKnowledge, 0); nativeKnowledge.Apply(false, false);
                nativeExplored.SetPixelData(fog.ExploredBlend ?? nativeEmptyKnowledge, 0); nativeExplored.Apply(false, false);
                nativeDangerCount = fog.Danger.Count;
                if (nativeDangerRecords == null || nativeDangerRecords.width < nativeDangerCount)
                    EnsureMaskData(ref nativeDangerRecords, ref nativeDangerData, RecordCapacity(nativeDangerCount), 1);
                for (int i = 0; i < nativeDangerCount; i++)
                {
                    var patch = fog.Danger[i];
                    nativeDangerData[i] = new Color(patch.Position.X, patch.Position.Y, patch.Radius, patch.Strength);
                }
                nativeDangerRecords.SetPixels(nativeDangerData); nativeDangerRecords.Apply(false, false);
                nativeKnowledgeRevision = fog.NextUpdate;
            }
            SetMaskProjection();
            nativeMaskMaterial.SetTexture("_Knowledge", nativeKnowledge);
            nativeMaskMaterial.SetTexture("_Explored", nativeExplored);
            nativeMaskMaterial.SetTexture("_Records", nativeDangerRecords);
            nativeMaskMaterial.SetFloat("_RecordCount", nativeDangerCount);
            nativeMaskMaterial.SetFloat("_RecordWidth", nativeDangerRecords.width);
            nativeMaskMaterial.SetFloat("_Hardcore", hardcoreFog ? 1 : 0);
            UnityEngine.Graphics.Blit(null, nativeFogTarget, nativeMaskMaterial, 0);
        }
        void UpdateNativeNightMask()
        {
            ResetNativeMaskBattle(); EnsureNativeMaskMaterial();
            if (nativeNightTarget == null) nativeNightTarget = NewMaskTarget(LightWidth, LightHeight);
            if (nativeNightTime == battle.Time) return;
            nativeNightTime = battle.Time;
            // Capacity follows actual source count, never truncates a burst or explosion.
            int capacity = Math.Max(1, battle.Soldiers.Count + battle.Blasts.Count);
            if (nativeLightRecords == null || nativeLightRecords.width < capacity)
                EnsureMaskData(ref nativeLightRecords, ref nativeLightData, RecordCapacity(capacity), 1);
            int count = 0;
            foreach (var soldier in battle.Soldiers)
            {
                float age = battle.Time - soldier.LastFire;
                if (battle.Fog.CanDraw(soldier) && soldier.Deployed && soldier.LastFire > 0 && age >= 0 && age < .12f)
                    nativeLightData[count++] = new Color(soldier.Position.X, soldier.Position.Y, 2, 1 - age / .12f);
            }
            foreach (var blast in battle.Blasts)
            {
                float age = battle.Time - blast.Born;
                if (battle.Fog.Revealed(blast.Position) && age >= 0 && age < .55f && battle.Time < blast.Until)
                    nativeLightData[count++] = new Color(blast.Position.X, blast.Position.Y, 5, 1 - age / .55f);
            }
            if (count == 0 && nativeNightSourceCount == 0) return;
            nativeNightSourceCount = count;
            nativeLightRecords.SetPixels(nativeLightData); nativeLightRecords.Apply(false, false);
            SetMaskProjection();
            nativeMaskMaterial.SetTexture("_Records", nativeLightRecords);
            nativeMaskMaterial.SetFloat("_RecordCount", count);
            nativeMaskMaterial.SetFloat("_RecordWidth", nativeLightRecords.width);
            UnityEngine.Graphics.Blit(null, nativeNightTarget, nativeMaskMaterial, 1);
        }
        void ReleaseNativeMasks()
        {
            ReleaseNativeRain();
            foreach (var target in new[] { nativeFogTarget, nativeNightTarget }) if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            foreach (var texture in new[] { nativeKnowledge, nativeExplored, nativeDangerRecords, nativeLightRecords }) if (texture != null) UnityEngine.Object.Destroy(texture);
            if (nativeMaskMaterial != null) UnityEngine.Object.Destroy(nativeMaskMaterial);
            nativeFogTarget = nativeNightTarget = null;
            nativeKnowledge = nativeExplored = nativeDangerRecords = nativeLightRecords = null;
            nativeDangerData = nativeLightData = null; nativeEmptyKnowledge = null;
            nativeMaskMaterial = null; nativeMaskBattle = null;
            nativeKnowledgeRevision = nativeFogTime = nativeNightTime = -1; nativeNightSourceCount = -1;
        }
    }
}
#endif
