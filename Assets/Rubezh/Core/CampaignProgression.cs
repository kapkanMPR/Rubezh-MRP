using System;

namespace SilentOrders.Core
{
    public static class CampaignProgression
    {
        public static int Reward(int mission) => mission < 1 || mission > 20 ? 0 : 150 + 20 * ((mission - 1) / 4);
        public static int Budget(int completed)
        {
            int total = 0;
            for (int i = 1; i <= Math.Min(20, completed); i++)
                total += Reward(i);
            return total;
        }

        public static int TrainingAfter(int level) => level <= 0 ? 0 : level == 1 ? 8 : level == 2 ? 12 : 16;
        public static int TrainingMax(int completed) => completed >= 16 ? 3 : completed >= 12 ? 2 : completed >= 8 ? 1 : 0;
        public static int BagCapacity(int extra) => extra == 2 ? 200 : 150;
    }
}
