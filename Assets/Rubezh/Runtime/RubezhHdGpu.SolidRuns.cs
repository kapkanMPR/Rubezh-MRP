using System;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        // A solid primitive has no texture/material-specific inputs in the native
        // shader. Rebind complete solid runs to an adjacent textured run without
        // moving any triangle. This also handles the initial page-zero primitive
        // in independently captured objects when their geometry is concatenated.
        void CompactSolidMaterialRuns()
        {
            if (!nativeAtlasMode || !UnityOwnedScene || runs.Items.Count < 2) return;
            var original = runs.Items.ToArray();
            var solid = new bool[original.Length];
            int firstTextured = -1;
            for (int r = 0; r < original.Length; r++)
            {
                var run = original[r];
                bool flat = run.Count > 0;
                for (int n = run.Start; flat && n < run.Start + run.Count; n++)
                {
                    int index = indices[n];
                    var uv = uvs[index]; var depth = depths[index];
                    flat = uv.x < -.5f && uv.y < -.5f && depth.y >= -1.5f && depth.y < 1.5f;
                }
                solid[r] = flat;
                if (!flat && firstTextured < 0) firstTextured = r;
            }
            int page = firstTextured < 0 ? original[0].Page : original[firstTextured].Page;
            runs.Clear();
            for (int r = 0; r < original.Length; r++)
            {
                var run = original[r];
                if (!solid[r]) page = run.Page;
                for (int n = 0; n < run.Count; n += 6) runs.AddQuad(page);
            }
        }
    }
}
