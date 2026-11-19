using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class CasualtyRecoveryState
    {
        public bool Arrived,
            FarewellSpoken;
        public float NextPlan,
            QuoteUntil;
        public int TargetId = -1,
            Cursor,
            SearchOffset;
        public readonly List<V2> Path = new List<V2>();

        public void Reset()
        {
            Arrived = FarewellSpoken = false;
            NextPlan = QuoteUntil = 0;
            TargetId = -1;
            Cursor = SearchOffset = 0;
            Path.Clear();
        }
    }

    public static class CasualtyRecovery
    {
        static readonly string[] Farewells =
        {
            "Все, мужики... Я всё...",
            "...Сейчас... Только отдохну чуть...",
            "...Работайте... Братья...",
            "...Выпейте... И за меня...",
        };

        // Fear slows crawling, but must not make an incapacitated survivor immobile.
        public static float CrawlSpeed(BattleSimulation b, Soldier s)
        {
            return CasualtyAI.Speed(b)
                * BodyArmor.Multiplier(s.ArmorLevel)
                * (.65f + .35f * Util.Clamp(s.Morale / .45f, 0, 1))
                * (1 - s.Suppression * .42f)
                * ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(b, s));
        }

        static bool Comrade(Soldier s, Soldier other)
        {
            return other.Id != s.Id && other.Side == s.Side && other.Alive && other.Deployed && !other.Incapacitated;
        }

        static bool Nearby(BattleSimulation b, Soldier s, Soldier other)
        {
            return Comrade(s, other)
                && V2.Distance(s.Position, other.Position) <= 2f
                && Locomotion.WalkableSegment(b, s.Position, other.Position);
        }

        public static void Maintain(BattleSimulation b)
        {
            foreach (var s in b.Soldiers)
            {
                if (!s.Bleeding)
                    s.CasualtyRecovery.Reset();
                if (!s.Alive || s.RescuerId < 0)
                    continue;
                var h = s.RescuerId < b.Soldiers.Count ? b.Soldiers[s.RescuerId] : null;
                if (h == null || h.RescueTargetId != s.Id)
                {
                    s.RescuerId = -1;
                    continue;
                }

                bool stalled =
                    (h.RescuePhase == RescuePhase.Approach || h.RescuePhase == RescuePhase.Drag)
                    && b.Time - Math.Max(h.LastMoved, h.RescueSince) > 2.5f;
                if (
                    !h.Alive
                    || !h.Deployed
                    || h.Incapacitated
                    || h.Broken
                    || h.Health < 28
                    || h.Suppression > .93f
                    || stalled
                )
                {
                    SupportAI.Release(b, h, false);
                    s.NextCrawlPlan = b.Time;
                }
            }
        }

        public static bool CrawlToComrades(BattleSimulation b, Soldier s, float dt)
        {
            if (!s.Bleeding || MedicalInventory.HasAid(s) || s.TreatmentUntil > 0 || s.RescuerId >= 0)
                return false;
            var state = s.CasualtyRecovery;
            TacticalAI.Stop(s);
            if (state.Arrived)
            {
                s.Reason = "Добрался к своим: остаюсь здесь, жду помощь";
                return true;
            }

            foreach (var item in b.GroundTourniquets)
                if (!item.Collected && item.Side == s.Side && item.IntendedFor == s.Id && item.Flying(b.Time))
                {
                    s.Reason = "Жду приземления брошенного мне жгута";
                    return true;
                }

            foreach (var ally in b.Soldiers)
                if (Nearby(b, s, ally))
                {
                    state.Arrived = true;
                    state.Path.Clear();
                    s.Reason = "Свои рядом: остаюсь здесь, жду помощь";
                    return true;
                }

            bool valid =
                state.TargetId >= 0 && state.TargetId < b.Soldiers.Count && Comrade(s, b.Soldiers[state.TargetId]);
            if (!valid)
            {
                state.Path.Clear();
                state.Cursor = 0;
            }

            if (b.Time >= state.NextPlan)
            {
                state.NextPlan = b.Time + 2;
                state.Path.Clear();
                state.Cursor = 0;
                state.TargetId = -1;
                var friends = new List<Soldier>();
                foreach (var ally in b.Soldiers)
                    if (Comrade(s, ally))
                        friends.Add(ally);
                friends.Sort(
                    (a, c) => V2.Distance(s.Position, a.Position).CompareTo(V2.Distance(s.Position, c.Position))
                );
                float best = float.MaxValue;
                // Bounded batches rotate if blocked, so unreachable nearest allies cannot mask the rest.
                for (int n = 0; n < Math.Min(3, friends.Count); n++)
                {
                    var ally = friends[(state.SearchOffset + n) % friends.Count];
                    V2 toward = (s.Position - ally.Position).Normalized;
                    double angle = Math.Atan2(toward.Y, toward.X);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = angle + k * Math.PI / 4;
                        V2 goal = ally.Position + new V2((float)Math.Cos(a), (float)Math.Sin(a)) * 1.5f;
                        if (!b.Map.Walkable(goal) || !Locomotion.WalkableSegment(b, goal, ally.Position))
                            continue;
                        bool occupied = false;
                        foreach (var other in b.Soldiers)
                            if (other != s && other.Alive && other.Deployed && V2.Distance(other.Position, goal) < .65f)
                            {
                                occupied = true;
                                break;
                            }

                        if (occupied)
                            continue;
                        List<V2> path;
                        if (Locomotion.WalkableSegment(b, s.Position, goal))
                            path = new List<V2> { goal };
                        else
                        {
                            path = LossMemory.Find(b, s, goal, v => TacticalAI.Risk(b, s, v));
                            b.Stats.Paths++;
                        }

                        if (path.Count == 0)
                            continue;
                        // Pathfinder can snap endpoints: only accept genuine arrival near the ally.
                        V2 end = path[path.Count - 1];
                        float gap = V2.Distance(end, ally.Position);
                        if (gap < 1f || gap > 2f || !Locomotion.WalkableSegment(b, end, ally.Position))
                            continue;
                        float score = SupportAI.PathLength(s.Position, path);
                        if (score >= best)
                            continue;
                        best = score;
                        state.TargetId = ally.Id;
                        state.Path.Clear();
                        state.Path.AddRange(path);
                    }
                }

                state.SearchOffset =
                    state.TargetId >= 0 ? 0 : (friends.Count > 0 ? (state.SearchOffset + 3) % friends.Count : 0);
            }

            if (state.Cursor >= state.Path.Count)
            {
                s.Reason = "Жгута и доступной помощи нет: зову своих, ищу проход";
                return true;
            }

            V2 delta = state.Path[state.Cursor] - s.Position;
            if (
                Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, CrawlSpeed(b, s) * dt), out var next)
            )
            {
                float moved = V2.Distance(s.Position, next);
                s.Position = next;
                s.DistanceTravelled += moved;
                s.Moving = moved > .00001f;
                if (s.Moving)
                    s.LastMoved = b.Time;
                if (V2.Distance(next, state.Path[state.Cursor]) < .04f)
                    state.Cursor++;
            }

            s.Reason = "Помощь недоступна: ползу к своим, остановлюсь рядом";
            foreach (var ally in b.Soldiers)
                if (Nearby(b, s, ally))
                {
                    state.Arrived = true;
                    state.Path.Clear();
                    s.Moving = false;
                    break;
                }

            return true;
        }

        static bool TimelyTreatment(BattleSimulation b, Soldier h, Soldier s)
        {
            if (
                !h.Alive
                || h.TreatmentUntil <= 0
                || h.TreatmentKind == 0
                || h.TreatmentUntil - b.Time >= s.Health
                || !InteractionReach.Can(b, h.Position, s.Position, 1.1f)
            )
                return false;
            int id = h.TreatmentDonorId;
            if (id < 0 || id >= b.Soldiers.Count)
                return false;
            var donor = b.Soldiers[id];
            return h.TreatmentKind == 1 ? donor.Tourniquets > 0 : donor.Medkits > 0;
        }

        public static void UpdateVoice(BattleSimulation b, Soldier s)
        {
            var state = s.CasualtyRecovery;
            if (!s.Alive || !s.Deployed || !s.Bleeding || s.Health > 3.5f || state.FarewellSpoken)
                return;
            if (TimelyTreatment(b, s, s))
                return;
            if (s.RescuerId >= 0 && s.RescuerId < b.Soldiers.Count && TimelyTreatment(b, b.Soldiers[s.RescuerId], s))
                return;
            string line = Farewells[b.VoiceRandom.Int(Farewells.Length)];
            // A single important line per bleeding episode; reserve its short display time.
            s.RetreatQuoteUntil = 0;
            s.SpeechAt = b.Time - 2;
            s.SpeechCooldown = 0;
            b.Say(s, line, true);
            state.FarewellSpoken = true;
            state.QuoteUntil = b.Time + 4.2f;
        }
    }
}
