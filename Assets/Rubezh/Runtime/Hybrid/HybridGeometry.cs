using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort.Hybrid
{
    public enum HybridSurface { Ground, Road, Concrete, Brick, Wood, Metal, Roof, Glass }

    /// <summary>CPU mesh data for the isolated hybrid playground. UVs use world metres.</summary>
    public sealed class HybridMeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color32> Colors = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();
        public int TriangleCount { get { return Triangles.Count / 3; } }

        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, UV);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public void Append(HybridMeshData source, Vector3 offset, float yaw = 0)
        {
            int start = Vertices.Count;
            for (int i = 0; i < source.Vertices.Count; i++)
            {
                Vertices.Add(HybridGeometry.Add(HybridGeometry.Rotate(source.Vertices[i], yaw), offset));
                Normals.Add(HybridGeometry.Rotate(source.Normals[i], yaw));
                UV.Add(source.UV[i]); Colors.Add(source.Colors[i]);
            }
            for (int i = 0; i < source.Triangles.Count; i++) Triangles.Add(start + source.Triangles[i]);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
        {
            float w = HybridGeometry.Distance(a, b), h = HybridGeometry.Distance(b, c);
            Quad(a, b, c, d, color, new Vector2(0, 0), new Vector2(w, 0), new Vector2(w, h), new Vector2(0, h));
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Vector3 normal = HybridGeometry.Normal(a, b, c);
            Quad(a, b, c, d, color, normal, normal, normal, normal, ua, ub, uc, ud);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color,
            Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int start = Vertices.Count;
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
            Normals.Add(na); Normals.Add(nb); Normals.Add(nc); Normals.Add(nd);
            UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
            Colors.Add(color); Colors.Add(color); Colors.Add(color); Colors.Add(color);
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
            Triangles.Add(start); Triangles.Add(start + 2); Triangles.Add(start + 3);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color32 color)
        {
            int start = Vertices.Count;
            Vector3 normal = HybridGeometry.Normal(a, b, c);
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
            Normals.Add(normal); Normals.Add(normal); Normals.Add(normal);
            UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(HybridGeometry.Distance(a, b), 0));
            UV.Add(new Vector2(HybridGeometry.Distance(a, b) * .5f, HybridGeometry.Distance(a, c)));
            Colors.Add(color); Colors.Add(color); Colors.Add(color);
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
        }
    }

    /// <summary>Primitive mesh writers; no individual GameObjects or colliders are created.</summary>
    public static class HybridGeometry
    {
        public static readonly Color32 White = new Color32(255, 255, 255, 255);
        public static Vector3 Add(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 Sub(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 Scale(Vector3 a, float s) { return new Vector3(a.x * s, a.y * s, a.z * s); }
        public static float Distance(Vector3 a, Vector3 b) { Vector3 d = Sub(a, b); return (float)Math.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z); }
        public static Vector3 Normal(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 u = Sub(b, a), v = Sub(c, a);
            var n = new Vector3(u.y * v.z - u.z * v.y, u.z * v.x - u.x * v.z, u.x * v.y - u.y * v.x);
            float length = (float)Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
            if (length < .000001f) throw new ArgumentException("Degenerate hybrid triangle");
            return Scale(n, 1 / length);
        }
        public static Vector3 Rotate(Vector3 p, float yaw)
        {
            float r = yaw * (float)Math.PI / 180f, c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vector3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
        }

        public static void Box(HybridMeshData target, Vector3 centre, Vector3 size, Color32 color, float yaw = 0)
        {
            float x = size.x * .5f, y = size.y * .5f, z = size.z * .5f;
            Func<float, float, float, Vector3> p = (a, b, c) => Add(centre, Rotate(new Vector3(a, b, c), yaw));
            target.Quad(p(-x, -y, z), p(x, -y, z), p(x, y, z), p(-x, y, z), color);
            target.Quad(p(x, -y, -z), p(-x, -y, -z), p(-x, y, -z), p(x, y, -z), color);
            target.Quad(p(x, -y, z), p(x, -y, -z), p(x, y, -z), p(x, y, z), color);
            target.Quad(p(-x, -y, -z), p(-x, -y, z), p(-x, y, z), p(-x, y, -z), color);
            target.Quad(p(-x, y, z), p(x, y, z), p(x, y, -z), p(-x, y, -z), color);
            target.Quad(p(-x, -y, -z), p(x, -y, -z), p(x, -y, z), p(-x, -y, z), color);
        }

        /// <summary>Octagonal footprint and sloping top/bottom bevels give real edge silhouette.</summary>
        public static void BevelBox(HybridMeshData target, Vector3 centre, Vector3 size, float bevel, Color32 color, float yaw = 0)
        {
            float x = size.x / 2, z = size.z / 2, y = size.y / 2;
            float b = Math.Min(bevel, Math.Min(y, Math.Min(x, z)) * .45f);
            Vector3[][] rings = new Vector3[4][];
            float[] heights = { -y, -y + b, y - b, y };
            for (int r = 0; r < 4; r++)
            {
                float inset = (r == 0 || r == 3) ? b * .5f : 0;
                float xx = x - inset, zz = z - inset, cut = b;
                float[] px = { -xx + cut, xx - cut, xx, xx, xx - cut, -xx + cut, -xx, -xx };
                float[] pz = { zz, zz, zz - cut, -zz + cut, -zz, -zz, -zz + cut, zz - cut };
                rings[r] = new Vector3[8];
                for (int i = 0; i < 8; i++) rings[r][i] = Add(centre, Rotate(new Vector3(px[i], heights[r], pz[i]), yaw));
            }
            for (int r = 0; r < 3; r++) for (int i = 0; i < 8; i++)
            { int j = (i + 1) % 8; target.Quad(rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i], color); }
            Vector3 top = Add(centre, new Vector3(0, y, 0)), bottom = Add(centre, new Vector3(0, -y, 0));
            for (int i = 0; i < 8; i++)
            { int j = (i + 1) % 8; target.Triangle(top, rings[3][i], rings[3][j], color); target.Triangle(bottom, rings[0][j], rings[0][i], color); }
        }

        public static void Cylinder(HybridMeshData target, Vector3 bottom, float radius, float height, Color32 color, int sides = 20)
        {
            for (int i = 0; i < sides; i++)
            {
                float a = (float)(2 * Math.PI * i / sides), b = (float)(2 * Math.PI * (i + 1) / sides);
                var na = new Vector3((float)Math.Cos(a), 0, (float)Math.Sin(a));
                var nb = new Vector3((float)Math.Cos(b), 0, (float)Math.Sin(b));
                Vector3 p = Add(bottom, Scale(na, radius)), q = Add(bottom, Scale(nb, radius));
                Vector3 pt = Add(p, new Vector3(0, height, 0)), qt = Add(q, new Vector3(0, height, 0));
                float u0 = a * radius, u1 = b * radius;
                target.Quad(q, p, pt, qt, color, nb, na, na, nb, new Vector2(u1, 0), new Vector2(u0, 0), new Vector2(u0, height), new Vector2(u1, height));
                target.Triangle(Add(bottom, new Vector3(0, height, 0)), qt, pt, color);
                target.Triangle(bottom, p, q, color);
            }
        }

        public static void Roof(HybridMeshData target, float x0, float x1, float z0, float z1, float eave, float ridge, Color32 color)
        {
            float mid = (z0 + z1) * .5f, thick = .11f;
            var a = new Vector3(x0, eave, z0); var b = new Vector3(x1, eave, z0);
            var c = new Vector3(x1, ridge, mid); var d = new Vector3(x0, ridge, mid);
            var e = new Vector3(x0, eave, z1); var f = new Vector3(x1, eave, z1);
            target.Quad(b, a, d, c, color); target.Quad(e, f, c, d, color);
            var down = new Vector3(0, -thick, 0);
            target.Quad(Add(a, down), Add(b, down), Add(c, down), Add(d, down), color);
            target.Quad(Add(f, down), Add(e, down), Add(d, down), Add(c, down), color);
            target.Quad(a, b, Add(b, down), Add(a, down), color);
            target.Quad(f, e, Add(e, down), Add(f, down), color);
            target.Quad(e, d, Add(d, down), Add(e, down), color);
            target.Quad(d, a, Add(a, down), Add(d, down), color);
            target.Quad(b, c, Add(c, down), Add(b, down), color);
            target.Quad(c, f, Add(f, down), Add(c, down), color);
        }
    }

    /// <summary>
    /// One authoritative occupancy grid for the demonstration wall. Front/back rectangles
    /// are greedily merged; boundaries alone receive thickness faces. A hole is a real
    /// opening with reveals, not an alpha-mask illusion. This state is isolated from combat.
    /// </summary>
    public sealed class HybridWallState
    {
        public readonly int Columns, Rows;
        public readonly float CellSize, Thickness;
        public readonly bool[] Solid;
        public int Revision { get; private set; }
        public HybridWallState(int columns = 16, int rows = 6, float cellSize = .5f, float thickness = .32f)
        {
            if (columns < 1 || rows < 1 || cellSize <= 0 || thickness <= 0) throw new ArgumentOutOfRangeException("wall dimensions");
            Columns = columns; Rows = rows; CellSize = cellSize; Thickness = thickness;
            Solid = new bool[Columns * Rows]; Reset();
        }
        public void Reset() { for (int i = 0; i < Solid.Length; i++) Solid[i] = true; Revision++; }
        public bool IsSolid(int x, int y) { return x >= 0 && x < Columns && y >= 0 && y < Rows && Solid[y * Columns + x]; }
        public int Damage(float localX, float localY, float radius)
        {
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius) || float.IsNaN(localX) || float.IsNaN(localY) || float.IsInfinity(localX) || float.IsInfinity(localY)) return 0;
            int changed = 0;
            for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++)
            {
                float dx = (x + .5f) * CellSize - localX, dy = (y + .5f) * CellSize - localY;
                if (Solid[y * Columns + x] && dx * dx + dy * dy <= radius * radius)
                { Solid[y * Columns + x] = false; changed++; }
            }
            if (changed > 0) Revision++;
            return changed;
        }
        public void Open(float x0, float y0, float x1, float y1)
        {
            bool changed = false;
            for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++)
            {
                float px = (x + .5f) * CellSize, py = (y + .5f) * CellSize;
                if (px >= x0 && px < x1 && py >= y0 && py < y1 && Solid[y * Columns + x])
                { Solid[y * Columns + x] = false; changed = true; }
            }
            if (changed) Revision++;
        }
        public HybridMeshData BuildMesh() { return BuildMesh(HybridGeometry.White); }
        public HybridMeshData BuildMesh(Color32 color)
        {
            var mesh = new HybridMeshData(); bool[] done = new bool[Solid.Length];
            float half = Thickness * .5f;
            for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++)
            {
                if (!IsSolid(x, y) || done[y * Columns + x]) continue;
                int width = 1; while (x + width < Columns && IsSolid(x + width, y) && !done[y * Columns + x + width]) width++;
                int height = 1; bool extend = true;
                while (y + height < Rows && extend)
                {
                    for (int a = 0; a < width; a++) if (!IsSolid(x + a, y + height) || done[(y + height) * Columns + x + a]) { extend = false; break; }
                    if (extend) height++;
                }
                for (int b = 0; b < height; b++) for (int a = 0; a < width; a++) done[(y + b) * Columns + x + a] = true;
                float x0 = x * CellSize, x1 = (x + width) * CellSize, y0 = y * CellSize, y1 = (y + height) * CellSize;
                mesh.Quad(new Vector3(x0, y0, half), new Vector3(x1, y0, half), new Vector3(x1, y1, half), new Vector3(x0, y1, half), color,
                    new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));
                mesh.Quad(new Vector3(x1, y0, -half), new Vector3(x0, y0, -half), new Vector3(x0, y1, -half), new Vector3(x1, y1, -half), color,
                    new Vector2(-x1, y0), new Vector2(-x0, y0), new Vector2(-x0, y1), new Vector2(-x1, y1));
            }
            // Merge all exposed vertical boundary runs. Interior faces are never emitted.
            for (int x = 0; x < Columns; x++) for (int side = -1; side <= 1; side += 2)
            {
                int y = 0;
                while (y < Rows)
                {
                    if (!IsSolid(x, y) || IsSolid(x + side, y)) { y++; continue; }
                    int first = y++; while (y < Rows && IsSolid(x, y) && !IsSolid(x + side, y)) y++;
                    float px = (x + (side > 0 ? 1 : 0)) * CellSize, y0 = first * CellSize, y1 = y * CellSize;
                    if (side > 0) mesh.Quad(new Vector3(px, y0, half), new Vector3(px, y0, -half), new Vector3(px, y1, -half), new Vector3(px, y1, half), color);
                    else mesh.Quad(new Vector3(px, y0, -half), new Vector3(px, y0, half), new Vector3(px, y1, half), new Vector3(px, y1, -half), color);
                }
            }
            for (int y = 0; y < Rows; y++) for (int side = -1; side <= 1; side += 2)
            {
                int x = 0;
                while (x < Columns)
                {
                    if (!IsSolid(x, y) || IsSolid(x, y + side)) { x++; continue; }
                    int first = x++; while (x < Columns && IsSolid(x, y) && !IsSolid(x, y + side)) x++;
                    float py = (y + (side > 0 ? 1 : 0)) * CellSize, x0 = first * CellSize, x1 = x * CellSize;
                    if (side > 0) mesh.Quad(new Vector3(x0, py, half), new Vector3(x1, py, half), new Vector3(x1, py, -half), new Vector3(x0, py, -half), color);
                    else mesh.Quad(new Vector3(x0, py, -half), new Vector3(x1, py, -half), new Vector3(x1, py, half), new Vector3(x0, py, half), color);
                }
            }
            return mesh;
        }
    }
}
