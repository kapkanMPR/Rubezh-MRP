using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Explicit portable DTOs: never deserialize x/y into Unity's private Vector2Int fields.
    public static class BuiltinCampaign
    {
        [Serializable]
        public class Point
        {
            public int x,
                y;
            public Vector2Int Value
            {
                get { return new Vector2Int(x, y); }
            }
        }

        [Serializable]
        public class Group
        {
            public string name;
            public Point position;
            public float arrivalSeconds,
                holdRadius;
            public bool legacyReserveTrigger,
                vehicleArrival;
            public DefenderBehavior behavior;
            public List<DefenderSlot> soldiers;

            public DefenderSpawn Create()
            {
                return new DefenderSpawn
                {
                    name = name,
                    position = position.Value,
                    arrivalSeconds = arrivalSeconds,
                    holdRadius = holdRadius,
                    legacyReserveTrigger = legacyReserveTrigger,
                    vehicleArrival = vehicleArrival,
                    behavior = behavior,
                    soldiers = soldiers,
                };
            }
        }

        [Serializable]
        public class Map
        {
            public List<DynamicBuildingData> dynamicBuildings = new List<DynamicBuildingData>();
            public ProceduralVisualData terrainVisual;
            public List<ProceduralVisualData> proceduralVisuals = new List<ProceduralVisualData>();
            public int proceduralCombatVersion;
            public List<ProceduralCombatCell> proceduralCombatCells = new List<ProceduralCombatCell>();
            public List<LevelRoofCell> proceduralRoofArea = new List<LevelRoofCell>();
            public List<LevelRoofCell> proceduralRoofCells = new List<LevelRoofCell>();
            public List<string> proceduralWallIds = new List<string>();
            public List<LevelRoofCell> roof;
            public int formatVersion,
                campaignMission,
                columns,
                rows,
                theme;
            public string name,
                operationDescription,
                expectedThreats;
            public bool enabledInGame,
                useInQuickBattle,
                customDefenders,
                greenVehicleArrival,
                overrideBriefing;
            public Point flag,
                playerSpawn,
                botSpawn;
            public List<LevelObjectCell> objects;
            public List<LevelGroundCell> ground;
            public List<Group> defenders;

            public RubezhLevel Create()
            {
                var l = ScriptableObject.CreateInstance<RubezhLevel>();
                l.name = name;
                l.formatVersion = formatVersion;
                l.campaignMission = campaignMission;
                l.columns = columns;
                l.rows = rows;
                l.theme = theme;
                l.proceduralVisuals = proceduralVisuals;
                l.dynamicBuildings = dynamicBuildings;
                l.proceduralCombatVersion = proceduralCombatVersion;
                l.proceduralCombatCells = proceduralCombatCells;
                l.proceduralRoofArea = proceduralRoofArea;
                l.proceduralRoofCells = proceduralRoofCells;
                l.proceduralWallIds = proceduralWallIds;
                l.terrainVisual = terrainVisual;
                l.roof = roof ?? new List<LevelRoofCell>();
                l.enabledInGame = enabledInGame;
                l.useInQuickBattle = useInQuickBattle;
                l.customDefenders = customDefenders;
                l.greenVehicleArrival = greenVehicleArrival;
                l.overrideBriefing = overrideBriefing;
                l.operationDescription = operationDescription;
                l.expectedThreats = expectedThreats;
                l.flag = flag.Value;
                l.playerSpawn = playerSpawn.Value;
                l.botSpawn = botSpawn.Value;
                l.objects = objects;
                l.ground = ground;
                l.defenders = new List<DefenderSpawn>();
                if (defenders != null)
                    foreach (var g in defenders)
                        l.defenders.Add(g.Create());
                return l;
            }
        }

        [Serializable]
        public class Bundle
        {
            public List<Map> missions;
        }

        static readonly Dictionary<int, RubezhLevel> cache = new Dictionary<int, RubezhLevel>();

        public static bool Owns(RubezhLevel level) => cache.ContainsValue(level);

        public static void ReleaseCached()
        {
            foreach (var level in cache.Values)
                if (level != null)
                    UnityEngine.Object.Destroy(level);
            cache.Clear();
        }

        public static RubezhLevel Find(int mission)
        {
            RubezhLevel result;
            if (cache.TryGetValue(mission, out result) && result != null)
                return result;
            var regenerated = Resources.Load<TextAsset>("Regenerated/Campaign_" + mission.ToString("00"));
            if (regenerated != null)
            {
                try
                {
                    result = JsonUtility.FromJson<Map>(PackedLevel.Read(regenerated)).Create();
                    string validationError;
                    if (!LevelValidation.Valid(result, out validationError))
                        throw new InvalidOperationException(validationError);
                    cache[mission] = result;
                    return result;
                }
                finally
                {
                    Resources.UnloadAsset(regenerated);
                }
            }
            var text = Resources.Load<TextAsset>("CampaignBaseline");
            if (text == null)
                return null;
            Bundle data;
            try
            {
                data = JsonUtility.FromJson<Bundle>(PackedLevel.Read(text));
            }
            finally
            {
                Resources.UnloadAsset(text);
            }
            if (data == null || data.missions == null)
                return null;
            foreach (var map in data.missions)
                if (map.campaignMission == mission)
                {
                    result = map.Create();
                    string error;
                    if (!LevelValidation.Valid(result, out error))
                    {
                        UnityEngine.Object.DestroyImmediate(result);
                        throw new InvalidOperationException("Встроенная миссия " + mission + ": " + error);
                    }

                    cache[mission] = result;
                    return result;
                }

            return null;
        }
    }
}
