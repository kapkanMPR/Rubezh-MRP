using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SuppressionModel
    {
        public static void Apply(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier shooter, SilentOrders.Core.V2 impact, int directVictim = -1)
        {
            bool flag = b.Map.At(impact) == SilentOrders.Core.Tile.LowCover || b.Map.At(impact) == SilentOrders.Core.Tile.Wall;
            foreach (SilentOrders.Core.Soldier soldier in b.Soldiers)
            {
                if (!soldier.Alive || soldier.Side == shooter.Side || soldier.Id == directVictim)
                {
                    continue;
                }

                float num = SilentOrders.Core.V2.SegmentDistance(soldier.Position, shooter.Position, impact);
                float num2 = SilentOrders.Core.V2.Distance(soldier.Position, impact);
                float num3 = ((num < 1.2f && b.Map.LineOfSight(shooter.Position, soldier.Position)) ? ((1f - num / 1.2f) * 0.085f) : 0f);
                float num4 = ((num2 < 2.3f && b.Map.LineOfSight(impact, soldier.Position)) ? ((1f - num2 / 2.3f) * (flag ? 0.16f : 0.07f)) : 0f);
                float num5 = System.Math.Max(num3, num4) * (shooter.GunMachineGunner ? 1.2f : 1f) * RifleStress.Multiplier(soldier, shooter.Position);
                if (!(num5 < 0.004f))
                {
                    float suppression = soldier.Suppression;
                    b.RecordStress(shooter, soldier, num5, num5 * 0.055f);
                    soldier.Suppression = SilentOrders.Core.Util.Clamp(suppression + num5*soldier.RaidSuppressionFactor, 0f, 1f);
                    soldier.LastIncoming = b.Time;
                    soldier.Morale = SilentOrders.Core.Util.Clamp(soldier.Morale - num5 * 0.055f, 0f, soldier.MoraleMaximum);
                    if (num4 > num3 && flag)
                    {
                        b.Stats.CoverImpacts++;
                    }
                    else
                    {
                        b.Stats.NearMisses++;
                    }

                    if (suppression < 0.65f && soldier.Suppression >= 0.65f)
                    {
                        SilentOrders.Core.Dialogue.Say(b, soldier, 8, urgent: true);
                    }
                }
            }
        }
    }
}
