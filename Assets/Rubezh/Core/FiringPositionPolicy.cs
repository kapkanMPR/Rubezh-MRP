using System;

namespace SilentOrders.Core
{
    // Shared by order assignment, decision and movement: a later planner must not
    // turn a useful protected firing position into an unnecessary close approach.
    public static class FiringPositionPolicy
    {
        public static bool ShouldHold(BattleSimulation b, Soldier s, Job job, V2 destination)
        {
            if (s != null && (ReconOpening.Owns(b, s) || QuietRecon.Owns(b, s) || PairTacticalPlan.Owns(b, s)))
                return false;
            if (s == null || LastOpponentHunt.Owns(b, s) || !s.Deployed || !s.Available || s.Busy || s.Broken || s.Sniper || s.Bleeding || s.Ammo + s.Reserve <= 0 || s.RescueTargetId >= 0 || s.RescuerId >= 0 || s.SupplyDonorId >= 0 || s.SupplyReceiverId >= 0 || s.HelpTargetId >= 0 || s.FlagResponseUntil > b.Time || s.HelpRetreatUntil > b.Time || s.PriorityMovement.Transfer || s.PairRegroupUntil > b.Time || s.LootTargetId >= 0 || s.LootGroundId >= 0 || s.WeaponAidTargetId >= 0 || s.LauncherPickupId >= 0)
                return false;
            if (job != Job.Advance && job != Job.Flank && job != Job.Reposition && job != Job.Cover && job != Job.Hold && job != Job.Command)
                return false;
            var q = b.Team(s);
            var pair = CommandHierarchy.Pair(q, s.Assignment.PairId);
            if ((q.Intent == ManeuverIntent.Straight || q.Intent == ManeuverIntent.Front) && q.Style != CombatStyle.Passive && q.Style != CombatStyle.Withdraw && CommandHierarchy.Owns(q) && pair != null && pair.Mover == s.Id && !StraightAdvance.Waiting(b, s) && StraightAdvance.Inside(b, s, destination) && V2.Dot(destination - s.Position, q.Straight.Axis) > .5f)
                return false;
            if (CommandPosture.ForwardNeeded(b, s, destination, true) || CommandHierarchy.ManeuverNeeded(b, s, destination, true))
                return false;
            // A selected short flank bound serves the commander's direction. Merely
            // having a shot must not veto it before the flank planner can assess cover.
            // Passive posture still prefers an existing useful sector.
            if (TacticalOrders.Flanks(q) && q.Style != CombatStyle.Passive && q.FlankPlan.Active && !q.FlankPlan.FixedSupport.Contains(s.Id) && !q.CommandTransition.Waiting)
            {
                int side = FlankTactics.SideOf(q, s.Id), slot = side < 0 ? 0 : 1;
                if (side != 0 && FlankTactics.MoverFor(q, s) == s.Id && V2.Dot(s.Position - b.Map.Entry, TacticalOrders.Lateral(b)) * side < 4f)
                    return false;
            }

            if (TacticalOrders.Emergency(q) || q.Style == CombatStyle.Withdraw || q.EmergencyRetreat || WithdrawalTactics.Active(q) || RedCounterattack.Retreating(b, s) || ObjectiveOpportunity.IsCapturer(b, s) || (q.Breakthrough.Active && q.Breakthrough.Movers.Contains(s.Id)))
                return false;
            // A concrete capture assignment may leave cover; mere offensive posture may not.
            if (s.Side == Side.Green && (job == Job.Advance || job == Job.Reposition) && V2.Distance(destination, b.Map.Objective) <= BattleMap.CaptureRadius && V2.Distance(s.Position, b.Map.Objective) > BattleMap.CaptureRadius)
                return false;
            if (!PositionLogic.Protected(b, s, s.Position))
                return false;
            foreach (var c in s.Contacts.Values)
            {
                if (!s.Visible.Contains(c.EnemyId) || c.SeenAt > b.Time || !PositionLogic.CanEngage(b, s, s.Position, c) || b.FriendlyInLane(s, c.Position))
                    continue;
                V2 muzzle;
                if (!Ballistics.TryMuzzle(b, s, c.Position, out muzzle))
                    continue;
                // A move away from this threat is allowed (e.g. restore spacing).
                if (V2.Distance(destination, c.Position) > V2.Distance(s.Position, c.Position) + .75f)
                    continue;
                return true;
            }

            return false;
        }

        public static bool Hold(BattleSimulation b, Soldier s)
        {
            if (!ShouldHold(b, s, s.Job, s.OrderAnchor))
                return false;
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Position;
            s.Job = s.Id == b.Team(s).LeaderId ? Job.Command : Job.Cover;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.CoverWaitStartedAt = -1f;
            s.Reason = "Укрытие и сектор огня есть — сохраняю позицию и дистанцию";
            return true;
        }
    }
}
