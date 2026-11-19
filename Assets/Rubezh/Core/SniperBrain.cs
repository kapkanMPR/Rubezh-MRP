using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SniperBrain
    {
        public static int Priority(BattleSimulation b, Soldier target)
        {
            return (!target.Sniper)
                ? (
                    (target.Role == Role.MachineGunner)
                        ? 1
                        : (
                            (target.Role == Role.Grenadier || target.Underbarrel)
                                ? 2
                                : ((target.Role == Role.Medic) ? 3 : ((target.Id == b.Team(target).LeaderId) ? 4 : 5))
                        )
                )
                : 0;
        }

        public static void Plan(BattleSimulation b, Soldier s)
        {
            SniperTactics.Plan(b, s);
        }
    }
}
