using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        AudioClip storyLaunch, storyIncoming, storyImpact;
        readonly AudioSource[] storyLaunchVoices = new AudioSource[3];
        readonly AudioSource[] storyIncomingVoices = new AudioSource[MissionMortarSupport.ShellCount];
        readonly bool[] storyIncomingStarted = new bool[MissionMortarSupport.ShellCount];
        AudioSource storyImpactVoice;
        bool storySalvoActive;
        float storySalvoElapsed;
        int storyLaunchCount;
        float storyCleanupAt = -1;
        bool storyAppPaused;

        AudioSource NewStoryVoice()
        {
            var voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0;
            voice.loop = false;
            return voice;
        }

        void LoadStoryMortarAudio()
        {
            storyLaunch = Resources.Load<AudioClip>("Audio/story-mortar-launch");
            storyIncoming = Resources.Load<AudioClip>("Audio/story-mortar-incoming");
            storyImpact = Resources.Load<AudioClip>("Audio/story-mortar-impact");
            for (int i = 0; i < 3; i++)
            {
                storyLaunchVoices[i] = NewStoryVoice();
            }
            for (int i = 0; i < storyIncomingVoices.Length; i++)
            {
                storyIncomingVoices[i] = NewStoryVoice();
            }
            storyImpactVoice = NewStoryVoice();
        }

        void TickDialogueSalvo()
        {
            if (storyAppPaused) return;
            storySalvoActive = true;
            // Runs while combat is paused; no accelerated time and no loading-screen time.
            if (storyLaunchCount < 3 && storySalvoElapsed >= storyLaunchCount)
            {
                var voice = storyLaunchVoices[storyLaunchCount++];
                if (soundEnabled && storyLaunch != null && voice != null)
                {
                    voice.clip = storyLaunch;
                    voice.volume = .55f;
                    voice.Play();
                }
            }
            storySalvoElapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (storyLaunchCount == 3 && storySalvoElapsed >= 2 + (storyLaunch != null ? storyLaunch.length : 2.15f))
            {
                battle.StoryMortars = new MissionMortarSupport(battle, storyIncoming != null ? storyIncoming.length : 1.675f);
                storySalvoActive = false;
                FinishMissionDialogue();
            }
        }

        void UpdateStoryMortarAudio()
        {
            if (storyImpactVoice == null) return;
            bool live = !loading && screen == 2 && battle != null && !storyAppPaused;
            if (!loading && screen != 2 && !MissionDialogueActive) { ResetStoryMortars(); return; }
            bool anyIncoming = false;
            foreach (var voice in storyLaunchVoices)
                if (voice != null) voice.mute = !soundEnabled || !live;
            storyImpactVoice.mute = !soundEnabled || !live;
            if (!live || paused) storyImpactVoice.Pause();
            else storyImpactVoice.UnPause();
            bool allStarted = true;
            for (int i = 0; i < storyIncomingVoices.Length; i++)
            {
                allStarted &= storyIncomingStarted[i];
                var voice = storyIncomingVoices[i];
                if (voice == null) continue;
                ArcProjectile projectile = null;
                if (live)
                    foreach (var p in battle.Projectiles)
                        if (p.StoryMortarIndex == i) { projectile = p; break; }
                if (projectile == null || storyIncoming == null)
                {
                    voice.Stop();
                    continue;
                }
                anyIncoming = true;
                float elapsed = Mathf.Clamp(battle.Time - projectile.Born, 0, storyIncoming.length - .001f);
                voice.mute = !soundEnabled;
                // Simulation clock is authoritative, including pause and 1x/2x/4x switches.
                voice.pitch = Mathf.Clamp(speed, .1f, 4);
                if (paused) { voice.Pause(); continue; }
                if (!storyIncomingStarted[i])
                {
                    storyIncomingStarted[i] = true;
                    voice.clip = storyIncoming;
                    voice.volume = .45f;
                    voice.time = elapsed;
                    voice.Play();
                }
                else
                {
                    voice.UnPause();
                    if (Mathf.Abs(voice.time - elapsed) > .12f) voice.time = elapsed;
                }
            }
            if (!anyIncoming && allStarted)
            {
                if (storyCleanupAt < 0) storyCleanupAt = Time.unscaledTime + (storyImpact != null ? storyImpact.length : 3) + .2f;
                if (live && !paused && Time.unscaledTime >= storyCleanupAt) ResetStoryMortars();
            }
        }

        void PlayStoryMortarImpact()
        {
            if (!soundEnabled || storyImpactVoice == null || storyImpact == null) return;
            storyImpactVoice.PlayOneShot(storyImpact, .55f);
        }

        void ResetStoryMortars()
        {
            storySalvoActive = false;
            storyCleanupAt = -1;
            storySalvoElapsed = 0;
            storyLaunchCount = 0;
            for (int i = 0; i < storyLaunchVoices.Length; i++)
            {
                if (storyLaunchVoices[i] != null) { storyLaunchVoices[i].Stop(); Destroy(storyLaunchVoices[i]); }
                storyLaunchVoices[i] = null;
            }
            for (int i = 0; i < storyIncomingVoices.Length; i++)
            {
                storyIncomingStarted[i] = false;
                if (storyIncomingVoices[i] != null) { storyIncomingVoices[i].Stop(); Destroy(storyIncomingVoices[i]); }
                storyIncomingVoices[i] = null;
            }
            if (storyImpactVoice != null) { storyImpactVoice.Stop(); Destroy(storyImpactVoice); storyImpactVoice = null; }
            foreach (var clip in new[] { storyLaunch, storyIncoming, storyImpact })
                if (clip != null) Resources.UnloadAsset(clip);
            storyLaunch = storyIncoming = storyImpact = null;
        }
    }
}
