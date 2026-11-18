using System;

namespace SilentOrders.Core
{
    /// <summary>
    /// Rules captured for one battle. Legacy static switches are defaults for NEW
    /// battles only; changing a test's defaults cannot change another live battle.
    /// Pass explicit rules to BattleSimulation for independent/concurrent runs.
    /// </summary>
    public sealed class GameplayRules
    {
        public bool Ordnance { get; }
        public bool SomaliFire { get; }
        public bool FriendlyGrenadeWarnings { get; }
        public bool FragmentDodge { get; }
        public int CareMask { get; }
        public int MechanicsMask { get; private set; }

        public GameplayRules(bool ordnance = true, bool somaliFire = true, int careMask = 7, int mechanicsMask = NewMechanics.AcceptedMask, bool friendlyGrenadeWarnings = true, bool fragmentDodge = true)
        {
            Ordnance = ordnance;
            SomaliFire = somaliFire;
            CareMask = careMask & 7;
            MechanicsMask = mechanicsMask & NewMechanics.AcceptedMask;
            FriendlyGrenadeWarnings = friendlyGrenadeWarnings;
            FragmentDodge = fragmentDodge;
        }

        public GameplayRules Copy() => new GameplayRules(Ordnance, SomaliFire, CareMask, MechanicsMask, FriendlyGrenadeWarnings, FragmentDodge);
        internal static GameplayRules CaptureDefaults() => new GameplayRules(OrdnanceExperiment.Enabled, global::SilentOrders.Core.SomaliFire.Enabled, CareExperiments.Mask, NewMechanics.Mask, FriendlyGrenadeSafety.Enabled, FriendlyGrenadeSafety.FragmentDodgeEnabled);
        internal void SetMechanicsMask(int value)
        {
            MechanicsMask = value & NewMechanics.AcceptedMask;
        }

        public bool CareEnabled(int bit) => bit >= 0 && bit < 3 && (CareMask & (1 << bit)) != 0;
    }
}
