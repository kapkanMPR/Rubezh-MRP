namespace SilentOrders.Core
{
    public static class OrdnanceExperiment
    {
        // Compatibility for old comparison runners: applies to NEW battles only.
        public static bool Enabled = true;

        public static int InitialLoaded(Soldier soldier) => soldier.Rules.Ordnance && soldier.Underbarrel ? 1 : 0;

        // Standalone legacy calculation; battle code uses the explicit overload.
        public static float LandingFuse(float distance, float range = 9f) => Fuse(Enabled, distance, range);

        public static float LandingFuse(BattleSimulation battle, float distance, float range = 9f) =>
            Fuse(battle.Rules.Ordnance, distance, range);

        private static float Fuse(bool enabled, float distance, float range) =>
            enabled ? 1.5f - Util.Clamp(distance / range, 0, 1) : GrenadeSafety.Fuse;
    }
}
