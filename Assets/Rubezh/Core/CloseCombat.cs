using System;

namespace SilentOrders.Core
{
    // Short contact range only; both sides use the same damage and recovery rules.
    public static class CloseCombat
    {
        public static bool TryStrike(BattleSimulation b, Soldier s)
        {
            if (
                !s.Available
                || s.Busy
                || s.Broken
                || s.Suppression > .8f
                || s.ReloadUntil > b.Time
                || s.ConcussionReactUntil > b.Time
                || s.NextMelee > b.Time
            )
                return false;
            foreach (int id in s.Visible)
            {
                var target = b.Soldiers[id];
                if (
                    !target.Available
                    || target.Side == s.Side
                    || V2.Distance(s.Position, target.Position) > 1.25f
                    || !b.Map.LineOfSight(s.Position, target.Position)
                )
                    continue;
                s.NextMelee = b.Time + 1.4f * ConcussionEffects.ReactionTimeMultiplier(ConcussionEffects.Current(b, s));
                s.NextShot = Math.Max(s.NextShot, b.Time + .5f);
                s.LastFire = b.Time;
                s.Facing = (target.Position - s.Position).Normalized;
                s.State = ActionState.Fire;
                s.Reason = "Ближний бой";
                if (
                    b.Random.Range(0f, 1f)
                    < .85f * ConcussionEffects.AccuracyMultiplier(ConcussionEffects.Current(b, s))
                )
                    b.Damage(target, s, b.Random.Range(18f, 28f), .15f, .08f);
                return true;
            }

            return false;
        }
    }
}
