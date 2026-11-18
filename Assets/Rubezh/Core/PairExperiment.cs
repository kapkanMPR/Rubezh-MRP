using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    // Opt-in game navigation experiment. No access to hidden opponents.
    public static class PairExperiment
    {
        sealed class Memory
        {
            public readonly Dictionary<int, float> Seen = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Openness = new Dictionary<int, float>();
            public int Revision = -1;
        }

        static readonly ConditionalWeakTable<BattleSimulation, Memory> Data = new ConditionalWeakTable<BattleSimulation, Memory>();
        static int Key(BattleSimulation b, V2 p) => (int)p.Y * b.Map.Columns + (int)p.X;
        public static V2 ChooseSector(BattleSimulation b, Soldier s, List<KeyValuePair<float, V2>> candidates, List<V2> assigned, V2 fallback)
        {
            var m = Data.GetOrCreateValue(b);
            float best = float.MinValue;
            V2 result = fallback;
            foreach (var c in candidates)
            {
                var delta = c.Value - s.Position;
                if (delta.Length < 1 || !b.Map.LineOfSight(s.Position, c.Value))
                    continue;
                float seen;
                float age = m.Seen.TryGetValue(Key(b, c.Value), out seen) ? Math.Min(8, b.Time - seen) : 8;
                float score = c.Key + age * (b.PairExperimentProfile == 4 ? .12f : .3f) - DirectionalVision.Angle(s.Awareness.Look, delta) * (b.PairExperimentProfile == 4 ? .025f : .008f);
                foreach (var p in assigned)
                {
                    float angle = DirectionalVision.Angle(delta, p - s.Position);
                    if (angle < 55)
                        score -= (55 - angle) * (b.PairExperimentProfile == 4 ? .06f : .13f);
                }

                if (score > best)
                {
                    best = score;
                    result = c.Value;
                }
            }

            // Mark only actually visible candidates; assignment alone never counts as inspection.
            foreach (var c in candidates)
                if (DirectionalVision.InView(b, s, c.Value) && b.Map.LineOfSight(s.Position, c.Value) && b.SmokeTransmission(s.Position, c.Value) > .8f)
                    m.Seen[Key(b, c.Value)] = b.Time;
            return result;
        }

        public static float RouteCost(BattleSimulation b, Soldier s, V2 p)
        {
            if (ReconCombatPhase.Active(b) || !b.PairExperimentMarch || b.PairExperimentProfile < 2)
                return 0;
            var m = Data.GetOrCreateValue(b);
            if (m.Revision != b.Map.Revision)
            {
                m.Revision = b.Map.Revision;
                m.Openness.Clear();
            }

            int key = Key(b, p);
            float open;
            if (!m.Openness.TryGetValue(key, out open))
            {
                open = 0;
                for (int i = 0; i < 8; i++)
                {
                    float a = i * (float)Math.PI / 4;
                    var end = p + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * 5;
                    if (b.Map.LineOfSight(p, end))
                        open++;
                }

                m.Openness[key] = open;
            }

            // Bounded non-negative cost. Profile 4 reduces detours; objective remains route endpoint.
            return open * (b.PairExperimentProfile == 4 ? .06f : .4f);
        }
    }
}
