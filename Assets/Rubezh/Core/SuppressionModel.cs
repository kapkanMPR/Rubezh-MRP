using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SuppressionModel
    {
        public static void Apply(BattleSimulation b, Soldier shooter, V2 impact, int directVictim = -1)
        {
            bool hitCover = b.Map.At(impact) == Tile.LowCover || b.Map.At(impact) == Tile.Wall;
            foreach (Soldier soldier in b.Soldiers)
            {
                if (!soldier.Alive || soldier.Side == shooter.Side || soldier.Id == directVictim)
                {
                    continue;
                }

                float trajectoryDistance = V2.SegmentDistance(soldier.Position, shooter.Position, impact);
                float impactDistance = V2.Distance(soldier.Position, impact);
                float nearMissStress = (
                    (trajectoryDistance < 1.2f && b.Map.LineOfSight(shooter.Position, soldier.Position))
                        ? ((1f - trajectoryDistance / 1.2f) * 0.085f)
                        : 0f
                );
                float impactStress = (
                    (impactDistance < 2.3f && b.Map.LineOfSight(impact, soldier.Position))
                        ? ((1f - impactDistance / 2.3f) * (hitCover ? 0.16f : 0.07f))
                        : 0f
                );
                float stress =
                    Math.Max(nearMissStress, impactStress)
                    * (shooter.GunMachineGunner ? 1.2f : 1f)
                    * RifleStress.Multiplier(soldier, shooter.Position);
                if (!(stress < 0.004f))
                {
                    float suppression = soldier.Suppression;
                    b.RecordStress(shooter, soldier, stress, stress * 0.055f);
                    soldier.Suppression = Util.Clamp(suppression + stress * soldier.RaidSuppressionFactor, 0f, 1f);
                    soldier.LastIncoming = b.Time;
                    soldier.Morale = Util.Clamp(soldier.Morale - stress * 0.055f, 0f, soldier.MoraleMaximum);
                    if (impactStress > nearMissStress && hitCover)
                    {
                        b.Stats.CoverImpacts++;
                    }
                    else
                    {
                        b.Stats.NearMisses++;
                    }

                    if (suppression < 0.65f && soldier.Suppression >= 0.65f)
                    {
                        Dialogue.Say(b, soldier, 8, urgent: true);
                    }
                }
            }
        }
    }
}
