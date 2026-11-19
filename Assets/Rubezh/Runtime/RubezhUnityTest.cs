using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.UIElements;
#endif
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        bool testBattle,
            unityTestBattle,
            testPreviousSoftware,
            testPreviousVisibility,
            testPreviousFog;
        int testPreviousQuality;
        string unityTestError;
        string testMapLabel = "ЛЕС · ПРОСЕКА";
        int testMapSeed = 200001;
        int testFpsFrames;
        float testFpsElapsed,
            testLiveFps;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        GameObject testHudObject;
        PanelSettings testPanel;
        UIDocument testDocument;
        Image testImage;
        Label testStatus;
        VisualElement testOverlay;
        float testStatusAt;
        bool testMultiGesture;
        float testDragDistance;
        readonly System.Collections.Generic.Dictionary<int, Vector2> testPointers =
            new System.Collections.Generic.Dictionary<int, Vector2>();

        void CreateUnityTestHud()
        {
            testFpsFrames = 0;
            testFpsElapsed = 0;
            testLiveFps = 0;
            testHudObject = new GameObject("Battle · UI Toolkit");
            testDocument = testHudObject.AddComponent<UIDocument>();
            testPanel = ScriptableObject.CreateInstance<PanelSettings>();
            testPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            testPanel.sortingOrder = 300;
            testPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");
            testDocument.panelSettings = testPanel;
            var root = testDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(.06f, .09f, .09f);
            InitializeBattleFont(root);
            root.style.fontSize = 23;
            testImage = new Image();
            testImage.style.position = Position.Absolute;
            testImage.style.left = 0;
            testImage.style.top = 0;
            testImage.style.right = 0;
            testImage.style.bottom = 0;
            testImage.scaleMode = ScaleMode.StretchToFill;
            root.Add(testImage);
            testImage.RegisterCallback<PointerDownEvent>(e =>
            {
                if (testOverlay != null || MissionDialogueActive || battle == null || battle.Outcome != Outcome.Running)
                    return;
                if (testPointers.Count == 0)
                {
                    testMultiGesture = false;
                    testDragDistance = 0;
                }
                else
                    testMultiGesture = true;
                testPointers[e.pointerId] = new Vector2(e.position.x, e.position.y);
                testImage.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            testImage.RegisterCallback<PointerMoveEvent>(e =>
            {
                Vector2 previous;
                if (!testPointers.TryGetValue(e.pointerId, out previous))
                    return;
                var now = new Vector2(e.position.x, e.position.y);
                testDragDistance += (now - previous).magnitude;
                if (testPointers.Count == 1)
                    pan += now - previous;
                else
                {
                    foreach (var p in testPointers)
                        if (p.Key != e.pointerId)
                        {
                            float old = (previous - p.Value).magnitude,
                                current = (now - p.Value).magnitude;
                            if (old > 5 && current > 5)
                                ZoomAt(
                                    (now + p.Value) * .5f - new Vector2(testImage.worldBound.x, testImage.worldBound.y),
                                    Mathf.Clamp(zoom * current / old, .65f, 3f)
                                );
                            break;
                        }
                }
                testPointers[e.pointerId] = now;
                renderedBattle = null;
                e.StopPropagation();
            });
            testImage.RegisterCallback<PointerUpEvent>(e =>
            {
                if (
                    testPointers.ContainsKey(e.pointerId)
                    && testPointers.Count == 1
                    && !testMultiGesture
                    && testDragDistance < 12
                    && testOverlay == null
                    && !MissionDialogueActive
                    && battle != null
                    && battle.Outcome == Outcome.Running
                )
                    SelectAt(
                        new Vector2(e.position.x - testImage.worldBound.x, e.position.y - testImage.worldBound.y),
                        true
                    );
                testPointers.Remove(e.pointerId);
                testImage.ReleasePointer(e.pointerId);
            });
            testImage.RegisterCallback<PointerCancelEvent>(e =>
            {
                testPointers.Remove(e.pointerId);
                testImage.ReleasePointer(e.pointerId);
            });
            testImage.RegisterCallback<WheelEvent>(e =>
            {
                if (testOverlay != null || MissionDialogueActive)
                    return;
                ZoomAt(
                    new Vector2(e.mousePosition.x - testImage.worldBound.x, e.mousePosition.y - testImage.worldBound.y),
                    Mathf.Clamp(zoom - e.delta.y * .05f, .65f, 3f)
                );
                renderedBattle = null;
                e.StopPropagation();
            });
            CreateNativeWorldOverlays(root);
            CreateNativeBattleControls(root);
        }
#endif

        void UpdateUnityTestHud()
        {
            if (screen != ScreenId.Battle || loading || battle == null)
                return;
            var safe = Screen.safeArea;
            float scale = Mathf.Min(safe.height / H, safe.width / 1400f);
            W = safe.width / scale;
            LayoutMap();
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (testHudObject == null)
                CreateUnityTestHud();
            testPanel.scale = scale;
            var root = testDocument.rootVisualElement;
            root.style.marginLeft = safe.x / testPanel.scale;
            root.style.marginRight = (Screen.width - safe.xMax) / testPanel.scale;
            root.style.marginTop = (Screen.height - safe.yMax) / testPanel.scale;
            root.style.marginBottom = safe.y / testPanel.scale;
            testImage.image = hdBattle == null ? null : hdBattle.Texture;
            UpdateNativeWorldOverlays();
            UpdateNativeBattleControls();
            testFpsFrames++;
            testFpsElapsed += Time.unscaledDeltaTime;
            if (testFpsElapsed >= .5f)
            {
                testLiveFps = testFpsFrames / testFpsElapsed;
                testFpsFrames = 0;
                testFpsElapsed = 0;
            }

            if (Time.unscaledTime >= testStatusAt)
            {
                testStatusAt = Time.unscaledTime + .25f;
                testStatus.text =
                    (
                        unityTestBattle
                            ? (
                                hdBattle != null && hdBattle.UnityCompatibilityDraw
                                    ? "UNITY · РЕЗЕРВ"
                                    : "UNITY · КАМЕРА"
                            )
                            : "ПРЕЖНИЙ РЕНДЕР"
                    )
                    + " · "
                    + (
                        testBattle ? testMapLabel
                        : campaignMode ? "СЮЖЕТ · " + mission
                        : IsRaid ? "РЕЙД · " + raid.wave
                        : "БОЙ · " + mission
                    )
                    + " · seed "
                    + battle.CombatSeed
                    + " · FPS "
                    + (testLiveFps > 0 ? testLiveFps.ToString("F1") : "…")
                    + " · кадр "
                    + (testLiveFps > 0 ? (1000f / testLiveFps).ToString("F1") : "…")
                    + " мс\n"
                    + (
                        unityTestError
                        ?? (
                            battle.Outcome == Outcome.Running
                                ? (paused ? "Пауза · " : "")
                                    + "время "
                                    + battle.Time.ToString("F1")
                                    + " · "
                                    + battle.Green.PriorityStatus
                                : "Бой завершён · " + battle.Outcome + (testBattle ? " · награды отключены" : "")
                        )
                    )
                    + (string.IsNullOrEmpty(message) ? "" : "\n" + message);
            }
#endif
        }

        void CancelUnityTestPointers()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (testImage != null)
                foreach (var id in testPointers.Keys.ToArray())
                    testImage.ReleasePointer(id);
            testPointers.Clear();
            testMultiGesture = false;
            testDragDistance = 0;
#endif
        }

        void HideUnityTestHud()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            CancelUnityTestPointers();
            CloseNativeDrawer();
            if (testHudObject != null)
                Destroy(testHudObject);
            if (testPanel != null)
                Destroy(testPanel);
            ReleaseBattleFont();
            ReleaseNativeWorldOverlays();
            if (raidClothIcon != null)
                Resources.UnloadAsset(raidClothIcon);
            if (raidPartsIcon != null)
                Resources.UnloadAsset(raidPartsIcon);
            raidClothIcon = raidPartsIcon = null;
            testHudObject = null;
            testPanel = null;
            nativeHudControls = null;
            nativeDrawer = "";
            nativeBindings.Clear();
            nativeSpeechLabels.Clear();
            nativeSpeechSizes.Clear();
            nativeSpeedButtons.Clear();
            nativeRaidCounts.Clear();
            nativeOrder = nativeOrderTitle = nativeCompletion = nativeRaid = null;
            nativeOrderRule = null;
            nativePause = null;
            nativePauseIcon = null;
            nativeZoom = null;
            nativeResultsShown = false;
            nativeDialogueLine = -1;
            testStatus = null;
            testImage = null;
            testDocument = null;

#endif
        }

        void ExitUnityTest()
        {
            HideUnityTestHud();
            ReleaseSceneGraphics();
            Rubezh.Graphics.ReleaseSceneSprites();
            battle = null;
            campBattle = null;
            campBusy = false;
            paused = false;
            screen = ScreenId.Camp;
            graphicsTier = testPreviousQuality;
            forceSoftware = testPreviousSoftware;
            fullVisibility = testPreviousVisibility;
            hardcoreFog = testPreviousFog;
            testBattle = unityTestBattle = false;
            unityTestError = null;
            diagDisableSmoke = diagDisableRoofs = diagDisableScenery = false;
            ShowCamp();
        }

        void ExportUnityTestReport()
        {
            TryExportUnityTestReport();
        }

        bool TryExportUnityTestReport()
        {
            if (battle == null)
                return false;
            try
            {
                string mode = campBattle == null ? "replay" : campBattle.mode;
                string context = testBattle
                    ? "UnityTest " + testMapLabel + " seed=" + testMapSeed
                    : mode + " mission=" + (campBattle == null ? mission : campBattle.mission);
                var report = AiBattleReport.Capture(battle, context);
                report.performance = CapturePerformance();
                string prefix =
                    benchExporting && benchPaired ? "Rubezh-RenderPair-" + benchPairId + "-phase" + benchPhase + "-"
                    : testBattle
                        ? (testMapSeed == 200020 ? "Rubezh-Test20-" : "Rubezh-TestForest-")
                            + (unityTestBattle ? "Unity" : "Reference")
                            + "-"
                    : "Rubezh-Battle-Performance-";
                ExportDeveloperText(JsonUtility.ToJson(report, false), prefix);
            }
            catch (Exception e)
            {
                message = "Ошибка отчёта: " + e.Message;
                return false;
            }
            return lastDeveloperExportSucceeded;
        }
    }
}
