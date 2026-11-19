using System;

namespace SilentOrders.Core
{
    // Priority flank probe when the squad is fighting, but this soldier has spent
    // too long without personally contributing. It uses reports/bearings, never
    // the actual locations of unseen enemies. No morale or movement overrides.
    public sealed class OutmaneuverMemory
    {
        public float NoContactSince = -1f,
            NextReview,
            CommittedUntil;
        public int ReportsUsed,
            FailedRoutes;
        public V2 LastGoal;

        public void Clear()
        {
            NoContactSince = -1f;
            NextReview = CommittedUntil = 0;
            ReportsUsed = FailedRoutes = 0;
            LastGoal = new V2();
        }
    }

    public static class FlankOutmaneuver
    {
        public const float OutOfContactSeconds = 7f;
        public const float SideEdgeMargin = 4f;

        static bool Eligible(BattleSimulation b, Soldier s)
        {
            var q = b.Team(s);
            // Deep, unsupervised pursuit belongs to Aggressive only. Balanced
            // movement is controlled by the local two-person flank planner.
            return q.Style == CombatStyle.Aggressive
                && TacticalOrders.Flanks(q)
                && q.FlankPlan.Active
                && !q.FlankPlan.FixedSupport.Contains(s.Id)
                && s.Id != q.LeaderId
                && s.Alive
                && s.Deployed
                && !s.Incapacitated
                && !s.Broken
                && !s.Busy
                && s.Health > 28f
                && s.Morale >= .32f
                && s.Suppression < .65f
                && s.Role != Role.Medic
                && !s.Sniper
                && s.Ammo + s.Reserve > 0
                && s.ReloadUntil <= b.Time
                && !s.PriorityMovement.Transfer
                && s.HelpTargetId < 0
                && s.HelpRetreatUntil <= b.Time;
        }

        public static bool AtSideEdge(BattleSimulation b, V2 position, int side)
        {
            if (side == 0)
                return false;
            V2 outward = TacticalOrders.Lateral(b) * (side > 0 ? 1f : -1f);
            // Detect the boundary *in the outward flank direction*, not the rear
            // deployment border. Geometry works for flipped/rotated maps too.
            V2 probe = position + outward * SideEdgeMargin;
            return probe.X <= 1.4f
                || probe.X >= b.Map.Columns - 1.4f
                || probe.Y <= 1.4f
                || probe.Y >= b.Map.Rows - 1.4f;
        }

        public static V2 TowardFlag(BattleSimulation b, V2 from)
        {
            var forward = (b.Map.Objective - from).Normalized;
            if (forward.Length < .001f)
                return b.Map.Objective;
            // Small, bounded step so we cannot skip across a blocked part of the map.
            return TacticalOrders.Open(b, from + forward * 4.5f);
        }

        static bool FreshPersonal(BattleSimulation b, Soldier s)
        {
            if (s.Visible.Count > 0 || b.Time - s.LastFire < 4f)
                return true;
            foreach (var c in s.Contacts.Values)
                if (!c.Reported && c.Visual && c.Confidence > .4f && c.SeenAt <= b.Time && b.Time - c.SeenAt < 2.5f)
                    return true;
            return false;
        }

        public static bool ComradesFighting(BattleSimulation b, Soldier s)
        {
            foreach (var ally in b.Soldiers)
                if (
                    ally.Id != s.Id
                    && ally.Side == s.Side
                    && ally.Alive
                    && ally.Deployed
                    && (b.Time - ally.LastFire < 4f || b.Time - ally.LastIncoming < 3f)
                )
                    return true;
            return false;
        }

        static bool GetBattleSector(BattleSimulation b, Soldier s, out V2 sector, out bool confirmed)
        {
            sector = new V2();
            confirmed = false;
            float newest = -999f,
                confidence = 0f;
            bool available = false;
            foreach (var contact in b.Team(s).Contacts.Values)
            {
                if (contact.SeenAt > b.Time || b.Time - contact.SeenAt > 12f || contact.Confidence < .35f)
                    continue;
                if (contact.SeenAt > newest || contact.SeenAt == newest && contact.Confidence > confidence)
                {
                    sector = contact.Position;
                    newest = contact.SeenAt;
                    confidence = contact.Confidence;
                    confirmed = contact.Visual && contact.Confidence >= .6f;
                    available = true;
                }
            }

            if (available)
                return true;
            // Fallback: use a recent FRIENDLY shooter's bearing only. It is a coarse
            // direction, not a precise, magically acquired enemy contact.
            float best = -999f;
            foreach (var ally in b.Soldiers)
                if (
                    ally.Side == s.Side
                    && ally.Id != s.Id
                    && ally.Alive
                    && ally.Deployed
                    && ally.LastFire >= 0
                    && b.Time - ally.LastFire < 4f
                    && ally.Facing.Length > .1f
                )
                {
                    if (ally.LastFire > best)
                    {
                        best = ally.LastFire;
                        sector = ally.Position + ally.Facing.Normalized * Math.Min(10f, b.WeaponRange(ally) * .7f);
                    }
                }

            return best > -999f;
        }

        public static V2 DesiredBehind(BattleSimulation b, Soldier s, V2 sector, int wing, bool confirmed)
        {
            var axis = TacticalOrders.Axis(b, s.Side);
            var lateral = TacticalOrders.Lateral(b) * (wing < 0 ? -1f : 1f);
            float depth = confirmed ? 3.5f : 1.5f; // uncertainty limits a speculative deep approach
            V2 goal = sector + axis * depth + lateral * (confirmed ? 5.2f : 4f);
            if (AtSideEdge(b, s.Position, wing))
                return TowardFlag(b, s.Position);
            var delta = goal - s.Position;
            if (delta.Length > 4.5f)
                goal = s.Position + delta.Normalized * 4.5f;
            return TacticalOrders.Open(b, goal);
        }

