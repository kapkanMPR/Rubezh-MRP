using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SmokePlanner
    {
        public static Soldier Carrier(
            BattleSimulation b,
            Squad q,
            V2 point,
            bool requireRange = true,
            bool allowSniper = false
        )
        {
            Soldier best = null;
            float score = float.MaxValue;
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && s.Available
                    && !s.Busy
                    && !s.Broken
                    && s.SmokeCharges > 0
                    && (
                        allowSniper
                        || q.Style == CombatStyle.Withdraw
                        || TacticalOrders.Emergency(q)
                        || s.SmokeCharges > 1
                    )
                    && s.SmokeReadyAt <= b.Time
                    && s.HandsBusyUntil <= b.Time
                    && s.ReloadUntil <= b.Time
                )
                {
                    float d = V2.Distance(s.Position, point);
                    if ((!requireRange || d <= 9 && b.Map.LineOfSight(s.Position, point)) && d < score)
                    {
                        score = d;
                        best = s;
                    }
                }

            return best;
        }

        public static void Plan(BattleSimulation b, Squad q, V2 destination)
        {
            SmokeTactics.Plan(b, q, destination);
        }

        public static void Update(BattleSimulation b, Squad q)
        {
            SmokeTactics.Update(b, q);
        }
    }
}
