using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort.Hybrid
{
    public sealed class HybridPart
    {
        public string Name;
        public HybridSurface Surface;
        public bool Roof;
        public readonly HybridMeshData Geometry = new HybridMeshData();
    }

    /// <summary>
    /// A deterministic 36 x 24 metre test location. Meshes are grouped by material and
    /// 12 metre spatial chunk; camera motion never regenerates any world geometry.
    /// </summary>
    public static class HybridLayout
    {
        public const int Width = 36, Depth = 24;
        public static Vector3 DemoWallPosition { get { return new Vector3(17, 0, 16.5f); } }
        static Color32 Tint(byte r, byte g, byte b) { return new Color32(r, g, b, 255); }

        public static List<HybridPart> Build()
        {
            var builder = new Builder(); builder.Build(); return builder.Parts;
        }

        sealed class Builder
        {
            public readonly List<HybridPart> Parts = new List<HybridPart>();
            readonly Dictionary<string, HybridPart> groups = new Dictionary<string, HybridPart>();
            HybridMeshData Group(HybridSurface surface, float x, float z, bool roof = false)
            {
                int cx = Math.Max(0, Math.Min(2, (int)(x / 12))), cz = Math.Max(0, Math.Min(1, (int)(z / 12)));
                string key = (roof ? "roof-" : "world-") + surface + "-" + cx + "-" + cz;
                HybridPart part;
                if (!groups.TryGetValue(key, out part))
                { part = new HybridPart { Name = key, Surface = surface, Roof = roof }; groups.Add(key, part); Parts.Add(part); }
                return part.Geometry;
            }
            void Box(HybridSurface s, float x, float y, float z, float w, float h, float d, Color32 color, float yaw = 0, bool roof = false)
            { HybridGeometry.Box(Group(s, x, z, roof), new Vector3(x, y, z), new Vector3(w, h, d), color, yaw); }

            public void Build()
            {
                for (int z = 0; z < 2; z++) for (int x = 0; x < 3; x++)
                {
                    // Continuous UVs keep both ground and road patterns seamless across chunks.
                    Flat(HybridSurface.Ground, x * 12, z * 12, x * 12 + 12, z * 12 + 12, -.035f, HybridGeometry.White);
                    if (z == 0)
                    {
                        Flat(HybridSurface.Road, x * 12, 10.95f, x * 12 + 12, 13.55f, .012f, Tint(240, 238, 228));
                        Flat(HybridSurface.Concrete, x * 12, 10.65f, x * 12 + 12, 10.95f, .008f, Tint(174, 169, 153));
                        Flat(HybridSurface.Concrete, x * 12, 13.55f, x * 12 + 12, 13.85f, .008f, Tint(174, 169, 153));
                    }
                }
                Building(23, 3, 9, 7, 3, false);
                Building(5, 14, 7, 6, 2.6f, true);

                // Open-air material samples in the south-west yard.
                Crate(3.1f, 3.4f, 0, 1.15f, 1.1f, 1, -7);
                Crate(4.45f, 3.35f, 0, 1, .85f, .9f, 4);
                Crate(3.1f, 3.4f, 1.13f, .92f, .8f, .85f, 7);
                Crate(6.3f, 4.1f, 0, 1.8f, .85f, 1.1f, 90);
                Crate(24.6f, 6, 0, 1.2f, 1, 1, 0);
                Crate(25.9f, 6, 0, 1, .8f, 1, 0);
                Crate(6.4f, 18.5f, 0, 1.1f, .85f, 1, 0);
                for (int i = 0; i < 5; i++) Barrel(9 + (i % 3) * .85f, 3.4f + (i / 3) * .85f, i);
                Barrel(30.5f, 8.4f, 2); Barrel(30.5f, 7.5f, 0);
                Barrel(13, 18.4f, 1); Barrel(13, 19.3f, 2);

                // Road-side barriers keep the central road and camera/pawn test paths clear.
                for (int i = 0; i < 4; i++) Barrier(3 + i * 2.5f, 8.8f, i % 2 == 0 ? 0 : 5);
                Barrier(18.8f, 7.2f, 90); Barrier(18.8f, 9.3f, 90);
                Barrier(29.8f, 17.8f, 0); Barrier(32.2f, 17.8f, -4);
                Table(7.9f, 6.1f, 0); Table(8.5f, 17.2f, 0);
                Table(29.5f, 5.1f, 90);
                Bench(7.9f, 7.3f, 0); Bench(9.5f, 17.2f, 90);
                TimberPile(2.8f, 17.5f);

                // An inspection pad behind the wall makes the through-hole and rubble legible.
                Flat(HybridSurface.Concrete, 16.5f, 15.65f, 25.5f, 17.35f, -.012f, Tint(219, 219, 211));
                Box(HybridSurface.Metal, 17.0f, .015f, 15.3f, .8f, .03f, .1f, Tint(220, 192, 103));
                Box(HybridSurface.Metal, 25.0f, .015f, 15.3f, .8f, .03f, .1f, Tint(220, 192, 103));
            }

            void Flat(HybridSurface surface, float x0, float z0, float x1, float z1, float y, Color32 tint)
            {
                Group(surface, (x0 + x1) * .5f, (z0 + z1) * .5f).Quad(
                    new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), new Vector3(x0, y, z0), tint,
                    new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0), new Vector2(x0, z0));
            }

            void Building(float x, float z, float width, float depth, float height, bool timber)
            {
                HybridSurface wallMaterial = timber ? HybridSurface.Wood : HybridSurface.Brick;
                Color32 wallColor = timber ? Tint(216, 220, 203) : Tint(240, 238, 230);
                float frontZ = timber ? z + .15f : z + depth - .15f;
                float backZ = timber ? z + depth - .15f : z + .15f;
                Flat(HybridSurface.Concrete, x + .15f, z + .15f, x + width - .15f, z + depth - .15f, .025f, Tint(235, 234, 225));
                var front = new HybridWallState((int)Math.Round(width / .2f), (int)Math.Round(height / .2f), .2f, .3f);
                float doorLeft = width * .5f - .9f, doorRight = width * .5f + .9f;
                front.Open(doorLeft, 0, doorRight, 2.2f);
                float windowLeft = timber ? .8f : 1.2f, windowWidth = timber ? 1.2f : 1.6f;
                front.Open(windowLeft, 1, windowLeft + windowWidth, 2.2f);
                front.Open(width - windowLeft - windowWidth, 1, width - windowLeft, 2.2f);
                Group(wallMaterial, x + width / 2, frontZ).Append(front.BuildMesh(wallColor), new Vector3(x, 0, frontZ));

                var back = new HybridWallState(front.Columns, front.Rows, .2f, .3f);
                back.Open(width / 2 - 1, 1, width / 2 + 1, 2.2f);
                Group(wallMaterial, x + width / 2, backZ).Append(back.BuildMesh(wallColor), new Vector3(x, 0, backZ));
                for (int side = 0; side < 2; side++)
                {
                    var wall = new HybridWallState((int)Math.Round((depth - .6f) / .2f), front.Rows, .2f, .3f);
                    float window = (depth - .6f) / 2;
                    wall.Open(window - .8f, 1, window + .8f, 2.2f);
                    float sx = side == 0 ? x + .15f : x + width - .15f;
                    Group(wallMaterial, sx, z + depth / 2).Append(wall.BuildMesh(wallColor), new Vector3(sx, 0, z + .3f), -90);
                    WindowFrame(sx, z + .3f + window, 1.6f, 1.2f, 1, -90);
                }
                WindowFrame(x + windowLeft + windowWidth / 2, frontZ, windowWidth, 1.2f, 1, 0);
                WindowFrame(x + width - windowLeft - windowWidth / 2, frontZ, windowWidth, 1.2f, 1, 0);
                WindowFrame(x + width / 2, backZ, 2, 1.2f, 1, 0);
                // Door jambs and lintel retain a full walk-through opening.
                Box(HybridSurface.Wood, x + doorLeft, 1.1f, frontZ, .09f, 2.2f, .37f, Tint(204, 196, 173));
                Box(HybridSurface.Wood, x + doorRight, 1.1f, frontZ, .09f, 2.2f, .37f, Tint(204, 196, 173));
                Box(HybridSurface.Wood, x + width / 2, 2.2f, frontZ, doorRight - doorLeft + .09f, .09f, .37f, Tint(204, 196, 173));
                // A door leaf held open against the inside wall, without blocking the doorway.
                Box(HybridSurface.Wood, x + doorRight + .13f, 1.05f, frontZ + (timber ? .85f : -.85f), .1f, 2.05f, 1.6f, Tint(224, 224, 216));

                float eave = height + .08f, ridge = height + (timber ? 1.0f : 1.25f);
                var roof = Group(HybridSurface.Roof, x + width / 2, z + depth / 2, true);
                HybridGeometry.Roof(roof, x - .32f, x + width + .32f, z - .35f, z + depth + .35f, eave, ridge, timber ? Tint(220, 228, 219) : HybridGeometry.White);
                // Gables belong to the roof group, so hiding the roof gives an unobstructed interior view.
                var gables = Group(wallMaterial, x + width / 2, z + depth / 2, true);
                gables.Triangle(new Vector3(x, height, z), new Vector3(x, height, z + depth), new Vector3(x, ridge - .08f, z + depth / 2), wallColor);
                gables.Triangle(new Vector3(x + width, height, z + depth), new Vector3(x + width, height, z), new Vector3(x + width, ridge - .08f, z + depth / 2), wallColor);
                Box(HybridSurface.Metal, x + width / 2, ridge + .025f, z + depth / 2, width + .7f, .1f, .18f, Tint(223, 225, 219), 0, true);
                Box(HybridSurface.Wood, x + width / 2, eave - .055f, z - .32f, width + .7f, .17f, .1f, Tint(211, 210, 193), 0, true);
                Box(HybridSurface.Wood, x + width / 2, eave - .055f, z + depth + .32f, width + .7f, .17f, .1f, Tint(211, 210, 193), 0, true);
                // Main building chimney is genuine brick volume; no baked perspective artwork.
                if (!timber)
                {
                    Box(HybridSurface.Brick, x + 1.6f, ridge - .2f, z + 1.9f, .65f, 1.6f, .65f, Tint(221, 215, 204), 0, true);
                    Box(HybridSurface.Concrete, x + 1.6f, ridge + .65f, z + 1.9f, .82f, .14f, .82f, Tint(235, 235, 228), 0, true);
                    Box(HybridSurface.Metal, x + 1.6f, ridge + .725f, z + 1.9f, .44f, .02f, .44f, Tint(75, 77, 73), 0, true);
                }
            }

            void WindowFrame(float x, float z, float width, float height, float bottom, float yaw)
            {
                var group = Group(HybridSurface.Wood, x, z); var offset = new Vector3(x, 0, z);
                Action<float, float, float, float, float, float> b = (px, py, pz, w, h, d) =>
                    HybridGeometry.Box(group, HybridGeometry.Add(offset, HybridGeometry.Rotate(new Vector3(px, py, pz), yaw)), new Vector3(w, h, d), Tint(231, 226, 210), yaw);
                b(-width / 2, bottom + height / 2, 0, .075f, height + .1f, .38f);
                b(width / 2, bottom + height / 2, 0, .075f, height + .1f, .38f);
                b(0, bottom, 0, width + .12f, .09f, .45f);
                b(0, bottom + height, 0, width + .12f, .075f, .38f);
                // A narrow mullion makes window depth readable while leaving open apertures.
                b(0, bottom + height / 2, 0, .045f, height, .08f);
            }

            void Crate(float x, float z, float bottom, float width, float height, float depth, float yaw)
            {
                var wood = Group(HybridSurface.Wood, x, z); var metal = Group(HybridSurface.Metal, x, z);
                Vector3 origin = new Vector3(x, bottom, z);
                Action<HybridMeshData, float, float, float, float, float, float, Color32> b = (mesh, px, py, pz, w, h, d, tint) =>
                    HybridGeometry.Box(mesh, HybridGeometry.Add(origin, HybridGeometry.Rotate(new Vector3(px, py, pz), yaw)), new Vector3(w, h, d), tint, yaw);
                b(wood, 0, height / 2, 0, width, height, depth, Tint(226, 219, 201));
                for (int face = -1; face <= 1; face += 2)
                {
                    b(wood, 0, .10f, face * (depth / 2 + .025f), width + .04f, .14f, .075f, Tint(246, 239, 217));
                    b(wood, 0, height - .10f, face * (depth / 2 + .025f), width + .04f, .14f, .075f, Tint(246, 239, 217));
                    b(wood, face * (width / 2 + .025f), height / 2, 0, .075f, height, .13f, Tint(246, 239, 217));
                    b(metal, face * width * .3f, height + .009f, 0, .045f, .018f, depth + .02f, Tint(187, 189, 176));
                    b(metal, face * width * .3f, height / 2, -depth / 2 - .01f, .045f, height, .018f, Tint(187, 189, 176));
                    b(metal, face * width * .3f, height / 2, depth / 2 + .01f, .045f, height, .018f, Tint(187, 189, 176));
                }
            }

            void Barrel(float x, float z, int variant)
            {
                var metal = Group(HybridSurface.Metal, x, z);
                Color32 color = variant % 3 == 0 ? Tint(161, 178, 150) : variant % 3 == 1 ? Tint(190, 159, 138) : Tint(176, 183, 179);
                HybridGeometry.Cylinder(metal, new Vector3(x, .03f, z), .34f, .91f, color, 20);
                foreach (float h in new[] { .04f, .30f, .66f, .91f })
                    HybridGeometry.Cylinder(metal, new Vector3(x, h, z), .357f, .045f, Tint(217, 220, 210), 20);
                HybridGeometry.Cylinder(metal, new Vector3(x + .13f, .945f, z + .08f), .05f, .02f, Tint(124, 129, 120), 12);
            }

            void Barrier(float x, float z, float yaw)
            {
                var concrete = Group(HybridSurface.Concrete, x, z);
                HybridGeometry.BevelBox(concrete, new Vector3(x, .51f, z), new Vector3(2, 1.02f, .65f), .09f, Tint(240, 239, 229), yaw);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 pos = HybridGeometry.Add(new Vector3(x, 0, z), HybridGeometry.Rotate(new Vector3(side * .62f, 1.065f, 0), yaw));
                    HybridGeometry.Box(Group(HybridSurface.Metal, x, z), pos, new Vector3(.1f, .09f, .14f), Tint(145, 148, 140), yaw);
                }
            }

            void Table(float x, float z, float yaw)
            {
                var wood = Group(HybridSurface.Wood, x, z);
                Action<float, float, float, float, float, float> b = (px, py, pz, w, h, d) =>
                    HybridGeometry.Box(wood, HybridGeometry.Add(new Vector3(x, 0, z), HybridGeometry.Rotate(new Vector3(px, py, pz), yaw)), new Vector3(w, h, d), Tint(233, 228, 210), yaw);
                b(0, .85f, 0, 1.65f, .12f, .9f);
                for (int i = -1; i <= 1; i += 2) for (int j = -1; j <= 1; j += 2) b(i * .65f, .4f, j * .32f, .11f, .8f, .11f);
                b(0, .66f, -.33f, 1.4f, .11f, .09f); b(0, .66f, .33f, 1.4f, .11f, .09f);
                Box(HybridSurface.Metal, x + .25f, .94f, z, .3f, .06f, .22f, Tint(179, 192, 160), yaw);
            }
            void Bench(float x, float z, float yaw)
            {
                Box(HybridSurface.Wood, x, .48f, z, 1.5f, .11f, .35f, Tint(220, 217, 197), yaw);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 p = HybridGeometry.Add(new Vector3(x, 0, z), HybridGeometry.Rotate(new Vector3(sign * .55f, .22f, 0), yaw));
                    Box(HybridSurface.Wood, p.x, p.y, p.z, .1f, .44f, .31f, Tint(219, 215, 195), yaw);
                }
            }
            void TimberPile(float x, float z)
            {
                for (int row = 0; row < 3; row++) for (int col = 0; col < 3 - row; col++)
                    Box(HybridSurface.Wood, x + row * .12f + col * .27f, .11f + row * .23f, z, .22f, .21f, 3.2f - row * .17f, Tint((byte)(231 - col * 7), (byte)(224 - col * 7), (byte)(203 - col * 7)));
            }
        }
    }
}
