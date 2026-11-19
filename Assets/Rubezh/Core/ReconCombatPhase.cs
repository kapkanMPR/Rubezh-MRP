using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    // One-way phase transition for this experimental battle. Never reads hidden enemies.
    public static class ReconCombatPhase
    {
        sealed class State
        {
            public bool Entered;
            public float Next;
            public string Signature = "";
            public int Reviews;
        }

        static readonly ConditionalWeakTable<BattleSimulation, State> Data =
            new ConditionalWeakTable<BattleSimulation, State>();

        public static bool Active(BattleSimulation b) =>
            b.ReconCombatTransitionEnabled && Data.GetOrCreateValue(b).Entered;

        public static int Reviews(BattleSimulation b) => Data.GetOrCreateValue(b).Reviews;

        public static void Enter(BattleSimulation b, string reason = "opening_release")
        {
            if (!b.ReconCombatTransitionEnabled || !b.PairExperimentMarch || b.Historical01225)
                return;
            var st = Data.GetOrCreateValue(b);
            if (st.Entered)
                return;
            PairTacticalPlan.ReleaseForCombat(b);
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Deployed)
                {
                    bool scoutRoute = s.Awareness.MarchRouteUntil > b.Time || ContactMovement.Active(b, s);
                    if (scoutRoute && TacticalMarch.Free(b, s))
                        TacticalAI.Stop(s);
                    s.Awareness.MarchRouteUntil = 0;
                    s.Awareness.ContactMove.Until = 0;
                    s.Awareness.ContactMove.Supporter = -1;
                    if (EngagementPolicy.Active(b, s))
                        EngagementPolicy.End(b, s, false, "Разведка завершена: боевой режим");
                    s.NextDecision = b.Time;
                }

            st.Entered = true;
            var march = b.Green.CommandPlan.March;
            march.Quiet = false;
            march.Coverer = -1;
            march.CoverAssigned = false;
            march.Sectors.Clear();
            march.Dispersed.Clear();
            Request(b, reason);
            b.History.Event(
                b,
                "combatPhase",
                b.Green.LeaderId,
                -1,
                new V2(),
                new V2(),
                0,
                "Разведка завершена; штатный боевой планировщик"
            );
        }

        static void Request(BattleSimulation b, string reason)
        {
            var q = b.Green;
            var st = Data.GetOrCreateValue(b);
            st.Reviews++;
            q.NextPlan = b.Time;
            q.NextPriorityPlan = b.Time;
            q.CommandPlan.NextReview = b.Time;
            foreach (var pair in q.CommandPlan.Pairs)
            {
                pair.NeedsReview = true;
                pair.NextReview = b.Time;
                pair.NextBound = b.Time;
                pair.WaitingSince = -1;
            }

            b.History.Event(b, "combatPlanReview", q.LeaderId, -1, new V2(), b.Map.Objective, st.Reviews, reason);
        }

        public static void Update(BattleSimulation b)
        {
            if (!Active(b))
                return;
            var st = Data.GetOrCreateValue(b);
            if (b.Time < st.Next)
                return;
            st.Next = b.Time + 1;
            var parts = new List<string>();
            parts.Add(b.Green.Revision + ":" + b.Green.LeaderId);
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Deployed)
                {
                    parts.Add(
                        s.Id
                            + ":"
                            + s.Available
                            + ":"
                            + (s.Health < 45)
                            + ":"
                            + (s.Suppression > .65f)
                            + ":"
                            + (s.Ammo == 0)
                            + ":"
                            + (s.ReloadUntil > b.Time)
                    );
                    foreach (var contact in Knowledge.Contacts(b, s))
                        if (
                            contact.Visual
                            && !contact.Inferred
                            && s.Visible.Contains(contact.EnemyId)
                            && b.Time - contact.SeenAt <= b.Config.PerceptionPeriod * 2
                        )
                            parts.Add(s.Id + ">" + contact.EnemyId);
                }

            parts.Sort(StringComparer.Ordinal);
            string current = string.Join("|", parts);
            if (current != st.Signature)
            {
                st.Signature = current;
                Request(b, "observed_state_changed");
            }
        }
    }
}
