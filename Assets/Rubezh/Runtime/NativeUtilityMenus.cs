using System;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
#endif
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        GameObject utilityObject;
        UIDocument utilityDocument;
        PanelSettings utilityPanel;
        FontAsset utilityFont;
        int utilityScreen = -1;
        float utilityWidth;
        bool utilityDirty;
        string[] utilityChoices;
        Label utilityMessage;

        Button UtilityButton(
            VisualElement parent,
            string text,
            Action action,
            float x,
            float y,
            float w,
            float h,
            bool primaryAction = false
        )
        {
            var button = new Button(() =>
            {
                action();
                utilityDirty = true;
            })
            {
                text = text,
            };
            button.AddToClassList(primaryAction ? "battle-primary" : "battle-button");
            if (primaryAction)
                button.AddToClassList("battle-button");
            button.style.unityFontDefinition = FontDefinition.FromSDFFont(utilityFont);
            Place(button, x, y, w, h);
            parent.Add(button);
            return button;
        }

        Label UtilityLabel(VisualElement parent, string text, float x, float y, float w, float h, int size)
        {
            var label = new Label(text);
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(utilityFont);
            Place(label, x, y, w, h);
            label.style.fontSize = size;
            label.style.unityFontStyleAndWeight = (size >= 30 || size == 21) ? FontStyle.Bold : FontStyle.Normal;
            label.style.color = (size == 21 || size == 23) ? muted : ink;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }

        void BuildUtilitySettings(VisualElement root)
        {
            UtilityLabel(root, "РУБЕЖ · НАСТРОЙКИ", 60, 75, W - 120, 60, 21);
            UtilityLabel(root, "Настройки", 60, 155, W - 120, 85, 54);
            float w = Mathf.Min(850, W - 120),
                x = (W - w) * .5f;
            UtilityButton(root, "Диагностика графики · Unity", GraphicsDiagnostics, x, 225, w, 34);
            var box = new VisualElement();
            box.AddToClassList("battle-panel");
            Place(box, x, 260, w, 650);
            root.Add(box);
            UtilityButton(
                root,
                "Лимит FPS: " + frameRateLimit,
                () => Pick("Лимит FPS", new[] { "30", "60", "90", "120" }, i => SetFrameRate(FrameRateChoices[i])),
                x + 35,
                270,
                w - 70,
                40
            );
            UtilityLabel(root, "КАЧЕСТВО ГРАФИКИ", x + 35, 315, w - 70, 55, 34);
            string[] qualities = { "ПИКСЕЛИ!!!", "Низкое", "Среднее", "Высокое", "Ультра" };
            for (int i = 0; i < qualities.Length; i++)
            {
                int level = i - 1;
                float bw = (w - 60 - 15 * (qualities.Length - 1)) / qualities.Length;
                var b = UtilityButton(
                    root,
                    qualities[i],
                    () => SetGraphics(level),
                    x + 30 + i * (bw + 15),
                    395,
                    bw,
                    75
                );
                if (graphicsTier == level)
                    b.AddToClassList("battle-chosen");
            }
            UtilityLabel(
                root,
                graphicsTier == -1 ? "Условные пиксельные фигурки без атласных спрайтов в бою"
                    : graphicsTier == 3 ? "Исходные спрайты · видимая область в разрешении экрана"
                    : graphicsTier == 2 ? "Полное разрешение изображения"
                    : graphicsTier == 1 ? "Уменьшенное разрешение изображения (75%)"
                    : "Уменьшенное разрешение изображения (50%)",
                x + 35,
                485,
                w - 70,
                65,
                23
            );
            UtilityLabel(root, "ЭФФЕКТЫ", x + 35, 570, 360, 55, 34);
            UtilityButton(
                root,
                soundEnabled ? "ВКЛ" : "ВЫКЛ",
                () => SetSound(!soundEnabled),
                x + w - 270,
                565,
                220,
                72
            );
            UtilityVolume(root, "Эффекты", x + 35, 650, w - 70, false);
            UtilityVolume(root, "Музыка", x + 35, 745, w - 70, true);
            UtilityButton(
                root,
                musicEnabled ? "Музыка: вкл" : "Музыка: выкл",
                () => ApplyCampOption("musicOn", musicEnabled ? 0 : 1),
                x + w - 245,
                735,
                210,
                45
            );
            UtilityButton(
                root,
                (fullVisibility ? "☑ " : "☐ ") + "Видимость",
                () =>
                {
                    fullVisibility = !fullVisibility;
                    SaveVisibility();
                },
                x + 35,
                850,
                (w - 85) * .55f,
                50
            );
            if (!fullVisibility)
                UtilityButton(
                    root,
                    (hardcoreFog ? "☑ " : "☐ ") + "Хардкор",
                    () =>
                    {
                        hardcoreFog = !hardcoreFog;
                        SaveVisibility();
                    },
                    x + 50 + (w - 85) * .55f,
                    850,
                    (w - 85) * .45f,
                    50
                );
            UtilityButton(
                root,
                "Полный сброс прогресса",
                () =>
                    Pick(
                        "Удалить весь прогресс и начать заново?",
                        new[] { "Отмена", "Удалить весь прогресс" },
                        i =>
                        {
                            if (i == 1)
                                try
                                {
                                    ResetCareer(true);
                                }
                                catch (Exception e)
                                {
                                    message = "Не удалось сбросить прогресс: " + e.Message;
                                }
                        }
                    ),
                x,
                917,
                w * .49f,
                62
            );
            UtilityButton(
                root,
                settingsReturn == ScreenId.Battle ? "← В БОЙ" : "← НАЗАД",
                CloseSettings,
                x + w * .51f,
                917,
                w * .49f,
                62,
                true
            );
        }

        void UtilityVolume(VisualElement root, string name, float x, float y, float w, bool music)
        {
            float value = music ? musicVolume : effectsVolume;
            var label = UtilityLabel(root, name + " · " + Mathf.RoundToInt(value * 100) + "%", x, y, w - 220, 36, 23);
            var slider = new SliderInt(0, 100) { value = Mathf.RoundToInt(value * 100) };
            Place(slider, x, y + 35, w, 38);
            slider.RegisterValueChangedCallback(e =>
            {
                ApplyCampOption(music ? "music" : "effects", e.newValue);
                label.text = name + " · " + e.newValue + "%";
            });
            root.Add(slider);
        }

        void BuildUtilityArchive(VisualElement root)
        {
            UtilityLabel(root, "РУБЕЖ · АРХИВ ТЕСТОВ", 60, 35, W - 500, 40, 21);
            UtilityLabel(root, "Интересные бои", 60, 90, W - 500, 80, 54);
            UtilityButton(root, "Главное меню", () => ShowCamp(), W - 390, 45, 330, 65);
            UtilityLabel(
                root,
                InterestingArchive.CompatibilityWarning
                    + " Доступны пауза, скорость и камера; приказы взяты из архива.",
                60,
                190,
                W - 120,
                110,
                23
            );
            try
            {
                if (interestingArchive == null)
                    interestingArchive = InterestingArchive.Load();
            }
            catch (Exception e)
            {
                UtilityLabel(root, e.Message, 60, 300, W - 120, 150, 23);
                return;
            }
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            Place(scroll, 60, 330, W - 120, 585);
            root.Add(scroll);
            foreach (var entry in interestingArchive.battles)
            {
                var panel = new VisualElement();
                panel.AddToClassList("battle-panel");
                panel.style.height = 185;
                panel.style.minHeight = 185;
                panel.style.marginBottom = 10;
                panel.style.flexShrink = 0;
                scroll.Add(panel);
                UtilityLabel(panel, "№" + entry.id + " · " + entry.title, 25, 14, W - 465, 45, 34);
                UtilityLabel(panel, entry.description, 25, 68, W - 465, 100, 23);
                UtilityButton(panel, "Пересчитать →", () => StartInteresting(entry), W - 405, 55, 255, 75, true);
            }
            if (WatchingInteresting && battle.Outcome == SilentOrders.Core.Outcome.Running)
                UtilityButton(
                    root,
                    "Продолжить просмотр №" + interestingSession.Entry.id,
                    () => ResumeBattle(true),
                    60,
                    930,
                    W - 120,
                    50
                );
        }

        void BuildUtilityChoice(VisualElement root)
        {
            var overlay = new VisualElement();
            Place(overlay, 0, 0, W, H);
            overlay.style.backgroundColor = new Color(0, 0, 0, .65f);
            root.Add(overlay);
            float w = Mathf.Min(1100, W - 100),
                h = Mathf.Min(800, 150 + pickOptions.Length * 90),
                x = (W - w) / 2,
                y = (H - h) / 2;
            var panel = new VisualElement();
            panel.AddToClassList("battle-panel");
            Place(panel, x, y, w, h);
            overlay.Add(panel);
            UtilityLabel(panel, pickTitle, 32, 20, w - 130, 60, 34);
            UtilityButton(
                panel,
                "×",
                () =>
                {
                    pickOptions = null;
                    pickApply = null;
                },
                w - 85,
                15,
                65,
                60
            );
            var list = new ScrollView(ScrollViewMode.Vertical);
            Place(list, 32, 95, w - 64, h - 115);
            panel.Add(list);
            for (int i = 0; i < pickOptions.Length; i++)
            {
                int choice = i;
                var button = new Button(() =>
                {
                    var action = pickApply;
                    pickOptions = null;
                    pickApply = null;
                    utilityDirty = true;
                    action?.Invoke(choice);
                })
                {
                    text = pickOptions[i],
                };
                button.AddToClassList("battle-button");
                button.style.unityFontDefinition = FontDefinition.FromSDFFont(utilityFont);
                button.style.height = 75;
                button.style.minHeight = 75;
                button.style.marginBottom = 12;
                list.Add(button);
            }
        }
