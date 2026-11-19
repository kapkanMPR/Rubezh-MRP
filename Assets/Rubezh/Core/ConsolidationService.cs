using System;

namespace SilentOrders.Core
{
    public static class ConsolidationService
    {
        public static bool Quiet(BattleSimulation b, Soldier s)
        {
            if (
                b.Team(s).Style != CombatStyle.Passive
                || !s.Alive
                || s.Incapacitated
                || s.RescuerId >= 0
                || s.RescuePhase != 0
                || s.TreatmentUntil > 0
                || s.Broken
                || s.Bleeding
                || s.Suppression > .2f
                || b.Time - s.LastIncoming < 6
                || s.GrenadeEscape.Active
            )
                return false;
            foreach (var c in Knowledge.Contacts(b, s))
                if (
                    PositionLogic.Recent(b, c)
                    && c.Visual
                    && c.Confidence > .6f
                    && V2.Distance(s.Position, c.Position) < b.WeaponRange(s) + 2
                )
                    return false;
            return true;
        }

        public static bool WantsReload(BattleSimulation b, Soldier s)
        {
            if (
                !MagazineSystem.BetterMagazine(s)
                || !Quiet(b, s)
                || s.Moving
                || s.PathCursor < s.Path.Count
                || s.Busy
                || TacticalOrders.ServiceActive(s)
                || s.HandsBusyUntil > b.Time
                || s.Ammo <= 0
                || s.Ammo >= s.MagazineSize
                || s.Reserve <= 0
                || s.ReloadUntil > 0
                || b.Time < s.NextTacticalReload
            )
                return false;
            var q = b.Team(s);
            int others = 0,
                watch = 0;
            foreach (var ally in b.Soldiers)
                if (ally != s && q.Contains(ally) && ally.Deployed && ally.Available)
                {
                    others++;
                    if (ally.ReloadUntil > b.Time)
                        return false; // one preventive reload at a time
                    if (
                        !ally.Broken
                        && !ally.Busy
                        && !TacticalOrders.ServiceActive(ally)
                        && !ally.Moving
                        && ally.PathCursor >= ally.Path.Count
                        && ally.Ammo > 0
                        && ally.Suppression < .5f
                    )
                        watch++;
                }

            return others == 0 || watch > 0;
        }

        public static bool SupplyReady(BattleSimulation b, Soldier s)
        {
            return Quiet(b, s)
                && !s.Busy
                && !TacticalOrders.ServiceActive(s)
                && s.ReloadUntil <= b.Time
                && s.HandsBusyUntil <= b.Time
                && !s.Moving
                && s.PathCursor >= s.Path.Count;
        }
    }
}
