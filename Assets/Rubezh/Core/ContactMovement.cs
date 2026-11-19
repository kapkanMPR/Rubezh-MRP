using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class ContactMoveState
    {
        public float Until,
            NextPlan,
            NextCover;
        public V2 Center,
            Goal;
        public float Radius;
        public int Sign,
            Supporter = -1,
            Revision,
            Enemy = -1,
            Arcs,
            CoverMoves,
            Completed,
            Cancelled;
        public bool Arc;
    }

    public static class ContactMovement
    {
        static bool Enabled(BattleSimulation b, Soldier s) =>
            b.ContactManeuversEnabled
            && !b.Historical01225
            && s.Side == Side.Green
            && (
                CombatTraining.Skill(s, 2, "contact")
                || s.CampSkills == null
                    && b.PairExperimentMarch
                    && !ReconCombatPhase.Active(b)
                    && b.PairExperimentProfile >= 3
            );

        static bool OrderAllows(BattleSimulation b, Soldier s) =>
            b.Team(s).Style == CombatStyle.Balanced || b.Team(s).Style == CombatStyle.Aggressive;

        public static bool Active(BattleSimulation b, Soldier s) =>
            Enabled(b, s)
            && s.Awareness.ContactMove.Until > b.Time
            && s.Awareness.ContactMove.Revision == b.Team(s).Revision
            && OrderAllows(b, s);

        public static bool FireMove(BattleSimulation b, Soldier s) =>
            Active(b, s) && CombatTraining.Skill(s, 3, "contact") && !s.GunSniper && s.Ammo > 0 && s.Suppression < .65f;

        public static bool Supporting(BattleSimulation b, Soldier s)
        {
            foreach (var other in b.Soldiers)
                if (Active(b, other) && other.Awareness.ContactMove.Supporter == s.Id)
                    return true;
            return false;
        }

        static bool Free(BattleSimulation b, Soldier s) =>
            Enabled(b, s)
            && OrderAllows(b, s)
            && TacticalMarch.Free(b, s)
            && !s.GunSniper
            && s.Ammo >= 3
            && s.Suppression < .65f
            && s.Health >= 35
            && !s.GrenadeEscape.Active;

        static V2 Turn(V2 v, float angle)
        {
            float a = angle * (float)Math.PI / 180;
            return new V2(
                v.X * (float)Math.Cos(a) - v.Y * (float)Math.Sin(a),
                v.X * (float)Math.Sin(a) + v.Y * (float)Math.Cos(a)
            );
        }

        public static bool RadiusSafe(V2 start, List<V2> route, V2 center, float radius)
        {
            var prev = start;
            foreach (var end in route)
            {
                int n = Math.Max(1, (int)Math.Ceiling(V2.Distance(prev, end) * 2));
                for (int i = 1; i <= n; i++)
                    if (V2.Distance(prev + (end - prev) * (i / (float)n), center) < radius - .8f)
                        return false;
                prev = end;
            }

            return true;
        }

        static void Start(
            BattleSimulation b,
            Soldier s,
            Contact c,
            List<V2> path,
            V2 goal,
            bool arc,
            int sign,
            int supporter
        )
        {
            var st = s.Awareness.ContactMove;
            st.Center = c.Position;
            st.Goal = goal;
            st.Radius = V2.Distance(s.Position, c.Position);
            st.Until = b.Time + 12;
            st.Revision = b.Team(s).Revision;
            st.Enemy = c.EnemyId;
            st.Arc = arc;
            st.Sign = sign;
            st.Supporter = supporter;
            st.NextPlan = b.Time + 15;
            if (arc)
                st.Arcs++;
            else
                st.CoverMoves++;
            s.Path = path;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = goal;
            CommandHierarchy.TransferAssignment(b, s, "Передано манёвру при контакте");
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Job = Job.Reposition;
            s.SupportReady = false;
            s.Awareness.ContactSprintUntil = CombatTraining.Skill(s, 3, "contact") ? 0 : b.Time + 6;
            s.Reason = arc
                ? "Обхожу известную угрозу по дуге под прикрытием"
                : (
                    CombatTraining.Skill(s, 3, "contact")
                        ? "Смещаюсь к укрытию с возможностью огня на ходу"
                        : "Перемещаюсь к укрытию; огонь после остановки"
                );
        }

        public static bool TryCover(BattleSimulation b, Soldier s, Contact c)
        {
            if (
                b.Time < s.Awareness.ContactMove.NextCover
                || !Free(b, s)
                || c == null
                || b.Time - c.SeenAt > 3
                || PositionLogic.Protected(b, s, s.Position)
            )
                return false;
            s.Awareness.ContactMove.NextCover = b.Time + 2;
            b.Tactics.Refresh(b.Map);
            float best = float.MaxValue;
            V2 chosen = new V2();
            List<V2> route = null;
            var forward = (c.Position - s.Position).Normalized;
            foreach (var slot in b.Tactics.Slots)
            {
                var p = slot.Position;
                float distance = V2.Distance(s.Position, p);
                if (
                    distance < .7f
                    || distance > 7
                    || V2.Dot(p - s.Position, forward) < -.25f
                    || PositionLogic.Occupied(b, s, p)
                    || !PositionLogic.Protected(b, s, p)
                    || !PositionLogic.CanEngage(b, s, p, c)
                )
                    continue;
                float score = distance + PositionLogic.Exposure(b, s, p) * 5;
                foreach (var other in b.Soldiers)
                    if (other.Side == s.Side && other.Available && other != s)
                        score += Math.Max(0, 2 - V2.Distance(other.OrderAnchor, p)) * 5;
                if (score >= best)
                    continue;
                var path = LossMemory.Find(
                    b,
                    s,
                    p,
                    v => PositionLogic.Exposure(b, s, v) * 4 + FireLaneSafety.PathCost(b, s, v)
                );
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 10)
                    continue;
                best = score;
                chosen = p;
                route = path;
            }

            if (route == null)
                return false;
            Start(b, s, c, route, chosen, false, 0, -1);
            return true;
        }

        static Soldier Support(BattleSimulation b, Soldier mover, Contact c)
        {
            Soldier best = null;
            foreach (var s in b.Soldiers)
                if (
                    b.Team(mover).Contains(s)
                    && s != mover
                    && Free(b, s)
                    && !Active(b, s)
                    && PositionLogic.Able(s, b)
                    && PositionLogic.CanEngage(b, s, s.Position, c)
                    && !b.FriendlyInLane(s, c.Position)
                    && (b.Time - s.LastFire < 2 || s.Visible.Contains(c.EnemyId))
                )
                {
                    if (best == null || s.GunMachineGunner && !best.GunMachineGunner)
                        best = s;
                }

            return best;
        }

        public static bool TryArc(BattleSimulation b, Soldier s, Contact c)
        {
            if (
                b.PairExperimentProfile == 4
                && c != null
                && PositionLogic.CanEngage(b, s, s.Position, c)
                && b.Time - s.LastFire < 4
            )
                return false;
            if (b.PairPlan != null && b.PairPlan.Enabled)
                return false;
            if (
                !Free(b, s)
                || Supporting(b, s)
                || c == null
                || !c.Visual
                || c.Inferred
                || b.Time - c.SeenAt > 3
                || b.Time < s.Awareness.ContactMove.NextPlan
            )
                return false;
            var q = b.Team(s);
            if (q.CommandPlan.March.Coverer == s.Id)
                return false;
            s.Awareness.ContactMove.NextPlan = b.Time + 4;
            float radius = V2.Distance(s.Position, c.Position);
            if (radius < 6 || radius > Math.Min(24, b.WeaponRange(s) + 3))
                return false;
            int active = 0,
                otherSign = 0;
            foreach (var mate in b.Soldiers)
                if (q.Contains(mate) && Active(b, mate) && mate.Awareness.ContactMove.Arc)
                {
                    active++;
                    otherSign = mate.Awareness.ContactMove.Sign;
                }

            if (active >= 2)
                return false;
            var support = Support(b, s, c);
            if (support == null)
                return false;
            var radial = s.Position - c.Position;
            float best = float.MaxValue;
            List<V2> chosen = null;
            V2 goal = new V2();
            int chosenSign = 0;
            foreach (int sign in new[] { -1, 1 })
            {
                if (otherSign == sign)
                    continue;
                foreach (float degrees in new[] { 25f, 40f })
                {
                    V2 desired = c.Position + Turn(radial, sign * degrees),
                        p = b.Map.NearestOpen(desired);
                    if (V2.Distance(p, desired) > 1.5f || !b.Map.Walkable(p) || PositionLogic.Occupied(b, s, p))
                        continue;
                    float lateral = V2.Dot(p - s.Position, TacticalOrders.Lateral(b));
                    if ((q.Intent == ManeuverIntent.Left || q.Intent == ManeuverIntent.LeftHold) && lateral > .5f)
                        continue;
                    if ((q.Intent == ManeuverIntent.Right || q.Intent == ManeuverIntent.RightHold) && lateral < -.5f)
                        continue;
                    if (V2.Distance(p, b.Map.Objective) > V2.Distance(s.Position, b.Map.Objective) + 3)
                        continue;
                    var path = LossMemory.Find(
                        b,
                        s,
                        p,
                        v =>
                            Math.Max(0, radius - V2.Distance(v, c.Position)) * 30
                            + PositionLogic.Exposure(b, s, v) * 3
                            + FireLaneSafety.PathCost(b, s, v)
                    );
                    b.Stats.Paths++;
                    float length = SupportAI.PathLength(s.Position, path);
                    if (
                        path.Count == 0
                        || length > radius * 1.1f + 3
                        || !RadiusSafe(s.Position, path, c.Position, radius)
                    )
                        continue;
                    float score =
                        length + (PositionLogic.Protected(b, s, p) ? -4 : 2) + PositionLogic.Exposure(b, s, p) * 3;
                    if (score < best)
                    {
                        best = score;
                        chosen = path;
                        goal = p;
                        chosenSign = sign;
                    }
                }
            }

            if (chosen == null)
                return false;
            Start(b, s, c, chosen, goal, true, chosenSign, support.Id);
            return true;
        }

        public static void EnforceSupport(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
                if (Supporting(b, s) && Free(b, s))
                {
                    TacticalAI.Stop(s);
                    s.MoveAuthorized = false;
                    s.Job = Job.Cover;
                    s.Reason = "Прикрываю обход товарища по дуге";
                }
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var st = s.Awareness.ContactMove;
            if (st.Until > 0 && !Active(b, s))
            {
                st.Until = 0;
                st.Cancelled++;
                if (s.PathCursor < s.Path.Count && V2.Distance(s.Destination, st.Goal) < .1f)
                    TacticalAI.Stop(s);
            }

            if (Active(b, s))
            {
                if (V2.Distance(s.Destination, st.Goal) > .1f)
                {
                    st.Until = 0;
                    st.Cancelled++;
                    return false;
                }

                Contact c = null;
                foreach (var known in Knowledge.Contacts(b, s))
                    if (known.EnemyId == st.Enemy)
                    {
                        c = known;
                        break;
                    }

                bool supportLost =
                    st.Arc
                    && (
                        st.Supporter < 0
                        || !PositionLogic.Able(b.Soldiers[st.Supporter], b)
                        || c == null
                        || !PositionLogic.CanEngage(b, b.Soldiers[st.Supporter], b.Soldiers[st.Supporter].Position, c)
                    );
                if (
                    !Free(b, s)
                    || c == null
                    || b.Time - c.SeenAt > 6
                    || V2.Distance(c.Position, st.Center) > 3
                    || supportLost
                )
                {
                    st.Until = 0;
                    st.Cancelled++;
                    TacticalAI.Stop(s);
                    return false;
                }

                if (!MotionCombat.HasRoute(s))
                {
                    st.Until = 0;
                    st.Completed++;
                    return false;
                }

                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                return true;
            }

            if (!Free(b, s))
                return false;
            if (Supporting(b, s))
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.MoveAuthorized = false;
                return true;
            }

            var target = TacticalOrders.ClosestKnown(b, s);
            if (target == null)
                return false;
            if (b.Team(s).CommandPlan.March.Coverer != s.Id && TryCover(b, s, target))
                return true;
            return TryArc(b, s, target);
        }
    }
}
