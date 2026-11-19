using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class AreaSuppressionState
    {
        public float Until,
            Next;
        public Contact Source;
        public bool Firing;
        public int BeneficiaryId = -1,
            StartedShots;
    }

    public sealed class ObservedFireLane
    {
        public V2 From,
            To;
        public float Until;
        public int Shooter;
    }

    public sealed class IncomingFireState
    {
        public readonly List<ObservedFireLane> Lanes = new List<ObservedFireLane>();
        public float NextPlan;
    }

    public static class SuppressionPriority
    {
        // TargetPriority is captured by visual perception and copied with reports.
        // Never inspect the live equipment of an unseen enemy during selection.
        public static int Rank(Contact c) =>
            c == null ? 0
            : c.TargetPriority == 1 ? 3
            : c.TargetPriority == 2 ? 2
            : c.TargetPriority == 0 ? 1
            : 0;
    }

    public static class AreaSuppression
    {
        public const float Radius = 2f; // Four-cell wide beaten zone, not a four-cell radius.

        public static bool Enabled(BattleSimulation b, Soldier s) =>
            b.Team(s).Style == CombatStyle.Aggressive
            && (
                GreenMechanics.SuppressEnabled(b, s)
                || s.Side == Side.Red
                    && (b.CampaignLevel == 19 || b.CampaignLevel == 20)
                    && (
                        LastOpponentHunt.IsCover(b, s)
                        || !(
                            s.DefenderGroup != null
                            && s.DefenderGroup.Counterattack.Phase == RedCounterattackPhase.Retreat
                            && s.DefenderGroup.Counterattack.RetreatClear
                        )
                    )
            );

        static bool Eligible(BattleSimulation b, Soldier s) =>
            Enabled(b, s)
            && GreenMechanics.SuppressionTaskReady(b, s)
            && (!LastOpponentHunt.Owns(b, s) || LastOpponentHunt.IsCover(b, s))
            && !TacticalOrders.Emergency(b.Team(s))
            && !WithdrawalTactics.Active(b.Team(s))
            && s.FlagResponseUntil <= b.Time
            && s.HelpRetreatUntil <= b.Time
            && s.Available
            && s.Deployed
            && !s.GrenadeEscape.Active
            && !s.Busy
            && !s.Broken
            && !s.Moving
            && (!s.GunSniper || SniperRetreatCover.Holding(b, s))
            && s.Role != Role.Medic
            && s.PathCursor >= s.Path.Count
            && (s.Job == Job.Cover || s.Job == Job.Hold || s.Job == Job.Command)
            && s.Ammo
                >= (
                    s.GunSniper ? 1
                    : s.GunMachineGunner ? 16
                    : 6
                )
            && s.ReloadUntil <= b.Time
            && s.HandsBusyUntil <= b.Time
            && s.Suppression < .6f
            && s.CounterSniper.Phase == CounterSniperPhase.None;

        static bool NeedsCover(BattleSimulation b, Soldier s)
        {
            if (
                LastOpponentHunt.IsCover(b, s)
                || SniperRetreatCover.Holding(b, s)
                || RedCounterattack.RetreatCover(b, s)
            )
                return true;
            foreach (var mate in b.Soldiers)
                if (
                    mate != s
                    && mate.Side == s.Side
                    && mate.Deployed
                    && mate.Available
                    && V2.Distance(mate.Position, s.Position) < 18
                    && (mate.Moving || mate.PathCursor < mate.Path.Count || mate.HelpRetreatUntil > b.Time)
                )
                    return true;
            return false;
        }

        static bool Safe(BattleSimulation b, Soldier s, Contact c)
        {
            var cone = c.Copy();
            cone.Uncertainty = Math.Max(Radius, c.Uncertainty);
            return CounterSniper.SafeFireLane(b, s, cone);
        }

        static Contact Candidate(BattleSimulation b, Soldier s)
        {
            Contact best = null;
            float score = float.MaxValue;
            foreach (var c in SniperRetreatCover.Sources(b, s))
            {
                float age = b.Time - c.SeenAt,
                    d = V2.Distance(s.Position, c.Position);
                if (
                    !GreenMechanics.RelevantTask(b, s, c)
                    || age > 8
                    || c.Confidence < .35f
                    || c.Uncertainty > (s.GunSniper ? 5 : Radius)
                    || d < 5
                    || d > b.WeaponRange(s) + Radius
                    || !Safe(b, s, c)
                )
                    continue;
                V2 point;
                if (!TryPoint(b, s, c, out point))
                    continue;
                var mover = FireSupportTask.Beneficiary(b, s);
                float taskPenalty = FireSupportTask.Threatens(b, c, mover) ? 0 : 6;
                float value =
                    taskPenalty
                    + age
                    + d * .1f
                    - (s.Side == Side.Green ? SuppressionPriority.Rank(c) * 40 : 0)
                    - (s.GunSniper && b.Time - c.FireSeenAt < 3 ? 2 : 0);
                if (value < score)
                {
                    best = c;
                    score = value;
                }
            }

            return best;
        }

        public static bool Reserved(BattleSimulation b, Soldier s) =>
            Enabled(b, s)
            && s.Available
            && s.Deployed
            && s.AreaSuppression.Until > b.Time
            && s.AreaSuppression.Firing
            && s.Ammo > 0
            && !s.GrenadeEscape.Active
            && (!s.GunSniper || SniperRetreatCover.Holding(b, s))
            && !s.Moving
            && !s.Busy
            && !s.Broken;

        public static bool Active(BattleSimulation b, Soldier s) => Reserved(b, s) && s.AreaSuppression.Source != null;

        public static void Stop(Soldier s)
        {
            GreenMechanics.AreaEnd(s);
            if (s.AreaSuppression.Firing)
                s.BurstRounds = 0;
            s.AreaSuppression.Firing = false;
            s.AreaSuppression.Until = 0;
            s.AreaSuppression.BeneficiaryId = -1;
        }

        public static Contact Select(BattleSimulation b, Soldier s, Contact ordinary)
        {
            var a = s.AreaSuppression;
            if (ordinary != null && ordinary.Visual && !ordinary.Inferred && s.Visible.Contains(ordinary.EnemyId))
            {
                Stop(s);
                if (LastOpponentHunt.IsCover(b, s))
                    s.SuppressiveFire = true;
                return ordinary;
            }

            // A running burst may finish with less than its initial ammunition threshold.
            bool running = Active(b, s);
            if (running && !GreenMechanics.AreaContinue(b, s))
            {
                Stop(s);
                return ordinary;
            }

            if (
                (!running && !Eligible(b, s))
                || running
                    && (
                        !s.Available || s.Busy || s.Broken || s.Moving || s.Suppression >= .6f || s.ReloadUntil > b.Time
                    )
                || !NeedsCover(b, s)
            )
            {
                Stop(s);
                if (LastOpponentHunt.IsCover(b, s))
                    s.SuppressiveFire = true;
                return ordinary;
            }

            if (!running)
            {
                Stop(s);
                if (b.Time < a.Next)
                    return ordinary;
                var c = Candidate(b, s);
                if (c == null)
                    return ordinary;
                foreach (var other in b.Soldiers)
                    if (other != s && other.Side == s.Side && other.DefenderGroup == s.DefenderGroup)
                    {
                        if (s.GunSniper || other.GunSniper)
                            continue; // Independent sniper overwatch does not consume the squad suppressor slot.
                        if (Active(b, other))
                            return ordinary;
                        if (
                            Eligible(b, other)
                            && b.Time >= other.AreaSuppression.Next
                            && (
                                other.GunMachineGunner && !s.GunMachineGunner
                                || other.GunMachineGunner == s.GunMachineGunner && other.Id < s.Id
                            )
                            && Candidate(b, other) != null
                        )
                            return ordinary;
                    }

                var beneficiary = FireSupportTask.Beneficiary(b, s);
                a.BeneficiaryId = beneficiary == null ? -1 : beneficiary.Id;
                a.StartedShots = s.Shots;
                a.Source = c.Copy();
                a.Until = b.Time + 6;
                a.Next = b.Time + (s.GunSniper || LastOpponentHunt.IsCover(b, s) ? 4 : 8);
                a.Firing = true;
                s.BurstRounds = 0;
                GreenMechanics.AreaStart(b, s, beneficiary);
            }

            if (a.BeneficiaryId >= 0 && FireSupportTask.Assigned(b, s) == null)
            {
                a.Next = Math.Min(a.Next, b.Time + 1.5f);
                Stop(s);
                return ordinary;
            }

            foreach (var fresh in SniperRetreatCover.Sources(b, s))
                if (
                    fresh.EnemyId == a.Source.EnemyId
                    && fresh.SeenAt > a.Source.SeenAt
                    && fresh.Confidence >= .35f
                    && V2.Distance(fresh.Position, a.Source.Position) < 4
                )
                    a.Source = fresh.Copy();
            if (
                s.Shots - a.StartedShots
                >= (
                    s.GunSniper ? 2
                    : s.GunMachineGunner ? 24
                    : 9
                )
            )
            {
                a.Next = Math.Max(a.Next, b.Time + 2);
                Stop(s);
                return ordinary;
            }

            if (b.Time - a.Source.SeenAt > 8 || !Safe(b, s, a.Source))
            {
                Stop(s);
                if (LastOpponentHunt.IsCover(b, s))
                    s.SuppressiveFire = true;
                return ordinary;
            }

            V2 point;
            if (TryPoint(b, s, a.Source, out point))
            {
                s.SuppressiveFire = true;
                s.Reason = "Агрессивное подавление: перекрываю переходы, прикрываю своих";
                return new Contact
                {
                    EnemyId = a.Source.EnemyId,
                    Position = point,
                    SeenAt = a.Source.SeenAt,
                    Confidence = a.Source.Confidence,
                    Visual = false,
                    Inferred = true,
                    Reported = true,
                };
            }

            Stop(s);
            return ordinary;
        }

        // Test the actual muzzle/LOS before reserving priority over another covering fighter.
        static bool TryPoint(BattleSimulation b, Soldier s, Contact source, out V2 result)
        {
            V2 axis = (source.Position - s.Position).Normalized,
                side = new V2(-axis.Y, axis.X);
            for (int i = 0; i < 9; i++)
            {
                int index = (s.Shots + i + s.Id) % 9;
                float lateral = (index % 5 - 2) * .85f,
                    depth = (index / 5 == 0 ? -.35f : .35f);
                V2 p = source.Position + side * lateral + axis * depth;
                if (s.GunSniper && source.Uncertainty > Radius)
                {
                    float angle = ((s.Shots + i) % 8) * (float)Math.PI / 4;
                    p =
                        source.Position
                        + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * source.Uncertainty * .7f;
                }

                if (
                    !b.Map.Inside((int)Math.Floor(p.X), (int)Math.Floor(p.Y))
                    || V2.Distance(s.Position, p) > b.WeaponRange(s)
                    || !b.Map.LineOfSight(s.Position, p)
                    || b.FriendlyInLane(s, p)
                )
                    continue;
                V2 muzzle;
                if (!Ballistics.TryMuzzle(b, s, p, out muzzle))
                    continue;
                result = p;
                return true;
            }

            result = s.Position;
            return false;
        }

        public static bool HoldDecision(BattleSimulation b, Soldier s)
        {
            if (!Active(b, s))
                return false;
            if (
                s.GrenadeEscape.Active
                || s.FlagResponseUntil > b.Time
                || s.HelpRetreatUntil > b.Time
                || TacticalOrders.Emergency(b.Team(s))
                || WithdrawalTactics.Active(b.Team(s))
                || s.Suppression >= .6f
                || s.ReloadUntil > b.Time
                || !NeedsCover(b, s)
                || b.Time - s.LastIncoming < 1 && !PositionLogic.Protected(b, s, s.Position)
            )
            {
                Stop(s);
                return false;
            }

            return true;
        }

        public static int BurstSize(BattleSimulation b, Soldier s)
        {
            if (s.GunSniper)
                return 1;
            int normal = s.GunMachineGunner
                ? (
                    b.Team(s).Style == CombatStyle.Passive ? 4
                    : b.Team(s).Style == CombatStyle.Aggressive ? 12
                    : 8
                )
                : 3;
            return normal * (Active(b, s) || LastOpponentHunt.IsCover(b, s) ? 2 : 1);
        }

        public static bool Covers(BattleSimulation b, Soldier s, Contact threat) =>
            Active(b, s)
            && b.Time - s.LastFire < .8f
            && V2.Distance(s.AreaSuppression.Source.Position, threat.Position) <= Radius
            && Safe(b, s, s.AreaSuppression.Source);
    }

    // Reactive knowledge from an actual passing bullet, not access to enemy orders or hidden routes.
    public static class IncomingFire
    {
        public static void Observe(BattleSimulation b, Soldier s, Bullet bullet, V2 from, V2 to)
        {
            if (!bullet.AreaFire || s.Side == bullet.Side)
                return;
            var lanes = s.IncomingFire.Lanes;
            lanes.RemoveAll(x => x.Until <= b.Time);
            var lane = lanes.Find(x => x.Shooter == bullet.ShooterId);
            if (lane == null)
            {
                if (lanes.Count >= 6)
                    lanes.RemoveAt(0);
                lane = new ObservedFireLane { Shooter = bullet.ShooterId };
                lanes.Add(lane);
            }

            lane.From = from;
            lane.To = to + bullet.Direction * 3;
            lane.Until = b.Time + 2.5f;
        }

        public static float Risk(BattleSimulation b, Soldier s, V2 point)
        {
#if RUBEZH_BENCH
            LossMemory.TraceRiskEvaluation?.Invoke(true, point);
#endif
            foreach (var lane in s.IncomingFire.Lanes)
                if (
                    lane.Until > b.Time
                    && V2.SegmentDistance(point, lane.From, lane.To) < 1
                    && b.Map.LineOfSight(point, lane.From)
                )
                    return 1;
            return 0;
        }

        // Pure permission shared by planning and concrete candidate-step checks.
        internal static bool IgnoresRisk(Soldier s) => s.GrenadeEscape.Active || s.Incapacitated;

        public static bool StepAllowed(BattleSimulation b, Soldier s, V2 from, V2 step)
        {
            if (IgnoresRisk(s))
                return true;
            V2 next = from + step.Normalized * Math.Max(.65f, step.Length);
            return Risk(b, s, next) <= Risk(b, s, from); // A fighter inside a lane can leave it.
        }

        public static bool AllowStep(BattleSimulation b, Soldier s, V2 step)
        {
            if (StepAllowed(b, s, s.Position, step))
                return true;
            if (b.Time >= s.IncomingFire.NextPlan)
            {
                s.IncomingFire.NextPlan = b.Time + 1.5f;
                if (!s.Busy && s.CounterSniper.Phase == CounterSniperPhase.None)
                {
                    s.Path = LossMemory.Find(b, s, s.Destination, p => PositionLogic.Exposure(b, s, p) * 3);
                    s.PathCursor = 0;
                    b.Stats.Paths++;
                    s.NextDecision = b.Time;
                    TacticalOrders.TrySmoke(b, s, TacticalOrders.ClosestKnown(b, s));
                }
            }

            s.Reason = "Переход простреливается: жду паузу или ищу обход";
            s.Moving = false;
            return false;
        }
    }
}
