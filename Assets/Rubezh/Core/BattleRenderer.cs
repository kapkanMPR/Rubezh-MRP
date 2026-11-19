using System;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    public abstract partial class BattleVisualRecipes
    {
        protected sealed class Item
        {
            public float Depth;
            public int ClipL,
                ClipR = int.MaxValue;
            public int TileId = -1;
            public StudioPlaced Decoration;
        }

        public const int Width = 1280;
        public const int Height = 800;
        public const float Scale = 1.33333337f;
        public const float UltraScale = 2.4f;
        public const float HighScale = Scale * 1.15f;
        public const int OutputWidth = 1707;
        public const int OutputHeight = 1067;
        public PixelCanvas Canvas;
        public float RasterScale { get; protected set; }

        protected BattleVisualRecipes()
            : this(Scale) { }

        protected BattleVisualRecipes(float rasterScale)
        {
            RasterScale = Math.Max(.5f, Math.Min(UltraScale, rasterScale));
            Canvas = new PixelCanvas(1280, 800, RasterScale);
        }

        public bool PrepareCanvas(BattleMap m, float rasterScale)
        {
            RasterScale = Math.Max(.5f, Math.Min(UltraScale, rasterScale));
            return PrepareCanvas(m);
        }

        public static int LogicalWidth(BattleMap m) =>
            m == null || m.Columns + m.Rows <= 108 ? 1280 : Math.Max(1280, (m.Columns + m.Rows) * 11 + 100);

        public static int LogicalHeight(BattleMap m) =>
            m == null || m.Columns + m.Rows <= 108 ? 800 : Math.Max(800, (m.Columns + m.Rows) * 5 + 160);

        public bool PrepareCanvas(BattleMap m)
        {
            int w = LogicalWidth(m),
                h = LogicalHeight(m);
            if (Canvas.Width == (int)Math.Round(w * RasterScale) && Canvas.Height == (int)Math.Round(h * RasterScale))
                return false;
            Canvas = new PixelCanvas(w, h, RasterScale);
            cachedMap = null;
            cachedRevision = -1;
            nativeMap = null;
            roofMap = null;
            scenery = null;
            return true;
        }

        protected readonly uint background = C(18, 27, 32);
        protected readonly uint outline = C(24, 33, 34);
        protected BattleMap activeMap;
        protected uint[] scenery;
        protected uint[] sharpGround;
        public bool SharpScenery { get; set; }
        protected bool cachedSharpScenery;
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        protected OrderedCanvas legacyOrdered;
        protected OrderedCanvas ordered => legacyOrdered ?? (legacyOrdered = new OrderedCanvas());
#endif
        protected int compositionStage = 1;
        protected BattleMap cachedMap;
        protected int cachedRevision = -1;

        protected static uint C(int r, int g, int b)
        {
            return PixelCanvas.RGB(r, g, b);
        }

        public static V2 Project(V2 p)
        {
            return new V2(112f + (p.X + p.Y) * 11f, 424f + (p.Y - p.X) * 5f);
        }

        public static V2 Project(V2 p, BattleMap map)
        {
            return new V2(
                LogicalWidth(map) * .5f + (p.X + p.Y - (float)(map.Columns + map.Rows) * 0.5f) * 11f,
                LogicalHeight(map) * .5f + 10f + (p.Y - p.X - (float)(map.Rows - map.Columns) * 0.5f) * 5f
            );
        }

        protected V2 P(V2 p)
        {
            return Project(p, activeMap);
        }

#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        protected void CacheScenery(BattleMap map)
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.Render.Scenery"))
            {
                if (TryPatchScenery(map))
                    return;
                FullSceneryBuilds++;
                activeMap = map;
                Canvas.TestDepth = false;
                Canvas.RecordDepth = false;
                if (Canvas.Occlusion == null)
                {
                    Canvas.Occlusion = new float[Canvas.Pixels.Length];
                }
                else
                {
                    Array.Clear(Canvas.Occlusion, 0, Canvas.Occlusion.Length);
                }

                if (Canvas.RoofOcclusion == null)
                    Canvas.RoofOcclusion = new float[Canvas.Pixels.Length];
                else
                    Array.Clear(Canvas.RoofOcclusion, 0, Canvas.RoofOcclusion.Length);
                DrawExterior(map);
                basePixels = (uint[])Canvas.Pixels.Clone();
                for (int k = 0; k < map.Rows; k++)
                {
                    for (int l = 0; l < map.Columns; l++)
                    {
                        Ground(map, l, k);
                    }
                }

                BuildSceneryItems(map);
                if (map.TerrainPicture != null)
                    DrawPictureSet(new[] { map.TerrainPicture });
                sharpGround = SharpScenery ? (uint[])Canvas.Pixels.Clone() : null;
                cachedSharpScenery = SharpScenery;
                Canvas.RecordDepth = true;
                foreach (var item in sceneryItems)
                    DrawSceneryItem(map, item);
                DrawProceduralPictures(map);
                Canvas.RecordDepth = false;
                if (scenery == null)
                {
                    scenery = new uint[Canvas.Pixels.Length];
                }

                Array.Copy(Canvas.Pixels, scenery, scenery.Length);
                sceneryDepth = (float[])Canvas.Occlusion.Clone();
                RememberBounds(map);
                RememberChunkState(map);
                cachedMap = map;
                cachedRevision = map.Revision;
            }
        }
