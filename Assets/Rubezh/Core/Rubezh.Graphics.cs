using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SilentOrders.Rendering;

namespace Rubezh
{
    public static partial class Graphics
    {
        class Tex
        {
            public int W,
                H;
            public byte[] P;
        }

        static volatile Tex[][] atlas;
        static readonly object loadGate = new object(),
            publishGate = new object();
        static int generation;

        // Source sampling density, independent of world footprint; do not upscale source art.
        public static float DetailScale(int id)
        {
            float baseline =
                id == 128 || id == 129 ? 1.25f
                : (id >= 8 && id <= 11 || id >= 21 && id <= 24 || id >= 34 && id <= 37) ? 1.30f
                : 1.10f;
            bool personal =
                id >= 0 && id < 46
                || id >= 122 && id <= 127
                || id >= 130 && id <= 137
                || id >= LoadoutSoldierStart && id < BackpackStart + 8;
            return baseline * (personal ? 1.30f : 1f);
        }

        // Pixel-only battle mode. Callers restore this flag after battlefield rendering.
        public static bool PrimitiveBattleMode;
        public static Func<Stream> OpenAtlas; // Optional legacy-tool input; runtime uses SpritePacks.

        class Stamp
        {
            public int X,
                Y,
                W,
                H;
            public uint[] Pixels;
        }

        struct StampKey : IEquatable<StampKey>
        {
            public int Id,
                Turn,
                X,
                Y;
            public float W,
                H,
                Scale;
            public bool Enemy;

            public bool Equals(StampKey b) =>
                Id == b.Id
                && Turn == b.Turn
                && X == b.X
                && Y == b.Y
                && W == b.W
                && H == b.H
                && Scale == b.Scale
                && Enemy == b.Enemy;

