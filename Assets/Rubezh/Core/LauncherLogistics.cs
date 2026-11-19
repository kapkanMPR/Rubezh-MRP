using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Launchers are single-use physical items. A fired tube remains as inert scenery;
    // a fresh supply tube has exactly one shot and can only be collected after landing.
    public sealed class GroundLauncher
    {
        public int Id,
            FromSoldierId,
            Kind; // 0: RPG-26, 1: RShG
        public bool Loaded,
            Collected;
        public V2 From,
            To;
        public float Born,
            Duration,
            Angle;
        public StudioPlaced Scene;

        public bool Flying(float now) => Duration > 0f && now < Born + Duration;

        public float Height(float now)
        {
            if (!Flying(now))
                return 0f;
            float t = Util.Clamp((now - Born) / Duration, 0f, 1f);
            return (float)Math.Sin(t * Math.PI) * 1.4f;
        }

        public V2 Position(float now)
        {
            if (!Flying(now))
                return To;
            float t = Util.Clamp((now - Born) / Duration, 0f, 1f);
            return From + (To - From) * t;
        }
    }

    public static class LauncherLogistics
    {
        public static void ReadyReserve(Soldier s)
        {
            if (s.Rockets != 0 || s.ReserveLauncherKind < 0)
                return;
            s.LauncherKind = s.ReserveLauncherKind;
            s.ReserveLauncherKind = -1;
            s.Rockets = 1;
        }

        public const float MaxSpentDistance = 2f;
        public const float MaxSupplyThrowDistance = 4f;
        const float CollectionRadius = 1.4f;

        static bool Ready(BattleSimulation b, Soldier s) =>
            s != null
            && s.Alive
            && s.Deployed
            && s.Available
            && !s.Incapacitated
            && !s.Broken
            && s.Suppression < .70f
            && s.Morale >= .25f
            && !TacticalOrders.Emergency(b.Team(s));

        static bool Suitable(BattleMap map, V2 pos) => CorpseEquipment.ValidDroppedWeaponSite(map, pos);

        static V2 Landing(BattleSimulation b, V2 from, V2 near, float maxOffset, float phase)
        {
            if (Suitable(b.Map, near) && b.Map.LineOfSight(from, near))
                return near;
            for (int ring = 0; ring < 4; ring++)
            {
                float dist = .4f + .35f * ring;
                for (int step = 0; step < 12; step++)
                {
                    float a = phase + step * (float)(Math.PI / 6f);
                    V2 p = near + new V2((float)Math.Cos(a) * dist, (float)Math.Sin(a) * dist);
                    if (V2.Distance(from, p) <= maxOffset && Suitable(b.Map, p) && b.Map.LineOfSight(from, p))
                        return p;
                }
            }

            return new V2(float.NaN, float.NaN); // Do not spawn in an obstacle or teleport across walls.
        }

        public static GroundLauncher EjectSpent(BattleSimulation b, Soldier shooter)
        {
            if (b == null || shooter == null)
                return null;
            float angle = ((shooter.Id * 41 + b.TickNumber * 19) % 628) * .01f;
            for (int n = 0; n < 12; n++)
            {
                float a = angle + n * (float)(Math.PI / 6f);
                float radius = 1.15f + (n % 3) * .36f;
                V2 proposed = shooter.Position + new V2((float)Math.Cos(a) * radius, (float)Math.Sin(a) * radius);
                if (!Suitable(b.Map, proposed) || !b.Map.LineOfSight(shooter.Position, proposed))
                    continue;
                return Spawn(b, shooter, shooter.LauncherKind, false, proposed, .35f + radius * .12f);
            }

            // Extremely cramped maps: tube falls at the shooter's walkable feet.
            if (Suitable(b.Map, shooter.Position))
                return Spawn(b, shooter, shooter.LauncherKind, false, shooter.Position, .22f);
            return null;
        }

        public static GroundLauncher DropLoaded(BattleSimulation b, Soldier s) =>
            Spawn(b, s, s.LauncherKind, true, s.Position, 0);

        static GroundLauncher Spawn(
            BattleSimulation b,
            Soldier from,
            int kind,
            bool loaded,
            V2 destination,
            float duration
        )
        {
            var item = new GroundLauncher
            {
                Id = b.GroundLaunchers.Count,
                FromSoldierId = from.Id,
                Kind = kind,
                Loaded = loaded,
                From = from.Position,
                To = destination,
                Born = b.Time,
                Duration = duration,
                Angle = ((from.Id * 73 + b.TickNumber * 11 + b.GroundLaunchers.Count * 59) % 628) * .01f,
            };
            b.GroundLaunchers.Add(item);
            return item;
        }

        static bool ThreatSafe(BattleSimulation b, Soldier s, V2 pos)
        {
            if (!Suitable(b.Map, pos))
                return false;
            float risk = PositionLogic.Exposure(b, s, pos);
            if (risk > 1.4f)
                return false;
            foreach (var c in b.Team(s).Contacts.Values)
            {
                if (c.Confidence < .25f || c.SeenAt > b.Time || b.Time - c.SeenAt > 8f)
                    continue;
                if (V2.Distance(pos, c.Position) < 4f)
                    return false;
            }

            return true;
        }

        public static bool ReachableOnGround(BattleSimulation b, Soldier grenadier, float maxRange)
        {
            var candidate = AvailableNear(b, grenadier, maxRange);
            if (candidate == null)
                return false;
            if (candidate.Flying(b.Time))
                return true;
            if (InteractionReach.Can(b, grenadier.Position, candidate.To, CollectionRadius))
                return true;
            var route = Pathfinder.Find(
                b.Map,
                grenadier.Position,
                candidate.To,
                p => PositionLogic.Exposure(b, grenadier, p) * 3f
            );
            b.Stats.Paths++;
            return route.Count > 0 && SupportAI.PathLength(grenadier.Position, route) <= 12f;
        }

        public static GroundLauncher AvailableNear(BattleSimulation b, Soldier grenadier, float maxRange)
        {
            GroundLauncher chosen = null;
            float score = float.MaxValue;
            foreach (var item in b.GroundLaunchers)
            {
                if (item.Scene != null || !item.Loaded || item.Collected)
                    continue;
                float distance = V2.Distance(grenadier.Position, item.To);
                if (distance > maxRange || !ThreatSafe(b, grenadier, item.To))
                    continue;
                float value = distance + (item.Flying(b.Time) ? 1f : 0f);
                if (value < score)
                {
                    score = value;
                    chosen = item;
                }
            }

            return chosen;
        }

        public static bool CanCollect(BattleSimulation b, Soldier s, GroundLauncher item) =>
            Ready(b, s)
            && (s.Role == Role.Grenadier || s.Role == Role.Commander)
            && s.Rockets == 0
            && item != null
            && (
                item.FromSoldierId < 0
                || item.FromSoldierId >= b.Soldiers.Count
                || b.Soldiers[item.FromSoldierId].Alive
                || CombatTraining.Skill(s, 2, "tube")
            )
            && item.Loaded
            && !item.Collected
            && !item.Flying(b.Time)
            && SceneEquipment.Reach(b, s.Position, item.To, item.Scene, 1.55f);

        public static bool Collect(BattleSimulation b, Soldier s, GroundLauncher item)
        {
            if (!CanCollect(b, s, item))
                return false;
            SceneEquipment.Consume(b, item.Scene);
            item.Collected = true;
            item.Loaded = false;
            s.LauncherKind = item.Kind;
            s.Rockets = 1;
            s.LauncherPickupId = -1;
            s.LauncherPickupRetryAt = b.Time + 1f;
            s.NextDecision = b.Time;
            s.LootRetryAt = b.Time + 1f;
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Destination = s.Position;
            s.Order = "Подобрал запасной одноразовый гранатомёт";
            b.Log(s.Side, s.Name + ": получил готовый гранатомёт (1/1)");
            return true;
        }

        public static bool HasPickupPriority(BattleSimulation b, Soldier s)
        {
            if (!Ready(b, s) || (s.Role != Role.Grenadier && s.Role != Role.Commander) || s.Rockets > 0)
                return false;
            if (s.LauncherPickupId < 0 || s.LauncherPickupId >= b.GroundLaunchers.Count)
                return false;
            var item = b.GroundLaunchers[s.LauncherPickupId];
            return item.Loaded && !item.Collected;
        }

        public static bool RoutePriority(BattleSimulation b, Soldier s) =>
            HasPickupPriority(b, s) && s.PathCursor < s.Path.Count && s.Suppression < .65f && s.Morale >= .30f;

        public static bool PlanPickup(BattleSimulation b, Soldier s)
        {
            if (
                !Ready(b, s)
                || (s.Role != Role.Grenadier && s.Role != Role.Commander)
                || s.Rockets > 0
                || s.SupplyDonorId >= 0
            )
                return false;
            // Do not lose a committed pickup route during its retry/cooldown window.
            if (HasPickupPriority(b, s) && s.PathCursor < s.Path.Count)
                return true;
            if (b.Time < s.LauncherPickupRetryAt)
                return CanWaitForSupply(b, s);
            if (HasPickupPriority(b, s))
            {
                var selected = b.GroundLaunchers[s.LauncherPickupId];
                if (selected.Flying(b.Time))
                    return true;
                if (Collect(b, s, selected))
                    return true;
                if (s.PathCursor < s.Path.Count)
                    return true;
            }

            var best = AvailableNear(b, s, 9f);
            if (best == null)
            {
                s.LauncherPickupId = -1;
                s.LauncherPickupRetryAt = b.Time + 1.5f;
                return false;
            }

            if (best.Flying(b.Time))
            {
                // Do not allow other AI orders to move away from a nearby incoming supply tube.
                if (V2.Distance(s.Position, best.To) < CollectionRadius)
                {
                    s.LauncherPickupId = best.Id;
                    return true;
                }
            }

            if (Collect(b, s, best))
                return true;
            var route = LossMemory.Find(b, s, best.To, p => PositionLogic.Exposure(b, s, p) * 3f);
            b.Stats.Paths++;
            if (route.Count == 0 || SupportAI.PathLength(s.Position, route) > 12f)
            {
                s.LauncherPickupRetryAt = b.Time + 2f;
                return false;
            }

            s.Path = route;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = best.To;
            s.LauncherPickupId = best.Id;
            s.LauncherPickupRetryAt = b.Time + 1.5f;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Иду за запасным гранатомётом";
            s.Reason = "Приоритет: подобрать доступную систему под прикрытием";
            return true;
        }

        public static bool TryCollectNearby(BattleSimulation b, Soldier s)
        {
            if (!Ready(b, s) || (s.Role != Role.Grenadier && s.Role != Role.Commander) || s.Rockets > 0)
                return false;
            if (
                s.LauncherPickupId >= 0
                && s.LauncherPickupId < b.GroundLaunchers.Count
                && Collect(b, s, b.GroundLaunchers[s.LauncherPickupId])
            )
                return true;
            foreach (var item in b.GroundLaunchers)
                if (Collect(b, s, item))
                    return true;
            return false;
        }

        public static bool TrySupplierThrow(BattleSimulation b, Soldier supplier, Soldier grenadier)
        {
            if (!CombatTraining.Skill(supplier, 1, "toss"))
                return false;
            if (
                !Ready(b, supplier)
                || !Ready(b, grenadier)
                || supplier.Id == grenadier.Id
                || supplier.Side != grenadier.Side
                || !supplier.SupplyPack
                || CombatTraining.AvailableLauncher(supplier, grenadier) < 0
                || (grenadier.Role != Role.Grenadier && grenadier.Role != Role.Commander)
                || grenadier.Rockets != 0
                || b.Time < supplier.HandsBusyUntil
                || supplier.ReloadUntil > b.Time
                || b.Time < supplier.LauncherThrowRetryAt
                || b.Time - supplier.LastIncoming < 2f
            )
                return false;
            float dist = V2.Distance(supplier.Position, grenadier.Position);
            if (dist <= 1.4f || dist > MaxSupplyThrowDistance)
                return false; // hand-off uses old SupplyAI.Transfer.
            if (!b.Map.LineOfSight(supplier.Position, grenadier.Position))
                return false;
            if (ReachableOnGround(b, grenadier, 7f))
                return false; // pickup first, delivery second.
            if (!ThreatSafe(b, grenadier, grenadier.Position))
                return false;
            V2 destination = Landing(
                b,
                supplier.Position,
                grenadier.Position,
                MaxSupplyThrowDistance,
                (grenadier.Id * 41 + supplier.Id * 13) % 628 * .01f
            );
            if (float.IsNaN(destination.X) || V2.Distance(destination, grenadier.Position) > CollectionRadius)
                return false;
            int kind = CombatTraining.AvailableLauncher(supplier, grenadier);
            var item = Spawn(b, supplier, kind, true, destination, .36f + dist * .11f);
            supplier.HandsBusyUntil = b.Time + .4f;
            CombatTraining.TakeLauncher(supplier, kind);
            supplier.LauncherThrowRetryAt = b.Time + 3f;
            grenadier.LauncherPickupId = item.Id;
            grenadier.LauncherPickupRetryAt = b.Time;
            grenadier.NextDecision = b.Time;
            b.Log(
                supplier.Side,
                supplier.Name + " перебросил " + (kind == 1 ? "РШГ" : "РПГ") + " для " + grenadier.Name
            );
            return true;
        }

        public static bool CanWaitForSupply(BattleSimulation b, Soldier s) =>
            HasPickupPriority(b, s)
            && b.GroundLaunchers[s.LauncherPickupId].Flying(b.Time)
            && V2.Distance(s.Position, b.GroundLaunchers[s.LauncherPickupId].To) <= CollectionRadius;
    }
}
