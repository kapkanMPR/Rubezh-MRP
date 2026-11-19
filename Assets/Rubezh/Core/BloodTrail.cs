using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class BloodTrace
    {
        public V2 Position;
        public int SoldierId;
        public float Time;
    }

    public static class BloodTrail
    {
        public const float Spacing = .36f;
        public const int MaxMarks = 2800;

        // Called after all movement including dragging; positions are simulation data.
        public static void Update(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
            {
                bool eligible = s.Alive && s.Deployed && s.Bleeding && (s.Incapacitated || s.RescuerId >= 0);
                if (!eligible)
                {
                    s.BloodAnchorValid = false;
                    continue;
                }

                if (!s.BloodAnchorValid)
                {
                    s.BloodAnchor = s.Position;
                    s.BloodAnchorValid = true;
                    continue;
                }

                float d = V2.Distance(s.BloodAnchor, s.Position);
                if (d < Spacing)
                    continue;
                // A teleport or scene placement must not paint a giant diagonal stripe.
                if (d > 5f)
                {
                    s.BloodAnchor = s.Position;
                    continue;
                }

                V2 direction = (s.Position - s.BloodAnchor).Normalized;
                int steps = Math.Min(24, (int)(d / Spacing));
                for (int i = 0; i < steps; i++)
                {
                    V2 point = s.BloodAnchor + direction * (Spacing * (i + 1));
                    if (!b.Map.Walkable(point))
                        continue;
                    b.BloodTraces.Add(
                        new BloodTrace
                        {
                            Position = point,
                            SoldierId = s.Id,
                            Time = b.Time,
                        }
                    );
                }

                s.BloodAnchor += direction * (Spacing * steps);
            }

            if (b.BloodTraces.Count > MaxMarks)
                b.BloodTraces.RemoveRange(0, b.BloodTraces.Count - MaxMarks);
        }
    }
}
