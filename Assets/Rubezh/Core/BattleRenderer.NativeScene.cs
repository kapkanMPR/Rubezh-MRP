using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    public interface IReusableSceneGpu { bool ReuseScene(BattleMap map); }

    public interface IStaticSceneGpu
    {
        bool NativeScene { get; }
        void BeginScene(BattleMap map);
        bool CachedObject(int id, int clipLeft, int clipRight);
        void CacheObject(int id, int clipLeft, int clipRight, bool blocksRoof, bool exterior, Action draw);
        void EndScene();
        void VehicleDepth(Action draw);
        void RoofMesh(BattleMap map, int revision, Action draw);
        void ExteriorImage(ExteriorSprite sprite, float x, float y, float scale);
    }

    public abstract partial class BattleVisualRecipes
    {
        BattleMap nativeMap;
        int nativeRevision = -1;
        ExteriorLayout nativeExterior;
        readonly List<ExteriorProp> nativeExteriorProps = new List<ExteriorProp>();
        ProceduralPicture[] nativePictures;
        Action drawVehicleDepth;
        BattleSimulation vehicleDepthBattle;

        protected void PrepareNativeScene(BattleMap map)
        {
            activeMap = map;
            if (nativeMap == map && nativeRevision == map.Revision) return;
            bool newMap = nativeMap != map;
            if (newMap)
            {
                // CPU compatibility buffers are allocated only when that path is used.
                basePixels = sharpGround = scenery = null;
                sceneryDepth = null;
                Canvas.Occlusion = Canvas.RoofOcclusion = null;
                Canvas.ReleasePixels();
                cachedMap = null; cachedRevision = -1;
            }
            BuildSceneryItems(map);
            RememberBounds(map);
            RememberChunkState(map);
            LastSceneryPixels = 0;
            nativePictures = map.ProceduralPictures;
            if (map.TerrainPicture != null)
            {
                nativePictures = new ProceduralPicture[1 + (map.ProceduralPictures?.Length ?? 0)];
                nativePictures[0] = map.TerrainPicture;
                if (map.ProceduralPictures != null)
                    Array.Copy(map.ProceduralPictures, 0, nativePictures, 1, map.ProceduralPictures.Length);
            }
            nativeMap = map;
            nativeRevision = map.Revision;
            if (newMap) BuildNativeExterior(map);
        }

        protected void RenderNativeScene(BattleMap map, IStaticSceneGpu gpu)
        {
            var reusable=gpu as IReusableSceneGpu;
            if(reusable!=null && reusable.ReuseScene(map)) { Canvas.TestDepth=true; return; }
            gpu.BeginScene(map);
            if (drawGroundCells == null) drawGroundCells = DrawGroundCells;
            ((IGroundChunkGpu)gpu).GroundChunks(map, drawGroundCells);
            var view = Canvas.Gpu as ICanvasViewportGpu;
            for (int i = 0; i < nativeExteriorProps.Count; i++)
            {
                var prop = nativeExteriorProps[i];
                var p = P(new V2(prop.X, prop.Y));
                if (view != null && !view.Visible((p.X - 180) * Canvas.Scale,
                    (p.Y - 220) * Canvas.Scale, (p.X + 180) * Canvas.Scale, (p.Y + 100) * Canvas.Scale)) continue;
                int id = -1 - i;
                if (!gpu.CachedObject(id, 0, int.MaxValue))
                    gpu.CacheObject(id, 0, int.MaxValue, false, true, () => DrawNativeExterior(prop, gpu));
            }
            var origin = P(new V2(0, 0));
            ((IProceduralGpu)gpu).Pictures(nativePictures, origin.X * Canvas.Scale, origin.Y * Canvas.Scale, Canvas.Scale);
            Canvas.TestDepth = true;
            foreach (var item in VisibleSceneryItems())
            {
                var o = item.Decoration ?? StudioRules.At(map, item.TileId);
                if (o != null && o.Destroyed) continue;
                if (o == null && map.Tiles[item.TileId] != Tile.LowCover && map.Tiles[item.TileId] != Tile.Wall) continue;
                if (gpu.CachedObject(item.TileId, item.ClipL, item.ClipR)) continue;
                var position = o == null ? map.Center(item.TileId) : new V2(o.X, o.Y);
                bool blocksRoof = map.DynamicBuildings == null || !map.DynamicBuildings.Exists(h => h.Contains(position));
                gpu.CacheObject(item.TileId, item.ClipL, item.ClipR, blocksRoof, false, () => DrawSceneryItem(map, item));
            }
            gpu.EndScene();
        }

        protected void NativeVehicleDepth(BattleSimulation battle, IStaticSceneGpu gpu)
        {
            if ((battle.GreenVehicle == null || !battle.GreenVehicle.Active) &&
                (battle.RedVehicle == null || !battle.RedVehicle.Active))
            {
                gpu.VehicleDepth(null);
                return;
            }
            vehicleDepthBattle = battle;
            if (drawVehicleDepth == null) drawVehicleDepth = DrawNativeVehicleDepth;
            gpu.VehicleDepth(drawVehicleDepth);
        }

        void DrawNativeVehicleDepth()
        {
            DrawVehicleArrival(vehicleDepthBattle, vehicleDepthBattle.GreenVehicle);
            DrawVehicleArrival(vehicleDepthBattle, vehicleDepthBattle.RedVehicle);
        }

        void BuildNativeExterior(BattleMap map)
        {
            nativeExterior = new ExteriorLayout(map);
            nativeExteriorProps.Clear();
            string style = nativeExterior.Style;
            bool urban = style == "town" || style == "ruins" || style == "hospital" || style == "construction" || style == "industrial";
            bool depot = style == "depot" || style == "base";
            int width = LogicalWidth(map), height = LogicalHeight(map);
            float halfX = width / 22f + 90f / 11f, halfY = height / 10f + 90f / 5f;
            int reach = (int)Math.Ceiling((halfX + halfY) * .5f) + 2;
            for (int y = map.Rows / 2 - reach; y < map.Rows / 2 + reach; y += urban || depot ? 9 : 5)
                for (int x = map.Columns / 2 - reach; x < map.Columns / 2 + reach; x += urban || depot ? 9 : 5)
                {
                    if (x > -6 && x < map.Columns + 4 && y > -6 && y < map.Rows + 4) continue;
                    var p = P(new V2(x, y));
                    if (p.X < -90 || p.X > width + 90 || p.Y < -10 || p.Y > height + 90) continue;
                    int seed = ExteriorHash(x, y);
                    if (seed % 5 == 0 || !nativeExterior.PropAllowed(x, y)) continue;
                    if ((urban || depot) && (Math.Abs(x) % 20 == 0 || Math.Abs(y) % 20 == 0)) continue;
                    nativeExteriorProps.Add(new ExteriorProp { X = x, Y = y, Seed = seed });
                }
            nativeExteriorProps.Sort((a, b) => (a.Y - a.X).CompareTo(b.Y - b.X));
        }

        void DrawNativeExterior(ExteriorProp prop, IStaticSceneGpu gpu)
        {
            string style = nativeExterior.Style;
            bool urban = style == "town" || style == "ruins" || style == "hospital" || style == "construction" || style == "industrial";
            bool depot = style == "depot" || style == "base";
            int trees = nativeExterior.TreesNear(prop.X, prop.Y);
            bool house = urban ? prop.Seed % 4 != 0 : depot ? prop.Seed % 3 != 0 : style == "village" && trees < 3 && prop.Seed % 4 == 0;
            if (trees >= 3) house = false;
            if (!house && trees == 0 && style != "forest" && style != "park" && prop.Seed % 3 != 0) return;
            Canvas.TestDepth = false;
            Canvas.Depth = -1; // Distance tint, no combat occlusion.
            if (!house) { ExteriorTrees(prop.X, prop.Y, prop.Seed, style == "park"); return; }
            string family = style == "village" && prop.Seed % 2 == 0 ? "logs" : ExteriorSprites.Family(style);
            int damage = style == "ruins" ? 1 + prop.Seed % 2 : prop.Seed % 3;
            var sprite = ExteriorSprites.Load?.Invoke(family + "-" + (damage * 4 + prop.Seed / 7 % 4));
            if (sprite == null) return;
            var p = P(new V2(prop.X, prop.Y));
            gpu.ExteriorImage(sprite, p.X, p.Y, Canvas.Scale);
        }

        void NativeExteriorGround(int x, int y)
        {
            var foot = P(new V2(x, y));
            var surface = nativeExterior.SurfaceAt(x, y);
            Canvas.Depth = -1;
            if (surface != null)
                StudioDrawing.Draw(Canvas, new StudioPlaced { Asset = surface, X = x, Y = y }, foot.X, foot.Y);
            else
            {
                StudioAsset unused;
                bool road = nativeExterior.RoadAt(x + .5f, y + .5f, 0, out unused);
                var center = P(new V2(x + .5f, y + .5f));
                Canvas.Diamond((int)center.X, (int)center.Y, 11, 5, road ? C(80, 85, 78) : C(58, 68, 45));
            }
        }
    }
}
