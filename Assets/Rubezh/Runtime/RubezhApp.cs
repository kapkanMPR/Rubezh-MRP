using System;
using System.IO;
using System.Linq;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{


    public sealed partial class RubezhApp : MonoBehaviour
    {
        BattleSimulation battle;
        NativeBattleComposer painter;
        Texture2D texture;
        byte[] pixels;
        Progress progress, draft;
        Kit[] quick = Progress.FreshKits();
        FormationSettings quickFormation = new FormationSettings();
        bool quickFormationLoaded;
        int mission = 1, selected, kitSelected, screen, style = 2, maneuver; // 0 native camp, 2 battle, 5 settings, 6 formation, 7 developer scenarios
        bool campaignMode = true, paused, debug, awarded;
        float accumulator, speed = 1, nextFrame;
        Vector2 menuScroll, detailScroll;
        string message = "";
        GUIStyle title, body, small, button;
        Rect mapRect;
        float W = 1600;
        const float H = 1000;
        string SavePath
        {
            get
            {
                return Path.Combine(Application.persistentDataPath, DeveloperBuild.WebPreview ? "rubezh-web-progress.json" : DeveloperBuild.Enabled ? "rubezh-developer-progress.json" : "rubezh-unity-progress.json");
            }
        }

        Kit[] Kits
        {
            get
            {
                return campaignMode ? draft.kits : quick;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<RubezhApp>() == null)
                new GameObject("Рубеж").AddComponent<RubezhApp>();
        }

        void Awake()
        {
#if UNITY_5_3_OR_NEWER
            Application.lowMemory += DiagnosticLowMemory;
            Application.logMessageReceivedThreaded += DiagnosticLog;
#endif
            // Frame pacing is initialized from saved options below.
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(transform);
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.063f, .106f, .122f);
            InitOptions();
            speechMode = (SpeechMode)Mathf.Clamp(PlayerPrefs.GetInt("rubezh.speechMode", (int)SpeechMode.Full), 0, 3);
            StartLoading();
        }

        Progress Load()
        {
            foreach (string path in new[]
            {
                SavePath,
                SavePath + ".bak"
            }

            )
                try
                {
                    if (File.Exists(path))
                    {
                        var p = JsonUtility.FromJson<Progress>(File.ReadAllText(path));
                        if (p != null)
                        {
                            p.MigratePockets();
                            p.MigrateTraining();
                        }

                        if (p != null && p.StructurallyValid())
                        {
                            if (!p.WithinBudget)
                                message = "Старый комплект превышает текущий бюджет. Прогресс и снаряжение сохранены.";
                            return p;
                        }

                        message = "Сохранение повреждено: " + Path.GetFileName(path);
                    }
                }
                catch (Exception ex)
                {
                    message = "Не удалось загрузить прогресс: " + ex.Message;
                }

            return new Progress();
        }

        bool Save()
        {
            try
            {
                if (!progress.StructurallyValid())
                {
                    message = "Некорректный прогресс. Сохранение не перезаписано.";
                    return false;
                }

                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(SavePath + ".tmp", JsonUtility.ToJson(progress, true));
                if (File.Exists(SavePath))
                    File.Copy(SavePath, SavePath + ".bak", true);
                File.Copy(SavePath + ".tmp", SavePath, true);
                File.Delete(SavePath + ".tmp");
                WebPersistence.Flush();
                return true;
            }
            catch (Exception ex)
            {
                message = "Ошибка сохранения: " + ex.Message;
                return false;
            }
        }

        // Dev-only preparation drafts. Never use battle inventory (ammo, drops, casualties) as a seed.
        [Serializable]
        sealed class DevPreparation
        {
            public Kit[] kits;
            public int[] training;
        }

        Progress legacyDev;
        bool legacyRead;
        int devDraftMission;
        string devLastJson;
        bool devInput;
        static string DevDraftKey(int m)
        {
            return "rubezh.dev.preparation.v1." + m;
        }

        static DevPreparation ReadDevDraft(int m)
        {
            string key = DevDraftKey(m);
            if (!PlayerPrefs.HasKey(key))
                return null;
            try
            {
                var a = JsonUtility.FromJson<DevPreparation>(PlayerPrefs.GetString(key));
                if (a != null && a.kits != null && a.kits.Length == 5 && a.kits.All(k => k != null && k.bag != null && k.bag.Length == 12 && k.weapon >= 0 && k.weapon <= 4 && k.extra >= 0 && k.extra <= 4 && k.rig >= 0 && k.rig <= 2 && k.armor >= 0 && k.armor <= 2 && k.grenade >= 0 && k.grenade <= 5 && k.grenade2 >= 0 && k.grenade2 <= 5 && k.grenade3 >= 0 && k.grenade3 <= 5) && a.training != null && a.training.Length == Progress.TrainingCount && a.training.All(t => t >= 0 && t <= 3))
                    return a;
            }
            catch (ArgumentException)
            {
            }

            return null;
        }

        void StoreDevDraft()
        {
            if (devDraftMission == 0 || draft == null || !DeveloperBuild.Enabled)
                return;
            var json = JsonUtility.ToJson(new DevPreparation { kits = draft.kits, training = draft.training });
            if (json == devLastJson)
                return;
            PlayerPrefs.SetString(DevDraftKey(devDraftMission), json);
            PlayerPrefs.Save();
            devLastJson = json;
        }

        Progress DevPreparationFor(int target)
        {
            if (!legacyRead)
            {
                legacyDev = progress;
                legacyRead = true;
            }

            var basis = DeveloperBuild.ForMission(target);
            for (int n = target; n >= 1; n--)
            {
                var saved = ReadDevDraft(n);
                // Adopt an existing pre-patch Dev save only at its own level or above.
                if (saved == null && legacyDev != null && legacyDev.budgetMission == n)
                    saved = new DevPreparation
                    {
                        kits = legacyDev.kits,
                        training = legacyDev.training
                    };
                if (saved != null)
                {
                    basis.kits = saved.kits;
                    basis.training = saved.training;
                    break;
                }
            }

            return basis;
        }

        static int lastCombatSeed;
        internal static int NewCombatSeed()
        {
            int value = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0) & int.MaxValue;
            if (value == lastCombatSeed)
                value = value == int.MaxValue ? 1 : value + 1;
            lastCombatSeed = value;
            return value;
        }

        readonly System.Collections.Generic.Dictionary<string, Rubezh.GreenLook> legacyLooks = new System.Collections.Generic.Dictionary<string, Rubezh.GreenLook>();
        Rubezh.GreenLook LegacyLook(int slot, Kit kit)
        {
            string key = CampKey + "look57." + slot;
            Rubezh.GreenLook l;
            if (!legacyLooks.TryGetValue(key, out l))
            {
                l = PlayerPrefs.HasKey(key) ? JsonUtility.FromJson<Rubezh.GreenLook>(PlayerPrefs.GetString(key)) : null;
                if (l == null)
                    l = new Rubezh.GreenLook();
                legacyLooks[key] = l;
            }

            int h = l.headId, a = l.lightId, f = l.familyId;
            l.Ensure(kit.Class(slot), slot == 0);
            if (h != l.headId || a != l.lightId || f != l.familyId)
            {
                PlayerPrefs.SetString(key, JsonUtility.ToJson(l));
                PlayerPrefs.Save();
            }

            return l;
        }

        void CompleteBattleStart(BattleSimulation candidate)
        {
            candidate.PrepareDeployment();
            newlyUnlocked = new EquipmentUnlock[0];
            unlockScroll = Vector2.zero;
            CancelTouchInput();
            ReleaseBattlePresentation();
            battle = candidate;
            interestingSession = null;
            battle.AudioEvents.Enabled = true;
            screen = 2;
            paused = false;
            awarded = false;
            accumulator = 0;
            selected = 0;
            drawer = "";
            zoom = 1;
            pan = Vector2.zero;
            style = 2;
            maneuver = 0;
            message = "";
            nextFrame = 0;
            renderedBattle = null;
            BeginBattleDiagnostics();
            BeginMissionDialogue();
        }

        double fullUpdateCpuMs,guiCpuMs;
        void Update()
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            try {
            guiCpuMs=0;
            UpdateAudio();
            if(TickHybridRange())return;
            UpdateStoryMortarAudio();
            TrimPortraits();
            long uiStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateNativeUtilityMenus();
            UpdateNativeFormationMenu();
            MaintainFixedBenchmark();
            guiCpuMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-uiStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            UpdatePerformance();
            UpdateDiagnostics();
            if (battle == null)
                importantCommands.Clear();
            if (loading)
            {
                uiStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                TickLoading();
                guiCpuMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-uiStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                return;
            }

            if (!KeepBattleView && sceneArtBattle != null)
            {
                ReleaseSceneGraphics();
                Rubezh.Graphics.ReleaseSceneSprites();
            }

            if (battle == null || screen != 2)
                return;
            if(!testBattle&&!benchActive)UpdateMissionDialogue();
            importantCommands.Observe(battle);
            if (!paused && speechMode != SpeechMode.Off)
                importantCommands.Tick(Mathf.Min(Time.unscaledDeltaTime, .2f));
            if (!benchActive && (testBattle || !MissionDialogueActive) && !paused && battle.Outcome == Outcome.Running)
                AdvanceSimulation(Time.unscaledDeltaTime);
            UpdateBattleSound();
            if(!benchActive)ProcessBattleCompletion();
            if(battle==null||screen!=2||loading)return;
            UpdatePresentationInterpolation();
            uiStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            UpdateUnityTestHud();
            guiCpuMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-uiStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            if(battle==null||screen!=2||loading)return;
            if (Time.unscaledTime >= nextFrame && NeedsBattleRender())
            {
                Render();
                nextFrame = Time.unscaledTime; // Application.targetFrameRate owns pacing for every quality tier.
            }
            } finally {fullUpdateCpuMs=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;}
        }

        // Bound catch-up work after a long frame. Never skip simulation ticks: under
        // sustained overload game time advances more slowly instead of freezing the UI.
        void AdvanceSimulation(float wallDelta)
        {
            if(benchActive)return;
            accumulator = Mathf.Min(.25f, accumulator + Mathf.Clamp(wallDelta, 0, .2f) * speed);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            int n = 0;
            while (accumulator >= battle.Config.Tick && n++ < 24 && battle.Outcome == Outcome.Running)
            {
                StepMeasured();
                UpdateBattleSound();
                accumulator -= battle.Config.Tick;
                if ((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= 8)
                    break;
            }
        }

        void Render()
        {
            RenderMeasured();
        }

        void Export()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "battle-log.txt");
                File.WriteAllLines(path, battle.Events.Select(e => e.Time.ToString("F1") + " " + e.Side + " " + e.Text));
                message = "Журнал сохранён: " + path;
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }
        }

        void ReleaseBattlePresentation()
        {
            renderedBattle = null;
            ambushSpeechVehicle = null;
        }

        void OnApplicationPause(bool pause)
        {
            SuspendHybridRange(pause);
            if (pause) CancelUnityTestPointers();
            storyAppPaused = pause;
            renderedBattle = null;
            CancelTouchInput();
            if (pause)
            {
                try { SaveInterruptedRaid(); } catch(Exception e) { Debug.LogException(e); }
                StoreDevDraft();
                paused = true;
                SaveAudio();
            }
        }

        void OnApplicationFocus(bool focus)
        {
            SuspendHybridRange(!focus,true);
            if (!focus)
            {
                CancelTouchInput();
                CancelUnityTestPointers();
            }
            else
            {
                renderedBattle = null;
                nextFrame = 0;
            }
        }

        void OnDestroy()
        {
            DisposeHybridRange();
            FinishFixedBenchmark(false);
            HideNativeUtilityMenus();
            DisposeNativeFormationMenu();
#if UNITY_5_3_OR_NEWER
            Application.lowMemory -= DiagnosticLowMemory;
            Application.logMessageReceivedThreaded -= DiagnosticLog;
#endif
            BuildingRecipe.ClearMaterials();
            importantCommands.Clear();
            if (portraits != null)
                portraits.Clear();
            StoreDevDraft();
            loadingDisposed = true;
            ReleaseSceneGraphics();
            ReleaseLoadingAtlas();
            ResetStoryMortars();
            ReleaseBattleSamples();
            ReleaseImportedPortraits();
            BuiltinCampaign.ReleaseCached();
            Rubezh.Graphics.Release();
            SaveAudio();
            if (engineClip != null)
                Destroy(engineClip);
            foreach (var t in uiTextures)
                if (t != null)
                    Destroy(t);

        }
    }
}
