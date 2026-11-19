using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class SquadCoordination
    {
        public static bool SupportPosition(BattleSimulation b, Soldier s)
        {
            if (PositionLogic.Protected(b, s, s.Position))
                return true;
            if (!s.Sniper)
                return false;
            var known = SniperTactics.Known(b, s);
            return known.Count > 0
                && SniperTactics.OpenSafe(b, s.Position, known, 2)
                && PositionLogic.HasSector(b, s, s.Position);
        }

        // Rebuild reciprocal pairs from actual equipment; no assumption about roster slots.
        public static void Pair(BattleSimulation b, Squad q, List<Soldier> units)
        {
            if (
                q.Side == Side.Green && q.CommandPlan.Pairs.Count > 0
                || CommandHierarchy.Owns(q)
                || WithdrawalTactics.Active(q) && q.CommandPlan.Pairs.Count > 0
                || TacticalOrders.Flanks(q)
                || TacticalOrders.Emergency(q)
            )
                return;
            if (b.Time < q.NextPairReview)
                return;
            q.NextPairReview = b.Time + 3;
            var left = new List<Soldier>();
            foreach (var s in units)
                if (s.Available && !s.Busy)
                    left.Add(s);
            // Treat the selected pairs as preferences, not as an all-or-nothing 5-man
            // formation. Preserve each surviving pair if the other one lost somebody.
            foreach (var s in units)
                s.BuddyId = s.Id;
            if (q.Formation != null && q.Formation.Valid() && q.Formation.pairing != 0)
            {
                for (int pair = 0; pair < 2; pair++)
                {
                    int a = q.Formation.DisplaySlot(pair * 2),
                        z = q.Formation.DisplaySlot(pair * 2 + 1);
                    if (a < 0 || z < 0)
                        continue;
                    Soldier x = null,
                        y = null;
                    foreach (var soldier in left)
                    {
                        if (soldier.Id == a)
                            x = soldier;
                        if (soldier.Id == z)
                            y = soldier;
                    }

                    if (x != null && y != null)
                    {
                        x.BuddyId = y.Id;
                        y.BuddyId = x.Id;
                        left.Remove(x);
                        left.Remove(y);
                    }
                }
                // If the specified partner is missing, fall back to a new matching pair
                // using whoever is actually available, rather than freezing in place.
            }

            while (left.Count > 1)
            {
                Soldier a = null,
                    z = null;
                float best = float.MinValue;
                for (int i = 0; i < left.Count; i++)
                for (int j = i + 1; j < left.Count; j++)
                {
                    var x = left[i];
                    var y = left[j];
                    float score = -V2.Distance(x.Position, y.Position);
                    if (x.SupplyPack && y.Role == Role.MachineGunner || y.SupplyPack && x.Role == Role.MachineGunner)
                        score += 30;
                    if (
                        x.Role == Role.Grenadier && y.Role == Role.Rifleman
                        || y.Role == Role.Grenadier && x.Role == Role.Rifleman
                    )
                        score += 15;
                    if (x.Role == Role.Medic && y.Id == q.LeaderId || y.Role == Role.Medic && x.Id == q.LeaderId)
                        score += 12;
                    if (x.Sniper || y.Sniper)
                        score -= 8;
                    if (score > best)
                    {
                        best = score;
                        a = x;
                        z = y;
                    }
                }

                a.BuddyId = z.Id;
                z.BuddyId = a.Id;
                left.Remove(a);
                left.Remove(z);
            }
        }

        public static bool AnyRouteCovered(BattleSimulation b, Squad q)
        {
            foreach (int id in q.ManeuverIds)
            {
                var s = b.Soldiers[id];
                if (!s.Available)
                    continue;
                if (CommanderPriorities.SmokeSafe(b, s) || TacticalAI.SafeTravel(b, s) || SquadBrain.HasCover(b, s))
                    return true;
            }

            return false;
        }

        public static int MovePriority(BattleSimulation b, Squad q, Soldier s)
        {
            if (q.Style == CombatStyle.Withdraw || q.EmergencyRetreat)
                return (
                    s.Health < 50 ? 0
                    : s.Role == Role.Medic ? 1
                    : s.Sniper ? 3
                    : s.Role == Role.MachineGunner ? 20
                    : 5
                );
            return (
                    s.Sniper ? 25
                    : s.Role == Role.Medic ? 30
                    : s.Role == Role.MachineGunner ? 20
                    : s.Role == Role.Rifleman ? 0
                    : 1
                ) + FormationAI.MovePriority(q, s);
        }

        public static bool CanSplit(BattleSimulation b, List<Soldier> units)
        {
            int fit = 0;
            foreach (var s in units)
                if (s.Available && !s.Busy && !s.Broken && s.Health >= 45 && s.Suppression < .5f && s.Ammo >= 3)
                    fit++;
            return fit >= 5;
        }

        public static void ReleaseBounds(BattleSimulation b, Squad q)
        {
            if (q.Phase != ManeuverPhase.Move || q.Breakthrough.Active)
                return;
            int index = 0;
            foreach (int id in q.ManeuverIds)
            {
                var s = b.Soldiers[id];
                if (!s.Available)
                    continue;
                if (!s.MoveAuthorized && b.Time - q.PhaseSince >= index * 1.2f && SquadBrain.MovementCovered(b, s))
                {
                    s.MoveAuthorized = true;
                    s.NextDecision = b.Time;
                }

                index++;
            }
        }
    }
}
