using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum GroupDuty
    {
        None,
        Recover,
        Guard,
        Screen,
        Support,
        Advance,
        Intercept,
    }

    public sealed class GroupCommandState
    {
        public GroupDuty Duty;
        public bool Active;
        public V2 Anchor;
        public float NextReview,
            Since,
            RecoveryUntil,
            NextBound,
            MoveUntil;
        public int Partner = -1,
            Mover = -1,
            Revision;
        public float ChoiceUntil;
        public int Intercepts,
            ChoiceChanges;
        public RedCounterattackPhase LastPhase;
        public string Reason = "";
    }

    // Coordinates existing detachments, not individual pairs. Observations keep their
    // original timestamps; no live enemy positions or artificial support readiness.
    public static class CommanderCooperation
    {
        static bool Ready(BattleSimulation b, Soldier s)
        {
            return s.Available
                && !s.Broken
                && !s.Busy
                && !s.VehicleRally
                && !s.VehicleDisperse
                && s.Suppression < .65f
                && s.Health >= 35
                && s.Ammo + s.Reserve > 0;
        }

        static bool Free(BattleSimulation b, Soldier s)
        {
            return !AreaSuppression.Reserved(b, s)
                && !CounterSniper.Reserved(b, s)
                && Ready(b, s)
                && !s.Bleeding
                && s.RescueTargetId < 0
                && s.RescuerId < 0
                && s.SupplyDonorId < 0
                && s.SupplyReceiverId < 0
                && s.LootGroundId < 0
                && s.LootTargetId < 0
                && s.LauncherPickupId < 0
                && s.WeaponAidTargetId < 0
                && s.HelpUntil <= b.Time
                && s.HelpRetreatUntil <= b.Time
                && s.FlagResponseUntil <= b.Time
                && s.HandsBusyUntil <= b.Time
                && s.ReloadUntil <= b.Time;
        }

        static int Count(BattleSimulation b, DefenderGroupState g, bool ready)
        {
            int n = 0;
            foreach (var s in g.Members)
                if (ready ? Ready(b, s) : s.Alive && s.Deployed)
                    n++;
            return n;
        }

        static V2 Center(DefenderGroupState g)
        {
            V2 p = new V2();
            int n = 0;
            foreach (var s in g.Members)
                if (s.Alive && s.Deployed)
                {
                    p += s.Position;
                    n++;
                }

            return n > 0 ? p / n : g.Spawn;
        }

        static bool Recovering(BattleSimulation b, DefenderGroupState g)
        {
            int alive = Count(b, g, false);
            return g.Counterattack.Phase == RedCounterattackPhase.Retreat
                || g.Cooperation.RecoveryUntil > b.Time
                || alive > 0 && Count(b, g, true) * 2 < alive;
        }

        static V2 Home(BattleSimulation b, DefenderGroupState g)
        {
            return g.Pullback || g.Counterattack.Phase == RedCounterattackPhase.Defense ? b.Map.Objective : g.Spawn;
        }

        static Contact Threat(BattleSimulation b, DefenderGroupState g)
        {
            Contact best = null;
            float score = float.MinValue;
            foreach (var c in g.Command.Contacts.Values)
            {
                float age = b.Time - c.SeenAt;
                if (age < 0 || age > 8 || c.Confidence < .35f)
                    continue;
                float v = c.Confidence * 5 - V2.Distance(c.Position, b.Map.Objective) * .1f - age * .2f;
                if (v > score)
                {
                    score = v;
                    best = c;
                }
            }

            return best;
        }

        public static void Review(BattleSimulation b, DefenderGroupState g)
        {
            var st = g.Cooperation;
            var phase = g.Counterattack.Phase;
            bool changedPhase = phase != st.LastPhase;
            if (changedPhase)
            {
                if (st.LastPhase == RedCounterattackPhase.Retreat && phase == RedCounterattackPhase.Defense)
                    st.RecoveryUntil = b.Time + 12;
                st.LastPhase = phase;
            }

            if (!changedPhase && b.Time < st.NextReview)
                return;
            st.NextReview = b.Time + 8;
            int deployed = 0;
            foreach (var other in b.DefenderGroups)
                if (other.Deployed && Count(b, other, false) > 0)
                    deployed++;
            // A single detachment still leaves recovery; there is simply no partner to assign.
            DefenderGroupState guard = null,
                distressed = null;
            float guardScore = float.MaxValue,
                distressScore = float.MaxValue;
            foreach (var other in b.DefenderGroups)
            {
                if (!other.Deployed || Count(b, other, false) == 0 || other.Command.Side != g.Command.Side)
                    continue;
                if (!Recovering(b, other))
                {
                    float d =
                        V2.Distance(Center(other), b.Map.Objective)
                        + (other.Counterattack.Phase == RedCounterattackPhase.Assault ? 20 : 0);
                    if (d < guardScore)
                    {
                        guardScore = d;
                        guard = other;
                    }
                }

                if (other != g && Recovering(b, other))
                {
                    float d = V2.Distance(Center(g), Center(other));
                    if (d < distressScore)
                    {
                        distressScore = d;
                        distressed = other;
                    }
                }
            }

            GroupDuty duty =
                Recovering(b, g) ? GroupDuty.Recover
                : phase == RedCounterattackPhase.Assault ? GroupDuty.Advance
                : g == guard ? GroupDuty.Guard
                : distressed != null ? GroupDuty.Support
                : GroupDuty.Screen;
            Contact threat = Threat(b, g);
            V2 home = Home(b, g),
                anchor = home;
            DefenderGroupState cooperating = distressed ?? (guard != g ? guard : null);
            if (cooperating == null)
            {
                float nearest = float.MaxValue;
                foreach (var other in b.DefenderGroups)
                    if (
                        other != g
                        && other.Deployed
                        && other.Command.Side == g.Command.Side
                        && Count(b, other, false) > 0
                    )
                    {
                        float d = V2.Distance(Center(g), Center(other));
                        if (d < nearest)
                        {
                            nearest = d;
                            cooperating = other;
                        }
                    }
            }

            int partner = cooperating == null ? -1 : b.DefenderGroups.IndexOf(cooperating);
            if (
                duty == GroupDuty.Guard
                && cooperating != null
                && cooperating.Counterattack.Phase == RedCounterattackPhase.Assault
                && Count(b, g, true) >= 2
                && b.Capture <= .001f
            )
            {
                var shared = Threat(b, cooperating);
                if (shared != null && V2.Distance(shared.Position, b.Map.Objective) > 10)
                {
                    duty = GroupDuty.Support;
                    threat = shared;
                }
            }

            // A spare defensive group can contest a reported approach while the guard
            // retains the objective. Selection uses reports, never player intent or
            // hidden unit positions. Recovery and authored assault/pullback take priority.
            if (
                duty == GroupDuty.Screen
                && guard != null
                && guard != g
                && threat != null
                && Count(b, g, true) >= 2
                && V2.Distance(threat.Position, b.Map.Objective) < 18
                && b.Capture < .01f
            )
            {
                DefenderGroupState closest = null;
                float distance = float.MaxValue;
                foreach (var other in b.DefenderGroups)
                    if (
                        other != guard
                        && other.Deployed
                        && other.Command.Side == g.Command.Side
                        && other.Counterattack.Phase != RedCounterattackPhase.Assault
                        && !Recovering(b, other)
                        && Count(b, other, true) >= 2
                    )
                    {
                        float gap = V2.Distance(Center(other), threat.Position);
                        if (gap < distance)
                        {
                            distance = gap;
                            closest = other;
                        }
                    }

                if (closest == g)
                    duty = GroupDuty.Intercept;
            }

            if (duty == GroupDuty.Advance || phase == RedCounterattackPhase.Retreat)
            {
                st.Active = false;
            }
            else
            {
                st.Active = true;
                if (threat != null && duty != GroupDuty.Recover)
                {
                    V2 direction = (threat.Position - home).Normalized;
                    // Guard the approach to the objective, rather than gathering directly on it.
                    float reach = duty == GroupDuty.Guard ? Math.Min(6, g.Radius) : Math.Min(10, g.Radius + 3);
                    anchor = home + direction * Math.Min(reach, Math.Max(0, V2.Distance(home, threat.Position) - 8));
                    if (duty == GroupDuty.Support)
                    {
                        V2 near = Center(distressed ?? cooperating);
                        anchor = near + (home - near).Normalized * 3;
                    }
                    else if (duty == GroupDuty.Intercept)
                    {
                        V2 side = new V2(-direction.Y, direction.X);
                        float sign = V2.Dot(Center(g) - home, side) >= 0 ? 1 : -1;
                        anchor =
                            home
                            + direction * Math.Min(reach * .65f, Math.Max(0, V2.Distance(home, threat.Position) - 8))
                            + side * sign * 3f;
                    }
                    else if (duty == GroupDuty.Screen && deployed > 1)
                    {
                        V2 side = new V2(-direction.Y, direction.X);
                        anchor += side * (b.DefenderGroups.IndexOf(g) % 2 == 0 ? 3 : -3);
                    }

                    V2 delta = anchor - home;
                    if (delta.Length > reach)
                        anchor = home + delta.Normalized * reach;
                }

                anchor = b.Map.NearestOpen(anchor);
            }

            // Avoid chasing every small report change. Safety/task changes bypass this
            // short commitment; continued interception keeps its already selected side.
            if (st.Active && st.Duty == duty && duty == GroupDuty.Intercept && b.Time < st.ChoiceUntil)
                anchor = st.Anchor;
            if (st.Duty != duty)
            {
                st.ChoiceChanges++;
                st.ChoiceUntil = b.Time + 16;
                if (duty == GroupDuty.Intercept)
                    st.Intercepts++;
            }

            string reason =
                duty == GroupDuty.Recover ? "Восстановиться после отхода; сохранить огонь и помощь своим"
                : duty == GroupDuty.Guard ? "Удерживать подходы к флагу, занять полезные сектора"
                : duty == GroupDuty.Support ? "Прикрывать соседнее отделение, помочь ему сменить рубеж"
                : duty == GroupDuty.Intercept ? "Резерв: занять боковой сектор известного подхода; сосед держит флаг"
                : duty == GroupDuty.Screen ? "Занять соседний сектор и восстановить контакт"
                : "Продолжать исходную задачу, учитывать доклады соседей";
            if (duty != st.Duty || partner != st.Partner || V2.Distance(anchor, st.Anchor) > 1)
            {
                st.Since = b.Time;
                st.Revision++;
                g.Command.Defense.NextReview = 0;
                b.Log(g.Command.Side, g.Name + ": " + reason);
            }

            st.Duty = duty;
            st.Partner = partner;
            st.Anchor = anchor;
            st.Reason = reason;
        }

        static bool Moving(BattleSimulation b, DefenderGroupState g)
        {
            var st = g.Cooperation;
            if (st.Mover < 0 || st.Mover >= b.Soldiers.Count)
                return false;
            var s = b.Soldiers[st.Mover];
            if (s.Order != "Связь отделений: занять согласованный сектор")
            {
                st.Mover = -1;
                return false;
            }

            if (
                st.MoveUntil > b.Time
                && Free(b, s)
                && s.PathCursor < s.Path.Count
                && g.Counterattack.Phase != RedCounterattackPhase.Retreat
            )
                return true;
            if (s.PathCursor < s.Path.Count && s.Order == "Связь отделений: занять согласованный сектор" && Free(b, s))
            {
                s.RejectedPost = s.OrderAnchor;
                s.RejectedUntil = b.Time + 8;
                TacticalAI.Stop(s);
                s.OrderAnchor = s.Position;
                s.Job = Job.Cover;
            }

            st.Mover = -1;
            return false;
        }

        public static bool Plan(BattleSimulation b, DefenderGroupState g)
        {
            var st = g.Cooperation;
            if (Moving(b, g))
            {
                g.Command.CommanderOrder = st.Reason;
                return true;
            }

            if (
                !st.Active
                || st.Duty == GroupDuty.Recover
                || g.Command.Defense.Active && st.Duty != GroupDuty.Support
                || b.Time < st.NextBound
            )
                return false;
            st.NextBound = b.Time + 8;
            var threat = Threat(b, g);
            if (threat == null)
                return false;
            // One short bound at a time. Existing useful firing positions and specialist
            // tasks are preserved; ordinary movement still validates cover/smoke support.
            Soldier chosen = null;
            V2 post = new V2();
            List<V2> path = null;
            float best = float.MinValue;
            foreach (var s in g.Members)
            {
                if (
                    !Free(b, s)
                    || s.PathCursor < s.Path.Count
                    || b.Time < s.Tactical.CommitUntil
                    || b.Time - s.LastFire < 4
                    || PositionLogic.HasSector(b, s, s.Position)
                )
                    continue;
                V2 toward = (threat.Position - s.Position).Normalized;
                V2 wanted = s.Position + toward * 3.5f;
                if (st.Duty == GroupDuty.Intercept)
                {
                    var delta = st.Anchor - s.Position;
                    wanted = s.Position + delta.Normalized * Math.Min(3.5f, delta.Length);
                }

                for (int k = 0; k < 3; k++)
                {
                    V2 candidate;
                    if (k == 0)
                    {
                        if (!PositionLogic.TryPost(b, s, wanted, 3, false, out candidate))
                            continue;
                    }
                    else
                        candidate = b.Map.NearestOpen(wanted + new V2(-toward.Y, toward.X) * (k == 1 ? 2 : -2));
                    if (
                        V2.Distance(candidate, st.Anchor) > Math.Max(g.Radius, V2.Distance(s.Position, st.Anchor) - .5f)
                        || PositionLogic.Occupied(b, s, candidate)
                        || (
                            st.Duty == GroupDuty.Intercept
                                ? V2.Distance(candidate, st.Anchor) > V2.Distance(s.Position, st.Anchor) - .5f
                                : V2.Distance(candidate, threat.Position) > V2.Distance(s.Position, threat.Position) - 1
                        )
                        || V2.Distance(candidate, s.Position) < 1
                    )
                        continue;
                    if (!PositionLogic.Protected(b, s, candidate) && PositionLogic.Exposure(b, s, candidate) > .05f)
                        continue;
                    var route = LossMemory.Find(
                        b,
                        s,
                        candidate,
                        p => PositionLogic.Exposure(b, s, p) * 3 + (PositionLogic.Occupied(b, s, p) ? 12 : 0)
                    );
                    b.Stats.Paths++;
                    if (route.Count == 0 || SupportAI.PathLength(s.Position, route) > 9)
                        continue;
                    float score =
                        (PositionLogic.HasSector(b, s, candidate) ? 12 : 0)
                        - V2.Distance(candidate, threat.Position)
                        - (s.Role == Role.Medic ? 4 : 0);
                    if (score > best)
                    {
                        best = score;
                        chosen = s;
                        post = candidate;
                        path = route;
                    }

                    break;
                }
            }

            if (chosen == null)
                return false;
            if (!SquadBrain.GiveOrder(b, g.Command, chosen, Job.Reposition, post, false))
                return false;
            if (V2.Distance(chosen.OrderAnchor, post) > .1f)
                return false;
            chosen.Path = path;
            chosen.PathCursor = 0;
            chosen.Order = "Связь отделений: занять согласованный сектор";
            chosen.Reason = st.Reason;
            st.Mover = chosen.Id;
            st.MoveUntil = b.Time + 14;
            chosen.NextDecision = b.Time;
            SmokeTactics.Request(b, g.Command, chosen, post, SmokePurpose.Route);
            g.Command.CommanderOrder = st.Reason;
            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var g = s.DefenderGroup;
            if (g == null || g.Cooperation.Mover != s.Id || !Moving(b, g))
                return false;
            s.MoveAuthorized =
                TacticalAI.SafeApproach(b, s)
                || DefenseBrain.Covering(b, s, s.OrderAnchor)
                || CommanderPriorities.SmokeSafe(b, s);
            s.Reason = s.MoveAuthorized ? g.Cooperation.Reason : "Согласованный сектор: проверяю прикрытие маршрута";
            return true;
        }
    }
}
