using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public static class RubezhVersion
    {
        public const string Current = "AI-0.12.223";
        public const int AndroidCode = 324;
    }

    public sealed partial class RubezhApp
    {
        volatile bool loadingDisposed;
        bool loading = true;
        float loadingTarget,
            loadingShown,
            atlasFraction;
        string loadingStatus = "Подготовка операции…",
            loadingError;
        TextAsset loadingAtlasAsset;

        void ReleaseLoadingAtlas()
        {
            HideNativeLoading();
            if (loadingAtlasAsset != null)
            {
                Resources.UnloadAsset(loadingAtlasAsset);
                loadingAtlasAsset = null;
            }
        }

        float runnerClock;

        void StartLoading()
        {
            // Retry always returns to the camp. Do not retain a half-created battle or
            // an old wave after a failed native map/atlas load; persisted raid rewards
            // and the saved session are reconciled by the camp on entry.
            ResetFailedBattleLoad();
            CancelTouchInput();
            PrepareFireLoading();
            loadingDisposed = false;
            loading = true;
            loadingTarget = loadingShown = atlasFraction = runnerClock = 0;
            loadingError = null;
            loadingStatus = "Подготовка операции…";
            StartCoroutine(GuardLoading(LoadGame()));
        }

        void ResetFailedBattleLoad()
        {
            ReleaseSceneGraphics();
            Rubezh.Graphics.ReleaseSceneSprites();
            if (testBattle)
            {
                graphicsTier = testPreviousQuality;
                forceSoftware = testPreviousSoftware;
                fullVisibility = testPreviousVisibility;
                hardcoreFog = testPreviousFog;
            }
            battle = null;
            campBattle = null;
            raid = null;
            raidChanging = false;
            campBusy = false;
            testBattle = unityTestBattle = false;
            unityTestError = null;
            interestingSession = null;
            paused = false;
            awarded = false;
            accumulator = 0;
            pickOptions = null;
            pickApply = null;
            CloseDrawer();
        }

        IEnumerator GuardLoading(IEnumerator work)
        {
            try
            {
                while (true)
                {
                    object current = null;
                    bool more;
                    try
                    {
                        more = work.MoveNext();
                        if (more)
                            current = work.Current;
                    }
                    catch (Exception ex)
                    {
                        campBusy = false;
                        raidChanging = false;
                        ReleaseSceneGraphics();
                        Rubezh.Graphics.ReleaseSceneSprites();
                        loadingError = "Не удалось подготовить игру. " + ex.Message;
                        loadingStatus = "Ошибка загрузки";
                        Debug.LogError("Загрузка: " + loadingStatus);
                        Debug.LogException(ex);
                        yield break;
                    }

                    if (!more)
                        yield break;
                    yield return current;
                }
            }
            finally
            {
                (work as IDisposable)?.Dispose();
            }
        }

        IEnumerator LoadGame()
        {
            // Give the startup screen a rendered frame before loading the large atlas.
            yield return null;
#if UNITY_WEBGL && !UNITY_EDITOR
            progress = Load();
            draft = JsonUtility.FromJson<Progress>(JsonUtility.ToJson(progress));
#endif
            loadingTarget = .15f;
            loadingStatus = "Загрузка графики…";
            yield return null;
            Rubezh.Graphics.ConfigurePacks();
            Rubezh.Graphics.ReleaseSceneSprites();
            // Native camp has its own imported images; battle packs load on entry to battle.
            loadingTarget = .85f;
            loadingStatus = "Подготовка лагеря…";
            loadingTarget = .92f;
            yield return null;
            loadingStatus = "Подготовка интерфейса…";
            yield return null;
            painter = null;
            RebuildOutputTexture();
            loadingTarget = 1;
            loadingStatus = "Готово";
            // Smooth the visual indicator to the completed real work; do not open an unready menu.
            while (loadingShown < 1f)
                yield return null;
            var fade = FinishFireLoading();
            while (fade.MoveNext())
                yield return fade.Current;
            loading = false;
            screen = ScreenId.Camp;
            ShowCamp();
        }

        void TickLoading()
        {
            loadingShown = Mathf.MoveTowards(loadingShown, Mathf.Clamp01(loadingTarget), Time.unscaledDeltaTime * 1.3f);
            if (loadingError == null && loadingShown < 1f)
                runnerClock += Time.unscaledDeltaTime;
            UpdateNativeLoading();
        }
    }
}
