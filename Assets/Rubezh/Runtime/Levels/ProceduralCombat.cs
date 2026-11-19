using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class ProceduralVisualData
    {
        public float left,
            top,
            width,
            height;
        public string png,
            depthPng;
        public string nativeKey;
        public int pixelWidth,
            pixelHeight;
        public DrawingOp[] recipe;
        public DrawingClip[] clipTable;

        public ProceduralPicture Decode()
        {
            if (!string.IsNullOrEmpty(nativeKey))
                return new ProceduralPicture
                {
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    PixelWidth = pixelWidth,
                    PixelHeight = pixelHeight,
                    NativeKey = nativeKey,
                };
#if UNITY_5_3_OR_NEWER && !UNITY_EDITOR
            throw new InvalidOperationException(
                "Procedural building is not prepared as Unity assets. Run native content preparation."
            );
#else
            if (recipe != null)
                return BuildingRecipe.Render(this);
            var color = new UnityEngine.Texture2D(2, 2);
            var depth = new UnityEngine.Texture2D(2, 2);
            try
            {
                if (
                    !UnityEngine.ImageConversion.LoadImage(color, Convert.FromBase64String(png))
                    || !UnityEngine.ImageConversion.LoadImage(depth, Convert.FromBase64String(depthPng))
                    || color.width != depth.width
                    || color.height != depth.height
                )
                    throw new Exception("Некорректное изображение процедурного дома");
                var c = color.GetPixels32();
                var d = depth.GetPixels32();
                // Keep twice the maximum game raster resolution, not a full editing canvas per house.
                int pw = Math.Min(color.width, Math.Max(1, (int)Math.Ceiling(width * 4.8f))),
                    ph = Math.Min(color.height, Math.Max(1, (int)Math.Ceiling(height * 4.8f)));
                var pixels = new uint[pw * ph];
                var z = new float[pixels.Length];
                for (int y = 0; y < ph; y++)
                for (int x = 0; x < pw; x++)
                {
                    int sx = Math.Min(color.width - 1, (int)((x + .5f) * color.width / pw)),
                        sy = Math.Min(color.height - 1, (int)((y + .5f) * color.height / ph));
                    int i = y * pw + x,
                        j = (color.height - 1 - sy) * color.width + sx;
                    pixels[i] = (uint)(c[j].r | (c[j].g << 8) | (c[j].b << 16) | (c[j].a << 24));
                    z[i] = (d[j].r + (d[j].g << 8)) / 64f;
                }
                return new ProceduralPicture
                {
                    Left = left,
                    Top = top,
                    Width = width,
                    Height = height,
                    PixelWidth = pw,
                    PixelHeight = ph,
                    Pixels = pixels,
                    Depth = z,
                };
            }
            finally
            {
                UnityEngine.Object.Destroy(color);
                UnityEngine.Object.Destroy(depth);
            }
#endif
        }
    }

    [Serializable]
    public sealed class ProceduralCombatCell
    {
        public int x,
            y;
        public float height,
            protection;
        public bool walk,
            shoot;
        public string kind;
    }

    // Static geometry from the editor, not a second random damage generator.
    public static class ProceduralCombat
    {
        public static bool Valid(RubezhLevel level, out string error)
        {
            error = "";
            if (level.proceduralCombatVersion == 0)
                return true;
            if (level.proceduralCombatVersion != 1 || level.proceduralCombatCells == null)
            {
                error = "Неподдерживаемый формат процедурных укрытий";
                return false;
            }
            var seen = new HashSet<int>();
            foreach (var c in level.proceduralCombatCells)
            {
                if (
                    c == null
                    || !level.Inside(c.x, c.y)
                    || !seen.Add(c.y * level.columns + c.x)
                    || float.IsNaN(c.height)
                    || float.IsInfinity(c.height)
                    || c.height < 0
                    || c.height > 10
                    || float.IsNaN(c.protection)
                    || float.IsInfinity(c.protection)
                    || c.protection < 0
                    || c.protection > .75f
                    || c.walk && (!c.shoot || c.protection != 0)
                )
                {
                    error = "Некорректная клетка процедурной стены";
                    return false;
                }
            }
            foreach (var list in new[] { level.proceduralRoofArea, level.proceduralRoofCells })
                if (list != null)
                    foreach (var c in list)
                        if (c == null || !level.Inside(c.x, c.y))
                        {
                            error = "Процедурная крыша за границей карты";
                            return false;
                        }
            if (level.proceduralVisuals != null)
                foreach (var v in level.proceduralVisuals)
                    if (
                        v == null
                        || float.IsNaN(v.left)
                        || float.IsInfinity(v.left)
                        || float.IsNaN(v.top)
                        || float.IsInfinity(v.top)
                        || float.IsNaN(v.width)
                        || float.IsInfinity(v.width)
                        || v.width <= 0
                        || float.IsNaN(v.height)
                        || float.IsInfinity(v.height)
                        || v.height <= 0
                        || (
                            string.IsNullOrEmpty(v.nativeKey)
                            && (string.IsNullOrEmpty(v.png) || string.IsNullOrEmpty(v.depthPng))
                        )
                    )
                    {
                        error = "Некорректное изображение процедурного здания";
                        return false;
                    }
            try
            {
                if (level.dynamicBuildings != null)
                    foreach (var h in level.dynamicBuildings)
                    {
                        if (h == null)
                            throw new ArgumentException("Пустой дом");
                        h.Validate();
                    }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            return true;
        }

        public static void Apply(BattleMap map, RubezhLevel level)
        {
            string error;
            if (!Valid(level, out error))
                throw new ArgumentException(error);
            ExteriorSpriteLoader.Initialize();
            int count = map.Columns * map.Rows;
            // Restore authored tile surfaces; do not decode or blur the procedural terrain overlay.
            map.TerrainPicture = null;
            map.DynamicBuildings = null;
            map.ProceduralPictures = null;
            map.ProceduralCells = new bool[count];
            map.ProceduralProtection = new float[count];
            map.ProceduralHeight = new float[count];
            if (level.proceduralCombatVersion != 1 || level.proceduralCombatCells == null)
                return;
            map.ProceduralPictures = null;
            if (
                (level.dynamicBuildings == null || level.dynamicBuildings.Count == 0)
                && level.proceduralVisuals != null
                && level.proceduralVisuals.Count > 0
            )
            {
                var pictures = new List<ProceduralPicture>();
                foreach (var v in level.proceduralVisuals)
                    pictures.Add(v.Decode());
                map.ProceduralPictures = pictures.ToArray();
            }
            if (level.dynamicBuildings != null && level.dynamicBuildings.Count > 0)
            {
                BuildingRecipe.BeginMap();
                map.DynamicBuildings = new List<DynamicBuilding>();
                var pictures = new List<ProceduralPicture>();
                foreach (var data in level.dynamicBuildings)
                {
                    var house = data.Decode();
                    map.DynamicBuildings.Add(house);
                    pictures.AddRange(house.Pictures);
                }
                map.ProceduralPictures = pictures.ToArray();
            }
            var ids = new HashSet<string>(level.proceduralWallIds ?? new List<string>());
            // The baked house replaces only the exact source wall instances.
            if (map.ProceduralPictures != null)
            {
                map.Studio.Objects.RemoveAll(o => ids.Contains(o.Uid));
                for (int i = 0; i < count; i++)
                    if (map.Studio.Cells[i] != null && ids.Contains(map.Studio.Cells[i].Uid))
                    {
                        map.Studio.Cells[i] = null;
                        map.Tiles[i] = Tile.Ground;
                    }
            }
            foreach (var c in level.proceduralCombatCells)
            {
                int i = c.y * map.Columns + c.x;
                var other = StudioRules.At(map, i);
                // Never erase a car, crate or unrelated wall under the generated house.
                if (other != null && other.Asset.Blocking && !ids.Contains(other.Uid))
                    continue;
                map.ProceduralCells[i] = true;
                map.ProceduralProtection[i] = c.protection;
                map.ProceduralHeight[i] = c.height;
                map.Tiles[i] =
                    c.walk ? Tile.Ground
                    : c.shoot ? Tile.LowCover
                    : Tile.Wall;
                map.CoverHealth[i] = 100;
            }
            if (level.proceduralRoofArea != null)
                foreach (var c in level.proceduralRoofArea)
                    map.Roof[c.y * map.Columns + c.x] = false;
            if (level.proceduralRoofCells != null)
                foreach (var c in level.proceduralRoofCells)
                    map.Roof[c.y * map.Columns + c.x] = true;
        }
    }
}
