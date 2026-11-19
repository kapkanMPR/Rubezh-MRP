using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class BehaviorAction
    {
        public string type = "advance",
            who = "group";
    }

    [Serializable]
    public sealed class BehaviorRule
    {
        public string when = "contact",
            mode = "parallel";
        public bool enabled = true;
        public float delay;
        public List<BehaviorAction> actions = new List<BehaviorAction>();
    }

    [Serializable]
    public sealed class BehaviorPlan
    {
        public string schema = "rubezh-behavior",
            id = "",
            name = "Новый план",
            style = "balanced";
        public int version = 2;
        public List<BehaviorRule> rules = new List<BehaviorRule>();

        public static BehaviorPlan Default()
        {
            var p = new BehaviorPlan { id = Guid.NewGuid().ToString("N"), name = "Уверенное продвижение" };
            p.rules.Add(Rule("wounded", "rescue", "medic", "suppress", "gunner"));
            p.rules.Add(Rule("flanked", "smoke", "group"));
            p.rules.Add(Rule("cluster", "heavy", "launcher"));
            p.rules.Add(Rule("sniper", "smoke", "group"));
            p.rules.Add(Rule("contact", "hold", "group", "suppress", "gunner", "heavy", "launcher"));
            var clear = Rule("clear", "advance", "group");
            clear.delay = 5;
            p.rules.Add(clear);
            p.rules.Add(Rule("travel", "advance", "group"));
            return p;
        }

        static BehaviorRule Rule(string when, params string[] actions)
        {
            var r = new BehaviorRule { when = when };
            for (int i = 0; i < actions.Length; i += 2)
                r.actions.Add(new BehaviorAction { type = actions[i], who = actions[i + 1] });
            return r;
        }

        public void Normalize()
        {
            if (string.IsNullOrEmpty(id))
                id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(name))
                name = "Новый план";
            if (name.Length > 64)
                name = name.Substring(0, 64);
            if (rules == null)
                rules = new List<BehaviorRule>();
            if (rules.Count > 40)
                rules.RemoveRange(40, rules.Count - 40);
            rules.RemoveAll(r => r == null);
            foreach (var r in rules)
            {
                r.delay = float.IsNaN(r.delay) ? 0 : Math.Max(0, Math.Min(60, r.delay));
                if (r.actions == null)
                    r.actions = new List<BehaviorAction>();
                r.actions.RemoveAll(a => a == null);
                if (r.actions.Count > 12)
                    r.actions.RemoveRange(12, r.actions.Count - 12);
            }
        }
    }

    // A plan submits priorities to the existing AI. Safety, ammunition, medical
    // ownership and ordinary combat remain authoritative. No enemy omniscience.
    public sealed class BehaviorPlanRunner
    {
        readonly BehaviorPlan plan;
        readonly float[] since;
        readonly bool[][] done;
        readonly float[] retry,
            episodeAt;
        readonly HashSet<int> occupied = new HashSet<int>();
        readonly List<Contact> contacts = new List<Contact>();
        public int Selected = -1,
            Episodes,
            Accepted,
            Rejected;
        public string Status = "Ожидание события";
        float next,
            lostAt = -1;
        bool hadContact,
            previousContact;

        public BehaviorPlanRunner(BehaviorPlan plan)
        {
            this.plan = plan;
            plan.Normalize();
            since = new float[plan.rules.Count];
            retry = new float[since.Length];
            episodeAt = new float[since.Length];
            done = new bool[since.Length][];
            for (int i = 0; i < done.Length; i++)
                done[i] = new bool[plan.rules[i].actions.Count];
            for (int i = 0; i < since.Length; i++)
                since[i] = -1;
        }

        public void Tick(BattleSimulation b)
        {
            if (b == null || b.Outcome != Outcome.Running || b.Time < next)
                return;
            next = b.Time + .25f;
            if (TacticalOrders.Emergency(b.Green))
                return;
            contacts.Clear();
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Alive && s.Deployed)
                    foreach (var c in s.Contacts.Values)
                    {
                        if (b.Time - c.SeenAt > 3 || c.Confidence < .35f)
                            continue;
                        bool duplicate = false;
                        foreach (var old in contacts)
                            if (old.EnemyId == c.EnemyId)
                            {
                                duplicate = true;
                                break;
                            }
                        if (!duplicate)
                            contacts.Add(c);
                    }
            bool contact = contacts.Count > 0;
            if (contact)
            {
                hadContact = true;
                lostAt = -1;
            }
            else if (previousContact)
                lostAt = b.Time;
            previousContact = contact;
            Selected = -1;
            occupied.Clear();
            string movement = null;
            for (int i = 0; i < plan.rules.Count; i++)
            {
                var rule = plan.rules[i];
                bool matches = rule.enabled && Matches(b, rule.when, contact);
                if (!matches)
                {
                    since[i] = -1;
                    Array.Clear(done[i], 0, done[i].Length);
                    continue;
                }
                if (since[i] < 0)
                {
                    since[i] = episodeAt[i] = b.Time;
                    retry[i] = b.Time;
                    Episodes++;
                }
                if (Selected < 0 && rule.actions.Count > 0)
                {
                    Selected = i;
                    Status = rule.when;
                }
                // Each action owns only its own resource. A higher medical order
                // does not suppress a lower compatible fire or movement request.
                bool ready = b.Time - since[i] >= rule.delay && b.Time >= retry[i];
                if (ready)
                    retry[i] = b.Time + 1;
                string higherMovement = movement;
                for (int a = 0; a < rule.actions.Count; a++)
                {
                    var action = rule.actions[a];
                    bool maneuver = IsMovement(action.type);
                    bool conflict =
                        maneuver && higherMovement != null && !CompatibleMovement(higherMovement, action.type);
                    if (maneuver && higherMovement == null)
                        movement = action.type;
                    if (done[i][a])
                    {
                        // Sustained fire owns that actor while the event remains active.
                        if (action.type == "suppress")
                            foreach (var unit in b.Soldiers)
                                if (
                                    unit.Side == Side.Green
                                    && Actor(unit, action.who)
                                    && unit.Available
                                    && unit.SuppressiveFire
                                    && unit.Ammo > 0
                                )
                                    occupied.Add(unit.Id);
                        continue;
                    }
                    if (ready && !conflict)
                    {
                        bool accepted = Execute(b, action);
                        if (accepted)
                        {
                            Accepted++;
                            done[i][a] = true;
                        }
                        else
                        {
                            Rejected++;
                            // No suitable executor/target must not stall subsequent
                            // requests indefinitely. Busy actors are retried later.
                            if (b.Time - episodeAt[i] > 12 + rule.delay && !HasCandidate(b, action))
                                done[i][a] = true;
                        }
                    }
                    // Sequence waits for acceptance of its previous request.
                    // Parallel visits every action, queuing only busy resources.
                    if (rule.mode == "sequence")
                        break;
                }
            }
        }

        public static bool IsMovement(string type)
        {
            return type == "advance"
                || type == "left"
                || type == "right"
                || type == "auto"
                || type == "hold"
                || type == "wait"
                || type == "retreat";
        }

        public static bool CompatibleMovement(string first, string second)
        {
            return first == second || (first == "hold" || first == "wait") && (second == "hold" || second == "wait");
        }

        static bool HasCandidate(BattleSimulation b, BehaviorAction a)
        {
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Alive && !s.Incapacitated && Actor(s, a.who))
                {
                    if (a.type == "smoke" && s.SmokeCharges <= 0)
                        continue;
                    if (a.type == "heavy" && s.Rockets <= 0)
                        continue;
                    if (
                        a.type == "grenade"
                        && s.GrenadeCharges + s.OtherGrenadeCharges + s.ThirdGrenadeCharges + s.LoadedUnderbarrel <= 0
                    )
                        continue;
                    return true;
                }
            return false;
        }

        bool Matches(BattleSimulation b, string ev, bool contact)
        {
            if (ev == "contact")
                return contact;
            if (ev == "travel")
                return !contact && !hadContact;
            if (ev == "clear")
                return !contact && hadContact && lostAt >= 0;
            int alive = 0;
            V2 center = new V2();
            foreach (var s in b.Soldiers)
            {
                if (s.Side != Side.Green || !s.Deployed || !s.Alive)
                    continue;
                alive++;
                center += s.Position;
                if (ev == "wounded" && (s.Incapacitated || s.Bleeding))
                    return true;
                if (ev == "suppressed" && s.Suppression >= .65f)
                    return true;
                if (ev == "lowammo" && s.Ammo + s.Reserve < s.MagazineSize)
                    return true;
            }
            if (alive > 0)
                center *= 1f / alive;
            foreach (var c in contacts)
            {
                if (ev == "sniper" && c.ObservedWeaponRange >= 20 && b.Time - c.FireSeenAt < 5)
                    return true;
                if (ev == "cluster")
                {
                    int n = 0;
                    foreach (var d in contacts)
                        if (V2.Distance(c.Position, d.Position) <= 3)
                            n++;
                    if (n >= 3)
                        return true;
                }
                if (ev == "flanked")
                {
                    var axis = PositionLogic.Axis(b);
                    var d = c.Position - center;
                    if (
                        V2.Distance(c.Position, center) < 15
                        && Math.Abs(V2.Dot(d, new V2(-axis.Y, axis.X))) > Math.Abs(V2.Dot(d, axis))
                    )
                        return true;
                }
            }
            return false;
        }

        public static bool Actor(Soldier s, string who)
        {
            return who == "group"
                || who == "commander" && s.Role == Role.Commander
                || who == "rifle" && s.Role == Role.Rifleman
                || who == "gunner" && s.Role == Role.MachineGunner
                || who == "launcher" && s.Role == Role.Grenadier
                || who == "medic" && s.Role == Role.Medic
                || who == "sniper" && s.Role == Role.Sniper;
        }

        bool Execute(BattleSimulation b, BehaviorAction a)
        {
            var q = b.Green;
            int style =
                plan.style == "bold" ? 3
                : plan.style == "careful" ? 1
                : 2;
            int intent = (int)q.Intent;
            bool order = true;
            switch (a.type)
            {
                case "advance":
                    intent = 7;
                    break;
                case "left":
                    intent = 1;
                    break;
                case "right":
                    intent = 2;
                    break;
                case "hold":
                case "wait":
                    style = 1;
                    break;
                case "retreat":
                    style = 0;
                    break;
                case "auto":
                    intent = 0;
                    break;
                default:
                    order = false;
                    break;
            }
            if (order)
            {
                if (a.who != "group")
                    return false;
                if ((int)q.Style != style || (int)q.Intent != intent)
                    if (!q.PriorityPending)
                        TacticalOrders.Request(b, style, intent);
                    else
                        return false;
                return true;
            }
            if (a.type == "rescue")
            {
                SupportAI.Plan(b, q);
                return HasHelper(b, a.who, true);
            }
            if (a.type == "resupply")
            {
                SupplyAI.Plan(b, q);
                return HasHelper(b, a.who, false);
            }
            foreach (var s in b.Soldiers)
            {
                if (
                    s.Side != Side.Green
                    || !Actor(s, a.who)
                    || !s.Deployed
                    || !s.Available
                    || s.Broken
                    || occupied.Contains(s.Id)
                    || s.RocketAimTarget >= 0
                    || s.OrdnanceAimTarget >= 0
                    || q.Smoke.Pending != null && q.Smoke.Pending.Carrier == s.Id
                    || s.GrenadeEscape.Active
                    || s.VehicleDisperse
                    || s.VehicleRally
                    || s.HandsBusyUntil > b.Time
                )
                    continue;
                if (
                    a.type == "smoke"
                    && s.SmokeCharges > 0
                    && SmokeTactics.Request(b, q, s, s.Position, SmokePurpose.ScreenThreat, s)
                )
                {
                    occupied.Add(s.Id);
                    return true;
                }
                if (a.type == "grenade")
                {
                    bool used = ArcedOrdnance.TryUse(b, s);
                    if (used || s.OrdnanceAimTarget >= 0)
                    {
                        occupied.Add(s.Id);
                        return true;
                    }
                }
                if (a.type == "heavy")
                {
                    bool used = SpecialWeapons.TryLaunch(b, s);
                    if (used || s.RocketAimTarget >= 0)
                    {
                        occupied.Add(s.Id);
                        return true;
                    }
                }
                if (a.type == "suppress")
                {
                    var target = TacticalAI.FireTarget(b, s);
                    if (target != null && s.Ammo > 0 && CombatCoordination.CanSuppress(b, s))
                    {
                        s.SuppressiveFire = true;
                        occupied.Add(s.Id);
                        return true;
                    }
                }
            }
            return false;
        }

        static bool HasHelper(BattleSimulation b, string who, bool medical)
        {
            foreach (var s in b.Soldiers)
                if (
                    s.Side == Side.Green
                    && Actor(s, who)
                    && s.Alive
                    && (medical ? s.RescueTargetId >= 0 || s.TreatmentUntil > b.Time : s.SupplyReceiverId >= 0)
                )
                    return true;
            return false;
        }
    }
}
