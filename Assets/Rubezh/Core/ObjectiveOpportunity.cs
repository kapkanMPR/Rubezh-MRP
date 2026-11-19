using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class ObjectiveCapturePlan
    {
        public bool Active,
            Recon;
        public V2 Goal,
            LastPosition;
        public float ProgressAt,
            ContinueUntil;
        public float QuietSince;
        public V2 QuietCenter;
        public bool QuietInitialized;
        public int ReconStarts,
            ReconCompleted,
            ReconAborted;
        public int Capturer = -1;
        public readonly List<int> Guards = new List<int>(3);
        public float Started,
            NextReview,
            RetryAt;
        internal List<V2> OwnedRoute;
        internal int OwnedRevision;
        internal readonly Dictionary<int, int> GuardRevisions = new Dictionary<int, int>();
        internal readonly Dictionary<int, List<V2>> GuardRoutes = new Dictionary<int, List<V2>>();
        internal readonly Dictionary<int, V2> GuardAnchors = new Dictionary<int, V2>();

        public void Clear()
        {
            Active = false;
            Recon = false;
            Capturer = -1;
            Guards.Clear();
            OwnedRoute = null;
            GuardRoutes.Clear();
            GuardRevisions.Clear();
            GuardAnchors.Clear();
            Started = NextReview = 0;
        }
    }

    // Opportunistic goal-taking: one mobile fighter captures, the others hold fire.
    // Decisions use the squad's reports only; no access to concealed red positions.
    public static class ObjectiveOpportunity
    {
        const float ContactAge = 10f;

        public static bool IsCapturer(BattleSimulation b, Soldier s)
        {
            return OwnsRoute(b.Team(s).CapturePlan, s);
        }

        internal static bool OwnsRoute(ObjectiveCapturePlan p, Soldier s)
        {
            return p.Active
                && p.Capturer == s.Id
                && p.OwnedRevision == s.OrderRevision
                && ReferenceEquals(p.OwnedRoute, s.Path)
                && V2.Distance(p.Goal, s.OrderAnchor) < .01f;
        }

        static bool OwnsGuard(ObjectiveCapturePlan p, Soldier s)
        {
            return p.GuardRoutes.TryGetValue(s.Id, out var route)
                && ReferenceEquals(route, s.Path)
                && s.Path.Count == 0
                && p.GuardRevisions.TryGetValue(s.Id, out var revision)
                && revision == s.OrderRevision
                && p.GuardAnchors.TryGetValue(s.Id, out var anchor)
                && V2.Distance(anchor, s.OrderAnchor) < .01f
                && s.Job == Job.Cover;
        }

        internal static bool RouteCovered(
            BattleSimulation b,
            Soldier s,
            List<V2> route,
            int cursor,
            bool recon,
            List<int> guards
        )
        {
            if (recon && b.Time - s.LastIncoming > 5 && s.Suppression < .3f && PathSafe(b, s, route, true, cursor))
                return true;
            if (
                guards.Count == 0
                && b.Alive(Side.Green) == 1
                && b.Time - s.LastIncoming > 8
                && PositionLogic.Exposure(b, s, s.Position) <= .12f
            )
                return true;
            var next = cursor < route.Count ? route[cursor] : s.OrderAnchor;
            foreach (int id in guards)
                if (id >= 0 && id < b.Soldiers.Count && PositionLogic.UsefulSupport(b, b.Soldiers[id], s, next))
                    return true;
            return false;
        }

        public static bool Covered(BattleSimulation b, Soldier s)
        {
            var p = b.Team(s).CapturePlan;
            for (int i = p.Guards.Count - 1; i >= 0; i--)
            {
                int id = p.Guards[i];
                if (id < 0 || id >= b.Soldiers.Count || !OwnsGuard(p, b.Soldiers[id]))
                {
                    p.Guards.RemoveAt(i);
                    p.GuardRoutes.Remove(id);
                    p.GuardRevisions.Remove(id);
                    p.GuardAnchors.Remove(id);
                }
            }

            return OwnsRoute(p, s)
                && RouteCovered(b, s, s.Path, Math.Min(s.PathCursor, s.Path.Count), p.Recon, p.Guards);
        }

        public static bool MovementAllowed(BattleSimulation b, Soldier s)
        {
            return OwnsRoute(b.Team(s).CapturePlan, s) && (Covered(b, s) || TacticalAI.SafeApproach(b, s));
        }

        static bool ThreatNearFlag(BattleSimulation b)
        {
            foreach (var c in b.Green.Contacts.Values)
                if (PositionLogic.Recent(b, c) && V2.Distance(c.Position, b.Map.Objective) < 7f)
                    return true;
            return false;
        }

        static bool PathSafe(BattleSimulation b, Soldier runner, List<V2> path, bool? recon = null, int start = 0)
        {
            float risk = 0;
            bool probing = recon ?? b.Green.CapturePlan.Recon;
            // A balanced/attacking squad cannot demand a perfectly covered route forever.
            // After a prolonged quiet deadlock the commander accepts one short probe;
            // incoming fire still cancels it and ordinary movement/collision rules apply.
            float quietAge = b.Time - b.Green.CapturePlan.QuietSince;
            float budget =
                quietAge >= 90 ? 4f
                : quietAge >= 45 ? 2f
                : .25f;
            float limit = probing ? Math.Max(budget, PositionLogic.Exposure(b, runner, runner.Position) + .1f) : 1.5f;
            for (int i = start; i < path.Count; i++)
            {
                var p = path[i];
                risk = Math.Max(risk, PositionLogic.Exposure(b, runner, p));
                if (risk > limit)
                    return false;
                foreach (var c in b.Green.Contacts.Values)
                    if (
                        PositionLogic.Recent(b, c)
                        && V2.Distance(p, c.Position) < 4.5f
                        && b.Map.LineOfSight(p, c.Position)
                        && b.SmokeTransmission(p, c.Position) > .35f
                    )
                        return false;
            }

            return true;
        }

        static Soldier Candidate(BattleSimulation b, List<V2> pathOut, List<int> guardsOut, bool recon)
        {
            Soldier best = null;
            float bestScore = float.MinValue;
            foreach (var s in b.Soldiers)
            {
                if (
                    s.Side != Side.Green
                    || !s.Deployed
                    || !s.Available
                    || s.Broken
                    || s.Busy
                    || s.HandsBusyUntil > b.Time
                    || s.GrenadeEscape.Active
                    || s.Suppression > .52f
                    || s.Morale < .38f
                    || s.Health < (recon ? 21f : 45f)
                    || s.ReloadUntil > b.Time
                    || s.RescueTargetId >= 0
                    || s.SupplyDonorId >= 0
                    || s.SupplyReceiverId >= 0
                    || s.PriorityMovement.Transfer
                    || s.LootTargetId >= 0
                    || s.LootGroundId >= 0
                    || s.LauncherPickupId >= 0
                    || s.WeaponAidTargetId >= 0
                    || !recon && s.Ammo + s.Reserve <= 0
                )
                    continue;
                float distance = V2.Distance(s.Position, b.Map.Objective);
                if (distance < 1.6f || !recon && distance > 17f)
                    continue;
                var path = LossMemory.Find(
                    b,
                    s,
                    b.Map.Objective,
                    p => PositionLogic.Exposure(b, s, p) * 12f + (PositionLogic.Occupied(b, s, p) ? 12 : 0)
                );
                b.Stats.Paths++;
                if (recon && path.Count > 0)
                {
                    var leg = new List<V2>();
                    float length = 0;
                    V2 last = s.Position;
                    foreach (var point in path)
                    {
                        length += V2.Distance(last, point);
                        if (length > (b.Time - b.Green.CapturePlan.QuietSince >= 45 ? 3f : 6f))
                            break;
                        leg.Add(point);
                        last = point;
                    }

                    path = leg;
                }

                if (
                    path.Count == 0
                    || SupportAI.PathLength(s.Position, path) > distance * 1.7f + 4
                    || !PathSafe(b, s, path, recon)
                )
                    continue;
                var guards = SelectGuards(b, s);
                if (guards.Count == 0 && !recon)
                {
                    bool solo = b.Alive(Side.Green) == 1 && distance <= 8 && b.Time - s.LastIncoming > 8;
                    foreach (var point in path)
                        if (PositionLogic.Exposure(b, s, point) > .12f)
                            solo = false;
                    if (!solo)
                        continue;
                }

                if (
                    !recon && !MedicTactics.AllowRoute(b, s, path)
                    || (!RouteCovered(b, s, path, 0, recon, guards) && !TacticalAI.SafeApproachTo(b, s, path[0]))
                )
                    continue;
                float score = -(distance * 1.3f) + s.Health * .055f + s.Morale * 3f - s.Suppression * 8f;
                if (s.Id == b.Green.LeaderId)
                    score -= 5f;
                if (s.Sniper || s.Role == Role.Medic)
                    score -= 12f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = s;
                    pathOut.Clear();
                    pathOut.AddRange(path);
                    guardsOut.Clear();
                    guardsOut.AddRange(guards);
                }
            }

            return best;
        }

        static List<int> SelectGuards(BattleSimulation b, Soldier mover)
        {
            var guards = new List<int>(2);
            var scored = new List<KeyValuePair<float, int>>();
            foreach (var g in b.Soldiers)
                if (
                    g.Side == Side.Green
                    && g.Id != mover.Id
                    && PositionLogic.Able(g, b)
                    && g.RescueTargetId < 0
                    && g.SupplyDonorId < 0
                )
                {
                    float gap = V2.Distance(g.Position, b.Map.Objective);
                    if (gap > 24)
                        continue;
                    bool lane =
                        b.Map.LineOfSight(g.Position, b.Map.Objective)
                        && b.SmokeTransmission(g.Position, b.Map.Objective) > .3f;
                    bool firing = PositionLogic.HasSector(b, g, g.Position);
                    if (!lane && !firing)
                        continue;
                    scored.Add(new KeyValuePair<float, int>((lane ? 10 : 0) + (firing ? 8 : 0) - gap * .22f, g.Id));
                }

            scored.Sort((x, y) => y.Key.CompareTo(x.Key));
            for (int i = 0; i < Math.Min(2, scored.Count); i++)
                guards.Add(scored[i].Value);
            return guards;
        }

        // The flag is known terrain; an unseen enemy is never queried here.
        public static bool QuietObjective(BattleSimulation b, Squad q)
        {
            if (q.Side != Side.Green || (q.Style != CombatStyle.Balanced && q.Style != CombatStyle.Aggressive))
                return false;
            var p = q.CapturePlan;
            V2 center = new V2();
            int count = 0;
            bool fighting = false;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && s.Deployed)
                {
                    center += s.Position;
                    count++;
                    // A distant sighting is not combat; a medic's short shuffle is not
                    // advancement of the squad. Actual fire and incoming fire take priority.
                    fighting |= b.Time - s.LastIncoming < 15 || b.Time - s.LastFire < 15;
                }
            if (count == 0)
                return false;
            center /= count;
            if (!p.QuietInitialized || fighting || V2.Distance(center, p.QuietCenter) > 2f)
            {
                p.QuietInitialized = true;
                p.QuietCenter = center;
                p.QuietSince = b.Time;
            }
            return !fighting && (p.ContinueUntil > b.Time || b.Time - p.QuietSince >= 25f);
        }

        public static bool Plan(BattleSimulation b, Squad q)
        {
            bool quiet = QuietObjective(b, q) || q.CapturePlan.Active && q.CapturePlan.Recon;
            if (
                q.Side != Side.Green
                || !quiet && (q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front)
                || q.Style == CombatStyle.Withdraw
                || q.Style == CombatStyle.Passive
                || TacticalOrders.Emergency(q)
                || b.Outcome != Outcome.Running
                || q.Breakthrough.Active
            )
            {
                Cancel(b, q, "Задача захвата уступила приоритет");
                return false;
            }

            var plan = q.CapturePlan;
            if (!quiet && ThreatNearFlag(b))
            {
                if (plan.Active)
                    Cancel(b, q, "Противник снова перекрыл точку");
                return false;
            }

            if (plan.Active)
            {
                if (plan.Capturer < 0 || plan.Capturer >= b.Soldiers.Count)
                {
                    Cancel(b, q, "Потерян назначенный боец");
                    return false;
                }

                var s = b.Soldiers[plan.Capturer];
                if (!OwnsRoute(plan, s) || !s.Available || s.Broken || s.Suppression > .74f || s.Health < 28f)
                {
                    Cancel(b, q, "Захватчик не может продолжать");
                    return false;
                }

                if (plan.Recon)
                {
                    if (V2.Distance(s.Position, plan.LastPosition) > .3f)
                    {
                        plan.ProgressAt = b.Time;
                        plan.LastPosition = s.Position;
                    }

                    if (b.Time - s.LastIncoming < 3 || s.Suppression > .45f)
                    {
                        Cancel(b, q, "Разведывательный переход попал под огонь");
                        return false;
                    }

                    if (V2.Distance(s.Position, plan.Goal) < .6f)
                    {
                        plan.ReconCompleted++;
                        plan.Clear();
                        plan.ContinueUntil = b.Time + 6;
                        plan.RetryAt = b.Time + 1;
                        return false;
                    }

                    if (b.Time - plan.ProgressAt > 6)
                    {
                        Cancel(b, q, "Переход к флагу не продвигается");
                        return false;
                    }
                }

                if (b.Time - plan.Started > 25f && V2.Distance(s.Position, b.Map.Objective) > 3f)
                {
                    Cancel(b, q, "Не удаётся достичь точки");
                    return false;
                }

                if (b.Time >= plan.NextReview)
                {
                    plan.NextReview = b.Time + .8f;
                    if (
                        !PathSafe(b, s, s.Path, plan.Recon, Math.Min(s.PathCursor, s.Path.Count))
                        || !MovementAllowed(b, s)
                    )
                    {
                        Cancel(b, q, "Маршрут или прикрытие больше не подходят");
                        return false;
                    }
                }

                q.Plan = q.Execution =
                    plan.Guards.Count == 0
                        ? "Захват видимой свободной точки последним бойцом"
                        : "Захват свободного флага; остальные прикрывают";
                return true;
            }

            if (
                b.Time < plan.RetryAt
                || b.Time < 8f
                || !b.Map.Walkable(b.Map.Objective)
                || b.Capture >= 1f
                || b.Alive(Side.Green) < 1
            )
                return false;
            // Only act once the enemy has been engaged/pushed off. No omniscient test for
            // live reds outside perception: a quiet frontline alone is not proof.
            bool hadContact = b.Time - q.LastContact < ContactAge;
            foreach (var c in q.Contacts.Values)
                if (PositionLogic.Remembered(b, c))
                    hadContact = true;
            if (!hadContact)
            {
                foreach (var observer in b.Soldiers)
                    if (
                        q.Contains(observer)
                        && observer.Available
                        && observer.Deployed
                        && DirectionalVision.InView(b, observer, b.Map.Objective)
                        && b.Map.LineOfSight(observer.Position, b.Map.Objective)
                        && b.SmokeTransmission(observer.Position, b.Map.Objective) > .7f
                    )
                    {
                        hadContact = true;
                        break;
                    }
            }

            if (!hadContact && !quiet)
                return false;
            var route = new List<V2>();
            var guards = new List<int>(2);
            Soldier mover = Candidate(b, route, guards, quiet);
            if (mover == null)
            {
                plan.RetryAt = b.Time + 3f;
                return false;
            }

            plan.Recon = quiet;
            plan.Goal = route[route.Count - 1];
            plan.LastPosition = mover.Position;
            plan.ProgressAt = b.Time;
            if (quiet)
                plan.ReconStarts++;
            plan.Active = true;
            plan.Capturer = mover.Id;
            plan.Started = b.Time;
            plan.NextReview = b.Time + 3f;
            plan.Guards.Clear();
            plan.Guards.AddRange(guards);
            // Existing roles/resources remain, only the assignment changes.
            mover.Path = route;
            plan.OwnedRoute = route;
            plan.OwnedRevision = mover.OrderRevision;
            mover.PathCursor = 0;
            mover.Destination = mover.OrderAnchor = plan.Goal;
            mover.WasAtAnchor = false;
            mover.Job = Job.Reposition;
            mover.MoveAuthorized = true;
            mover.ManeuverPaused = false;
            mover.Order = plan.Recon ? "Задача — флаг. Проверяю следующий участок обхода" : "Захватить свободную точку";
            mover.Reason =
                plan.Guards.Count == 0 ? "Видимая точка рядом, путь проверен" : "Путь открыт, товарищи прикрывают";
            mover.NextDecision = b.Time;
            CommandHierarchy.TransferAssignment(b, mover, "Передача задачи захвату точки");
            foreach (int id in plan.Guards)
            {
                var g = b.Soldiers[id];
                TacticalAI.Stop(g);
                g.OrderAnchor = g.Position;
                g.Job = Job.Cover;
                g.MoveAuthorized = true;
                g.ManeuverPaused = false;
                g.Order = "Прикрывать захват флага";
                plan.GuardRoutes[id] = g.Path;
                plan.GuardRevisions[id] = g.OrderRevision;
                plan.GuardAnchors[id] = g.OrderAnchor;
                g.NextDecision = b.Time;
                CommandHierarchy.TransferAssignment(b, g, "Передача задачи прикрытию захвата");
            }

            q.Plan = q.Execution =
                plan.Guards.Count == 0
                    ? "Последний боец занимает свободную точку"
                    : "Один берёт флаг, остальные прикрывают";
            b.Log(
                q.Side,
                plan.Recon ? "Контакт потерян, задача прежняя — флаг. Продвигаемся по проверяемому обходу."
                    : plan.Guards.Count == 0 ? "Точка рядом. Занимаю её!"
                    : "Путь к точке открыт. Один на флаг, остальные прикрывают!"
            );
            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var p = b.Team(s).CapturePlan;
            if (!p.Active || s.Side != Side.Green)
                return false;
            if (s.Id == p.Capturer)
            {
                if (!OwnsRoute(p, s))
                {
                    Cancel(b, b.Team(s), "Маршрут захвата заменён другой задачей");
                    return false;
                }

                if (!s.Available || s.Broken)
                    return false;
                if (
                    p.Recon
                    && V2.Distance(s.Position, p.Goal) < .6f
                    && V2.Distance(s.Position, b.Map.Objective) >= 1.9f
                )
                {
                    p.ReconCompleted++;
                    TacticalAI.Stop(s);
                    p.Clear();
                    p.ContinueUntil = b.Time + 6;
                    p.RetryAt = b.Time + 1;
                    s.NextDecision = b.Time + 1;
                    return true;
                }

                if (V2.Distance(s.Position, b.Map.Objective) < 1.9f)
                {
                    if (s.PathCursor < s.Path.Count)
                        TacticalAI.Stop(s);
                    s.Job = Job.Hold;
                    s.MoveAuthorized = true;
                    s.Reason = "Захватываю точку";
                }
                else
                {
                    if (s.PathCursor >= s.Path.Count)
                    {
                        Cancel(b, b.Green, "Захватчику нужен новый маршрут");
                        return false;
                    }

                    s.Job = Job.Reposition;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.Reason =
                        p.Guards.Count == 0
                            ? "Перемещаюсь к видимой свободной точке"
                            : "Перемещаюсь к флагу под прикрытием";
                }

                return true;
            }

            if (p.Guards.Contains(s.Id))
            {
                if (!PositionLogic.Able(s, b) || !OwnsGuard(p, s))
                {
                    p.Guards.Remove(s.Id);
                    p.GuardRoutes.Remove(s.Id);
                    p.GuardRevisions.Remove(s.Id);
                    p.GuardAnchors.Remove(s.Id);
                    return false;
                }

                s.OrderAnchor = s.Position;
                s.Job = Job.Cover;
                s.MoveAuthorized = true;
                s.Reason = "Прикрываю захват флага";
                return true;
            }

            return false;
        }

        public static void Cancel(BattleSimulation b, Squad q, string why)
        {
            var plan = q.CapturePlan;
            if (!plan.Active)
            {
                plan.Clear();
                return;
            }

            if (plan.Recon)
                plan.ReconAborted++;
            plan.ContinueUntil = 0;
            int capturer = plan.Capturer;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && (s.Id == capturer || plan.Guards.Contains(s.Id)))
                {
                    if (s.Id == capturer ? !OwnsRoute(plan, s) : !OwnsGuard(plan, s))
                        continue;
                    if (s.Id == capturer)
                    {
                        TacticalAI.Stop(s);
                        s.OrderAnchor = s.Position;
                    }

                    s.NextDecision = b.Time;
                    s.Job = Job.Hold;
                    s.Order = "Возвращаюсь к исходной тактике";
                }

            plan.Clear();
            plan.RetryAt = b.Time + 4f;
            b.Log(q.Side, "Захват точки отменён: " + why);
        }
    }
}
