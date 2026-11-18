using System;

namespace SilentOrders.Core
{
    public sealed class GreenExperimentMemory
    {
        public int RelayTargets, OrphanReleases, SpacingReviews, ReadinessWaits, DetourSearches, ReservationsReviewed;
    }

    // AI-exp: optional policies belong to the green command hierarchy only.
    // No changes to weapon stats, visibility, ammunition, incoming damage or enemy policies.
    public static class GreenExperiments
    {
        public const int AcceptedMask = 5;
        public static bool Enabled(Squad q, int bit)
        {
            return q.Side == Side.Green && (AcceptedMask & bit) != 0;
        }

        static Soldier Unit(BattleSimulation b, int id)
        {
            return id >= 0 && id < b.Soldiers.Count ? b.Soldiers[id] : null;
        }

        static bool Live(Soldier s, Squad q)
        {
            return s != null && q.Contains(s) && s.Deployed && s.Available;
        }

        public static V2 Target(BattleSimulation b, Squad q, PairCommandPlan p, Soldier mover, V2 ordinary)
        {
            if (!Enabled(q, 1) || p.Duty != PairDuty.Support || q.Style == CombatStyle.Passive || q.Style == CombatStyle.Withdraw)
                return ordinary;
            Soldier beneficiary = null;
            float best = float.MaxValue;
            foreach (var other in q.CommandPlan.Pairs)
                if (other.SupportPair == p.Id)
                    foreach (int id in new[]
                    {
                        other.A,
                        other.B
                    }

                    )
                    {
                        var u = Unit(b, id);
                        if (!Live(u, q))
                            continue;
                        float d = V2.Distance(u.Position, mover.Position);
                        if (d < best)
                        {
                            best = d;
                            beneficiary = u;
                        }
                    }

            if (beneficiary == null)
                return ordinary;
            // Follow the actual surviving recipient, not the centroid of obsolete pair goals.
            if (best > 8 && !CommandHierarchy.AssignmentUseful(b, mover))
            {
                q.CommandPlan.Experiments.RelayTargets++;
                return TacticalOrders.Open(b, beneficiary.Position - TacticalOrders.Axis(b, q.Side) * 3);
            }

            return ordinary;
        }

        public static bool ReservedByOtherPair(BattleSimulation b, Squad q, Soldier mover, Soldier other)
        {
            return Enabled(q, 16) && other != mover && Live(other, q) && other.Assignment.PairId >= 0 && other.Assignment.PairId != mover.Assignment.PairId && other.Assignment.Active && other.Assignment.IsBound && other.Assignment.Deadline > b.Time && other.PathCursor < other.Path.Count;
        }

        public static float Spacing(BattleSimulation b, Squad q, Soldier mover, V2 point)
        {
            if (Enabled(q, 16))
            {
                float reservationCost = 0;
                foreach (var u in b.Soldiers)
                    if (ReservedByOtherPair(b, q, mover, u))
                    {
                        q.CommandPlan.Experiments.ReservationsReviewed++;
                        float distance = V2.Distance(point, u.Assignment.Goal);
                        if (distance < 1.3f)
                            reservationCost -= (1.3f - distance) * 3;
                    }

                return reservationCost;
            }

            if (!Enabled(q, 2))
                return 0;
            q.CommandPlan.Experiments.SpacingReviews++;
            float score = 0;
            foreach (var u in b.Soldiers)
                if (u != mover && Live(u, q))
                {
                    // A bound reserves its endpoint; a stationary soldier owns the occupied firing post.
                    V2 anchor = u.Assignment.Active && u.Assignment.IsBound ? u.Assignment.Goal : u.Position;
                    float d = V2.Distance(point, anchor);
                    if (d < 2.5f)
                        score -= (2.5f - d) * 2;
                }

            return score;
        }

        public static bool WaitForPartner(BattleSimulation b, Squad q, PairCommandPlan p, Soldier mover)
        {
            if (!Enabled(q, 4) || q.Style == CombatStyle.Passive || q.Style == CombatStyle.Withdraw || mover.Suppression > .4f || b.Time - mover.LastIncoming < 3)
                return false;
            var partner = Unit(b, p.A == mover.Id ? p.B : p.A);
            if (!Live(partner, q) || partner.Broken || partner.Busy || V2.Distance(partner.Position, mover.Position) > 9)
                return false;
            // Finite rendezvous at an existing protected post, only for a reload already underway.
            float left = partner.ReloadUntil - b.Time;
            if (left <= 0 || left > 3 || !PositionLogic.Protected(b, mover, mover.Position))
                return false;
            if (TacticalOrders.ClosestKnown(b, mover) == null)
                return false;
            q.CommandPlan.Experiments.ReadinessWaits++;
            mover.Reason = "Жду завершения перезарядки напарника перед следующей перебежкой";
            return true;
        }

        public static int SearchRadius(Squad q, PairCommandPlan p)
        {
            if (!Enabled(q, 8) || p.Failures < 3 || q.Style == CombatStyle.Passive || q.Style == CombatStyle.Withdraw)
                return 3;
            q.CommandPlan.Experiments.DetourSearches++;
            return 5;
        }
    }
}
