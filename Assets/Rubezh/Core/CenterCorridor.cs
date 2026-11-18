using System;

namespace SilentOrders.Core
{
    // Soft shaping for commander-owned offensive bounds. Urgent safety, medical and withdrawal routes are exempt.
    public static class CenterCorridor
    {
        const float HalfWidth = 4.0f;
        static V2 Lateral(BattleSimulation b)
        {
            return TacticalOrders.Lateral(b);
        }

        static V2 Middle(BattleSimulation b)
        {
            return new V2(b.Map.Columns * .5f, b.Map.Rows * .5f);
        }

        public static V2 Limit(BattleSimulation b, V2 goal)
        {
            var lat = Lateral(b);
            var mid = Middle(b);
            float deviation = V2.Dot(goal - mid, lat);
            // Allow convergence to off-centre flags during the final approach.
            float near = V2.Distance(goal, b.Map.Objective);
            float allowance = HalfWidth + Math.Max(0, 9 - near) * .95f;
            return TacticalOrders.Open(b, goal - lat * (deviation - Util.Clamp(deviation, -allowance, allowance)));
        }

        public static float Penalty(BattleSimulation b, Soldier s, V2 point)
        {
            var q = b.Team(s);
            if (q.Side != Side.Green || q.Intent != ManeuverIntent.Center || q.Style == CombatStyle.Withdraw || q.Style == CombatStyle.Passive || TacticalOrders.Emergency(q) || CommandPosture.Service(b, s) || b.Time - s.LastIncoming < 2f)
                return 0;
            float outside = Math.Abs(V2.Dot(point - Middle(b), Lateral(b))) - HalfWidth;
            return Math.Max(0, outside) * .9f;
        }
    }
}
