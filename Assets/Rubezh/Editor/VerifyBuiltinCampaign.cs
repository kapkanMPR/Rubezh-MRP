#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    public static class VerifyBuiltinCampaign
    {
        [MenuItem("Рубеж/Проверить встроенную кампанию")]
        public static void Run()
        {
            int checkedMaps = 0;
            for (int i = 1; i <= 20; i++)
            {
                var baseline = BuiltinCampaign.Find(i);
                var selected = RubezhLevelRuntime.Find(true, i);
                string error;
                if (baseline == null || selected == null || !LevelValidation.Valid(selected, out error))
                {
                    Debug.LogError("Кампания: не загружена миссия " + i);
                    return;
                }
                Debug.Log(
                    "Кампания "
                        + i
                        + ": "
                        + selected.name
                        + "; "
                        + selected.columns
                        + "x"
                        + selected.rows
                        + "; флаг "
                        + selected.flag
                        + "; игрок "
                        + selected.playerSpawn
                        + "; объектов "
                        + selected.objects.Count
                        + "; групп "
                        + selected.defenders.Count
                );
                checkedMaps++;
            }
            EditorUtility.DisplayDialog(
                "Рубеж",
                "Проверены " + checkedMaps + " карт. Координаты и составы проходят проверку. Подробности в Console.",
                "OK"
            );
        }
    }
}
#endif
