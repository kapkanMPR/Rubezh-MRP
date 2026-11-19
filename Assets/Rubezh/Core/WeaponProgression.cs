using System;

namespace SilentOrders.Core
{
    // Immutable per-weapon state. Shared safely between corpse, ground and carrier.
    public sealed class WeaponUpgrade
    {
        public readonly int Variant,
            Rarity,
            Level;
        public readonly WeaponBalance.Spec Spec;

        internal WeaponUpgrade(int variant, int rarity, int level, WeaponBalance.Spec spec)
        {
            Variant = variant;
            Rarity = rarity;
            Level = level;
            Spec = spec;
        }
    }

    public static class WeaponProgression
    {
        // Frozen balance: weapon-workbench epic40-v1 (2026-11-12).
        // 60% of the increase is awarded by breakthroughs, 40% by levels.
        public static WeaponBalance.Spec BuildSpec(int variant, float progress)
        {
            var p = WeaponBalance.ReferenceProfiles[variant];
            float sniper = variant % 6 >= 4 ? 5 : 0;
            float originalMin = 18 * p.Damage + p.DamageBonus + sniper;
            float originalMax = 30 * p.Damage + p.DamageBonus + sniper;
            float low = originalMin * (15f / 18f),
                high = originalMax * (20f / 30f);
            float min = low + (originalMin * 1.15f - low) * progress;
            float max = high + (originalMax * 1.15f - high) * progress;
            float multiplier = (max - min) / 12;
            return new WeaponBalance.Spec(
                p.Range,
                p.Accuracy * (.9f + .25f * progress),
                multiplier,
                p.FireSeconds,
                p.Magazine,
                p.Suppression,
                p.Morale,
                p.DistanceFalloff
            )
            {
                DamageBonus = min - 18 * multiplier - sniper,
                CriticalBonus = p.CriticalBonus,
                Spread = p.Spread,
                AccuracyAfterPenalties = p.AccuracyAfterPenalties,
                AimSeconds = p.AimSeconds,
            };
        }

        public static WeaponUpgrade At(int variant, int rarity, int level)
        {
            bool western = variant >= 6;
            int initial = western ? 2 : 1;
            rarity = Math.Max(initial, Math.Min(3, rarity));
            float progress;
            if (rarity == initial)
            {
                int cap = western ? 30 : 20;
                level = Math.Max(1, Math.Min(cap, level));
                progress = (western ? .2f : .15f) * (level - 1f) / (cap - 1);
            }
            else if (rarity == 2)
            {
                level = Math.Max(20, Math.Min(30, level));
                progress = .45f + .15f * (level - 20) / 10;
            }
            else
            {
                level = Math.Max(30, Math.Min(40, level));
                progress = (western ? .8f : .9f) + (western ? .2f : .1f) * (level - 30) / 10;
            }
            return new WeaponUpgrade(variant, rarity, level, BuildSpec(variant, progress));
        }

        public static void Bounds(int mission, bool endless, bool western, out int low, out int high)
        {
            int last = endless ? 50 : 20;
            float t = (Math.Max(1, Math.Min(last, mission)) - 1f) / (last - 1);
            low = (int)Math.Round((western ? 29 : 30) * t);
            high = (int)Math.Round(2 + ((western ? 40 : 41) - 2) * t);
        }

        public static void Assign(BattleSimulation b, Soldier s, int mission, bool endless)
        {
            if (s.Side != Side.Red || mission < 1)
                return;
            int variant = WeaponBalance.Index(s),
                low,
                high;
            Bounds(mission, endless, variant >= 6, out low, out high);
            // Independent hash: no combat RNG consumption, stable for late reinforcements.
            uint h = unchecked((uint)s.Id * 747796405u + (uint)b.Seed * 2891336453u);
            h ^= h >> 16;
            int step = low + (int)(h % (uint)(high - low + 1));
            if (variant >= 6)
                s.WeaponUpgrade = step <= 29 ? At(variant, 2, step + 1) : At(variant, 3, step);
            else
                s.WeaponUpgrade =
                    step <= 19 ? At(variant, 1, step + 1)
                    : step <= 30 ? At(variant, 2, step)
                    : At(variant, 3, step - 1);
        }

        public static void Configure(BattleSimulation b, int mission, bool endless)
        {
            b.EnemyWeaponMission = mission;
            b.EnemyWeaponEndless = endless;
            foreach (var s in b.Soldiers)
                Assign(b, s, mission, endless);
        }
    }
}
