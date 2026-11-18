using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Reconstructs the actual campaign seed, without consulting Resources level overrides.
    public static class LevelCampaignFactory
    {
        public static BattleSimulation Battle(int mission)
        {
            if (mission < 1 || mission > 20)
                throw new ArgumentOutOfRangeException("mission");
            var b = new BattleSimulation(8000 + mission * 97);
            if (!CampaignScenario.Apply(b, mission + ",20,0,0,0"))
                throw new InvalidOperationException("Cannot generate campaign mission");
            return b;
        }

        public static RubezhLevel Create(int mission)
        {
            var b = Battle(mission);
            var map = b.Map;
            var l = ScriptableObject.CreateInstance<RubezhLevel>();
            l.name = "Mission_" + mission.ToString("00");
            l.campaignMission = mission;
            l.useInQuickBattle = false;
            l.columns = map.Columns;
            l.rows = map.Rows;
            l.theme = map.Theme;
            l.flag = Cell(map.Objective);
            l.playerSpawn = Cell(map.Entry);
            l.botSpawn = Cell(map.EnemyEntry);
            for (int y = 0; y < map.Rows; y++)
                for (int x = 0; x < map.Columns; x++)
                {
                    int i = y * map.Columns + x;
                    var tile = map.Tiles[i];
                    if (tile == Tile.Road || tile == Tile.Rubble)
                        l.SetGround(x, y, tile == Tile.Road ? LevelGroundKind.Road : LevelGroundKind.Rubble);
                    if (tile == Tile.Wall || tile == Tile.LowCover)
                    {
                        var kind = tile == Tile.Wall ? LevelObjectKind.HighWallOriginal : LevelObjectKind.LowCoverOriginal;
                        if (map.LevelProps != null && map.LevelProps[i] > 0)
                            kind = (LevelObjectKind)(100 + map.LevelProps[i]);
                        else if (map.LowCoverStyle != null && map.LowCoverStyle[i] > 0)
                            kind = (LevelObjectKind)(1 + map.LowCoverStyle[i]);
                        l.SetObject(x, y, kind);
                    }
                }

            Populate(l, b);
            return l;
        }

        public static void Populate(RubezhLevel l, BattleSimulation b)
        {
            l.defenders.Clear();
            int count = b.CampaignTotal, first = count - b.CampaignReserve;
            var kits = CampaignScenario.EnemyKits(b.CampaignLevel);
            for (int start = 0; start < count;)
            {
                bool reserve = start >= first;
                int size = Math.Min(5, (reserve ? count : first) - start);
                Soldier reference = CampaignScenario.Enemy(b, 5 + start, start, kits[start], reserve);
                var point = l.ClampPoint(Cell(reference.Position));
                while (l.defenders.Exists(g => g.position == point) || point == l.playerSpawn)
                {
                    point.x++;
                    if (point.x >= l.columns)
                    {
                        point.x = 0;
                        point.y = (point.y + 1) % l.rows;
                    }
                }

                var group = new DefenderSpawn
                {
                    name = reserve ? "Резерв" : "Основной отряд",
                    position = point,
                    arrivalSeconds = reserve ? 110f : 0f,
                    legacyReserveTrigger = reserve,
                    behavior = !reserve && b.CampaignReserve > 0 ? DefenderBehavior.Attack : DefenderBehavior.Hold
                };
                group.soldiers.Clear();
                for (int i = 0; i < size; i++)
                {
                    var soldier = CampaignScenario.Enemy(b, 5 + start + i, start + i, kits[start + i], reserve);
                    group.soldiers.Add(new DefenderSlot { name = soldier.Name, kit = DefenderLoadout.Read(soldier) });
                }

                l.defenders.Add(group);
                l.SetObject(point.x, point.y, LevelObjectKind.None);
                start += size;
            }

            l.customDefenders = true;
        }

        public static int RecommendedCost(int mission)
        {
            int cost = 0;
            for (int i = 0; i < CampaignEnemyEquipment.Kits[mission - 1].Length; i++)
            {
                var s = new Soldier();
                CampaignEnemyEquipment.Apply(mission, i, s);
                cost += DefenderLoadout.Cost(DefenderLoadout.Read(s));
            }

            return cost;
        }

        static Vector2Int Cell(V2 p)
        {
            return new Vector2Int((int)p.X, (int)p.Y);
        }
    }
}