#endif

        void BuildSceneryItems(BattleMap map)
        {
            System.Collections.Generic.List<BattleVisualRecipes.Item> list =
                new System.Collections.Generic.List<BattleVisualRecipes.Item>();
            for (int m = 0; m < map.Tiles.Length; m++)
            {
                if (
                    StudioRules.At(map, m) != null
                        ? !StudioRules.At(map, m).Destroyed && StudioRules.At(map, m).Root == m
                        : map.Tiles[m] == Tile.LowCover || map.Tiles[m] == Tile.Wall
                )
                {
                    V2 v4 = map.Center(m);
                    list.Add(
                        new BattleVisualRecipes.Item
                        {
                            TileId = m,
                            Depth =
                                StudioRules.At(map, m) != null
                                    ? 256f
                                        + StudioRules.At(map, m).Y
                                        + StudioRules.At(map, m).Asset.Height
                                        - StudioRules.At(map, m).X
                                    : 256f + v4.Y - v4.X,
                        }
                    );
                }
            }

            if (map.Studio != null)
                foreach (var d in map.Studio.Decorations)
                    if (!d.Destroyed)
                        list.Add(
                            new Item
                            {
                                TileId = map.Tiles.Length + map.Studio.Decorations.IndexOf(d),
                                Decoration = d,
                                Depth = DecorRules.Depth(d),
                            }
                        );
            var expanded = new System.Collections.Generic.List<Item>(list.Count);
            foreach (var old in list)
            {
                var o = old.Decoration ?? StudioRules.At(map, old.TileId);
                if (o == null)
                {
                    expanded.Add(old);
                    continue;
                }

                var p = o.Support ?? o;
                int w = p.Asset.Width,
                    h = p.Asset.Height,
                    count = w == h ? 1 : 2 * (w + h);
                float bias =
                    o.Support != null ? .002f + (o.OffsetY - o.OffsetX) * .0001f
                    : o.Asset.Decor ? o.OffsetY - o.OffsetX
                    : 0f;
                float start = P(new V2(p.X, p.Y)).X;
                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? w : (i + .5f) / 2;
                    expanded.Add(
                        new Item
                        {
                            TileId = old.TileId,
                            Decoration = old.Decoration,
                            Depth = 256 + p.Y - p.X + Math.Min(h, t) + Math.Max(0, t - w) - t + bias,
                            ClipL = i == 0 ? 0 : (int)Math.Round((start + 5.5f * i) * Canvas.Scale),
                            ClipR =
                                i == count - 1
                                    ? int.MaxValue
                                    : (int)Math.Round((start + 5.5f * (i + 1)) * Canvas.Scale),
                        }
                    );
                }
            }

            list = expanded;
            list.Sort(
                (BattleVisualRecipes.Item a1, BattleVisualRecipes.Item b1) =>
                    a1.Depth == b1.Depth ? a1.TileId.CompareTo(b1.TileId) : a1.Depth.CompareTo(b1.Depth)
            );
            sceneryItems = list;
        }

        protected void DrawSceneryItem(BattleMap map, Item item)
        {
            var roofObject = item.Decoration ?? StudioRules.At(map, item.TileId);
            V2 roofPosition = roofObject == null ? map.Center(item.TileId) : new V2(roofObject.X, roofObject.Y);
            Canvas.RoofBlocker =
                map.DynamicBuildings == null || !map.DynamicBuildings.Exists(h => h.Contains(roofPosition));
            Canvas.LayerLeft = item.ClipL;
            Canvas.LayerRight = item.ClipR;
            try
            {
                DrawSceneryPixels(map, item);
            }
            finally
            {
                Canvas.RoofBlocker = true;
                Canvas.LayerLeft = 0;
                Canvas.LayerRight = int.MaxValue;
            }
        }

        void DrawSceneryPixels(BattleMap map, Item item)
        {
            Canvas.Depth = item.Depth;
            if (item.Decoration != null)
            {
                var d = item.Decoration;
                if (d.Destroyed)
                    return;
                var dp = P(new V2(d.X, d.Y));
                StudioDrawing.Draw(Canvas, d, dp.X, dp.Y);
                return;
            }

            if (
                map.ProceduralPictures != null
                && map.ProceduralCells != null
                && item.TileId >= 0
                && item.TileId < map.ProceduralCells.Length
                && map.ProceduralCells[item.TileId]
            )
                return;
            var studio = StudioRules.At(map, item.TileId);
            if (studio != null)
            {
                var foot = P(new V2(studio.X, studio.Y));
                StudioDrawing.Draw(Canvas, studio, foot.X, foot.Y, map.BushVisualScale(item.TileId));
                return;
            }

            int prop = map.LevelProps == null ? 0 : map.LevelProps[item.TileId];
            if (
                map.VehicleFootprint != null
                && item.TileId < map.VehicleFootprint.Length
                && map.VehicleFootprint[item.TileId] != 0
            )
                return;
            if (prop != 0)
            {
                var p = P(map.Center(item.TileId));
                int width,
                    height;
                Rubezh.Graphics.PropSize(prop, out width, out height);
                // Rotate around the ground contact point, not the sprite centre.
                float sx =
                    map.LevelPropScaleX != null
                    && item.TileId < map.LevelPropScaleX.Length
                    && map.LevelPropScaleX[item.TileId] > 0f
                        ? Math.Max(.4f, Math.Min(2.5f, map.LevelPropScaleX[item.TileId]))
                        : 1f;
                float sy =
                    map.LevelPropScaleY != null
                    && item.TileId < map.LevelPropScaleY.Length
                    && map.LevelPropScaleY[item.TileId] > 0f
                        ? Math.Max(.4f, Math.Min(2.5f, map.LevelPropScaleY[item.TileId]))
                        : 1f;
                float degrees =
                    map.LevelPropRotation != null && item.TileId < map.LevelPropRotation.Length
                        ? map.LevelPropRotation[item.TileId]
                        : 0f;
                float wear = map.BushVisualScale(item.TileId);
                sx *= wear;
                sy *= wear;
                float angle = degrees * (float)Math.PI / 180f;
                int scaledW = Math.Max(1, (int)Math.Round(width * sx)),
                    scaledH = Math.Max(1, (int)Math.Round(height * sy));
                float dy =
                    -scaledH * .5f
                    + (
                        prop == 9 ? 3f
                        : prop == 10 ? 1.5f
                        : prop == 11 || prop == 12 ? 2.5f
                        : 2f
                    );
                float cx = p.X - dy * (float)Math.Sin(angle),
                    cy = p.Y + dy * (float)Math.Cos(angle);
                Rubezh.Graphics.Prop(Canvas, prop, cx, cy, scaledW, scaledH, angle);
            }
            else
            {
                var foot = P(map.Center(item.TileId));
                float sx =
                    map.LevelPropScaleX != null && map.LevelPropScaleX[item.TileId] > 0
                        ? map.LevelPropScaleX[item.TileId]
                        : 1;
                float sy =
                    map.LevelPropScaleY != null && map.LevelPropScaleY[item.TileId] > 0
                        ? map.LevelPropScaleY[item.TileId]
                        : 1;
                float angle = map.LevelPropRotation == null ? 0 : map.LevelPropRotation[item.TileId];
                Canvas.SetObjectTransform(foot.X, foot.Y, sx, sy, angle);
                try
                {
                    Block(map, item.TileId);
                }
                finally
                {
                    Canvas.ClearObjectTransform();
                }
            }
        }

        // Shared scenery is read-only here; vehicle occlusion belongs to this pass.




        protected void SetCompositionStage(ICanvasGpu destination, int stage)
        {
            compositionStage = stage;
            var direct = destination as INativeComposition;
            if (direct != null && direct.DirectComposition)
                direct.SetStage(stage);
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
            else
                ordered.Stage = stage;
#else
            else
                throw new InvalidOperationException("Native composition sink required.");
#endif
        }

        // Ground equipment is drawn before all soldiers. Only items still in
        // flight are drawn afterwards; visual layering does not affect pickups.
        // Permanent 1-cell-wide decals left by bleeding crawlers and evacuated patients.
        protected void DrawVehicleArrivals(BattleSimulation b)
        {
            DrawVehicleArrival(b, b.GreenVehicle);
            DrawVehicleArrival(b, b.RedVehicle);
        }

        protected void DrawVehicleArrival(BattleSimulation b, VehicleArrivalState v)
        {
            if (v == null || !v.Active || !b.Fog.Revealed(v.Center))
                return;
            var p = P(v.Center);
            Canvas.TestDepth = true;
            Canvas.Depth = 256f + v.Center.Y - v.Center.X + .08f;
            // Ground-shadow anchor shared with the native GPU renderer and editor.
            float scale = Rubezh.Graphics.VehicleScale(v.Side == Side.Green, b.CampaignLevel);
            Rubezh.Graphics.Vehicle(
                Canvas,
                v.Side == Side.Green,
                v.Direction,
                p.X,
                p.Y
                    - (Rubezh.Graphics.VehicleGroundAnchor - .5f)
                        * (int)Math.Round(Rubezh.Graphics.VehicleRenderHeight * scale),
                scale
            );
        }

        // Native sinks retain immutable world recipes; legacy/editor sinks draw normally.
        protected virtual bool BeginWorldItem(
            object item,
            int kind,
            V2 position,
            float height,
            float angle,
            int art,
            int width = 0,
            int heightPixels = 0,
            float bias = 0
        ) => true;

        protected virtual void EndWorldItem() { }

        protected void DrawBloodTraces(BattleSimulation b)
        {
            Canvas.TestDepth = true;
            Canvas.RecordDepth = false;
            uint dark = C(104, 26, 28),
                light = C(141, 34, 37);
            foreach (var mark in b.BloodTraces)
            {
                if (!b.Fog.Revealed(mark.Position))
                    continue;
                if (!BeginWorldItem(mark, 0, mark.Position, 0, 0, 0))
                    continue;
                Canvas.Depth = 1f; // Above bare terrain (0), below every scenery object.
                var p = P(mark.Position);
                // Ellipses require no per-mark int[4] allocations (Diamond does).
                Canvas.Ellipse((int)p.X, (int)p.Y, 12, 6, dark);
                Canvas.Ellipse((int)p.X, (int)p.Y, 9, 4, light);
                EndWorldItem();
            }

            foreach (var s in b.Soldiers)
                if (b.Fog.CanDraw(s) && CasualtyVisuals.Pool(s))
                {
                    if (!BeginWorldItem(s, 1, s.Position, CasualtyVisuals.PoolScale(s), 0, 0))
                        continue;
                    Canvas.Depth = 1f;
                    var p = P(s.Position);
                    float scale = CasualtyVisuals.PoolScale(s);
                    Canvas.Ellipse((int)p.X, (int)p.Y, (int)Math.Round(12 * scale), (int)Math.Round(6 * scale), dark);
                    Canvas.Ellipse((int)p.X, (int)p.Y, (int)Math.Round(9 * scale), (int)Math.Round(4 * scale), light);
                    EndWorldItem();
                }

            Canvas.TestDepth = false;
        }

        // Approved material fragments; settled fragments remain behind live world objects.
        // Strictly visual: no hitboxes, physics objects or navigation updates.
        protected void DrawCoverDebris(BattleSimulation b, bool airborne)
        {
            Canvas.RecordDepth = false;
            foreach (var part in CoverDebrisSystem.Visuals(b))
            {
                if (part.Flying(b.Time) != airborne)
                    continue;
                var world = part.Position(b.Time);
                if (!b.Fog.Revealed(world))
                    continue;
                if (
                    !BeginWorldItem(
                        part,
                        2,
                        world,
                        airborne ? part.Height(b.Time) : 0,
                        part.Rotation(b.Time),
                        part.SpriteId,
                        part.Width,
                        part.HeightPixels,
                        part.Born
                    )
                )
                    continue;
                var pos = P(world);
                Canvas.TestDepth = true;
                float bornBias = Math.Min(.12f, Math.Max(0f, part.Born) * .00001f);
                Canvas.Depth = 256f + world.Y - world.X - .35f + bornBias;
                if (airborne)
                    pos.Y -= part.Height(b.Time) * 14f;
                Rubezh.Graphics.Debris(
                    Canvas,
                    part.SpriteId,
                    pos.X,
                    pos.Y,
                    part.Width,
                    part.HeightPixels,
                    part.Rotation(b.Time)
                );
                EndWorldItem();
            }

            Canvas.TestDepth = false;
        }

        protected void GroundShadow(V2 world, int rx, int ry, uint color)
        {
            int stage = compositionStage;
            float depth = Canvas.Depth;
            bool test = Canvas.TestDepth;
            SetCompositionStage(Canvas.Gpu, 0);
            Canvas.TestDepth = true;
            Canvas.Depth = 256f + world.Y - world.X - .2f;
            var point = P(world);
            Canvas.Ellipse((int)point.X, (int)point.Y, rx, ry, color);
            SetCompositionStage(Canvas.Gpu, stage);
            Canvas.Depth = depth;
            Canvas.TestDepth = test;
        }

        protected void DrawGroundEquipment(BattleSimulation b, bool airborne)
        {
            Canvas.RecordDepth = false;
            foreach (var soldier in b.Soldiers)
            {
                if (
                    !b.Fog.Revealed(soldier.DroppedWeaponPosition)
                    || airborne
                    || soldier.Alive
                    || !soldier.Deployed
                    || !soldier.WeaponDropped
                    || soldier.WeaponClaimed
                )
                    continue;
                Canvas.TestDepth = true;
                if (
                    !BeginWorldItem(
                        soldier,
                        3,
                        soldier.DroppedWeaponPosition,
                        0,
                        soldier.DroppedWeaponAngle,
                        soldier.DroppedWeaponVariant,
                        soldier.DroppedWeaponUpgrade?.Rarity ?? -1
                    )
                )
                    continue;
                Canvas.Depth = 256f + soldier.DroppedWeaponPosition.Y - soldier.DroppedWeaponPosition.X - .15f;
                if (soldier.WeaponDropped && !soldier.WeaponClaimed)
                {
                    var fallenGun = P(soldier.DroppedWeaponPosition);
                    Rubezh.Graphics.LooseItem(
                        Canvas,
                        soldier.DroppedWeaponVariant,
                        fallenGun.X,
                        fallenGun.Y,
                        soldier.DroppedWeaponAngle,
                        upgrade: soldier.DroppedWeaponUpgrade
                    );
                    EndWorldItem();
                }
            }

            foreach (var launcher in b.GroundLaunchers)
            {
                if (launcher.Scene != null)
                    continue;
                if (!b.Fog.Revealed(launcher.Position(b.Time)))
                    continue;
                if (launcher.Collected || launcher.Flying(b.Time) != airborne)
                    continue;
                var where = launcher.Position(b.Time);
                int sourceSide =
                    launcher.FromSoldierId >= 0 && launcher.FromSoldierId < b.Soldiers.Count
                        ? (int)b.Soldiers[launcher.FromSoldierId].Side
                        : -1;
                if (
                    !BeginWorldItem(
                        launcher,
                        4,
                        where,
                        launcher.Height(b.Time),
                        launcher.Angle,
                        launcher.Kind,
                        sourceSide
                    )
                )
                    continue;
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + where.Y - where.X - .15f;
                var v = P(launcher.Position(b.Time));
                float y = v.Y - launcher.Height(b.Time) * 12f;
                if (
                    launcher.FromSoldierId >= 0
                    && launcher.FromSoldierId < b.Soldiers.Count
                    && b.Soldiers[launcher.FromSoldierId].Side == Side.Red
                )
                    Rubezh.Graphics.StudioSprite(Canvas, 10, v.X, y, 24 * .85f, 6 * .85f, launcher.Angle);
                else
                    Rubezh.Graphics.LooseItem(Canvas, 12 + launcher.Kind, v.X, y, launcher.Angle, 24f * .85f / 19f);
                if (launcher.Flying(b.Time))
                {
                    GroundShadow(where, 5, 2, C(33, 41, 35));
                }
                EndWorldItem();
            }

            foreach (var ammo in b.GroundAmmo)
            {
                if (!b.Fog.Revealed(ammo.Position(b.Time)))
                    continue;
                if (ammo.Collected || ammo.Flying(b.Time) != airborne)
                    continue;
                var loc = ammo.Position(b.Time);
                if (!BeginWorldItem(ammo, 5, loc, ammo.Height(b.Time), 0, MagazineSystem.ArtIndex(ammo)))
                    continue;
                var v = P(loc);
                int y = (int)(v.Y - ammo.Height(b.Time) * 12f);
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + loc.Y - loc.X - .15f;
                Rubezh.Graphics.Ammunition(Canvas, MagazineSystem.ArtIndex(ammo), v.X, y);
                if (airborne)
                    GroundShadow(loc, 5, 2, C(39, 36, 43));
                EndWorldItem();
            }

            foreach (var band in b.GroundTourniquets)
            {
                if (!b.Fog.Revealed(band.Position(b.Time)))
                    continue;
                if (band.Collected || band.Flying(b.Time) != airborne)
                    continue;
                var location = band.Position(b.Time);
                if (!BeginWorldItem(band, 6, location, band.Arc(b.Time), 0, 14))
                    continue;
                var projected = P(location);
                projected.Y -= band.Arc(b.Time) * 12f;
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + location.Y - location.X - .15f;
                Rubezh.Graphics.LooseItem(Canvas, 14, projected.X, projected.Y, 0);
                if (airborne)
                    GroundShadow(location, 5, 2, C(39, 36, 43));
                EndWorldItem();
            }

            foreach (var ground in b.GroundWeapons)
            {
                if (ground.Scene != null)
                    continue;
                if (!b.Fog.Revealed(ground.DisplayPosition(b.Time)))
                    continue;
                if (ground.Collected || ground.Flying(b.Time) != airborne)
                    continue;
                Canvas.TestDepth = true;
                var world = ground.DisplayPosition(b.Time);
                if (
                    !BeginWorldItem(
                        ground,
                        7,
                        world,
                        ground.ArcHeight(b.Time),
                        ground.Angle,
                        ground.Variant,
                        ground.Upgrade?.Rarity ?? -1
                    )
                )
                    continue;
                Canvas.Depth = 256f + world.Y - world.X - .15f;
                var position = ground.DisplayPosition(b.Time);
                var v = P(position);
                Rubezh.Graphics.LooseItem(
                    Canvas,
                    ground.Variant,
                    v.X,
                    v.Y - ground.ArcHeight(b.Time) * 12f,
                    ground.Angle,
                    upgrade: ground.Upgrade
                );
                if (ground.Flying(b.Time))
                    GroundShadow(position, 4, 2, C(49, 53, 46));
                EndWorldItem();
            }

            Canvas.TestDepth = false;
        }

        protected void DrawEffects(BattleSimulation b)
        {
            Canvas.TestDepth = false;
            foreach (ArcProjectile projectile in b.Projectiles)
            {
                if (!b.Fog.Revealed(projectile.Position))
                    continue;
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + projectile.Position.Y - projectile.Position.X + .12f;
                V2 a = P(projectile.Position);
                V2 a2 = P(projectile.From);
                float progress = Util.Clamp((b.Time - projectile.Born) / projectile.Duration, 0f, 1f);
                if (!projectile.HasMuzzleOffset)
                {
                    WeaponPose(
                        b,
                        b.Soldiers[projectile.ShooterId],
                        projectile.Kind >= 3,
                        out var center,
                        out var muzzle,
                        out var _,
                        out var _,
                        out var _
                    );
                    projectile.MuzzleOffset =
                        (
                            (projectile.Kind >= 3)
                                ? muzzle
                                : ((projectile.Kind == 2) ? (muzzle + new V2(0f, 3f)) : center)
                        ) - P(b.Soldiers[projectile.ShooterId].Position);
                    projectile.MuzzleOffset *= .85f;
                    projectile.HasMuzzleOffset = true;
                }

                V2 a3 = (
                    (projectile.Kind >= 3)
                        ? (a + projectile.MuzzleOffset * (1f - progress) + new V2(0f, -9f * progress))
                        : (
                            a
                            + projectile.MuzzleOffset * (1f - progress)
                            + new V2(
                                0f,
                                (0f - (projectile.Height - ((projectile.Kind == 2) ? 1.55f : 1.35f) * (1f - progress)))
                                    * 9f
                            )
                        )
                );
                if (projectile.Landed)
                    a3 = a;
                GroundShadow(projectile.Position, 3, 1, C(40, 48, 39));
                if (projectile.Kind >= 3)
                {
                    V2 normalized = (P(projectile.To) + new V2(0f, -9f) - (a2 + projectile.MuzzleOffset)).Normalized;
                    V2 v = a3 - normalized * 10f;
                    V2 trailEnd = a3 - normalized * 4f;
                    Canvas.Line((int)v.X, (int)v.Y, (int)trailEnd.X, (int)trailEnd.Y, C(236, 153, 73));
                    Canvas.Line((int)trailEnd.X, (int)trailEnd.Y, (int)a3.X, (int)a3.Y, C(248, 226, 173));
                    Canvas.Dot((int)a3.X, (int)a3.Y, C(255, 247, 205));
                }
                else if (
                    projectile.ShooterId >= 0
                    && projectile.ShooterId < b.Soldiers.Count
                    && b.Soldiers[projectile.ShooterId].Side == Side.Green
                    && (projectile.Kind == 0 || projectile.Kind == 1 && HandGrenades.Type(projectile) >= 3)
                )
                    Rubezh.Graphics.StudioSprite(
                        Canvas,
                        projectile.Kind == 0 ? 6825 : 6826 + HandGrenades.Type(projectile) - 3,
                        a3.X,
                        a3.Y,
                        projectile.Kind == 0 ? 3 : 4,
                        6
                    );
                else if (
                    projectile.Kind == 2
                    && projectile.ShooterId >= 0
                    && projectile.ShooterId < b.Soldiers.Count
                    && b.Soldiers[projectile.ShooterId].Side == Side.Green
                )
                    Rubezh.Graphics.StudioSprite(Canvas, Rubezh.Graphics.VogSprite, a3.X, a3.Y, 3, 6);
                else if (projectile.Kind == 1)
                    Rubezh.Graphics.Grenade(Canvas, a3.X, a3.Y);
                else
                {
                    Canvas.Ellipse(
                        (int)a3.X,
                        (int)a3.Y,
                        3,
                        2,
                        (projectile.Kind == 2) ? C(239, 197, 97) : C(148, 172, 94)
                    );
                    Canvas.Dot((int)a3.X + 1, (int)a3.Y - 1, C(252, 235, 175));
                }
            }

            foreach (BlastEffect blast in b.Blasts)
            {
                if (!b.Fog.Revealed(blast.Position))
                    continue;
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + blast.Position.Y - blast.Position.X + .12f;
                V2 v3 = P(blast.Position);
                v3.Y -= blast.Height * 24 / 2.8f;
                float blastAge = b.Time - blast.Born;
                int ringRadius = (int)((8f + blastAge * 28f) * blast.Radius / 3.3f);
                for (int i = 0; i < 28; i++)
                {
                    double angle = (double)i * Math.PI * 2.0 / 28.0;
                    Canvas.Rect(
                        (int)v3.X + (int)(Math.Cos(angle) * (double)ringRadius),
                        (int)v3.Y + (int)(Math.Sin(angle) * (double)ringRadius * 0.5) - 5,
                        3,
                        2,
                        (blastAge < 0.35f) ? ((blast.Kind == 2) ? C(255, 146, 65) : C(255, 216, 123)) : C(179, 139, 98)
                    );
                }

                if (blast.Mortar && blastAge < .28f)
                {
                    int flash = (int)(28 * (1 - blastAge / .35f));
                    Canvas.Ellipse((int)v3.X, (int)v3.Y - 8, flash, Math.Max(4, flash / 2), C(255, 240, 183));
                    Canvas.Ellipse((int)v3.X, (int)v3.Y - 8, flash / 2, Math.Max(3, flash / 3), C(255, 255, 240));
                }
                if (blastAge < 0.2f)
                {
                    Canvas.Ellipse((int)v3.X, (int)v3.Y - 6, 11, 8, C(250, 222, 144));
                    Canvas.Rect((int)v3.X - 2, (int)v3.Y - 22, 4, 23, C(255, 243, 190));
                }
            }

            foreach (SmokeCloud item in b.Smoke)
            {
                if (!b.Fog.SmokeVisible(b, item))
                    continue;
                Canvas.TestDepth = true;
                Canvas.Depth = 256f + item.Position.Y - item.Position.X + .12f;
                V2 v4 = P(item.Position);
                float smokeOpacity =
                    Util.Clamp((item.Until - b.Time) / 3f, 0f, 1f) * Util.Clamp((b.Time - item.Born) / 1.8f, 0f, 1f);
                int animationStep = (int)(b.Time * 2f);
                for (int j = 0; j < 5; j++)
                {
                    int puffX = (int)v4.X + (j - 2) * 10;
                    int puffY = (int)v4.Y - 9 - j % 2 * 6;
                    var smokeGpu = Canvas.Gpu as ISmokePuffGpu;
                    if (
                        smokeGpu != null
                        && smokeGpu.SmokePuff(
                            (puffX - 22) * Canvas.Scale,
                            (puffY - 14) * Canvas.Scale,
                            Canvas.Scale,
                            ((j * 3 + animationStep) % 7 + 7) % 7,
                            (int)(4 * smokeOpacity),
                            j % 2 != 0,
                            Canvas.Depth
                        )
                    )
                        continue;
                    for (int k = -14; k <= 14; k += 2)
                    {
                        for (int l = -22; l <= 22; l += 2)
                        {
                            if (
                                !((float)(l * l) / 484f + (float)(k * k) / 196f > 1f)
                                && !((float)(((l + k + j * 3 + animationStep) % 7 + 7) % 7) > 4f * smokeOpacity)
                            )
                            {
                                uint c = ((j % 2 == 0) ? C(161, 170, 164) : C(132, 145, 141));
                                Canvas.Rect(puffX + l, puffY + k, 2, 2, c);
                            }
                        }
                    }
                }
            }
        }

        protected void Ground(BattleMap map, int x, int y)
        {
            var ground = map.Studio == null ? null : map.Studio.Ground[y * map.Columns + x];
            if (ground != null)
            {
                var foot = P(new V2(x, y));
                StudioDrawing.Draw(
                    Canvas,
                    new StudioPlaced
                    {
                        Asset = ground,
                        X = x,
                        Y = y,
                    },
                    foot.X,
                    foot.Y
                );
                return;
            }

            V2 v = P(new V2((float)x + 0.5f, (float)y + 0.5f));
            int screenX = (int)v.X;
            int screenY = (int)v.Y;
            int variation = ((x * 71 + y * 139) ^ (x * y * 17)) % 9;
            Tile tile = map.At(x, y);
            bool isRoad = tile == Tile.Road;
            int themeTint = ((map.Theme == 4) ? 13 : ((map.Theme == 5) ? (-8) : 0));
            uint c = (
                isRoad
                    ? C(77 + variation, 83 + variation, 78 + variation)
                    : C(88 + variation + themeTint, 99 + variation, 74 + variation + ((map.Theme == 5) ? 12 : 0))
            );
            Canvas.Diamond(screenX, screenY, 11, 5, c);
            Canvas.Line(screenX - 11, screenY, screenX, screenY + 5, isRoad ? C(71, 78, 74) : C(80, 92, 69));
            if ((x * 11 + y * 3) % 4 == 0)
            {
                Canvas.Rect(screenX - 3, screenY - 1, 2, 1, isRoad ? C(101, 104, 93) : C(115, 121, 83));
            }

            if (!isRoad && tile == Tile.Ground && map.Theme == 0 && y % 3 == 0)
            {
                Canvas.Line(
                    screenX - 7,
                    screenY + 3,
                    screenX + 7,
                    screenY - 3,
                    C(103 + variation, 111 + variation, 70)
                );
            }

            if (!isRoad && tile == Tile.Ground && map.Theme == 4 && (x + y) % 3 == 0)
            {
                Canvas.Line(screenX - 4, screenY, screenX + 4, screenY - 2, C(148, 139, 111));
            }

            if (tile == Tile.Rubble)
            {
                Canvas.Rect(screenX - 4, screenY, 3, 2, C(109, 109, 95));
                Canvas.Rect(screenX + 2, screenY - 2, 2, 2, C(69, 75, 63));
            }

            if (isRoad && y == map.Rows / 2 && x % 4 == 0)
            {
                Canvas.Line(screenX - 4, screenY + 2, screenX + 3, screenY - 1, C(145, 137, 101));
            }
        }

        protected void Block(BattleMap map, int index)
        {
            V2 v = P(map.Center(index));
            int screenX = (int)v.X;
            int screenY = (int)v.Y;
            bool isWall = map.Tiles[index] == Tile.Wall;
            int coverStyle =
                (!isWall && map.LowCoverStyle != null && map.LowCoverStyle[index] > 0)
                    ? map.LowCoverStyle[index] - 1
                    : (
                        (map.Theme == 4)
                            ? 2
                            : ((map.Theme == 0) ? 2 : ((index / map.Columns + index % map.Columns / 3) % 3))
                    );
            int heightPixels = (isWall ? 25 : ((map.CoverHealth[index] < 40f) ? 5 : 9));
            Canvas.Diamond(screenX + 3, screenY + 4, 13, 6, C(44, 53, 45));
            if (!isWall && coverStyle == 2)
            {
                Canvas.Polygon(
                    new int[6] { screenX - 10, screenX - 6, screenX + 2, screenX + 9, screenX + 7, screenX - 3 },
                    new int[6]
                    {
                        screenY,
                        screenY - heightPixels + 1,
                        screenY - heightPixels - 2,
                        screenY - 3,
                        screenY + 3,
                        screenY + 4,
                    },
                    C(116, 123, 105)
                );
                Canvas.Polygon(
                    new int[4] { screenX - 6, screenX + 2, screenX + 9, screenX },
                    new int[4] { screenY - heightPixels + 1, screenY - heightPixels - 2, screenY - 3, screenY - 2 },
                    C(158, 159, 136)
                );
                Canvas.Line(screenX, screenY - 2, screenX + 7, screenY + 3, C(88, 99, 85));
                return;
            }

            if (!isWall && map.Theme == 1 && coverStyle == 0)
            {
                Canvas.Ellipse(screenX - 6, screenY + 3, 3, 2, C(28, 35, 32));
                Canvas.Ellipse(screenX + 6, screenY - 2, 3, 2, C(28, 35, 32));
                Canvas.Polygon(
                    new int[4] { screenX - 12, screenX + 4, screenX + 12, screenX - 4 },
                    new int[4] { screenY - 2, screenY - 9, screenY - 4, screenY + 3 },
                    C(104, 121, 99)
                );
                Canvas.Polygon(
                    new int[4] { screenX - 5, screenX + 1, screenX + 6, screenX },
                    new int[4] { screenY - 4, screenY - 7, screenY - 4, screenY - 1 },
                    C(50, 71, 73)
                );
                Canvas.Line(screenX - 10, screenY - 2, screenX - 5, screenY, C(165, 169, 129));
                return;
            }

            if (!isWall && coverStyle == 1)
            {
                Canvas.Rect(screenX - 6, screenY - heightPixels, 12, heightPixels + 1, C(89, 111, 100));
                Canvas.Ellipse(screenX, screenY + 1, 6, 3, C(65, 84, 74));
                Canvas.Ellipse(screenX, screenY - heightPixels, 6, 3, C(133, 149, 125));
                Canvas.Line(screenX - 5, screenY - 3, screenX + 5, screenY - 3, C(51, 70, 61));
                return;
            }

            uint surfaceColor;
            if (!isWall)
            {
                switch (coverStyle)
                {
                    default:
                        surfaceColor = C(139, 140, 125);
                        break;
                    case 1:
                        surfaceColor = C(99, 121, 111);
                        break;
                    case 0:
                        surfaceColor = C(127, 113, 77);
                        break;
                }
            }
            else
            {
                surfaceColor = C(151, 148, 125);
            }

            uint c = surfaceColor;
            Canvas.Polygon(
                new int[4] { screenX - 10, screenX, screenX, screenX - 10 },
                new int[4] { screenY, screenY + 5, screenY + 5 - heightPixels, screenY - heightPixels },
                isWall ? C(94, 98, 87) : C(77, 87, 72)
            );
            Canvas.Polygon(
                new int[4] { screenX, screenX + 10, screenX + 10, screenX },
                new int[4] { screenY + 5, screenY, screenY - heightPixels, screenY + 5 - heightPixels },
                isWall ? C(111, 115, 99) : C(98, 103, 80)
            );
            Canvas.Diamond(screenX, screenY - heightPixels, 10, 5, c);
            if (isWall)
            {
                for (int i = 6; i < heightPixels; i += 7)
                {
                    Canvas.Line(screenX - 9, screenY - i, screenX, screenY + 4 - i, C(77, 85, 76));
                    Canvas.Line(screenX, screenY + 4 - i, screenX + 9, screenY - i, C(88, 94, 81));
                }

                Canvas.Line(screenX - 5, screenY - 7, screenX - 5, screenY - 13, C(77, 85, 76));
            }
            else
            {
                switch (coverStyle)
                {
                    case 0:
                        Canvas.Line(screenX - 7, screenY - heightPixels, screenX + 6, screenY + 2, C(169, 151, 103));
                        Canvas.Line(screenX + 6, screenY - heightPixels, screenX - 6, screenY + 1, C(70, 74, 59));
                        break;
                    case 1:
                        Canvas.Ellipse(screenX, screenY - heightPixels, 7, 3, C(128, 149, 132));
                        Canvas.Line(screenX - 7, screenY - 3, screenX + 7, screenY - 3, C(52, 70, 63));
                        break;
                    default:
                        Canvas.Line(
                            screenX - 7,
                            screenY - heightPixels,
                            screenX - 2,
                            screenY - heightPixels - 2,
                            C(180, 177, 151)
                        );
                        break;
                }
            }

            if (!isWall && map.CoverHealth[index] < 70f)
            {
                Canvas.Line(screenX - 3, screenY - heightPixels, screenX + 2, screenY + 2, C(43, 49, 43));
            }
        }

        protected void DrawObjective(BattleSimulation b)
        {
            SetCompositionStage(Canvas.Gpu, 0);
            uint objectiveColor = (b.Contested ? C(246, 185, 98) : C(95, 212, 186));
            for (int i = 0; i < 48; i++)
            {
                double startAngle = (double)i * Math.PI * 2.0 / 48.0;
                double endAngle = (double)(i + 1) * Math.PI * 2.0 / 48.0;
                V2 v = P(b.Map.Objective + new V2((float)Math.Cos(startAngle), (float)Math.Sin(startAngle)) * 2.1f);
                V2 endPoint = P(b.Map.Objective + new V2((float)Math.Cos(endAngle), (float)Math.Sin(endAngle)) * 2.1f);
                if (i % 3 != 0 || b.Capture > (float)i / 48f)
                {
                    Canvas.Line((int)v.X, (int)v.Y, (int)endPoint.X, (int)endPoint.Y, objectiveColor);
                }
            }

            V2 v3 = P(b.Map.Objective);
            int centerX = (int)v3.X;
            int centerY = (int)v3.Y;
            Canvas.Diamond(centerX, centerY, 7, 3, C(55, 130, 120));
            SetCompositionStage(Canvas.Gpu, 1);
            Canvas.Line(centerX, centerY, centerX, centerY - 28, C(221, 222, 189));
            Canvas.Rect(centerX + 1, centerY - 28, 13, 7, objectiveColor);
            Canvas.Rect(centerX + 1, centerY - 21, 9, 2, objectiveColor);
        }

        public static bool IsLaunching(BattleSimulation b, Soldier s)
        {
            if (HandLauncher.Active(s) || s.State == ActionState.Launch)
            {
                return true;
            }

            foreach (ShotTrace trace in b.Traces)
            {
                if (b.Fog.Enabled && (!b.Fog.Revealed(trace.From) || !b.Fog.Revealed(trace.To)))
                    continue;
                if (trace.ShooterId == s.Id && trace.Rocket && trace.Until >= b.Time)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool WesternWeapon(Soldier s, int weapon)
        {
            return (weapon != 8)
                ? (WeaponBalance.Index(s) >= 6)
                : (s.WeaponModel == 3 || (WeaponBalance.Index(s) >= 6 && s.WeaponModel != 2));
        }

        public static int WeaponWidth(int weapon) =>
            weapon == 11 ? 36
            : weapon == 9 ? 33
            : weapon == 10 ? 24
            : 28;

        static void DrawCarriedWeapon(
            PixelCanvas canvas,
            int weapon,
            int variant,
            float x,
            float y,
            bool enemy,
            float angle,
            WeaponUpgrade upgrade = null
        )
        {
            int appearance = Rubezh.WeaponAppearance.Sprite(variant, upgrade);
            if (appearance >= 0 && weapon == 8)
            {
                int w = WeaponWidth(weapon);
                Rubezh.Graphics.StudioSprite(
                    canvas,
                    appearance,
                    x,
                    y,
                    w,
                    w * Rubezh.WeaponAppearance.Aspect(appearance),
                    angle
                );
                return;
            }
            variant %= 6;
            int width = WeaponWidth(weapon);
            if (weapon == 11)
                SniperSprites.Draw(canvas, variant, x, y, width, SniperSprites.Height(variant, width), angle);
            else if (weapon == 8 && (variant == 2 || variant == 3))
                UnderbarrelSprites.Draw(canvas, variant, x, y, width, UnderbarrelSprites.Height(variant, width), angle);
            else
                PawnSprites.Draw(canvas, weapon, x, y, width, WeaponHeight(weapon, width, enemy), enemy, angle);
        }

        public static int WeaponHeight(int weapon, int width, bool enemy)
        {
            if (weapon == 11)
            {
                return SniperSprites.Height(enemy ? 5 : 4, width);
            }

            PawnSprites.Sprite sprite = PawnSprites.Atlas[weapon + (enemy ? 13 : 0)];
            return (int)Math.Round((float)(Math.Abs(width) * sprite.H) / (float)sprite.W);
        }

        public static void WeaponPose(
            BattleSimulation b,
            Soldier s,
            bool rocket,
            out V2 center,
            out V2 muzzle,
            out float aim,
            out int weapon,
            out bool left
        )
        {
            V2 v = Project(s.Position + s.Exchange.Visual, b.Map);
            V2 poseFacing = SomaliFire.Visual(s, b.Time) ? (s.SomaliTarget - s.SomaliMuzzle).Normalized : s.Facing;
            float facingX = (poseFacing.X + poseFacing.Y) * 11f;
            float facingY = (poseFacing.Y - poseFacing.X) * 5f;
            aim = (float)Math.Atan2(facingY, facingX);
            left = facingX < 0f;
            weapon = (rocket ? 10 : (s.GunSniper ? 11 : (s.GunMachineGunner ? 9 : 8)));
            int stanceOffset = (s.Broken ? 7 : (s.Crouched ? 4 : 0));
            float walkBob = (s.Moving ? ((float)Math.Sin(b.Time * 10f + (float)s.Id) * 0.85f) : 0f);
            float weaponY = (float)Math.Round(v.Y) - 10f + (float)stanceOffset + (float)Math.Round(walkBob);
            float recoil = ((b.Time - s.LastFire < 0.1f) ? 2 : 0);
            V2 a = new V2((float)Math.Cos(aim), (float)Math.Sin(aim));
            center = new V2((float)Math.Round(v.X), weaponY + (s.Side == Side.Green ? -2f : 3f)) + a * (3.5f - recoil);
            float weaponWidth = WeaponWidth(weapon);
            float weaponHeight = WeaponHeight(weapon, (int)weaponWidth, WesternWeapon(s, weapon));
            float barrelOffset = (
                (weapon == 11)
                    ? SniperSprites.BarrelY[(s.WeaponModel == 5) ? 1 : 0]
                    : PawnSprites.BarrelY[(WesternWeapon(s, weapon) ? 3 : 0) + weapon - 8]
            );
            if (s.Underbarrel && weapon == 8)
            {
                weaponHeight = UnderbarrelSprites.Height(s.WeaponModel, (int)weaponWidth);
                barrelOffset = UnderbarrelSprites.BarrelY[(s.WeaponModel == 3) ? 1 : 0];
            }

            int appearance = Rubezh.WeaponAppearance.Sprite(WeaponBalance.Index(s), s.WeaponUpgrade);
            if (weapon == 8 && appearance >= 0)
            {
                weaponHeight = weaponWidth * Rubezh.WeaponAppearance.Aspect(appearance);
                barrelOffset = Rubezh.WeaponAppearance.BarrelY(appearance);
            }
            V2 a2 = new V2(0f - a.Y, a.X) * ((!left) ? 1 : (-1));
            muzzle = center + a * (weaponWidth * 0.5f) + a2 * (barrelOffset * weaponHeight);
            if (!rocket && SomaliFire.Visual(s, b.Time))
            {
                V2 extended = Project(s.SomaliMuzzle, b.Map);
                extended.Y -= s.SomaliLow ? 36 : 14;
                muzzle = extended;
                center = muzzle - a * (weaponWidth * .5f) - a2 * (barrelOffset * weaponHeight);
            }
        }

        public static int FacingFrame(V2 facing)
        {
            float x = (facing.X + facing.Y) * 11f,
                y = (facing.Y - facing.X) * 5f;
            return Math.Abs(y) > Math.Abs(x) * .55f ? (y < 0f ? 2 : 0) : (x > 0f ? 1 : 3);
        }

        protected virtual void BeginHeldWeaponVisual() { }

        protected virtual void EndHeldWeaponVisual() { }

        protected void DrawHeldWeapon(BattleSimulation b, Soldier s)
        {
            float savedDepth = Canvas.Depth;
            if (SomaliFire.Visual(s, b.Time))
                Canvas.Depth = 256f + s.SomaliMuzzle.Y - s.SomaliMuzzle.X + .08f;
            bool isGreen = s.Side == Side.Green;
            WeaponPose(
                b,
                s,
                IsLaunching(b, s),
                out var center,
                out var muzzle,
                out var aim,
                out var weapon,
                out var left
            );
            if (!s.Broken && s.State != ActionState.Treat && s.RescuePhase != RescuePhase.Drag)
            {
                BeginHeldWeaponVisual();
                int spriteWidth = WeaponWidth(weapon);
                int appearance = Rubezh.WeaponAppearance.Sprite(WeaponBalance.Index(s), s.WeaponUpgrade);
                if (isGreen && weapon == 10)
                    Rubezh.Graphics.StudioSprite(
                        Canvas,
                        6829 + (s.LauncherKind == 1 ? 1 : 0),
                        center.X,
                        center.Y,
                        left ? 24 : -24,
                        s.LauncherKind == 1 ? 5 : 7,
                        left ? aim - MathF.PI : aim
                    );
                else if (weapon == 8 && appearance >= 0)
                {
                    Rubezh.Graphics.StudioSprite(
                        Canvas,
                        appearance,
                        center.X,
                        center.Y,
                        left ? -spriteWidth : spriteWidth,
                        spriteWidth * Rubezh.WeaponAppearance.Aspect(appearance),
                        left ? aim - MathF.PI : aim
                    );
                }
                else if (weapon == 11)
                {
                    SniperSprites.Draw(
                        Canvas,
                        s.WeaponModel,
                        center.X,
                        center.Y,
                        left ? (-spriteWidth) : spriteWidth,
                        SniperSprites.Height(s.WeaponModel, spriteWidth),
                        left ? (aim - MathF.PI) : aim
                    );
                }
                else if (s.Underbarrel && weapon == 8)
                {
                    UnderbarrelSprites.Draw(
                        Canvas,
                        s.WeaponModel,
                        center.X,
                        center.Y,
                        left ? (-spriteWidth) : spriteWidth,
                        UnderbarrelSprites.Height(s.WeaponModel, spriteWidth),
                        left ? (aim - MathF.PI) : aim
                    );
                }
                else
                {
                    PawnSprites.Draw(
                        Canvas,
                        weapon,
                        center.X,
                        center.Y,
                        left ? (-spriteWidth) : spriteWidth,
                        WeaponHeight(weapon, spriteWidth, WesternWeapon(s, weapon)),
                        WesternWeapon(s, weapon),
                        left ? (aim - MathF.PI) : aim
                    );
                }

                V2 a = new V2((float)Math.Cos(aim), (float)Math.Sin(aim));
                // Keep hands on the same weapon-local side through mirroring and recoil.
                V2 below = new V2(-a.Y, a.X) * (left ? -1f : 1f);
                float handScale =
                    weapon == 10
                        ? 1f
                        : spriteWidth
                            / (
                                weapon == 11 ? 27f
                                : weapon == 9 ? 25f
                                : 21f
                            );
                V2 grip = center - a * (3f * handScale) + below * (1.2f * handScale);
                V2 support = center + a * (3f * handScale) + below * (1.2f * handScale);
                Rubezh.Graphics.Hand(Canvas, false, left, grip.X, grip.Y, aim, !isGreen);
                Rubezh.Graphics.Hand(Canvas, true, left, support.X, support.Y, aim, !isGreen);
                EndHeldWeaponVisual();
            }

            Canvas.Depth = savedDepth;
        }

        protected void DrawSoldier(BattleSimulation b, Soldier s, bool selected)
        {
            var pivot = P(s.Position + s.Exchange.Visual);
            Canvas.SetObjectTransform(pivot.X, pivot.Y, .85f, .85f, 0);
            var groups = Canvas.Gpu as IVisualGroupGpu;
            groups?.BeginVisual(s.Id, pivot.X, pivot.Y);
            try
            {
                DrawSoldierVisual(b, s, selected);
            }
            finally
            {
                Canvas.ClearObjectTransform();
                groups?.EndVisual();
            }
        }

        // A section can be refreshed independently without rebuilding the body recipe.
        // -1 is the reference editor renderer; 0 ground markers, 1 body, 2 status.
        protected int SoldierVisualSection = -1;
        protected bool SoldierBody => SoldierVisualSection < 0 || SoldierVisualSection == 1;
        protected bool SoldierStatus => SoldierVisualSection < 0 || SoldierVisualSection == 2;
        protected bool SoldierUnderlay => SoldierVisualSection < 0 || SoldierVisualSection == 0;

        protected void DrawSoldierVisual(BattleSimulation b, Soldier s, bool selected)
        {
            if (!s.Deployed)
            {
                return;
            }

            V2 v = P(s.Position + s.Exchange.Visual);
            int screenX = (int)Math.Round(v.X);
            int screenY = (int)Math.Round(v.Y);
            bool isGreen = s.Side == Side.Green;
            if (SoldierUnderlay)
                Canvas.Ellipse(screenX, screenY + 1, 8, 3, C(38, 50, 43));
            if (!s.Alive)
            {
                if (SoldierBody)
                {
                    Rubezh.Graphics.PersonSprite(
                        Canvas,
                        Rubezh.Graphics.CorpseFrame(s),
                        screenX,
                        screenY - 4,
                        32,
                        !isGreen,
                        Rubezh.Graphics.CorpseAngle(s),
                        s.ArmorLevel,
                        s.RigLevel,
                        s.Look,
                        s.Role,
                        s.Id
                    );
                    for (int k = 0; k < CasualtyVisuals.MarkCount(s.Id); k++)
                    {
                        var dot = CasualtyVisuals.Mark(s.Id, k);
                        Canvas.Ellipse(screenX + (int)dot.X, screenY + (int)dot.Y, 1, 1, C(163, 30, 33));
                    }
                }
                return;
            }

            if (
                s.Incapacitated
                || (
                    s.RescuerId >= 0
                    && s.RescuerId < b.Soldiers.Count
                    && b.Soldiers[s.RescuerId].RescuePhase == RescuePhase.Drag
                )
            )
            {
                if (
                    s.RescuerId >= 0
                    && s.RescuerId < b.Soldiers.Count
                    && b.Soldiers[s.RescuerId].RescuePhase == RescuePhase.Drag
                )
                {
                    screenX -= 8;
                    screenY += 2;
                }

                if (SoldierBody)
                    Rubezh.Graphics.PersonSprite(
                        Canvas,
                        FacingFrame(s.Facing),
                        screenX,
                        screenY - 4,
                        32,
                        !isGreen,
                        1.3f,
                        s.ArmorLevel,
                        s.RigLevel,
                        s.Look,
                        s.Role,
                        s.Id
                    );
                if (SoldierStatus)
                {
                    Canvas.TestDepth = false;
                    uint c = C(247, 190, 110);
                    Canvas.Rect(screenX - 1, screenY - 25, 2, 8, c);
                    Canvas.Rect(screenX - 4, screenY - 22, 8, 2, c);
                    Canvas.Rect(screenX - 6, screenY - 15, 12, 2, C(40, 49, 45));
                    Canvas.Rect(screenX - 6, screenY - 15, (int)(s.Health * 0.12f), 2, c);
                    if (selected)
                    {
                        Canvas.Frame(screenX - 16, screenY - 15, 33, 23, C(230, 232, 178));
                    }
                }
                return;
            }

            uint teamAccent = (isGreen ? C(161, 220, 118) : C(245, 137, 108));
            if (SoldierUnderlay)
            {
                if (selected)
                {
                    Canvas.Diamond(screenX, screenY + 1, 12, 5, C(221, 228, 177));
                    Canvas.Diamond(screenX, screenY + 1, 10, 3, C(44, 61, 51));
                }

                Canvas.Line(screenX - 6, screenY + 2, screenX, screenY + 5, teamAccent);
                Canvas.Line(screenX, screenY + 5, screenX + 6, screenY + 2, teamAccent);
            }
            bool isLeader = b.Team(s).LeaderId == s.Id;
            int stanceOffset = (s.Broken ? 7 : (s.Crouched ? 4 : 0));
            WeaponPose(b, s, IsLaunching(b, s), out var center, out var _, out var aim, out var weapon, out var left);
            float facingX = (s.Facing.X + s.Facing.Y) * 11f;
            float facingY = (s.Facing.Y - s.Facing.X) * 5f;
            int facingFrame = FacingFrame(s.Facing);
            float walkBob = (s.Moving ? ((float)Math.Sin(b.Time * 10f + (float)s.Id) * 0.85f) : 0f);
            int torsoY = screenY - 10 + stanceOffset + (int)Math.Round(walkBob);
            int headY = torsoY - 13;
            bool hasBackLauncher = (s.Rockets > 0 && !IsLaunching(b, s)) || s.ReserveLauncherKind >= 0;
            int bodyX = screenX;
            int attachmentOffset;
            switch (facingFrame)
            {
                default:
                    attachmentOffset = -2;
                    break;
                case 0:
                    attachmentOffset = 6;
                    break;
                case 3:
                    attachmentOffset = 6;
                    break;
                case 1:
                    attachmentOffset = -6;
                    break;
            }

            float cx = bodyX + attachmentOffset;
            float cy = torsoY - 3;
            if (SoldierBody)
            {
                if (facingFrame == 2 && !SomaliFire.Visual(s, b.Time))
                    DrawHeldWeapon(b, s); // Far-side hands and weapon, then torso/head.
                if (s.HasBackWeapon)
                {
                    int backSprite =
                        s.BackGunRole == Role.Sniper ? 11
                        : s.BackGunRole == Role.MachineGunner ? 9
                        : 8;
                    DrawCarriedWeapon(Canvas, backSprite, s.BackVariant, cx, cy, !isGreen, -1.15f, s.BackWeaponUpgrade);
                }

                if (HandLauncher.Active(s))
                    DrawCarriedWeapon(
                        Canvas,
                        s.GunSniper ? 11
                            : s.GunMachineGunner ? 9
                            : 8,
                        WeaponBalance.Index(s),
                        cx,
                        cy,
                        WesternWeapon(s, s.GunMachineGunner ? 9 : 8),
                        -1.15f,
                        s.WeaponUpgrade
                    );
                if (hasBackLauncher && facingFrame != 2)
                    DrawBackLaunchers(b, s, cx, cy, isGreen, facingFrame == 3 ? 1.15f : -1.15f);

                bool medicalBag = SupplyRecovery.Medical(s),
                    hasBag = medicalBag || s.SupplyPack;
                // Front view: backpack sits behind the torso. Side/rear views: outer pack is visible.
                if (hasBag && facingFrame == 0)
                    Rubezh.Graphics.Backpack(Canvas, medicalBag, facingFrame, screenX, torsoY);
                Rubezh.Graphics.PersonSprite(
                    Canvas,
                    facingFrame,
                    screenX,
                    screenY - 16 + stanceOffset + (int)Math.Round(walkBob),
                    36,
                    !isGreen,
                    0,
                    s.ArmorLevel,
                    s.RigLevel,
                    s.Look,
                    s.Role,
                    s.Id
                );
                if (hasBag && facingFrame != 0)
                    Rubezh.Graphics.Backpack(Canvas, medicalBag, facingFrame, screenX, torsoY);
                if (hasBackLauncher && facingFrame == 2)
                    DrawBackLaunchers(b, s, cx, cy, isGreen, facingFrame == 3 ? 1.15f : -1.15f);
            }
            if (SoldierStatus && isLeader)
            {
                uint commanderAccent = C(247, 211, 111);
                Canvas.Rect(screenX - 5, headY - 5, 10, 2, commanderAccent);
                Canvas.Line(screenX - 10, torsoY - 3, screenX - 10, headY - 12, outline);
                Canvas.Dot(screenX - 10, headY - 12, commanderAccent);
                Canvas.Line(screenX - 3, screenY - 48, screenX, screenY - 51, commanderAccent);
                Canvas.Line(screenX, screenY - 51, screenX + 3, screenY - 48, commanderAccent);
                Canvas.Line(screenX - 3, screenY - 45, screenX, screenY - 48, commanderAccent);
                Canvas.Line(screenX, screenY - 48, screenX + 3, screenY - 45, commanderAccent);
            }

            if (SoldierBody && (facingFrame != 2 || SomaliFire.Visual(s, b.Time)))
                DrawHeldWeapon(b, s);
            if (!SoldierStatus)
                return;
            Canvas.TestDepth = false;
            if (s.Suppression > 0.45f)
            {
                Canvas.Rect(screenX - 6, screenY - 37, 12, 2, C(39, 42, 39));
                Canvas.Rect(screenX - 6, screenY - 37, (int)(12f * s.Suppression), 2, C(238, 183, 92));
            }

            Canvas.Rect(screenX - 6, screenY - 40, 12, 2, C(39, 42, 39));
            Canvas.Rect(screenX - 6, screenY - 40, Math.Max(1, (int)(12f * s.Health / 100f)), 2, teamAccent);
            // Broken is now rendered as a small red crossed badge in Unity UI
            // for all graphics presets; no additional purple icon in HD canvas.
            if (!s.Broken && s.SupportReady)
            {
                Canvas.Rect(screenX + 8, screenY - 37, 3, 3, C(133, 221, 210));
            }

            if (s.State == ActionState.Treat)
            {
                uint c2 = C(179, 245, 215);
                Canvas.Rect(screenX + 8, screenY - 19, 2, 8, c2);
                Canvas.Rect(screenX + 5, screenY - 16, 8, 2, c2);
            }

            if (s.State == ActionState.Reload)
            {
                Canvas.Rect(screenX + 7, torsoY - 4, 2, 5, C(236, 200, 117));
            }
        }

        protected void DrawDebug(BattleSimulation b, int selected)
        {
            Squad green = b.Green;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (soldier.Side == Side.Green && soldier.Alive)
                {
                    bool isSupport = green.SupportIds.Contains(soldier.Id);
                    if (isSupport || green.ManeuverIds.Contains(soldier.Id))
                    {
                        V2 b2 = P(soldier.Position);
                        V2 a = P(soldier.ManeuverPaused ? soldier.SavedManeuverAnchor : soldier.OrderAnchor);
                        uint color = (isSupport ? C(100, 218, 216) : C(164, 221, 112));
                        Canvas.Line((int)b2.X, (int)b2.Y, (int)a.X, (int)a.Y, color);
                        V2 normalized = (a - b2).Normalized;
                        V2 a2 = new V2(0f - normalized.Y, normalized.X);
                        V2 v = a - normalized * 8f + a2 * 4f;
                        V2 arrowLeft = a - normalized * 8f - a2 * 4f;
                        Canvas.Line((int)v.X, (int)v.Y, (int)a.X, (int)a.Y, color);
                        Canvas.Line((int)arrowLeft.X, (int)arrowLeft.Y, (int)a.X, (int)a.Y, color);
                    }
                }
            }

            foreach (V2 item in green.PlannedSmoke)
            {
                V2 v3 = P(item);
                Canvas.Diamond((int)v3.X, (int)v3.Y, 14, 7, C(201, 213, 215));
                Canvas.Diamond((int)v3.X, (int)v3.Y, 11, 5, C(70, 91, 98));
            }

            foreach (Soldier teammate in b.Soldiers)
            {
                if (!teammate.Alive || (selected >= 0 && selected != teammate.Id))
                {
                    continue;
                }

                uint pathColor = ((teammate.Side == Side.Green) ? C(88, 154, 116) : C(172, 102, 85));
                V2 v4 = P(teammate.Position);
                for (int i = teammate.PathCursor; i < teammate.Path.Count; i++)
                {
                    V2 v5 = P(teammate.Path[i]);
                    Canvas.Line((int)v4.X, (int)v4.Y, (int)v5.X, (int)v5.Y, pathColor);
                    v4 = v5;
                }

                Canvas.Frame((int)v4.X - 2, (int)v4.Y - 2, 4, 4, pathColor);
                if (teammate.Id != selected)
                {
                    continue;
                }

                foreach (Contact value in teammate.Contacts.Values)
                {
                    V2 v6 = P(value.Position);
                    Canvas.Frame((int)v6.X - 4, (int)v6.Y - 4, 8, 8, C(232, 183, 113));
                }
            }
        }

        void DrawBackLaunchers(BattleSimulation battle, Soldier s, float x, float y, bool green, float angle)
        {
            bool primary = s.Rockets > 0 && !IsLaunching(battle, s);
            bool reserve = s.ReserveLauncherKind >= 0;
            if (!primary && !reserve)
                return;
            float first = BackLauncherHeight(s.LauncherKind, green);
            float second = BackLauncherHeight(s.ReserveLauncherKind, green);
            float separation = primary && reserve ? ((first + second) * .5f + 1f) * .5f : 0;
            if (primary)
                DrawBackLauncher(s.LauncherKind, x, y, green, angle, -separation * .5f);
            if (reserve)
                DrawBackLauncher(s.ReserveLauncherKind, x, y, green, angle, separation * .5f);
        }

        static float BackLauncherHeight(int kind, bool green) =>
            green ? (kind == 1 ? 5f : 7f) * 1.75f : WeaponHeight(10, 24, true);

        void DrawBackLauncher(int kind, float x, float y, bool green, float angle, float offset)
        {
            x -= (float)Math.Sin(angle) * offset;
            y += (float)Math.Cos(angle) * offset;
            float height = BackLauncherHeight(kind, green);
            if (green)
                Rubezh.Graphics.StudioSprite(Canvas, 6829 + (kind == 1 ? 1 : 0), x, y, 24, height, angle);
            else
                PawnSprites.Draw(Canvas, 10, x, y, 24, (int)Math.Round(height), true, angle);
        }
    }
}
