using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class FormationEnvelopeState
    {
        public float At = -1;
        public V2 Center, Axis, Lateral;
        public int Count;
        public readonly Dictionary<int, float> Lanes = new Dictionary<int, float>();
        public readonly List<float> Forward = new List<float>(8);
        public readonly List<float> Sideways = new List<float>(8);
    }

    public sealed class FormationReturnState
    {
        public float ReviewAt, Until, DetourLimit;
        public List<V2> Route;
        public V2 Goal;
    }

    public static class FormationEnvelope
    {
        // Preferred spans: 8 / 12 / 18 cells. These are scoring preferences, never movement limits.
        public static float HalfDepth(Squad q) => q.Formation == null || q.Formation.depth == 1 ? 6f : q.Formation.depth == 0 ? 4f : 9f;
        public static float HalfWidth(Squad q) => q.Formation == null || q.Formation.width == 1 ? 6f : q.Formation.width == 0 ? 4f : 9f;

        public static bool AuthorizedManeuver(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            if (s.PriorityMovement.Transfer || PairTacticalPlan.FlankRoute(b, s)) return true;
            if (q.EnvelopmentActive && q.ManeuverIds.Contains(s.Id)) return true;
            var p = CommandHierarchy.Pair(q, s.Assignment.PairId);
            // The pair's maneuver survives pauses between its individual bounds.
            if (CommandHierarchy.Owns(q) && p != null && p.Duty == PairDuty.Maneuver
                && (p.A == s.Id || p.B == s.Id) && p.IntentKey == (int)q.Intent
                && q.CommandPlan.Intent == q.Intent)
                return true;
            return TacticalOrders.Flanks(q) && q.FlankPlan.Active && q.FlankPlan.Mode == q.Intent
                && !q.FlankPlan.FixedSupport.Contains(s.Id)
                && (q.FlankPlan.Wing.ContainsKey(s.Id) || s.Id == q.LeaderId && q.FlankPlan.CommanderWing != 0);
        }

        static bool Member(BattleSimulation b, Squad q, Soldier s) => q.Contains(s) && s.Deployed && s.Available && !s.Broken
            && !AuthorizedManeuver(b, s) && !s.VehicleRally && !s.VehicleDisperse && !TacticalOrders.ServiceActive(s) && !s.GrenadeEscape.Active;

        public static bool Eligible(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            return q.Side == Side.Green && Member(b, q, s) && !TacticalOrders.Emergency(q)
                && q.Style != CombatStyle.Withdraw && !WithdrawalTactics.Active(q)
                && s.HelpUntil <= b.Time && s.HelpRetreatUntil <= b.Time
                && s.CounterSniper.Phase == CounterSniperPhase.None && !ObjectiveOpportunity.IsCapturer(b, s)
                && s.FlagResponseUntil <= b.Time && s.HandsBusyUntil <= b.Time;
        }

        public static FormationEnvelopeState Frame(BattleSimulation b, Squad q)
        {
            var f = q.FormationEnvelope;
            if (f.At == b.Time) return f;
            f.At = b.Time;
            f.Axis = PositionLogic.Axis(b);
            f.Lateral = new V2(-f.Axis.Y, f.Axis.X);
            f.Forward.Clear(); f.Sideways.Clear();
            foreach (var s in b.Soldiers)
                if (Member(b, q, s))
                {
                    f.Forward.Add(V2.Dot(s.Position, f.Axis));
                    f.Sideways.Add(V2.Dot(s.Position, f.Lateral));
                }
            f.Count = f.Forward.Count;
            if (f.Count > 0)
            {
                f.Forward.Sort(); f.Sideways.Sort();
                int mid = f.Count / 2;
                float a = f.Count % 2 == 0 ? (f.Forward[mid - 1] + f.Forward[mid]) * .5f : f.Forward[mid];
                float c = f.Count % 2 == 0 ? (f.Sideways[mid - 1] + f.Sideways[mid]) * .5f : f.Sideways[mid];
                f.Center = f.Axis * a + f.Lateral * c;
            }
            return f;
        }

        // A preference, not a wall: commanded flanks and emergency work retain priority.
        public static float LaneCost(BattleSimulation b, Soldier s, V2 point)
        {
            if (!Eligible(b, s)) return 0;
            var q = b.Team(s); var f = Frame(b, q);
            if (f.Count < 2) return 0;
            if (f.Lanes.Count == 0)
                foreach (var mate in b.Soldiers)
                    if (Member(b, q, mate))
                        f.Lanes[mate.Id] = Util.Clamp(V2.Dot(mate.Position - f.Center, f.Lateral), -HalfWidth(q) + 1, HalfWidth(q) - 1);
            float lane;
            if (!f.Lanes.TryGetValue(s.Id, out lane))
                f.Lanes[s.Id] = lane = Util.Clamp(V2.Dot(s.Position - f.Center, f.Lateral), -HalfWidth(q) + 1, HalfWidth(q) - 1);
            float side = V2.Dot(point - f.Center, f.Lateral);
            float spacing = q.Formation == null || q.Formation.width == 1 ? 2.5f : q.Formation.width == 0 ? 1.8f : 3.2f;
            float crowd = 0;
            foreach (var mate in b.Soldiers)
                if (mate != s && q.Contains(mate) && mate.Available && mate.Deployed)
                {
                    V2 occupied = ExecutionReview.ReservesGoal(b, mate) && mate.PathCursor < mate.Path.Count ? mate.OrderAnchor : mate.Position;
                    crowd += Math.Max(0, spacing - V2.Distance(point, occupied)) * 2;
                }
            return Math.Min(4, Math.Max(0, Math.Abs(side - lane) - 1.5f) * 1.5f)
                + Math.Min(4, Excess(b, s, point) * .8f) + Math.Min(5, crowd);
        }

        public static float Excess(BattleSimulation b, Soldier s, V2 p, float inset = 0)
        {
            var q = b.Team(s); var f = Frame(b, q);
            if (f.Count < 2) return 0;
            V2 d = p - f.Center;
            return Math.Max(0, Math.Abs(V2.Dot(d, f.Axis)) - (HalfDepth(q) - inset))
                + Math.Max(0, Math.Abs(V2.Dot(d, f.Lateral)) - (HalfWidth(q) - inset));
        }

        // Compatibility entry points: no clipping, stopping or forced return at the envelope.
        public static V2 ClampGoal(BattleSimulation b, Soldier s, V2 p) => p;
        public static bool Owns(Soldier s) => false;
        public static bool Active(BattleSimulation b, Soldier s) => false;
        public static bool AllowStep(BattleSimulation b, Soldier s, V2 p) => true;
        public static bool Decide(BattleSimulation b, Soldier s) => false;

        // Penalize cosmetic shuffles, but still allow a short move into useful cover/fire.
        public static float ShuffleCost(BattleSimulation b, Soldier s, V2 p)
        {
            if (!Eligible(b, s)) return 0;
            float distance = V2.Distance(s.Position, p);
            if (distance < .6f || distance >= 3.5f) return 0;
            bool gainsCover = !PositionLogic.Protected(b,s,s.Position) && PositionLogic.Protected(b,s,p);
            bool gainsFire = !PositionLogic.HasSector(b,s,s.Position) && PositionLogic.HasSector(b,s,p);
            return gainsCover || gainsFire ? 0 : (3.5f-distance)*3;
        }
    }
}
