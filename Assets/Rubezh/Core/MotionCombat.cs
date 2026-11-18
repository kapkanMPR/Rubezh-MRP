using System;

namespace SilentOrders.Core
{
    public sealed class MotionState
    {
        public float SpreadUntil, NextSpread;
        public V2 SpreadGoal;
        public int OpenSpreads;
        public bool WasMoving, Fast, ExemptStep;
        public V2 Previous, Direction;
        public float StepAt = -100, FastSince = -1, RecoveryUntil, RecoveryDuration, NextSmoke;
        public V2 ShoulderGoal;
        public float ShoulderApproachUntil;
        public int ShoulderApproaches, ApproachFront = -1;
        public float GroupWaitUntil, MobileSmokeUntil, NextFast;
        public float WallUntil, PairUntil, NextPair;
        public V2 WallGoal;
        public int WallRevision = -1, FrontId = -1, RearId = -1;
        public int RetreatDetours;
        public int GroupPeak;
        public double TripleSeconds;
        public int MovingShots, FastRuns, MovingSmokes, WallBounds, ShoulderShots, ExtraBounds;
        public double MovingSeconds, FastSeconds, SideSeconds, BackSeconds;
    }

    public static class MotionCombat
    {
        public static bool HasRoute(Soldier s) => s.PathCursor < s.Path.Count;
        public static bool InMotion(BattleSimulation b, Soldier s) => s.Moving || (s.Motion.WasMoving && b.Time - s.LastMoved <= b.Config.Tick * 2.1f);
        public static bool WantsFast(BattleSimulation b, Soldier s)
        {
            if (b.Time < s.Motion.NextFast || !HasRoute(s) || !s.Available || s.Broken || s.Busy || s.ReloadUntil > b.Time || s.Suppression > .65f || s.Health < 35 || ShoulderPair.Linked(b, s))
                return false;
            if (ContactMovement.FireMove(b, s))
                return false;
            if (TacticalMarch.Quiet(b, b.Team(s)))
                return false;
            if (b.Time < s.Awareness.ContactSprintUntil)
                return true;
            if (WallTravel.Active(b, s))
                return false;
            return (SmokeTactics.AssaultActive(b, s) || s.Assignment.Active && s.Assignment.IsBound || RedCounterattack.Retreating(b, s) || LastOpponentHunt.Sprint(b, s) || SnailMovement.Sprint(b, s)) && V2.Distance(s.Position, s.Destination) > 2 && (!PositionLogic.Protected(b, s, s.Position) || SnailMovement.Sprint(b, s));
        }

        static void FinishSprint(BattleSimulation b, MotionState m)
        {
            if (m.FastSince >= 0)
            {
                m.RecoveryDuration = Util.Clamp(b.Time - m.FastSince, 1, 3);
                m.RecoveryUntil = b.Time + m.RecoveryDuration;
                m.FastSince = -1;
            }
        }

        public static void Begin(BattleSimulation b, Soldier s)
        {
            var m = s.Motion;
            m.ExemptStep = !GroupMovement.Ordinary(b, s);
            m.WasMoving = s.Moving;
            m.Previous = s.Position;
            m.StepAt = b.Time;
            bool fast = WantsFast(b, s);
            if (m.Fast && !fast)
                FinishSprint(b, m);
            m.Fast = fast;
        }

        public static void End(BattleSimulation b, Soldier s, float dt)
        {
            var m = s.Motion;
            V2 delta = s.Position - m.Previous;
            if (delta.Length > .00001f)
            {
                m.Direction = delta.Normalized;
                m.MovingSeconds += dt;
                if (m.Fast)
                {
                    if (m.FastSince < 0)
                    {
                        m.FastSince = b.Time - dt;
                        m.FastRuns++;
                    }

                    m.FastSeconds += dt;
                }

                float dot = V2.Dot(m.Direction, (s.Awareness.Initialized ? s.Awareness.Look : s.Facing).Normalized);
                if (dot < -.5f)
                    m.BackSeconds += dt;
                else if (dot < .5f)
                    m.SideSeconds += dt;
            }
            else if (m.Fast)
            {
                m.NextFast = b.Time + .8f;
                m.Fast = false;
                FinishSprint(b, m);
            }
        }

        public static float DirectionMultiplier(V2 direction, V2 look)
        {
            if (direction.Length < .01f || look.Length < .01f)
                return 1;
            float dot = Util.Clamp(V2.Dot(direction.Normalized, look.Normalized), -1, 1);
            return dot >= 0 ? .85f + .15f * dot : .85f + .20f * dot;
        }

