// Real effect presenter code against portable Unity contracts; no GPU/shader execution claim.
using System;
using System.Collections;
using System.Reflection;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

public static class NativeEffects213Check
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    static void Check(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static object Get(object target, string field) => target.GetType().GetField(field, Flags).GetValue(target);

    static void Update(HdGpuRenderer renderer, BattleSimulation battle) =>
        renderer.GetType().GetMethod("UpdateNativeEffects", Flags).Invoke(renderer, new object[] { battle });

    public static void Run(HdGpuRenderer renderer, BattleSimulation battle)
    {
        var traces = battle.Traces.ToArray();
        var blasts = battle.Blasts.ToArray();
        var smoke = battle.Smoke.ToArray();
        var projectiles = battle.Projectiles.ToArray();
        var craters = battle.Craters.ToArray();
        float time = battle.Time;
        int tick = battle.TickNumber;
        bool fog = battle.Fog.Enabled;
        var loader = Resources.Loader;
        try
        {
            Resources.Loader = path =>
                path == "Effects/terrain-crater" ? new Texture2D(16, 16, TextureFormat.RGBA32, false) : loader(path);
            battle.Traces.Clear();
            battle.Blasts.Clear();
            battle.Smoke.Clear();
            battle.Projectiles.Clear();
            battle.Craters.Clear();
            battle.Fog.Enabled = false;
            battle.Time = 2;
            battle.TickNumber++;
            V2 position = battle.Soldiers[0].Position;
            battle.Blasts.Add(
                new BlastEffect
                {
                    Position = position,
                    Born = 1.9f,
                    Until = 3,
                    Radius = 3.3f,
                }
            );
            battle.Smoke.Add(
                new SmokeCloud
                {
                    Position = position,
                    Born = 0,
                    Until = 10,
                }
            );
            var projectile = new ArcProjectile
            {
                Position = position,
                From = position,
                To = position + new V2(3, 0),
                Kind = 3,
                ShooterId = 0,
                Born = 1.8f,
                Duration = 1,
                Height = 1,
            };
            battle.Projectiles.Add(projectile);
            battle.Traces.Add(
                new ShotTrace
                {
                    From = position,
                    To = position + new V2(2, 0),
                    Tail = position,
                    Ballistic = true,
                    Born = 1.99f,
                    Side = Side.Green,
                }
            );
            battle.Craters.Add(
                new BlastCrater
                {
                    Position = position,
                    Cells = 2,
                    Mirror = true,
                }
            );
            var pixels = CraterArt.Pixels;
            CraterArt.Pixels = null;
            int vertices = ((ICollection)Get(renderer, "vertices")).Count;
            Update(renderer, battle);
            Check(renderer.NativeEffectNodes > 5, "pooled effects absent");
            Check(renderer.NativeEffectMeshBuilds == 2, "only shared quad plus immutable blast mesh expected");
            Check(!projectile.HasMuzzleOffset, "native presenter mutated simulation projectile");
            Check(
                CraterArt.Pixels == null && renderer.GetType().GetField("craterPage", Flags) == null,
                "native crater triggered CPU copy/reupload"
            );
            Check(
                ((ICollection)Get(renderer, "vertices")).Count == vertices,
                "native effects appended legacy canvas geometry"
            );
            int updates = renderer.NativeEffectUpdates,
                pool = renderer.NativeEffectPoolSize;
            Update(renderer, battle);
            Check(
                renderer.NativeEffectUpdates == updates && renderer.NativeEffectPoolSize == pool,
                "same-frame camera render rebuilt effects"
            );
            battle.Time += .05f;
            battle.TickNumber++;
            Update(renderer, battle);
            Check(
                renderer.NativeEffectUpdates == updates + 1 && renderer.NativeEffectMeshBuilds == 2,
                "simulation update did not retain immutable geometry"
            );
            battle.Traces.Clear();
            battle.Blasts.Clear();
            battle.Smoke.Clear();
            battle.Projectiles.Clear();
            battle.Craters.Clear();
            for (int i = 0; i < 122; i++)
            {
                battle.TickNumber++;
                Update(renderer, battle);
            }
            Check(
                renderer.NativeEffectNodes == 0 && renderer.NativeEffectPoolSize == 0,
                "idle effect pool not released"
            );
            Check(
                ((IDictionary)Get(renderer, "projectileOrigins")).Count == 0,
                "expired projectile visual cache retained"
            );
            CraterArt.Pixels = pixels;
            Console.WriteLine(
                "PASS NativeEffects213Check: immutable geometry, simulation-time update, camera cache, no CPU crater data, pool expiry, no core mutation. GPU validation pending."
            );
        }
        finally
        {
            battle.Traces.Clear();
            battle.Traces.AddRange(traces);
            battle.Blasts.Clear();
            battle.Blasts.AddRange(blasts);
            battle.Smoke.Clear();
            battle.Smoke.AddRange(smoke);
            battle.Projectiles.Clear();
            battle.Projectiles.AddRange(projectiles);
            battle.Craters.Clear();
            battle.Craters.AddRange(craters);
            battle.Time = time;
            battle.TickNumber = tick;
            battle.Fog.Enabled = fog;
            Resources.Loader = loader;
        }
    }
}
