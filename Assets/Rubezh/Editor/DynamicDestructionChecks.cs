#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SilentOrders.Core;

namespace Rubezh.UnityPort.Editor
{
    public static class DynamicDestructionChecks
    {
        static int count;

        static void Check(bool v, string name)
        {
            if (!v)
                throw new Exception(name);
            count++;
        }

        static StructureWall Wall(float ax, float ay, float bx, float by, bool window = false)
        {
            var w = new StructureWall
            {
                ax = ax,
                ay = ay,
                bx = bx,
                by = by,
                vax = ax,
                vay = ay,
                vbx = bx,
                vby = by,
                height = 2.8f,
                thickness = .15f,
                material = "wood",
                window = window,
                windowLo = .15f,
                windowHi = .85f,
                sill = .9f,
                lintel = 2.1f,
            };
            for (int i = 0; i < 48; i++)
            {
                var c = new StructureColumn();
                if (window && i >= 8 && i < 40)
                {
                    c.spans.Add(new StructureSpan { lo = 0, hi = .9f });
                    c.spans.Add(new StructureSpan { lo = 2.1f, hi = 2.8f });
                }
                else
                    c.spans.Add(new StructureSpan { lo = 0, hi = 2.8f });
                w.columns.Add(c);
            }
            return w;
        }

        static DynamicBuilding House(BattleMap map, bool window = false)
        {
            var walls = new List<StructureWall>();
            for (int i = 0; i < 8; i++)
            {
                float a = 4 + i * .5f,
                    b = a + .5f;
                walls.Add(Wall(a, 4, b, 4, window && i == 3));
                walls.Add(Wall(a, 8, b, 8));
                walls.Add(Wall(4, a, 4, b));
                walls.Add(Wall(8, a, 8, b));
            }
            var floor = new List<StructureFloor>();
            var roof = new List<StructureRoof>();
            for (int y = 32; y < 64; y++)
            for (int x = 32; x < 64; x++)
                roof.Add(new StructureRoof { x = x, y = y });
            for (int y = 8; y < 16; y++)
            for (int x = 8; x < 16; x++)
                floor.Add(new StructureFloor { x = x / 2f, y = y / 2f });
            var h = new DynamicBuilding(walls, roof, floor, new List<StructureLayer>());
            map.DynamicBuildings = new List<DynamicBuilding> { h };
            map.ProceduralCells = new bool[map.Tiles.Length];
            map.ProceduralHeight = new float[map.Tiles.Length];
            map.ProceduralProtection = new float[map.Tiles.Length];
            map.Roof = new bool[map.Tiles.Length];
            foreach (var w in walls)
            {
                int i = map.Index(new V2((w.ax + w.bx) / 2, (w.ay + w.by) / 2));
                map.ProceduralCells[i] = true;
                map.Tiles[i] = Tile.Wall;
            }
            return h;
        }

