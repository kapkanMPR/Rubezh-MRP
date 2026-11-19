using System;
using System.Collections.Generic;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        struct GroundMaterialKey : IEquatable<GroundMaterialKey>
        {
            public int A,
                B,
                C,
                D;

            public bool Equals(GroundMaterialKey other) => A == other.A && B == other.B && C == other.C && D == other.D;

            public override bool Equals(object other) => other is GroundMaterialKey && Equals((GroundMaterialKey)other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((A * 397 ^ B) * 397 ^ C) * 397 ^ D;
                }
            }
        }

        const int GroundPaletteLimit = 256;
        readonly Dictionary<GroundMaterialKey, int> groundMaterialPages = new Dictionary<GroundMaterialKey, int>();
        readonly Dictionary<int, int[]> groundMaterialPalettes = new Dictionary<int, int[]>();
        public int GroundPaletteMaterials => groundMaterialPages.Count;
        public long GroundPaletteRunsSaved { get; private set; }
        public long SceneryPaletteRunsSaved { get; private set; }

        bool OrdinaryGroundPage(int page)
        {
            if (page < 0 || page >= atlases.Count || atlases[page] == null)
                return false;
            if (page == 0)
                return true; // Native white primitive texture.
            int original;
            return nativeTexturePages.TryGetValue(atlases[page], out original) && original == page;
        }

        int GroundMaterialPage(List<int> palette)
        {
            palette.Sort();
            var key = new GroundMaterialKey
            {
                A = palette[0],
                B = palette.Count > 1 ? palette[1] : -1,
                C = palette.Count > 2 ? palette[2] : -1,
                D = palette.Count > 3 ? palette[3] : -1,
            };
            int page;
            if (groundMaterialPages.TryGetValue(key, out page))
                return page;
            // Map resources live for the battle. Bound palette materials as well;
            // unusual custom maps fall back to ordinary runs after this budget.
            if (groundMaterialPages.Count >= GroundPaletteLimit)
                return -1;
            var material = new Material(shader);
            material.SetFloat("_GroundMultiTex", 1);
            material.SetTexture("_MainTex", atlases[palette[0]]);
            material.SetTexture("_GroundTex1", atlases[palette[Math.Min(1, palette.Count - 1)]]);
            material.SetTexture("_GroundTex2", atlases[palette[Math.Min(2, palette.Count - 1)]]);
            material.SetTexture("_GroundTex3", atlases[palette[Math.Min(3, palette.Count - 1)]]);
            material.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
            page = materials.Count;
            // This slot owns a material only; source textures remain Addressables-owned.
            atlases.Add(null);
            materials.Add(material);
            groundMaterialPages.Add(key, page);
            groundMaterialPalettes.Add(page, palette.ToArray());
            return page;
        }

        // Four existing atlas textures are bound together. The additional per-vertex
        // value chooses the original sampler; UVs, filters, precision, triangle order
        // and texture resolution remain unchanged. No texture copy or rescale occurs.
        List<Vector2> CompactGroundMaterialRuns()
        {
            return CompactTexturePaletteRuns(false);
        }

        List<Vector2> CompactStaticMaterialRuns() => CompactTexturePaletteRuns(true);

        List<Vector2> CompactTexturePaletteRuns(bool scenery)
        {
            if (!nativeAtlasMode || !UnityOwnedScene || runs.Items.Count < 2)
                return null;
            var sourceRuns = runs.Items;
            var eligible = new bool[sourceRuns.Count];
            bool canMerge = false;
            for (int r = 0; r < sourceRuns.Count; r++)
            {
                var run = sourceRuns[r];
                if (!OrdinaryGroundPage(run.Page))
                    continue;
                eligible[r] = true;
                for (int n = run.Start; n < run.Start + run.Count; n++)
                {
                    // Procedural depth, roof reveal and patterned shadows use additional
                    // material inputs. Keep those draws on their original material.
                    float mode = depths[indices[n]].y;
                    if (mode == 0 || mode == -1 || (scenery && mode == 1))
                        continue;
                    eligible[r] = false;
                    break;
                }
                canMerge |= r > 0 && eligible[r - 1] && eligible[r];
            }
            if (!canMerge)
                return null;

            // A shared vertex cannot select different samplers in different runs.
            // Emit the original draws if a future producer welds across materials.
            var owner = new int[vertices.Count];
            for (int r = 0; r < sourceRuns.Count; r++)
            {
                var run = sourceRuns[r];
                for (int n = run.Start; n < run.Start + run.Count; n++)
                {
                    int vertex = indices[n];
                    if (owner[vertex] != 0 && owner[vertex] != r + 1)
                        return null;
                    owner[vertex] = r + 1;
                }
            }
            var original = sourceRuns.ToArray();
            var slots = new List<Vector2>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
                slots.Add(new Vector2(0, 0));
            runs.Clear();
            var palette = new List<int>(4);
            for (int first = 0; first < original.Length; )
            {
                palette.Clear();
                int end = first;
                while (end < original.Length && eligible[end])
                {
                    int source = original[end].Page;
                    if (!palette.Contains(source))
                    {
                        if (palette.Count == 4)
                            break;
                        palette.Add(source);
                    }
                    end++;
                }
                int material = end - first > 1 ? GroundMaterialPage(palette) : -1;
                if (end == first)
                    end = first + 1;
                for (int r = first; r < end; r++)
                {
                    var run = original[r];
                    int slot = material >= 0 ? palette.IndexOf(run.Page) : 0;
                    for (int n = run.Start; n < run.Start + run.Count; n++)
                        slots[indices[n]] = new Vector2(slot, 0);
                    for (int n = 0; n < run.Count; n += 6)
                        runs.AddQuad(material >= 0 ? material : run.Page);
                }
                first = end;
            }
            if (scenery)
                SceneryPaletteRunsSaved += original.Length - runs.Items.Count;
            else
                GroundPaletteRunsSaved += original.Length - runs.Items.Count;
            return original.Length == runs.Items.Count ? null : slots;
        }
    }
}
