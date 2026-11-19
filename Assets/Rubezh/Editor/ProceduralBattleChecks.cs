#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using SilentOrders.Core;

namespace Rubezh.UnityPort.Editor
{
    public static class ProceduralBattleChecks
    {
        static int checks;

        static void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
            checks++;
        }

        [MenuItem("Рубеж/Процедурные здания/Проверить боевую геометрию")]
        public static void Geometry()
        {
            checks = 0;
            var level = ScriptableObject.CreateInstance<RubezhLevel>();
            try
            {
                var battle = new BattleSimulation(149);
                var map = battle.Map;
                Array.Clear(map.Tiles, 0, map.Tiles.Length);
                map.Studio = null;
                level.columns = map.Columns;
                level.rows = map.Rows;
                level.proceduralCombatVersion = 1;
                var cell = new ProceduralCombatCell
                {
                    x = 5,
                    y = 5,
                    kind = "gap",
                    shoot = true,
                    walk = false,
                    height = 0,
                    protection = 0,
                };
                level.proceduralCombatCells = new List<ProceduralCombatCell> { cell };
                var from = new V2(4.5f, 5.5f);
                var to = new V2(7.5f, 5.5f);
                var blocked = typeof(ArcedOrdnance).GetMethod("Blocked", BindingFlags.Static | BindingFlags.NonPublic);
                Check(blocked != null, "Не найдена проверка гранат");
                Action apply = () =>
                {
                    ProceduralCombat.Apply(map, level);
                    map.Revision++;
                };
                apply();
                Check(!map.Walkable(5, 5), "Низкий пролом не должен давать проход");
                Check(map.Protection(from, to, false) == 0, "Пролом снизу не должен давать защиту");
                Check(!map.NearCover(from), "ИИ не должен считать пустой пролом укрытием");
                Check(map.LineOfSight(from, to), "Прострел пролома");
                Check(
                    Ballistics.Geometry(map, from, 1.55f, new V2(1, 0), 0, 3).Kind == BulletStop.None,
                    "Пуля в проломе"
                );
                Check(V2.Distance(map.RocketImpact(new V2(2.5f, 5.5f), to), to) < .001f, "РПГ в проломе");
                Check(
                    !(bool)blocked.Invoke(null, new object[] { battle, new V2(5.5f, 5.5f), 1f }),
                    "Граната в проломе"
                );
                cell.kind = "low";
                cell.height = .9f;
                cell.protection = .5f;
                apply();
                Check(Math.Abs(map.Protection(from, to, false) - .5f) < .001f, "Низкое укрытие 50%");
                Check(
                    Ballistics.Geometry(map, from, .5f, new V2(1, 0), 0, 3).Kind == BulletStop.LowCover,
                    "Пуля ниже стенки"
                );
                Check(
                    Ballistics.Geometry(map, from, 1.55f, new V2(1, 0), 0, 3).Kind == BulletStop.None,
                    "Пуля выше стенки"
                );
                Check(
                    !(bool)blocked.Invoke(null, new object[] { battle, new V2(5.5f, 5.5f), 1.1f }),
                    "Граната выше стенки"
                );
                cell.kind = "open";
                cell.walk = true;
                cell.height = 0;
                cell.protection = 0;
                apply();
                Check(map.Walkable(5, 5), "Полный проход");
                cell.kind = "high";
                cell.walk = false;
                cell.shoot = false;
                cell.height = 2.8f;
                cell.protection = .75f;
                apply();
                Check(!map.LineOfSight(from, to), "Целая стена закрывает обзор");
                Check(
                    (bool)blocked.Invoke(null, new object[] { battle, new V2(5.5f, 5.5f), 2.6f }),
                    "Высота целой стены для гранаты"
                );
                map.Roof = new bool[map.Tiles.Length];
                map.Roof[5 * map.Columns + 5] = true;
                V2 hit;
                Check(
                    RoofRules.Crosses(map, new V2(5.5f, 5.5f), 3, new V2(5.5f, 5.5f), 1, out hit),
                    "Крыша задерживает падение"
                );
                map.Roof[5 * map.Columns + 5] = false;
                Check(
                    !RoofRules.Crosses(map, new V2(5.5f, 5.5f), 3, new V2(5.5f, 5.5f), 1, out hit),
                    "Дыра крыши пропускает"
                );
                string error;
                cell.height = float.NaN;
                Check(!ProceduralCombat.Valid(level, out error), "Отбраковка NaN");
                Debug.Log("[Rubezh] PASS procedural geometry: " + checks);
                EditorUtility.DisplayDialog("Боевая геометрия", "Проверок пройдено: " + checks, "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Ошибка проверки", e.ToString(), "OK");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(level);
            }
        }

        [MenuItem("Рубеж/Процедурные здания/Прогнать JSON — 20 боевых запусков")]
        public static void Simulate()
        {
            string path = EditorUtility.OpenFilePanel("Боевой JSON из редактора 1.9.0", "", "json");
            if (string.IsNullOrEmpty(path))
                return;
            var level = ScriptableObject.CreateInstance<RubezhLevel>();
            int steps = 0,
                runs = 0;
            try
            {
                JsonUtility.FromJsonOverwrite(LevelJson.ToUnity(File.ReadAllText(path)), level);
                string error;
                if (!LevelValidation.Valid(level, out error))
                    throw new Exception(error);
                if (level.proceduralCombatVersion != 1)
                    throw new Exception("Нужен боевой экспорт процедурной карты");
                for (int seed = 0; seed < 20; seed++)
                {
                    if (
                        EditorUtility.DisplayCancelableProgressBar(
                            "Рубеж",
                            "Боевой запуск " + (seed + 1) + " / 20",
                            seed / 20f
                        )
                    )
                        break;
                    var battle = new BattleSimulation(15000 + seed);
                    if (!RubezhLevelRuntime.ApplyLevel(battle, level))
                        throw new Exception(RubezhLevelRuntime.LastError);
                    for (int tick = 0; tick < 600; tick++)
                    {
                        battle.Step();
                        steps++;
                        foreach (var soldier in battle.Soldiers)
                            if (
                                float.IsNaN(soldier.Position.X)
                                || float.IsNaN(soldier.Position.Y)
                                || float.IsNaN(soldier.Health)
                            )
                                throw new Exception("NaN в состоянии бойца, seed=" + seed);
                        if (battle.Outcome != 0)
                            break;
                    }
                    runs++;
                }
                string report =
                    "Запусков: "
                    + runs
                    + " / 20; шагов движка: "
                    + steps
                    + ". Проверка исключений и NaN, не баланс и не FPS.";
                Directory.CreateDirectory("Validation");
                File.WriteAllText("Validation/Procedural150.txt", report);
                Debug.Log("[Rubezh] " + report);
                EditorUtility.DisplayDialog("Прогон завершён", report, "OK");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("Ошибка прогона", e.ToString(), "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                UnityEngine.Object.DestroyImmediate(level);
            }
        }
    }
}
#endif