#endif

        void UpdateNativeUtilityMenus()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            bool needed =
                !loading
                && (
                    screen == ScreenId.Settings
                    || screen == ScreenId.DeveloperScenarios
                    || (screen == ScreenId.Camp && nativeCamp == null && !campInitializing)
                );
            if (!needed)
            {
                HideNativeUtilityMenus();
                return;
            }
            if (screen == ScreenId.DeveloperScenarios && !DeveloperBuild.InterestingAvailable)
            {
                ShowCamp();
                return;
            }
            var safe = Screen.safeArea;
            float scale = Mathf.Min(safe.height / H, safe.width / 1400f);
            W = safe.width / scale;
            if (utilityObject == null)
            {
                utilityObject = new GameObject("Rubezh utility screens · UI Toolkit");
                utilityDocument = utilityObject.AddComponent<UIDocument>();
                utilityPanel = ScriptableObject.CreateInstance<PanelSettings>();
                utilityPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
                utilityPanel.sortingOrder = 400;
                utilityPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");
                utilityDocument.panelSettings = utilityPanel;
                var font = Resources.Load<Font>("Fonts/RubezhSans");
                if (font == null)
                    throw new InvalidOperationException("Нет Fonts/RubezhSans");
                utilityFont = FontAsset.CreateFontAsset(
                    font,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic,
                    true
                );
                utilityDirty = true;
            }
            utilityPanel.scale = scale;
            var root = utilityDocument.rootVisualElement;
            root.style.marginLeft = safe.x / scale;
            root.style.marginRight = (Screen.width - safe.xMax) / scale;
            root.style.marginTop = (Screen.height - safe.yMax) / scale;
            root.style.marginBottom = safe.y / scale;
            if (
                utilityDirty
                || utilityScreen != screen
                || utilityWidth != W
                || !ReferenceEquals(utilityChoices, pickOptions)
            )
            {
                root.Clear();
                root.style.flexGrow = 1;
                root.style.backgroundColor = bg;
                root.style.color = ink;
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(utilityFont);
                var css = Resources.Load<StyleSheet>("NativeBattle/Hud");
                if (css != null && !root.styleSheets.Contains(css))
                    root.styleSheets.Add(css);
                utilityScreen = screen;
                utilityWidth = W;
                utilityChoices = pickOptions;
                utilityDirty = false;
                var main = new VisualElement();
                Place(main, 0, 0, W, H);
                root.Add(main);
                if (screen == ScreenId.Settings)
                    BuildUtilitySettings(main);
                else if (screen == ScreenId.DeveloperScenarios)
                    BuildUtilityArchive(main);
                else
                    UtilityButton(main, "Открыть лагерь", ShowCamp, W / 2 - 180, 500, 360, 65);
                utilityMessage = UtilityLabel(main, message, 32, H - 27, W - 64, 26, 21);
                if (pickOptions != null)
                {
                    main.SetEnabled(false);
                    BuildUtilityChoice(root);
                }
            }
            if (utilityMessage != null && utilityMessage.text != message)
                utilityMessage.text = message;
#endif
        }

        void HideNativeUtilityMenus()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (utilityObject != null)
                Destroy(utilityObject);
            if (utilityPanel != null)
                Destroy(utilityPanel);
            if (utilityFont != null)
            {
                foreach (var atlas in utilityFont.atlasTextures)
                    if (atlas != null)
                        Destroy(atlas);
                if (utilityFont.material != null)
                    Destroy(utilityFont.material);
                Destroy(utilityFont);
            }
            utilityObject = null;
            utilityDocument = null;
            utilityPanel = null;
            utilityFont = null;
            utilityMessage = null;
            utilityChoices = null;
            utilityScreen = -1;
#endif
        }
    }
}
