#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
using System;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    // Compatibility renderer for editor diagnostics and portable reference images.
    // The Player uses NativeBattleComposer and never invokes this orchestration.
    public sealed class BattleRenderer : BattleVisualRecipes
    {
        public BattleRenderer()
            : base() { }

        public BattleRenderer(float scale)
            : base(scale) { }

        public void RenderVisibleSoldiers(BattleSimulation b, int selected = -1)
        {
            RenderVisibleSoldiers(b, selected, null);
        }

        public void RenderVisibleSoldiers(BattleSimulation b, int selected, BattleRenderer sceneSource)
        {
            PrepareCanvas(b.Map);
            var nativeScene = Canvas.Gpu as IStaticSceneGpu;
            if (nativeScene != null && nativeScene.NativeScene)
            {
                PrepareNativeScene(b.Map);
                NativeVehicleDepth(b, nativeScene);
            }
            else
            {
                float[] depth;
                if (sceneSource != null && sceneSource != this)
                {
                    sceneSource.PrepareCanvas(b.Map);
                    if (
                        sceneSource.Canvas.Width != Canvas.Width
                        || sceneSource.Canvas.Height != Canvas.Height
                        || sceneSource.Canvas.Scale != Canvas.Scale
                    )
                        throw new ArgumentException("Foreground and scenery scales differ.", nameof(sceneSource));
                    sceneSource.EnsureScenery(b.Map);
                    depth = sceneSource.sceneryDepth;
                    // A renderer switching from standalone to shared foreground no longer owns a scene cache.
                    scenery = null;
                    sharpGround = null;
                    basePixels = null;
                    sceneryDepth = null;
                    sceneryItems = null;
                    sceneryBounds.Clear();
                    cachedMap = null;
                    cachedRevision = -1;
                    activeMap = b.Map;
                    if (Canvas.Occlusion == null)
                        Canvas.Occlusion = new float[Canvas.Pixels.Length];
                }
                else
                {
                    EnsureScenery(b.Map);
                    depth = sceneryDepth;
                }

                bool vehicleDepth =
                    (b.GreenVehicle != null && b.GreenVehicle.Active) || (b.RedVehicle != null && b.RedVehicle.Active);
                if (Canvas.Gpu == null || vehicleDepth)
                    Array.Copy(depth, Canvas.Occlusion, Canvas.Occlusion.Length);
                var foregroundSink = Canvas.Gpu;
                Canvas.Gpu = null; // vehicle depth belongs to the CPU occlusion mask, not the foreground image
                Canvas.RecordDepth = true;
                try
                {
                    DrawVehicleArrival(b, b.GreenVehicle);
                    DrawVehicleArrival(b, b.RedVehicle);
                }
                finally
                {
                    Canvas.RecordDepth = false;
                    Canvas.Gpu = foregroundSink;
                }
            }

            if (Canvas.Gpu == null)
                Array.Clear(Canvas.Pixels, 0, Canvas.Pixels.Length);
            var destination = Canvas.Gpu;
            ordered.Begin(Canvas, destination);
            Canvas.Gpu = ordered;
            try
            {
                foreach (var unit in b.Soldiers)
                {
                    if (!unit.Deployed || !b.Fog.CanDraw(unit))
                        continue;
                    Canvas.TestDepth = true;
                    Canvas.Depth = 256f + unit.Position.Y - unit.Position.X + .06f;
                    ordered.Stage = unit.Alive ? 1 : 0;
                    DrawSoldier(b, unit, unit.Id == selected);
                }
            }
            finally
            {
                Canvas.Gpu = destination;
                ordered.Flush();
            }

            // Software sprite blending is premultiplied; GUI textures use straight alpha.
            if (destination == null)
                for (int i = 0; i < Canvas.Pixels.Length; i++)
                {
                    uint pixel = Canvas.Pixels[i];
                    int alpha = (int)(pixel >> 24);
                    if (alpha == 0 || alpha == 255)
                        continue;
                    int red = Math.Min(255, (int)(pixel & 255) * 255 / alpha),
                        green = Math.Min(255, (int)((pixel >> 8) & 255) * 255 / alpha),
                        blue = Math.Min(255, (int)((pixel >> 16) & 255) * 255 / alpha);
                    Canvas.Pixels[i] = (uint)(red | (green << 8) | (blue << 16) | (alpha << 24));
                }
        }

        public void Render(BattleSimulation b, bool debug = false, int selected = -1)
        {
            using (var sample = new SilentOrders.Core.CpuSample("Rubezh.Render.Compose"))
            {
                PrepareCanvas(b.Map);
                var nativeScene = Canvas.Gpu as IStaticSceneGpu;
                bool native = nativeScene != null && nativeScene.NativeScene;
                if (native)
                    PrepareNativeScene(b.Map);
                else
                    EnsureScenery(b.Map);
                if (native)
                    RenderNativeScene(b.Map, nativeScene);
                else if (Canvas.Gpu == null)
                    Array.Copy(scenery, Canvas.Pixels, scenery.Length);
                else
                {
                    bool sharp = SharpScenery && Canvas.Gpu is IProceduralGpu && sharpGround != null;
                    var groundGpu = sharp && b.Map.TerrainPicture == null ? Canvas.Gpu as IGroundChunkGpu : null;
                    Canvas.Gpu.Background(
                        groundGpu != null ? basePixels
                            : sharp ? sharpGround
                            : scenery,
                        sceneryDepth,
                        cachedMap,
                        cachedRevision
                    );
                    if (groundGpu != null)
                    {
                        if (drawGroundCells == null)
                            drawGroundCells = DrawGroundCells;
                        groundGpu.GroundChunks(b.Map, drawGroundCells);
                    }
                    if (sharp)
                    {
                        var origin = P(new V2(0, 0));
                        ((IProceduralGpu)Canvas.Gpu).Pictures(
                            b.Map.ProceduralPictures,
                            origin.X * Canvas.Scale,
                            origin.Y * Canvas.Scale,
                            Canvas.Scale
                        );
                        Canvas.TestDepth = true;
                        foreach (var item in VisibleSceneryItems())
                        {
                            var o = item.Decoration ?? StudioRules.At(b.Map, item.TileId);
                            if (o != null && o.Destroyed)
                                continue;
                            if (
                                o == null
                                && b.Map.Tiles[item.TileId] != Tile.LowCover
                                && b.Map.Tiles[item.TileId] != Tile.Wall
                            )
                                continue;
                            DrawSceneryItem(b.Map, item);
                        }
                    }
                }
                DrawCraters(b);
                var destination = Canvas.Gpu;
                var direct = destination as INativeComposition;
                bool retained = direct != null && direct.DirectComposition;
                if (retained)
                    direct.BeginDynamic();
                else
                {
                    ordered.Begin(Canvas, destination);
                    Canvas.Gpu = ordered;
                }
                try
                {
                    Canvas.TestDepth = false;
                    if (debug && !b.Fog.Enabled)
                    {
                        DrawDebug(b, selected);
                    }

                    Canvas.TestDepth = true;
                    Canvas.Depth = 256f + b.Map.Objective.Y - b.Map.Objective.X;
                    DrawObjective(b);
                    SetCompositionStage(destination, -1); // Ground stains precede corpses, equipment and debris.
                    DrawBloodTraces(b);
                    SetCompositionStage(destination, 0);
                    DrawGroundEquipment(b, false); // Landed equipment is BELOW all actors.
                    DrawCoverDebris(b, false);
                    SetCompositionStage(destination, 1);
                    // OrderedCanvas interleaves all actors, vehicles and flying items in one pass.
                    DrawVehicleArrival(b, b.GreenVehicle);
                    DrawVehicleArrival(b, b.RedVehicle);
                    foreach (var item in b.Soldiers)
                    {
                        if (!item.Deployed || !b.Fog.CanDraw(item))
                            continue;
                        Canvas.TestDepth = true;
                        Canvas.Depth = 256f + item.Position.Y - item.Position.X + .06f;
                        SetCompositionStage(destination, item.Alive ? 1 : 0);
                        DrawSoldier(b, item, item.Id == selected);
                    }

                    SetCompositionStage(destination, 1);
                    Canvas.TestDepth = false;
                    foreach (Soldier soldier in b.Soldiers)
                    {
                        if (
                            b.Fog.CanDraw(soldier)
                            && soldier.Alive
                            && soldier.RescuePhase == RescuePhase.Drag
                            && soldier.RescueTargetId >= 0
                        )
                        {
                            Canvas.TestDepth = true;
                            Canvas.Depth = 256f + soldier.Position.Y - soldier.Position.X + .06f;
                            V2 v = P(soldier.Position);
                            V2 patientScreenPosition = P(b.Soldiers[soldier.RescueTargetId].Position);
                            Canvas.Line(
                                (int)v.X,
                                (int)v.Y - 9,
                                (int)patientScreenPosition.X - 8,
                                (int)patientScreenPosition.Y - 2,
                                C(223, 190, 143)
                            );
                        }
                    }

                    DrawGroundEquipment(b, true); // Sorted with actors by current world position.
                    DrawCoverDebris(b, true);
                    if (!(destination is INativeEffectsGpu nativeEffects) || !nativeEffects.OwnsBattleEffects)
                    {
                        foreach (ShotTrace trace in b.Traces)
                        {
                            if (b.Fog.Enabled && (!b.Fog.Revealed(trace.From) || !b.Fog.Revealed(trace.To)))
                                continue;
                            Canvas.TestDepth = true;
                            Canvas.Depth = 256f + (trace.From.Y + trace.To.Y - trace.From.X - trace.To.X) * .5f;
                            V2 tail = trace.Ballistic ? trace.Tail : trace.From;
                            V2 muzzle = P(tail);
                            V2 v3 = P(trace.To);
                            v3.Y -= trace.Ballistic ? trace.ToHeight * 9f : 9f;
                            if (trace.Ballistic)
                            {
                                muzzle.Y -= trace.TailHeight * 9f;
                            }
                            else if (trace.ShooterId >= 0 && trace.ShooterId < b.Soldiers.Count)
                            {
                                WeaponPose(
                                    b,
                                    b.Soldiers[trace.ShooterId],
                                    trace.Rocket,
                                    out var _,
                                    out muzzle,
                                    out var _,
                                    out var _,
                                    out var _
                                );
                                var foot = P(
                                    b.Soldiers[trace.ShooterId].Position + b.Soldiers[trace.ShooterId].Exchange.Visual
                                );
                                muzzle = foot + (muzzle - foot) * .85f;
                            }
                            else
                            {
                                muzzle.Y -= 11f;
                            }

                            int segments = Math.Max(
                                1,
                                (int)Math.Ceiling(Math.Max(Math.Abs(v3.X - muzzle.X), Math.Abs(v3.Y - muzzle.Y)) / 3f)
                            );
                            if (trace.Ballistic && V2.Distance(trace.From, trace.To) < .001f)
                                segments = 0;
                            for (int part = 0; part < segments; part++)
                            {
                                float t0 = part / (float)segments,
                                    t1 = (part + 1) / (float)segments,
                                    mid = (t0 + t1) * .5f;
                                Canvas.Depth = 256f + (tail.Y - tail.X) * (1f - mid) + (trace.To.Y - trace.To.X) * mid;
                                Canvas.Line(
                                    (int)(muzzle.X + (v3.X - muzzle.X) * t0),
                                    (int)(muzzle.Y + (v3.Y - muzzle.Y) * t0),
                                    (int)(muzzle.X + (v3.X - muzzle.X) * t1),
                                    (int)(muzzle.Y + (v3.Y - muzzle.Y) * t1),
                                    trace.Side == Side.Green ? C(242, 218, 144) : C(244, 157, 100)
                                );
                                if (trace.Fragment)
                                    Canvas.Line(
                                        (int)muzzle.X,
                                        (int)muzzle.Y + 1,
                                        (int)v3.X,
                                        (int)v3.Y + 1,
                                        C(242, 218, 144)
                                    );
                            }

                            Canvas.Depth = 256f + trace.From.Y - trace.From.X + .06f;
                            if (
                                !trace.Fragment && (!trace.Ballistic || !trace.Ricochet && b.Time - trace.Born <= .075f)
                            )
                            {
                                V2 flash = trace.Ballistic ? P(trace.From) + trace.MuzzleOffset : muzzle;
                                Canvas.Rect((int)flash.X - 1, (int)flash.Y - 1, 3, 3, C(255, 242, 182));
                            }

                            if (trace.Ballistic && V2.Distance(trace.From, trace.To) > .001f)
                            {
                                Canvas.Depth = 256f + trace.To.Y - trace.To.X + .06f;
                                Canvas.Rect((int)v3.X, (int)v3.Y, 1, 1, C(255, 245, 202));
                            }

                            if (trace.Hit)
                            {
                                Canvas.Depth = 256f + trace.To.Y - trace.To.X + .12f;
                                Canvas.Line(
                                    (int)v3.X - 2,
                                    (int)v3.Y - 2,
                                    (int)v3.X + 2,
                                    (int)v3.Y + 2,
                                    C(249, 227, 185)
                                );
                            }
                        }

                        foreach (var spark in b.Ballistics.Sparks)
                        {
                            float h;
                            V2 world = Ballistics.SparkPosition(spark, b.Time, out h),
                                point = P(world);
                            if (!b.Fog.Revealed(world))
                                continue;
                            point.Y -= h * 9f;
                            Canvas.TestDepth = true;
                            Canvas.Depth = 256f + world.Y - world.X + .08f;
                            uint tint = spark.Dust ? C(170, 150, 112) : C(255, 211, 119);
                            Canvas.Line(
                                (int)point.X,
                                (int)point.Y,
                                (int)(point.X + spark.Direction.X * 3),
                                (int)(point.Y - spark.Direction.Y * 3),
                                tint
                            );
                        }

                        DrawEffects(b);
                    }
                }
                finally
                {
                    Canvas.Gpu = destination;
                    if (retained)
                        direct.EndDynamic();
                }

                if (!retained)
                    ordered.Flush();
                DrawCachedRoofs(b);
            }
        }
    }
}

#endif
