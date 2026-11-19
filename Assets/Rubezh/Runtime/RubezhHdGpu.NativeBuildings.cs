using System;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        sealed class BuildingPage
        {
            public NativeBuildingAssets.Page Source;
            public int Page,
                MaskWidth;
            public Texture2D Mask,
                Meta;
            public byte[] MaskBytes;
            public float[] MetaFloats;
            public readonly Dictionary<DynamicBuilding.Wall, Vector2Int> MaskOrigins =
                new Dictionary<DynamicBuilding.Wall, Vector2Int>();
            public readonly Dictionary<DynamicBuilding.Wall, int> Revisions =
                new Dictionary<DynamicBuilding.Wall, int>();
            public readonly Dictionary<NativeBuildingRegion, int> RegionRows =
                new Dictionary<NativeBuildingRegion, int>();
        }

        readonly Dictionary<NativeBuildingAssets.Page, BuildingPage> buildingPages =
            new Dictionary<NativeBuildingAssets.Page, BuildingPage>();
        readonly Dictionary<string, ProceduralPicture> buildingPictures = new Dictionary<string, ProceduralPicture>();
        public int NativeBuildingMaskUpdates { get; private set; }
        public long NativeBuildingMaskBytes
        {
            get
            {
                long bytes = 0;
                foreach (var page in buildingPages.Values)
                    bytes += page.MaskBytes.LongLength + page.MetaFloats.LongLength * 4;
                return bytes;
            }
        }

        static int PowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
                result *= 2;
            return result;
        }

        void ReleaseNativeBuildings()
        {
            foreach (var page in buildingPages.Values)
            {
                UnityEngine.Object.Destroy(page.Mask);
                UnityEngine.Object.Destroy(page.Meta);
                UnityEngine.Object.Destroy(materials[page.Page]);
                materials[page.Page] = null;
                atlases[page.Page] = null;
                pictureDepthPages.Remove(page.Page);
            }
            buildingPages.Clear();
            buildingPictures.Clear();
        }

        BuildingPage BuildingAtlas(NativeBuildingAssets.Page source)
        {
            BuildingPage result;
            if (buildingPages.TryGetValue(source, out result))
                return result;
            result = new BuildingPage { Source = source };
            int limit = SystemInfo.maxTextureSize;
            if (source.Regions.Count > Math.Min(65535, limit))
                throw new InvalidOperationException("Building metadata exceeds texture limit");
            int x = 0,
                y = 1,
                columnWidth = 0,
                usedHeight = 1;
            foreach (var region in source.Regions)
            {
                var wall = buildingPictures[region.key].NativeWall;
                if (wall == null || result.MaskOrigins.ContainsKey(wall))
                    continue;
                if (wall.NX <= 0 || wall.NZ <= 0 || wall.NX > limit || wall.NZ > limit)
                    throw new InvalidOperationException("Invalid building damage grid");
                if (y + wall.NZ > limit)
                {
                    x += columnWidth;
                    y = 0;
                    columnWidth = 0;
                }
                if (x + wall.NX > limit)
                    throw new InvalidOperationException("Building damage masks exceed texture limit");
                result.MaskOrigins.Add(wall, new Vector2Int(x, y));
                columnWidth = Math.Max(columnWidth, wall.NX);
                y += wall.NZ;
                usedHeight = Math.Max(usedHeight, y);
            }
            int width = Math.Min(limit, PowerOfTwo(Math.Max(1, x + columnWidth)));
            int height = Math.Min(limit, PowerOfTwo(usedHeight));
            result.MaskWidth = width;
            result.Mask = new Texture2D(width, height, TextureFormat.R8, false, true);
            result.MaskBytes = new byte[width * height];
            result.Mask.filterMode = FilterMode.Point;
            result.Mask.wrapMode = TextureWrapMode.Clamp;
            result.Meta = new Texture2D(8, Math.Max(1, source.Regions.Count), TextureFormat.RGBAFloat, false, true);
            result.MetaFloats = new float[8 * Math.Max(1, source.Regions.Count) * 4];
            result.Meta.filterMode = FilterMode.Point;
            result.Meta.wrapMode = TextureWrapMode.Clamp;
            int index = 0;
            foreach (var region in source.Regions)
            {
                result.RegionRows.Add(region, index);
                var p = buildingPictures[region.key];
                var w = p.NativeWall;
                int offset = index * 32;
                var f = result.MetaFloats;
                f[offset] = p.Left + region.px * p.Width / p.PixelWidth;
                f[offset + 1] = p.Top + region.py * p.Height / p.PixelHeight;
                f[offset + 2] = region.w * p.Width / p.PixelWidth;
                f[offset + 3] = region.h * p.Height / p.PixelHeight;
                if (w != null)
                {
                    var s = w.Source;
                    f[offset + 4] = s.vax;
                    f[offset + 5] = s.vay;
                    f[offset + 6] = s.vbx;
                    f[offset + 7] = s.vby;
                    f[offset + 8] = s.height;
                    f[offset + 9] = s.windowLo;
                    f[offset + 10] = s.windowHi;
                    f[offset + 12] = s.sill;
                    f[offset + 13] = s.lintel;
                    var origin = result.MaskOrigins[w];
                    f[offset + 14] = origin.x;
                    f[offset + 15] = origin.y;
                    f[offset + 16] = w.NX;
                    f[offset + 17] = w.NZ;
                    f[offset + 18] = width;
                    f[offset + 19] = height;
                }
                f[offset + 20] = (float)region.x / source.Color.width;
                f[offset + 21] = (float)region.y / source.Color.height;
                f[offset + 22] = (float)region.w / source.Color.width;
                f[offset + 23] = (float)region.h / source.Color.height;
                f[offset + 24] = region.w;
                f[offset + 25] = region.h;
                f[offset + 26] = w != null ? 1 : 0;
                f[offset + 27] = w != null && w.Source.window ? 1 : 0;
                index++;
            }
            var material = new Material(shader);
            material.SetTexture("_MainTex", source.Color);
            material.SetTexture("_PictureDepth", source.Depth);
            material.SetTexture("_BuildingMask", result.Mask);
            material.SetTexture("_BuildingMeta", result.Meta);
            material.SetFloat("_BuildingMode", 1);
            material.SetFloat("_BuildingRows", Math.Max(1, source.Regions.Count));
            material.SetTexture("_Occlusion", occlusion);
            material.SetVector("_CanvasSize", new Vector4(canvas.Width, canvas.Height, 0, 0));
            result.Page = atlases.Count;
            atlases.Add(source.Color);
            materials.Add(material);
            pictureDepthPages[result.Page] = source.Depth;
            buildingPages.Add(source, result);
            UpdateBuildingMask(result, true);
            return result;
        }

        void UpdateBuildingMask(BuildingPage page, bool force = false)
        {
            bool changed = force;
            foreach (var pair in page.MaskOrigins)
            {
                var wall = pair.Key;
                int previous;
                if (!force && page.Revisions.TryGetValue(wall, out previous) && previous == wall.Revision)
                    continue;
                for (int z = 0; z < wall.NZ; z++)
                for (int x = 0; x < wall.NX; x++)
                    page.MaskBytes[(pair.Value.y + z) * page.MaskWidth + pair.Value.x + x] = BuildingDamageMask.Removed(
                        wall,
                        x,
                        z
                    )
                        ? (byte)255
                        : (byte)0;
                page.Revisions[wall] = wall.Revision;
                changed = true;
            }
            if (!changed)
                return;
            foreach (var region in page.Source.Regions)
            {
                var wall = buildingPictures[region.key].NativeWall;
                if (wall != null)
                {
                    int offset = page.RegionRows[region] * 32;
                    page.MetaFloats[offset + 11] = wall.GlassBroken ? 1 : 0;
                    page.MetaFloats[offset + 26] = wall.Revision > 0 ? 1 : 0;
                }
            }
            page.Mask.SetPixelData(page.MaskBytes, 0);
            page.Mask.Apply(false, false);
            page.Meta.SetPixelData(page.MetaFloats, 0);
            page.Meta.Apply(false, false);
            NativeBuildingMaskUpdates++;
            depthDirty = true;
        }

        void NativePictures(ProceduralPicture[] pictures, float ox, float oy, float scale)
        {
            if (pictures == null)
                return;
            foreach (var p in pictures)
                if (!string.IsNullOrEmpty(p.NativeKey))
                    buildingPictures[p.NativeKey] = p;
            foreach (var p in pictures)
            {
                if (p.NativeFaces != null)
                {
                    uint c = p.NativeColor;
                    var tint = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24));
                    float u = 1.5f / atlases[0].width,
                        v = 1.5f / atlases[0].height;
                    foreach (var face in p.NativeFaces)
                    {
                        int start = depths.Count;
                        Quad(
                            ox + face[0] * scale,
                            oy + face[1] * scale,
                            ox + face[3] * scale,
                            oy + face[4] * scale,
                            ox + face[6] * scale,
                            oy + face[7] * scale,
                            ox + face[9] * scale,
                            oy + face[10] * scale,
                            u,
                            v,
                            u,
                            v,
                            tint,
                            face[2],
                            true,
                            0
                        );
                        for (int i = start; i < depths.Count; i++)
                            depths[i] = new Vector2(face[(i - start) * 3 + 2], 1);
                    }
                    continue;
                }
                if (string.IsNullOrEmpty(p.NativeKey))
                    throw new InvalidOperationException("Unprepared CPU building picture in native battle");
                foreach (var region in NativeBuildingAssets.Get(p.NativeKey))
                {
                    var page = BuildingAtlas(region.Page);
                    var r = region.Data;
                    int row = page.RegionRows[r];
                    float left = ox + (p.Left + r.px * p.Width / p.PixelWidth) * scale,
                        top = oy + (p.Top + r.py * p.Height / p.PixelHeight) * scale;
                    float right = left + r.w * p.Width / p.PixelWidth * scale,
                        bottom = top + r.h * p.Height / p.PixelHeight * scale;
                    var atlas = region.Page.Color;
                    int start = depths.Count;
                    Quad(
                        left,
                        top,
                        right,
                        top,
                        right,
                        bottom,
                        left,
                        bottom,
                        (float)r.x / atlas.width,
                        (float)r.y / atlas.height,
                        (float)(r.x + r.w) / atlas.width,
                        (float)(r.y + r.h) / atlas.height,
                        new Color32((byte)row, (byte)(row >> 8), 255, 255),
                        0,
                        true,
                        page.Page
                    );
                    for (int i = start; i < depths.Count; i++)
                        depths[i] = new Vector2(0, 2);
                }
            }
            foreach (var page in buildingPages.Values)
                UpdateBuildingMask(page);
        }
    }
}
