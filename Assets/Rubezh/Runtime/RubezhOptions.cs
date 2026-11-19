using System;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        HdGpuRenderer hdBattle;
        bool hdGpuUnavailable;
        string firstHdGpuError;
        bool forceSoftware;
        bool diagDisableSmoke,
            diagDisableRoofs,
            diagDisableScenery;

        void GraphicsDiagnostics()
        {
            Pick(
                "Диагностика графики · влияет только на изображение",
                new[]
                {
                    "Дым: " + (diagDisableSmoke ? "выключен" : "включён"),
                    "Крыши: " + (diagDisableRoofs ? "выключены" : "включены"),
                    "Окружение: " + (diagDisableScenery ? "выключено" : "включено"),
                    "Включить все слои",
                },
                i =>
                {
                    if (i == 0)
                        diagDisableSmoke = !diagDisableSmoke;
                    if (i == 1)
                        diagDisableRoofs = !diagDisableRoofs;
                    if (i == 2)
                        diagDisableScenery = !diagDisableScenery;
                    if (i == 3)
                        diagDisableSmoke = diagDisableRoofs = diagDisableScenery = false;
                    renderedBattle = null;
                }
            );
        }

        int frameRateLimit = 60;
        static readonly int[] FrameRateChoices = { 30, 60, 90, 120 };
        int graphicsTier = 2; // -1 pixels, 0 low, 1 medium, 2 high
        bool soundEnabled = true;
        bool fullVisibility = true,
            hardcoreFog;
        AudioSource soundSource;
        AudioClip[] weaponSounds;
        AudioClip[] rifleSamples,
            machineGunSamples;
        AudioClip launcherSample,
            explosionSample,
            sniperSample,
            smokeSample;
        int lastMachineSample = -1;

        void InitOptions()
        {
            frameRateLimit = NormalizeFrameRate(PlayerPrefs.GetInt("rubezh.fps", 60));
            fullVisibility = PlayerPrefs.GetInt("rubezh.visibility", 1) != 0;
            hardcoreFog = PlayerPrefs.GetInt("rubezh.hardcoreFog", 0) != 0;
            graphicsTier = Mathf.Clamp(PlayerPrefs.GetInt("rubezh.graphics", 2), -1, 3);
            forceSoftware = false; // Legacy preference no longer selects a second renderer.
            soundEnabled = PlayerPrefs.GetInt("rubezh.sound", 1) != 0;
            soundSource = gameObject.AddComponent<AudioSource>();
            soundSource.playOnAwake = false;
            soundSource.spatialBlend = 0;
            ApplyFrameRate();
            InitMusic();
        }

        static int NormalizeFrameRate(int value) => Array.IndexOf(FrameRateChoices, value) >= 0 ? value : 60;

        void ApplyFrameRate()
        {
#if UNITY_5_3_OR_NEWER
            QualitySettings.vSyncCount = 0;
#endif
            Application.targetFrameRate = frameRateLimit;
            nextFrame = 0;
        }

        void SetFrameRate(int value)
        {
            frameRateLimit = NormalizeFrameRate(value);
            PlayerPrefs.SetInt("rubezh.fps", frameRateLimit);
            PlayerPrefs.Save();
            ApplyFrameRate();
        }

        void SetGraphics(int level)
        {
            graphicsTier = Mathf.Clamp(level, -1, 3);
            PlayerPrefs.SetInt("rubezh.graphics", graphicsTier);
            PlayerPrefs.Save();
            ApplyFrameRate();
            RebuildOutputTexture();
        }

        void SetSound(bool value)
        {
            soundEnabled = value;
            PlayerPrefs.SetInt("rubezh.sound", value ? 1 : 0);
            PlayerPrefs.Save();
            if (value && AudioBattle)
                LoadBattleSamples();
            if (!value && soundSource != null)
                soundSource.Stop();
        }

        NativeCampOptions ReadNativeCampOptions()
        {
            return new NativeCampOptions
            {
                quality = graphicsTier,
                fps = frameRateLimit,
                uiScale = Mathf.Clamp(PlayerPrefs.GetInt("rubezh.uiScale", 100), 50, 300),
                cpu = forceSoftware,
                sound = soundEnabled,
                musicOn = musicEnabled,
                effects = Mathf.RoundToInt(effectsVolume * 100),
                music = Mathf.RoundToInt(musicVolume * 100),
                visibility = fullVisibility,
                fog = hardcoreFog,
            };
        }

        void ApplyCampOption(string k, int v)
        {
            switch (k)
            {
                case "fps":
                    SetFrameRate(v);
                    return;
                case "uiScale":
                    PlayerPrefs.SetInt("rubezh.uiScale", Mathf.Clamp(v, 50, 300));
                    PlayerPrefs.Save();
                    return;
                case "quality":
                    SetGraphics(v);
                    return;
                case "cpu": // Ignore obsolete saved/external CPU-renderer commands.
                    return;
                case "sound":
                    SetSound(v != 0);
                    return;
                case "effects":
                    effectsVolume = Mathf.Clamp(v, 0, 100) / 100f;
                    break;
                case "music":
                    musicVolume = Mathf.Clamp(v, 0, 100) / 100f;
                    break;
                case "musicOn":
                    musicEnabled = v != 0;
                    if (musicA != null && musicB != null)
                    {
                        if (musicEnabled)
                        {
                            musicA.UnPause();
                            musicB.UnPause();
                        }
                        else
                        {
                            musicA.Pause();
                            musicB.Pause();
                        }
                    }

                    break;
                case "visibility":
                    fullVisibility = v != 0;
                    SaveVisibility();
                    return;
                case "fog":
                    hardcoreFog = v != 0;
                    SaveVisibility();
                    return;
                default:
                    return;
            }

            SaveAudio();
        }

        AudioClip Synthesize(int type)
        {
            const int rate = 22050;
            float duration =
                type == 5 ? .85f
                : type == 3 || type == 4 ? .48f
                : type == 2 ? .35f
                : .18f;
            int n = Mathf.CeilToInt(duration * rate);
            float[] samples = new float[n];
            uint rng = (uint)(0xA19B + type * 341);
            float pitch =
                type == 0 ? 150
                : type == 1 ? 115
                : type == 2 ? 215
                : type == 3 ? 92
                : type == 4 ? 120
                : 62;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate,
                    u = t / duration;
                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                float noise = ((rng & 65535) / 32768f - 1f);
                float env = (1f - u) * (1f - u) * Mathf.Min(1f, t * 2500f);
                float rumble = Mathf.Sin(6.2831853f * (pitch * t - 45f * t * t));
                samples[i] = Mathf.Clamp((noise * .48f + rumble * .52f) * env * .55f, -1f, 1f);
            }

            var clip = AudioClip.Create("Rubezh-" + type, n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        void LoadBattleSamples()
        {
            if (!soundEnabled || weaponSounds != null)
                return;
            weaponSounds = new AudioClip[6];
            for (int i = 0; i < weaponSounds.Length; i++)
                weaponSounds[i] = Synthesize(i);
            rifleSamples = new[] { Resources.Load<AudioClip>("Audio/rifle01") };
            sniperSample = Resources.Load<AudioClip>("Audio/sniper");
            smokeSample = Resources.Load<AudioClip>("Audio/smoke");
            machineGunSamples = new AudioClip[8];
            for (int i = 0; i < 8; i++)
                machineGunSamples[i] = Resources.Load<AudioClip>("Audio/machinegun" + (i + 1).ToString("00"));
            launcherSample = Resources.Load<AudioClip>("Audio/launcher");
            explosionSample = Resources.Load<AudioClip>("Audio/explosion");
        }

        void ReleaseBattleSamples()
        {
            if (soundSource != null)
            {
                soundSource.Stop();
                soundSource.clip = null;
            }
            foreach (var voice in smokeVoices.Values)
            {
                voice.Stop();
                voice.clip = null;
                Destroy(voice);
            }
            smokeVoices.Clear();
            expiredSmokeVoices.Clear();
            if (weaponSounds != null)
                foreach (var clip in weaponSounds)
                    if (clip != null)
                        Destroy(clip);
            weaponSounds = null;
            if (rifleSamples != null)
                foreach (var clip in rifleSamples)
                    if (clip != null)
                        Resources.UnloadAsset(clip);
            if (machineGunSamples != null)
                foreach (var clip in machineGunSamples)
                    if (clip != null)
                        Resources.UnloadAsset(clip);
            foreach (var clip in new[] { sniperSample, smokeSample, launcherSample, explosionSample })
                if (clip != null)
                    Resources.UnloadAsset(clip);
            rifleSamples = machineGunSamples = null;
            sniperSample = smokeSample = launcherSample = explosionSample = null;
        }

        void Sound(int category)
        {
            if (category == 7)
            {
                PlayStoryMortarImpact();
                return;
            }
            if (
                !soundEnabled
                || soundSource == null
                || weaponSounds == null
                || category < 0
                || category >= weaponSounds.Length
            )
                return;
            AudioClip clip = weaponSounds[category];
            if (category == 0 && rifleSamples != null && rifleSamples[0] != null)
                clip = rifleSamples[0];
            if (category == 1 && machineGunSamples != null)
            {
                int next = audioRandom.Next(machineGunSamples.Length - 1);
                if (next >= lastMachineSample && lastMachineSample >= 0)
                    next++;
                lastMachineSample = next;
                if (machineGunSamples[next] != null)
                    clip = machineGunSamples[next];
            }

            if ((category == 3 || category == 4) && launcherSample != null)
                clip = launcherSample;
            if (category == 2 && sniperSample != null)
                clip = sniperSample;
            if (category == 5 && explosionSample != null)
                clip = explosionSample;
            soundSource.PlayOneShot(clip, .50f);
        }

        void UpdateBattleSound()
        {
            if (battle == null)
                return;
            while (battle.AudioEvents.Pending.Count > 0)
            {
                var e = battle.AudioEvents.Pending.Dequeue();
                Sound(e.Category);
            }
        }

        void RebuildOutputTexture()
        {
            renderedBattle = null;
            hdGpuUnavailable = false;
            if (hdBattle != null)
            {
                hdBattle.Dispose();
                hdBattle = null;
            }

            if (texture != null)
                Destroy(texture);
            texture = null;
        }

        bool HasStudioArt
        {
            get
            {
                return battle != null
                    && battle.Map.Studio != null
                    && (battle.Map.Studio.Objects.Count > 0 || Array.Exists(battle.Map.Studio.Ground, a => a != null));
            }
        }

        bool HasRoof => battle != null && battle.Map.Roof != null && System.Array.Exists(battle.Map.Roof, v => v);

        float BattleRasterScale => 1f;

        float MapWidth => BattleVisualRecipes.LogicalWidth(battle == null ? null : battle.Map);

        float MapHeight => BattleVisualRecipes.LogicalHeight(battle == null ? null : battle.Map);

        SilentOrders.Core.BattleSimulation presentationSnapBattle;
        int presentationSnapTick = -1;

        void UpdatePresentationInterpolation()
        {
            if (painter == null || battle == null)
                return;
            painter.ObserveTick(battle);
            bool stopped =
                paused || battle.Outcome != SilentOrders.Core.Outcome.Running || (!testBattle && MissionDialogueActive);
            if (stopped)
            {
                presentationSnapBattle = battle;
                presentationSnapTick = battle.TickNumber;
            }
            bool holdEndpoint = presentationSnapBattle == battle && presentationSnapTick == battle.TickNumber;
            painter.SetInterpolation(
                stopped || holdEndpoint || battle.Config.Tick <= 0
                    ? 1f
                    : Mathf.Clamp01(accumulator / battle.Config.Tick)
            );
        }

        void RenderOutput()
        {
            var phase = System.Diagnostics.Stopwatch.StartNew();
            UpdatePresentationInterpolation();
            diagInterpolationMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            // A dimensions-only canvas remains for projection metadata. Native rendering
            // never requests its Pixels or invokes the legacy software compositor.
            if (painter.PrepareCanvas(battle.Map, 1f))
                RebuildOutputTexture();
            diagCanvasMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            ApplyVisibility();
            diagVisibilityMs = phase.Elapsed.TotalMilliseconds;
            phase.Restart();
            try
            {
                if (hdBattle == null)
                    hdBattle = new HdGpuRenderer(painter.Canvas, true, graphicsTier == -1, true);
                LayoutMap();
                var safe = Screen.safeArea;
                float ui = UIScale;
                float vw = Mathf.Max(1, safe.width / ui),
                    vh = Mathf.Max(1, safe.height / ui);
                var view = new Rect(
                    -mapRect.x / mapRect.width,
                    -mapRect.y / mapRect.height,
                    vw / mapRect.width,
                    vh / mapRect.height
                );
                float limit = SystemInfo.maxTextureSize;
#if UNITY_ANDROID && !UNITY_EDITOR
                int mobileLimit = PlayerPrefs.GetInt("rubezh.hdRenderLimit", 1920);
                if (mobileLimit > 0)
                    limit = Mathf.Min(limit, Mathf.Max(960, mobileLimit));
#endif
                float quality =
                    graphicsTier <= 0 ? .5f
                    : graphicsTier == 1 ? .75f
                    : 1f;
                float fit = Mathf.Min(quality, limit / Mathf.Max(1, Mathf.Max(safe.width, safe.height)));
                int width = Mathf.Max(1, Mathf.RoundToInt(safe.width * fit)),
                    height = Mathf.Max(1, Mathf.RoundToInt(safe.height * fit));
                hdBattle.DisableSmoke = diagDisableSmoke;
                hdBattle.DisableRoofs = diagDisableRoofs;
                hdBattle.DisableScenery = diagDisableScenery;
                diagLayoutMs = phase.Elapsed.TotalMilliseconds;
                phase.Restart();
                hdBattle.Render(painter, battle, debug, selected, width, height, view);
                diagNativeMs = phase.Elapsed.TotalMilliseconds;
                phase.Restart();
                if (!fullVisibility)
                    hdBattle.RenderForeground(painter, battle, selected); // Native camera/layer pass, no actor recomposition.
                diagForegroundMs = phase.Elapsed.TotalMilliseconds;
            }
            catch (Exception error)
            {
                if (string.IsNullOrEmpty(firstHdGpuError))
                    firstHdGpuError = error.ToString();
                if (benchActive)
                    FinishFixedBenchmark(false);
                unityTestError = error.Message;
                paused = true;
                Debug.LogException(error);
            }
        }
    }
}
