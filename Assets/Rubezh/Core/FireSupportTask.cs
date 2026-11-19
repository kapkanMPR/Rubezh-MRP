using System;

namespace SilentOrders.Core
{
    // Short-lived coordination based on observed contacts and existing movement orders.
    public static class FireSupportTask
    {
        public static Soldier Beneficiary(BattleSimulation b, Soldier shooter)
        {
            Soldier best = null;
            float score = float.MinValue;
            foreach (var s in b.Soldiers)
            {
                if (
                    s == shooter
                    || s.Side != shooter.Side
                    || !s.Available
                    || !s.Deployed
                    || s.Busy
                    || s.PathCursor >= s.Path.Count
                    || V2.Distance(s.Position, shooter.Position) > 18
                )
                    continue;
                float value =
                    (s.DefenderGroup == shooter.DefenderGroup ? 10 : 0)
                    + (RedCounterattack.Retreating(b, s) ? 12 : 0)
                    - V2.Distance(s.Position, shooter.Position) * .3f;
                if (value > score)
                {
                    score = value;
                    best = s;
                }
            }

            return best;
        }

        public static Soldier Assigned(BattleSimulation b, Soldier shooter)
        {
            int id = shooter.AreaSuppression.BeneficiaryId;
            if (id < 0 || id >= b.Soldiers.Count)
                return null;
            var s = b.Soldiers[id];
            return s.Available && s.Deployed && !s.Busy && s.PathCursor < s.Path.Count ? s : null;
        }

        public static bool Threatens(BattleSimulation b, Contact c, Soldier mover)
        {
            if (mover == null)
                return true;
            for (int i = 0; i < 4; i++)
            {
                V2 point = mover.Position + (mover.Destination - mover.Position) * (i / 3f);
                if (
                    PositionLogic.Relevant(b, c, point)
                    && b.Map.LineOfSight(c.Position, point)
                    && b.SmokeTransmission(c.Position, point) > .3f
                )
                    return true;
            }

            return false;
        }
    }
}