        public static float SpeedMultiplier(BattleSimulation b, Soldier s)
        {
            if (TacticalMarch.Quiet(b, b.Team(s)))
                return .7f * DirectionMultiplier(HasRoute(s) ? s.Path[s.PathCursor] - s.Position : s.Motion.Direction, s.Awareness.Look);
            if (s.Motion.Fast)
                return 1.2f;
            var direction = HasRoute(s) ? s.Path[s.PathCursor] - s.Position : s.Motion.Direction;
            return DirectionMultiplier(direction, s.Awareness.Initialized ? s.Awareness.Look : s.Facing);
        }

        public static bool CanFire(BattleSimulation b, Soldier s) => (!InMotion(b, s) || CombatTraining.Skill(s, 3, "moving")) && !PairExchange.Active(b, s) && !CombatCrouch.Standing(b, s) && !s.Motion.Fast && (!InMotion(b, s) || !s.GunSniper);
        public static float AimTravelDot(Soldier s, V2 point)
        {
            V2 direction = s.Motion.Direction;
            if (HasRoute(s) && (s.Path[s.PathCursor] - s.Position).Length > .01f)
                direction = s.Path[s.PathCursor] - s.Position;
            return direction.Length < .01f ? 1 : V2.Dot(direction.Normalized, (point - s.Position).Normalized);
        }

        public static bool ForwardAction(BattleSimulation b, Soldier s, V2 point) => !InMotion(b, s) || AimTravelDot(s, point) >= -.0001f;
        public static float SideAccuracy(BattleSimulation b, Soldier s, V2 point) => InMotion(b, s) && AimTravelDot(s, point) < .7071068f ? .85f : 1f;
        public static bool SafeFire(BattleSimulation b, Soldier s, V2 point)
        {
            if (!ForwardAction(b, s, point))
                return false;
            if (!InMotion(b, s))
                return true;
            V2 dir = (point - s.Position).Normalized;
            float range = V2.Distance(s.Position, point);
            foreach (var ally in b.Soldiers)
                if (ally != s && ally.Side == s.Side && ally.Alive && ally.Deployed)
                {
                    if (ShoulderPair.CanShootOver(b, s, ally, point))
                        continue;
                    V2 next = ally.Position;
                    if (HasRoute(ally) && ally.MoveAuthorized)
                    {
                        float budget = b.Config.MoveSpeed * 1.2f * Math.Min(.75f, range / 70f + .1f);
                        for (int i = ally.PathCursor; i < ally.Path.Count && budget > 0; i++)
                        {
                            V2 d = ally.Path[i] - next;
                            float step = Math.Min(d.Length, budget);
                            next += d.Normalized * step;
                            budget -= step;
                        }
                    }

                    foreach (var p in new[]
                    {
                        ally.Position,
                        next
                    }

                    )
                    {
                        float along = V2.Dot(p - s.Position, dir);
                        if (along <= .2f || along >= range + 1)
                            continue;
                        float width = .45f + 1.2f * Util.Clamp(along / Math.Max(1, range), 0, 1);
                        if (V2.SegmentDistance(p, s.Position, point) < width)
                            return false;
                    }
                }

            return true;
        }

        public static float Accuracy(BattleSimulation b, Soldier s)
        {
            float factor = InMotion(b, s) ? (s.GunMachineGunner ? .55f : .7f) : 1;
            var m = s.Motion;
            if (m.RecoveryUntil > b.Time)
                factor *= 1 - .25f * Util.Clamp((m.RecoveryUntil - b.Time) / Math.Max(1, m.RecoveryDuration), 0, 1);
            return factor;
        }

        public static float SmokeRange(BattleSimulation b, Soldier s) => 9 + (s.Side == Side.Green && s.Role == Role.Rifleman ? 1 : 0) + (InMotion(b, s) ? 1 : 0);
        public static void Smoke(BattleSimulation b, Soldier s)
        {
            if (!InMotion(b, s) || !HasRoute(s) || b.Time < s.Motion.NextSmoke || b.Time - s.LastIncoming > 1.5f)
                return;
            s.Motion.NextSmoke = b.Time + 2;
            SmokeTactics.Request(b, b.Team(s), s, s.Destination, SmokePurpose.Route, s);
        }

        public static float Recommended(Squad q) => q.Style == CombatStyle.Aggressive ? 8 : 6;
        public static float Maximum(Squad q) => Recommended(q) + 4;
    }
}
