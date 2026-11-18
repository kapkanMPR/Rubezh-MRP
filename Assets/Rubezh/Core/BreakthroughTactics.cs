using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum BreakthroughPhase
    {
        Idle,
        Volley,
        Advance,
        CloseFight,
        Recover
    }

    public sealed class BreakthroughState
    {
        public BreakthroughPhase Phase;
        public float Since, NextAttempt, LastVolley;
        public int Operation, Starts, Completed, Aborted, Shots, RouteRejects, DesiredVolley, MaxVolley;
        public string Status = "Ожидание";
        public V2 Target;
        public ManeuverIntent Intent;
        public readonly Dictionary<int, V2> Goals = new Dictionary<int, V2>();
        public readonly Dictionary<int, int> Targets = new Dictionary<int, int>();
        public readonly HashSet<int> Fired = new HashSet<int>();
        public readonly List<int> Movers = new List<int>();
        public bool Active => Phase == BreakthroughPhase.Volley || Phase == BreakthroughPhase.Advance || Phase == BreakthroughPhase.CloseFight;
    }

    public static class BreakthroughTactics
    {
        static bool Fit(BattleSimulation b, Soldier s, bool allowWall = false) => s.Available && !s.Busy && !s.Broken && !(allowWall && SmokeTactics.WallThrowBusy(b, s) ? CommandPosture.ServiceWithoutHands(b, s) : CommandPosture.Service(b, s)) && s.Health >= 40 && s.Morale >= .3f && s.Suppression < .55f && ConcussionEffects.Current(b, s) < .3f;
        public static bool SystemReady(BattleSimulation b, Soldier s) => Fit(b, s) && !s.Sniper && s.Role != Role.Medic && s.ReloadUntil <= b.Time && (s.Role == Role.Grenadier && s.Rockets > 0 && s.NextSpecial <= b.Time || s.Underbarrel && GrenadePockets.Count(s, 2) > 0 && s.NextOrdnance <= b.Time);
        public static string Unavailable(BattleSimulation b, Squad q)
        {
            if (q.Style != CombatStyle.Aggressive)
                return "Требуется агрессивный стиль";
            int systems = 0, ready = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available)
                {
                    if (s.Role == Role.Grenadier || s.Underbarrel)
                        systems++;
                    if (SystemReady(b, s))
                        ready++;
                }

            if (systems < 2)
                return "Нужно минимум два гранатомётчика, включая подствольники";
            if (ready < 2)
                return "Нет двух готовых систем: проверьте БК и состояние бойцов";
            if (q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count || !Fit(b, b.Soldiers[q.LeaderId]))
                return "Командир не готов";
            return "";
        }

        public static V2 TaskGoal(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if ((q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front))
                return StraightAdvance.Goal(b, s);
            var p = CommandHierarchy.Pair(q, s.Assignment.PairId);
            if (p != null && p.Duty == PairDuty.Reserve)
            {
                V2 sum = new V2();
                int n = 0;
                foreach (var other in q.CommandPlan.Pairs)
                    if (other.Duty == PairDuty.Advance || other.Duty == PairDuty.Maneuver)
                    {
                        sum += other.Goal;
                        n++;
                    }

                if (n > 0)
                    return sum / n;
            }

            return p == null ? b.Map.Objective : p.Goal;
        }

        public static bool DirectionAllowed(BattleSimulation b, Soldier s, V2 point)
        {
            var q = b.Team(s);
            V2 goal = TaskGoal(b, s);
            V2 desired = (goal - s.Position).Normalized, delta = (point - s.Position).Normalized;
            if (desired.Length < .1f || (q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front) && StraightAdvance.Waiting(b, s))
                return false;
            return V2.Dot(desired, delta) > .45f && StraightAdvance.Inside(b, s, point);
        }

        static bool Opportunity(BattleSimulation b, Squad q)
        {
            if (q.Style != CombatStyle.Aggressive || q.CommandTransition.Waiting || q.CapturePlan.Active || b.Time - q.Posture.Since < 4)
                return false;
            // A commander chooses this expensive action only when the accepted task is contested.
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && Fit(b, s) && s.PathCursor >= s.Path.Count && b.Time - s.LastMoved > 3)
                {
                    var p = CommandHierarchy.Pair(q, s.Assignment.PairId);
                    if (p == null || p.Duty == PairDuty.Support || p.Duty == PairDuty.Reserve)
                        continue;
                    foreach (var c in s.Contacts.Values)
                        if (c.Visual && c.Confidence > .7f && b.Time - c.SeenAt < 2.5f && DirectionAllowed(b, s, c.Position))
                            return true;
                }

            return false;
        }

        static Contact TargetFor(BattleSimulation b, Soldier s, HashSet<int> used)
        {
            Contact best = null;
            float score = float.MinValue;
            bool rocket = s.Role == Role.Grenadier && s.Rockets > 0 && s.NextSpecial <= b.Time;
            foreach (var c in s.Contacts.Values)
            {
                if (!DirectionAllowed(b, s, c.Position) || !c.Visual || c.Confidence < .7f || b.Time - c.SeenAt > 2.5f)
                    continue;
                float d = V2.Distance(s.Position, c.Position), r = rocket ? SpecialWeapons.Radius(s) : 2.7f;
                if (d < r + 1.5f || d > (rocket ? SpecialWeapons.Range(b) : ArcedOrdnance.Range(b, 2)))
                    continue;
                var impact = rocket ? b.Map.LauncherImpact(s.Position, c.Position) : ArcedOrdnance.ArcImpact(b, s.Position, c.Position, 2);
                if (V2.Distance(impact, c.Position) > r * .6f || rocket && (!b.Map.LineOfSight(s.Position, c.Position) || !SpecialWeapons.UsefulImpact(b, s, c.Position, impact)))
                    continue;
                bool friendly = false;
                foreach (var u in b.Soldiers)
                    if (u.Side == s.Side && u.Alive && u.Deployed && (V2.Distance(u.Position, impact) < r + 1.2f || u.Moving && V2.Distance(u.OrderAnchor, impact) < r + 1.2f))
                    {
                        friendly = true;
                        break;
                    }

                if (friendly || b.FriendlyInLane(s, c.Position))
                    continue;
                float value = (used.Contains(c.EnemyId) ? 0 : 20) + (6 - c.TargetPriority) - d * .1f;
                if (value > score)
                {
                    best = c;
                    score = value;
                }
            }

            return best;
        }

        public static bool AdvanceRoute(BattleSimulation b, Soldier s, V2 taskGoal, out V2 post, out List<V2> route)
        {
            post = s.Position;
            route = null;
            var full = LossMemory.Find(b, s, TacticalOrders.Open(b, taskGoal), p => PositionLogic.Exposure(b, s, p) * 3 + FireLaneSafety.PathCost(b, s, p));
            b.Stats.Paths++;
            var prefix = new List<V2>();
            V2 prev = s.Position;
            float length = 0;
            foreach (var point in full)
            {
                length += V2.Distance(prev, point);
                prev = point;
                if (length > 6)
                    break;
                prefix.Add(point);
                if (length < 1.2f || !DirectionAllowed(b, s, point) || !StraightAdvance.Inside(b, s, point) || PositionLogic.Occupied(b, s, point) || !PositionLogic.UsefulRoute(b, s, prefix))
                    continue;
                post = point;
                route = new List<V2>(prefix);
            }

            return route != null;
        }

        static void Announce(BattleSimulation b, Squad q, string message, bool urgent)
        {
            q.Breakthrough.Status = message;
            q.PriorityStatus = message;
            q.CommanderOrder = message;
            b.Log(q.Side, message);
            b.Say(b.Soldiers[q.LeaderId], message, urgent: urgent);
        }

        static void Hold(BattleSimulation b, Squad q, Soldier s)
        {
            CommandHierarchy.TransferAssignment(b, s, "Передано подготовке прорыва");
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Position;
            s.Job = s.Id == q.LeaderId ? Job.Command : Job.Cover;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
        }

        public static void End(BattleSimulation b, Squad q, string reason, bool completed = false)
        {
            var st = q.Breakthrough;
            if (!st.Active)
                return;
            if (completed)
                st.Completed++;
            else
                st.Aborted++;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Available && !s.Busy && !CommandPosture.Service(b, s) && (st.Movers.Contains(s.Id) || st.Targets.ContainsKey(s.Id)))
                    Hold(b, q, s);
            st.Phase = BreakthroughPhase.Recover;
            st.Since = b.Time;
            st.NextAttempt = b.Time + 20;
            q.ManeuverIds.Clear();
            q.SupportIds.Clear();
            q.EnvelopmentActive = false;
            q.Phase = ManeuverPhase.Hold;
            q.NextPriorityPlan = b.Time;
            q.PriorityChanged = true;
            Announce(b, q, reason, true);
        }

        public static int RequiredVolley(BattleSimulation b, List<Soldier> units)
        {
            var threats = new Dictionary<int, V2>();
            foreach (var u in units)
                if (SystemReady(b, u))
                    foreach (var c in u.Contacts.Values)
                        if (c.Visual && c.Confidence >= .7f && b.Time - c.SeenAt <= 2.5f && DirectionAllowed(b, u, c.Position) && V2.Distance(u.Position, c.Position) <= (u.Role == Role.Grenadier && u.Rockets > 0 ? SpecialWeapons.Range(b) : ArcedOrdnance.Range(b, 2)))
                            threats[c.EnemyId] = c.Position;
            var sectors = new List<V2>();
            foreach (var point in threats.Values)
            {
                bool covered = false;
                foreach (var sector in sectors)
                    if (V2.Distance(point, sector) <= 4)
                    {
                        covered = true;
                        break;
                    }

                if (!covered)
                    sectors.Add(point);
            }

            return Math.Max(2, Math.Max(sectors.Count, (threats.Count + 1) / 2));
        }

        public static bool Plan(BattleSimulation b, Squad q, List<Soldier> units)
        {
            var st = q.Breakthrough;
            if (st.Active && (q.Style != CombatStyle.Aggressive || q.Intent != st.Intent || q.EmergencyRetreat || q.LeaderId < 0 || q.LeaderId >= b.Soldiers.Count || !Fit(b, b.Soldiers[q.LeaderId], true)))
            {
                End(b, q, "Прорыв отменён. Перегруппировка и прикрытие");
                return false;
            }

            if (!st.Active)
            {
                if (q.Style != CombatStyle.Aggressive || b.Time < st.NextAttempt)
                    return false;
                CommandHierarchy.Begin(b, q);
                if (!Opportunity(b, q))
                    return false;
                string reason = Unavailable(b, q);
                if (reason != "")
                {
                    st.Status = reason;
                    st.NextAttempt = b.Time + 3;
                    return false;
                }

                st.Targets.Clear();
                st.Fired.Clear();
                st.Movers.Clear();
                st.Goals.Clear();
                var used = new HashSet<int>();
                var candidates = new Dictionary<int, Contact>();
                var routes = new Dictionary<int, V2>();
                foreach (var s in units)
                    if (SystemReady(b, s))
                    {
                        var c = TargetFor(b, s, used);
                        if (c != null)
                        {
                            candidates[s.Id] = c;
                            used.Add(c.EnemyId);
                        }
                    }

                if (candidates.Count < 2)
                {
                    st.Status = "Нет двух безопасных секторов для залпа";
                    st.NextAttempt = b.Time + 2;
                    return false;
                }

                // Reserve a real mover BEFORE allocating launchers. Adding a third or fourth
                // launcher must never consume the final fighter who can exploit the volley.
                int reservedMover = -1;
                foreach (var s in units)
                    if (Fit(b, s) && !s.GunSniper && s.Role != Role.Medic && !s.GunMachineGunner && s.Ammo > 0)
                    {
                        var pair = CommandHierarchy.Pair(q, s.Assignment.PairId);
                        if (pair == null || pair.Duty == PairDuty.Support || StraightAdvance.Waiting(b, s))
                            continue;
                        var goal = TaskGoal(b, s);
                        if (V2.Distance(goal, s.Position) < 3)
                            continue;
                        V2 post;
                        List<V2> preview;
                        if (!AdvanceRoute(b, s, goal, out post, out preview))
                        {
                            st.RouteRejects++;
                            continue;
                        }

                        routes[s.Id] = post;
                        if (reservedMover < 0 || candidates.ContainsKey(reservedMover) && !candidates.ContainsKey(s.Id))
                            reservedMover = s.Id;
                    }

                if (reservedMover < 0)
                {
                    st.Status = "Нет готовой штурмовой группы";
                    st.NextAttempt = b.Time + 3;
                    return false;
                }

                candidates.Remove(reservedMover);
                if (candidates.Count < 2)
                {
                    st.Status = "Недостаточно систем после выделения штурмовой группы";
                    st.NextAttempt = b.Time + 3;
                    return false;
                }

                st.DesiredVolley = RequiredVolley(b, units);
                V2 target = new V2();
                used.Clear();
                // Prefer non-assault personnel for the volley, retain extra movers when sufficient.
                for (int pass = 0; pass < 2; pass++)
                    foreach (var u in units)
                    {
                        if (st.Targets.Count >= st.DesiredVolley)
                            break;
                        if (!candidates.ContainsKey(u.Id) || (routes.ContainsKey(u.Id) ? 1 : 0) != pass)
                            continue;
                        var c = TargetFor(b, u, used);
                        if (c == null)
                            continue;
                        st.Targets[u.Id] = c.EnemyId;
                        used.Add(c.EnemyId);
                        target += c.Position;
                    }

                if (st.Targets.Count < 2)
                {
                    st.Targets.Clear();
                    st.NextAttempt = b.Time + 2;
                    return false;
                }

                foreach (var route in routes)
                    if (!st.Targets.ContainsKey(route.Key))
                    {
                        st.Movers.Add(route.Key);
                        st.Goals[route.Key] = route.Value;
                    }

                st.MaxVolley = Math.Max(st.MaxVolley, st.Targets.Count);
                st.Intent = q.Intent;
                st.Target = target / st.Targets.Count;
                st.Operation++;
                st.Starts++;
                st.Phase = BreakthroughPhase.Volley;
                st.Since = b.Time;
                st.LastVolley = -100;
                FireSupportBrain.Reset(b, q);
                q.PlannedSmoke.Clear();
                q.EnvelopmentActive = false;
                q.SupportIds.Clear();
                q.ManeuverIds.Clear();
                q.Phase = ManeuverPhase.Prepare;
                foreach (var s in units)
                    if (st.Movers.Contains(s.Id) || st.Targets.ContainsKey(s.Id))
                    {
                        Hold(b, q, s);
                        s.NextDecision = b.Time;
                    }

                Announce(b, q, "ДАВИМ!!! Гранатомёты — залп, группе приготовиться!", true);
                return true;
            }

            if (st.Active)
                foreach (int id in st.Movers)
                    if (SmokeTactics.WallThrowBusy(b, b.Soldiers[id]))
                        return true;
            if (st.Phase == BreakthroughPhase.Volley)
            {
                foreach (int id in st.Targets.Keys)
                    if (!st.Fired.Contains(id) && !Fit(b, b.Soldiers[id]))
                    {
                        End(b, q, "Залп сорван: гранатомётчик не готов");
                        return false;
                    }

                if (b.Time - st.Since > 7)
                {
                    End(b, q, "Нет согласованного залпа. Удерживать позиции");
                    return false;
                }

                if (st.Fired.Count < st.Targets.Count)
                    return true;
                foreach (var p in b.Projectiles)
                    if (st.Targets.ContainsKey(p.ShooterId))
                        return true;
                int paths = 0;
                int priorRevision = q.Revision;
                q.Revision++;
                SmokeTactics.CarryThroughVolley(q, priorRevision);
                q.ManeuverIds.Clear();
                foreach (int id in st.Movers)
                {
                    var s = b.Soldiers[id];
                    if (!Fit(b, s))
                        continue;
                    V2 taskGoal;
                    if (!st.Goals.TryGetValue(id, out taskGoal))
                        continue;
                    V2 post;
                    List<V2> path;
                    if (!AdvanceRoute(b, s, taskGoal, out post, out path))
                    {
                        st.RouteRejects++;
                        continue;
                    }

                    s.Path = path;
                    s.PathCursor = 0;
                    s.Destination = s.OrderAnchor = post;
                    s.Job = Job.Advance;
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.WasAtAnchor = false;
                    s.Tactical.CommitUntil = b.Time + 3;
                    s.NextDecision = b.Time + 1;
                    q.ManeuverIds.Add(id);
                    paths++;
                }

                if (paths == 0)
                {
                    End(b, q, "Прорыв отменён: нет проходимого маршрута");
                    return false;
                }

                st.Phase = BreakthroughPhase.Advance;
                st.Since = b.Time;
                q.Phase = ManeuverPhase.Move;
                q.PhaseSince = b.Time;
                q.Stage = b.Soldiers[q.ManeuverIds[0]].OrderAnchor;
                SmokePlanner.Plan(b, q, q.Stage);
                Announce(b, q, "Залп прошёл. Вперёд под прикрытием!", true);
                return true;
            }

            int alive = 0, arrived = 0;
            foreach (int id in q.ManeuverIds)
            {
                var s = b.Soldiers[id];
                if (!Fit(b, s))
                    continue;
                alive++;
                if (V2.Distance(s.Position, s.OrderAnchor) < 1.2f)
                    arrived++;
            }

            if (alive == 0 || b.Time - st.Since > 16)
            {
                End(b, q, "Прорыв остановлен. Закрепиться и восстановить группу");
                return false;
            }

            if (st.Phase == BreakthroughPhase.Advance && arrived == alive)
            {
                st.Phase = BreakthroughPhase.CloseFight;
                st.Since = b.Time;
                Announce(b, q, "Рубеж достигнут. Гранаты по контакту, закрепиться!", false);
            }

            if (st.Phase == BreakthroughPhase.CloseFight && b.Time - st.Since > 4)
            {
                End(b, q, "Прорыв завершён. Подтянуть поддержку", true);
                return false;
            }

            return true;
        }

        public static bool OrdnanceAllowed(BattleSimulation b, Soldier s, bool rocket)
        {
            var st = b.Team(s).Breakthrough;
            if (st.Phase != BreakthroughPhase.Volley)
                return true;
            if (!st.Targets.ContainsKey(s.Id) || st.Fired.Contains(s.Id))
                return false;
            bool useRocket = s.Role == Role.Grenadier && s.Rockets > 0 && s.NextSpecial <= b.Time;
            return rocket == useRocket;
        }

        public static bool TargetAllowed(BattleSimulation b, Soldier s, Contact c)
        {
            var st = b.Team(s).Breakthrough;
            return st.Phase != BreakthroughPhase.Volley || st.Targets.TryGetValue(s.Id, out int id) && id == c.EnemyId;
        }

        public static bool Assigned(BattleSimulation b, Soldier s)
        {
            var st = b.Team(s).Breakthrough;
            return st.Phase == BreakthroughPhase.Volley && st.Targets.ContainsKey(s.Id);
        }

        public static void Fired(BattleSimulation b, Soldier s)
        {
            var st = b.Team(s).Breakthrough;
            if (st.Phase == BreakthroughPhase.Volley && st.Targets.ContainsKey(s.Id) && st.Fired.Add(s.Id))
            {
                st.Shots++;
                st.LastVolley = b.Time;
            }
        }

        public static bool BlastWindow(BattleSimulation b, Soldier s)
        {
            var st = b.Team(s).Breakthrough;
            return st.Phase == BreakthroughPhase.Advance && st.Movers.Contains(s.Id) && b.Time - st.Since < 2.5f && s.Suppression < .55f && s.Health >= 40;
        }

        public static bool MovementAllowed(BattleSimulation b, Soldier s)
        {
            if (BlastWindow(b, s))
                return true;
            V2 next = s.PathCursor < s.Path.Count ? s.Path[s.PathCursor] : s.Position;
            if (PositionLogic.Exposure(b, s, next) <= .12f && s.Suppression < .55f)
                return true;
            foreach (var ally in b.Soldiers)
                if (ally != s && b.Team(s).Contains(ally) && WithdrawalTactics.CanCover(b, ally) && ally.PathCursor >= ally.Path.Count && PositionLogic.UsefulSupport(b, ally, s, next))
                    return true;
            return false;
        }

        public static bool SoldierPlan(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            var st = q.Breakthrough;
            if (!st.Active || s.Busy || s.Sniper || s.Role == Role.Medic)
                return false;
            if (!Fit(b, s))
                return false;
            if (st.Phase == BreakthroughPhase.Volley && (st.Targets.ContainsKey(s.Id) || st.Movers.Contains(s.Id)))
            {
                Hold(b, q, s);
                return true;
            }

            if (!st.Movers.Contains(s.Id))
            {
                Hold(b, q, s);
                s.Reason = "Прикрываю решение командира в рамках замысла";
                return true;
            }

            // Existing ordnance API keeps its arming time, trajectory and friendly safety checks.
            if (HandGrenades.Count(s) > 0 && s.NextOrdnance <= b.Time)
                foreach (var c in s.Contacts.Values)
                    if (c.Visual && c.Confidence >= .6f && b.Time - c.SeenAt < 2 && V2.Distance(s.Position, c.Position) > 4 && V2.Distance(s.Position, c.Position) < (GrenadePockets.Count(s, 1) > 0 || GrenadePockets.Count(s, 4) > 0 ? 9 : 8))
                    {
                        bool friendly = false;
                        foreach (var u in b.Soldiers)
                            if (u.Side == s.Side && u.Alive && V2.Distance(u.Position, c.Position) < 3.3f)
                                friendly = true;
                        if (!friendly)
                        {
                            TacticalAI.Stop(s);
                            s.Reason = "Прорыв: граната по близкому контакту";
                            return true;
                        }
                    }

            if (st.Phase == BreakthroughPhase.CloseFight && s.PathCursor >= s.Path.Count)
            {
                // Do not run into our own airborne grenades. Approach only a current visible contact.
                bool pending = false;
                foreach (var p in b.Projectiles)
                    if (b.Soldiers[p.ShooterId].Side == s.Side && V2.Distance(p.To, s.Position) < 10)
                        pending = true;
                if (!pending)
                    foreach (var c in s.Contacts.Values)
                        if (DirectionAllowed(b, s, c.Position) && s.Visible.Contains(c.EnemyId) && c.Visual && b.Time - c.SeenAt < 1 && V2.Distance(s.Position, c.Position) <= 7)
                        {
                            float distance = V2.Distance(s.Position, c.Position);
                            if (distance <= 1.25f)
                                return false;
                            V2 post = c.Position + (s.Position - c.Position).Normalized * 1.1f;
                            if (!b.Map.Walkable(post) || PositionLogic.Occupied(b, s, post))
                                continue;
                            var path = LossMemory.Find(b, s, post, p => PositionLogic.Exposure(b, s, p));
                            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 8)
                                continue;
                            s.Path = path;
                            s.PathCursor = 0;
                            s.OrderAnchor = s.Destination = post;
                            s.Job = Job.Advance;
                            s.MoveAuthorized = true;
                            s.ManeuverPaused = false;
                            s.Reason = "Прорыв: сближаюсь с видимым противником";
                            return true;
                        }
            }

            if (s.PathCursor < s.Path.Count)
            {
                s.MoveAuthorized = MovementAllowed(b, s);
                s.Reason = s.MoveAuthorized ? "Прорыв: короткое продвижение по замыслу" : "Прорыв: ожидаю прикрытие";
                return true;
            }

            return false;
        }
    }
}
