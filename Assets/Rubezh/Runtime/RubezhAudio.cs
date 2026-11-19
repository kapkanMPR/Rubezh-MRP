using System;
using System.Collections;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        float effectsVolume = .8f,
            musicVolume = .4f,
            musicBlend = 1f;
        bool musicEnabled = true,
            audioLoading;
        AudioSource musicA,
            musicB,
            engineSource;
        AudioClip engineClip;
        string audioContext = "";
        int musicTrack = -1,
            settingsReturn;
        bool settingsPaused;
        readonly List<int> musicQueue = new List<int>();
        readonly System.Random audioRandom = new System.Random();
        bool AudioBattle
        {
            get
            {
                return battle != null
                    && (screen == ScreenId.Battle || screen == ScreenId.Settings && settingsReturn == ScreenId.Battle);
            }
        }

        void InitMusic()
        {
            effectsVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("rubezh.effectsVolume", .8f));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("rubezh.musicVolume", .4f));
            musicEnabled = PlayerPrefs.GetInt("rubezh.musicEnabled", 1) != 0;
            if (FindFirstObjectByType<AudioListener>() == null)
                gameObject.AddComponent<AudioListener>();
            musicA = NewAudio();
            musicB = NewAudio();
            engineSource = NewAudio();
            const int rate = 22050;
            var data = new float[rate * 2];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                data[i] =
                    (
                        .24f * Mathf.Sin(t * 2 * Mathf.PI * 44)
                        + .12f * Mathf.Sin(t * 2 * Mathf.PI * 88)
                        + .06f * Mathf.Sin(t * 2 * Mathf.PI * 132)
                    ) * (.85f + .15f * Mathf.Sin(t * 2 * Mathf.PI * 11));
            }

            engineClip = AudioClip.Create("VehicleEngine", data.Length, 1, rate, false);
            engineClip.SetData(data, 0);
            engineSource.clip = engineClip;
            engineSource.loop = true;
            engineSource.volume = 0;
        }

        AudioSource NewAudio()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0;
            return s;
        }

        void SaveAudio()
        {
            PlayerPrefs.SetFloat("rubezh.effectsVolume", effectsVolume);
            PlayerPrefs.SetFloat("rubezh.musicVolume", musicVolume);
            PlayerPrefs.SetInt("rubezh.musicEnabled", musicEnabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        int NextMusic()
        {
            if (musicQueue.Count == 0)
            {
                for (int i = 1; i <= 8; i++)
                    musicQueue.Add(i);
                for (int i = 7; i > 0; i--)
                {
                    int j = audioRandom.Next(i + 1);
                    int t = musicQueue[i];
                    musicQueue[i] = musicQueue[j];
                    musicQueue[j] = t;
                }

                if (musicQueue[7] == musicTrack)
                {
                    int t = musicQueue[7];
                    musicQueue[7] = musicQueue[0];
                    musicQueue[0] = t;
                }
            }

            int next = musicQueue[musicQueue.Count - 1];
            musicQueue.RemoveAt(musicQueue.Count - 1);
            return next;
        }

        IEnumerator LoadMusic(int track, string context)
        {
            audioLoading = true;
            var request = Resources.LoadAsync<AudioClip>("Music/track" + track.ToString("00"));
            yield return request;
            var clip = request.asset as AudioClip;
            audioLoading = false;
            if (clip == null)
            {
                Debug.LogWarning("Music missing: " + track);
                audioContext = context;
                yield break;
            }

            var old = musicB.clip;
            musicB.Stop();
            musicB.clip = null;
            if (old != null && old != musicA.clip && old != clip)
                Resources.UnloadAsset(old);
            var swap = musicB;
            musicB = musicA;
            musicA = swap;
            musicA.clip = clip;
            musicA.volume = 0;
            musicA.Play();
            if (!musicEnabled)
                musicA.Pause();
            musicBlend = 0;
            musicTrack = track;
            audioContext = context;
        }

        void UpdateAudio()
        {
            UpdateSmokeAudio();
            if (musicA == null)
                return;
            bool inBattle = AudioBattle;
            string wanted = inBattle && battle.CampaignLevel != 20 ? "battle" : "theme";
            if (
                musicEnabled
                && !loading
                && !audioLoading
                && (
                    audioContext != wanted
                    || musicA.clip != null
                        && musicEnabled
                        && (!musicA.isPlaying || musicA.clip.length - musicA.time < .8f)
                )
            )
                StartCoroutine(LoadMusic(wanted == "theme" ? 0 : NextMusic(), wanted));
            musicBlend = Mathf.MoveTowards(musicBlend, 1, Time.unscaledDeltaTime / 1.2f);
            float gain = musicVolume * (inBattle ? .65f : .42f);
            musicA.volume = musicEnabled ? gain * musicBlend : 0;
            musicB.volume = musicEnabled ? gain * (1 - musicBlend) : 0;
            if (musicBlend >= 1 && musicB.clip != null)
            {
                var old = musicB.clip;
                musicB.Stop();
                musicB.clip = null;
                if (old != musicA.clip)
                    Resources.UnloadAsset(old);
            }
            soundSource.volume = effectsVolume;
            bool moving =
                screen == ScreenId.Battle
                && battle != null
                && !paused
                && battle.Outcome == Outcome.Running
                && (
                    (battle.GreenVehicle != null && battle.GreenVehicle.Active && !battle.GreenVehicle.Stopped)
                    || (battle.RedVehicle != null && battle.RedVehicle.Active && !battle.RedVehicle.Stopped)
                );
            engineSource.volume = Mathf.MoveTowards(
                engineSource.volume,
                soundEnabled && moving ? effectsVolume * .32f : 0,
                Time.unscaledDeltaTime * 2
            );
            if (moving && soundEnabled && !engineSource.isPlaying)
                engineSource.Play();
            else if (engineSource.volume <= 0 && engineSource.isPlaying)
                engineSource.Stop();
        }

        bool KeepBattleView =>
            screen == ScreenId.Battle || (screen == ScreenId.Settings && settingsReturn == ScreenId.Battle);

        void OpenSettings()
        {
            settingsReturn = screen;
            settingsPaused = paused;
            if (screen == ScreenId.Battle)
                paused = true;
            CloseDrawer();
            screen = ScreenId.Settings;
            message = "";
        }

        void CloseSettings()
        {
            SaveAudio();
            CancelTouchInput();
            if (settingsReturn == ScreenId.Battle)
            {
                if (battle == null)
                {
                    screen = ScreenId.Camp;
                    return;
                }

                paused = settingsPaused;
                if (sceneArtBattle != battle || painter == null)
                {
                    ResumeBattle();
                    return;
                }

                renderedBattle = null;
                nextFrame = 0;
            }

            screen = settingsReturn;
        }

        float VolumeSlider(float x, float y, float width, float value)
        {
            // Touch-aware slider, matching the coordinate system of the rest of the HUD.
            Rect r = new Rect(x, y, width, 48);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && r.Contains(e.mousePosition))
            {
                value = Mathf.Clamp01((e.mousePosition.x - x) / width);
                e.Use();
            }

            Fill(new Rect(x, y + 20, width, 8), panel);
            Fill(new Rect(x, y + 20, width * value, 8), accent);
            Fill(new Rect(x + width * value - 7, y + 8, 14, 32), ink);
            return value;
        }

        void AudioControls(float x, float y, float width)
        {
            Txt(x, y, width, 36, "Эффекты · " + Mathf.RoundToInt(effectsVolume * 100) + "%", small);
            effectsVolume = VolumeSlider(x, y + 35, width, effectsVolume);
            Txt(x, y + 95, width - 220, 36, "Музыка · " + Mathf.RoundToInt(musicVolume * 100) + "%", small);
            if (B(x + width - 210, y + 85, 210, 55, musicEnabled ? "Музыка: вкл" : "Музыка: выкл", compactButton))
            {
                musicEnabled = !musicEnabled;
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

                SaveAudio();
            }

            musicVolume = VolumeSlider(x, y + 135, width, musicVolume);
        }

        void Gear(float x, float y)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4;
                Fill(new Rect(x + 12 + Mathf.Cos(a) * 11, y + 12 + Mathf.Sin(a) * 11, 6, 6), ink);
            }

            Fill(new Rect(x + 6, y + 6, 18, 18), ink);
            Fill(new Rect(x + 11, y + 11, 8, 8), panel);
        }
    }
}