        public static bool Decide(BattleSimulation b, Soldier s)
        {
            var memory = s.Outmaneuver;
            if (!Eligible(b, s))
            {
                memory.NoContactSince = -1f;
                if (memory.CommittedUntil > b.Time)
                    memory.CommittedUntil = 0;
                return false;
            }

            if (FreshPersonal(b, s))
            {
                // If we had a speculative pursuit route, release it for the observed
                // enemy rather than continuing a stale rearward move.
                if (memory.CommittedUntil > b.Time && s.PathCursor < s.Path.Count)
                {
                    s.Path.Clear();
                    s.PathCursor = 0;
                    s.PriorityMovement.NextRoute = 0;
                }

                memory.NoContactSince = -1f;
                memory.CommittedUntil = 0;
                return false; // Direct engagement beats a speculative deep flank.
            }

            // Reaching the side of the map is not an objective. At the outer boundary,
            // redirect along the interior toward the operation flag even without a
            // fresh enemy report. A useful covering/firing sector is never abandoned.
            int boundaryWing = FlankTactics.SideOf(b.Team(s), s.Id);
            if (
                boundaryWing != 0
                && AtSideEdge(b, s.Position, boundaryWing)
                && !PositionLogic.HasSector(b, s, s.Position)
            )
            {
                V2 outward = TacticalOrders.Lateral(b) * (boundaryWing < 0 ? -1f : 1f);
                if (s.PathCursor < s.Path.Count)
                {
                    if (V2.Dot(s.Destination - s.Position, outward) <= .25f)
                    {
                        s.MoveAuthorized = true;
                        s.ManeuverPaused = false;
                        return true;
                    }

                    s.Path.Clear();
                    s.PathCursor = 0;
                    memory.CommittedUntil = 0;
                    memory.NextReview = b.Time;
                }

                if (b.Time >= memory.NextReview)
                {
                    memory.NextReview = b.Time + 2.5f;
                    V2 toward = TowardFlag(b, s.Position);
                    if (V2.Distance(toward, s.Position) > 1.3f && TacticalOrders.Route(b, s, toward, false, false))
                    {
                        memory.LastGoal = s.Destination;
                        memory.CommittedUntil = b.Time + 6f;
                        s.Job = Job.Flank;
                        s.MoveAuthorized = true;
                        s.ManeuverPaused = false;
                        s.Order = "Достиг края фланга — продолжаю к флагу";
                        s.Reason = "Край карты: прекращаю боковое движение, смещаюсь внутрь";
                        return true;
                    }
                }
            }

            if (!ComradesFighting(b, s))
            {
                memory.NoContactSince = -1f;
                memory.CommittedUntil = 0;
                return false;
            }

            if (memory.NoContactSince < 0)
                memory.NoContactSince = b.Time;
            if (b.Time - memory.NoContactSince < OutOfContactSeconds)
                return false;
            if (memory.CommittedUntil > b.Time && s.PathCursor < s.Path.Count)
            {
                // An old outward path must not trap the fighter on the map boundary.
                int priorWing = FlankTactics.SideOf(b.Team(s), s.Id);
                if (priorWing == 0)
                    priorWing = s.Id % 2 == 0 ? 1 : -1;
                V2 outward = TacticalOrders.Lateral(b) * (priorWing < 0 ? -1f : 1f);
                if (AtSideEdge(b, s.Position, priorWing) && V2.Dot(s.Destination - s.Position, outward) > .25f)
                {
                    s.Path.Clear();
                    s.PathCursor = 0;
                    memory.CommittedUntil = 0;
                    memory.NextReview = b.Time;
                }
                else
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    s.Order = "Обхожу к флангу или тылу по докладу своих";
                    return true;
                }
            }

            if (b.Time < memory.NextReview)
                return false;
            memory.NextReview = b.Time + 2.5f + (s.Id % 3) * .25f;
            V2 sector;
            bool confirmed;
            if (!GetBattleSector(b, s, out sector, out confirmed))
                return false;
            int wing = FlankTactics.SideOf(b.Team(s), s.Id);
            if (wing == 0)
                wing = s.Id % 2 == 0 ? 1 : -1;
            var desired = DesiredBehind(b, s, sector, wing, confirmed);
            if (V2.Distance(desired, s.Position) < 1.8f)
            {
                memory.NextReview = b.Time + 4f;
                return false;
            }

            // Keep the latest heading unless the current path has actually finished.
            // Same normal A*: no teleport, movement-speed bonus, or Broken/Pinned bypass.
            bool cover = b.Team(s).Style != CombatStyle.Aggressive && PositionLogic.Exposure(b, s, desired) > .55f;
            bool routed = TacticalOrders.Route(b, s, desired, cover, false);
            if (!routed && cover)
                routed = TacticalOrders.Route(b, s, desired, false, false);
            if (!routed)
            {
                memory.FailedRoutes++;
                memory.NextReview = b.Time + Math.Min(8f, 3f + memory.FailedRoutes);
                return false;
            }

            memory.LastGoal = s.Destination;
            memory.FailedRoutes = 0;
            memory.CommittedUntil = b.Time + 7f;
            memory.ReportsUsed++;
            s.Job = Job.Flank;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Удар во фланг / тыл";
            s.Reason = confirmed
                ? "Товарищи ведут бой; обхожу последнюю известную позицию врага"
                : "Ориентируюсь по направлению огня товарищей; ищу сектор";
            return true;
        }
    }
}
