using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Ground-truth diagnostic observer only. It never writes orders or consumes gameplay RNG.
    // A reason tagged "hypothesis" is a risk factor, NOT an established causal conclusion.
    [Serializable]
    public sealed class CriticalUnitSnapshot
    {
        public float time,
            x,
            y,
            hp,
            suppression,
            morale,
            exposureSeconds,
            coverProtection;
        public int unit,
            group,
            revision,
            pair,
            visibleEnemies,
            contacts,
            alliesWithinSix,
            enemyWithinSix,
            ammo;
        public bool deployed,
            alive,
            covered,
            underFire,
            moving,
            crouched,
            broken,
            incapacitated,
            bleeding,
            reloading;
        public string style,
            intent,
            phase,
            job,
            task,
            order,
            reason,
            execution,
            weapon,
            assignment;
    }

    [Serializable]
    public sealed class CriticalSideSnapshot
    {
        public float time,
            greenHealth,
            redHealth,
            greenSuppression,
            redSuppression,
            greenMorale,
            redMorale;
        public int greenAlive,
            redAlive,
            greenCritical,
            redCritical,
            greenPinned,
            redPinned,
            greenBroken,
            redBroken,
            greenExposed,
            redExposed;
    }

    [Serializable]
    public sealed class CriticalFollowup
    {
        public float afterSeconds,
            observedAt;
        public bool complete,
            targetAlive,
            targetIncapacitated,
            targetBroken,
            targetUnderFire,
            targetCovered;
        public float targetHealth,
            targetSuppression,
            targetMorale;
        public int alliedDeaths,
            enemyDeaths,
            alliedCriticals,
            enemyCriticals;
        public CriticalSideSnapshot state;
    }

    [Serializable]
    public sealed class CriticalDecisionTrace
    {
        public float time;
        public int soldier,
            revision;
        public string transition,
            task,
            reason;
    }

    [Serializable]
    public sealed class CriticalCombatOutcome
    {
        public int id,
            unit,
            actor,
            side,
            group,
            relatedPressureSource;
        public float at,
            healthBefore,
            suppressionBefore,
            moraleBefore,
            damage,
            pressureAgo;
        public string kind,
            cause,
            actorSide,
            certainty,
            summary;
        public CriticalUnitSnapshot before,
            atEvent,
            actorAtEvent;
        public CriticalSideSnapshot battlefieldAtEvent;
        public List<CriticalUnitSnapshot> leadup = new List<CriticalUnitSnapshot>();
        public List<CriticalUnitSnapshot> attackerLeadup = new List<CriticalUnitSnapshot>();
        public List<HistoryEvent> combatLeadup = new List<HistoryEvent>();
        public List<CriticalDecisionTrace> decisionLeadup = new List<CriticalDecisionTrace>();
        public List<string> confirmed = new List<string>();
        public List<string> hypotheses = new List<string>();
        public List<CriticalFollowup> followups = new List<CriticalFollowup>();
    }

    public sealed class CriticalOutcomeReview
    {
        public bool Enabled;
        public const int MaxEvents = 1500;
        public readonly List<CriticalCombatOutcome> Events = new List<CriticalCombatOutcome>();
        public int DroppedEvents;
        readonly Dictionary<int, List<CriticalUnitSnapshot>> previous =
            new Dictionary<int, List<CriticalUnitSnapshot>>();
        readonly Dictionary<int, int> lastPressure = new Dictionary<int, int>();
        readonly Dictionary<int, float> lastPressureTime = new Dictionary<int, float>();
        readonly Dictionary<int, bool> pinned = new Dictionary<int, bool>();
        readonly Dictionary<int, bool> broken = new Dictionary<int, bool>();
        readonly Dictionary<int, bool> openFire = new Dictionary<int, bool>();
        float nextSample;
        int nextId;
        static readonly float[] Windows = { 5f, 15f, 30f };

        static bool Exposed(BattleSimulation b, Soldier s)
        {
            return !b.Map.NearCover(s.Position);
        }

        public void Pressure(BattleSimulation b, Soldier source, Soldier victim, float suppression, float morale)
        {
            if (
                !Enabled
                || source == null
                || victim == null
                || source.Side == victim.Side
                || suppression <= 0 && morale <= 0
            )
                return;
            lastPressure[victim.Id] = source.Id;
            lastPressureTime[victim.Id] = b.Time;
        }

        public void Wound(
            BattleSimulation b,
            Soldier victim,
            Soldier shooter,
            float oldHealth,
            float damage,
            string cause = "direct_hit"
        )
        {
            if (!Enabled || oldHealth <= 20f || victim.Health <= 0 || victim.Health > 20f)
                return;
            Add(
                b,
                "CriticalWound",
                victim,
                shooter,
                cause,
                oldHealth,
                damage,
                "Здоровье упало до тяжёлого ранения (HP <= 20)"
            );
        }

        public void BleedingWound(BattleSimulation b, Soldier victim, Soldier source)
        {
            if (!Enabled)
                return;
            float prior = victim.Health;
            List<CriticalUnitSnapshot> history;
            if (previous.TryGetValue(victim.Id, out history) && history.Count > 0)
                prior = history[history.Count - 1].hp;
            Add(
                b,
                "CriticalWound",
                victim,
                source,
                "bleeding",
                prior,
                0,
                "Недееспособен после ухудшения состояния / кровопотери"
            );
        }

        public void Death(BattleSimulation b, Soldier victim, Soldier killer, string cause)
        {
            if (!Enabled)
                return;
            Add(
                b,
                "Death",
                victim,
                killer,
                cause,
                0,
                0,
                cause == "bleeding" ? "Смерть от кровопотери" : "Погиб от полученного урона"
            );
        }

        static int Group(BattleSimulation b, Soldier s)
        {
            return s.DefenderGroup == null ? -1 : b.DefenderGroups.IndexOf(s.DefenderGroup);
        }

        static CriticalUnitSnapshot Capture(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            int allies = 0,
                enemies = 0;
            foreach (var other in b.Soldiers)
            {
                if (other == s || !other.Alive || !other.Deployed || V2.Distance(s.Position, other.Position) > 6f)
                    continue;
                if (other.Side == s.Side)
                    allies++;
                else
                    enemies++;
            }

            return new CriticalUnitSnapshot
            {
                time = b.Time,
                unit = s.Id,
                group = Group(b, s),
                revision = s.OrderRevision,
                pair = s.Assignment.PairId,
                x = s.Position.X,
                y = s.Position.Y,
                hp = s.Health,
                suppression = s.Suppression,
                morale = s.Morale,
                exposureSeconds = s.ExposedTime,
                coverProtection = 0,
                visibleEnemies = s.Visible.Count,
                contacts = s.Contacts.Count,
                ammo = s.Ammo + s.Reserve,
                alliesWithinSix = allies,
                enemyWithinSix = enemies,
                deployed = s.Deployed,
                alive = s.Alive,
                covered = !Exposed(b, s),
                underFire = b.Time - s.LastIncoming <= 2f,
                moving = s.Moving,
                crouched = s.Crouched,
                broken = s.Broken,
                incapacitated = s.Incapacitated,
                bleeding = s.Bleeding,
                reloading = s.ReloadUntil > b.Time,
                style = q.Style.ToString(),
                intent = q.Intent.ToString(),
                phase = q.CommandPlan.Scheme + " / " + q.Breakthrough.Phase,
                job = s.Job.ToString(),
                task = s.Ai.Kind.ToString(),
                order = s.Order,
                reason = s.Reason,
                execution = s.Execution.Cause,
                weapon = s.GunRole.ToString() + "/" + s.WeaponVariant,
                assignment = s.Assignment.Owner + "/" + s.Assignment.Status,
            };
        }

        static CriticalSideSnapshot Sides(BattleSimulation b)
        {
            var r = new CriticalSideSnapshot { time = b.Time };
            foreach (var s in b.Soldiers)
            {
                if (!s.Deployed)
                    continue;
                bool g = s.Side == Side.Green;
                if (g)
                {
                    r.greenHealth += s.Health;
                    r.greenSuppression += s.Suppression;
                    r.greenMorale += s.Morale;
                    if (s.Alive)
                        r.greenAlive++;
                    if (s.Incapacitated && s.Alive)
                        r.greenCritical++;
                    if (s.Suppression >= .95f && s.Alive)
                        r.greenPinned++;
                    if (s.Broken && s.Alive)
                        r.greenBroken++;
                    if (s.Alive && Exposed(b, s))
                        r.greenExposed++;
                }
                else
                {
                    r.redHealth += s.Health;
                    r.redSuppression += s.Suppression;
                    r.redMorale += s.Morale;
                    if (s.Alive)
                        r.redAlive++;
                    if (s.Incapacitated && s.Alive)
                        r.redCritical++;
                    if (s.Suppression >= .95f && s.Alive)
                        r.redPinned++;
                    if (s.Broken && s.Alive)
                        r.redBroken++;
                    if (s.Alive && Exposed(b, s))
                        r.redExposed++;
                }
            }

            return r;
        }

        static int Count(int green, int red, int side)
        {
            return side == (int)Side.Green ? green : red;
        }

        void Add(
            BattleSimulation b,
            string kind,
            Soldier victim,
            Soldier actor,
            string cause,
            float beforeHealth,
            float damage,
            string summary
        )
        {
            if (Events.Count >= MaxEvents)
            {
                Events.RemoveAt(0);
                DroppedEvents++;
            }

            CriticalUnitSnapshot now = Capture(b, victim),
                earlier = now;
            List<CriticalUnitSnapshot> list;
            if (previous.TryGetValue(victim.Id, out list) && list.Count > 0)
                earlier = list[list.Count - 1];
            var e = new CriticalCombatOutcome
            {
                id = ++nextId,
                unit = victim.Id,
                actor = actor == null ? -1 : actor.Id,
                actorSide = actor == null ? "unknown" : actor.Side.ToString(),
                side = (int)victim.Side,
                group = Group(b, victim),
                at = b.Time,
                kind = kind,
                cause = cause,
                certainty = "observed_event; context is not proof of causation",
                summary = summary,
                healthBefore = beforeHealth,
                suppressionBefore = earlier.suppression,
                moraleBefore = earlier.morale,
                damage = damage,
                before = earlier,
                atEvent = now,
                actorAtEvent = actor == null ? null : Capture(b, actor),
                battlefieldAtEvent = Sides(b),
            };
            if (list != null)
                foreach (var item in list)
                    if (b.Time - item.time <= 10f)
                        e.leadup.Add(item);
            List<CriticalUnitSnapshot> attackers;
            if (actor != null && previous.TryGetValue(actor.Id, out attackers))
                foreach (var item in attackers)
                    if (b.Time - item.time <= 10f)
                        e.attackerLeadup.Add(item);
            // Attach relevant last 10 seconds of factual combat history and issued AI task traces.
            // No new game observations or RNG: these logs already exist when diagnostics are enabled.
            for (int i = b.History.Events.Count - 1; i >= 0 && e.combatLeadup.Count < 24; i--)
            {
                var hit = b.History.Events[i];
                if (b.Time - hit.time > 10f)
                    break;
                if (
                    hit.actor == victim.Id
                    || hit.target == victim.Id
                    || actor != null && (hit.actor == actor.Id || hit.target == actor.Id)
                )
                    e.combatLeadup.Insert(0, hit);
            }

            for (int i = b.Diagnostics.Events.Count - 1; i >= 0 && e.decisionLeadup.Count < 24; i--)
            {
                var decision = b.Diagnostics.Events[i];
                if (b.Time - decision.Time > 10f)
                    break;
                if (decision.Soldier == victim.Id || actor != null && decision.Soldier == actor.Id)
                    e.decisionLeadup.Insert(
                        0,
                        new CriticalDecisionTrace
                        {
                            time = decision.Time,
                            soldier = decision.Soldier,
                            revision = decision.Revision,
                            transition = decision.Event,
                            task = decision.Task,
                            reason = decision.Reason,
                        }
                    );
            }

            int source;
            float sourceAt;
            if (
                lastPressure.TryGetValue(victim.Id, out source)
                && lastPressureTime.TryGetValue(victim.Id, out sourceAt)
                && b.Time - sourceAt <= 5f
            )
            {
                e.relatedPressureSource = source;
                e.pressureAgo = b.Time - sourceAt;
                e.confirmed.Add("За последние 5 секунд зарегистрировано давление от бойца ID=" + source);
            }
            else
                e.relatedPressureSource = -1;
            if (actor != null)
            {
                e.confirmed.Add("Идентификатор непосредственного источника: " + actor.Id + " (" + actor.Side + ")");
                if (actor.Side == victim.Side)
                    e.confirmed.Add("Дружественный урон или собственный источник: требуется разбор");
                if (!Exposed(b, victim))
                    e.atEvent.coverProtection = b.Map.Protection(victim.Position, actor.Position);
                float dx = actor.Position.X - victim.Position.X,
                    dy = actor.Position.Y - victim.Position.Y;
                V2 relative = new V2(dx, dy);
                if (relative.Length > .01f && victim.Facing.Length > .01f)
                {
                    float facing = V2.Dot(relative.Normalized, victim.Facing.Normalized);
                    if (facing < -.55f)
                        e.hypotheses.Add("Источник сзади относительно направления взгляда");
                    else if (facing < .3f)
                        e.hypotheses.Add("Источник сбоку относительно направления взгляда");
                }
            }

            if (cause == "bleeding")
                e.confirmed.Add(
                    "Смерть наступила при тикающем кровотечении; источник исходного ранения не обязательно непосредственная причина задержки помощи"
                );
            if (Exposed(b, victim))
                e.hypotheses.Add("Вне ближайшего укрытия в момент события");
            if (earlier.moving || victim.Moving)
                e.hypotheses.Add("Движение во время опасного эпизода");
            if (earlier.reloading)
                e.hypotheses.Add("Перезарядка незадолго до события");
            if (earlier.suppression >= .7f)
                e.hypotheses.Add("Сильное подавление перед событием");
            if (earlier.morale <= .25f)
                e.hypotheses.Add("Низкая мораль перед событием");
            if (earlier.enemyWithinSix > earlier.alliesWithinSix)
                e.hypotheses.Add("Локальное численное превосходство противника в радиусе 6 клеток");
            if (victim.Visible.Count == 0 && actor != null)
                e.hypotheses.Add("Пострадавший не видел ни одного врага в момент события");
            Events.Add(e);
        }

        public void Sample(BattleSimulation b)
        {
            if (!Enabled)
                return;
            // Detect crossings, not continuous exposure; avoid filling logs with repeated states.
            foreach (var s in b.Soldiers)
            {
                if (!s.Deployed)
                    continue;
                bool full = s.Alive && s.Suppression >= .95f,
                    collapse = s.Alive && s.Broken;
                bool exposed = s.Alive && !s.Incapacitated && Exposed(b, s) && b.Time - s.LastIncoming <= 1.5f;
                bool old;
                if (pinned.TryGetValue(s.Id, out old) && full && !old)
                    Add(
                        b,
                        "FullyPinned",
                        s,
                        RecentPressure(b, s),
                        "suppression_threshold",
                        s.Health,
                        0,
                        "Подавление достигло 95%"
                    );
                pinned[s.Id] = full;
                if (broken.TryGetValue(s.Id, out old) && collapse && !old)
                    Add(
                        b,
                        "MoraleCollapse",
                        s,
                        RecentPressure(b, s),
                        "morale_broken",
                        s.Health,
                        0,
                        "Боец перешёл в состояние морального срыва"
                    );
                broken[s.Id] = collapse;
                if (openFire.TryGetValue(s.Id, out old) && exposed && !old)
                    Add(
                        b,
                        "CaughtInOpen",
                        s,
                        RecentPressure(b, s),
                        "incoming_while_exposed",
                        s.Health,
                        0,
                        "Под огнём вне ближайшего укрытия"
                    );
                openFire[s.Id] = exposed;
            }

            // Events recorded during this tick receive 5s/15s/30s follow-up outcomes.
            foreach (var e in Events)
            {
                while (e.followups.Count < Windows.Length && b.Time - e.at >= Windows[e.followups.Count])
                {
                    AppendFollowup(b, e, Windows[e.followups.Count], true);
                }
            }

            if (b.Time < nextSample)
                return;
            nextSample = b.Time + 1f;
            foreach (var s in b.Soldiers)
            {
                List<CriticalUnitSnapshot> list;
                if (!previous.TryGetValue(s.Id, out list))
                {
                    list = new List<CriticalUnitSnapshot>(12);
                    previous[s.Id] = list;
                }

                if (list.Count >= 12)
                    list.RemoveAt(0);
                list.Add(Capture(b, s));
            }
        }

        Soldier RecentPressure(BattleSimulation b, Soldier victim)
        {
            int id;
            float when;
            if (
                lastPressure.TryGetValue(victim.Id, out id)
                && lastPressureTime.TryGetValue(victim.Id, out when)
                && b.Time - when <= 5f
                && id >= 0
                && id < b.Soldiers.Count
            )
                return b.Soldiers[id];
            return null;
        }

        void AppendFollowup(BattleSimulation b, CriticalCombatOutcome e, float window, bool complete)
        {
            var snapshot = Sides(b);
            Soldier victim = e.unit >= 0 && e.unit < b.Soldiers.Count ? b.Soldiers[e.unit] : null;
            int initialAlly = Count(e.battlefieldAtEvent.greenAlive, e.battlefieldAtEvent.redAlive, e.side);
            int initialEnemy = Count(e.battlefieldAtEvent.redAlive, e.battlefieldAtEvent.greenAlive, e.side);
            int finalAlly = Count(snapshot.greenAlive, snapshot.redAlive, e.side),
                finalEnemy = Count(snapshot.redAlive, snapshot.greenAlive, e.side);
            int initialCritical = Count(e.battlefieldAtEvent.greenCritical, e.battlefieldAtEvent.redCritical, e.side);
            int initialEnemyCritical = Count(
                e.battlefieldAtEvent.redCritical,
                e.battlefieldAtEvent.greenCritical,
                e.side
            );
            int finalCritical = Count(snapshot.greenCritical, snapshot.redCritical, e.side),
                finalEnemyCritical = Count(snapshot.redCritical, snapshot.greenCritical, e.side);
            e.followups.Add(
                new CriticalFollowup
                {
                    afterSeconds = window,
                    observedAt = b.Time,
                    complete = complete,
                    state = snapshot,
                    targetAlive = victim != null && victim.Alive,
                    targetIncapacitated = victim != null && victim.Incapacitated,
                    targetBroken = victim != null && victim.Broken,
                    targetUnderFire = victim != null && b.Time - victim.LastIncoming <= 2f,
                    targetCovered = victim != null && b.Map.NearCover(victim.Position),
                    targetHealth = victim == null ? 0 : victim.Health,
                    targetSuppression = victim == null ? 0 : victim.Suppression,
                    targetMorale = victim == null ? 0 : victim.Morale,
                    alliedDeaths = Math.Max(0, initialAlly - finalAlly),
                    enemyDeaths = Math.Max(0, initialEnemy - finalEnemy),
                    alliedCriticals = Math.Max(0, finalCritical - initialCritical),
                    enemyCriticals = Math.Max(0, finalEnemyCritical - initialEnemyCritical),
                }
            );
        }

        // Export in-progress windows separately; do not mislabel a 3-second result as 5 seconds.
        public List<CriticalCombatOutcome> Export(BattleSimulation b)
        {
            var result = new List<CriticalCombatOutcome>(Events.Count);
            foreach (var e in Events)
            {
                var copy = new CriticalCombatOutcome
                {
                    id = e.id,
                    unit = e.unit,
                    actor = e.actor,
                    side = e.side,
                    group = e.group,
                    relatedPressureSource = e.relatedPressureSource,
                    at = e.at,
                    healthBefore = e.healthBefore,
                    suppressionBefore = e.suppressionBefore,
                    moraleBefore = e.moraleBefore,
                    damage = e.damage,
                    pressureAgo = e.pressureAgo,
                    kind = e.kind,
                    cause = e.cause,
                    actorSide = e.actorSide,
                    certainty = e.certainty,
                    summary = e.summary,
                    before = e.before,
                    atEvent = e.atEvent,
                    actorAtEvent = e.actorAtEvent,
                    battlefieldAtEvent = e.battlefieldAtEvent,
                    leadup = e.leadup,
                    attackerLeadup = e.attackerLeadup,
                    combatLeadup = e.combatLeadup,
                    decisionLeadup = e.decisionLeadup,
                    confirmed = e.confirmed,
                    hypotheses = e.hypotheses,
                    followups = new List<CriticalFollowup>(e.followups),
                };
                if (copy.followups.Count < Windows.Length)
                {
                    float window = Windows[copy.followups.Count];
                    if (b.Time > e.at)
                    {
                        AppendFollowup(b, copy, window, false);
                    }
                }

                result.Add(copy);
            }

            return result;
        }
    }
}
