using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public int UnityObjectMaskBuilds { get; private set; }
        public int UnityRoofMaskBuilds { get; private set; }
        public bool UnityPackedMasks { get; private set; }
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        bool? packedMaskSupported;

        static RenderTexture CreateMaskTarget(int width, int height, RenderTextureFormat format, string name)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (texture.Create() && texture.format == format)
                return texture;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        bool EnsureUnityMaskTargets(int width, int height)
        {
            if (!packedMaskSupported.HasValue)
                packedMaskSupported =
                    SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat)
                    && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.RGFloat);
            bool packed = packedMaskSupported.Value && CanUseMergedMaskGeometry();
            if (
                unityMask != null
                && unityMask.width == width
                && unityMask.height == height
                && unityMask.IsCreated()
                && packed == UnityPackedMasks
                && (packed || (unityRoofMask != null && unityRoofMask.IsCreated()))
            )
                return false;

            ReleaseUnityMask();
            if (packed)
            {
                // MAX blends the channels independently. Moving vehicles contribute to red only.
                unityMask = CreateMaskTarget(width, height, RenderTextureFormat.RGFloat, "Rubezh object + roof depth");
                if (unityMask != null)
                {
                    UnityPackedMasks = true;
                    return true;
                }
                packedMaskSupported = false;
            }
            unityMask = CreateMaskTarget(width, height, RenderTextureFormat.RFloat, "Rubezh object depth");
            unityRoofMask = CreateMaskTarget(width, height, RenderTextureFormat.RFloat, "Rubezh roof depth");
            if (unityMask == null || unityRoofMask == null)
            {
                ReleaseUnityMask();
                throw new System.InvalidOperationException("Cannot create Rubezh occlusion targets.");
            }
            return true;
        }

        // Match the visibility and geometry inputs of BattleRenderer.DrawVehicleArrival.
        // An active, parked vehicle is not a changing occluder.
        struct VehicleMaskState
        {
            public bool Visible;
            public float X,
                Y,
                Scale;
            public int Direction;
            public Side Side;

            public static VehicleMaskState Read(BattleSimulation battle, VehicleArrivalState vehicle)
            {
                if (vehicle == null || !vehicle.Active || !battle.Fog.Revealed(vehicle.Center))
                    return default(VehicleMaskState);
                return new VehicleMaskState
                {
                    Visible = true,
                    X = vehicle.Center.X,
                    Y = vehicle.Center.Y,
                    Direction = vehicle.Direction,
                    Side = vehicle.Side,
                    Scale = Rubezh.Graphics.VehicleScale(vehicle.Side == Side.Green, battle.CampaignLevel),
                };
            }

            public bool Same(VehicleMaskState other) =>
                Visible == other.Visible
                && (
                    !Visible
                    || X == other.X
                        && Y == other.Y
                        && Scale == other.Scale
                        && Direction == other.Direction
                        && Side == other.Side
                );
        }

        VehicleMaskState greenMaskState,
            redMaskState;
        int unityMaskBuildingUpdates = -1,
            unityMaskRenderedGeometry = -1,
            unityMaskRenderedMaterials = -1;
        bool unityMaskLastMergedMode;

        void RenderCachedUnityMasks(BattleSimulation battle, Rect view, bool allocated)
        {
            // Submitted static mask commands for this frame; renderer fallback has no
            // reliable draw counter here and reports -1. Vehicles are renderer-owned.
            UnityObjectMaskDraws = UnityRoofMaskDraws = 0;
            var green = VehicleMaskState.Read(battle, battle.GreenVehicle);
            var red = VehicleMaskState.Read(battle, battle.RedVehicle);
            bool sceneChanged =
                unityMaskRenderedMaterials != unityStaticMaterialRevision
                || unityMaskRenderedGeometry != geometrySerial
                || unityMaskLastMergedMode != UnityMergedMaskCommands
                || unityMaskBuildingUpdates != NativeBuildingMaskUpdates
                || allocated
                || unityMaskMap != battle.Map
                || unityMaskRevision != battle.Map.Revision
                || unityMaskView != view
                || unityMaskSceneryDisabled != DisableScenery;
            bool vehiclesChanged = !green.Same(greenMaskState) || !red.Same(redMaskState);
            if (!sceneChanged && !vehiclesChanged)
                return;
            bool merged = PrepareUnityMaskCommands(view);
            if (merged)
                RenderUnityMergedMask(unityObjectMaskCommands, unityMask, true);
            else
            {
                UnityObjectMaskDraws = -1;
                unityMaskCamera.cullingMask = (1 << ObjectLayer) | (1 << SceneryLayer);
                unityMaskCamera.targetTexture = unityMask;
                unityMaskCamera.RenderWithShader(unityMaskShader, "");
            }
            UnityObjectMaskBuilds++;
            if (UnityPackedMasks)
            {
                UnityRoofMaskBuilds++;
            }
            else if (sceneChanged)
            {
                if (merged)
                    RenderUnityMergedMask(unityRoofMaskCommands, unityRoofMask, false);
                else
                {
                    UnityRoofMaskDraws = -1;
                    unityMaskCamera.cullingMask = 1 << RoofMaskLayer;
                    unityMaskCamera.targetTexture = unityRoofMask;
                    unityMaskCamera.RenderWithShader(unityMaskShader, "");
                }
                UnityRoofMaskBuilds++;
            }
            else
                UnityRoofMaskDraws = 0;
            UnityMaskBuilds++;
            greenMaskState = green;
            redMaskState = red;
            unityMaskBuildingUpdates = NativeBuildingMaskUpdates;
            unityMaskMap = battle.Map;
            unityMaskRevision = battle.Map.Revision;
            unityMaskView = view;
            unityMaskSceneryDisabled = DisableScenery;
            unityMaskRenderedGeometry = geometrySerial;
            unityMaskRenderedMaterials = unityStaticMaterialRevision;
            unityMaskLastMergedMode = UnityMergedMaskCommands;
        }
#endif
    }
}
