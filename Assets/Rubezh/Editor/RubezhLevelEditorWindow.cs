#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rubezh.UnityPort.Editor
{
    // Dedicated 2D isometric map authoring UI. The game itself remains a pure battle simulation.
    // Save assets under Resources/Levels so they are included in standalone/mobile builds.
    public sealed partial class RubezhLevelEditorWindow : EditorWindow
    {
        const string LevelFolder = "Assets/Rubezh/Resources/Levels";
        const string LastAssetKey = "Rubezh.LevelEditor.LastLevel";
        static readonly LevelObjectKind[] LowPalette =
        {
            LevelObjectKind.TreeTall,
            LevelObjectKind.TreeRound,
            LevelObjectKind.TreeSparse,
            LevelObjectKind.TreeWide,
            LevelObjectKind.BushSmall,
            LevelObjectKind.BushDense,
            LevelObjectKind.BushSparse,
            LevelObjectKind.BushTall,
            LevelObjectKind.RockBoulder,
            LevelObjectKind.RockSlab,
            LevelObjectKind.RockPile,
            LevelObjectKind.RockJagged,
            LevelObjectKind.BarrelOlive,
            LevelObjectKind.BarrelRust,
        };
        static readonly LevelObjectKind[] HighPalette = new LevelObjectKind[0];
        static readonly string[] PropNames =
        {
            "TreeTall",
            "TreeRound",
            "TreeSparse",
            "TreeWide",
            "BushSmall",
            "BushDense",
            "BushSparse",
            "BushTall",
            "RockBoulder",
            "RockSlab",
            "RockPile",
            "RockJagged",
            "CrateWood",
            "CrateMilitary",
            "BarrelOlive",
            "BarrelRust",
            "SandbagShortA",
            "SandbagShortB",
            "SandbagLongA",
            "SandbagLongB",
            "SandbagCornerA",
            "SandbagCornerB",
            "SandbagBrokenA",
            "SandbagBrokenB",
        };
        static readonly string[] PropLabels =
        {
            "Дерево · узкое",
            "Дерево · круглое",
            "Дерево · редкое",
            "Дерево · широкое",
            "Куст · маленький",
            "Куст · густой",
            "Куст · ветвистый",
            "Куст · высокий",
            "Камень · валун",
            "Камни · плиты",
            "Камни · россыпь",
            "Камни · скала",
            "Ящик · деревянный",
            "Ящик · военный",
            "Бочка · зелёная",
            "Бочка · ржавая",
            "Мешки · короткие",
            "Мешки · короткие (зерк.)",
            "Мешки · длинные",
            "Мешки · длинные (зерк.)",
            "Мешки · угол",
            "Мешки · угол (зерк.)",
            "Мешки · разрушенные",
            "Мешки · разрушенные (зерк.)",
        };

        enum BrushMode
        {
            Object,
            Ground,
            Marker,
            Erase,
            Edit,
            Roof,
        }

        RubezhLevel level;
        int tab = 4;
        int marker;
        LevelObjectKind selected = LevelObjectKind.BarrelOlive;
        LevelGroundKind selectedGround = LevelGroundKind.Road;
        BrushMode brush = BrushMode.Object;
        Vector2 sidebarScroll,
            pan;
        bool viewSprites = true,
            showObjectNames;
        float previewAlpha = 1f,
            previewScale = 1.6f;
        float zoom = 1.5f;

        // Brush defaults + independent transforms for an already placed object.
        float brushAngle,
            brushScaleX = 1f,
            brushScaleY = 1f;
        Vector2Int editCell = new Vector2Int(-1, -1);
        bool painting;
        int previousX = int.MinValue,
            previousY = int.MinValue;

        // The editor displays original PNG files, not imported Sprite subassets.
        // Default .png Unity import settings can differ between project machines;
        // direct decoding avoids AssetDatabase.LoadAssetAtPath<Texture2D> returning null.
        readonly Dictionary<int, Texture2D> thumbnails = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, string> missingThumbnails = new Dictionary<int, string>();

        [MenuItem("Рубеж/Редактор уровней", priority = 55)]
        public static void Open()
        {
            var w = GetWindow<RubezhLevelEditorWindow>("Рубеж · Уровни");
            w.minSize = new Vector2(850, 560);
            w.Show();
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            string path = EditorPrefs.GetString(LastAssetKey, "");
            if (!string.IsNullOrEmpty(path))
                level = AssetDatabase.LoadAssetAtPath<RubezhLevel>(path);
            if (level == null)
            {
                var assets = AssetDatabase.FindAssets("t:RubezhLevel", new[] { LevelFolder });
                if (assets.Length > 0)
                    level = AssetDatabase.LoadAssetAtPath<RubezhLevel>(AssetDatabase.GUIDToAssetPath(assets[0]));
            }
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            ClearThumbnails();
            ClearStudioTextures();
            if (!EditorApplication.isPlaying)
                Rubezh.Graphics.ReleaseSceneSprites();
        }

        void OnProjectChange()
        {
            ClearThumbnails();
            ClearStudioTextures();
            if (!EditorApplication.isPlaying)
                Rubezh.Graphics.ReleaseSceneSprites();
            Repaint();
        }

        void ClearThumbnails()
        {
            foreach (var entry in thumbnails)
                if (entry.Value != null)
                    DestroyImmediate(entry.Value);
            thumbnails.Clear();
            missingThumbnails.Clear();
        }

        void OnUndoRedo()
        {
            Repaint();
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is RubezhLevel chosen && chosen != level)
            {
                SelectLevel(chosen);
            }
        }

        void SaveLast()
        {
            if (level != null && AssetDatabase.Contains(level))
                EditorPrefs.SetString(LastAssetKey, AssetDatabase.GetAssetPath(level));
        }

        void SelectLevel(RubezhLevel data)
        {
            if (data == null)
                return;
            ClearThumbnails();
            ClearStudioTextures();
            level = data;
            editCell = new Vector2Int(-1, -1);
            painting = false;
            pan = Vector2.zero;
            zoom = 1f;
            SaveLast();
            Repaint();
        }

        void ShowSavedLevels()
        {
            EnsureFolders();
            var menu = new GenericMenu();
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:RubezhLevel", new[] { LevelFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            if (paths.Count == 0)
                menu.AddDisabledItem(new GUIContent("Нет сохранённых карт"));
            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<RubezhLevel>(path);
                if (asset == null)
                    continue;
                var captured = asset;
                string label = path.Substring(LevelFolder.Length + 1);
                menu.AddItem(new GUIContent(label), captured == level, () => SelectLevel(captured));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Выбрать .asset на диске..."), false, LoadFromDisk);
            menu.ShowAsContext();
        }

        void LoadFromDisk()
        {
            string full = EditorUtility.OpenFilePanel(
                "Загрузить сохранённую карту Рубежа",
                Path.Combine(Application.dataPath, "Rubezh/Resources/Levels"),
                "asset"
            );
            if (string.IsNullOrEmpty(full))
                return;
            string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            string selectedPath = Path.GetFullPath(full).Replace('\\', '/');
            if (!selectedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Рубеж",
                    "Карта должна находиться в папке Assets текущего Unity-проекта.",
                    "OK"
                );
                return;
            }
            string assetPath = "Assets" + selectedPath.Substring(root.Length);
            var loaded = AssetDatabase.LoadAssetAtPath<RubezhLevel>(assetPath);
            if (loaded == null)
            {
                EditorUtility.DisplayDialog("Рубеж", "Этот .asset не является картой RubezhLevel.", "OK");
                return;
            }
            SelectLevel(loaded);
            if (!assetPath.StartsWith(LevelFolder + "/", StringComparison.Ordinal))
                EditorUtility.DisplayDialog(
                    "Рубеж",
                    "Карта открыта, но для использования в игре сохрани её в " + LevelFolder + ".",
                    "OK"
                );
        }

        static string Label(LevelObjectKind id)
        {
            switch (id)
            {
                case LevelObjectKind.LowCoverOriginal:
                    return "Старое укрытие · авто";
                case LevelObjectKind.LowCoverSandbags:
                    return "Старое укрытие · низкое";
                case LevelObjectKind.LowCoverBarrel:
                    return "Старая бочка";
                case LevelObjectKind.LowCoverConcrete:
                    return "Старая баррикада";
                case LevelObjectKind.HighWallOriginal:
                    return "Высокая стена (из игры)";
                default:
                    int n = (int)id - 101;
                    return n >= 0 && n < PropLabels.Length ? PropLabels[n] : id.ToString();
            }
        }

        static string ArtFileName(LevelObjectKind id)
        {
            int n = (int)id - 101;
            if (n >= 0 && n < PropNames.Length)
                return PropNames[n];
            switch (id)
            {
                case LevelObjectKind.LowCoverOriginal:
                    return "LegacyLowCoverOriginal";
                case LevelObjectKind.LowCoverSandbags:
                    return "LegacyLowCoverSandbags";
                case LevelObjectKind.LowCoverBarrel:
                    return "LegacyLowCoverBarrel";
                case LevelObjectKind.LowCoverConcrete:
                    return "LegacyLowCoverConcrete";
                case LevelObjectKind.HighWallOriginal:
                    return "LegacyHighWallOriginal";
                default:
                    return null;
            }
        }

        Texture2D Thumbnail(LevelObjectKind id)
        {
            return LoadPreview((int)id, ArtFileName(id));
        }

        Texture2D VehicleThumbnail(bool green)
        {
            return LoadPreview(green ? -1 : -2, green ? "TigrFinal" : "HumveeFinal");
        }

        Texture2D LoadPreview(int key, string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;
            Texture2D cached;
            if (thumbnails.TryGetValue(key, out cached))
                return cached;
            // Editor image preview must not depend on the TextureImporter type, compression,
            // sprite slicing, or Unity's delayed AssetDatabase import pass.
            string absolutePath = Path.Combine(Application.dataPath, "Rubezh/Art/LevelProps/" + fileName + ".png");
            Texture2D tex = null;
            try
            {
                if (!File.Exists(absolutePath))
                    throw new FileNotFoundException("Отсутствует PNG", absolutePath);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.name = "Рубеж редактор: " + fileName;
                tex.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(absolutePath), true))
                    throw new InvalidDataException("PNG не удалось декодировать");
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                thumbnails[key] = tex;
                return tex;
            }
            catch (Exception exception)
            {
                if (tex != null)
                    DestroyImmediate(tex);
                thumbnails[key] = null; // Cache failed reads; do not hit disk every OnGUI frame.
                missingThumbnails[key] = fileName + ".png: " + exception.Message;
                Debug.LogWarning("[Рубеж/Уровни] Превью не загружено: " + exception.Message);
                return null;
            }
        }

        void DrawPreviewDiagnostics()
        {
            // Load/check the exact same PNG images used by the palette and the map.
            int loaded = 0;
            foreach (var kind in LowPalette)
                if (Thumbnail(kind) != null)
                    loaded++;
            foreach (var kind in HighPalette)
                if (Thumbnail(kind) != null)
                    loaded++;
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Спрайты: " + loaded + " / " + (LowPalette.Length + HighPalette.Length),
                EditorStyles.miniLabel
            );
            if (GUILayout.Button("Обновить PNG", GUILayout.Width(112)))
            {
                ClearThumbnails();
                Repaint();
            }
            GUILayout.EndHorizontal();
            if (missingThumbnails.Count > 0)
            {
                foreach (var entry in missingThumbnails)
                {
                    EditorGUILayout.HelpBox(
                        "Не удалось прочитать " + entry.Value + ". Проверь Assets/Rubezh/Art/LevelProps/",
                        MessageType.Error
                    );
                    break;
                }
            }
        }

        void OnGUI()
        {
            DrawToolbar();
            DrawCampaignToolbar();
            if (level == null)
            {
                EditorGUILayout.HelpBox(
                    "Создай карту или выбери готовый файл RubezhLevel в Project. Существующие процедурные карты не изменятся, пока ты не назначишь свой уровень на миссию.",
                    MessageType.Info
                );
                if (GUILayout.Button("Создать новую карту", GUILayout.Height(42)))
                    CreateNew();
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(310));
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll, GUILayout.ExpandHeight(true));
            DrawSettings();
            DrawPalette();
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            var boundary = GUILayoutUtility.GetRect(
                250,
                300,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );
            DrawMap(boundary);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(
                "ЛКМ: кисть · ПКМ: ластик · колёсико: масштаб · средняя кнопка: перемещение · Ctrl+Z",
                EditorStyles.miniLabel
            );
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                level.columns
                    + " × "
                    + level.rows
                    + " · объектов: "
                    + (level.objects == null ? 0 : level.objects.Count),
                EditorStyles.miniLabel
            );
            GUILayout.EndHorizontal();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Новая", EditorStyles.toolbarButton, GUILayout.Width(65)))
                CreateNew();
            if (GUILayout.Button("Загрузить карту ▾", EditorStyles.toolbarButton, GUILayout.Width(137)))
                ShowSavedLevels();
            if (GUILayout.Button("Сохранить", EditorStyles.toolbarButton, GUILayout.Width(85)))
                Save();
            if (GUILayout.Button("Копия...", EditorStyles.toolbarButton, GUILayout.Width(78)))
                Duplicate();
            if (GUILayout.Button("Проверить", EditorStyles.toolbarButton, GUILayout.Width(88)))
                Validate();
            GUILayout.Space(10);
            var chosen = (RubezhLevel)
                EditorGUILayout.ObjectField(level, typeof(RubezhLevel), false, GUILayout.MinWidth(170));
            if (chosen != level && chosen != null)
                SelectLevel(chosen);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("В проекте", EditorStyles.toolbarButton, GUILayout.Width(85)) && level != null)
            {
                Selection.activeObject = level;
                EditorGUIUtility.PingObject(level);
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawSettings()
        {
            GUILayout.Label("РАЗМЕР И НАЗНАЧЕНИЕ", EditorStyles.boldLabel);
            bool enabled = EditorGUILayout.ToggleLeft("Карта включена в игре", level.enabledInGame);
            if (enabled != level.enabledInGame)
            {
                Record("Включить карту");
                level.enabledInGame = enabled;
                Changed();
            }
            int width = EditorGUILayout.IntSlider("Ширина", level.columns, 16, 136);
            int height = EditorGUILayout.IntSlider("Высота", level.rows, 12, 80);
            if (width != level.columns || height != level.rows)
            {
                Record("Изменить размер карты");
                level.Resize(width, height);
                Changed();
            }
            int theme = EditorGUILayout.Popup(
                "Тема карты",
                level.theme,
                new[] { "Лес", "Промзона", "Пустошь", "Равнина", "Сухая", "Холодная" }
            );
            if (theme != level.theme)
            {
                Record("Изменить тему");
                level.theme = theme;
                Changed();
            }
            int mission = EditorGUILayout.IntPopup(
                "Миссия",
                level.campaignMission,
                new[]
                {
                    "Не назначена",
                    "1",
                    "2",
                    "3",
                    "4",
                    "5",
                    "6",
                    "7",
                    "8",
                    "9",
                    "10",
                    "11",
                    "12",
                    "13",
                    "14",
                    "15",
                    "16",
                    "17",
                    "18",
                    "19",
                    "20",
                },
                new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 }
            );
            if (mission != level.campaignMission)
            {
                Record("Назначить миссию");
                level.campaignMission = mission;
                Changed();
            }
            bool quick = EditorGUILayout.ToggleLeft("Использовать в свободном бою", level.useInQuickBattle);
            if (quick != level.useInQuickBattle)
            {
                Record("Назначить свободный бой");
                level.useInQuickBattle = quick;
                Changed();
            }
            EditorGUILayout.HelpBox(
                "Габариты ограничены диапазоном 16–136 × 12–80, который помещается в существующее игровое изометрическое поле. Назначь 1–20 для кампании; для теста включи свободный бой. Карты лежат в Resources/Levels.",
                MessageType.None
            );
            GUILayout.Space(6);
            GUILayout.Label("ОТОБРАЖЕНИЕ КАРТЫ", EditorStyles.boldLabel);
            viewSprites = EditorGUILayout.ToggleLeft("ТЕКСТУРНЫЙ ПРОСМОТР (все укрытия)", viewSprites);
            if (viewSprites)
                DrawPreviewDiagnostics();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("− Масштаб", GUILayout.Height(23)))
                zoom = Mathf.Max(.4f, zoom / 1.35f);
            if (GUILayout.Button("+ Масштаб", GUILayout.Height(23)))
                zoom = Mathf.Min(10f, zoom * 1.35f);
            if (GUILayout.Button("Вписать", GUILayout.Height(23)))
            {
                zoom = 1f;
                pan = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Приближение", zoom.ToString("0.00") + "×");
            previewAlpha = EditorGUILayout.Slider("Непрозрачность", previewAlpha, .35f, 1f);
            previewScale = EditorGUILayout.Slider("Размер объектов", previewScale, .7f, 2.6f);
            showObjectNames = EditorGUILayout.ToggleLeft("Подписи объектов", showObjectNames);
            EditorGUILayout.HelpBox(
                "Для плотных локаций приблизь карту колёсиком и перемещай её средней кнопкой. Превью показывает рисунки ВСЕХ старых и новых укрытий; столкновения не меняются.",
                MessageType.None
            );
            GUILayout.Space(6);
            GUILayout.Label("КИСТИ И МАТЕРИАЛЫ", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            TerrainButton("Трава", LevelGroundKind.Ground);
            TerrainButton("Дорога", LevelGroundKind.Road);
            TerrainButton("Камни", LevelGroundKind.Rubble);
            GUILayout.EndHorizontal();
            if (
                GUILayout.Button(
                    "Ластик · удалить укрытие",
                    brush == BrushMode.Erase ? ActiveStyle() : GUI.skin.button,
                    GUILayout.Height(26)
                )
            )
            {
                brush = BrushMode.Erase;
                Repaint();
            }
            GUILayout.Space(8);
        }

        void TerrainButton(string name, LevelGroundKind value)
        {
            bool active = brush == BrushMode.Ground && selectedGround == value;
            if (GUILayout.Button(name, active ? ActiveStyle() : GUI.skin.button, GUILayout.Height(26)))
            {
                selectedGround = value;
                brush = BrushMode.Ground;
                Repaint();
            }
        }

        static GUIStyle ActiveStyle()
        {
            // Existing Unity editor style, no dynamic textures/materials allocated per frame.
            return EditorStyles.miniButtonMid;
        }

        void DrawPalette()
        {
            tab = GUILayout.Toolbar(tab, new[] { "Низкие", "Высокие", "Сценарий", "Брифинг", "Новые" });
            GUILayout.Space(4);
            if (
                GUILayout.Button(
                    "Крыша · ЛКМ добавить / ПКМ убрать",
                    brush == BrushMode.Roof ? ActiveStyle() : GUI.skin.button,
                    GUILayout.Height(28)
                )
            )
            {
                brush = BrushMode.Roof;
                Repaint();
            }
            if (tab == 4)
            {
                DrawStudioPalette();
                DrawObjectTransform();
                return;
            }
            if (tab == 2)
            {
                DrawScenario();
                return;
            }
            if (tab == 3)
            {
                DrawBriefingEditor();
                return;
            }
            LevelObjectKind[] palette = tab == 0 ? LowPalette : HighPalette;
            EditorGUILayout.HelpBox(
                tab == 0
                    ? "Сохранённые деревья, кусты, камни и бочки. Актуальные стены, мешки, ящики и мебель — во вкладке «Новые»."
                    : "Актуальные стены и проёмы — во вкладке «Новые». Устаревшие стены удалены.",
                MessageType.None
            );
            DrawObjectTransform();
            if (tab == 0)
            {
                for (int i = 0; i < palette.Length; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    PaletteItem(palette[i]);
                    if (i + 1 < palette.Length)
                        PaletteItem(palette[i + 1]);
                    GUILayout.EndHorizontal();
                }
            }
            else
                foreach (var kind in palette)
                    PaletteItem(kind);
        }

        string decorEditUid;

        LevelObjectCell EditingObject()
        {
            if (level == null || level.objects == null || !level.Inside(editCell.x, editCell.y))
                return null;
            for (int i = level.objects.Count - 1; i >= 0; i--)
            {
                var o = level.objects[i];
                if (o != null && RubezhLevel.Contains(o, editCell.x, editCell.y))
                    return o;
            }
            return null;
        }

        void DrawObjectTransform()
        {
            GUILayout.Space(10);
            GUILayout.Label("ПОВОРОТ И РАСТЯЖЕНИЕ", EditorStyles.boldLabel);
            bool edit = brush == BrushMode.Edit;
            if (
                GUILayout.Button(
                    edit ? "✓ Режим выбора объекта (включён)" : "Выбрать объект на карте",
                    edit ? ActiveStyle() : GUI.skin.button,
                    GUILayout.Height(29)
                )
            )
            {
                brush = BrushMode.Edit;
                Repaint();
            }
            var target = edit ? EditingObject() : null;
            if (edit && target != null)
            {
                var decor = new List<LevelObjectCell>();
                foreach (var item in level.objects)
                    if (
                        item != null
                        && RubezhLevel.Contains(item, editCell.x, editCell.y)
                        && SilentOrders.Core.StudioCatalog.Find(item.assetId)?.Decor == true
                    )
                        decor.Add(item);
                if (decor.Count > 0)
                {
                    int at = decor.FindIndex(item => item.uid == decorEditUid);
                    if (at < 0)
                        at = 0;
                    var names = new string[decor.Count];
                    for (int i = 0; i < decor.Count; i++)
                        names[i] = (i + 1) + ". " + SilentOrders.Core.StudioCatalog.Find(decor[i].assetId).Name;
                    at = EditorGUILayout.Popup("Декор в клетке (до 3)", at, names);
                    target = decor[at];
                    decorEditUid = target.uid;
                    EditorGUI.BeginChangeCheck();
                    float dx = EditorGUILayout.Slider("Смещение X", target.offsetX, -.45f, .45f),
                        dy = EditorGUILayout.Slider("Смещение Y", target.offsetY, -.45f, .45f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Record("Сдвиг декора");
                        target.offsetX = dx;
                        target.offsetY = dy;
                        Changed();
                    }
                    if (GUILayout.Button("В центр клетки"))
                    {
                        Record("Центр декора");
                        target.offsetX = target.offsetY = 0;
                        Changed();
                    }
                    return;
                }
            }

            if (
                target != null
                && !string.IsNullOrEmpty(target.assetId)
                && GUILayout.Button("Отразить текущую текстуру")
            )
            {
                Record("Отразить объект");
                string problem;
                if (!level.MirrorObject(target, out problem))
                    ShowNotification(new GUIContent(problem));
                else
                    Changed();
            }
            if (
                target != null
                && !string.IsNullOrEmpty(target.assetId)
                && !SilentOrders.Core.StudioCatalog.Find(target.assetId).Legacy
            )
            {
                EditorGUILayout.HelpBox(
                    "Объект каталога: используйте готовую проекцию без растяжения.",
                    MessageType.None
                );
                return;
            }
            if (edit && target != null)
            {
                EditorGUILayout.LabelField("Выбран:", Label(target.kind) + " [" + target.x + "," + target.y + "]");
            }
            else if (edit)
            {
                EditorGUILayout.HelpBox(
                    "Щёлкни по укрытию на карте, затем меняй его угол и масштаб. Коллайдер останется в исходной клетке.",
                    MessageType.Info
                );
            }
            else
                EditorGUILayout.LabelField("Параметры следующего объекта кисти", EditorStyles.miniLabel);
            if (edit && target == null)
                return;
            LevelObjectKind transforming = edit ? target.kind : selected;
            if (!RubezhLevel.IsSprite(transforming))
            {
                EditorGUILayout.HelpBox(
                    "Поворот и растяжение в бою доступны для новых объектов с PNG (деревья, камни, мешки и т. д.). Старые процедурные укрытия и стены используют фиксированную геометрию.",
                    MessageType.Info
                );
                return;
            }
            float originalAngle = edit ? target.rotationDegrees : brushAngle;
            float originalX = edit ? LevelObjectCell.SafeScale(target.scaleX) : brushScaleX;
            float originalY = edit ? LevelObjectCell.SafeScale(target.scaleY) : brushScaleY;
            EditorGUI.BeginChangeCheck();
            float angle = EditorGUILayout.Slider("Угол, °", originalAngle, -180f, 180f);
            float x = EditorGUILayout.Slider("Растяжение X", originalX, .4f, 2.5f);
            float y = EditorGUILayout.Slider("Растяжение Y", originalY, .4f, 2.5f);
            if (EditorGUI.EndChangeCheck())
            {
                if (edit)
                {
                    Record("Поворот и размер объекта");
                    target.rotationDegrees = angle;
                    target.scaleX = x;
                    target.scaleY = y;
                    Changed();
                }
                else
                {
                    brushAngle = angle;
                    brushScaleX = x;
                    brushScaleY = y;
                    Repaint();
                }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Сброс 0° / 1× / 1×"))
            {
                if (edit)
                {
                    Record("Сброс трансформации");
                    target.rotationDegrees = 0f;
                    target.scaleX = target.scaleY = 1f;
                    Changed();
                }
                else
                {
                    brushAngle = 0f;
                    brushScaleX = brushScaleY = 1f;
                    Repaint();
                }
            }
            if (edit && GUILayout.Button("В кисть"))
            {
                brushAngle = target.rotationDegrees;
                brushScaleX = LevelObjectCell.SafeScale(target.scaleX);
                brushScaleY = LevelObjectCell.SafeScale(target.scaleY);
                selected = target.kind;
                brush = BrushMode.Object;
                Repaint();
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "Вращение — в плоскости экрана, вокруг нижней точки объекта. Масштаб X/Y влияет только на рисунок, а не на коллизии или проходимость.",
                MessageType.None
            );
        }

        void PaletteItem(LevelObjectKind kind)
        {
            bool active = brush == BrushMode.Object && selected == kind;
            Texture2D image = Thumbnail(kind);
            var old = GUI.backgroundColor;
            if (active)
                GUI.backgroundColor = new Color(.62f, .83f, .55f);
            bool pressed;
            if (image != null)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(143));
                Rect r = GUILayoutUtility.GetRect(130f, 70f);
                GUI.DrawTexture(r, image, ScaleMode.ScaleToFit, true);
                pressed = GUILayout.Button(
                    Label(kind),
                    active ? EditorStyles.miniButtonMid : EditorStyles.miniButton,
                    GUILayout.Height(32)
                );
                GUILayout.EndVertical();
            }
            else
                pressed = GUILayout.Button(
                    Label(kind),
                    active ? EditorStyles.miniButtonMid : GUI.skin.button,
                    GUILayout.MinHeight(45),
                    GUILayout.Width(143)
                );
            GUI.backgroundColor = old;
            if (pressed)
            {
                selected = kind;
                brush = BrushMode.Object;
                Repaint();
            }
        }

        void DrawScenario()
        {
            DrawDefenderEditor();
            EditorGUILayout.HelpBox(
                "Выбери сценарную точку, затем щёлкни по клетке на карте. На месте маркера укрытие автоматически удаляется.",
                MessageType.Info
            );
            ScenarioButton(0, "🚩 Флаг · точка захвата", level.flag);
            ScenarioButton(1, "🟢 Спавн игрока", level.playerSpawn);
            bool car = EditorGUILayout.ToggleLeft("Высадка зелёных на ГАЗ Тигр", level.greenVehicleArrival);
            if (car != level.greenVehicleArrival)
            {
                Record("Транспорт зелёных");
                level.greenVehicleArrival = car;
                Changed();
            }
            if (car)
                EditorGUILayout.HelpBox(
                    "ГАЗ Тигр появляется у левого края по центру, проезжает 4 клетки, останавливается и выпускает отряд. После высадки — неразрушаемое высокое укрытие 8 × 4 клетки.",
                    MessageType.None
                );
            if (!level.customDefenders)
                ScenarioButton(2, "Спавн бота", level.botSpawn);
            GUILayout.Space(5);
            EditorGUILayout.LabelField("Флаг", level.flag.ToString());
            EditorGUILayout.LabelField("Игрок", level.playerSpawn.ToString());
            EditorGUILayout.LabelField("Бот", level.botSpawn.ToString());
            EditorGUILayout.HelpBox(
                "Сценарные маркеры не являются укрытиями. При запуске боя используются настоящие координаты флага и обеих групп.",
                MessageType.None
            );
        }

        void ScenarioButton(int n, string title, Vector2Int point)
        {
            bool active = brush == BrushMode.Marker && marker == n;
            if (
                GUILayout.Button(
                    title + "  " + point.x + "," + point.y,
                    active ? ActiveStyle() : GUI.skin.button,
                    GUILayout.Height(40)
                )
            )
            {
                brush = BrushMode.Marker;
                marker = n;
                Repaint();
            }
        }

        static Vector2 Project(float x, float y, RubezhLevel data, float hx, float hy, Vector2 center)
        {
            return new Vector2(
                center.x + (x + y + 1f - (data.columns + data.rows) * .5f) * hx,
                center.y + (y - x - (data.rows - data.columns) * .5f) * hy
            );
        }

        void DrawMap(Rect outer)
        {
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(outer, new Color(.12f, .17f, .17f, 1f));
            GUI.BeginGroup(outer);
            Rect local = new Rect(0, 0, outer.width, outer.height);
            float fit = Mathf.Min(
                (outer.width - 55f) / ((level.columns + level.rows) * 11f),
                (outer.height - 55f) / ((level.columns + level.rows) * 5f)
            );
            fit = Mathf.Max(.08f, fit) * zoom;
            float hx = 11f * fit,
                hy = 5f * fit;
            Vector2 center = local.center + pan;
            Event evt = Event.current;
            if (evt.type == EventType.ScrollWheel && local.Contains(evt.mousePosition))
            {
                zoom = Mathf.Clamp(zoom * (evt.delta.y > 0 ? .82f : 1.22f), .4f, 10f);
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseDrag && evt.button == 2)
            {
                pan += evt.delta;
                evt.Use();
                Repaint();
            }
            if (evt.type == EventType.MouseDown && evt.button == 2)
            {
                evt.Use();
            }
            Vector2Int hovered = CellAt(
                evt.mousePosition
                    + (
                        brush == BrushMode.Roof
                            ? new Vector2(0, SilentOrders.Core.RoofRules.LiftPixels * fit)
                            : Vector2.zero
                    ),
                hx,
                hy,
                center
            );
            if (
                (evt.type == EventType.MouseDown && (evt.button == 0 || evt.button == 1))
                && local.Contains(evt.mousePosition)
            )
            {
                painting = true;
                previousX = previousY = int.MinValue;
                if (brush != BrushMode.Edit)
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("Рубеж: нарисовать объект");
                    Undo.RegisterCompleteObjectUndo(level, "Рубеж: редактировать карту");
                }
                PaintAt(hovered, evt.button == 1);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDrag && painting && (evt.button == 0 || evt.button == 1))
            {
                PaintAt(hovered, evt.button == 1);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && (evt.button == 0 || evt.button == 1))
            {
                painting = false;
                previousX = previousY = int.MinValue;
            }
            if (evt.type == EventType.Repaint)
            {
                // Build a dense lookup once per paint frame. Sparse lists are used for
                // serialization but searching them for every cell is O(cells × props).
                int cellCount = level.columns * level.rows;
                var terrainLookup = new LevelGroundKind[cellCount];
                var coverLookup = new LevelObjectKind[cellCount];
                if (level.ground != null)
                    foreach (var ground in level.ground)
                        if (ground != null && level.Inside(ground.x, ground.y))
                            terrainLookup[ground.y * level.columns + ground.x] = ground.kind;
                if (level.objects != null)
                    foreach (var prop in level.objects)
                        if (prop != null && level.Inside(prop.x, prop.y))
                            coverLookup[prop.y * level.columns + prop.x] = prop.kind;
                DrawTiles(hx, hy, center, local, terrainLookup, coverLookup);
                DrawProps(hx, hy, center, fit);
                if (level.roof != null)
                {
                    Handles.BeginGUI();
                    foreach (var roof in level.roof)
                    {
                        var pos = Project(roof.x, roof.y, level, hx, hy, center);
                        pos.y -= SilentOrders.Core.RoofRules.LiftPixels * fit;
                        Diamond(pos, hx, hy, new Color(.63f, .67f, .71f, .10f), new Color(.63f, .67f, .71f, .10f));
                    }
                    Handles.EndGUI();
                }
                DrawMarkers(hx, hy, center);
                if (brush == BrushMode.Edit && EditingObject() != null)
                {
                    Vector2 pos = Project(editCell.x, editCell.y, level, hx, hy, center);
                    Handles.BeginGUI();
                    Diamond(pos, hx, hy, new Color(.22f, .86f, 1f, .08f), new Color(.18f, .89f, 1f, .95f));
                    Handles.EndGUI();
                }
                if (level.Inside(hovered.x, hovered.y))
                {
                    var previewAsset =
                        tab == 4 && brush == BrushMode.Object
                            ? SilentOrders.Core.StudioCatalog.Find(studioSelected, studioMirrored)
                            : null;
                    int pw = previewAsset == null ? 1 : previewAsset.Width,
                        ph = previewAsset == null ? 1 : previewAsset.Height;
                    for (int yy = hovered.y; yy < hovered.y + ph; yy++)
                    for (int xx = hovered.x; xx < hovered.x + pw; xx++)
                    {
                        Vector2 v = Project(xx, yy, level, hx, hy, center);
                        if (brush == BrushMode.Roof)
                            v.y -= SilentOrders.Core.RoofRules.LiftPixels * fit;
                        Diamond(
                            v,
                            hx,
                            hy,
                            new Color(1f, 1f, .64f, .18f),
                            level.Inside(xx, yy) ? new Color(.95f, 1f, .63f, .9f) : Color.red
                        );
                    }
                    var studioOver = previewStudio.Cells[hovered.y * level.columns + hovered.x];
                    if (studioOver != null)
                        GUI.Label(
                            new Rect(10, 36, 400, 24),
                            studioOver.Asset.Name + " · " + studioOver.Asset.Width + "×" + studioOver.Asset.Height,
                            EditorStyles.whiteMiniLabel
                        );
                    var overKind = level.ObjectAt(hovered.x, hovered.y);
                    GUI.Label(
                        new Rect(10, 10, 330, 24),
                        "Клетка "
                            + hovered.x
                            + ", "
                            + hovered.y
                            + (overKind != LevelObjectKind.None ? " · " + Label(overKind) : " · свободна"),
                        EditorStyles.whiteMiniLabel
                    );
                }
            }
            GUI.EndGroup();
        }

        Vector2Int CellAt(Vector2 mouse, float hx, float hy, Vector2 center)
        {
            float a = (mouse.x - center.x) / hx + (level.columns + level.rows) * .5f - 1f;
            float b = (mouse.y - center.y) / hy + (level.rows - level.columns) * .5f;
            int mx = Mathf.RoundToInt((a - b) * .5f),
                my = Mathf.RoundToInt((a + b) * .5f);
            Vector2Int result = new Vector2Int(-1, -1);
            float best = 1.01f;
            for (int y = my - 1; y <= my + 1; y++)
            for (int x = mx - 1; x <= mx + 1; x++)
            {
                if (!level.Inside(x, y))
                    continue;
                var v = Project(x, y, level, hx, hy, center);
                float dist = Mathf.Abs(mouse.x - v.x) / hx + Mathf.Abs(mouse.y - v.y) / hy;
                if (dist < best)
                {
                    best = dist;
                    result = new Vector2Int(x, y);
                }
            }
            return result;
        }

        void PaintAt(Vector2Int p, bool eraseMouse)
        {
            if (!level.Inside(p.x, p.y) || p.x == previousX && p.y == previousY)
                return;
            previousX = p.x;
            previousY = p.y;
            if (!eraseMouse && brush == BrushMode.Edit)
            {
                editCell = p;
                if (EditingObject() == null)
                    ShowNotification(new GUIContent("На клетке нет объекта"));
                Repaint();
                return;
            }
            if (brush == BrushMode.Roof)
                level.SetRoof(p.x, p.y, !eraseMouse);
            else if (eraseMouse || brush == BrushMode.Erase)
                level.SetObject(p.x, p.y, LevelObjectKind.None);
            else if (brush == BrushMode.Ground)
                level.SetGround(p.x, p.y, selectedGround);
            else if (brush == BrushMode.Marker)
            {
                if (marker == 0)
                    level.SetFlag(p.x, p.y);
                else if (marker == 1)
                    level.SetPlayerSpawn(p.x, p.y);
                else if (marker >= 3 && level.customDefenders && marker - 3 < level.defenders.Count)
                {
                    var g = level.defenders[marker - 3];
                    if (level.defenders.Exists(other => other != g && other.position == p) || p == level.playerSpawn)
                    {
                        ShowNotification(new GUIContent("Эта клетка уже занята спавном"));
                        return;
                    }
                    g.position = level.ClampPoint(p);
                    level.SetObject(p.x, p.y, LevelObjectKind.None);
                }
                else
                    level.SetBotSpawn(p.x, p.y);
            }
            else if (tab == 4 && studioSelected != null)
            {
                if (!level.SetStudioObject(p.x, p.y, studioSelected, studioMirrored))
                    ShowNotification(new GUIContent("Объект выходит за карту или пересекает сценарную точку"));
            }
            else
                level.SetObject(p.x, p.y, selected, brushAngle, brushScaleX, brushScaleY);
            Changed();
        }

        static void Diamond(Vector2 p, float hx, float hy, Color fill, Color outline)
        {
            Vector3 a = new Vector3(p.x, p.y - hy, 0),
                b = new Vector3(p.x + hx, p.y, 0),
                c = new Vector3(p.x, p.y + hy, 0),
                d = new Vector3(p.x - hx, p.y, 0);
            Handles.color = fill;
            Handles.DrawAAConvexPolygon(a, b, c, d);
            Handles.color = outline;
            Handles.DrawAAPolyLine(1.0f, a, b, c, d, a);
        }

        void DrawTiles(
            float hx,
            float hy,
            Vector2 center,
            Rect local,
            LevelGroundKind[] terrainLookup,
            LevelObjectKind[] coverLookup
        )
        {
            PrepareStudioPreview();
            Handles.BeginGUI();
            for (int y = 0; y < level.rows; y++)
            for (int x = 0; x < level.columns; x++)
            {
                Vector2 p = Project(x, y, level, hx, hy, center);
                if (p.x + hx < 0 || p.y + hy < 0 || p.x - hx > local.width || p.y - hy > local.height)
                    continue;
                LevelGroundKind ground = terrainLookup[y * level.columns + x];
                LevelObjectKind obj = coverLookup[y * level.columns + x];
                Color baseColor =
                    ground == LevelGroundKind.Road ? new Color(.34f, .38f, .34f)
                    : ground == LevelGroundKind.Rubble ? new Color(.46f, .45f, .41f)
                    : new Color(.31f, .39f, .30f);
                // The object's artwork is the primary visual cue, not an opaque colored tile.
                if (!viewSprites && RubezhLevel.IsHigh(obj))
                    baseColor = new Color(.37f, .46f, .45f);
                else if (!viewSprites && RubezhLevel.IsLow(obj))
                    baseColor = new Color(.46f, .43f, .30f);
                Diamond(p, hx, hy, baseColor, new Color(.17f, .23f, .20f, .52f));
                var terrain = previewStudio.Ground[y * level.columns + x];
                if (terrain != null)
                    DrawStudioObject(
                        new SilentOrders.Core.StudioPlaced
                        {
                            Asset = terrain,
                            X = x,
                            Y = y,
                        },
                        hx,
                        hy,
                        center
                    );
                if (!viewSprites && RubezhLevel.IsHigh(obj))
                {
                    Handles.color = new Color(.62f, .67f, .64f);
                    Handles.DrawSolidRectangleWithOutline(
                        new Rect(p.x - hx * .24f, p.y - hy * 1.8f, hx * .48f, hy * 1.5f),
                        new Color(.50f, .58f, .55f),
                        new Color(.19f, .24f, .24f)
                    );
                }
            }
            Handles.EndGUI();
        }

        static void PreviewDimensions(LevelObjectKind kind, out int width, out int height)
        {
            if (RubezhLevel.IsSprite(kind))
            {
                Rubezh.Graphics.PropSize((int)kind - 100, out width, out height);
                return;
            }
            switch (kind)
            {
                case LevelObjectKind.HighWallOriginal:
                    width = 48;
                    height = 61;
                    break;
                case LevelObjectKind.LowCoverBarrel:
                    width = 29;
                    height = 34;
                    break;
                case LevelObjectKind.LowCoverConcrete:
                    width = 54;
                    height = 29;
                    break;
                default:
                    width = 52;
                    height = 32;
                    break;
            }
        }

        void DrawProps(float hx, float hy, Vector2 center, float scale)
        {
            if (level.objects == null)
                return;
            var ordered = new List<LevelObjectCell>(level.objects);
            // The same iso painter order as the battle scene (far to near).
            ordered.Sort((a, b) => PreviewDepth(a).CompareTo(PreviewDepth(b)));
            foreach (var obj in ordered)
            {
                if (obj == null || !level.Inside(obj.x, obj.y))
                    continue;
                if (!string.IsNullOrEmpty(obj.assetId))
                {
                    var p = PreviewObject(obj);
                    if (p != null)
                        DrawStudioObject(p, hx, hy, center);
                    continue;
                }
                Vector2 foot = Project(obj.x, obj.y, level, hx, hy, center);
                Texture2D image = Thumbnail(obj.kind);
                if (viewSprites && image != null)
                {
                    int w,
                        h;
                    PreviewDimensions(obj.kind, out w, out h);
                    float drawingScale = scale * previewScale;
                    float ww = Mathf.Max(6f, w * drawingScale * LevelObjectCell.SafeScale(obj.scaleX));
                    float hh = Mathf.Max(6f, h * drawingScale * LevelObjectCell.SafeScale(obj.scaleY));
                    Rect rect = new Rect(foot.x - ww * .5f, foot.y - hh + 2f * scale, ww, hh);
                    Color saved = GUI.color;
                    Matrix4x4 savedMatrix = GUI.matrix;
                    GUI.color = new Color(1f, 1f, 1f, previewAlpha);
                    GUIUtility.RotateAroundPivot(obj.rotationDegrees, foot);
                    GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, true);
                    GUI.matrix = savedMatrix;
                    GUI.color = saved;
                    if (showObjectNames && scale > .25f)
                        GUI.Label(
                            new Rect(foot.x - 82f, foot.y + hy, 164f, 17f),
                            Label(obj.kind),
                            EditorStyles.centeredGreyMiniLabel
                        );
                }
                else
                {
                    // Fallback for art import failure or disabled previews, never an invisible object.
                    Color fill = RubezhLevel.IsHigh(obj.kind)
                        ? new Color(.53f, .60f, .56f)
                        : new Color(.64f, .60f, .41f);
                    Rect block = new Rect(foot.x - hx * .45f, foot.y - hy * 2.0f, hx * .9f, hy * 1.9f);
                    EditorGUI.DrawRect(block, fill);
                    GUI.Label(
                        new Rect(block.x, block.y, block.width, block.height),
                        viewSprites ? "?" : "◆",
                        EditorStyles.centeredGreyMiniLabel
                    );
                }
            }
        }

        void DrawMarkers(float hx, float hy, Vector2 center)
        {
            DrawMarker(level.flag, "Ф", new Color(.96f, .82f, .32f), hx, hy, center);
            DrawMarker(level.playerSpawn, "И", new Color(.37f, .83f, .49f), hx, hy, center);
            if (level.greenVehicleArrival)
                DrawArrivalPreview(true, hx, hy, center);
            if (!level.customDefenders)
                DrawMarker(level.botSpawn, "Б", new Color(.94f, .39f, .35f), hx, hy, center);
            else
                for (int i = 0; i < level.defenders.Count; i++)
                {
                    var g = level.defenders[i];
                    if (g.vehicleArrival)
                        DrawArrivalPreview(false, hx, hy, center);
                    if (i == groupSelection)
                    {
                        var ring = new Vector3[65];
                        for (int r = 0; r <= 64; r++)
                        {
                            float angle = r * Mathf.PI * 2f / 64f;
                            Vector2 point = Project(
                                g.position.x + Mathf.Cos(angle) * g.holdRadius,
                                g.position.y + Mathf.Sin(angle) * g.holdRadius,
                                level,
                                hx,
                                hy,
                                center
                            );
                            ring[r] = new Vector3(point.x, point.y, 0);
                        }
                        Handles.BeginGUI();
                        Handles.color = new Color(1f, .65f, .25f, .7f);
                        Handles.DrawAAPolyLine(2f, ring);
                        Handles.EndGUI();
                    }
                    DrawMarker(
                        g.position,
                        (i + 1).ToString() + (g.arrivalSeconds > 0 ? "+" : ""),
                        g.behavior == DefenderBehavior.Attack ? new Color(1f, .52f, .2f) : new Color(.94f, .39f, .35f),
                        hx,
                        hy,
                        center
                    );
                }
        }

        void DrawArrivalPreview(bool green, float hx, float hy, Vector2 center)
        {
            float x = green ? 5f : level.columns - 5f;
            float y = level.rows * .5f;
            var p = Project(x - .5f, y - .5f, level, hx, hy, center);
            Handles.BeginGUI();
            float fromX = green ? 1f : level.columns - 1f;
            Vector2 edge = Project(fromX - .5f, y - .5f, level, hx, hy, center);
            Handles.color = green ? new Color(.4f, .85f, .5f, .8f) : new Color(.95f, .45f, .4f, .8f);
            Handles.DrawAAPolyLine(3f, new Vector3(edge.x, edge.y, 0), new Vector3(p.x, p.y, 0));
            var col = green ? new Color(.4f, .85f, .5f, .35f) : new Color(.95f, .45f, .4f, .35f);
            // Match the exact 8 x 4 tile collision footprint of a parked vehicle.
            float halfLength = SilentOrders.Core.VehicleArrivals.LengthCells * .5f;
            float halfWidth = SilentOrders.Core.VehicleArrivals.WidthCells * .5f;
            Vector2 a = Project(x - .5f - halfLength, y - .5f - halfWidth, level, hx, hy, center);
            Vector2 b = Project(x - .5f + halfLength, y - .5f - halfWidth, level, hx, hy, center);
            Vector2 c = Project(x - .5f + halfLength, y - .5f + halfWidth, level, hx, hy, center);
            Vector2 d = Project(x - .5f - halfLength, y - .5f + halfWidth, level, hx, hy, center);
            Handles.color = col;
            Handles.DrawAAConvexPolygon(a, b, c, d);
            Handles.color = Color.white;
            Handles.DrawAAPolyLine(
                2f,
                new Vector3(a.x, a.y, 0),
                new Vector3(b.x, b.y, 0),
                new Vector3(c.x, c.y, 0),
                new Vector3(d.x, d.y, 0),
                new Vector3(a.x, a.y, 0)
            );
            Handles.EndGUI();
            var image = VehicleThumbnail(green);
            if (image != null && viewSprites)
            {
                float w = Rubezh.Graphics.VehicleRenderWidth * (hx / 11f) * previewScale;
                float h = Rubezh.Graphics.VehicleRenderHeight * (hx / 11f) * previewScale;
                Color original = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, previewAlpha);
                GUI.DrawTexture(
                    new Rect(p.x - w * .5f, p.y - h * Rubezh.Graphics.VehicleGroundAnchor, w, h),
                    image,
                    ScaleMode.ScaleToFit,
                    true
                );
                GUI.color = original;
            }
            GUI.Label(
                new Rect(p.x - 36, p.y + hy * 1.5f, 100, 23),
                green ? "ГАЗ Тигр" : "Humvee",
                EditorStyles.whiteMiniLabel
            );
        }

        void DrawMarker(Vector2Int point, string label, Color color, float hx, float hy, Vector2 center)
        {
            Vector2 p = Project(point.x, point.y, level, hx, hy, center);
            float size = Mathf.Max(15f, hx * 1.18f);
            Rect r = new Rect(p.x - size * .5f, p.y - size * .65f, size, size);
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawSolidDisc(new Vector3(r.center.x, r.center.y, 0), Vector3.forward, size * .4f);
            Handles.EndGUI();
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.black;
            GUI.Label(r, label, style);
        }

        void Record(string description)
        {
            Undo.RecordObject(level, description);
        }

        void Changed()
        {
            EditorUtility.SetDirty(level);
            Repaint();
        }

        void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Rubezh/Resources"))
                AssetDatabase.CreateFolder("Assets/Rubezh", "Resources");
            if (!AssetDatabase.IsValidFolder(LevelFolder))
                AssetDatabase.CreateFolder("Assets/Rubezh/Resources", "Levels");
        }

        void CreateNew()
        {
            EnsureFolders();
            string path = EditorUtility.SaveFilePanelInProject(
                "Создать уровень Рубежа",
                "Level_01",
                "asset",
                "Сохраняйте уровни в Assets/Rubezh/Resources/Levels",
                LevelFolder
            );
            if (string.IsNullOrEmpty(path))
                return;
            if (!path.StartsWith(LevelFolder + "/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog(
                    "Рубеж",
                    "Уровни должны лежать в " + LevelFolder + " для загрузки игрой.",
                    "OK"
                );
                return;
            }
            var data = CreateInstance<RubezhLevel>();
            data.campaignMission = 0;
            data.useInQuickBattle = false;
            data.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            level = data;
            Selection.activeObject = level;
            SaveLast();
            pan = Vector2.zero;
            zoom = 1f;
            editCell = new Vector2Int(-1, -1);
            Repaint();
        }

        void Duplicate()
        {
            if (level == null)
                return;
            EnsureFolders();
            string path = EditorUtility.SaveFilePanelInProject(
                "Копия уровня",
                level.name + "_Copy",
                "asset",
                "Сохранить копию уровня",
                LevelFolder
            );
            if (string.IsNullOrEmpty(path))
                return;
            if (!path.StartsWith(LevelFolder + "/", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Рубеж", "Копии уровней должны находиться в " + LevelFolder, "OK");
                return;
            }
            var clone = Instantiate(level);
            clone.campaignMission = 0;
            clone.useInQuickBattle = false;
            clone.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            SelectLevel(clone);
            Selection.activeObject = clone;
        }

        void Save()
        {
            if (level == null)
                return;
            if (!AssetDatabase.Contains(level))
            {
                SaveWorkingAsset();
                return;
            }
            string issue;
            if (!LevelValidation.Valid(level, out issue))
            {
                EditorUtility.DisplayDialog("Рубеж", issue, "OK");
                return;
            }
            if (!ResolveAssignments())
                return;
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            SaveLast();
            ShowNotification(new GUIContent("Уровень сохранён"));
        }

        void Validate()
        {
            if (level == null)
                return;
            var errors = new List<string>();
            string issue;
            if (!LevelValidation.Valid(level, out issue))
                errors.Add(issue);
            if (!AssetDatabase.GetAssetPath(level).StartsWith(LevelFolder + "/", StringComparison.Ordinal))
                errors.Add("Файл расположен вне папки Resources/Levels.");
            if (
                level.flag == level.playerSpawn
                || !level.customDefenders && (level.flag == level.botSpawn || level.playerSpawn == level.botSpawn)
            )
                errors.Add("Сценарные точки совпадают. Разнеси флаг и оба спавна.");
            if (
                !level.Inside(level.flag.x, level.flag.y)
                || !level.Inside(level.playerSpawn.x, level.playerSpawn.y)
                || !level.customDefenders && !level.Inside(level.botSpawn.x, level.botSpawn.y)
            )
                errors.Add("Есть сценарные точки вне карты.");
            if (level.campaignMission == 0 && !level.useInQuickBattle)
                errors.Add("Уровень не назначен ни на кампанию, ни на свободный бой.");
            foreach (var cell in level.objects)
            {
                if (
                    cell != null
                    && (
                        !level.Fits(cell)
                        || (SilentOrders.Core.StudioCatalog.Find(cell.assetId)?.Blocking ?? true)
                            && level.IsMarker(cell.x, cell.y)
                    )
                )
                    errors.Add("Укрытие за границей или на сценарном маркере: " + cell.x + "," + cell.y);
            }
            var levels = AssetDatabase.FindAssets("t:RubezhLevel", new[] { LevelFolder });
            foreach (var guid in levels)
            {
                var other = AssetDatabase.LoadAssetAtPath<RubezhLevel>(AssetDatabase.GUIDToAssetPath(guid));
                if (other == null || other == level || !other.enabledInGame)
                    continue;
                if (level.campaignMission > 0 && level.campaignMission == other.campaignMission)
                    errors.Add("Миссия " + level.campaignMission + " уже назначена уровню «" + other.name + "».");
                if (level.useInQuickBattle && other.useInQuickBattle)
                    errors.Add(
                        "Несколько уровней для свободного боя; будет выбран первый по имени: «" + other.name + "»."
                    );
            }
            EditorUtility.DisplayDialog(
                "Проверка уровня",
                errors.Count == 0 ? "Проверка пройдена. Уровень назначен корректно." : string.Join("\n", errors),
                "Понятно"
            );
        }
    }
}
#endif
