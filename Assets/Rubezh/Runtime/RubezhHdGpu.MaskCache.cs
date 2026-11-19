using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        public int UnityObjectMaskBuilds { get; private set; }
        public int UnityRoofMaskBuilds { get; private set; }
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
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
            bool merged = (sceneChanged || vehiclesChanged) && PrepareUnityMaskCommands(view);
            if (sceneChanged || vehiclesChanged)
            {
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
            }
            // Vehicles do not contribute to layer 26: do not redraw the roof mask for them.
            if (sceneChanged)
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
            if (sceneChanged || vehiclesChanged)
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
