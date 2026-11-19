using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class GrenadeEscapeState
    {
        public bool Active;
        public float NextPlan;
        public ArcProjectile Threat;
        public List<V2> Path = new List<V2>();
        public int Cursor;
    }

    public static class GrenadeSafety
    {
        public const float Fuse = 1.5f,
            Margin = .35f;

        public static float ScatterRadius(BattleSimulation b, Soldier shooter, Contact target, int kind)
        {
            bool personal =
                target.Visual
                && shooter.Visible.Contains(target.EnemyId)
                && b.Time - target.SeenAt <= b.Config.PerceptionPeriod * 2
                && b.Map.LineOfSight(shooter.Position, target.Position)
                && b.SmokeTransmission(shooter.Position, target.Position) >= .25f;
            float bonus =
                shooter.Side == Side.Green
                && (kind == 2 ? shooter.Role == Role.Grenadier : shooter.Role == Role.Rifleman)
                    ? 1f
                    : 0f;
            return Math.Max(
                    0,
                    (kind == 2 ? 3f : 2f)
                        / Ballistics.DistanceBonus(
                            V2.Distance(shooter.Position, target.Position),
                            ArcedOrdnance.Range(b, kind)
                        )
                        * (personal ? 1 : 2)
                        - bonus
                ) + target.Uncertainty;
        }

        public static V2 Scatter(BattleSimulation b, Soldier shooter, Contact target, int kind)
        {
            float angle = b.Random.Value() * (float)Math.PI * 2,
                radius = (float)Math.Sqrt(b.Random.Value()) * ScatterRadius(b, shooter, target, kind);
            return target.Position + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
        }

        public static bool Live(BattleSimulation b, ArcProjectile p) =>
            p != null && p.Kind == 1 && p.Landed && b.Projectiles.Contains(p);

        public static bool Dangerous(BattleSimulation b, V2 point)
        {
            foreach (var p in b.Projectiles)
                if (
                    Live(b, p)
                    && V2.Distance(point, p.Position) < ArcedOrdnance.BlastRadius(p) + Margin
                    && b.Map.LineOfSight(p.Position, point)
                )
                    return true;
            return false;
        }

        static ArcProjectile Threat(BattleSimulation b, Soldier s)
        {
            ArcProjectile best = null;
            float time = float.MaxValue;
            foreach (var p in b.Projectiles)
                if (
                    Live(b, p)
                    && p.DetonateAt < time
                    && V2.Distance(s.Position, p.Position) < ArcedOrdnance.BlastRadius(p) + Margin
                    && b.Map.LineOfSight(p.Position, s.Position)
                )
                {
                    best = p;
                    time = p.DetonateAt;
                }

            return best;
        }

        static void Interrupt(BattleSimulation b, Soldier s)
        {
            if (s.RescuerId >= 0)
            {
                var helper = b.Soldiers[s.RescuerId];
                SupportAI.Release(b, helper, false);
                helper.RescueCooldown = b.Time;
                s.RescueCooldown = b.Time;
            }

            if (s.RescueTargetId >= 0)
            {
                var patient = b.Soldiers[s.RescueTargetId];
                SupportAI.Release(b, s, false);
                patient.RescueCooldown = b.Time;
                s.RescueCooldown = b.Time;
            }

            foreach (var actor in b.Soldiers)
                if (
                    actor.SupplyDonorId >= 0
                    && (actor.Id == s.Id || actor.SupplyDonorId == s.Id || actor.SupplyReceiverId == s.Id)
                )
                    SupplyAI.Cancel(b, actor);
            s.TreatmentUntil = 0;
            s.TreatmentKind = 0;
            s.TreatmentDonorId = -1;
            s.OrdnanceAimTarget = s.RocketAimTarget = -1;
            s.SupportReady = false;
        }

        static void Plan(BattleSimulation b, Soldier s, ArcProjectile threat)
        {
            var state = s.GrenadeEscape;
            state.Path.Clear();
            state.Cursor = 0;
            state.NextPlan = b.Time + .3f;
            float best = float.MaxValue;
            V2 away = (s.Position - threat.Position).Normalized;
            if (away.Length < .1f)
                away = new V2(1, 0);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * (float)Math.PI / 8;
                V2 d = new V2(
                    away.X * (float)Math.Cos(angle) - away.Y * (float)Math.Sin(angle),
                    away.X * (float)Math.Sin(angle) + away.Y * (float)Math.Cos(angle)
                );
                V2 goal = threat.Position + d * (ArcedOrdnance.BlastRadius(threat) + Margin + .3f);
                if (!b.Map.Walkable(goal) || Dangerous(b, goal))
                    continue;
                var path = Locomotion.WalkableSegment(b, s.Position, goal)
                    ? new List<V2> { goal }
                    : Pathfinder.Find(b.Map, s.Position, goal);
                if (path.Count == 0)
                    continue;
                float length = 0;
                V2 previous = s.Position;
                foreach (var point in path)
                {
                    length += V2.Distance(previous, point);
                    previous = point;
                }

                if (length < best)
                {
                    best = length;
                    state.Path = path;
                }
            }
        }

        public static bool Update(BattleSimulation b, Soldier s, float dt)
        {
            if (!CombatTraining.Skill(s, 1, "evade") || !s.Alive || !s.Deployed)
                return false;
            var state = s.GrenadeEscape;
            var threat = Threat(b, s);
            if (threat == null)
            {
                if (!state.Active)
                    return false;
                // Stay out until the grenade has actually detonated; do not re-enter for an old order.
                if (Live(b, state.Threat))
                {
                    s.Moving = false;
                    s.SupportReady = false;
                    s.Reason = "Вышел из зоны гранаты — жду взрыв";
                    return true;
                }

                state.Active = false;
                state.Threat = null;
                state.Path.Clear();
                TacticalAI.Stop(s);
                s.NextDecision = 0;
                b.Team(s).NextPlan = 0;
                s.Reason = "Граната взорвалась — возвращаюсь к задаче";
                return false;
            }

            if (!state.Active || state.Threat != threat)
            {
                state.Active = true;
                state.Threat = threat;
                state.NextPlan = 0;
                state.Path.Clear();
                state.Cursor = 0;
                b.Diagnostics.Add(b, s, "interrupt", "EvadeGrenade", "Граната рядом: срочно выхожу из зоны поражения");
            }

            Interrupt(b, s);
            if (state.Cursor >= state.Path.Count && b.Time >= state.NextPlan)
                Plan(b, s, threat);
            while (state.Cursor < state.Path.Count && V2.Distance(s.Position, state.Path[state.Cursor]) < .08f)
                state.Cursor++;
            V2 direction =
                state.Cursor < state.Path.Count
                    ? (state.Path[state.Cursor] - s.Position).Normalized
                    : (s.Position - threat.Position).Normalized;
            float speed =
                b.Config.MoveSpeed
                * (
                    s.Incapacitated ? .32f
                    : s.Health < 45 ? .75f
                    : 1f
                )
                * ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(b, s));
            float distance =
                state.Cursor < state.Path.Count ? V2.Distance(s.Position, state.Path[state.Cursor]) : speed * dt;
            V2 desired = direction * Math.Min(distance, speed * dt),
                next;
            if (Locomotion.TryStep(b, s, desired, out next))
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.Facing = direction;
                s.Moving = true;
                s.LastMoved = b.Time;
            }
            else if (b.Time >= state.NextPlan)
            {
                state.Path.Clear();
                state.Cursor = 0;
            }

            s.Crouched = s.Incapacitated;
            s.State = s.Incapacitated ? ActionState.Incapacitated : ActionState.Move;
            s.Reason = s.Incapacitated
                ? "Граната рядом — отползаю из зоны взрыва"
                : "Граната рядом — срочно отбегаю, даже из укрытия";
            return true;
        }
    }
}
