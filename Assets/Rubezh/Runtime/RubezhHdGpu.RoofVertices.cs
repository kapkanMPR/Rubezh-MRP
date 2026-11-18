using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public int RoofVerticesBefore { get; private set; }
        public int RoofVerticesAfter { get; private set; }

        // All shader inputs participate in equality. Never weld merely nearby positions:
        // a UV seam, shade change, reveal coordinate or closure height must remain distinct.
        struct RoofVertexKey : IEquatable<RoofVertexKey>
        {
            public Vector3 Position;
            public Vector2 UV, Depth, Clip;
            public Color32 Color;
            public bool Equals(RoofVertexKey b) =>
                Position.x == b.Position.x && Position.y == b.Position.y && Position.z == b.Position.z &&
                UV.x == b.UV.x && UV.y == b.UV.y && Depth.x == b.Depth.x && Depth.y == b.Depth.y &&
                Clip.x == b.Clip.x && Clip.y == b.Clip.y &&
                Color.r == b.Color.r && Color.g == b.Color.g && Color.b == b.Color.b && Color.a == b.Color.a;
            public override bool Equals(object value) => value is RoofVertexKey && Equals((RoofVertexKey)value);
            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Position.x.GetHashCode(); h = h * 397 ^ Position.y.GetHashCode(); h = h * 397 ^ Position.z.GetHashCode();
                    h = h * 397 ^ UV.x.GetHashCode(); h = h * 397 ^ UV.y.GetHashCode();
                    h = h * 397 ^ Depth.x.GetHashCode(); h = h * 397 ^ Depth.y.GetHashCode();
                    h = h * 397 ^ Clip.x.GetHashCode(); h = h * 397 ^ Clip.y.GetHashCode();
                    return h * 397 ^ (Color.r | Color.g << 8 | Color.b << 16 | Color.a << 24);
                }
            }
        }

        void CompactRoofVertices()
        {
            RoofVerticesBefore = vertices.Count;
            var unique = new Dictionary<RoofVertexKey, int>(vertices.Count);
            var remap = new int[vertices.Count];
            int count = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                var key = new RoofVertexKey { Position = vertices[i], UV = uvs[i], Depth = depths[i], Clip = clips[i], Color = colors[i] };
                int target;
                if (!unique.TryGetValue(key, out target))
                {
                    target = count++;
                    unique.Add(key, target);
                    vertices[target] = key.Position; uvs[target] = key.UV; depths[target] = key.Depth;
                    clips[target] = key.Clip; colors[target] = key.Color;
                }
                remap[i] = target;
            }
            // Remap in place: triangle sequence, winding and material runs stay identical.
            for (int i = 0; i < indices.Count; i++) indices[i] = remap[indices[i]];
            int removed = vertices.Count - count;
            vertices.RemoveRange(count, removed); uvs.RemoveRange(count, removed); depths.RemoveRange(count, removed);
            clips.RemoveRange(count, removed); colors.RemoveRange(count, removed);
            RoofVerticesAfter = count;
        }
    }
}
