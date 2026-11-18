using System;

namespace SilentOrders.Core
{
    // A short local spacing correction, not a new flank order or a substitute for aid.
    public static class OpenGroundSpacing
    {
        public static bool Enabled(BattleSimulation b, Soldier s) => !b.Historical01225 && s.Side == Side.Green;
        public static bool WorkPriority(BattleSimulation b, Soldier s) => s.RocketAimTarget >= 0 || s.OrdnanceAimTarget >= 0 || s.SupportReady || s.SuppressiveFire || s.FireLaneUntil > b.Time || b.Time - s.LastFire < 1.2f;
        static bool Free(BattleSimulation b, Soldier s) => !WorkPriority(b, s) && s.Available && s.Deployed && !s.Busy && !s.Broken && !s.Bleeding && s.Suppression < .7f && s.ReloadUntil <= b.Time && s.HandsBusyUntil <= b.Time && !CommandPosture.Service(b, s) && !PairExchange.Active(b, s) && !s.GrenadeEscape.Active && s.TrafficYieldUntil <= b.Time;
        static float Clearance(BattleSimulation b, Soldier s, V2 p)
        {
            float d = 10;
            foreach (var u in b.Soldiers)
                if (u != s && u.Side == s.Side && u.Alive && u.Deployed)
                    d = Math.Min(d, V2.Distance(p, u.Position));
            return d;
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            if (!Enabled(b, s))
                return false;
            var m = s.Motion;
            if (ShoulderPair.KeepClose(b, s) || !Free(b, s) || b.Map.NearCover(s.Position) || MotionCombat.HasRoute(s))
            {
                m.SpreadUntil = 0;
                return false;
            }

            if (m.SpreadUntil <= b.Time)
            {
                if (b.Time < m.NextSpread)
                    return false;
                m.NextSpread = b.Time + 2;
                Soldier neighbour = null;
                float distance = 1.6f;
                foreach (var u in b.Soldiers)
                    if (u != s && u.Side == s.Side && u.Alive && u.Deployed && (!Free(b, u) || s.Id > u.Id))
                    {
                        float d = V2.Distance(s.Position, u.Position);
                        if (d < distance)
                        {
                            distance = d;
                            neighbour = u;
                        }
                    }

                if (neighbour == null)
                    return false;
                V2 best = s.Position;
                float score = float.MinValue;
                var threat = TacticalOrders.ClosestKnown(b, s);
                for (int i = 0; i < 24; i++)
                {
                    double a = (i % 12) * Math.PI / 6;
                    float range = i < 12 ? 1.2f : 2f;
                    V2 p = s.Position + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * range;
                    if (!b.Map.Walkable(p) || !CommandPosture.Inside(b, s, p) || !Locomotion.WalkableSegment(b, s.Position, p) || PositionLogic.Occupied(b, s, p))
                        continue;
                    float space = Clearance(b, s, p);
                    if (space < 1.85f)
                        continue;
                    // Do not advance toward a known threat merely to make space. Exposure is a ranking,
                    // not a veto that allows two soldiers to remain a single target in the open.
                    if (threat != null && V2.Distance(p, threat.Position) < V2.Distance(s.Position, threat.Position) - .35f)
                        continue;
                    float value = Math.Min(space, 2.4f) * 3 - range - PositionLogic.Exposure(b, s, p) * 2;
                    if (value > score)
                    {
                        score = value;
                        best = p;
                    }
                }

                if (score == float.MinValue)
                    return false;
                ShoulderPair.Release(b, s);
                m.SpreadGoal = best;
                m.SpreadUntil = b.Time + 3;
                m.OpenSpreads++;
            }

            V2 delta = m.SpreadGoal - s.Position;
            if (delta.Length < .12f || Clearance(b, s, s.Position) >= 2.05f)
            {
                m.SpreadUntil = 0;
                m.NextSpread = b.Time + 3;
                return false;
            }

            V2 next;
            if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt), out next))
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.LastMoved = b.Time;
                s.Moving = true;
                s.State = ActionState.Move;
                s.RocketAimTarget = -1;
                s.SupportReady = false;
                s.Reason = "Открытое место: увеличиваю интервал, не прячусь за товарищем";
            }

            return true;
        }
    }
}
