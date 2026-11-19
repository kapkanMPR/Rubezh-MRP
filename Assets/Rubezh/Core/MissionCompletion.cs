using System;

namespace SilentOrders.Core
{
    // Authoritative simulation time, not real-time UI timers. Survivors include wounded
    // and undeployed units; a pending reinforcement wave always prevents completion.
    public static class MissionCompletion
    {
        public const float ClearDelay = 5f;

        public static bool EnemyCompletelyEliminated(BattleSimulation b)
        {
            if (b.CampaignReserve > 0)
                return false;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Red && s.Alive)
                    return false;
            return true;
        }

        public static bool Update(BattleSimulation b)
        {
            if (b.Outcome != Outcome.Running)
                return b.VictoryByElimination;
            if (b.Alive(Side.Green) == 0 || !EnemyCompletelyEliminated(b))
            {
                b.EnemyClearedAt = -1f;
                return false;
            }

            if (b.EnemyClearedAt < 0f)
            {
                b.EnemyClearedAt = b.Time;
                b.Log(Side.Green, "Противник разгромлен. Завершение операции через 5 секунд.");
            }

            if (b.Time - b.EnemyClearedAt + 0.0001f < ClearDelay)
                return false;
            b.VictoryByElimination = true;
            b.Outcome = Outcome.GreenCaptured; // Reuse existing victory/reward/next-mission path.
            b.Contested = false;
            b.Log(Side.Green, "Противник полностью разгромлен, включая подкрепления. Операция завершена.");
            return true;
        }

        public static float Remaining(BattleSimulation b)
        {
            return b.EnemyClearedAt < 0f ? -1f : Math.Max(0f, ClearDelay - (b.Time - b.EnemyClearedAt));
        }
    }
}
