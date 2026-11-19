namespace SilentOrders.Core
{
    public static class CombatTraining
    {
        public static bool Has(Soldier s, int level) =>
            s != null && (s.Side == Side.Red || s.CombatTrainingLevel >= level);

        public static bool Skill(Soldier s, int level, string key) =>
            s != null && (s.CampSkills == null ? Has(s, level) : s.CampSkills.Contains(key));

        public static bool Corners(BattleSimulation b, Soldier s) =>
            s != null
            && (
                s.Side == Side.Red
                    ? b.CampaignLevel > 7
                    : (s.CampSkills == null && b.PairExperimentCover) || Skill(s, 1, "corner")
            );

        public static bool ThrowBack(BattleSimulation b, Soldier s) =>
            s.Side == Side.Red ? b.CampaignLevel > 16 : Skill(s, 3, "return");

        public static int Max(int completed) =>
            completed >= 17 ? 3
            : completed >= 14 ? 2
            : completed >= 6 ? 1
            : 0;

        public static int Price(int level) =>
            level == 3 ? 550
            : level == 2 ? 350
            : level == 1 ? 150
            : 0;

        public static float Cover(BattleSimulation b, Soldier s, V2 point, V2 threat) =>
            b.Map.Protection(point, threat, Corners(b, s));

        public static int AvailableLauncher(Soldier supplier, Soldier receiver) =>
            LauncherStock(supplier, receiver.LauncherKind) > 0 ? receiver.LauncherKind
            : LauncherStock(supplier, 1 - receiver.LauncherKind) > 0 ? 1 - receiver.LauncherKind
            : -1;

        public static int LauncherStock(Soldier s, int kind) => kind == 1 ? s.PackRshg : s.PackRockets;

        public static void TakeLauncher(Soldier s, int kind)
        {
            if (kind == 1)
                s.PackRshg--;
            else
                s.PackRockets--;
        }
    }
}
