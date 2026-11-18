using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class SurpriseBurstState
    {
        public readonly HashSet<int> Attacked = new HashSet<int>();
        public int Target = -1, Remaining, Starts, BoostedShots;
        public float LastShot = -100;
    }

    public static class SurpriseBurst
    {
        // First assault-rifle burst: up to 3 emitted rounds, once per shooter/target per battle.
        // Eligibility is adjudicated by combat resolution, never exposed to route planning.
        public static float DistanceMultiplier(float distance) => 1 + 2 * Util.Clamp((7 - distance) / 3f, 0, 1);
        public static float ShotMultiplier(BattleSimulation b, Soldier shooter, Contact c)
        {
            if (!b.SurpriseBurstEnabled || b.Historical01225 || shooter.GunSniper || shooter.GunMachineGunner || shooter.SomaliFiring || c == null)
                return 1;
            var st = shooter.Awareness.Surprise;
            int id = c.EnemyId;
            bool continuation = st.Target == id && st.Remaining > 0 && b.Time - st.LastShot <= Math.Max(2, WeaponBalance.For(shooter).FireSeconds * 2.5f);
            if (!continuation)
            {
                st.Target = -1;
                st.Remaining = 0;
                if (id < 0 || id >= b.Soldiers.Count || !st.Attacked.Add(id))
                    return 1;
                var target = b.Soldiers[id];
                if (V2.Distance(shooter.Position, target.Position) >= 7 || !target.Alive || !target.Deployed || target.Side == shooter.Side || !c.Visual || c.Inferred || !shooter.Visible.Contains(id) || b.Time - c.SeenAt > b.Config.PerceptionPeriod * 2)
                    return 1;
                V2 look = target.Awareness.Initialized ? target.Awareness.Look : target.Facing;
                if (DirectionalVision.Angle(look, shooter.Position - target.Position) < 130 || target.Visible.Contains(shooter.Id))
                    return 1;
                // Refresh geometry too: a stale Visible list cannot create a bonus by itself.
                if (DirectionalVision.InView(b, target, shooter.Position) && b.Map.LineOfSight(target.Position, shooter.Position) && b.SmokeTransmission(target.Position, shooter.Position) >= .25f)
                    return 1;
                st.Target = id;
                st.Remaining = 3;
                st.Starts++;
                b.History.Event(b, "surpriseBurstStart", shooter.Id, id, shooter.Position, c.Position, 3, "Первая незамеченная очередь сзади");
            }

            st.Remaining--;
            st.LastShot = b.Time;
            float multiplier = DistanceMultiplier(V2.Distance(shooter.Position, b.Soldiers[id].Position));
            if (multiplier > 1)
                st.BoostedShots++;
            return multiplier;
        }

        public static float Damage(Bullet p, int victim) => p.SurpriseTarget == victim && !p.Ricocheted ? p.Damage * p.SurpriseMultiplier : p.Damage;
        public static void Interrupt(Soldier s)
        {
            s.Awareness.Surprise.Remaining = 0;
            s.Awareness.Surprise.Target = -1;
        }
    }
}
