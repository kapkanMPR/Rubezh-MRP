using System;

namespace SilentOrders.Core
{
    // All smoke sources use the same visible trajectory as a hand-thrown grenade.
    // A cloud exists only after the projectile lands (or is blocked by a wall).
    public static class SmokeThrow
    {
        public static void Launch(BattleSimulation b, Soldier soldier, V2 destination)
        {
            if (soldier.SmokeThrowAt >= 0) return;
            soldier.SmokeThrowAt = b.Time + ArcedOrdnance.HandAimSeconds;
            soldier.SmokeThrowTarget = destination;
            soldier.HandsBusyUntil = Math.Max(soldier.HandsBusyUntil, soldier.SmokeThrowAt);
        }

        public static void Update(BattleSimulation b)
        {
            foreach(var s in b.Soldiers)
            {
                if(s.SmokeThrowAt < 0) continue;
                bool cancelled = !s.Alive || s.Incapacitated || s.RescuerId >= 0;
                if(!cancelled && b.Time + .0001f < s.SmokeThrowAt) continue;
                s.SmokeThrowAt = -1f;
                if(cancelled || !MotionCombat.ForwardAction(b,s,s.SmokeThrowTarget))
                {
                    // The reserved grenade remains with its carrier if preparation is interrupted.
                    s.SmokeCharges++;
                    b.SmokeEffects.RemoveAll(e=>e.Carrier==s.Id && e.Born>=b.Time);
                    continue;
                }
                LaunchPrepared(b,s,s.SmokeThrowTarget);
            }
        }

        static void LaunchPrepared(BattleSimulation b, Soldier soldier, V2 destination)
        {
            if (!MotionCombat.ForwardAction(b, soldier, destination))
                return;
            float distance = V2.Distance(soldier.Position, destination);
            float range = MotionCombat.SmokeRange(b, soldier);
            if (distance > range)
                destination = soldier.Position + (destination - soldier.Position).Normalized * range;
            b.Stats.SmokeUsed++;
            b.Diagnostics.SmokeLaunched++;
            b.Projectiles.Add(new ArcProjectile { RoofLowArc = RoofRules.LowArc(b.Map, soldier.Position, destination, 0), ShooterId = soldier.Id, Kind = 0, From = soldier.Position, To = destination, Position = soldier.Position, Height = ArcedOrdnance.Height(0f, 0), Born = b.Time, Duration = 1f + V2.Distance(soldier.Position, destination) / 7f });
        }
    }
}
