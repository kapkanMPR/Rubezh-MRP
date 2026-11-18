namespace SilentOrders.Core
{
    // Applied once to a new battle, after loading the formation snapshot.
    public static class OperationMode
    {
        public static void Apply(BattleSimulation b)
        {
            bool quiet = b.Green.Formation != null && b.Green.Formation.quietWork;
            b.PairExperimentMarch = quiet;
            b.PairExperimentProfile = quiet ? 4 : 0;
            b.PairExperimentCover = quiet;
            b.PairPlan = quiet ? new PairPlanSettings
            {
                AdaptiveRisk = true
            }

            : null;
            b.TacticalMarchEnabled = quiet;
            b.QuietReconEnabled = quiet;
            b.UsefulReconEnabled = true;
            b.ReconOpeningEnabled = quiet;
            b.ReconCombatTransitionEnabled = quiet;
            b.OpeningFixedAccuracyEnabled = true;
            b.OpeningIgnoreCoverEnabled = false;
            b.OpeningDoubleAccuracyEnabled = false;
            b.OpeningHorizonEnabled = true;
            b.OpeningCriticalEnabled = true;
            b.OpeningAllowOpenGround = true;
        }
    }
}
