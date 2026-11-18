using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    [Serializable]
    public class CampaignTransfer
    {
        public string format = "rubezh-mobile-campaign-v2", game_version = "0.12.55", campaignTitle = "Рубеж", exportedAt = "";
        public List<LevelTransfer> missions = new List<LevelTransfer>();
    }

    [Serializable]
    public class LevelTransfer
    {
        public List<DynamicBuildingData> dynamicBuildings = new List<DynamicBuildingData>();
        public List<ProceduralVisualData> proceduralVisuals = new List<ProceduralVisualData>();
        public int proceduralCombatVersion;
        public List<ProceduralCombatCell> proceduralCombatCells = new List<ProceduralCombatCell>();
        public List<LevelRoofCell> proceduralRoofArea = new List<LevelRoofCell>();
        public List<LevelRoofCell> proceduralRoofCells = new List<LevelRoofCell>();
        public List<string> proceduralWallIds = new List<string>();
        public List<LevelRoofCell> roof = new List<LevelRoofCell>();
        public int formatVersion = 6, campaignMission, columns, rows, theme;
        public string name, operationDescription, expectedThreats;
        public bool enabledInGame, useInQuickBattle, customDefenders, greenVehicleArrival, overrideBriefing;
        public Vector2Int flag, playerSpawn, botSpawn;
        public List<LevelObjectCell> objects;
        public List<LevelGroundCell> ground;
        public List<DefenderSpawn> defenders;
        public RubezhLevel Create()
        {
            var l = ScriptableObject.CreateInstance<RubezhLevel>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(this), l);
            l.name = name;
            l.displayName = name;
            return l;
        }

        public static LevelTransfer Read(RubezhLevel l)
        {
            var d = JsonUtility.FromJson<LevelTransfer>(JsonUtility.ToJson(l));
            d.name = l.DisplayName;
            return d;
        }
    }
}
