using System;
using System.Collections.Generic;
using UnityEngine;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        readonly Dictionary<SmokeCloud, AudioSource> smokeVoices = new Dictionary<SmokeCloud, AudioSource>();
        readonly List<SmokeCloud> expiredSmokeVoices = new List<SmokeCloud>();
        void UpdateSmokeAudio()
        {
            bool active = battle != null && screen == 2 && battle.Outcome == Outcome.Running && soundEnabled && !paused;
            expiredSmokeVoices.Clear();
            foreach (var pair in smokeVoices)
            {
                if (battle == null || !battle.Smoke.Contains(pair.Key) || pair.Key.Until <= battle.Time || screen != 2)
                {
                    pair.Value.Stop();
                    Destroy(pair.Value);
                    expiredSmokeVoices.Add(pair.Key);
                }
                else if (!active)
                    pair.Value.Pause();
            }

            foreach (var cloud in expiredSmokeVoices)
                smokeVoices.Remove(cloud);
            if (!active || smokeSample == null || smokeSample.length <= 0)
                return;
            int count = 0;
            foreach (var cloud in battle.Smoke)
                if (cloud.Until > battle.Time)
                    count++;
            foreach (var cloud in battle.Smoke)
            {
                if (cloud.Until <= battle.Time)
                    continue;
                AudioSource source;
                if (!smokeVoices.TryGetValue(cloud, out source))
                {
                    source = NewAudio();
                    source.clip = smokeSample;
                    source.loop = true;
                    smokeVoices.Add(cloud, source);
                }

                // Leave sample pitch natural at 1x; faster simulation accelerates playback too.
                source.pitch = speed;
                source.volume = effectsVolume * .25f / (float)Math.Sqrt(Math.Max(1, count));
                float cursor = (battle.Time - cloud.Born) % smokeSample.length;
                if (!source.isPlaying)
                {
                    source.time = cursor;
                    source.Play();
                }
                else if (Math.Abs(source.time - cursor) > .2f)
                    source.time = cursor;
            }
        }
    }
}
