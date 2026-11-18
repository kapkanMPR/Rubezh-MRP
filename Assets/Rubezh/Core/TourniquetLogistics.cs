using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Physical tourniquet, shared by both factions and both renderers.
    public sealed class GroundTourniquet
    {
        public int Id, ThrownBy, IntendedFor;
        public Side Side;
        public V2 From, To;
        public float Born, Duration;
        public bool Collected;
        public bool Flying(float time) => !Collected && time < Born + Duration;
        public V2 Position(float time)
        {
            if (!Flying(time))
                return To;
            float t = Util.Clamp((time - Born) / Duration, 0f, 1f);
            return From + (To - From) * t;
        }

        public float Arc(float time)
        {
            if (!Flying(time))
                return 0f;
            float t = Util.Clamp((time - Born) / Duration, 0f, 1f);
            return (float)Math.Sin(Math.PI * t) * 1.7f;
        }
    }

    public static class TourniquetLogistics
    {
        public const float ThrowRange = 10f;
        public const float PickupRange = 1.1f;
        static bool Reachable(BattleSimulation b, V2 from, V2 to) => b.Map.Walkable(from) && Locomotion.WalkableSegment(b, from, to);
        public static bool ValidLanding(BattleMap map, V2 pos) => CorpseEquipment.ValidDroppedWeaponSite(map, pos);
        public static bool PendingFor(BattleSimulation b, Soldier patient)
        {
            foreach (var item in b.GroundTourniquets)
                if (!item.Collected && item.Side == patient.Side && item.IntendedFor == patient.Id && (item.Flying(b.Time) || (V2.Distance(item.To, patient.Position) <= 2.6f && Reachable(b, patient.Position, item.To))))
                    return true;
            return false;
        }

        static bool CanReachSafely(BattleSimulation b, Soldier helper, Soldier patient)
        {
            float distance = V2.Distance(helper.Position, patient.Position);
            if (distance <= 2.5f && b.Map.LineOfSight(helper.Position, patient.Position))
                return true;
            if (distance > 8f)
                return false;
            var path = Pathfinder.Find(b.Map, helper.Position, patient.Position, p => TacticalAI.Risk(b, helper, p) * 3f);
            b.Stats.Paths++;
            if (path.Count == 0 || SupportAI.PathLength(helper.Position, path) > 12f)
                return false;
            foreach (var p in path)
                if (TacticalAI.Risk(b, helper, p) > .65f)
                    return false;
            return true;
        }

        static bool Landing(BattleSimulation b, Soldier helper, Soldier patient, out V2 landing)
        {
            landing = patient.Position;
            // Aim on the casualty's side of cover, inside the actual pickup radius.
            // Start toward the helper; progressively smaller rings handle tight corners.
            float start = (float)Math.Atan2(helper.Position.Y - patient.Position.Y, helper.Position.X - patient.Position.X);
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < 12; i++)
                {
                    float angle = start + (float)(i * Math.PI / 6);
                    float radius = ring == 0 ? .65f : ring == 1 ? .35f : .95f;
                    V2 pos = patient.Position + new V2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                    if (V2.Distance(helper.Position, pos) > ThrowRange || !ValidLanding(b.Map, pos) || !b.Map.LineOfSight(helper.Position, pos) || !Reachable(b, patient.Position, pos))
                        continue;
                    landing = pos;
                    return true;
                }

            return ValidLanding(b.Map, patient.Position) && V2.Distance(helper.Position, patient.Position) <= ThrowRange && b.Map.LineOfSight(helper.Position, patient.Position);
        }

        public static bool Throw(BattleSimulation b, Soldier helper, Soldier patient)
        {
            if (!CombatTraining.Skill(helper, 1, "toss"))
                return false;
            if (helper == null || patient == null || helper.Side != patient.Side || !helper.Alive || !patient.Alive || !helper.Deployed || !patient.Deployed || !helper.Available || helper.Incapacitated || helper.Broken || helper.HandsBusyUntil > b.Time || helper.ReloadUntil > b.Time || helper.Suppression > .7f || helper.Tourniquets <= 0 || patient.Tourniquets > 0 || !patient.Bleeding || patient.TreatmentUntil > 0 || patient.RescuerId >= 0 || (TacticalOrders.Emergency(b.Team(helper)) || RedCounterattack.Retreating(b, helper) || RedCounterattack.Retreating(b, patient)) || PendingFor(b, patient) || b.Time < helper.NextTourniquetThrow)
                return false;
            float distance = V2.Distance(helper.Position, patient.Position);
            if (distance < 2.5f || distance > ThrowRange || CanReachSafely(b, helper, patient))
                return false;
            V2 landing;
            if (!Landing(b, helper, patient, out landing))
                return false;
            // Consume at throw time: no infinite duplicates and no mid-air pickup.
            helper.HandsBusyUntil = b.Time + .4f;
            helper.Tourniquets--;
            helper.NextTourniquetThrow = b.Time + 9f;
            var item = new GroundTourniquet
            {
                Id = b.GroundTourniquets.Count,
                ThrownBy = helper.Id,
                IntendedFor = patient.Id,
                Side = helper.Side,
                From = helper.Position,
                To = landing,
                Born = b.Time,
                Duration = .35f + distance * .075f
            };
            b.GroundTourniquets.Add(item);
            patient.NextCrawlPlan = b.Time;
            b.Log(helper.Side, helper.Name + ": бросил жгут " + patient.Name);
            b.Say(helper, "Лови жгут!", true);
            return true;
        }

        public static void Plan(BattleSimulation b)
        {
            if (b.Time < b.NextTourniquetPlan)
                return;
            b.NextTourniquetPlan = b.Time + .75f;
            foreach (var patient in b.Soldiers)
            {
                if (!patient.Alive || !patient.Deployed || !patient.Bleeding || patient.Tourniquets > 0 || patient.TreatmentUntil > 0 || patient.RescuerId >= 0 || PendingFor(b, patient) || RedCounterattack.Retreating(b, patient))
                    continue;
                var helpers = new System.Collections.Generic.List<Soldier>();
                foreach (var soldier in b.Soldiers)
                {
                    if (soldier.Id == patient.Id || soldier.Side != patient.Side || RedCounterattack.Retreating(b, soldier) || !soldier.Available || soldier.Broken || soldier.Tourniquets <= 0 || soldier.Suppression > .7f || b.Time < soldier.NextTourniquetThrow)
                        continue;
                    float d = V2.Distance(soldier.Position, patient.Position);
                    if (d >= 2.5f && d <= ThrowRange)
                        helpers.Add(soldier);
                }

                helpers.Sort((a, c) => V2.Distance(a.Position, patient.Position).CompareTo(V2.Distance(c.Position, patient.Position)));
                foreach (var helper in helpers)
                    if (Throw(b, helper, patient))
                        break;
            }
        }

        // Called before regular rescue/crawling. A casualty can crawl only a short,
        // valid route, respecting incoming fire and injuries. No teleport or free healing.
        public static bool UpdatePatient(BattleSimulation b, Soldier patient, float dt)
        {
            if (!patient.Alive || !patient.Deployed || !patient.Bleeding || patient.Tourniquets > 0 || patient.RescuerId >= 0 || patient.TreatmentUntil > 0)
                return false;
            GroundTourniquet chosen = null;
            float best = 3f;
            foreach (var item in b.GroundTourniquets)
            {
                if (item.Collected || item.Side != patient.Side || item.Flying(b.Time) || !Reachable(b, patient.Position, item.To))
                    continue;
                // Do not steal a delivery from a living recipient who can still reach it.
                if (item.IntendedFor != patient.Id && item.IntendedFor >= 0 && item.IntendedFor < b.Soldiers.Count)
                {
                    var intended = b.Soldiers[item.IntendedFor];
                    if (intended.Alive && intended.Deployed && intended.Bleeding && intended.Tourniquets == 0 && V2.Distance(intended.Position, item.To) <= 2.6f && Reachable(b, intended.Position, item.To))
                        continue;
                }

                float d = V2.Distance(item.To, patient.Position);
                if (d < best)
                {
                    chosen = item;
                    best = d;
                }
            }

            if (chosen == null)
                return false;
            if (best <= PickupRange)
            {
                chosen.Collected = true;
                patient.Tourniquets++;
                patient.Path.Clear();
                patient.PathCursor = 0;
                patient.Order = "Подобрал жгут, перевязываюсь";
                b.Log(patient.Side, patient.Name + ": подобрал брошенный жгут");
                MedicalInventory.Start(b, patient, patient);
                return true;
            }

            // Do not drag a badly wounded casualty through a lethal field for distant loot.
            if (patient.Suppression > .85f || best > 2.6f || TacticalAI.Risk(b, patient, chosen.To) > .7f)
                return false;
            // Verify the whole short crawl, not just its endpoint (no corner cutting).
            int samples = Math.Max(1, (int)Math.Ceiling(best * 4f));
            for (int i = 1; i <= samples; i++)
                if (TacticalAI.Risk(b, patient, patient.Position + (chosen.To - patient.Position) * (i / (float)samples)) > .7f)
                    return false;
            V2 displacement = (chosen.To - patient.Position).Normalized * Math.Min(best, b.Config.MoveSpeed * ConcussionEffects.MovementMultiplier(ConcussionEffects.Current(b, patient)) * (patient.Incapacitated ? .25f : .5f) * dt);
            V2 next = patient.Position + displacement;
            if (!Reachable(b, patient.Position, next))
                return false;
            foreach (var ally in b.Soldiers)
                if (ally.Id != patient.Id && ally.Alive && ally.Deployed && V2.Distance(ally.Position, next) < .4f)
                    return false;
            patient.Position = next;
            patient.Moving = true;
            patient.LastMoved = b.Time;
            patient.State = patient.Incapacitated ? ActionState.Incapacitated : ActionState.Move;
            patient.Reason = "Ползу к брошенному жгуту";
            return true;
        }
    }
}
