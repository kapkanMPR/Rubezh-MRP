using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    // Shared duration/strength model. Kept separate from damage and morale.
    public static class ConcussionEffects
    {
        public sealed class State
        {
            public float StartedAt,
                EndsAt,
                Strength;
        }

        static readonly ConditionalWeakTable<BattleSimulation, Dictionary<int, State>> States =
            new ConditionalWeakTable<BattleSimulation, Dictionary<int, State>>();

        static Dictionary<int, State> For(BattleSimulation battle) =>
            States.GetValue(battle, _ => new Dictionary<int, State>());

        // blastRadius is the damaging/suppression radius of the explosive source.
        // At the edge, concussion fades to zero; direct blasts last 3 seconds.
        public static float StrengthAt(float distance, float blastRadius)
        {
            if (blastRadius <= 0 || distance >= blastRadius)
                return 0f;
            float x = 1f - Math.Max(0f, distance) / blastRadius;
            return x * x * (3f - 2f * x); // smooth falloff
        }

        public static void Apply(BattleSimulation battle, Soldier soldier, float distance, float blastRadius)
        {
            if (battle == null || soldier == null || !soldier.Alive || !soldier.Deployed)
                return;
            float strength = StrengthAt(distance, blastRadius);
            if (strength <= 0f)
                return;
            float duration = 2f + strength;
            soldier.ConcussionReactUntil = Math.Max(
                soldier.ConcussionReactUntil,
                battle.Time + battle.Config.DecisionPeriod * ReactionTimeMultiplier(strength)
            );
            soldier.NextDecision = Math.Max(soldier.NextDecision, soldier.ConcussionReactUntil);
            var states = For(battle);
            if (!states.TryGetValue(soldier.Id, out var previous) || previous.EndsAt <= battle.Time)
            {
                states[soldier.Id] = new State
                {
                    StartedAt = battle.Time,
                    EndsAt = battle.Time + duration,
                    Strength = strength,
                };
            }
            else
            {
                // Multiple detonations extend, but do not stack into permanent stun.
                previous.EndsAt = Math.Max(previous.EndsAt, battle.Time + duration);
                previous.Strength = Math.Max(previous.Strength, strength);
                previous.StartedAt = battle.Time;
            }
        }

        public static float Current(BattleSimulation battle, Soldier soldier)
        {
            if (battle == null || soldier == null)
                return 0f;
            var states = For(battle);
            if (!states.TryGetValue(soldier.Id, out var state))
                return 0f;
            if (!soldier.Alive || battle.Time >= state.EndsAt)
            {
                states.Remove(soldier.Id);
                return 0f;
            }

            float fade = Math.Min(1f, (state.EndsAt - battle.Time) / .65f);
            return state.Strength * fade;
        }

        public static float MovementMultiplier(float strength) => 1f - .8f * Math.Max(0f, Math.Min(1f, strength));

        public static float AccuracyMultiplier(float strength) => 1f - .75f * Math.Max(0f, Math.Min(1f, strength));

        public static float ReactionTimeMultiplier(float strength) => 1f + 2f * Math.Max(0f, Math.Min(1f, strength));
    }
}