            public override bool Equals(object o) => o is StampKey && Equals((StampKey)o);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Id;
                    h = h * 31 + Turn;
                    h = h * 31 + X;
                    h = h * 31 + Y;
                    h = h * 31 + W.GetHashCode();
                    h = h * 31 + H.GetHashCode();
                    h = h * 31 + Scale.GetHashCode();
                    return h * 31 + (Enemy ? 1 : 0);
                }
            }
        }

        static readonly Dictionary<StampKey, Stamp> cache = new Dictionary<StampKey, Stamp>();
        static readonly Queue<StampKey> order = new Queue<StampKey>();
        const long StampBudget = 16L * 1024 * 1024;
        static long stampBytes;
        public static long StampCacheBytes => stampBytes;

        static void ClearStampCache()
        {
            cache.Clear();
            order.Clear();
            stampBytes = 0;
        }

        public static void Release()
        {
            lock (publishGate)
            {
                System.Threading.Interlocked.Increment(ref generation);
                atlas = null;
                PackedMode = false;
                loadedPages.Clear();
                partialPages.Clear();
            }

            ClearStampCache();
            OpenAtlas = null;
        }

        // Load HD sprites for the software renderer and warm up the atlas off the Unity main thread.
        public static void Warmup(Action<float> report)
        {
            Load(report);
        }

        public static void Warmup(Action<float> report, Func<bool> cancelled)
        {
            Load(report, cancelled);
        }

        static void Load(Action<float> report = null, Func<bool> cancelled = null)
        {
            if (atlas != null)
            {
                if (report != null)
                    report(1f);
                return;
            }

            lock (loadGate)
            {
                LoadExclusive(report, cancelled);
            }
        }

        static void LoadExclusive(Action<float> report, Func<bool> cancelled)
        {
            int epoch = System.Threading.Volatile.Read(ref generation);
            if (cancelled != null && cancelled())
                throw new OperationCanceledException();
            if (atlas != null)
            {
                if (report != null)
                    report(1f);
                return;
            }

            var source = OpenAtlas;
            if (source == null)
                throw new InvalidOperationException("Sprite source is not initialized.");
            using (var stream = source())
            using (var packed = new GZipStream(stream, CompressionMode.Decompress))
            using (var reader = new BinaryReader(packed))
            {
                if (reader.ReadInt32() != 0x48445152)
                    throw new InvalidDataException("Invalid sprite pack");
                int count = reader.ReadInt32();
                if (count <= 0 || count > 16384)
                    throw new InvalidDataException("Invalid sprite count");
                var loaded = new Tex[count][];
                for (int i = 0; i < count; i++)
                {
                    if (epoch != System.Threading.Volatile.Read(ref generation) || cancelled != null && cancelled())
                        throw new OperationCanceledException();
                    int levels = reader.ReadInt32();
                    if (levels <= 0 || levels > 32)
                        throw new InvalidDataException("Invalid sprite mip count");
                    loaded[i] = new Tex[levels];
                    for (int j = 0; j < levels; j++)
                    {
                        int w = reader.ReadInt32(),
                            h = reader.ReadInt32();
                        if (w <= 0 || h <= 0 || w > 8192 || h > 8192)
                            throw new InvalidDataException("Invalid sprite dimensions");
                        int size = checked(w * h * 4);
                        byte[] pixels = reader.ReadBytes(size);
                        if (pixels.Length != size)
                            throw new EndOfStreamException();
                        loaded[i][j] = new Tex
                        {
                            W = w,
                            H = h,
                            P = pixels,
                        };
                    }

                    if (report != null)
                        report((i + 1f) / count);
                }

                // Publish only a fully decoded array; do not expose partially loaded sprites.
                lock (publishGate)
                {
                    if (epoch != generation || cancelled != null && cancelled())
                        throw new OperationCanceledException();
                    atlas = loaded;
                }
            }
        }

        public static void Pawn(PixelCanvas c, int id, float x, float y, int w, int h, bool enemy, float angle)
        {
            Draw(c, id + (enemy ? 13 : 0), x, y, w, h, enemy, angle);
        }

        public static void Equipment(PixelCanvas c, int id, float x, float y, int w, int h, bool enemy, float angle)
        {
            Draw(c, (enemy ? 38 : 26) + id, x, y, w, h, enemy, angle);
        }

        public static void Sniper(PixelCanvas c, int id, float x, float y, int w, int h, float angle)
        {
            Draw(c, id == 5 ? 35 : 34, x, y, w, h, false, angle);
        }

        public static void Underbarrel(PixelCanvas c, int id, float x, float y, int w, int h, float angle)
        {
            Draw(c, id == 3 ? 37 : 36, x, y, w, h, false, angle);
        }

        // The 24 authored scenery sprites are stored separately in Art/LevelProps and baked
        // by stable IDs after the original 46 sprites in the independently loaded pages.
        // Their ids are stable and shared by the Unity level editor and both HD renderers.
        public const int PropSpriteStart = 46;
        public const int DebrisSpriteStart = 86;
        public const int HandSpriteStart = 122;
        public const int VogSprite = 7861;
        public const int GrenadeSprite = 126,
            TourniquetSprite = 127;
        public const int AmmunitionStart = 219;
        public const int RedBeltSprite = 7459,
            RedSniperMagSprite = 7460,
            AmmoArtCount = 6;

        public static int AmmoId(int type) =>
            type == 4 ? RedBeltSprite
            : type == 5 ? RedSniperMagSprite
            : AmmunitionStart + type;

        public static int AmmoHeight(int type) => (type == 3 || type == 5) ? 4 : 6;

        public static float AmmoWidth(int type)
        {
            int w,
                h;
            SpriteData(AmmoId(type), 0, out w, out h);
            return AmmoHeight(type) * (float)w / h;
        }

        public static void Ammunition(PixelCanvas c, int type, float x, float y)
        {
            StudioSprite(c, AmmoId(type), x, y, AmmoWidth(type), AmmoHeight(type));
        }

        public static void Grenade(PixelCanvas c, float x, float y)
        {
            Draw(c, GrenadeSprite, x, y, 7, 5, false, 0);
        }

        public static void Tourniquet(PixelCanvas c, float x, float y)
        {
            Draw(c, TourniquetSprite, x, y, 9, 4, false, 0);
        }

        public static float VehicleScale(bool green, int mission)
        {
            return 1f;
        }

        // Authored grip/support pairs, with separate left-facing artwork.
        public static void Hand(PixelCanvas c, bool support, bool left, float x, float y, float angle, bool enemy)
        {
            Hand(c, support, left, x, y, angle, enemy, 1f);
        }

        public static void Hand(
            PixelCanvas c,
            bool support,
            bool left,
            float x,
            float y,
            float angle,
            bool enemy,
            float scale
        )
        {
            if (PrimitiveBattleMode)
            {
                c.Rect(
                    (int)Math.Round(x) - 1,
                    (int)Math.Round(y) - 1,
                    2,
                    2,
                    enemy ? PixelCanvas.RGB(165, 115, 87) : PixelCanvas.RGB(125, 137, 92)
                );
                return;
            }

            Draw(
                c,
                HandSpriteStart + (support ? 2 : 0) + (left ? 1 : 0),
                x,
                y,
                4 * scale,
                3 * scale,
                false,
                left ? angle - (float)Math.PI : angle
            );
        }

        public static void Debris(PixelCanvas c, int id, float x, float y, int w, int h, float angle)
        {
            if (id == 39 || id == 40)
            {
                int xx = (int)x,
                    yy = (int)y,
                    ww = Math.Max(3, w / 2),
                    hh = Math.Max(2, h / 3);
                c.Polygon(
                    new[] { xx - ww, xx - ww / 2, xx + ww, xx + ww / 2 },
                    new[] { yy, yy - hh, yy, yy + hh },
                    PixelCanvas.RGB(39, 40, 35)
                );
                c.Line(xx - ww / 2, yy - hh, xx + ww, yy, PixelCanvas.RGB(70, 70, 60));
                return;
            }

            if (id >= 41)
            {
                int sprite = SilentOrders.Core.DebrisCatalog.Sprite(id);
                if (sprite >= 0)
                    Draw(c, sprite, x, y, w, h, false, angle);
                return;
            }

            if (id >= 1 && id <= 36)
                Draw(c, DebrisSpriteStart + id - 1, x, y, w, h, false, angle);
            else if (id == 37 || id == 38)
                Draw(c, 128 + id - 37, x, y, w, h, false, angle);
        }

        // Shared world sizes: preserve source-art proportions, then apply authored per-object scale.
        static readonly int[] PropWidths =
        {
            0,
            32,
            44,
            38,
            50,
            32,
            38,
            32,
            26,
            36,
            44,
            38,
            40,
            28,
            30,
            22,
            22,
            44,
            44,
            64,
            64,
            52,
            52,
            48,
            48,
        };
        static readonly int[] PropHeights =
        {
            0,
            93,
            49,
            47,
            48,
            23,
            25,
            29,
            38,
            26,
            21,
            32,
            31,
            26,
            26,
            35,
            34,
            33,
            32,
            34,
            34,
            29,
            29,
            26,
            26,
        };

        public static void PropSize(int id, out int width, out int height)
        {
            if (id < 1 || id > 24)
            {
                width = height = 0;
                return;
            }

            width = PropWidths[id];
            height = PropHeights[id];
            if (id >= 9 && id <= 12)
            {
                width = (int)Math.Round(width * .5);
                height = (int)Math.Round(height * .5);
            }
        }

        public static void Prop(PixelCanvas c, int id, float x, float y, int width, int height)
        {
            Prop(c, id, x, y, width, height, 0f);
        }

        public static void Prop(PixelCanvas c, int id, float x, float y, int width, int height, float angleRadians)
        {
            if (id >= 1 && id <= 24)
                Draw(c, PropSpriteStart + id - 1, x, y, width, height, false, angleRadians);
        }

        // Only TWO approved HD images exist: Humvee (70) and GAZ Tigr (78).
        // Render dimensions are intentionally smaller than v0.11.32; full-res source sprites stay intact.
        // The other 14 legacy directional entries are transparent placeholders.
        public const int VehicleSpriteStart = 70;
        public const int VehicleRenderWidth =
            (SilentOrders.Core.VehicleArrivals.LengthCells + SilentOrders.Core.VehicleArrivals.WidthCells) * 11;
        public const int VehicleRenderHeight = 105;

        // Projected centre of the hull ground plane, not the lowest tire pixel.
        // Both renderers and the editor use the same calibrated anchor.
        public const float VehicleGroundAnchor = .70f;
        public static float VehicleSpriteCenterOffset
        {
            get { return (VehicleGroundAnchor - .5f) * VehicleRenderHeight; }
        }

        public static int VehicleSprite(bool green)
        {
            return VehicleSpriteStart + (green ? 8 : 0);
        }

        public static void Vehicle(PixelCanvas c, bool green, int direction, float x, float y, float scale = 1f)
        {
            Draw(
                c,
                VehicleSprite(green),
                x,
                y,
                (int)Math.Round(VehicleRenderWidth * scale),
                (int)Math.Round(VehicleRenderHeight * scale),
                false,
                0f
            );
        }

        // All eight pack images share a square canvas and an authored torso anchor.
        // Direction changes select artwork, never stretch the same picture.
        public const int BackpackStart = 211;

        public static int BackpackSprite(bool medical, int frame) =>
            BackpackStart + (medical ? 0 : 4) + Math.Max(0, Math.Min(3, frame));

        public const int BackpackSize = 22;

        public static void Backpack(PixelCanvas c, bool medical, int frame, float x, float y)
        {
            float offset =
                frame == 1 ? -7
                : frame == 3 ? 7
                : 0;
            Draw(c, BackpackSprite(medical, frame), x + offset, y, BackpackSize, BackpackSize, false, 0);
        }

        public const int WholeSoldierStart = 130,
            KedrBadge = 138;
        public const int LoadoutSoldierStart = 139;

        public static int SoldierSprite(int armor, int rig, bool enemy, int frame = 0)
        {
            armor = GreenLook.VisualArmor;
            rig = GreenLook.VisualRig;
            frame = Math.Max(0, Math.Min(3, frame));
            return LoadoutSoldierStart + ((armor * 3 + rig) * 2 + (enemy ? 1 : 0)) * 4 + frame;
        }

        public static void WholeSoldier(
            PixelCanvas c,
            int frame,
            float x,
            float y,
            int size,
            bool enemy,
            float angle = 0,
            int armor = 0,
            int rig = 0
        )
        {
            Draw(c, SoldierSprite(armor, rig, enemy, frame), x, y, size, size, enemy, angle);
        }

        public static float CorpseAngle(SilentOrders.Core.Soldier s)
        {
            uint hash = unchecked(
                (uint)(s.Id * 73856093) ^ (uint)(s.Position.X * 19349663) ^ (uint)(s.Position.Y * 83492791)
            );
            hash ^= hash >> 13;
            return (hash % 6283) * .001f;
        }

        public static int CorpseFrame(SilentOrders.Core.Soldier s) => (int)(CorpseAngle(s) * 997) % 4;

        public static int SpriteCount
        {
            get
            {
                Load();
                return atlas.Length;
            }
        }

        public static int LevelCount(int id)
        {
            Load();
            if (PackedMode && !HasSprite(id))
            {
                ReportMissingSprite(id);
                return 1;
            }
            return atlas[ReadyId(id)].Length;
        }

        public static byte[] SpriteData(int id, int level, out int width, out int height)
        {
            Load();
            if (PackedMode && !HasSprite(id))
            {
                ReportMissingSprite(id);
                width = height = 1;
                return missingPixels;
            }
            var t = atlas[ReadyId(id)][level];
            width = t.W;
            height = t.H;
            return t.P;
        }

        public static void ReplaySprite(PixelCanvas c, int id, float x, float y, int w, int h, float angle)
        {
            Draw(c, id, x, y, w, h, id >= 13 && id < 26, angle);
        }

        public static readonly int[] LooseIds = { 8, 9, 36, 37, 34, 35, 21, 22, 36, 37, 34, 35, 6829, 6830, 127, 6824 };
        public static readonly float[] LooseAspect =
        {
            0.4804688f,
            0.3750000f,
            0.4492188f,
            0.4804688f,
            0.2500000f,
            0.2382812f,
            0.5000000f,
            0.3867188f,
            0.4492188f,
            0.4804688f,
            0.2500000f,
            0.2382812f,
            0.2734375f,
            0.2167969f,
            0.8281250f,
            1.4065934f,
        };

        public static void LooseItem(
            PixelCanvas c,
            int i,
            float x,
            float y,
            float angle,
            float scale = 1,
            SilentOrders.Core.WeaponUpgrade upgrade = null
        )
        {
            int appearance = WeaponAppearance.Sprite(i, upgrade);
            if (appearance >= 0)
            {
                float width = 19 * scale;
                StudioSprite(c, appearance, x, y, width, width * WeaponAppearance.Aspect(appearance), angle);
                return;
            }
            i = Math.Max(0, Math.Min(15, i));
            float w = (i >= 14 ? 9 : 19) * scale;
            StudioSprite(c, LooseIds[i], x, y, w, w * LooseAspect[i], angle);
        }

        public static void StudioSprite(PixelCanvas c, int id, float x, float y, float w, float h, float angle = 0)
        {
            bool old = PrimitiveBattleMode;
            PrimitiveBattleMode = false;
            try
            {
                Draw(c, id, x, y, w, h, false, angle);
            }
            finally
            {
                PrimitiveBattleMode = old;
            }
        }

        static void Draw(PixelCanvas c, int id, float cx, float cy, float width, float height, bool enemy, float angle)
        {
            if (width == 0 || height == 0)
                return;
            c.TransformSprite(ref cx, ref cy, ref width, ref height);
            if (PrimitiveBattleMode)
            {
                DrawPrimitive(c, id, cx, cy, (int)width, (int)height, enemy);
                return;
            }

            if (PackedMode && !HasSprite(id))
            {
                ReportMissingSprite(id);
                return;
            }
            if (c.Gpu != null)
            {
                c.Gpu.Sprite(id, cx, cy, (int)Math.Round(width), (int)Math.Round(height), angle, c.Depth, c.TestDepth);
                return;
            }

            Load();
            float w = width * c.Scale,
                h = height * c.Scale;
            // Quarter-pixel translations and 1024 angle steps: under 0.15 px error at game scale.
            int centerX = (int)Math.Floor(cx * c.Scale),
                centerY = (int)Math.Floor(cy * c.Scale);
            int phaseX = (int)Math.Round((cx * c.Scale - centerX) * 4),
                phaseY = (int)Math.Round((cy * c.Scale - centerY) * 4);
            int turn = (int)Math.Round(angle * 1024 / (Math.PI * 2));
            var key = new StampKey
            {
                Id = id,
                W = width,
                H = height,
                Scale = c.Scale,
                Enemy = enemy,
                Turn = turn,
                X = phaseX,
                Y = phaseY,
            };
            Stamp stamp;
            if (!cache.TryGetValue(key, out stamp))
            {
                stamp = Build(id, w, h, enemy, (float)(turn * Math.PI * 2 / 1024), phaseX * .25f, phaseY * .25f);
                long bytes = (long)stamp.Pixels.Length * 4;
                if (bytes <= StampBudget)
                {
                    while (cache.Count > 0 && (cache.Count >= 256 || stampBytes + bytes > StampBudget))
                    {
                        var evicted = order.Dequeue();
                        stampBytes -= (long)cache[evicted].Pixels.Length * 4;
                        cache.Remove(evicted);
                    }

                    cache.Add(key, stamp);
                    order.Enqueue(key);
                    stampBytes += bytes;
                }
            }

            int left = centerX + stamp.X,
                top = centerY + stamp.Y;
            int sx0 = Math.Max(0, Math.Max(c.ClipLeft, c.LayerLeft) - left),
                sx1 = Math.Min(stamp.W, Math.Min(c.Width, Math.Min(c.ClipRight, c.LayerRight)) - left),
                sy0 = Math.Max(0, c.ClipTop - top),
                sy1 = Math.Min(stamp.H, Math.Min(c.Height, c.ClipBottom) - top);
            for (int sy = sy0; sy < sy1; sy++)
            {
                int src = sy * stamp.W + sx0,
                    dest = (top + sy) * c.Width + left + sx0;
                for (int sx = sx0; sx < sx1; sx++, src++, dest++)
                {
                    uint pixel = stamp.Pixels[src];
                    int alpha = (int)(pixel >> 24);
                    if (alpha == 0 || (c.TestDepth && c.Occlusion[dest] > c.Depth))
                        continue;
                    if (alpha == 255)
                        c.Pixels[dest] = pixel;
                    else
                    {
                        uint old = c.Pixels[dest];
                        int inv = 255 - alpha;
                        int r = (int)(pixel & 255) + (int)(old & 255) * inv / 255,
                            g = (int)((pixel >> 8) & 255) + (int)((old >> 8) & 255) * inv / 255,
                            b = (int)((pixel >> 16) & 255) + (int)((old >> 16) & 255) * inv / 255;
                        c.Pixels[dest] =
                            (PixelCanvas.RGB(Math.Min(255, r), Math.Min(255, g), Math.Min(255, b)) & 0xFFFFFFu)
                            | ((uint)(alpha + (int)(old >> 24) * inv / 255) << 24);
                    }

                    if (c.RecordDepth && alpha >= 128)
                        c.StoreDepth(dest, c.Depth);
                }
            }
        }

        static void DrawPrimitive(PixelCanvas c, int id, float cx, float cy, int width, int height, bool enemy)
        {
            // Primitive top-down silhouettes in the original canvas; no atlas lookup or stamp cache.
            int x = (int)(cx * c.Scale),
                y = (int)(cy * c.Scale);
            int w = Math.Max(2, (int)(Math.Abs(width) * c.Scale)),
                h = Math.Max(2, (int)(Math.Abs(height) * c.Scale));
            int halfW = Math.Max(1, w / 3),
                halfH = Math.Max(1, h / 3);
            uint main = enemy ? PixelCanvas.RGB(158, 67, 57) : PixelCanvas.RGB(76, 144, 91);
            uint trim = enemy ? PixelCanvas.RGB(92, 38, 34) : PixelCanvas.RGB(39, 82, 48);
            // Equipment and scenery remain recognizable as geometric tokens.
            bool unit =
                id >= 139 && id < 211
                || id >= 130 && id < 138
                || id < 26
                || id == 34
                || id == 35
                || id == 36
                || id == 37;
            int left = x - w / 2,
                top = y - h / 2;
            if (c.Gpu != null)
            {
                // The same opaque silhouette as the software reference, represented by
                // at most three retained rectangles. Never allocate/access CPU pixels.
                if (!unit)
                    PrimitiveGpuRect(c, left, top, left + w, top + h, main);
                else
                {
                    PrimitiveGpuRect(
                        c,
                        Math.Max(left, x - halfW + 1),
                        top,
                        Math.Min(left + w, x + halfW),
                        top + h,
                        main
                    );
                    PrimitiveGpuRect(
                        c,
                        Math.Max(left, x - w / 2 + 1),
                        Math.Max(top, y - halfH + 1),
                        Math.Min(left + w, x + w / 2),
                        Math.Min(top + h, y + halfH),
                        main
                    );
                    PrimitiveGpuRect(
                        c,
                        Math.Max(left, x - halfW / 2 + 1),
                        top,
                        Math.Min(left + w, x + halfW / 2),
                        Math.Min(top + h, y - halfH / 2),
                        trim
                    );
                }
                return;
            }
            for (int yy = Math.Max(c.ClipTop, top); yy < Math.Min(Math.Min(c.Height, c.ClipBottom), top + h); yy++)
            for (int xx = Math.Max(c.ClipLeft, left); xx < Math.Min(Math.Min(c.Width, c.ClipRight), left + w); xx++)
            {
                int rx = xx - x,
                    ry = yy - y;
                bool shape = unit ? (Math.Abs(rx) < halfW || (Math.Abs(ry) < halfH && Math.Abs(rx) < w / 2)) : true;
                if (!shape)
                    continue;
                int idx = yy * c.Width + xx;
                if (c.TestDepth && c.Occlusion[idx] > c.Depth)
                    continue;
                c.Pixels[idx] = unit && (Math.Abs(rx) < halfW / 2 && ry < -halfH / 2) ? trim : main;
                if (c.RecordDepth)
                    c.StoreDepth(idx, c.Depth);
            }
        }

        static void PrimitiveGpuRect(PixelCanvas canvas, int left, int top, int right, int bottom, uint color)
        {
            left = Math.Max(Math.Max(0, canvas.ClipLeft), left);
            top = Math.Max(Math.Max(0, canvas.ClipTop), top);
            right = Math.Min(Math.Min(canvas.Width, canvas.ClipRight), right);
            bottom = Math.Min(Math.Min(canvas.Height, canvas.ClipBottom), bottom);
            if (right > left && bottom > top)
                canvas.Gpu.Rect(left, top, right - left, bottom - top, color, canvas.Depth, canvas.TestDepth);
        }

        static bool originalSampling;

        public static void OriginalSampling(bool value)
        {
            if (originalSampling == value)
                return;
            originalSampling = value;
            ClearStampCache();
        }

        static Stamp Build(int id, float w, float h, bool enemy, float angle, float x, float y)
        {
            var levels = atlas[ReadyId(id)];
            int level = 0;
            while (
                !originalSampling
                && level + 1 < levels.Length
                && levels[level + 1].W >= Math.Abs(w) * DetailScale(id)
                && levels[level + 1].H >= Math.Abs(h) * DetailScale(id)
            )
                level++;
            var t = levels[level];
            float cos = (float)Math.Cos(angle),
                sin = (float)Math.Sin(angle);
            float ex = (Math.Abs(w * cos) + Math.Abs(h * sin)) * .5f + 1,
                ey = (Math.Abs(w * sin) + Math.Abs(h * cos)) * .5f + 1;
            int x0 = (int)Math.Floor(x - ex),
                x1 = (int)Math.Ceiling(x + ex),
                y0 = (int)Math.Floor(y - ey),
                y1 = (int)Math.Ceiling(y + ey);
            var stamp = new Stamp
            {
                X = x0,
                Y = y0,
                W = x1 - x0 + 1,
                H = y1 - y0 + 1,
            };
            stamp.Pixels = new uint[stamp.W * stamp.H];
            float du = cos / w * t.W,
                dv = -sin / h * t.H;
            for (int py = y0; py <= y1; py++)
            {
                float u = ((x0 - x) * cos + (py - y) * sin) / w * t.W + t.W * .5f - .5f;
                float v = (-(x0 - x) * sin + (py - y) * cos) / h * t.H + t.H * .5f - .5f;
                for (int px = x0; px <= x1; px++, u += du, v += dv)
                {
                    if (u < -.5f || v < -.5f || u >= t.W - .5f || v >= t.H - .5f)
                        continue;
                    float su = Math.Max(0, Math.Min(t.W - 1, u)),
                        sv = Math.Max(0, Math.Min(t.H - 1, v));
                    int ix = (int)su,
                        iy = (int)sv;
                    int ax = Math.Min(ix + 1, t.W - 1),
                        ay = Math.Min(iy + 1, t.H - 1);
                    float fx = su - ix,
                        fy = sv - iy;
                    int p00 = (iy * t.W + ix) * 4,
                        p10 = (iy * t.W + ax) * 4,
                        p01 = (ay * t.W + ix) * 4,
                        p11 = (ay * t.W + ax) * 4;
                    float q00 = (1 - fx) * (1 - fy),
                        q10 = fx * (1 - fy),
                        q01 = (1 - fx) * fy,
                        q11 = fx * fy;
                    var p = t.P;
                    int a = (int)(p[p00 + 3] * q00 + p[p10 + 3] * q10 + p[p01 + 3] * q01 + p[p11 + 3] * q11 + .5f);
                    if (a == 0)
                        continue;
                    int r = (int)(p[p00] * q00 + p[p10] * q10 + p[p01] * q01 + p[p11] * q11 + .5f);
                    int g = (int)(p[p00 + 1] * q00 + p[p10 + 1] * q10 + p[p01 + 1] * q01 + p[p11 + 1] * q11 + .5f);
                    int b = (int)(p[p00 + 2] * q00 + p[p10 + 2] * q10 + p[p01 + 2] * q01 + p[p11 + 2] * q11 + .5f);
                    stamp.Pixels[(py - y0) * stamp.W + px - x0] = (uint)(r | (g << 8) | (b << 16) | (a << 24));
                }
            }

            return stamp;
        }
    }
}
