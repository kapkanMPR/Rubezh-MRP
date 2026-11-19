using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Tracks physical progress independently of LastMoved (route planners also write it).
    public sealed class ProgressMemory
    {
        public bool Initialized;
        public V2 Position,
            FailedGoal,
            PreviousOrigin;
        public float AvoidReturnUntil;
        public int OriginRevision;
        public float LastActivity,
            ProgressAt,
            NextReview,
            Until,
            NextSafety,
            FailedUntil;
        public int Attempts,
            Started,
            Finished,
            Aborted,
            NoRoute,
            ConsecutiveFailures;
        public bool Owned;
        public int Revision;
    }

    public static class ProgressRecovery
    {
        public const float IdleDelay = 8f;

        public static bool Active(BattleSimulation b, Soldier s)
        {
            return s.Progress.Until > b.Time && s.PathCursor < s.Path.Count && Eligible(b, s);
        }

        static bool Eligible(BattleSimulation b, Soldier s)
        {
            return PositionLogic.ShelterEligible(b, s)
                && (!s.Sniper || SniperTactics.MobileLead(b, s))
                && s.Ammo + s.Reserve > 0
                && s.LootTargetId < 0
                && s.LootGroundId < 0
                && s.WeaponAidTargetId < 0
                && s.LauncherPickupId < 0
                && !s.PriorityMovement.Transfer
                && !AreaSuppression.Reserved(b, s);
        }

        public static void Track(BattleSimulation b, Soldier s)
        {
            var m = s.Progress;
            if (!m.Initialized || V2.Distance(m.Position, s.Position) > .4f)
            {
                m.Initialized = true;
                m.Position = s.Position;
                m.LastActivity = m.ProgressAt = b.Time;
            }

            if (b.Time - s.LastFire < 2f)
                m.LastActivity = m.ProgressAt = b.Time;
        }

        public static void Cancel(BattleSimulation b, Soldier s)
        {
            var m = s.Progress;
            if (m.Until > 0)
                m.Aborted++;
            m.Until = 0;
            m.Owned = false;
            m.NextReview = b.Time + 3;
        }

        // A finite recovery move may cross safe open ground, never an unchecked firing lane.
        public static bool SafeRoute(BattleSimulation b, Soldier s, List<V2> path, int cursor = 0)
        {
            V2 from = s.Position;
            float initial = PositionLogic.Exposure(b, s, from),
                length = 0;
            for (int k = cursor; k < path.Count; k++)
            {
                V2 to = path[k];
                if (s.Role == Role.Medic && !MedicTactics.AllowSegment(b, s, from, to))
                    return false;
                float d = V2.Distance(from, to);
                length += d;
                if (length > 9)
                    return false;
                int steps = Math.Max(1, (int)Math.Ceiling(d * 2));
                for (int i = 1; i <= steps; i++)
                {
                    var p = from + (to - from) * (i / (float)steps);
                    float exposure = PositionLogic.Exposure(b, s, p);
                    if (exposure > Math.Max(.32f, initial + .05f))
                        return false;
                    foreach (var c in Knowledge.Contacts(b, s))
                        if (
                            PositionLogic.Recent(b, c)
                            && V2.Distance(p, c.Position) < 6
                            && V2.Distance(p, c.Position) < V2.Distance(s.Position, c.Position) - .5f
                            && b.Map.LineOfSight(p, c.Position)
                        )
                            return false;
                }

                from = to;
            }

            return true;
        }

        public static bool WithinAssignment(BattleSimulation b, Soldier s, V2 point)
        {
            if (
                !CommandPosture.Inside(b, s, point)
                || !StraightAdvance.Inside(b, s, point)
                || !CommandHierarchy.WithinManeuver(b, s, point)
            )
                return false;
            var g = s.DefenderGroup;
            if (g != null && (!g.Attack || g.Counterattack.Phase == RedCounterattackPhase.Defense))
            {
                V2 home =
                    g.Pullback || g.Counterattack.Phase == RedCounterattackPhase.Defense ? b.Map.Objective : g.Spawn;
                if (V2.Distance(point, home) > Math.Max(Math.Max(3, g.Radius), V2.Distance(s.Position, home) - .5f))
                    return false;
            }

            var q = b.Team(s);
            if (TacticalOrders.Flanks(q) && s.BuddyId >= 0 && s.BuddyId < b.Soldiers.Count && s.BuddyId != s.Id)
            {
                var buddy = b.Soldiers[s.BuddyId];
                if (
                    buddy.Available
                    && buddy.Deployed
                    && V2.Distance(point, buddy.Position) > Math.Max(7, V2.Distance(s.Position, buddy.Position) - .5f)
                )
                    return false;
            }

            return true;
        }

        static bool TeamIdle(BattleSimulation b, Soldier mover)
        {
            var q = b.Team(mover);
            if (
                q.Side != Side.Green
                || q.Style != CombatStyle.Balanced && q.Style != CombatStyle.Aggressive
                || q.CommandTransition.Waiting
                || b.Capture > 0
                || b.Time < 30
            )
                return false;
            foreach (var u in b.Soldiers)
                if (q.Contains(u) && u.Deployed && u.Available)
                {
                    if (
                        u.Busy
                        || u.Broken
                        || u.Suppression > .45f
                        || b.Time - u.LastIncoming < 8
                        || b.Time - u.LastFire < 40
                        || b.Time - u.Progress.LastActivity < 40
                        || ExecutionReview.WorkingSector(b, u)
                    )
                        return false;
                }

            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var m = s.Progress;
            if (!Eligible(b, s))
            {
                if (m.Until > 0)
                    Cancel(b, s);
                return false;
            }

            if (m.Until > 0)
            {
                if (s.PathCursor >= s.Path.Count)
                {
                    m.Finished++;
                    m.Owned = false;
                    m.Until = 0;
                    m.ProgressAt = b.Time;
                    m.NextReview = b.Time + 4;
                    return false;
                }

                if (b.Time >= m.Until || b.Time - m.ProgressAt > 3.5f)
                {
                    m.FailedGoal = s.Destination;
                    m.FailedUntil = b.Time + 15;
                    Cancel(b, s);
                    TacticalAI.Stop(s);
                    s.OrderAnchor = s.Position;
                    CommandHierarchy.Failed(b, s, "Безопасное смещение не выполнено");
                    return false;
                }

                if (b.Time >= m.NextSafety)
                {
                    m.NextSafety = b.Time + 1;
                    if (s.Suppression > .6f || !SafeRoute(b, s, s.Path, s.PathCursor))
                    {
                        Cancel(b, s);
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                        return false;
                    }
                }

                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                s.Reason = "Выполняю короткое безопасное смещение после пересмотра задачи";
                return true;
            }

            // Do not steal a physically progressing route merely because its owner has not fired.
            if (s.PathCursor < s.Path.Count && b.Time - m.ProgressAt < 3.5f)
                return false;
            if (
                b.Time < m.NextReview
                || b.Time - m.ProgressAt < IdleDelay
                || b.Time - s.LastFire < IdleDelay
                || s.ReloadUntil > b.Time
                || s.Suppression > .45f
                || s.Morale < .4f
                || b.Time - s.LastIncoming < 4
                || ExecutionReview.WorkingSector(b, s)
            )
                return false;
            m.NextReview = b.Time + 4 + (s.Id % 3) * .2f;
            m.Attempts++;
            var threat = TacticalOrders.ClosestKnown(b, s);
            var q = b.Team(s);
            if (
                threat == null
                && (s.Side != Side.Green || q.Style == CombatStyle.Passive || q.Style == CombatStyle.Withdraw)
            )
                return false;
            if (StraightAdvance.Waiting(b, s) || s.Sniper && !TeamIdle(b, s))
                return false;
            foreach (var other in b.Soldiers)
                if (other != s && q.Contains(other) && other.Progress.Owned && Active(b, other))
                    return false;
            // Preserve valid overwatch while another member is actually moving.
            foreach (var ally in b.Soldiers)
                if (
                    ally != s
                    && b.Team(s).Contains(ally)
                    && ally.Available
                    && ally.Moving
                    && PositionLogic.UsefulSupport(b, s, ally, ally.Destination)
                )
                    return false;
            var candidates = new List<KeyValuePair<float, V2>>();
            var seen = new HashSet<int>();
            var duty = CommandHierarchy.Pair(q, s.Assignment.PairId);
            bool commanded = duty != null && CommandHierarchy.Owns(q);
            bool anchorDuty =
                commanded
                && (duty.Duty == PairDuty.Reserve || duty.Duty == PairDuty.Guard || duty.Duty == PairDuty.Support);
            bool stalled = (s.Sniper || m.ConsecutiveFailures >= 2) && TeamIdle(b, s);
            if (
                anchorDuty
                && !stalled
                && V2.Distance(s.Position, duty.Goal) < 4
                && PositionLogic.Protected(b, s, s.Position)
            )
                return false;
            var wanted = anchorDuty ? duty.Goal : s.OrderAnchor;
            if (!anchorDuty && V2.Distance(wanted, s.Position) < 1)
                wanted = threat != null ? threat.Position : b.Map.Objective;
            if (threat == null && s.Assignment.PairId >= 0)
            {
                var pair = CommandHierarchy.Pair(q, s.Assignment.PairId);
                if (
                    pair != null
                    && (pair.Duty == PairDuty.Advance || pair.Duty == PairDuty.Maneuver)
                    && V2.Distance(pair.Goal, s.Position) > 2
                )
                    wanted = pair.Goal;
            }

            if (stalled)
            {
                // A covered pair can be waiting on a bound that nobody can start. Follow the
                // actual safe prefix towards the objective instead of retrying its stale staging cell.
                var path = LossMemory.Find(
                    b,
                    s,
                    TacticalOrders.Open(b, b.Map.Objective),
                    p =>
                        PositionLogic.Exposure(b, s, p) * 5
                        + FireLaneSafety.PathCost(b, s, p)
                        + (PositionLogic.Occupied(b, s, p) ? 16 : 0)
                );
                b.Stats.Paths++;
                var prefix = new List<V2>();
                float length = 0;
                var from = s.Position;
                foreach (var p in path)
                {
                    length += V2.Distance(from, p);
                    if (length > 5 || !WithinAssignment(b, s, p) || !MedicTactics.AllowSegment(b, s, from, p))
                        break;
                    prefix.Add(p);
                    from = p;
                }

                while (prefix.Count > 0)
                {
                    var end = prefix[prefix.Count - 1];
                    if (
                        V2.Distance(end, s.Position) >= 1
                        && !PositionLogic.Occupied(b, s, end)
                        && SafeRoute(b, s, prefix)
                    )
                    {
                        s.Path = prefix;
                        s.PathCursor = 0;
                        s.OrderAnchor = s.Destination = end;
                        s.Job = Job.Reposition;
                        s.MoveAuthorized = true;
                        s.ManeuverPaused = false;
                        s.SupportReady = false;
                        s.WasAtAnchor = false;
                        m.PreviousOrigin = s.Position;
                        m.OriginRevision = q.Revision;
                        m.AvoidReturnUntil = b.Time + 15;
                        m.Owned = true;
                        m.Revision = q.Revision;
                        m.Until = b.Time + 8;
                        m.NextSafety = b.Time + 1;
                        m.ProgressAt = b.Time;
                        m.Started++;
                        m.ConsecutiveFailures = 0;
                        s.Reason = "Отряд остановился: проверяю короткий проход к цели";
                        return true;
                    }

                    prefix.RemoveAt(prefix.Count - 1);
                }
            }

            if (s.Sniper)
                return false; // no safe prefix: retain overwatch
            var toGoal = LossMemory.Find(
                b,
                s,
                TacticalOrders.Open(b, wanted),
                p => PositionLogic.Exposure(b, s, p) * 5
            );
            b.Stats.Paths++;
            // A reachable waypoint around a wall is better than an unreachable Euclidean goal.
            V2 guide = wanted;
            float distance = 0;
            V2 prev = s.Position;
            foreach (var p in toGoal)
            {
                distance += V2.Distance(prev, p);
                guide = p;
                prev = p;
                if (distance >= 4)
                    break;
            }

            for (int y = -5; y <= 5; y++)
            for (int x = -5; x <= 5; x++)
            {
                if (x * x + y * y > 25 || x * x + y * y < 2)
                    continue;
                var p = new V2((int)s.Position.X + x + .5f, (int)s.Position.Y + y + .5f);
                if (
                    !b.Map.Walkable(p)
                    || !MedicTactics.AllowStep(b, s, p)
                    || !WithinAssignment(b, s, p)
                    || PositionLogic.Occupied(b, s, p)
                    || m.FailedUntil > b.Time && V2.Distance(p, m.FailedGoal) < 1.3f
                )
                    continue;
                if (
                    anchorDuty && V2.Distance(p, duty.Goal) > Math.Max(3.5f, V2.Distance(s.Position, duty.Goal))
                    || m.OriginRevision == q.Revision
                        && b.Time < m.AvoidReturnUntil
                        && V2.Distance(p, m.PreviousOrigin) < 1.25f
                )
                    continue;
                bool sector = PositionLogic.HasSector(b, s, p),
                    cover = PositionLogic.Protected(b, s, p);
                float risk = PositionLogic.Exposure(b, s, p);
                if (
                    risk > .32f
                    || (!sector || PositionLogic.HasSector(b, s, s.Position))
                        && V2.Distance(p, guide) >= V2.Distance(s.Position, guide) - .7f
                )
                    continue;
                float score =
                    (sector ? 10 : 0)
                    + (cover ? 2 : 0)
                    - V2.Distance(p, guide) * 1.5f
                    - V2.Distance(p, s.Position) * .3f
                    - risk * 8
                    - FormationEnvelope.LaneCost(b, s, p);
                candidates.Add(new KeyValuePair<float, V2>(score, p));
            }

            candidates.Sort((a, z) => z.Key.CompareTo(a.Key));
            int budget = 5;
            foreach (var candidate in candidates)
            {
                var path = LossMemory.Find(
                    b,
                    s,
                    candidate.Value,
                    p =>
                        PositionLogic.Exposure(b, s, p) * 5
                        + FireLaneSafety.PathCost(b, s, p)
                        + (PositionLogic.Occupied(b, s, p) ? 20 : 0)
                );
                b.Stats.Paths++;
                if (path.Count > 0 && SafeRoute(b, s, path))
                {
                    s.Path = path;
                    s.PathCursor = 0;
                    s.OrderAnchor = s.Destination = candidate.Value;
                    s.Job = Job.Reposition;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.SupportReady = false;
                    s.WasAtAnchor = false;
                    m.PreviousOrigin = s.Position;
                    m.OriginRevision = q.Revision;
                    m.AvoidReturnUntil = b.Time + 15;
                    m.Owned = true;
                    m.Revision = q.Revision;
                    m.Until = b.Time + 8;
                    m.NextSafety = b.Time + 1;
                    m.ProgressAt = b.Time;
                    m.Started++;
                    m.ConsecutiveFailures = 0;
                    s.LastMoved = b.Time;
                    s.Reason = "Прежняя задача не даёт результата — короткое безопасное смещение";
                    b.Diagnostics.Add(b, s, "replan", "Progress", s.Reason);
                    return true;
                }

                if (--budget <= 0)
                    break;
            }

            m.NoRoute++;
            m.ConsecutiveFailures++;
            m.NextReview = b.Time + Math.Min(16, 4 + m.ConsecutiveFailures * 2);
            s.Tactical.Block = "Пересмотр задачи: безопасный маршрут пока не найден";
            CommandHierarchy.Failed(b, s, "Безопасный маршрут не найден");
            return false;
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            var m = s.Progress;
            if (!m.Owned)
                return false;
            if (
                !Active(b, s)
                || m.Revision != b.Team(s).Revision
                || s.Suppression > .6f
                || s.Broken
                || s.Busy
                || s.GrenadeEscape.Active
            )
            {
                Cancel(b, s);
                return false;
            }

            if (!SafeRoute(b, s, s.Path, s.PathCursor))
            {
                TacticalAI.Stop(s);
                Cancel(b, s);
                return false;
            }

            Locomotion.AdvanceWaypoint(b, s);
            if (s.PathCursor < s.Path.Count)
            {
                var d = s.Path[s.PathCursor] - s.Position;
                V2 next;
                if (
                    Locomotion.TryStep(
                        b,
                        s,
                        d.Normalized * Math.Min(d.Length, CombatMovement.Speed(b, s) * dt),
                        out next
                    )
                )
                {
                    s.DistanceTravelled += V2.Distance(s.Position, next);
                    s.Position = next;
                    s.Moving = true;
                    s.LastMoved = b.Time;
                    s.Facing = d.Normalized;
                    if (V2.Distance(next, s.Path[s.PathCursor]) < .05f)
                        s.PathCursor++;
                }
                else
                {
                    s.Moving = false;
                    s.Reason = "Безопасное смещение: проход временно занят";
                }
            }

            Locomotion.AdvanceWaypoint(b, s);
            if (s.PathCursor >= s.Path.Count)
            {
                m.Finished++;
                m.Until = 0;
                m.Owned = false;
                m.NextReview = b.Time + 4;
                s.Moving = false;
                s.NextDecision = 0;
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }
    }
}
