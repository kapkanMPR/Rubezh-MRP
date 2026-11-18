using System;

namespace SilentOrders.Core
{
    // Applied ONLY to bullet hits / near misses, never explosive shock waves.
    // A fighter can turn to face a flank attacker; therefore the bonus is based
    // on current Facing, not an omniscient fixed team direction.
    public static class RifleStress
    {
        public static float Multiplier(Soldier target, V2 origin)
        {
            V2 facing = target.Facing;
            V2 approach = origin - target.Position;
            if (facing.Length < .1f || approach.Length < .1f)
                return 1f;
            float dot = V2.Dot(facing.Normalized, approach.Normalized);
            if (dot <= -.55f)
                return 1.8f; // rear arc
            if (dot < .3f)
                return 1.35f; // lateral arc
            return 1f; // frontal fire
        }

        public static void RifleHit(BattleSimulation battle, Soldier shooter, Soldier victim, float damage, float suppression, float morale)
        {
            float bonus = Multiplier(victim, shooter.Position);
            // Health damage deliberately remains unchanged.
            battle.Damage(victim, shooter, damage, suppression * bonus, morale * bonus);
        }
    }

    public static class MoraleDynamics
    {
        public const float BrokenRecoveryPause = 2f;
        public static void ConfigureTraining(BattleSimulation b, Soldier s, int level)
        {
            level = Math.Max(0, Math.Min(3, level));
            s.MoraleFactor = b.Historical01225 ? 1f + level * .05f : 1f;
            s.MoraleMaximum = b.Historical01225 ? 1f : 1f + level * .05f;
            s.MoraleRegenBonus = b.Historical01225 ? 0f : level * .01f;
            s.MoraleBlockReduction = b.Historical01225 ? 0f : level * .5f;
            s.Morale = s.MoraleMaximum;
        }

        public static float RecoveryRate(Soldier s, float rate) => rate > 0 ? rate * s.MoraleFactor + s.MoraleRegenBonus : rate;
        public static float RecoveryPause(Soldier s) => Math.Max(0, BrokenRecoveryPause - s.MoraleBlockReduction);
        public static bool EnterBroken(BattleSimulation b, Soldier s)
        {
            if (!s.Alive || s.Broken)
                return false;
            s.Broken = true;
            s.MoraleRecoveryBlockedUntil = b.Time + RecoveryPause(s);
            s.SupportReady = false;
            b.Stats.Cowers++;
            Dialogue.Say(b, s, 9, urgent: true);
            s.NextDecision = b.Time;
            return true;
        }

        public static void Restore(BattleSimulation b, Soldier s, float amount)
        {
            if (s.Broken && b.Time < s.MoraleRecoveryBlockedUntil)
                return;
            s.Morale = Util.Clamp(s.Morale + amount, 0f, s.MoraleMaximum);
        }

        public static bool CanRecover(BattleSimulation b, Soldier s)
        {
            return s.Broken && b.Time >= s.MoraleRecoveryBlockedUntil && b.Time - s.LastIncoming > 3f && s.Suppression < .28f && s.Morale >= .2f;
        }
    }
}
