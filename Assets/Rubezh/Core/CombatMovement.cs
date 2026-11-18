using System;

namespace SilentOrders.Core
{
    public static class CombatMovement
    {
        // Morale below 45% degrades decisiveness and movement; Broken uses its
        // existing Cower / crawl behavior and cannot use combat sprint.
        public static float MoraleMultiplier(Soldier s)
        {
            if (s.Broken)
                return 0f;
            float fraction = Util.Clamp(s.Morale / .45f, 0f, 1f);
            return .65f + .35f * fraction;
        }

        public static float Speed(BattleSimulation b, Soldier s)
        {
            if (!s.Alive || s.Incapacitated || s.Broken)
                return 0f;
            float speed = b.Config.MoveSpeed * MotionCombat.SpeedMultiplier(b, s) * BodyArmor.Multiplier(s.ArmorLevel);
            speed *= s.GunMachineGunner ? .78f : 1f;
            speed *= s.Health < 45f ? .75f : 1f;
            speed *= 1f - Util.Clamp(s.Suppression, 0f, 1f) * .42f;
            speed *= ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(b, s));
            speed *= MoraleMultiplier(s);
            return Math.Max(0f, speed);
        }
    }
}
