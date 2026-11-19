using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class WallTravel
    {
        static bool Wall(BattleSimulation b, V2 p) =>
            b.Map.Inside((int)Math.Floor(p.X), (int)Math.Floor(p.Y)) && b.Map.At(p) == Tile.Wall;

        static bool ClearKnown(BattleSimulation b, Soldier s, V2 p)
        {
            foreach (var c in Knowledge.Contacts(b, s))
                if (
                    c.Confidence >= .25f
                    && b.Time - c.SeenAt <= 25
                    && PositionLogic.Relevant(b, c, p)
                    && b.Map.LineOfSight(c.Position, p)
                    && b.SmokeTransmission(c.Position, p) > .3f
                )
                    return false;
            return true;
        }

        public static bool Safe(BattleSimulation b, Soldier s, V2 goal)
        {
            if (!CombatTraining.Skill(s, 2, "wall"))
                return false;
            V2 delta = goal - s.Position;
            if (delta.Length < .05f)
                return false;
            if (delta.Length > 12 || !Locomotion.WalkableSegment(b, s.Position, goal))
                return false;
            foreach (var side in new[] { new V2(1, 0), new V2(-1, 0), new V2(0, 1), new V2(0, -1) })
            {
                if (Math.Abs(V2.Dot(side, delta.Normalized)) > .25f || !Wall(b, s.Position + side))
                    continue;
                bool ok = true;
                int n = Math.Max(1, (int)Math.Ceiling(delta.Length * 2));
                for (int i = 0; i <= n; i++)
                {
                    var p = s.Position + delta * (i / (float)n);
                    if (!Wall(b, p + side) || !ClearKnown(b, s, p))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                    return true;
            }

            return false;
        }

        public static bool TryRoute(BattleSimulation b, Soldier s, List<V2> longPath, out List<V2> route)
        {
            route = null;
            var prefix = new List<V2>();
            V2 prev = s.Position;
            float length = 0;
            foreach (var point in longPath)
            {
                length += V2.Distance(prev, point);
                if (length > 12 || !Safe(b, s, point))
                    break;
                prefix.Add(point);
                prev = point;
            }

            if (prefix.Count == 0 || SupportAI.PathLength(s.Position, prefix) < 1.2f)
                return false;
            route = prefix;
            return true;
        }

        public static void Mark(BattleSimulation b, Soldier s, V2 goal)
        {
            s.Motion.WallGoal = goal;
            s.Motion.WallUntil = b.Time + 20;
            s.Motion.WallRevision = b.Map.Revision;
            s.Motion.WallBounds++;
        }

        public static bool Active(BattleSimulation b, Soldier s)
        {
            if (!CombatTraining.Skill(s, 2, "wall"))
                return false;
            var m = s.Motion;
            if (
                m.WallUntil <= b.Time
                || m.WallRevision != b.Map.Revision
                || !MotionCombat.HasRoute(s)
                || V2.Distance(s.Destination, m.WallGoal) > .2f
            )
                return false;
            if (!ClearKnown(b, s, s.Position) || !ClearKnown(b, s, s.Path[s.PathCursor]))
            {
                return false;
            }

            return true;
        }
    }
}
