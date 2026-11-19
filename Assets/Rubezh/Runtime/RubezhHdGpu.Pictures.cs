using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        sealed class PicturePiece
        {
            public ProceduralPicture Image;
            public int X,
                Y,
                W,
                H,
                Page,
                AX,
                AY;
        }

        sealed class PictureGroup
        {
            public readonly List<ProceduralPicture> Images = new List<ProceduralPicture>();
            public readonly List<PicturePiece> Pieces = new List<PicturePiece>();
            public readonly List<int> Pages = new List<int>();
            public float L = float.MaxValue,
                T = float.MaxValue,
                R = float.MinValue,
                B = float.MinValue;
            public long Bytes;
            public int LastUsed,
                NextPage;
            public bool Wanted;
            public AtlasLayout.Layout Plan;
        }

        readonly Dictionary<long, PictureGroup> pictureGroups = new Dictionary<long, PictureGroup>();
        readonly Dictionary<ProceduralPicture, List<PicturePiece>> picturePieces =
            new Dictionary<ProceduralPicture, List<PicturePiece>>();
        readonly Dictionary<int, Texture2D> pictureDepthPages = new Dictionary<int, Texture2D>();
        readonly Stack<int> pictureFreePages = new Stack<int>();
        public long PictureFullUploads,
            PictureRegionUploads,
            PictureUploadedBytes;
        public double PictureUploadMs,
            PictureIndexMs,
            PictureFrameUploadMs,
            MaxPicturePageMs;
        public int PictureEvictions,
            PicturePagesUploaded,
            PictureDemandPages,
            PicturePrefetchPages;
        readonly List<PictureGroup> pictureLru = new List<PictureGroup>();
        bool pictureMemoryPressure;

        public void TrimRenderCache()
        {
            pictureMemoryPressure = true;
        }

        int pictureFrame;
        long pictureBytes;
        ProceduralPicture[] pictureSet;
        public long ProceduralGpuBytes => pictureBytes;
        public int ProceduralGpuImages => picturePieces.Count;

        void DropGroup(PictureGroup g)
        {
            foreach (int page in g.Pages)
            {
                UnityEngine.Object.Destroy(atlases[page]);
                UnityEngine.Object.Destroy(pictureDepthPages[page]);
                UnityEngine.Object.Destroy(materials[page]);
                atlases[page] = null;
                materials[page] = null;
                pictureDepthPages.Remove(page);
                pictureFreePages.Push(page);
            }
            foreach (var p in g.Images)
                picturePieces.Remove(p);
            pictureBytes -= g.Bytes;
            g.Bytes = 0;
            g.Pages.Clear();
            g.Pieces.Clear();
            g.Plan = null;
            g.NextPage = 0;
        }

        void ReleasePictures()
        {
            ReleaseNativeBuildings();
            foreach (var g in pictureGroups.Values)
                DropGroup(g);
            pictureGroups.Clear();
            pictureSet = null;
            pictureFreePages.Clear();
        }

        void IndexPictures(ProceduralPicture[] pictures)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var next = new Dictionary<long, PictureGroup>();
            if (pictures != null)
                foreach (var p in pictures)
                {
                    float px = p.Left + p.Width * .5f,
                        py = p.Top + p.Height * .5f;
                    int x = (int)Math.Floor((px / 22 - py / 10) / 10),
                        y = (int)Math.Floor((px / 22 + py / 10) / 10);
                    long key = DynamicBuilding.Key(x, y);
                    PictureGroup g;
                    if (!next.TryGetValue(key, out g))
                        next[key] = g = new PictureGroup();
                    g.Images.Add(p);
                    g.L = Math.Min(g.L, p.Left);
                    g.T = Math.Min(g.T, p.Top);
                    g.R = Math.Max(g.R, p.Left + p.Width);
                    g.B = Math.Max(g.B, p.Top + p.Height);
                }
            foreach (var old in pictureGroups)
            {
                PictureGroup n;
                bool compatible = next.TryGetValue(old.Key, out n) && n.Images.Count == old.Value.Images.Count;
                if (compatible)
                    for (int i = 0; i < n.Images.Count; i++)
                    {
                        var a = old.Value.Images[i];
                        var b = n.Images[i];
                        if (
                            a.PixelWidth != b.PixelWidth
                            || a.PixelHeight != b.PixelHeight
                            || a.Left != b.Left
                            || a.Top != b.Top
                            || a.Width != b.Width
                            || a.Height != b.Height
                        )
                        {
                            compatible = false;
                            break;
                        }
                    }
#if UNITY_5_3_OR_NEWER || RUBEZH_PICTURE_CONTRACT
                if (compatible && (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) == 0)
                    for (int i = 0; i < n.Images.Count; i++)
                        if (!ReferenceEquals(n.Images[i], old.Value.Images[i]))
                        {
                            compatible = false;
                            break;
                        }
#endif
                if (compatible)
                {
                    var g = old.Value;
                    for (int i = 0; i < n.Images.Count; i++)
                        if (!ReferenceEquals(g.Images[i], n.Images[i]))
                        {
                            var before = g.Images[i];
                            var after = n.Images[i];
                            List<PicturePiece> pieces;
                            if (picturePieces.TryGetValue(before, out pieces))
                            {
                                foreach (var piece in pieces)
                                {
                                    piece.Image = after;
                                    UpdatePiece(piece);
                                }
                                picturePieces.Remove(before);
                                picturePieces[after] = pieces;
                            }
                            foreach (var pending in g.Pieces)
                                if (ReferenceEquals(pending.Image, before))
                                    pending.Image = after;
                            g.Images[i] = after;
                        }
                    next[old.Key] = g;
                }
                else
                    DropGroup(old.Value);
            }
            pictureGroups.Clear();
            foreach (var pair in next)
                pictureGroups.Add(pair.Key, pair.Value);
            pictureSet = pictures;
            PictureIndexMs = watch.Elapsed.TotalMilliseconds;
        }

        void UploadGroup(PictureGroup g, bool onePage = false)
        {
            if (g.Plan != null && g.NextPage == g.Plan.Pages.Length)
                return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            if (g.Plan == null)
            {
                PictureFullUploads++;
                var sizes = new List<AtlasLayout.Size>();
                int tile = AtlasLayout.ProceduralTileSide(SystemInfo.maxTextureSize);
                if (tile < 1)
                    throw new NotSupportedException("Texture limit too small");
                foreach (var p in g.Images)
                    for (int y = 0; y < p.PixelHeight; y += tile)
                    for (int x = 0; x < p.PixelWidth; x += tile)
                    {
                        var piece = new PicturePiece
                        {
                            Image = p,
                            X = x,
                            Y = y,
                            W = Math.Min(tile, p.PixelWidth - x),
                            H = Math.Min(tile, p.PixelHeight - y),
                        };
                        g.Pieces.Add(piece);
                        sizes.Add(new AtlasLayout.Size(piece.W, piece.H));
                    }
                g.Plan = AtlasLayout.Pack(sizes, Math.Min(1024, SystemInfo.maxTextureSize));
            }
            var pack = g.Plan;
            int stop = onePage ? Math.Min(pack.Pages.Length, g.NextPage + 1) : pack.Pages.Length;
            for (int page = g.NextPage; page < stop; page++)
            {
                var pageWatch = System.Diagnostics.Stopwatch.StartNew();
                var size = pack.Pages[page];
                var color = new Texture2D(size.W, size.H, TextureFormat.RGBA32, false, true);
                var depth = new Texture2D(size.W, size.H, TextureFormat.RFloat, false, true);
#if UNITY_5_3_OR_NEWER || RUBEZH_PICTURE_CONTRACT
                var rgba = color.GetPixelData<uint>(0);
                var depthsData = depth.GetPixelData<float>(0);
                for (int k = 0; k < rgba.Length; k++)
                {
                    rgba[k] = 0;
                    depthsData[k] = 0;
                }
#else
                var rgba = new uint[size.W * size.H];
                var depthsData = new float[rgba.Length];
#endif
                for (int i = 0; i < g.Pieces.Count; i++)
                {
                    var r = pack.Items[i];
                    if (r.Page != page)
                        continue;
                    var piece = g.Pieces[i];
                    piece.AX = r.X;
                    piece.AY = r.Y;
                    for (int y = -1; y <= piece.H; y++)
                    for (int x = -1; x <= piece.W; x++)
                    {
                        int sx = piece.X + Math.Max(0, Math.Min(piece.W - 1, x)),
                            sy = piece.Y + Math.Max(0, Math.Min(piece.H - 1, y)),
                            src = sy * piece.Image.PixelWidth + sx,
                            dst = (r.Y + y) * size.W + r.X + x;
                        uint c = piece.Image.Pixels[src],
                            a = c >> 24;
                        rgba[dst] =
                            (a << 24)
                            | ((c & 255) * a / 255)
                            | ((((c >> 8) & 255) * a / 255) << 8)
                            | ((((c >> 16) & 255) * a / 255) << 16);
                        depthsData[dst] = piece.Image.Depth[src];
                    }
                }
                // A white primitive texel is reserved by AtlasLayout, also on procedural pages.
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    rgba[y * size.W + x] = 0xffffffff;

#if !UNITY_5_3_OR_NEWER && !RUBEZH_PICTURE_CONTRACT
                color.SetPixelData(rgba, 0);
                depth.SetPixelData(depthsData, 0);
#endif
                color.filterMode = FilterMode.Bilinear;
                depth.filterMode = FilterMode.Point;
                color.wrapMode = depth.wrapMode = TextureWrapMode.Clamp;
                color.Apply(false, true);
                depth.Apply(false, true);
                var mat = new Material(shader);
                mat.SetTexture("_MainTex", color);
                mat.SetTexture("_PictureDepth", depth);
                mat.SetTexture("_Occlusion", occlusion);
                mat.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
                int index;
                if (pictureFreePages.Count > 0)
                {
                    index = pictureFreePages.Pop();
                    atlases[index] = color;
                    materials[index] = mat;
                }
                else
                {
                    index = atlases.Count;
                    atlases.Add(color);
                    materials.Add(mat);
                }
                pictureDepthPages[index] = depth;
                g.Pages.Add(index);
                long bytes = (long)size.W * size.H * 8;
                g.Bytes += bytes;
                pictureBytes += bytes;
                PictureUploadedBytes += bytes;
                for (int i = 0; i < g.Pieces.Count; i++)
                    if (pack.Items[i].Page == page)
                    {
                        var piece = g.Pieces[i];
                        piece.Page = index;
                        List<PicturePiece> list;
                        if (!picturePieces.TryGetValue(piece.Image, out list))
                            picturePieces[piece.Image] = list = new List<PicturePiece>();
                        list.Add(piece);
                    }
                g.NextPage = page + 1;
                PicturePagesUploaded++;
                MaxPicturePageMs = Math.Max(MaxPicturePageMs, pageWatch.Elapsed.TotalMilliseconds);
            }
            PictureUploadMs = watch.Elapsed.TotalMilliseconds;
            PictureFrameUploadMs += PictureUploadMs;
        }

        // Only the modified sprite rectangle is copied into the existing GPU atlas.
        void UpdatePiece(PicturePiece piece)
        {
            PictureRegionUploads++;
            PictureUploadedBytes += (long)(piece.W + 2) * (piece.H + 2) * 8;
#if UNITY_5_3_OR_NEWER || RUBEZH_PICTURE_CONTRACT
            int width = piece.W + 2,
                height = piece.H + 2;
            var color = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var depth = new Texture2D(width, height, TextureFormat.RFloat, false, true);
            try
            {
                var rgba = color.GetPixelData<uint>(0);
                var dz = depth.GetPixelData<float>(0);
                for (int y = -1; y <= piece.H; y++)
                for (int x = -1; x <= piece.W; x++)
                {
                    int sx = piece.X + Math.Max(0, Math.Min(piece.W - 1, x)),
                        sy = piece.Y + Math.Max(0, Math.Min(piece.H - 1, y));
                    int src = sy * piece.Image.PixelWidth + sx,
                        dst = (y + 1) * width + x + 1;
                    uint c = piece.Image.Pixels[src],
                        a = c >> 24;
                    rgba[dst] =
                        (a << 24)
                        | ((c & 255) * a / 255)
                        | ((((c >> 8) & 255) * a / 255) << 8)
                        | ((((c >> 16) & 255) * a / 255) << 16);
                    dz[dst] = piece.Image.Depth[src];
                }
                color.Apply(false, true);
                depth.Apply(false, true);
                UnityEngine.Graphics.CopyTexture(
                    color,
                    0,
                    0,
                    0,
                    0,
                    width,
                    height,
                    atlases[piece.Page],
                    0,
                    0,
                    piece.AX - 1,
                    piece.AY - 1
                );
                UnityEngine.Graphics.CopyTexture(
                    depth,
                    0,
                    0,
                    0,
                    0,
                    width,
                    height,
                    pictureDepthPages[piece.Page],
                    0,
                    0,
                    piece.AX - 1,
                    piece.AY - 1
                );
            }
            finally
            {
                UnityEngine.Object.Destroy(color);
                UnityEngine.Object.Destroy(depth);
            }
#endif
        }

        bool PictureNear(PictureGroup g, float ox, float oy, float scale, float margin) =>
            ox + g.R * scale >= visiblePixels.xMin - margin
            && ox + g.L * scale <= visiblePixels.xMax + margin
            && oy + g.B * scale >= visiblePixels.yMin - margin
            && oy + g.T * scale <= visiblePixels.yMax + margin;

        public void Pictures(ProceduralPicture[] pictures, float ox, float oy, float scale)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            NativePictures(pictures, ox, oy, scale);
