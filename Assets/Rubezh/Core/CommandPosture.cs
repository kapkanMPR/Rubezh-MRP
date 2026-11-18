using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class PosturePlan
    {
        public bool Initialized;
        public CombatStyle Style;
        public float Since, NextReview;
        public int Changes, LocalMoves, Holds, RejectedDrift, ForwardBounds, Returns, BlockedMoves;
        public string Reason = "";
        public readonly Dictionary<int, V2> Anchors = new Dictionary<int, V2>();
    }

    // The order constrains autonomous movement. It is not a global cancellation of aid or safety.
    public static class CommandPosture
    {
        public static void Sync(BattleSimulation b, Squad q)
        {
            var p = q.Posture;
            if (p.Initialized && p.Style == q.Style)
                return;
            p.Initialized = true;
            p.Style = q.Style;
            p.Since = b.Time;
            p.Changes++;
            p.Anchors.Clear();
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Alive && s.Deployed)
                    p.Anchors[s.Id] = s.Position;
            p.Reason = q.Style == CombatStyle.Passive ? "Закрепиться на занимаемом рубеже" : q.Style == CombatStyle.Withdraw ? "Последовательный отход всей группы" : q.Style == CombatStyle.Aggressive ? "Продолжать наступление в заданном направлении" : "Продвижение с сохранением рабочих позиций";
        }

        public static V2 Anchor(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            Sync(b, q);
            V2 point;
            if (!q.Posture.Anchors.TryGetValue(s.Id, out point))
                q.Posture.Anchors[s.Id] = point = s.Position;
            return point;
        }

        public static bool Inside(BattleSimulation b, Soldier s, V2 point)
        {
            var q = b.Team(s);
            // Allow initial, bounded covered deployment to a wide line. Afterwards passive stance holds.
            if (q.Intent == ManeuverIntent.Front && q.Style == CombatStyle.Passive && q.Straight.Active && b.Time - q.Straight.Since < 22f)
                return true;
            return q.Style != CombatStyle.Passive || V2.Distance(Anchor(b, s), point) <= 3.5f;
        }

        public static bool Service(BattleSimulation b, Soldier s) => ServiceWithoutHands(b, s) || s.HandsBusyUntil > b.Time;
        public static bool ServiceWithoutHands(BattleSimulation b, Soldier s)
        {
            return s.Busy || s.Bleeding || s.Broken || s.GrenadeEscape.Active || TacticalOrders.ServiceActive(s) || s.HelpUntil > b.Time || s.HelpRetreatUntil > b.Time || s.VehicleRally || s.VehicleDisperse;
        }

        public static bool ForwardNeeded(BattleSimulation b, Soldier s, V2 goal, bool assignedOnly)
        {
            var q = b.Team(s);
            if (q.Style != CombatStyle.Aggressive || q.CommandTransition.Waiting || !CommandHierarchy.Owns(q) || s.Sniper || Service(b, s))
                return false;
            var p = CommandHierarchy.Pair(q, s.Assignment.PairId);
            if (p == null || p.Duty != PairDuty.Advance && p.Duty != PairDuty.Maneuver || assignedOnly && p.Mover != s.Id)
                return false;
            return V2.Distance(s.Position, p.Goal) > 5 && V2.Distance(goal, p.Goal) < V2.Distance(s.Position, p.Goal) - .5f;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (q.Style != CombatStyle.Passive || !s.Available || Service(b, s))
                return false;
            Sync(b, q);
            var anchor = Anchor(b, s);
            // Passive + Front: spread only through safe positions; unlike aggression, do not cross hostile open ground.
            if (q.Intent == ManeuverIntent.Front && !Service(b, s) && s.Suppression < .30f && s.Morale > .55f && b.Time - s.LastIncoming > 4f)
            {
                StraightAdvance.Sync(b, q);
                if (b.Time - q.Straight.Since < 20f && s.PathCursor >= s.Path.Count && b.Time >= s.Tactical.NextUsefulReview)
                {
                    s.Tactical.NextUsefulReview = b.Time + 3.5f;
                    float lane;
                    var plan = q.Straight;
                    if (plan.Lanes.TryGetValue(s.Id, out lane))
                    {
                        var lateral = new V2(-plan.Axis.Y, plan.Axis.X);
                        float own = V2.Dot(s.Position - plan.Origin, plan.Axis);
                        float delta = lane - V2.Dot(s.Position - plan.Origin, lateral);
                        var wanted = TacticalOrders.Open(b, s.Position + lateral * Util.Clamp(delta, -6f, 6f));
                        if (Math.Abs(delta) > 2.5f && TacticalOrders.Route(b, s, wanted, true, false))
                        {
                            s.Job = Job.Reposition;
                            s.Reason = "Фронтом: занимаю безопасный участок широкой линии";
                            return true;
                        }
                    }
                }
            }

            // Medical planning is still allowed to obtain a real service owner.
            // Actual aid is scheduled by SupportAI before ordinary decisions; free medics obey this line too.
            if (s.PathCursor < s.Path.Count && s.Progress.Initialized && b.Time - s.Progress.ProgressAt > 8)
            {
                s.Assignment.FailedGoal = s.Destination;
                s.Assignment.FailedUntil = b.Time + 10;
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
                CommandHierarchy.ReleaseAssignment(b, s, "Остановлен застрявший маршрут закрепления", false);
                s.Tactical.NextUsefulReview = b.Time + 4;
                q.Posture.BlockedMoves++;
            }

            if (s.PathCursor < s.Path.Count)
            {
                if ((Inside(b, s, s.Destination) || V2.Distance(s.Destination, anchor) < V2.Distance(s.Position, anchor) - .5f) && SupportAI.PathLength(s.Position, s.Path.GetRange(s.PathCursor, s.Path.Count - s.PathCursor)) <= 6)
                {
                    s.MoveAuthorized = true;
                    return true;
                }

                // Finish an actual safety escape, even if it leaves the retained line.
                if (PositionLogic.OwnsShelter(s) && s.Tactical.ShelterUntil > b.Time && b.Time - s.LastIncoming < 3)
                    return true;
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Destination = s.Position;
                CommandHierarchy.ReleaseAssignment(b, s, "Остановлен выход за удерживаемый рубеж", false);
                q.Posture.RejectedDrift++;
            }

            if (b.Time >= s.Tactical.NextUsefulReview)
            {
                s.Tactical.NextUsefulReview = b.Time + 4;
                if (V2.Distance(s.Position, anchor) > 4 && b.Time - s.LastIncoming > 4)
                {
                    var full = LossMemory.Find(b, s, TacticalOrders.Open(b, anchor), v => PositionLogic.Exposure(b, s, v) * 5 + (PositionLogic.Occupied(b, s, v) ? 15 : 0));
                    b.Stats.Paths++;
                    var shortPath = new List<V2>();
                    V2 previous = s.Position;
                    float length = 0;
                    foreach (var point in full)
                    {
                        length += V2.Distance(previous, point);
                        if (length > 4.5f)
                            break;
                        shortPath.Add(point);
                        previous = point;
                    }

                    if (shortPath.Count > 0 && !(s.Assignment.FailedUntil > b.Time && V2.Distance(previous, s.Assignment.FailedGoal) < 1.2f) && V2.Distance(previous, anchor) < V2.Distance(s.Position, anchor) - .5f && ProgressRecovery.SafeRoute(b, s, shortPath))
                    {
                        s.Path = shortPath;
                        s.PathCursor = 0;
                        s.Destination = s.OrderAnchor = previous;
                        s.Job = Job.Reposition;
                        s.MoveAuthorized = true;
                        s.ManeuverPaused = false;
                        s.LastMoved = b.Time;
                        CommandHierarchy.TransferAssignment(b, s, "Передано возвращению на удерживаемый рубеж");
                        q.Posture.Returns++;
                        s.Reason = "Закрепление: возвращаюсь к своему рубежу после срочной задачи";
                        return true;
                    }
                }

                bool crowded = PositionLogic.Occupied(b, s, s.Position);
                bool currentCover = PositionLogic.Protected(b, s, s.Position), currentSector = ExecutionReview.WorkingSector(b, s);
                if (!currentCover || crowded || !currentSector)
                {
                    V2 best = s.Position;
                    List<V2> route = null;
                    float bestScore = (currentCover ? 4 : 0) + (currentSector ? 6 : 0);
                    for (int y = -3; y <= 3; y++)
                        for (int x = -3; x <= 3; x++)
                        {
                            var v = new V2((int)anchor.X + x + .5f, (int)anchor.Y + y + .5f);
                            if (s.Assignment.FailedUntil > b.Time && V2.Distance(v, s.Assignment.FailedGoal) < 1.2f || !Inside(b, s, v) || V2.Distance(v, s.Position) < .8f || V2.Distance(v, s.Position) > 5 || !b.Map.Walkable(v) || PositionLogic.Occupied(b, s, v) || !PositionLogic.Protected(b, s, v))
                                continue;
                            bool sector = PositionLogic.HasSector(b, s, v);
                            float risk = PositionLogic.Exposure(b, s, v);
                            float score = 4 + (sector ? 6 : 0) - V2.Distance(v, s.Position) * .8f - risk * 8;
                            if (score <= bestScore + 1)
                                continue;
                            var path = LossMemory.Find(b, s, v, t => PositionLogic.Exposure(b, s, t) * 5);
                            b.Stats.Paths++;
                            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 6 || !ProgressRecovery.SafeRoute(b, s, path))
                                continue;
                            route = path;
                            best = v;
                            bestScore = score;
                        }

                    if (route != null)
                    {
                        s.Path = route;
                        s.PathCursor = 0;
                        s.Destination = s.OrderAnchor = best;
                        s.Job = Job.Reposition;
                        s.MoveAuthorized = true;
                        s.ManeuverPaused = false;
                        s.LastMoved = b.Time;
                        CommandHierarchy.TransferAssignment(b, s, "Передано выбору укрытия на рубеже");
                        q.Posture.LocalMoves++;
                        s.Reason = "Закрепление: занимаю ближайшее укрытие с полезным сектором";
                        return true;
                    }
                }
            }

            s.OrderAnchor = s.Destination = s.Position;
            s.Job = s.Id == q.LeaderId ? Job.Command : Job.Cover;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Reason = "Закрепление: удерживаю позицию, наблюдаю сектор и поддерживаю своих";
            q.Posture.Holds++;
            return true;
        }
    }
}
