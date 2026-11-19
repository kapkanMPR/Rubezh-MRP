using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class SniperEstimate
    {
        public Contact Contact;
        public int Shot;
        internal V2 Origin,
            Bias;
        public float NextRequest;
    }

    public sealed class CounterSniperIntel
    {
        public readonly Dictionary<int, SniperEstimate> Green = new Dictionary<int, SniperEstimate>(),
            Red = new Dictionary<int, SniperEstimate>();
    }

    public enum CounterSniperPhase
    {
        None,
        Escape,
        Approach,
        Cover,
        Return,
        CloseIn,
    }

    public sealed class CounterSniperAction
    {
        public CounterSniperPhase Phase;
        public Contact Threat;
        public int Victim = -1;
        public float IncomingUntil,
            NextPlan,
            Until,
            CoverUntil,
            NextSupport;
        public V2 Home,
            Goal;
        public Job OldJob;
        public List<V2> Route = new List<V2>();
        public int Cursor;
    }

    public static class CounterSniper
    {
        public static V2 Scatter(BattleSimulation b, V2 point, float radius)
        {
            if (radius <= 0)
                return point;
            float angle = b.Random.Value() * (float)Math.PI * 2;
            return point
                + new V2((float)Math.Cos(angle), (float)Math.Sin(angle))
                    * ((float)Math.Sqrt(b.Random.Value()) * radius);
        }

        static Dictionary<int, SniperEstimate> Intel(BattleSimulation b, Side side) =>
            side == Side.Green ? b.SniperIntel.Green : b.SniperIntel.Red;

        public static void Incoming(BattleSimulation b, Soldier victim, Bullet bullet)
        {
            if (
                !bullet.SniperShot
                || bullet.Ricocheted
                || victim.Side == bullet.Side
                || !victim.Alive
                || !victim.Deployed
            )
                return;
            var intel = Intel(b, victim.Side);
            SniperEstimate entry;
            if (!intel.TryGetValue(bullet.ShooterId, out entry))
            {
                entry = new SniperEstimate();
                intel[bullet.ShooterId] = entry;
            }

            if (entry.Contact == null || bullet.ShotId > entry.Shot)
            {
                bool reset = entry.Contact == null || V2.Distance(entry.Origin, bullet.SourcePosition) >= 6f;
                if (reset)
                {
                    entry.Origin = bullet.SourcePosition;
                    float angle = b.Random.Value() * (float)Math.PI * 2;
                    entry.Bias = new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * b.Random.Range(.4f, .95f);
                }

                float radius = reset ? 5 : Math.Max(0, entry.Contact.Uncertainty - 1);
                V2 estimate = bullet.SourcePosition + entry.Bias * radius;
                estimate = new V2(
                    Util.Clamp(estimate.X, .1f, b.Map.Columns - .1f),
                    Util.Clamp(estimate.Y, .1f, b.Map.Rows - .1f)
                );
                entry.Shot = bullet.ShotId;
                entry.Contact = new Contact
                {
                    EnemyId = bullet.ShooterId,
                    Position = estimate,
                    SeenAt = b.Time,
                    FireSeenAt = b.Time,
                    Confidence = .7f,
                    Reported = true,
                    Inferred = true,
                    Uncertainty = radius,
                    TargetPriority = 1,
                };
            }

            // The origin is event evidence only. Decisions receive this uncertain contact,
            // never the hidden soldier's current coordinates or movement.
            Contact known = entry.Contact;
            Contact direct;
            if (
                victim.Contacts.TryGetValue(bullet.ShooterId, out direct)
                && victim.Visible.Contains(bullet.ShooterId)
                && direct.Visual
                && b.Time - direct.SeenAt <= b.Config.PerceptionPeriod * 2
            )
                known = direct;
            Publish(victim.Contacts, known);
            Publish(b.Team(victim).Contacts, known);
            if (V2.Distance(victim.Position, known.Position) + known.Uncertainty <= b.WeaponRange(victim))
                return;
            var action = victim.CounterSniper;
            if (
                action.Phase == CounterSniperPhase.Cover
                || action.Phase == CounterSniperPhase.Approach
                || action.Phase == CounterSniperPhase.Return
            )
                End(b, victim);
            action.Threat = known.Copy();
            action.IncomingUntil = b.Time + 12;
            victim.NextDecision = Math.Min(victim.NextDecision, b.Time);
            b.Diagnostics.Add(
                b,
                victim,
                "report",
                "CounterSniper",
                "Дальний снайперский огонь; погрешность " + known.Uncertainty.ToString("0") + " клеток"
            );
        }

        static void Publish(Dictionary<int, Contact> contacts, Contact incoming)
        {
            Contact old;
            if (
                !contacts.TryGetValue(incoming.EnemyId, out old)
                || old.SeenAt < incoming.SeenAt
                || old.SeenAt == incoming.SeenAt && incoming.Uncertainty < old.Uncertainty
            )
                contacts[incoming.EnemyId] = incoming.Copy();
        }

        static bool Free(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            return s.Deployed
                && s.Available
                && !s.Busy
                && !s.Broken
                && !s.VehicleRally
                && !s.VehicleDisperse
                && !s.GrenadeEscape.Active
                && s.RescuerId < 0
                && s.RescueTargetId < 0
                && s.SupplyDonorId < 0
                && s.SupplyReceiverId < 0
                && s.LootGroundId < 0
                && s.LootTargetId < 0
                && s.WeaponAidTargetId < 0
                && s.LauncherPickupId < 0
                && s.HandsBusyUntil <= b.Time
                && !TacticalOrders.Emergency(q)
                && !WithdrawalTactics.Active(q)
                && !q.Breakthrough.Active;
        }

        public static bool Reserved(BattleSimulation b, Soldier s)
        {
            return Free(b, s)
                && (s.CounterSniper.Phase != CounterSniperPhase.None || s.CounterSniper.IncomingUntil > b.Time);
        }

        public static bool AreaFire(BattleSimulation b, Soldier s, Contact c)
        {
            var a = s.CounterSniper;
            return c != null
                && Free(b, s)
                && a.Phase == CounterSniperPhase.Cover
                && a.Threat != null
                && a.Threat.EnemyId == c.EnemyId
                && a.Until > b.Time
                && b.Time - c.SeenAt <= 8;
        }

        public static bool SafeFireLane(BattleSimulation b, Soldier s, Contact c)
        {
            V2 delta = c.Position - s.Position;
            float length = delta.Length;
            if (length < .1f)
                return false;
            V2 direction = delta.Normalized;
            foreach (var ally in b.Soldiers)
                if (ally != s && ally.Side == s.Side && ally.Alive && ally.Deployed)
                {
                    float along = V2.Dot(ally.Position - s.Position, direction);
                    if (along <= 0 || along > length + c.Uncertainty + 1.5f)
                        continue;
                    if (
                        V2.SegmentDistance(ally.Position, s.Position, c.Position)
                        < .5f + (c.Uncertainty + 1.5f) * Math.Min(1, along / length)
                    )
                        return false;
                }

            return true;
        }

        static Contact Refresh(BattleSimulation b, Soldier s)
        {
            var a = s.CounterSniper;
            if (a.Threat == null)
                return null;
            Contact c;
            var best = a.Threat;
            if (s.Contacts.TryGetValue(best.EnemyId, out c))
                best = Knowledge.Newer(best, c);
            if (b.Team(s).Contacts.TryGetValue(best.EnemyId, out c))
                best = Knowledge.Newer(best, c);
            SniperEstimate e;
            if (Intel(b, s.Side).TryGetValue(best.EnemyId, out e))
                best = Knowledge.Newer(best, e.Contact);
            a.Threat = best.Copy();
            Publish(s.Contacts, best);
            return best;
        }

        static float Risk(BattleSimulation b, V2 p, Contact c)
        {
            float worst = 0;
            for (int i = 0; i < 5; i++)
            {
                V2 source = c.Position;
                if (i > 0)
                {
                    float angle = (i - 1) * (float)Math.PI * .5f;
                    source += new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * c.Uncertainty;
                }

                float v = b.Map.LineOfSight(p, source)
                    ? (1 - b.Map.Protection(p, source)) * b.SmokeTransmission(p, source)
                    : 0;
                worst = Math.Max(worst, v);
            }

            return worst;
        }

        public static bool SafeRoute(BattleSimulation b, Soldier s, List<V2> route, Contact threat)
        {
            V2 previous = s.Position;
            float initial = Risk(b, s.Position, threat),
                exposure = PositionLogic.Exposure(b, s, s.Position);
            foreach (var end in route)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(V2.Distance(previous, end) * 2));
                for (int i = 1; i <= steps; i++)
                {
                    V2 p = previous + (end - previous) * (i / (float)steps);
                    if (Risk(b, p, threat) > initial + .12f || PositionLogic.Exposure(b, s, p) > exposure + .15f)
                        return false;
                    foreach (var other in Knowledge.Contacts(b, s))
                        if (
                            other.EnemyId != threat.EnemyId
                            && PositionLogic.Recent(b, other)
                            && b.Map.LineOfSight(p, other.Position)
                            && b.SmokeTransmission(p, other.Position) > .3f
                        )
                        {
                            float now = V2.Distance(p, other.Position),
                                before = V2.Distance(s.Position, other.Position);
                            if (
                                now < b.Config.WeaponRange + 2 + other.Uncertainty
                                && now < before - .5f
                                && Risk(b, p, other) > .5f
                            )
                                return false;
                        }
                }

                previous = end;
            }

            return true;
        }

        static void Begin(BattleSimulation b, Soldier s, CounterSniperPhase phase, V2 goal, List<V2> path)
        {
            var a = s.CounterSniper;
            if (a.Phase == CounterSniperPhase.None)
            {
                a.Home = s.Position;
                a.OldJob = s.Job;
            }

            TacticalAI.Stop(s);
            a.Phase = phase;
            a.Goal = goal;
            a.Route = path ?? new List<V2>();
            a.Cursor = 0;
            a.Until = b.Time + (phase == CounterSniperPhase.Cover ? 10 : 20);
            s.OrderAnchor = s.Destination = goal;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.SupportReady = false;
            s.Job = phase == CounterSniperPhase.Cover ? Job.Cover : Job.Reposition;
            s.Reason =
                phase == CounterSniperPhase.Escape ? "Снайпер вне дальности: разрываю линию огня"
                : phase == CounterSniperPhase.Return ? "Товарищ отошёл — возвращаюсь к своему рубежу"
                : "Прикрываю отход от снайпера";
        }

        static void End(BattleSimulation b, Soldier s)
        {
            var a = s.CounterSniper;
            a.Phase = CounterSniperPhase.None;
            a.Route.Clear();
            a.Cursor = 0;
            a.Victim = -1;
            s.NextDecision = 0;
            b.Team(s).NextPlan = 0;
        }

        static bool Escape(BattleSimulation b, Soldier s, Contact threat)
        {
            if (s.CounterSniper.Phase != CounterSniperPhase.None)
                return false;
            float risk = Risk(b, s.Position, threat),
                best = float.MinValue;
            V2 goal = s.Position;
            List<V2> chosen = null;
            V2 forward = (threat.Position - s.Position).Normalized;
            for (int ring = 0; ring < 4; ring++)
            for (int i = 0; i < 12; i++)
            {
                float angle = i * (float)Math.PI / 6;
                V2 p = b.Map.NearestOpen(
                    s.Position + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * (2.5f + ring * 2f)
                );
                if (
                    V2.Dot(p - s.Position, forward) > .6f
                    || PositionLogic.Occupied(b, s, p)
                    || V2.Distance(s.Position, p) < 1
                )
                    continue;
                float danger = Risk(b, p, threat);
                if (danger > risk - .08f)
                    continue;
                var path = LossMemory.Find(
                    b,
                    s,
                    p,
                    v => PositionLogic.Exposure(b, s, v) * 3 + (PositionLogic.Occupied(b, s, v) ? 15 : 0)
                );
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 16 || !SafeRoute(b, s, path, threat))
                    continue;
                float score = (risk - danger) * 10 - SupportAI.PathLength(s.Position, path) * .3f;
                if (score > best)
                {
                    best = score;
                    goal = p;
                    chosen = path;
                }
            }

            if (chosen == null)
                return false;
            Begin(b, s, CounterSniperPhase.Escape, goal, chosen);
            return true;
        }

        static bool Heavy(BattleSimulation b, Soldier s, Contact c)
        {
            float distance = V2.Distance(s.Position, c.Position);
            bool ugl = s.Underbarrel && GrenadePockets.Count(s, 2) > 0 && distance <= ArcedOrdnance.Range(b, 2);
            bool rocket =
                s.Role == Role.Grenadier
                && s.Rockets > 0
                && distance <= SpecialWeapons.Range(b)
                && b.Map.LineOfSight(s.Position, c.Position);
            return ugl || rocket;
        }

        static bool Approach(BattleSimulation b, Soldier helper, Contact threat, out V2 goal, out List<V2> route)
        {
            goal = helper.Position;
            route = null;
            V2 forward = (threat.Position - helper.Position).Normalized,
                side = new V2(-forward.Y, forward.X);
            for (int i = 0; i < 4; i++)
            {
                V2 p = b.Map.NearestOpen(helper.Position + forward * (i < 2 ? 2 : 4) + side * (i % 2 == 0 ? 3 : -3));
                if (
                    PositionLogic.Occupied(b, helper, p)
                    || !PositionLogic.Protected(b, helper, p)
                    || V2.Distance(p, threat.Position) > b.WeaponRange(helper) - .5f
                    || !b.Map.LineOfSight(p, threat.Position)
                )
                    continue;
                var path = Pathfinder.Find(
                    b.Map,
                    helper.Position,
                    p,
                    v => PositionLogic.Exposure(b, helper, v) * 3 + (PositionLogic.Occupied(b, helper, v) ? 15 : 0)
                );
                b.Stats.Paths++;
                if (
                    path.Count == 0
                    || SupportAI.PathLength(helper.Position, path) > 8
                    || !SafeRoute(b, helper, path, threat)
                )
                    continue;
                goal = p;
                route = path;
                return true;
            }

            return false;
        }

        static void Request(BattleSimulation b, Soldier victim, Contact threat)
        {
            var a = victim.CounterSniper;
            if (b.Time < a.NextSupport)
                return;
            a.NextSupport = b.Time + 8;
            b.Say(victim, "Снайпер! Нужен дым или прикрытие!", true);
            SmokeTactics.Request(b, b.Team(victim), victim, victim.Position, SmokePurpose.Withdrawal);
            // At most one fire helper per victim and one per external detachment.
            foreach (var s in b.Soldiers)
                if (s.CounterSniper.Victim == victim.Id && s.CounterSniper.Phase != CounterSniperPhase.None)
                    return;
            Soldier chosen = null;
            V2 goal = new V2();
            List<V2> route = null;
            float best = float.MinValue;
            foreach (var helper in b.Soldiers)
            {
                if (
                    helper.Side != victim.Side
                    || helper.Id == victim.Id
                    || !Free(b, helper)
                    || RedCounterattack.Retreating(b, helper)
                    || helper.CounterSniper.Phase != CounterSniperPhase.None
                    || helper.CounterSniper.IncomingUntil > b.Time
                    || helper.FlagResponseUntil > b.Time
                    || helper.Health < 45
                    || helper.Suppression > .45f
                    || (helper.Ammo + helper.Reserve <= 0 && !Heavy(b, helper, threat))
                    || helper.Sniper && threat.Inferred && !Heavy(b, helper, threat)
                    || helper.HelpUntil > b.Time
                    || helper.ReloadUntil > b.Time
                    || !GroupCoordination.CanLend(b, helper, victim)
                    || V2.Distance(helper.Position, victim.Position) > 18
                )
                    continue;
                bool borrowed = helper.DefenderGroup != victim.DefenderGroup,
                    occupied = false;
                if (borrowed)
                    foreach (var other in b.Soldiers)
                        if (
                            other.DefenderGroup == helper.DefenderGroup
                            && other.CounterSniper.Phase != CounterSniperPhase.None
                        )
                            occupied = true;
                if (occupied)
                    continue;
                bool heavy = Heavy(b, helper, threat),
                    fire = PositionLogic.CanEngage(b, helper, helper.Position, threat);
                V2 p = helper.Position;
                List<V2> path = null;
                if (!heavy && !fire && (!Approach(b, helper, threat, out p, out path) || helper.Role == Role.Medic))
                    continue;
                float score =
                    (
                        heavy ? 30
                        : fire ? 20
                        : 5
                    )
                    - V2.Distance(helper.Position, victim.Position)
                    - (borrowed ? 3 : 0);
                if (score <= best)
                    continue;
                best = score;
                chosen = helper;
                goal = p;
                route = path;
            }

            if (chosen == null)
            {
                victim.Reason = "Снайпер вне дальности: запрашиваю дым, безопасной поддержки пока нет";
                return;
            }

            chosen.CounterSniper.Threat = threat.Copy();
            Publish(chosen.Contacts, threat);
            chosen.CounterSniper.Victim = victim.Id;
            Begin(b, chosen, route == null ? CounterSniperPhase.Cover : CounterSniperPhase.Approach, goal, route);
            b.Log(victim.Side, chosen.Name + ": поддерживаю " + victim.Name + " против дальнего огня");
        }

        static bool Return(BattleSimulation b, Soldier s)
        {
            var a = s.CounterSniper;
            if (V2.Distance(s.Position, a.Home) < .8f)
            {
                End(b, s);
                return false;
            }

            var path = LossMemory.Find(b, s, a.Home, v => PositionLogic.Exposure(b, s, v) * 3);
            b.Stats.Paths++;
            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 10 || !SafeRoute(b, s, path, a.Threat))
            {
                End(b, s);
                s.Reason = "Обратный путь опасен — остаюсь в укрытии и возвращаюсь к задаче";
                return false;
            }

            Begin(b, s, CounterSniperPhase.Return, a.Home, path);
            return true;
        }

        public static bool CanCloseIn(BattleSimulation b, Soldier s, Contact threat)
        {
            if (
                threat == null
                || !Free(b, s)
                || s.Bleeding
                || s.Health < 45
                || s.Suppression > .5f
                || s.Ammo + s.Reserve <= 0
                || s.HelpUntil > b.Time
                || s.HelpRetreatUntil > b.Time
                || s.FlagResponseUntil > b.Time
                || RedCounterattack.Retreating(b, s)
                || b.Team(s).Style == CombatStyle.Withdraw
                || ObjectiveOpportunity.IsCapturer(b, s)
                || b.Time - threat.SeenAt > 8
                || threat.Confidence < .35f
                || threat.Uncertainty > 5
            )
                return false;
            // Only observations, never the global number or positions of living enemies.
            foreach (var ally in b.Soldiers)
                if (ally.Side == s.Side && ally.Deployed && ally.Available && b.Team(s).Contains(ally))
                    foreach (int id in ally.Visible)
                        if (id != threat.EnemyId)
                            return false;
            return true;
        }

        static bool CloseIn(BattleSimulation b, Soldier s, Contact threat)
        {
            if (!CanCloseIn(b, s, threat))
                return false;
            int movers = 0;
            foreach (var ally in b.Soldiers)
                if (
                    ally != s
                    && ally.Side == s.Side
                    && b.Team(s).Contains(ally)
                    && ally.Available
                    && ally.CounterSniper.Phase == CounterSniperPhase.CloseIn
                )
                    movers++;
            if (movers >= 2)
                return false;
            float distance = V2.Distance(s.Position, threat.Position);
            if (distance + threat.Uncertainty <= b.WeaponRange(s) && PositionLogic.CanEngage(b, s, s.Position, threat))
                return false;
            V2 forward = (threat.Position - s.Position).Normalized,
                side = new V2(-forward.Y, forward.X);
            float best = float.MinValue;
            V2 goal = s.Position;
            List<V2> chosen = null;
            for (int i = 0; i < 6; i++)
            {
                float step = i < 3 ? 3 : 5,
                    offset = (i % 3 - 1) * 2;
                V2 p = b.Map.NearestOpen(s.Position + forward * step + side * offset);
                if (
                    PositionLogic.Occupied(b, s, p)
                    || V2.Distance(p, threat.Position) > distance - 1
                    || V2.Distance(p, threat.Position) < 4
                )
                    continue;
                var path = LossMemory.Find(
                    b,
                    s,
                    p,
                    v => PositionLogic.Exposure(b, s, v) * 4 + (PositionLogic.Occupied(b, s, v) ? 15 : 0)
                );
                b.Stats.Paths++;
                if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > 7 || !SafeRoute(b, s, path, threat))
                    continue;
                float score =
                    distance
                    - V2.Distance(p, threat.Position)
                    + (PositionLogic.Protected(b, s, p) ? 3 : 0)
                    - SupportAI.PathLength(s.Position, path) * .25f;
                if (score > best)
                {
                    best = score;
                    goal = p;
                    chosen = path;
                }
            }

            if (chosen == null)
                return false;
            s.CounterSniper.Threat = threat.Copy();
            s.CounterSniper.Victim = -1;
            Begin(b, s, CounterSniperPhase.CloseIn, goal, chosen);
            s.Reason = "Других целей не видно: коротко сближаюсь со снайпером";
            b.Diagnostics.Add(b, s, "decision", "CounterSniper", "Сближение со снайпером по известному сектору");
            return true;
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var a = s.CounterSniper;
            if (!Free(b, s))
            {
                if (a.Phase != CounterSniperPhase.None)
                    End(b, s);
                return false;
            }

            if (a.Phase == CounterSniperPhase.None && a.IncomingUntil <= b.Time)
            {
                if (b.Time < a.NextPlan)
                    return false;
                a.NextPlan = b.Time + 2;
                foreach (var estimate in Intel(b, s.Side).Values)
                    if (CloseIn(b, s, estimate.Contact))
                        return true;
                return false;
            }

            var threat = Refresh(b, s);
            if (threat == null)
                return false;
            if (a.Phase != CounterSniperPhase.None)
                return true;
            if (
                a.IncomingUntil <= b.Time
                || b.Time - threat.SeenAt > 12
                || V2.Distance(s.Position, threat.Position) + threat.Uncertainty <= b.WeaponRange(s)
            )
                return false;
            if (b.Time >= a.NextPlan)
            {
                a.NextPlan = b.Time + 2;
                if (!CloseIn(b, s, threat))
                    Escape(b, s, threat);
                Request(b, s, threat);
            }

            if (a.Phase == CounterSniperPhase.None)
            {
                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.Reason = "Дальний огонь: ищу выход из сектора, запросил поддержку";
            }

            return true;
        }

        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            var a = s.CounterSniper;
            if (a.Phase == CounterSniperPhase.None)
                return false;
            if (!Free(b, s))
            {
                End(b, s);
                return false;
            }

            var threat = Refresh(b, s);
            if (a.Phase == CounterSniperPhase.CloseIn && !CanCloseIn(b, s, threat))
            {
                End(b, s);
                return false;
            }

            if (a.Victim >= 0 && a.Phase != CounterSniperPhase.Return && (s.Suppression > .65f || s.Health < 40))
            {
                Return(b, s);
                return a.Phase == CounterSniperPhase.Return;
            }

            bool relief =
                a.Victim >= 0
                && (
                    a.Victim >= b.Soldiers.Count
                    || !b.Soldiers[a.Victim].Alive
                    || b.Soldiers[a.Victim].CounterSniper.IncomingUntil <= b.Time
                    || Risk(b, b.Soldiers[a.Victim].Position, threat) < .3f
                );
            if (a.Phase == CounterSniperPhase.Cover)
            {
                if (relief || b.Time >= a.Until || b.Time - threat.SeenAt > 8)
                {
                    Return(b, s);
                    return a.Phase == CounterSniperPhase.Return;
                }

                TacticalAI.Stop(s);
                s.Job = Job.Cover;
                s.SuppressUntil = b.Time + 1;
                s.Reason = "Прикрываю отход: работаю по предполагаемому сектору снайпера";
                return false;
            }

            if (b.Time >= a.Until || a.Phase == CounterSniperPhase.Approach && relief)
            {
                if (a.Phase == CounterSniperPhase.Approach)
                    Return(b, s);
                else
                    End(b, s);
                return false;
            }

            while (a.Cursor < a.Route.Count && V2.Distance(s.Position, a.Route[a.Cursor]) < .08f)
                a.Cursor++;
            if (a.Cursor >= a.Route.Count)
            {
                if (a.Phase == CounterSniperPhase.CloseIn)
                {
                    Begin(b, s, CounterSniperPhase.Cover, s.Position, null);
                    a.Home = s.Position;
                    a.Until = b.Time + 1.3f;
                    s.Reason = "Остановка после сближения: работаю по сектору снайпера";
                    return false;
                }

                if (a.Phase == CounterSniperPhase.Approach)
                    Begin(b, s, CounterSniperPhase.Cover, s.Position, null);
                else
                {
                    if (a.Phase == CounterSniperPhase.Escape)
                        a.IncomingUntil = 0;
                    End(b, s);
                }

                return false;
            }

            V2 delta = a.Route[a.Cursor] - s.Position;
            var nextPath = new List<V2> { s.Position + delta.Normalized * Math.Min(.5f, delta.Length) };
            if (!SafeRoute(b, s, nextPath, threat))
            {
                if (a.Phase == CounterSniperPhase.Approach)
                    Return(b, s);
                else
                    End(b, s);
                return false;
            }

            if (
                Locomotion.TryStep(
                    b,
                    s,
                    delta.Normalized * Math.Min(delta.Length, CombatMovement.Speed(b, s) * dt),
                    out var next
                )
            )
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.Facing = delta.Normalized;
                s.LastMoved = b.Time;
                s.Moving = true;
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            s.SupportReady = false;
            return true;
        }
    }
}