#else
            if (!ReferenceEquals(pictureSet, pictures))
                IndexPictures(pictures);
            if (pictures == null)
                return;
            PictureFrameUploadMs = 0;
            foreach (var g in pictureGroups.Values)
            {
                g.Wanted = PictureNear(g, ox, oy, scale, 32 * scale);
                if (g.Wanted)
                {
                    g.LastUsed = pictureFrame;
                    int before = PicturePagesUploaded;
                    UploadGroup(g);
                    PictureDemandPages += PicturePagesUploaded - before;
                }
            }
            // Retain recently used pages instead of deleting them merely for leaving the viewport.
            // Do not evict visible groups; a cold camera jump completes them synchronously,
            // so loading budgets never expose transparent holes in a building.
            long spare = 0;
            pictureLru.Clear();
            foreach (var g in pictureGroups.Values)
                if (!g.Wanted)
                {
                    spare += g.Bytes;
                    if (g.Bytes > 0)
                        pictureLru.Add(g);
                }
            pictureLru.Sort((a, b) => a.LastUsed.CompareTo(b.LastUsed));
            long budget = pictureMemoryPressure ? 0 : 64L * 1024 * 1024;
            foreach (var g in pictureLru)
            {
                if (spare <= budget)
                    break;
                spare -= g.Bytes;
                DropGroup(g);
                PictureEvictions++;
            }
            if (!pictureMemoryPressure && PictureFrameUploadMs < 3 && spare < budget - 8L * 1024 * 1024)
            {
                PictureGroup nearest = null;
                float best = float.MaxValue;
                foreach (var g in pictureGroups.Values)
                {
                    if (
                        g.Wanted
                        || (g.Plan != null && g.NextPage == g.Plan.Pages.Length)
                        || !PictureNear(g, ox, oy, scale, 192 * scale)
                    )
                        continue;
                    float dx = ox + (g.L + g.R) * .5f * scale - visiblePixels.center.x,
                        dy = oy + (g.T + g.B) * .5f * scale - visiblePixels.center.y;
                    float distance = dx * dx + dy * dy;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = g;
                    }
                }
                if (nearest != null)
                {
                    int before = PicturePagesUploaded;
                    UploadGroup(nearest, true);
                    nearest.LastUsed = pictureFrame;
                    PicturePrefetchPages += PicturePagesUploaded - before;
                }
            }
            foreach (var p in pictures)
            {
                List<PicturePiece> pieces;
                if (!picturePieces.TryGetValue(p, out pieces))
                    continue;
                foreach (var piece in pieces)
                {
                    float l = ox + (p.Left + piece.X * p.Width / p.PixelWidth) * scale,
                        t = oy + (p.Top + piece.Y * p.Height / p.PixelHeight) * scale,
                        r = l + piece.W * p.Width / p.PixelWidth * scale,
                        b = t + piece.H * p.Height / p.PixelHeight * scale;
                    var atlas = atlases[piece.Page];
                    int start = depths.Count;
                    Quad(
                        l,
                        t,
                        r,
                        t,
                        r,
                        b,
                        l,
                        b,
                        (float)piece.AX / atlas.width,
                        (float)piece.AY / atlas.height,
                        (float)(piece.AX + piece.W) / atlas.width,
                        (float)(piece.AY + piece.H) / atlas.height,
                        new Color32(255, 255, 255, 255),
                        0,
                        true,
                        piece.Page
                    );
                    for (int i = start; i < depths.Count; i++)
                        depths[i] = new Vector2(0, 2);
                }
            }
#endif
        }
    }
}
