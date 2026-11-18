using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SniperBrain
    {
        public static int Priority(SilentOrders.Core.BattleSimulation b, SilentOrders.Core.Soldier target)
        {
            return (!target.Sniper) ? ((target.Role == SilentOrders.Core.Role.MachineGunner) ? 1 : ((target.Role == SilentOrders.Core.Role.Grenadier || target.Underbarrel) ? 2 : ((target.Role == SilentOrders.Core.Role.Medic) ? 3 : ((target.Id == b.Team(target).LeaderId) ? 4 : 5)))) : 0;
        }

        public static void Plan(BattleSimulation b, Soldier s)
        {
            SniperTactics.Plan(b, s);
        }
    }
}
