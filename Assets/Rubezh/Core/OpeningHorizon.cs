using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Short lookahead over known geometry and observed contacts, never hidden enemy state.
    public static class OpeningHorizon
    {
        public static bool WalkSegment(BattleSimulation b, V2 a, V2 z)
        {
            int n = Math.Max(1, (int)Math.Ceiling(V2.Distance(a, z) * 4));
            for (int i = 0; i <= n; i++)
                if (!b.Map.Walkable(a + (z - a) * (i / (float)n)))
                    return false;
            return true;
        }

        public static float PositionValue(BattleSimulation b, Soldier s, V2 p, List<Contact> contacts)
        {
            float value = 0;
            foreach (var c in contacts)
            {
                float d = V2.Distance(p, c.Position);
                if (!b.Map.LineOfSight(p, c.Position))
                    continue;
                float own = CombatTraining.Cover(b, s, p, c.Position),
                    enemy = b.Map.Protection(c.Position, p);
                float range = Util.Clamp((b.WeaponRange(s) - d) / 5, 0, 1);
                value += (d <= b.WeaponRange(s) ? (1 - enemy) * 5 + range * 2 : 0) - (1 - own) * 7;
            }

            return value;
        }

        public static float Evaluate(
            BattleSimulation b,
            Soldier s,
            V2 p,
            List<Contact> contacts,
            out V2 next,
            out int exits
        )
        {
            next = p;
            exits = 0;
            float current = PositionValue(b, s, p, contacts),
                best = current;
            float nearestGoal = V2.Distance(p, b.Map.Objective);
            for (int i = 0; i < 8; i++)
            for (int radius = 2; radius <= 4; radius += 2)
            {
                float angle = i * (float)Math.PI / 4;
                V2 z = p + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                if (!b.Map.Walkable(z) || !CommandHierarchy.WithinManeuver(b, s, z) || !WalkSegment(b, p, z))
                    continue;
                bool protectedPoint = false,
                    tooClose = false;
                foreach (var c in contacts)
                {
                    if (V2.Distance(z, c.Position) < 5)
                        tooClose = true;
                    if (!b.Map.LineOfSight(z, c.Position) || CombatTraining.Cover(b, s, z, c.Position) > .5f)
                        protectedPoint = true;
                }

                if (tooClose || !protectedPoint)
                    continue;
                exits++;
                float score =
                    PositionValue(b, s, z, contacts)
                    + (nearestGoal - V2.Distance(z, b.Map.Objective)) * .8f
                    - radius * .25f;
                if (score > best + .5f)
                {
                    best = score;
                    next = z;
                }
            }

            // Reward a sustainable current position, plus a usable next step. Penalize cul-de-sacs.
            return current * 1.5f
                + Math.Max(0, best - current) * .75f
                + Math.Min(exits, 4) * .35f
                - (exits == 0 ? 6 : 0);
        }
    }
}
