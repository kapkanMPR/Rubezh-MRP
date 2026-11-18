using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Bridges serialized Unity levels to the original standalone C# battle simulation.
    // No Editor API, GameObjects or per-frame allocations are involved.
    public static class RubezhLevelRuntime
    {
        public static string LastError { get; private set; }

        public static RubezhLevel Find(bool campaign, int mission)
        {
            if (campaign)
            {
                var regenerated = BuiltinCampaign.Find(mission);
                if(regenerated != null && regenerated.terrainVisual != null)return regenerated;
                var canonical = Resources.Load<RubezhLevel>("Levels/Campaign_" + mission.ToString("00"));
                string error;
                if (canonical != null && canonical.enabledInGame && canonical.campaignMission == mission && LevelValidation.Valid(canonical, out error))
                    return canonical;
                if (canonical != null) Resources.UnloadAsset(canonical);
                var embedded = BuiltinCampaign.Find(mission);
                if (embedded != null)
                    return embedded;
                return null;
            }

            var levels = Resources.LoadAll<RubezhLevel>("Levels");
            Array.Sort(levels, (a, b) => string.CompareOrdinal(a == null ? "" : a.name, b == null ? "" : b.name));
            RubezhLevel chosen = null;
            foreach (var level in levels)
                if (level != null && level.enabledInGame && level.useInQuickBattle) { chosen=level; break; }
            foreach (var level in levels)
                if (level != null && level != chosen) Resources.UnloadAsset(level);
            return chosen;
        }

        public static bool Apply(BattleSimulation battle, bool campaign, int mission)
        {
            LastError = "";
            RubezhLevel level;
            try
            {
                level = Find(campaign, mission);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogError("[Rubezh Levels] " + LastError);
                return false;
            }

            if (level == null)
            {
                LastError = "Не найдена карта миссии " + mission;
                return false;
            }

            try { return ApplyLevel(battle, level); }
            finally { if (!BuiltinCampaign.Owns(level)) Resources.UnloadAsset(level); }
        }

        // Explicit level snapshot, independent of editor/custom campaign overrides.
        public static bool ApplyLevel(BattleSimulation battle, RubezhLevel level)
        {
            LastError = "";
            if (level == null)
            {
                LastError = "Карта отсутствует";
                return false;
            }

            int mission = level.campaignMission;
            if (level.columns < 16 || level.rows < 12 || level.columns > 136 || level.rows > 80)
            {
                Debug.LogError("[Rubezh Levels] Incorrect level dimensions: " + level.name);
                return false;
            }

            string error;
            if (!LevelValidation.Valid(level, out error))
            {
                LastError = "Миссия " + mission + " / " + level.name + " [" + level.columns + "x" + level.rows + "] флаг=" + level.flag + ", спавн=" + level.playerSpawn + ": " + error;
                Debug.LogError("[Rubezh Levels] " + LastError);
                return false;
            }

            BattleMap map = battle.Map;
            map.Columns = level.columns;
            map.Rows = level.rows;
            map.Theme = Mathf.Clamp(level.theme, 0, 5);
            int count = checked(map.Columns * map.Rows);
            map.Roof = new bool[count];
            if (level.roof != null)
                foreach (var p in level.roof)
                    map.Roof[p.y * map.Columns + p.x] = true;
            map.Studio = new StudioMap(count);
            map.Tiles = new Tile[count];
            map.CoverHealth = new float[count];
            map.LevelProps = new byte[count];
            map.LowCoverStyle = new byte[count];
            map.VehicleFootprint = new byte[count];
            map.LevelPropRotation = new float[count];
            map.LevelPropScaleX = new float[count];
            map.LevelPropScaleY = new float[count];
            for (int i = 0; i < count; i++)
                map.CoverHealth[i] = 100f;
            if (level.ground != null)
                foreach (var tile in level.ground)
                {
                    if (tile == null || !map.Inside(tile.x, tile.y))
                        continue;
                    int i = tile.y * map.Columns + tile.x;
                    map.Studio.Ground[i] = StudioCatalog.Find(tile.assetId, tile.mirrored);
                    map.Tiles[i] = tile.kind == LevelGroundKind.Road ? Tile.Road : tile.kind == LevelGroundKind.Rubble ? Tile.Rubble : Tile.Ground;
                }

            if (level.objects != null)
                foreach (var obj in level.objects)
                {
                    if (obj == null || !map.Inside(obj.x, obj.y))
                        continue;
                    var asset = StudioCatalog.Find(obj.assetId, obj.mirrored);
                    if (asset != null)
                    {
                        var placed = new StudioPlaced
                        {
                            Uid = obj.uid,
                            ParentUid = obj.parentUid,
                            OffsetX = obj.offsetX,
                            OffsetY = obj.offsetY,
                            HitsLeft = asset.DecorHits,
                            Asset = asset,
                            X = obj.x,
                            Y = obj.y,
                            Angle = obj.rotationDegrees,
                            ScaleX = LevelObjectCell.SafeScale(obj.scaleX),
                            ScaleY = LevelObjectCell.SafeScale(obj.scaleY),
                            Root = obj.y * map.Columns + obj.x
                        };
                        if (asset.Decor)
                        {
                            map.Studio.Decorations.Add(placed);
                            continue;
                        }

                        map.Studio.Objects.Add(placed);
                        for (int y = obj.y; y < obj.y + asset.Height; y++)
                            for (int x = obj.x; x < obj.x + asset.Width; x++)
                            {
                                int cell = y * map.Columns + x;
                                map.Studio.Cells[cell] = placed;
                                if (asset.Blocking)
                                    map.Tiles[cell] = StudioRules.Cell(asset, x - obj.x, y - obj.y);
                            }

                        continue;
                    }

                    if (level.IsMarker(obj.x, obj.y))
                        continue;
                    int i = obj.y * map.Columns + obj.x;
                    if (!RubezhLevel.IsLow(obj.kind) && !RubezhLevel.IsHigh(obj.kind))
                        continue;
                    map.Tiles[i] = RubezhLevel.IsHigh(obj.kind) ? Tile.Wall : Tile.LowCover;
                    int code = (int)obj.kind;
                    map.LevelPropRotation[i] = Mathf.Clamp(obj.rotationDegrees, -180f, 180f);
                    map.LevelPropScaleX[i] = LevelObjectCell.SafeScale(obj.scaleX);
                    map.LevelPropScaleY[i] = LevelObjectCell.SafeScale(obj.scaleY);
                    if (RubezhLevel.IsSprite(obj.kind))
                    {
                        map.LevelProps[i] = (byte)(code - 100);
                    }
                    else if (code >= 2 && code <= 4)
                        map.LowCoverStyle[i] = (byte)(code - 1); // 1..3 force old graphics variants.
                }

            ProceduralCombat.Apply(map, level);
            DecorRules.Bind(map.Studio, map.Columns);
            map.Studio.Connect();
            SceneEquipment.Bind(battle);
            map.Entry = Center(level.playerSpawn, map);
            map.EnemyEntry = Center(level.botSpawn, map);
            map.Objective = Center(level.flag, map);
            map.CustomLevel = true;
            map.ExteriorMission = level.campaignMission;
            map.Revision++;
            if (level.customDefenders)
            {
                battle.CustomDefenders = true;
                battle.DefenderGroups.Clear();
                foreach (var old in battle.Soldiers)
                    if (old.Side == Side.Red)
                    {
                        old.Health = 0;
                        old.Deployed = false;
                    }

                battle.Counterattack.Begin(0);
                foreach (var source in level.defenders)
                {
                    var group = new DefenderGroupState
                    {
                        Name = source.name,
                        Spawn = Center(source.position, map),
                        SpawnTime = source.arrivalSeconds,
                        Radius = source.holdRadius,
                        Attack = source.behavior != DefenderBehavior.Hold,
                        Pullback = source.behavior == DefenderBehavior.Pullback,
                        LegacyReserveTrigger = source.legacyReserveTrigger,
                        VehicleArrival = source.vehicleArrival
                    };
                    foreach (var slot in source.soldiers)
                        group.Members.Add(DefenderLoadout.Build(slot, battle.Rules));
                    battle.DefenderGroups.Add(group);
                }

                battle.CampaignTotal = 0;
                foreach (var g in battle.DefenderGroups)
                    battle.CampaignTotal += g.Members.Count;
                battle.CampaignReserve = battle.CampaignTotal;
            }

            SpawnBothTeams(battle);
            if (level.greenVehicleArrival)
            {
                foreach (var s in battle.Soldiers)
                    if (s.Side == Side.Green && s.Alive)
                        s.Deployed = false;
                VehicleArrivals.Start(battle, Side.Green);
            }

            if (level.customDefenders)
                DefenderGroupsRuntime.Update(battle);
            return true;
        }

        static V2 Center(Vector2Int p, BattleMap map)
        {
            int x = Mathf.Clamp(p.x, 0, map.Columns - 1), y = Mathf.Clamp(p.y, 0, map.Rows - 1);
            return map.NearestOpen(new V2(x + .5f, y + .5f));
        }

        static void SpawnBothTeams(BattleSimulation battle)
        {
            var used = new bool[battle.Map.Tiles.Length];
            int green = 0, red = 0;
            foreach (var soldier in battle.Soldiers)
            {
                if (!soldier.Deployed || !soldier.Alive)
                    continue;
                bool isGreen = soldier.Side == Side.Green;
                int rank = isGreen ? green++ : red++;
                V2 origin = isGreen ? battle.Map.Entry : battle.Map.EnemyEntry;
                var preference = new V2(origin.X + (isGreen ? -1f : 1f) * (rank / 5) * 1.4f, origin.Y + ((rank % 5) - 2) * 1.5f);
                var point = DistinctOpen(battle.Map, preference, used);
                soldier.Position = point;
                soldier.Destination = point;
                soldier.OrderAnchor = point;
                soldier.Path.Clear();
                soldier.PathCursor = 0;
                soldier.NextDecision = 0;
            }

            battle.Green.Initialized = false;
            battle.Red.Initialized = false;
        }

        static V2 DistinctOpen(BattleMap map, V2 preferred, bool[] used)
        {
            int best = -1;
            float score = float.MaxValue;
            for (int i = 0; i < map.Tiles.Length; i++)
            {
                if (used[i] || !map.Walkable(i % map.Columns, i / map.Columns))
                    continue;
                V2 p = map.Center(i);
                float dx = p.X - preferred.X, dy = p.Y - preferred.Y;
                float dist = dx * dx + dy * dy;
                if (dist < score)
                {
                    score = dist;
                    best = i;
                }
            }

            if (best < 0)
                return map.NearestOpen(preferred);
            used[best] = true;
            return map.Center(best);
        }
    }
}