        [MenuItem("Рубеж/Процедурные здания/Проверить динамические разрушения")]
        public static void Run()
        {
            count = 0;
            try
            {
                var battle = new BattleSimulation(150);
                var map = battle.Map;
                Array.Clear(map.Tiles, 0, map.Tiles.Length);
                map.Studio = null;
                var house = House(map);
                V2 hit;
                float fraction;
                Check(
                    DynamicStructures.Ray(map, new V2(5.75f, 3), 1.35f, new V2(5.75f, 5), 1.35f, out hit, out fraction),
                    "Стена до попадания"
                );
                int before = house.Walls.Sum(w => w.Solid.Count(x => x));
                DynamicStructures.Blast(battle, new V2(5.75f, 4), 3.3f, 1, false, new V2(0, 1));
                int after = house.Walls.Sum(w => w.Solid.Count(x => x));
                Check(after < before, "РПГ вырезал материал");
                Check(
                    !DynamicStructures.Ray(
                        map,
                        new V2(5.75f, 3),
                        1.35f,
                        new V2(5.75f, 5),
                        1.35f,
                        out hit,
                        out fraction
                    ),
                    "Сквозной прострел после РПГ"
                );
                Check(
                    DynamicStructures.Ray(map, new V2(7.75f, 3), 1.35f, new V2(7.75f, 5), 1.35f, out hit, out fraction),
                    "Дальняя часть стены сохранилась"
                );
                Check(house.Pictures.Length > 0, "Новые торцы пролома");
                Check(
                    map.BrokenCovers.All(e => e.MaxReach <= 3 && e.FanDegrees == 100),
                    "Веер мусора 100 градусов / 3 клетки"
                );
                house = House(map);
                int roof = house.Roof.Count;
                DynamicStructures.Blast(battle, new V2(6, 6), 1.1f, 0, true, new V2(0, 1));
                Check(house.Roof.Count < roof, "Граната разрушает крышу");
                Check(!house.Roof.Contains(DynamicBuilding.Key(48, 48)), "В центре крыши сквозная дыра");
                house = House(map, true);
                var window = house.Walls.First(w => w.Source.window);
                DynamicStructures.Glass(map, new V2(5.75f, 3), 1.3f, new V2(5.75f, 5), 1.3f);
                Check(window.GlassBroken, "Стекло от пули");
                house = House(map, true);
                window = house.Walls.First(w => w.Source.window);
                DynamicStructures.Blast(battle, new V2(6, 6), 1.1f, 0, false, new V2(0, 1));
                Check(window.GlassBroken, "Окна от внутреннего взрыва");
                house = House(map);
                roof = house.Roof.Count;
                DynamicStructures.Blast(battle, new V2(6, 6), 3.3f, 1, false, new V2(0, 1));
                Check(house.Roof.Count < roof, "Внутренний РПГ разрушает крышу");
                house = House(map);
                before = house.Walls.Sum(w => w.Solid.Count(x => x));
                DynamicStructures.Blast(battle, new V2(5.75f, 4), 3.3f, 1, false, new V2(0, 1));
                int rpgLoss = before - house.Walls.Sum(w => w.Solid.Count(x => x));
                house = House(map);
                before = house.Walls.Sum(w => w.Solid.Count(x => x));
                DynamicStructures.Blast(battle, new V2(5.75f, 4), 4.3f, 2, false, new V2(0, 1));
                int rshgLoss = before - house.Walls.Sum(w => w.Solid.Count(x => x));
                Check(rshgLoss > rpgLoss * 1.25f && rshgLoss < rpgLoss * 1.85f, "Площадь РШГ примерно 1.5 РПГ");
                house = House(map);
                Check(
                    DynamicStructures.Ray(map, new V2(5.1f, 4), 1.3f, new V2(5.4f, 4), 1.3f, out hit, out fraction),
                    "Продольный луч внутри стены"
                );
                battle.Soldiers.Clear();
                var shooter = new Soldier
                {
                    Id = 0,
                    Side = Side.Green,
                    Position = new V2(5.75f, 2),
                };
                var victim = new Soldier
                {
                    Id = 1,
                    Side = Side.Red,
                    Position = new V2(5.75f, 4.3f),
                };
                var far = new Soldier
                {
                    Id = 2,
                    Side = Side.Red,
                    Position = new V2(5.75f, 5.1f),
                };
                battle.Soldiers.Add(shooter);
                battle.Soldiers.Add(victim);
                battle.Soldiers.Add(far);
                var shielded = DynamicStructures.Shielded(battle, new V2(5.75f, 4), new V2(0, 1));
                Check(shielded.Contains(victim) && !shielded.Contains(far), "Радиус за стеной ограничен 0.8");
                DynamicStructures.Blast(battle, new V2(5.75f, 4), 3.3f, 1, false, new V2(0, 1));
                float hp = victim.Health;
                var done = new HashSet<int> { victim.Id };
                DynamicStructures.BehindDamage(battle, shooter, new V2(5.75f, 4), shielded, 1, done);
                Check(victim.Health == hp, "Повторный урон исключён");
                DynamicStructures.BehindDamage(battle, shooter, new V2(5.75f, 4), shielded, 1, new HashSet<int>());
                Check(victim.Health < hp, "Урон после пробития стены");
                Check(ConcussionEffects.Current(battle, victim) > 0, "Контузия за пробитой стеной");
                Debug.Log("[Rubezh] PASS dynamic checks: " + count);
                EditorUtility.DisplayDialog("Динамические разрушения", "Пройдено проверок: " + count, "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Ошибка динамики", e.ToString(), "OK");
            }
        }
    }
}
#endif
