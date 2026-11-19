using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    public static class FriendlyGrenadeSafety
    {
        // Legacy defaults for future battles only.
        public static bool Enabled = true,
            FragmentDodgeEnabled = true;
        public const float Lead = .25f,
            HoldAfter = .35f,
            HearingRange = 24f;

        public sealed class Metrics
        {
            public int Shouts,
                Ducks,
                FriendlyRolls,
                EnemyRolls,
                FriendlyBypasses,
                EnemyBypasses;
            internal readonly Dictionary<int, float> Until = new Dictionary<int, float>();
        }

        static readonly ConditionalWeakTable<BattleSimulation, Metrics> States =
            new ConditionalWeakTable<BattleSimulation, Metrics>();

        public static Metrics Stats(BattleSimulation b) => States.GetValue(b, _ => new Metrics());

        public static void Warn(BattleSimulation b, Soldier thrower, ArcProjectile p)
        {
            if (
                !b.Rules.FriendlyGrenadeWarnings
                || !CombatTraining.Skill(thrower, 2, "warn")
                || thrower.Side != Side.Green
                || p.Kind != 1
            )
                return;
            p.WarningListeners = new List<int>();
            foreach (var s in b.Soldiers)
                if (
                    s.Side == thrower.Side
                    && s.Alive
                    && s.Deployed
                    && V2.Distance(s.Position, thrower.Position) <= HearingRange
                )
                    p.WarningListeners.Add(s.Id);
            Stats(b).Shouts++;
            b.Say(thrower, "СВОЯ!", urgent: true);
            b.History.Event(
                b,
                "friendlyGrenadeWarning",
                thrower.Id,
                -1,
                thrower.Position,
                p.To,
                HandGrenades.Type(p),
                HandGrenades.Name(HandGrenades.Type(p))
            );
        }

        public static float ExpectedExplosion(BattleSimulation b, ArcProjectile p, out V2 point)
        {
            point = p.Landed ? p.Position : ArcedOrdnance.ArcImpact(b, p.From, p.To, 1);
            if (p.Rethrown || p.Landed)
                return p.DetonateAt;
            float length = V2.Distance(p.From, p.To),
                distance = V2.Distance(p.From, point);
            float arrival = p.Born + p.Duration * (length > .001f ? Util.Clamp(distance / length, 0, 1) : 1);
            return arrival + (p.ImpactFuse ? 0 : OrdnanceExperiment.LandingFuse(b, distance, p.F1 ? 8 : 9));
        }

        static void Schedule(BattleSimulation b, ArcProjectile p, V2 point, float until)
        {
            if (p.WarningListeners == null)
                return;
            var state = Stats(b);
            float radius = Math.Max(ArcedOrdnance.BlastRadius(p), HandGrenades.FragmentRange(p));
            foreach (int id in p.WarningListeners)
            {
                if (id < 0 || id >= b.Soldiers.Count)
                    continue;
                var s = b.Soldiers[id];
                if (
                    !CombatTraining.Skill(s, 2, "warn")
                    || !s.Alive
                    || !s.Deployed
                    || s.Side != Side.Green
                    || V2.Distance(s.Position, point) > radius + .35f
                    || !b.Map.LineOfSight(point, s.Position)
                )
                    continue;
                float old;
                state.Until.TryGetValue(id, out old);
                if (old <= b.Time)
                {
                    state.Ducks++;
                    b.History.Event(
                        b,
                        "friendlyGrenadeDuck",
                        s.Id,
                        p.ShooterId,
                        s.Position,
                        point,
                        until - b.Time,
                        "СВОЯ!"
                    );
                }

                state.Until[id] = Math.Max(old, until);
            }
        }

        public static void Update(BattleSimulation b)
        {
            if (!b.Rules.FriendlyGrenadeWarnings)
                return;
            foreach (var p in b.Projectiles)
                if (p.Kind == 1 && p.WarningListeners != null)
                {
                    V2 point;
                    float at = ExpectedExplosion(b, p, out point);
                    if (at - b.Time <= Lead && at >= b.Time - .05f)
                        Schedule(b, p, point, Math.Max(b.Time, at) + HoldAfter);
                }
        }

        public static void Exploded(BattleSimulation b, ArcProjectile p)
        {
            // Only already warned listeners react; impact grenades never acquire a new fuse delay.
            if (!b.Rules.FriendlyGrenadeWarnings || p.Kind != 1 || p.WarningListeners == null)
                return;
            Schedule(b, p, p.Position, b.Time + HoldAfter);
        }

        public static bool Active(BattleSimulation b, Soldier s)
        {
            Metrics state;
            float until;
            return b.Rules.FriendlyGrenadeWarnings
                && s.Side == Side.Green
                && s.Alive
                && s.Deployed
                && States.TryGetValue(b, out state)
                && state.Until.TryGetValue(s.Id, out until)
                && until > b.Time;
        }

        public static bool Hold(BattleSimulation b, Soldier s)
        {
            if (!Active(b, s))
                return false;
            s.Crouch.StandUntil = 0;
            s.Crouched = true;
            s.Moving = false;
            s.SupportReady = false;
            s.State = ActionState.Observe;
            s.Reason = "СВОЯ! Присяду к взрыву, пережду осколки";
            return true;
        }

        public static bool Bypass(BattleSimulation b, Bullet p, Soldier target)
        {
            if (!b.Rules.FragmentDodge || !p.Fragment)
                return false;
            if (target.Id >= p.CrouchRoll.Length)
                Array.Resize(ref p.CrouchRoll, b.Soldiers.Count);
            if (p.CrouchRoll[target.Id] != 0)
                return p.CrouchRoll[target.Id] == 1;
            if (!target.Crouched)
                return false;
            bool pass = b.Random.Value() < .5f;
            p.CrouchRoll[target.Id] = (byte)(pass ? 1 : 2);
            var stats = Stats(b);
            if (target.Side == p.Side)
            {
                stats.FriendlyRolls++;
                if (pass)
                    stats.FriendlyBypasses++;
            }
            else
            {
                stats.EnemyRolls++;
                if (pass)
                    stats.EnemyBypasses++;
            }

            return pass;
        }
    }
}
