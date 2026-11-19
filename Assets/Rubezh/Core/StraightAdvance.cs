using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class StraightPlan
    {
        public bool SoloApproach,
            HasLine,
            Active;
        public ManeuverIntent Mode;
        public V2 Axis,
            Origin;
        public float Since,
            Rear,
            Front;
        public int Waits;
        public readonly Dictionary<int, float> Lanes = new Dictionary<int, float>();
    }

    // A retained heading and individual lanes, captured once when the order is executed.
    public static class StraightAdvance
    {
        public static void Sync(BattleSimulation b, Squad q)
        {
            var p = q.Straight;
            if (q.Intent != ManeuverIntent.Straight && q.Intent != ManeuverIntent.Front)
            {
                p.Active = false;
                return;
            }

            if (p.Active && p.Mode == q.Intent)
                return;
            p.Active = true;
            p.Mode = q.Intent;
            p.Since = b.Time;
            p.Axis = TacticalOrders.Axis(b, q.Side);
            p.Lanes.Clear();
            V2 center = new V2();
            int n = 0;
            foreach (var s in b.Soldiers)
                if (q.Contains(s) && s.Alive && s.Deployed)
                {
                    center += s.Position;
                    n++;
                }

            p.Origin = n > 0 ? center / n : b.Map.Entry;
            var lateral = new V2(-p.Axis.Y, p.Axis.X);
            if (q.Intent == ManeuverIntent.Front)
            {
                // Spread across the usable front, not into map-edge obstacles. Commander anchors the middle.
                var members = new List<Soldier>();
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && s.Alive && s.Deployed)
                        members.Add(s);
                members.Sort((a, z) => a.Id.CompareTo(z.Id));
                var mid = new V2(b.Map.Columns * .5f, b.Map.Rows * .5f);
                p.Origin += lateral * V2.Dot(mid - p.Origin, lateral);
                float lateralExtent = Math.Abs(lateral.X) * b.Map.Columns + Math.Abs(lateral.Y) * b.Map.Rows;
                float half = Math.Min(Math.Max(3, (lateralExtent - 4) * .45f), Math.Max(3, (members.Count - 1) * 3.5f));
                int followers = members.Count - 1,
                    slot = 0,
                    negative = (followers + 1) / 2;
                foreach (var member in members)
                    if (member.Id != q.LeaderId)
                    {
                        // Keep sequential preferred pairs on the same side of the line.
                        int i = slot++;
                        float rank = i < negative ? i - negative : i - negative + 1;
                        p.Lanes[member.Id] = Util.Clamp(rank * half / Math.Max(1, negative), -half, half);
                    }

                if (members.Count > 0)
                {
                    bool hasLeader = false;
                    foreach (var s in members)
                        if (s.Id == q.LeaderId)
                            hasLeader = true;
                    if (hasLeader)
                        p.Lanes[q.LeaderId] = 0;
                }
            }
            else
                foreach (var s in b.Soldiers)
                    if (q.Contains(s) && s.Alive && s.Deployed)
                        p.Lanes[s.Id] = V2.Dot(s.Position - p.Origin, lateral);
        }

        public static void Review(BattleSimulation b, Squad q)
        {
            Sync(b, q);
            if (!q.Straight.Active)
                return;
            float rear = float.MaxValue,
                front = float.MinValue;
            int able = 0;
            if (q.Side == Side.Green)
                foreach (var u in b.Soldiers)
                    if (q.Contains(u) && u.Deployed && u.Alive && !u.Incapacitated)
                        able++;
            q.Straight.SoloApproach =
                able == 1 && (q.Style == CombatStyle.Balanced || q.Style == CombatStyle.Aggressive);
            // Compare combat pairs, not the commander/reserve or a wounded fighter needing help.
            foreach (var p in q.CommandPlan.Pairs)
            {
                float sum = 0;
                int n = 0;
                foreach (int id in new[] { p.A, p.B })
                    if (id >= 0 && id < b.Soldiers.Count)
                    {
                        var s = b.Soldiers[id];
                        if (q.Contains(s) && s.Available && s.Deployed && !CommandPosture.Service(b, s) && !s.Sniper)
                        {
                            sum += V2.Dot(s.Position - q.Straight.Origin, q.Straight.Axis);
                            n++;
                        }
                    }

                if (n == 0 || p.B < 0 && p.A == q.LeaderId)
                    continue;
                float x = sum / n;
                rear = Math.Min(rear, x);
                front = Math.Max(front, x);
            }

            q.Straight.HasLine = rear != float.MaxValue;
            if (!q.Straight.HasLine)
            {
                rear = 0;
                front = 0;
            }

            q.Straight.Rear = rear;
            q.Straight.Front = front;
        }

        public static bool Waiting(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (
                q.Intent != ManeuverIntent.Straight && q.Intent != ManeuverIntent.Front
                || q.Style == CombatStyle.Passive
                || q.Style == CombatStyle.Withdraw
                || TacticalOrders.Emergency(q)
                || CommandPosture.Service(b, s)
            )
                return false;
            Sync(b, q);
            if (q.Intent == ManeuverIntent.Front)
            {
                float lane;
                if (q.Straight.Lanes.TryGetValue(s.Id, out lane))
                {
                    var lat = new V2(-q.Straight.Axis.Y, q.Straight.Axis.X);
                    if (Math.Abs(V2.Dot(s.Position - q.Straight.Origin, lat) - lane) > 2.5f)
                        return false;
                }
            }

            return !q.Straight.SoloApproach
                && q.Straight.HasLine
                && V2.Dot(s.Position - q.Straight.Origin, q.Straight.Axis) > q.Straight.Rear + 3.5f;
        }

        public static V2 Goal(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            Sync(b, q);
            var p = q.Straight;
            if (p.SoloApproach)
                return b.Map.Objective;
            var lat = new V2(-p.Axis.Y, p.Axis.X);
            float lane;
            if (!p.Lanes.TryGetValue(s.Id, out lane))
                p.Lanes[s.Id] = lane = V2.Dot(s.Position - p.Origin, lat);
            float own = V2.Dot(s.Position - p.Origin, p.Axis);
            float along = Waiting(b, s) ? own : (p.HasLine ? Math.Max(own, Math.Min(p.Rear + 6, own + 6)) : own + 6);
            return TacticalOrders.Open(b, p.Origin + p.Axis * along + lat * lane);
        }

        public static bool Inside(BattleSimulation b, Soldier s, V2 point)
        {
            var q = b.Team(s);
            if (q.Intent != ManeuverIntent.Straight && q.Intent != ManeuverIntent.Front)
                return true;
            Sync(b, q);
            if (q.Straight.SoloApproach)
                return true;
            float lane;
            if (!q.Straight.Lanes.TryGetValue(s.Id, out lane))
                return true;
            var lat = new V2(-q.Straight.Axis.Y, q.Straight.Axis.X);
            float deviation = Math.Abs(V2.Dot(point - q.Straight.Origin, lat) - lane);
            if (q.Intent == ManeuverIntent.Front)
            {
                // Permit safe intermediate bounds towards a distant assigned lane.
                float current = Math.Abs(V2.Dot(s.Position - q.Straight.Origin, lat) - lane);
                return deviation <= Math.Max(2.8f, current + .6f);
            }

            return deviation <= 2.5f;
        }

        public static bool Hold(BattleSimulation b, Soldier s)
        {
            if (!Waiting(b, s) || s.GrenadeEscape.Active || s.Sniper || s.Busy)
                return false;
            // The front element covers the catching-up element. Existing firing remains available.
            TacticalAI.Stop(s);
            CommandHierarchy.TransferAssignment(b, s, "Передано прикрытию выравнивания фронта");
            s.OrderAnchor = s.Destination = s.Position;
            s.Job = s.Id == b.Team(s).LeaderId ? Job.Command : Job.Cover;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Reason =
                b.Team(s).Intent == ManeuverIntent.Front
                    ? "Фронтом: прикрываю соседей по широкому рубежу"
                    : "Прямо: прикрываю выравнивание фронта, жду отставшую двойку";
            b.Team(s).Straight.Waits++;
            return true;
        }
    }
}
