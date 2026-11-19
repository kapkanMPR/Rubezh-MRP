using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum SmokePurpose
    {
        ScreenThreat,
        Route,
        Rescue,
        Withdrawal,
    }

    public sealed class ThreatArea
    {
        public V2 Position;
        public float SeenAt,
            Confidence;
        public int EnemyId = -1;
        public bool Inferred;
    }

    public sealed class SmokeIntent
    {
        public int Carrier = -1,
            Subject = -1,
            Revision;
        public V2 Destination,
            Point,
            Origin;
        public float Created,
            Score,
            Gain;
        public SmokePurpose Purpose;
    }

    public sealed class SmokeState
    {
        public float CurtainNext,
            CurtainReady;
        public int CurtainRevision;
        public readonly HashSet<int> CurtainMembers = new HashSet<int>();
        public readonly Dictionary<int, List<V2>> AssaultRoutes = new Dictionary<int, List<V2>>();
        public readonly Dictionary<int, float> AssaultReview = new Dictionary<int, float>();
        public readonly Dictionary<int, float> WallHands = new Dictionary<int, float>();
        public float WallChoiceNext;
        public int WallChoices;
        public int SequenceStage,
            SequenceRevision = -1,
            SequenceThreat = -1;
        public float SequenceAt,
            NextRefill;
        public readonly Dictionary<int, V2> SequenceOrigins = new Dictionary<int, V2>();
        public V2 SequenceAxis;
        public SmokeIntent Pending;
        public float NextReview;
        public int Cursor;
        public string Reason = "";
    }

    public sealed class SmokeEffect
    {
        public int Subject,
            Carrier,
            Revision;
        public Side Side;
        public V2 Point,
            From,
            Threat;
        public float Born,
            Ready,
            Until,
            Before,
            ExpectedGain;
        public bool Evaluated;
    }

    // A single entry point owns validation, inventory, cooldown, flight reservation
    // and feedback. Threat inputs are reports/memory/observed noise, never enemy units.
    public static class SmokeTactics
    {
        const float Radius = 2.8f;

        static bool Emergency(BattleSimulation b, Soldier s, SmokePurpose purpose)
        {
            return purpose == SmokePurpose.Rescue
                || purpose == SmokePurpose.Withdrawal
                || s.Suppression > .82f
                || s.Health < 30;
        }

        static bool CanThrow(BattleSimulation b, Soldier s, bool emergency)
        {
            return !(
                    s != null
                    && s.Side == Side.Green
                    && (QuietRecon.Active(b) || ReconOpening.Preparing(b))
                    && !emergency
                )
                && s != null
                && s.Available
                && s.Deployed
                && !s.Broken
                && !s.Busy
                && s.SupplyReceiverId < 0
                && s.RescuerId < 0
                && s.ReloadUntil <= b.Time
                && s.HandsBusyUntil <= b.Time
                && s.SmokeReadyAt <= b.Time
                && b.Time >= s.ConcussionReactUntil
                && !BreakthroughTactics.Assigned(b, s)
                && s.Suppression < .88f
                && s.SmokeCharges > 0
                && (emergency || !s.Sniper || s.SmokeCharges > 1);
        }

        public static float Transmission(V2 from, V2 to, V2 point, float born, float at)
        {
            return Math.Max(.035f, (float)Math.Exp(-Depth(from, to, point, born, born + 20, Radius, at)));
        }

        static float Chord(float radius, float lateral, float along, float length)
        {
            if (lateral >= radius)
                return 0;
            float h = (float)Math.Sqrt(radius * radius - lateral * lateral);
            return Math.Max(0, Math.Min(length, along + h) - Math.Max(0, along - h));
        }

        // Optical density is lower in the outer fringe; the central plume stays opaque.
        public static float Depth(V2 from, V2 to, V2 center, float born, float until, float radius, float at)
        {
            if (at <= born || at >= until)
                return 0;
            var delta = to - from;
            float length = delta.Length;
            if (length < .0001f)
                return 0;
            float age = Util.Clamp((at - born) / 1.8f, 0, 1),
                fade = Util.Clamp((until - at) / 3, 0, 1),
                r = radius * (.35f + .65f * age);
            var dir = delta / length;
            float along = V2.Dot(center - from, dir),
                side = V2.Distance(center, from + dir * along);
            return age * fade * (.55f * Chord(r, side, along, length) + .60f * Chord(.72f * r, side, along, length));
        }

        // Inspect a nearby visible hostile screen before stepping deeper. Keep escape
        // and rescue routes available. Steer around the rim only toward a walkable flank.
        public static V2 CautiousStep(BattleSimulation b, Soldier s, V2 step)
        {
            if (
                b.Smoke.Count == 0
                || s.GrenadeEscape.Active
                || s.RescuerId >= 0
                || s.Broken
                || s.Health < 30
                || step.Length < .00001f
            )
                return step;
            foreach (var c in b.Smoke)
            {
                if (c.SourceSide < 0 || c.SourceSide == (int)s.Side || c.Until - b.Time < 2 || c.Born > b.Time)
                    continue;
                var outwards = s.Position - c.Position;
                float dist = outwards.Length;
                float r = c.Radius * (.35f + .65f * Util.Clamp((b.Time - c.Born) / 1.8f, 0, 1));
                if (dist > r + .85f || dist < .001f)
                    continue;
                var normal = outwards / dist;
                if (V2.Dot(step, normal) >= 0)
                    continue;
                var edge = c.Position + normal * Math.Min(r * .98f, dist);
                if (
                    !b.Map.LineOfSight(s.Position, edge)
                    || dist > r && !object.ReferenceEquals(s.WatchedSmoke, c) && !DirectionalVision.InView(b, s, edge)
                )
                    continue;
                if (!object.ReferenceEquals(s.WatchedSmoke, c))
                {
                    s.WatchedSmoke = c;
                    s.SmokeCheckAt = b.Time;
                    s.SmokeFlank = 0;
                }

                s.Reason = "Вижу чужой дым: проверяю край завесы";
                if (b.Time - s.SmokeCheckAt < 1.5f)
                {
                    s.Motion.GroupWaitUntil = Math.Max(s.Motion.GroupWaitUntil, s.SmokeCheckAt + 1.5f);
                    return new V2();
                }

                var tangent = new V2(-normal.Y, normal.X);
                if (s.SmokeFlank == 0)
                    s.SmokeFlank = V2.Dot(tangent, s.Destination - s.Position) >= 0 ? 1 : -1;
                // Near the rim, walk around toward a destination beyond the cloud.
                if (
                    dist > r * .70f
                    && V2.Distance(s.Destination, c.Position) > r
                    && V2.Dot(s.Destination - s.Position, normal) < -.1f
                )
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var side = tangent * (i == 0 ? s.SmokeFlank : -s.SmokeFlank);
                        if (Locomotion.WalkableSegment(b, s.Position, s.Position + side * .9f))
                        {
                            if (i != 0)
                                s.SmokeFlank = -s.SmokeFlank;
                            return side * (step.Length * .60f);
                        }
                    }
                }

                return step * .40f;
            }

            return step;
        }

        public static float Forecast(BattleSimulation b, V2 from, V2 to, float at)
        {
            float result = b.SmokeTransmissionAt(from, to, at);
            foreach (var p in b.Projectiles)
                if (p.Kind == 0)
                    result *= Transmission(from, to, p.To, p.Born + p.Duration, at);
            return Math.Max(.035f, result);
        }

        static List<ThreatArea> Threats(BattleSimulation b, Soldier s, V2 goal, bool moving)
        {
            var result = new List<ThreatArea>(12);
            var ids = new HashSet<int>();
            var q = b.Team(s);
            foreach (var c in Knowledge.Contacts(b, s))
                if (PositionLogic.Remembered(b, c) && c.SeenAt <= b.Time)
                {
                    ids.Add(c.EnemyId);
                    result.Add(
                        new ThreatArea
                        {
                            Position = c.Position,
                            EnemyId = c.EnemyId,
                            SeenAt = c.SeenAt,
                            Confidence = c.Confidence / (1 + (b.Time - c.SeenAt) * .1f),
                            Inferred = b.Time - c.SeenAt > 4,
                        }
                    );
                }

            foreach (var c in q.Observation.LastContacts.Values)
                if (!ids.Contains(c.EnemyId) && PositionLogic.Remembered(b, c))
                {
                    result.Add(
                        new ThreatArea
                        {
                            Position = c.Position,
                            EnemyId = c.EnemyId,
                            SeenAt = c.SeenAt,
                            Confidence = c.Confidence * .45f / (1 + (b.Time - c.SeenAt) * .08f),
                            Inferred = true,
                        }
                    );
                }

            foreach (var c in q.Observation.NoiseAreas)
                if (b.Time - c.SeenAt < 12 && V2.Distance(c.Position, goal) < b.Config.WeaponRange + 6)
                    result.Add(c);
            // Pure uncertainty is deliberately weak. It cannot alone justify emptying
            // inventory; recently observed cells remove this hypothesis entirely.
            if (
                moving
                && V2.Distance(s.Position, goal) > 6
                && result.Count == 0
                && V2.Distance(goal, s.Side == Side.Green ? b.Map.Objective : b.Map.Entry) < 18
                && V2.Distance(s.Position, s.Side == Side.Green ? b.Map.Entry : b.Map.Objective) > 8
            )
            {
                var axis = TacticalOrders.Axis(b, s.Side);
                var lateral = new V2(-axis.Y, axis.X);
                for (int i = -1; i <= 1; i++)
                {
                    V2 p = goal + axis * 6 + lateral * (i * 4);
                    if (b.Map.Walkable(p) && q.Observation.State(b, p) != 2 && b.Map.LineOfSight(p, goal))
                        result.Add(
                            new ThreatArea
                            {
                                Position = p,
                                SeenAt = b.Time,
                                Confidence = .16f,
                                Inferred = true,
                            }
                        );
                }
            }

            result.Sort((a, z) => z.Confidence.CompareTo(a.Confidence));
            if (result.Count > 8)
                result.RemoveRange(8, result.Count - 8);
            return result;
        }

        struct Sample
        {
            public V2 Position;
            public float Eta;
        }

        struct RiskLine
        {
            public V2 From,
                To;
            public float Weight,
                Eta;
        }

        struct FriendlyLane
        {
            public int Ally;
            public V2 From,
                To;
        }

        static List<Sample> Samples(BattleSimulation b, Soldier subject, V2 goal)
        {
            var result = new List<Sample>(7)
            {
                new Sample { Position = subject.Position, Eta = 0 },
            };
            if (V2.Distance(subject.Position, goal) < .8f)
                return result;
            List<V2> path;
            int first;
            if (subject.PathCursor < subject.Path.Count && V2.Distance(subject.Destination, goal) < 1)
            {
                path = subject.Path;
                first = subject.PathCursor;
            }
            else
            {
                path = Pathfinder.Find(b.Map, subject.Position, goal);
                first = 0;
                b.Stats.Paths++;
            }

            V2 last = subject.Position;
            float length = 0,
                next = 2;
            float speed = Math.Max(.5f, CombatMovement.Speed(b, subject));
            for (int i = first; i < path.Count && result.Count < 7; i++)
            {
                V2 p = path[i],
                    d = p - last;
                float segment = d.Length;
                while (next <= length + segment && result.Count < 7)
                {
                    result.Add(new Sample { Position = last + d.Normalized * (next - length), Eta = next / speed });
                    next += 2;
                }

                length += segment;
                last = p;
            }

            if (result.Count == 1 && path.Count > first)
                result.Add(new Sample { Position = last, Eta = length / speed });
            return result;
        }

        static void Reject(BattleSimulation b, Squad q, Soldier s, string reason)
        {
            b.Diagnostics.SmokeRejected++;
            if (q.Smoke.Reason != reason)
            {
                q.Smoke.Reason = reason;
                b.Diagnostics.Add(b, s, "reject", "Smoke", reason);
            }
        }

        static bool Duplicate(BattleSimulation b, V2 point)
        {
            foreach (var p in b.Projectiles)
                if (p.Kind == 0 && V2.Distance(p.To, point) < 3.2f)
                    return true;
            foreach (var c in b.Smoke)
                if (c.Until > b.Time + 5 && V2.Distance(c.Position, point) < 2.8f)
                    return true;
            return false;
        }

        public static SmokeIntent Evaluate(
            BattleSimulation b,
            Squad q,
            Soldier subject,
            V2 goal,
            SmokePurpose purpose,
            Soldier preferred = null
        )
        {
            if (subject == null || !subject.Alive || !subject.Deployed)
                return null;
            bool moving = V2.Distance(subject.Position, goal) > .8f,
                urgent = Emergency(b, subject, purpose);
            var carriers = new List<Soldier>();
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && (preferred == null || preferred == s)
                    && CanThrow(b, s, urgent)
                    && (!ReserveForStage(b, q) || urgent || s.SmokeCharges > 1)
                )
                    carriers.Add(s);
            if (carriers.Count == 0)
            {
                Reject(b, q, subject, "Дым: нет свободного исполнителя");
                return null;
            }

            var threats = Threats(b, subject, goal, moving);
            if (threats.Count == 0)
            {
                Reject(b, q, subject, "Дым: нет обоснованной угрозы для задачи");
                return null;
            }

            var samples = Samples(b, subject, goal);
            if (moving && samples.Count == 1)
            {
                Reject(b, q, subject, "Дым: маршрут недоступен");
                return null;
            }

            // Candidate-independent geometry is calculated once, not once per grenade point.
            var riskLines = new List<RiskLine>(samples.Count * threats.Count);
            foreach (var sample in samples)
            foreach (var threat in threats)
                if (
                    V2.Distance(threat.Position, sample.Position) <= b.Config.WeaponRange + 7
                    && b.Map.LineOfSight(threat.Position, sample.Position)
                )
                    riskLines.Add(
                        new RiskLine
                        {
                            From = threat.Position,
                            To = sample.Position,
                            Eta = sample.Eta,
                            Weight = threat.Confidence * (1 - b.Map.Protection(sample.Position, threat.Position)),
                        }
                    );
            var friends = new List<Soldier>();
            var lanes = new List<FriendlyLane>();
            foreach (var ally in b.Soldiers)
                if (ally.Side == q.Side && ally.Deployed && ally.Available && ally.Ammo > 0)
                {
                    int index = friends.Count;
                    friends.Add(ally);
                    foreach (var c in Knowledge.Contacts(b, ally))
                        if (PositionLogic.CanEngage(b, ally, ally.Position, c) && !b.FriendlyInLane(ally, c.Position))
                            lanes.Add(
                                new FriendlyLane
                                {
                                    Ally = index,
                                    From = ally.Position,
                                    To = c.Position,
                                }
                            );
                }

            var losses = new float[friends.Count];
            var points = new List<V2>(48);
            var cells = new HashSet<int>();
            // Fixed budget: reports x a few samples, no map-wide search or enemy scans.
            for (int ti = 0; ti < Math.Min(4, threats.Count); ti++)
            for (int si = 0; si < samples.Count; si += Math.Max(1, samples.Count / 3))
            {
                var sample = samples[si].Position;
                V2 line = threats[ti].Position - sample,
                    lateral = new V2(-line.Normalized.Y, line.Normalized.X);
                for (int step = 1; step <= 3; step++)
                {
                    V2 p = sample + line * (step * .25f);
                    for (int side = -1; side <= 1; side++)
                    {
                        V2 candidate = p + lateral * (side * 1.8f);
                        int key = (int)(candidate.X * 2) + 10000 * (int)(candidate.Y * 2);
                        if (points.Count < 72 && b.Map.Walkable(candidate) && cells.Add(key))
                            points.Add(candidate);
                    }
                }
            }

            if (urgent && b.Map.Walkable(subject.Position))
                points.Add(subject.Position);
            SmokeIntent best = null;
            float bestScore = .28f;
            foreach (var point in points)
            {
                if (Duplicate(b, point))
                    continue;
                Soldier carrier = null;
                float travel = float.MaxValue;
                foreach (var c in carriers)
                {
                    float distance = V2.Distance(c.Position, point);
                    if (
                        MotionCombat.ForwardAction(b, c, point)
                        && distance <= MotionCombat.SmokeRange(b, c)
                        && distance < travel
                        && b.Map.LineOfSight(c.Position, point)
                    )
                    {
                        travel = distance;
                        carrier = c;
                    }
                }

                if (carrier == null)
                    continue;
                float born = b.Time + 1 + travel / 7,
                    ready = born + 1.8f,
                    gain = 0,
                    risk = 0;
                foreach (var line in riskLines)
                {
                    float at = ready + line.Eta,
                        before = Forecast(b, line.From, line.To, at);
                    risk += line.Weight * before;
                    gain += line.Weight * before * (1 - Transmission(line.From, line.To, point, born, at));
                }

                gain /= samples.Count;
                risk /= samples.Count;
                if (gain < .14f || gain < risk * .28f)
                    continue;
                float cost = 0;
                Array.Clear(losses, 0, losses.Length);
                foreach (var lane in lanes)
                {
                    float before = Forecast(b, lane.From, lane.To, ready);
                    losses[lane.Ally] = Math.Max(
                        losses[lane.Ally],
                        before * (1 - Transmission(lane.From, lane.To, point, born, ready))
                    );
                }

                for (int i = 0; i < friends.Count; i++)
                {
                    var ally = friends[i];
                    float factor =
                        ally == subject && moving ? .15f
                        : ally.SupportReady || b.Team(ally).SupportIds.Contains(ally.Id) ? 1.1f
                        : .45f;
                    cost += losses[i] * factor;
                    if (!urgent && V2.Distance(point, ally.Position) < Radius)
                        cost += .55f;
                }

                // Preserve the last resource under weak hypotheses. Emergency aid can use it.
                float resource = carrier.SmokeCharges == 1 && !urgent ? .42f : .12f;
                // Each lost friendly firing sector has a cost, including neighbouring groups.
                // Adding more allies must not dilute the cost of obscuring the same coverers.
                float score =
                    gain
                        * (
                            urgent ? 3.8f
                            : moving ? 3.1f
                            : 2.4f
                        )
                    - cost
                    - .03f * travel
                    - resource;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = new SmokeIntent
                {
                    Carrier = carrier.Id,
                    Subject = subject.Id,
                    Revision = subject.OrderRevision,
                    Origin = subject.Position,
                    Destination = goal,
                    Point = point,
                    Purpose = purpose,
                    Created = b.Time,
                    Score = score,
                    Gain = gain,
                };
            }

            if (best == null)
                Reject(b, q, subject, "Дым отклонён: малая польза, помеха своим или сектор уже закрыт");
            return best;
        }

        static bool Launch(BattleSimulation b, Squad q, SmokeIntent plan)
        {
            if (plan == null || plan.Carrier < 0 || plan.Carrier >= b.Soldiers.Count)
                return false;
            var s = b.Soldiers[plan.Carrier];
            var subject = b.Soldiers[plan.Subject];
            if (
                !MotionCombat.ForwardAction(b, s, plan.Point)
                || !CanThrow(b, s, Emergency(b, subject, plan.Purpose))
                || Duplicate(b, plan.Point)
                || V2.Distance(s.Position, plan.Point) > MotionCombat.SmokeRange(b, s)
                || !b.Map.LineOfSight(s.Position, plan.Point)
            )
                return false;
            float born = b.Time + ArcedOrdnance.HandAimSeconds + 1 + V2.Distance(s.Position, plan.Point) / 7;
            s.RocketAimTarget = s.OrdnanceAimTarget = -1;
            if (MotionCombat.InMotion(b, s))
            {
                s.Motion.MovingSmokes++;
                s.Motion.MobileSmokeUntil = born + 1.8f;
            }

            s.SmokeCharges--;
            s.SmokeReadyAt = b.Time + 12;
            s.HandsBusyUntil = b.Time + ArcedOrdnance.HandAimSeconds;
            s.PriorityMovement.NextSmoke = s.SniperNextSmoke = s.SmokeReadyAt;
            q.NextSmoke = b.Time + 3.5f;
            var threats = Threats(b, subject, plan.Destination, V2.Distance(subject.Position, plan.Destination) > .8f);
            V2 witness = subject.Position;
            V2 threat = threats.Count > 0 ? threats[0].Position : plan.Point;
            var samples = Samples(b, subject, plan.Destination);
            float best = 0;
            foreach (var sample in samples)
            foreach (var t in threats)
            {
                float effect =
                    Forecast(b, sample.Position, t.Position, born + 1.8f)
                    * (1 - Transmission(sample.Position, t.Position, plan.Point, born, born + 1.8f));
                if (effect > best && b.Map.LineOfSight(sample.Position, t.Position))
                {
                    best = effect;
                    witness = sample.Position;
                    threat = t.Position;
                }
            }

            b.SmokeEffects.Add(
                new SmokeEffect
                {
                    Subject = subject.Id,
                    Carrier = s.Id,
                    Revision = subject.OrderRevision,
                    Side = q.Side,
                    Point = plan.Point,
                    From = witness,
                    Threat = threat,
                    Born = born,
                    Ready = born + 1.8f,
                    Until = born + 20,
                    Before = Forecast(b, witness, threat, born + 1.8f),
                    ExpectedGain = plan.Gain,
                }
            );
            // Capture the forecast without this grenade, then launch. An old fading cloud
            // must not turn a successful replacement screen into a false failure.
            SmokeThrow.Launch(b, s, plan.Point);
            q.Smoke.Reason =
                plan.Purpose == SmokePurpose.Route ? "Завеса для маршрута; проверяю раскрытие"
                : plan.Purpose == SmokePurpose.ScreenThreat ? "Закрываю обзор известной угрозе"
                : "Завеса для помощи или отхода";
            b.Diagnostics.Add(
                b,
                s,
                "launch",
                "Smoke",
                q.Smoke.Reason
                    + "; benefit="
                    + plan.Gain.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            );
            b.Log(s.Side, s.Name + ": " + q.Smoke.Reason);
            b.Say(s, "Дымовая завеса!", true);
            return true;
        }

        public static bool Request(
            BattleSimulation b,
            Squad q,
            Soldier subject,
            V2 goal,
            SmokePurpose purpose,
            Soldier preferred = null
        )
        {
            b.Diagnostics.SmokeRequests++;
            if (q.Smoke.Pending != null || b.Time < q.NextSmoke || b.Time < q.Smoke.NextReview)
                return false;
            q.Smoke.NextReview = b.Time + .9f;
            return Launch(b, q, Evaluate(b, q, subject, goal, purpose, preferred));
        }

        public static void Plan(BattleSimulation b, Squad q, V2 destination, Soldier assigned = null)
        {
            if (q.Smoke.Pending != null || b.Time < q.NextSmoke || b.Time < q.Smoke.NextReview)
                return;
            Soldier subject = assigned;
            if (subject == null)
                foreach (int id in q.ManeuverIds)
                    if (id >= 0 && id < b.Soldiers.Count && b.Soldiers[id].Available)
                    {
                        subject = b.Soldiers[id];
                        break;
                    }

            if (subject == null)
                foreach (var s in b.Soldiers)
                    if (
                        q.Contains(s)
                        && s.Available
                        && !s.Busy
                        && !s.Broken
                        && (
                            subject == null
                            || V2.Distance(s.Position, destination) < V2.Distance(subject.Position, destination)
                        )
                    )
                        subject = s;
            if (subject == null)
                return;
            q.Smoke.NextReview = b.Time + 1.2f;
            b.Diagnostics.SmokeRequests++;
            var goal = V2.Distance(subject.Position, subject.OrderAnchor) > .8f ? subject.OrderAnchor : destination;
            var purpose =
                WithdrawalTactics.Active(q) || TacticalOrders.Emergency(q)
                    ? SmokePurpose.Withdrawal
                    : SmokePurpose.Route;
            var plan = Evaluate(b, q, subject, goal, purpose);
            if (plan == null)
                return;
            q.Smoke.Pending = plan;
            q.PlannedSmoke.Clear();
            q.PlannedSmoke.Add(plan.Point);
            q.NextPlannedSmoke = b.Time;
            q.SmokeWaitUntil = b.Time + 4;
            b.Stats.SmokePlans++;
            b.Diagnostics.Add(b, subject, "plan", "Smoke", purpose.ToString());
        }

        public static void Update(BattleSimulation b, Squad q)
        {
            var plan = q.Smoke.Pending;
            if (plan != null && b.Time >= q.NextPlannedSmoke)
            {
                q.Smoke.Pending = null;
                q.PlannedSmoke.Clear();
                q.SmokeWaitUntil = 0;
                if (plan.Subject < 0 || plan.Subject >= b.Soldiers.Count)
                    return;
                var subject = b.Soldiers[plan.Subject];
                if (
                    !subject.Available
                    || subject.OrderRevision != plan.Revision
                    || b.Time - plan.Created > 3
                    || V2.Distance(subject.OrderAnchor, plan.Destination) > 1.5f
                        && subject.PathCursor < subject.Path.Count
                )
                {
                    Reject(b, q, subject, "Дым отменён: задача изменилась");
                    return;
                }

                var updated = Evaluate(b, q, subject, plan.Destination, plan.Purpose);
                if (Launch(b, q, updated))
                    q.SmokeWaitUntil =
                        b.Time + 1 + V2.Distance(b.Soldiers[updated.Carrier].Position, updated.Point) / 7 + 1.8f;
            }
            else if (plan == null && q.PlannedSmoke.Count > 0)
            {
                // Old raw-point queue has no task owner or expected effect. Re-plan safely.
                q.PlannedSmoke.Clear();
                q.SmokeWaitUntil = 0;
            }
        }

        public static bool Waiting(BattleSimulation b, Soldier s)
        {
            if (TacticalOrders.Emergency(b.Team(s)))
                return false;
            var state = b.Team(s).Smoke;
            if (
                state.CurtainRevision == b.Team(s).Revision
                && state.CurtainReady > b.Time
                && state.CurtainMembers.Contains(s.Id)
                && s.Health >= 35
                && b.Time - s.LastIncoming >= 1.5f
                && !s.Broken
                && !s.Busy
                && !s.GrenadeEscape.Active
                && !WithdrawalTactics.Active(b.Team(s))
            )
                return true;
            foreach (var effect in b.SmokeEffects)
                if (
                    effect.Subject == s.Id
                    && effect.Revision == s.OrderRevision
                    && b.Time < effect.Ready
                    && b.Time < effect.Until
                )
                    return true;
            return false;
        }

        public static void Tick(BattleSimulation b)
        {
            foreach (var effect in b.SmokeEffects)
                if (!effect.Evaluated && b.Time >= effect.Ready)
                {
                    effect.Evaluated = true;
                    float actual = effect.Before - b.SmokeTransmission(effect.From, effect.Threat);
                    var s =
                        effect.Subject >= 0 && effect.Subject < b.Soldiers.Count ? b.Soldiers[effect.Subject] : null;
                    if (actual > .15f)
                    {
                        b.Diagnostics.SmokeEffective++;
                        b.Diagnostics.Add(
                            b,
                            s,
                            "effect",
                            "Smoke",
                            "Завеса раскрылась, видимость выбранного участка уменьшилась"
                        );
                    }
                    else
                    {
                        b.Diagnostics.SmokeIneffective++;
                        b.Diagnostics.Add(b, s, "failure", "Smoke", "Ожидаемая завеса не получена: пересмотр задачи");
                        if (s != null)
                        {
                            b.Team(s).NextPlan = 0;
                            s.NextDecision = 0;
                            s.SupportReady = false;
                        }
                    }
                }

            b.SmokeEffects.RemoveAll(e => b.Time > e.Until + 1);
        }

        // Two distinct carriers screen the approach together. Both landing points
        // are close to each carrier's normal maximum throw; no range bonus is used.
        public static bool WallThrowBusy(BattleSimulation b, Soldier s)
        {
            float until;
            return b.Team(s).Smoke.WallHands.TryGetValue(s.Id, out until)
                && b.Time < until
                && s.HandsBusyUntil > b.Time
                && s.HandsBusyUntil <= until + .001f;
        }

        public static bool WallStyle(Squad q) => q.Style == CombatStyle.Aggressive || q.Style == CombatStyle.Balanced;

        public static float WallChance(CombatStyle style) =>
            style == CombatStyle.Aggressive ? .8f
            : style == CombatStyle.Balanced ? .65f
            : 0;

        public static void CarryThroughVolley(Squad q, int previousRevision)
        {
            if (q.Smoke.SequenceRevision == previousRevision)
                q.Smoke.SequenceRevision = q.Revision;
            if (q.Smoke.CurtainRevision == previousRevision)
                q.Smoke.CurtainRevision = q.Revision;
        }

        public static string WallOrder(BattleSimulation b, List<int> carriers)
        {
            var names = new List<string>();
            foreach (int id in carriers)
                names.Add(b.Soldiers[id].Name);
            return "Код СТЕНА! " + string.Join(", ", names.ToArray()) + " — завеса! СБЛИЖЕНИЕ!";
        }

        static void AnnounceWall(BattleSimulation b, Squad q, List<int> carriers)
        {
            string message = WallOrder(b, carriers);
            q.CommanderOrder = q.PriorityStatus = message;
            b.Say(b.Soldiers[q.LeaderId], message, true);
            b.History.Event(
                b,
                "wallOrder",
                q.LeaderId,
                -1,
                b.Soldiers[q.LeaderId].Position,
                b.Map.Objective,
                carriers.Count,
                message
            );
        }

        public static bool ReserveForStage(BattleSimulation b, Squad q) =>
            q.Side == Side.Green
            && WallStyle(q)
            && q.Smoke.SequenceRevision == q.Revision
            && q.Smoke.SequenceStage == 1
            && b.Time - q.Smoke.SequenceAt < 22;

        public static bool StageProgress(BattleSimulation b, Squad q)
        {
            int moved = 0;
            foreach (var u in b.Soldiers)
                if (q.Contains(u) && u.Available && !u.Broken)
                {
                    V2 origin;
                    if (
                        q.Smoke.SequenceOrigins.TryGetValue(u.Id, out origin)
                        && V2.Dot(u.Position - origin, q.Smoke.SequenceAxis) >= 3.5f
                    )
                        moved++;
                }

            return moved >= 2;
        }

        static bool OpenApproach(BattleSimulation b, Soldier u, Contact threat)
        {
            var pair = CommandHierarchy.Pair(b.Team(u), u.Assignment.PairId);
            V2 goal = pair != null ? pair.Goal : b.Map.Objective;
            V2 direction = (goal - u.Position).Normalized;
            if (V2.Distance(goal, u.Position) < 3)
                return false;
            int exposed = 0;
            for (int i = 1; i <= 3; i++)
            {
                var point = u.Position + direction * (2 * i);
                if (
                    b.Map.Walkable(point)
                    && b.Map.LineOfSight(point, threat.Position)
                    && b.Map.Protection(point, threat.Position) < .4f
                )
                    exposed++;
            }

            return exposed >= 2;
        }

        static void RefillStage(BattleSimulation b, Squad q)
        {
            if (!ReserveForStage(b, q) || b.Time < q.Smoke.NextRefill)
                return;
            q.Smoke.NextRefill = b.Time + 1;
            foreach (var donor in b.Soldiers)
                if (q.Contains(donor) && donor.Available && donor.PackSmokes > 0 && !donor.Busy)
                {
                    foreach (var receiver in b.Soldiers)
                        if (
                            q.Contains(receiver)
                            && receiver.Available
                            && !receiver.Busy
                            && q.Smoke.CurtainMembers.Contains(receiver.Id)
                            && receiver.SmokeCharges == 0
                            && GrenadePockets.CanAdd(receiver, 0)
                        )
                        {
                            if (SupplyAI.Transfer(b, donor, receiver, 5))
                            {
                                b.History.Event(
                                    b,
                                    "smokeStageSupply",
                                    donor.Id,
                                    receiver.Id,
                                    donor.Position,
                                    receiver.Position,
                                    1,
                                    "Пополнение носителя для следующего заслона"
                                );
                                return;
                            }
                        }
                }
        }

        public static bool TryCurtain(BattleSimulation b, Squad q)
        {
            if (
                q.Side != Side.Green
                || q.Style != CombatStyle.Aggressive && q.Style != CombatStyle.Balanced
                || q.CommandTransition.Waiting
                || q.Smoke.Pending != null
                || b.Time < q.Smoke.CurtainNext
            )
                return false;
            if (
                q.LeaderId < 0
                || q.LeaderId >= b.Soldiers.Count
                || !b.Soldiers[q.LeaderId].Available
                || b.Soldiers[q.LeaderId].Broken
            )
                return false;
            if (
                q.Breakthrough.Phase == BreakthroughPhase.Volley
                && q.Breakthrough.Fired.Count < q.Breakthrough.Targets.Count
            )
                return false;
            var sequence = q.Smoke;
            if (sequence.SequenceRevision != q.Revision)
            {
                sequence.SequenceStage = 0;
                sequence.SequenceRevision = q.Revision;
                sequence.SequenceOrigins.Clear();
            }

            if (WallStyle(q) && sequence.SequenceStage >= 2)
                return false;
            bool continuing = WallStyle(q) && sequence.SequenceStage == 1;
            if (continuing && (b.Time - sequence.SequenceAt > 22 || !StageProgress(b, q)))
                return false;
            if (!continuing && b.Time < sequence.WallChoiceNext)
                return false;
            q.Smoke.CurtainNext = b.Time + 1;
            var subjects = new List<Soldier>();
            Contact threat = null;
            Soldier lead = null;
            float best = 0;
            foreach (var u in b.Soldiers)
                if (q.Contains(u) && u.Available && !u.Broken && !u.Busy && u.Role != Role.Medic)
                {
                    foreach (var contact in Knowledge.Contacts(b, u))
                    {
                        float distance = V2.Distance(u.Position, contact.Position);
                        if (
                            !contact.Visual
                            || contact.Inferred
                            || contact.Confidence < (continuing ? .35f : .6f)
                            || b.Time - contact.SeenAt > (continuing ? 20 : 8)
                            || distance < 8
                            || distance > 26
                            || !b.Map.LineOfSight(u.Position, contact.Position)
                        )
                            continue;
                        bool outranged =
                            distance > b.WeaponRange(u) + .5f && contact.ObservedWeaponRange > b.WeaponRange(u) + 1;
                        bool knownDanger =
                            SuppressionPriority.Rank(contact) >= 2
                            || contact.ObservedWeaponRange > b.WeaponRange(u) + 1
                            || b.Time - contact.FireSeenAt < 3;
                        bool proactive = WallStyle(q) && knownDanger && OpenApproach(b, u, contact);
                        if (continuing && contact.EnemyId != sequence.SequenceThreat)
                            continue;
                        if (
                            !proactive
                            && !outranged
                            && !(SuppressionPriority.Rank(contact) >= 2 && b.Time - u.LastIncoming < 4)
                        )
                            continue;
                        float score = (outranged ? 5 : 0) + SuppressionPriority.Rank(contact) + contact.Confidence;
                        if (score > best)
                        {
                            best = score;
                            threat = contact;
                            lead = u;
                        }
                    }
                }

            if (threat == null)
                return false;
            foreach (var u in b.Soldiers)
                if (
                    q.Contains(u)
                    && u.Available
                    && !u.Broken
                    && !u.Busy
                    && u.Role != Role.Medic
                    && V2.Distance(u.Position, lead.Position) < 12
                    && V2.Distance(u.Position, b.Map.Objective) > 8
                )
                    subjects.Add(u);
            if (subjects.Count == 0)
                return false;
            var carriers = new List<Soldier>();
            foreach (var u in b.Soldiers)
                if (q.Contains(u) && CanThrow(b, u, false) && V2.Distance(u.Position, lead.Position) < 12)
                    carriers.Add(u);
            if (carriers.Count < 2)
                return false;
            V2 goal = b.Map.Objective;
            var pair = CommandHierarchy.Pair(q, lead.Assignment.PairId);
            if (pair != null && (pair.Duty == PairDuty.Maneuver || pair.Duty == PairDuty.Advance))
                goal = pair.Goal;
            V2 axis = (goal - lead.Position).Normalized;
            if (axis.Length < .1f)
                return false;
            V2 lateral = new V2(-axis.Y, axis.X);
            var points = new List<SmokeIntent>();
            foreach (var carrier in carriers)
                for (int k = -2; k <= 2; k++)
                {
                    float angle = k * .16f;
                    V2 direction = axis * (float)Math.Cos(angle) + lateral * (float)Math.Sin(angle);
                    var point = carrier.Position + direction * (MotionCombat.SmokeRange(b, carrier) - .05f);
                    if (
                        !b.Map.Walkable(point)
                        || Duplicate(b, point)
                        || !MotionCombat.ForwardAction(b, carrier, point)
                        || !b.Map.LineOfSight(carrier.Position, point)
                    )
                        continue;
                    points.Add(
                        new SmokeIntent
                        {
                            Carrier = carrier.Id,
                            Subject = lead.Id,
                            Revision = lead.OrderRevision,
                            Origin = lead.Position,
                            Destination = goal,
                            Point = point,
                            Purpose = SmokePurpose.Route,
                            Created = b.Time,
                        }
                    );
                }

            SmokeIntent a = null,
                z = null;
            float bestGain = .65f;
            for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
            {
                var x = points[i];
                var y = points[j];
                if (x.Carrier == y.Carrier)
                    continue;
                float separation = V2.Distance(x.Point, y.Point),
                    across = Math.Abs(V2.Dot(x.Point - y.Point, lateral));
                if (
                    separation < 3.3f
                    || separation > 5.2f
                    || across < 3.2f
                    || Math.Abs(V2.Dot(x.Point - y.Point, axis)) > 2.5f
                )
                    continue;
                float bornX = b.Time + 1 + V2.Distance(b.Soldiers[x.Carrier].Position, x.Point) / 7;
                float bornY = b.Time + 1 + V2.Distance(b.Soldiers[y.Carrier].Position, y.Point) / 7;
                float at = Math.Max(bornX, bornY) + 1.8f;
                float gain = 0;
                int useful = 0;
                foreach (var u in subjects)
                {
                    float own = 0;
                    for (int step = 1; step <= 3; step++)
                    {
                        V2 sample = u.Position + axis * (step * 2);
                        if (!b.Map.Walkable(sample) || !b.Map.LineOfSight(sample, threat.Position))
                            continue;
                        float arrival = at + step * 2 / Math.Max(.5f, CombatMovement.Speed(b, u));
                        float before = Forecast(b, sample, threat.Position, continuing ? arrival : at);
                        own +=
                            before
                            * (
                                1
                                - Transmission(sample, threat.Position, x.Point, bornX, at)
                                    * Transmission(sample, threat.Position, y.Point, bornY, at)
                            );
                    }

                    own /= 3;
                    gain += own;
                    if (own > .35f)
                        useful++;
                }

                if (useful < 2)
                    continue;
                // The screen must close a threatening sector for movers, not only obscure
                // an already harmless target. Preserve other functioning support lanes.
                float cost = 0;
                foreach (var u in b.Soldiers)
                    if (q.Contains(u) && u.Available && u.GunMachineGunner && !u.Moving && b.Time - u.LastFire < 2)
                        foreach (var contact in u.Contacts.Values)
                            if (
                                u.Visible.Contains(contact.EnemyId)
                                && contact.EnemyId != threat.EnemyId
                                && PositionLogic.CanEngage(b, u, u.Position, contact)
                            )
                                cost +=
                                    .65f
                                    * (
                                        1
                                        - Transmission(u.Position, contact.Position, x.Point, bornX, at)
                                            * Transmission(u.Position, contact.Position, y.Point, bornY, at)
                                    );
                float value = gain - cost;
                if (value > bestGain)
                {
                    bestGain = value;
                    a = x;
                    z = y;
                    a.Gain = z.Gain = gain / subjects.Count;
                }
            }

            if (a == null)
                return false;
            if (!continuing)
            {
                sequence.WallChoiceNext = b.Time + 8;
                sequence.WallChoices++;
                var choice = new Rng(unchecked(b.CombatSeed ^ q.Revision * 397 ^ sequence.WallChoices * 7919 ^ 0x571A));
                if (choice.Range(0, 1) >= WallChance(q.Style))
                {
                    b.Diagnostics.Add(b, lead, "choice", "Wall", "СТЕНА: выбрано обычное продолжение задачи");
                    return false;
                }
            }

            var wall = new List<SmokeIntent> { a, z };
            // Extra carriers widen the screen only when their additional cloud has real marginal benefit.
            while (wall.Count < Math.Min(4, carriers.Count))
            {
                SmokeIntent extra = null;
                float bestExtra = .5f;
                foreach (var point in points)
                {
                    bool conflict = false,
                        joined = false;
                    foreach (var old in wall)
                    {
                        float distance = V2.Distance(old.Point, point.Point);
                        if (old.Carrier == point.Carrier || distance < 3.3f)
                            conflict = true;
                        if (distance <= 5.2f)
                            joined = true;
                    }

                    if (conflict || !joined || Math.Abs(V2.Dot(point.Point - a.Point, axis)) > 2.5f)
                        continue;
                    float born = b.Time + 1 + V2.Distance(b.Soldiers[point.Carrier].Position, point.Point) / 7,
                        at = born + 1.8f,
                        gain = 0;
                    foreach (var u in subjects)
                        for (int step = 1; step <= 3; step++)
                        {
                            var sample = u.Position + axis * (step * 2);
                            if (!b.Map.Walkable(sample) || !b.Map.LineOfSight(sample, threat.Position))
                                continue;
                            float before = Forecast(b, sample, threat.Position, at);
                            foreach (var old in wall)
                                before *= Transmission(
                                    sample,
                                    threat.Position,
                                    old.Point,
                                    b.Time + 1 + V2.Distance(b.Soldiers[old.Carrier].Position, old.Point) / 7,
                                    at
                                );
                            gain += before * (1 - Transmission(sample, threat.Position, point.Point, born, at)) / 3;
                        }

                    if (gain > bestExtra)
                    {
                        bestExtra = gain;
                        extra = point;
                        extra.Gain = gain / subjects.Count;
                    }
                }

                if (extra == null)
                    break;
                wall.Add(extra);
            }

            // All resource/geometry checks above are read-only; launch only a complete pair.
            if (!Launch(b, q, a))
                return false;
            bool second = Launch(b, q, z);
            if (second)
            {
                var used = new List<int> { a.Carrier, z.Carrier };
                for (int i = 2; i < wall.Count; i++)
                    if (Launch(b, q, wall[i]))
                        used.Add(wall[i].Carrier);
                q.Smoke.WallHands.Clear();
                foreach (int id in used)
                    q.Smoke.WallHands[id] = b.Soldiers[id].HandsBusyUntil;
                AnnounceWall(b, q, used);
                q.Smoke.AssaultRoutes.Clear();
                q.Smoke.AssaultReview.Clear();
                q.Smoke.CurtainMembers.Clear();
                foreach (var u in subjects)
                    q.Smoke.CurtainMembers.Add(u.Id);
                q.Smoke.CurtainRevision = q.Revision;
                q.Smoke.CurtainReady = b.Time + 1 + 10f / 7 + 1.8f;
                q.Smoke.CurtainNext = b.Time + (q.Style == CombatStyle.Aggressive ? 8 : 10);
                q.NextSmoke = Math.Max(q.NextSmoke, b.Time + 5);
                if (WallStyle(q))
                {
                    sequence.SequenceStage++;
                    sequence.SequenceAt = b.Time;
                    sequence.SequenceThreat = threat.EnemyId;
                    sequence.SequenceAxis = axis;
                    sequence.SequenceOrigins.Clear();
                    foreach (var u in subjects)
                        sequence.SequenceOrigins[u.Id] = u.Position;
                    b.History.Event(
                        b,
                        "smokeStage",
                        a.Carrier,
                        threat.EnemyId,
                        a.Point,
                        z.Point,
                        sequence.SequenceStage,
                        continuing
                            ? "Второй заслон после подтверждённого продвижения"
                            : "Первый заслон против известной угрозы"
                    );
                }

                b.History.Event(
                    b,
                    "smokeCurtain",
                    a.Carrier,
                    z.Carrier,
                    a.Point,
                    z.Point,
                    subjects.Count,
                    "Широкая завеса: жду раскрытия, затем сближение"
                );
            }

            return true;
        }

        // A short, interruptible bound uses actual smoke, never knowledge of hidden units.
        public static bool AssaultActive(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            var m = q.Smoke;
            return s.Side == Side.Green
                && WallStyle(q)
                && (
                    !q.Breakthrough.Active
                    || q.Breakthrough.Phase != BreakthroughPhase.Volley && q.Breakthrough.Movers.Contains(s.Id)
                )
                && m.CurtainRevision == q.Revision
                && b.Time >= m.CurtainReady
                && b.Time < m.CurtainReady + 12
                && m.CurtainMembers.Contains(s.Id)
                && PositionLogic.ShelterEligible(b, s)
                && s.Health >= 35
                && s.Suppression < .65f
                && !s.GrenadeEscape.Active;
        }

        static bool ScreenedRoute(BattleSimulation b, Soldier s, List<V2> path)
        {
            float length = 0;
            V2 previous = s.Position;
            bool screened = false;
            foreach (var end in path)
            {
                float d = V2.Distance(previous, end);
                int steps = Math.Max(1, (int)Math.Ceiling(d * 2));
                for (int k = 1; k <= steps; k++)
                {
                    var point = previous + (end - previous) * (k / (float)steps);
                    float at = b.Time + (length + d * k / steps) / Math.Max(.5f, CombatMovement.Speed(b, s));
                    foreach (var c in Knowledge.Contacts(b, s))
                        if (PositionLogic.Remembered(b, c) && b.Map.LineOfSight(point, c.Position))
                        {
                            float transmission = Forecast(b, point, c.Position, at);
                            if (transmission < .25f)
                                screened = true;
                            if (
                                V2.Distance(point, c.Position) < 4
                                || transmission > .35f && b.Map.Protection(point, c.Position) < .4f
                            )
                                return false;
                        }
                }

                length += d;
                previous = end;
            }

            return screened && length <= 7;
        }

        public static bool AssaultMove(BattleSimulation b, Soldier s, float dt)
        {
            var m = b.Team(s).Smoke;
            if (!AssaultActive(b, s) || s.ReloadUntil > b.Time || s.HandsBusyUntil > b.Time)
            {
                m.AssaultRoutes.Remove(s.Id);
                return false;
            }

            // Pause at grenade range: ordinary aiming, scatter and friendly safety remain authoritative.
            bool grenade =
                s.GrenadeCharges > 0 && s.GrenadeType != 0 || s.OtherGrenadeCharges > 0 || s.ThirdGrenadeCharges > 0;
            if (grenade)
                foreach (var c in s.Contacts.Values)
                    if (
                        c.Visual
                        && b.Time - c.SeenAt < 3
                        && c.Confidence >= .6f
                        && V2.Distance(s.Position, c.Position) >= 4
                        && V2.Distance(s.Position, c.Position) <= 8.5f
                    )
                    {
                        TacticalAI.Stop(s);
                        s.Motion.WasMoving = false;
                        s.Motion.Fast = false;
                        if (ArcedOrdnance.TryUse(b, s))
                        {
                            m.AssaultRoutes.Remove(s.Id);
                            return true;
                        }

                        break;
                    }

            List<V2> route;
            if (!m.AssaultRoutes.TryGetValue(s.Id, out route))
            {
                float review;
                if (m.AssaultReview.TryGetValue(s.Id, out review) && b.Time < review)
                    return false;
                m.AssaultReview[s.Id] = b.Time + 1;
                var pair = CommandHierarchy.Pair(b.Team(s), s.Assignment.PairId);
                var goal = pair != null ? pair.Goal : b.Map.Objective;
                V2 breakthroughGoal;
                if (
                    b.Team(s).Breakthrough.Active
                    && b.Team(s).Breakthrough.Goals.TryGetValue(s.Id, out breakthroughGoal)
                )
                    goal = breakthroughGoal;
                var full = Pathfinder.Find(b.Map, s.Position, goal);
                b.Stats.Paths++;
                route = new List<V2>();
                float length = 0;
                V2 prior = s.Position;
                foreach (var point in full)
                {
                    length += V2.Distance(prior, point);
                    if (length > 6)
                        break;
                    if (!ProgressRecovery.WithinAssignment(b, s, point))
                        break;
                    route.Add(point);
                    prior = point;
                }

                if (
                    route.Count == 0
                    || V2.Distance(s.Position, route[route.Count - 1]) < 1
                    || !ScreenedRoute(b, s, route)
                )
                    return false;
                m.AssaultRoutes[s.Id] = route;
                b.History.Event(
                    b,
                    "smokeAssault",
                    s.Id,
                    -1,
                    s.Position,
                    route[route.Count - 1],
                    route.Count,
                    "Короткий рывок по выбранному флангу под завесой"
                );
            }

            while (route.Count > 0 && V2.Distance(s.Position, route[0]) < .15f)
                route.RemoveAt(0);
            if (route.Count == 0 || !ScreenedRoute(b, s, route))
            {
                m.AssaultRoutes.Remove(s.Id);
                s.NextDecision = 0;
                return false;
            }

            s.Path = new List<V2>(route);
            s.PathCursor = 0;
            s.Destination = route[route.Count - 1];
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.SupportReady = false;
            var direction = route[0] - s.Position;
            V2 next;
            if (
                Locomotion.TryStep(
                    b,
                    s,
                    direction.Normalized * Math.Min(direction.Length, CombatMovement.Speed(b, s) * dt),
                    out next
                )
            )
            {
                s.DistanceTravelled += V2.Distance(s.Position, next);
                s.Position = next;
                s.Moving = true;
                s.LastMoved = b.Time;
                s.Facing = direction.Normalized;
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            s.Reason = "Рывок под дымовой стеной; затем гранаты";
            return true;
        }

        public static void ReviewGroup(BattleSimulation b, Squad q)
        {
            RefillStage(b, q);
            if (q.Smoke.Pending == null && TryCurtain(b, q))
                return;
            if (ReserveForStage(b, q))
                return;
            if (b.Time < q.NextSmoke || b.Time < q.Smoke.NextReview || q.Smoke.Pending != null)
                return;
            // Proactive review also covers flanks and authored defenders, independently of
            // their tactical planner. Evaluate at most one actual moving assignment.
            for (int i = 0; i < b.Soldiers.Count; i++)
            {
                var s = b.Soldiers[(i + q.Smoke.Cursor) % b.Soldiers.Count];
                if (
                    q.Contains(s)
                    && s.Available
                    && !s.Busy
                    && !s.Broken
                    && s.PathCursor < s.Path.Count
                    && V2.Distance(s.Position, s.Destination) > 2
                )
                {
                    q.Smoke.Cursor = (s.Id + 1) % b.Soldiers.Count;
                    Plan(b, q, s.Destination, s);
                    return;
                }
            }

            // Stationary units can ask to obscure a harmful known sector before taking damage.
            foreach (var s in b.Soldiers)
                if (
                    q.Contains(s)
                    && s.Available
                    && !s.Busy
                    && !s.Broken
                    && PositionLogic.Exposure(b, s, s.Position) > .45f
                )
                {
                    Request(b, q, s, s.Position, SmokePurpose.ScreenThreat);
                    return;
                }

            q.Smoke.NextReview = b.Time + 1;
        }
    }
}
