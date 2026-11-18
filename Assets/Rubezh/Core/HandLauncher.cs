using System;

namespace SilentOrders.Core
{
    public static class HandLauncher
    {
        public static bool Active(Soldier s) => s.Mechanics.HandLauncher && s.Rockets > 0;
        public static float ExtraScatter(Soldier s) => s.Role == Role.Grenadier || s.Role == Role.Commander ? 0f : 2f;
        public static void Release(BattleSimulation b, Soldier s)
        {
            if (!s.Mechanics.HandLauncher)
                return;
            if (s.Rockets > 0)
            {
                LauncherLogistics.DropLoaded(b, s);
                s.Rockets = 0;
            }

            s.Mechanics.HandLauncher = false;
            s.RocketAimTarget = -1;
            TacticalAI.Stop(s);
            s.NextDecision = b.Time;
            s.Mechanics.RecoveryNext = b.Time + 12;
        }

        public static bool Update(BattleSimulation b, Soldier s, float dt)
        {
            if (!s.Mechanics.HandLauncher)
                return false;
            if (s.Rockets == 0)
            {
                s.Mechanics.HandLauncher = false;
                s.NextDecision = b.Time;
                return false;
            }

            if (!s.Available || s.Broken || s.Bleeding || s.Suppression > .65f || b.Time > s.Mechanics.HandUntil || b.Team(s).Revision != s.Mechanics.HandRevision || TacticalOrders.Emergency(b.Team(s)) || s.RescueTargetId >= 0 || s.RescuerId >= 0)
            {
                Release(b, s);
                return false;
            }

            if (b.Time < s.HandsBusyUntil)
                return true;
            s.SupportReady = false;
            if (s.PathCursor >= s.Path.Count && SpecialWeapons.TryLaunch(b, s))
                return true;
            if (b.Time >= s.Mechanics.HandNextPlan)
            {
                s.Mechanics.HandNextPlan = b.Time + 2;
                V2 best = s.Position;
                float bestScore = float.MaxValue;
                foreach (var c in s.Contacts.Values)
                {
                    if (!c.Visual || c.Confidence < .7f || b.Time - c.SeenAt > 2.5f)
                        continue;
                    for (int i = 0; i < 17; i++)
                    {
                        float a = i * (float)Math.PI / 8;
                        V2 p = i == 16 ? s.Position : s.Position + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * (i < 8 ? 2 : 4);
                        float d = V2.Distance(p, c.Position);
                        if (d < 7 || d > SpecialWeapons.Range(b) || !CommandPosture.Inside(b, s, p) || !StraightAdvance.Inside(b, s, p) || !b.Map.LineOfSight(p, c.Position) || !Locomotion.WalkableSegment(b, s.Position, p) || PositionLogic.Occupied(b, s, p))
                            continue;
                        float risk = 0;
                        for (int k = 0; k <= 4; k++)
                            risk = Math.Max(risk, PositionLogic.Exposure(b, s, s.Position + (p - s.Position) * (k / 4f)));
                        if (risk > .6f)
                            continue;
                        float score = V2.Distance(p, s.Position) + risk * 8;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = p;
                        }
                    }
                }

                s.Destination = s.OrderAnchor = best;
                s.Path.Clear();
                s.PathCursor = 0;
            }

            s.Reason = "Поднял — выстрелил: выхожу на рубеж";
            s.State = ActionState.Observe;
            V2 delta = s.Destination - s.Position, next;
            if (delta.Length > .2f && dt > 0 && Locomotion.WalkableSegment(b, s.Position, s.Destination) && Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt), out next))
            {
                float moved = V2.Distance(s.Position, next);
                s.Position = next;
                s.DistanceTravelled += moved;
                s.Moving = moved > .00001f;
                if (s.Moving)
                    s.LastMoved = b.Time;
                s.State = ActionState.Move;
            }

            return true;
        }
    }
}
