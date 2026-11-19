using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Persistent physical firearm. Landing and claim state are authoritative, not renderer-owned.
    public sealed class GroundWeapon
    {
        public int Id,
            FromSoldierId;
        public Role GunRole;
        public int Variant,
            Magazine;
        public WeaponUpgrade Upgrade;
        public V2 From,
            To;
        public float Born,
            Duration,
            Angle;
        public bool Collected;
        public StudioPlaced Scene;

        public bool Flying(float now) => Duration > 0 && now < Born + Duration;

        public float ArcHeight(float now)
        {
            if (!Flying(now))
                return 0f;
            float t = Util.Clamp((now - Born) / Duration, 0, 1);
            return (float)Math.Sin(Math.PI * t) * 1.8f;
        }

        public V2 DisplayPosition(float now)
        {
            if (!Flying(now))
                return To;
            float t = Util.Clamp((now - Born) / Duration, 0, 1);
            return From + (To - From) * t;
        }
    }

    public static class CorpseEquipment
    {
        static int Total(Soldier s) => Math.Max(0, s.Ammo) + Math.Max(0, s.Reserve);

        static bool Empty(Soldier s) => Total(s) == 0;

        static bool Mobile(BattleSimulation b, Soldier s) =>
            s != null
            && s.Available
            && !s.Broken
            && s.Deployed
            && s.Suppression < .72f
            && s.Morale >= .24f
            && b.Time - s.LastIncoming >= 2f
            && !TacticalOrders.Emergency(b.Team(s));

        public static bool ValidDroppedWeaponSite(BattleMap map, V2 candidate)
        {
            if (!map.Walkable(candidate))
                return false;
            Tile tile = map.At(candidate);
            return tile != Tile.Wall && tile != Tile.LowCover;
        }

        // Stable candidate sampling: an object does not jump between random locations
        // when redrawn. Prefer a small empty patch next to the body, not inside cover.
        public static V2 FindWeaponLanding(BattleMap map, V2 center, float preferredAngle)
        {
            for (int ring = 0; ring < 3; ring++)
            {
                float radius = 1.25f + ring * .32f; // 1.25–1.89 tiles from corpse
                for (int i = 0; i < 12; i++)
                {
                    float angle = preferredAngle + (float)(i * Math.PI / 6);
                    V2 target = center + new V2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                    if (!ValidDroppedWeaponSite(map, target))
                        continue;
                    if (!map.LineOfSight(center, target))
                        continue;
                    return target;
                }
            }

            var nearest = map.NearestOpen(center);
            if (ValidDroppedWeaponSite(map, nearest))
                return nearest;
            // A fallback is allowed only when no safe site exists; still never put it
            // inside a solid/half-cover tile, even on a very small generated map.
            for (int y = 0; y < map.Rows; y++)
            for (int x = 0; x < map.Columns; x++)
            {
                var test = new V2(x + .5f, y + .5f);
                if (ValidDroppedWeaponSite(map, test))
                    return test;
            }

            return center;
        }

        public static GroundWeapon Place(
            BattleSimulation b,
            Role role,
            int variant,
            int magazine,
            V2 point,
            int owner,
            V2 destination,
            float flight = 0f,
            WeaponUpgrade upgrade = null
        )
        {
            int id = b.GroundWeapons.Count;
            if (!ValidDroppedWeaponSite(b.Map, destination))
                destination = FindWeaponLanding(b.Map, point, id * .27f + owner * .4f);
            var drop = new GroundWeapon
            {
                Id = id,
                FromSoldierId = owner,
                GunRole = role,
                Variant = variant,
                Upgrade = upgrade,
                Magazine = Math.Max(0, magazine),
                From = point,
                To = destination,
                Born = b.Time,
                Duration = flight,
                Angle = ((id * 71 + owner * 31 + 19) % 628) / 100f,
            };
            b.GroundWeapons.Add(drop);
            return drop;
        }

        public static void Drop(BattleSimulation b, Soldier fallen)
        {
            if (fallen.WeaponDropped)
                return;
            uint hash = unchecked(((uint)fallen.Id * 747796405u) ^ ((uint)b.TickNumber * 2891336453u));
            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            float a = (hash % 6283u) / 1000f,
                g = ((hash >> 10) % 6283u) / 1000f;
            fallen.CorpseAngle = 1.57f;
            fallen.DroppedWeaponAngle = g; // fixed classic body, randomized gun
            fallen.DroppedWeaponPosition = FindWeaponLanding(b.Map, fallen.Position, g);
            fallen.DroppedWeaponRole = fallen.GunRole;
            fallen.DroppedWeaponVariant = WeaponBalance.Index(fallen);
            fallen.DroppedWeaponUpgrade = fallen.WeaponUpgrade;
            // Reserve magazines stay with the corpse's inventory, NOT inside its dropped weapon.
            fallen.DroppedAmmo = Math.Max(0, fallen.Ammo);
            fallen.DroppedReserve = 0;
            fallen.WeaponDropped = true;
            fallen.WeaponClaimed = false;
            if (fallen.HasBackWeapon)
            {
                var v = FindWeaponLanding(b.Map, fallen.Position, a + 1.9f);
                Place(
                    b,
                    fallen.BackGunRole,
                    fallen.Side == Side.Red && fallen.BackVariant < 6 ? fallen.BackVariant + 6 : fallen.BackVariant,
                    fallen.BackAmmo,
                    fallen.Position,
                    fallen.Id,
                    v,
                    upgrade: fallen.BackWeaponUpgrade
                );
                fallen.HasBackWeapon = false;
                fallen.BackAmmo = fallen.BackReserve = 0;
            }
        }

        public static bool CanTake(BattleSimulation b, Soldier looter, Soldier corpse)
        {
            if (!CombatTraining.Skill(looter, 2, "loot"))
                return false;
            return looter != null
                && corpse != null
                && looter.Id != corpse.Id
                && looter.Alive
                && !looter.Incapacitated
                && !looter.Broken
                && looter.Deployed
                && !corpse.Alive
                && corpse.Deployed
                && corpse.WeaponDropped
                && !corpse.WeaponClaimed
                && corpse.DroppedAmmo > 0
                && Empty(looter)
                && !TacticalOrders.Emergency(b.Team(looter));
        }

        static bool HasNeedyAlly(BattleSimulation b, Soldier helper)
        {
            foreach (var ally in b.Soldiers)
                if (
                    ally.Side == helper.Side
                    && ally.Id != helper.Id
                    && ally.Alive
                    && ally.Deployed
                    && !ally.Incapacitated
                    && Empty(ally)
                    && V2.Distance(ally.Position, helper.Position) < 14f
                )
                    return true;
            return false;
        }

        static bool CanCarry(BattleSimulation b, Soldier looter)
        {
            return CombatTraining.Skill(looter, 1, "toss")
                && Mobile(b, looter)
                && !looter.HasBackWeapon
                && HasNeedyAlly(b, looter)
                && Total(looter) > 0;
        }

        static bool CanPlanPickup(BattleSimulation b, Soldier s, Role role, int variant, int rounds, bool forAlly)
        {
            if (rounds <= 0 || s.HasBackWeapon)
                return false;
            if (s.Side == Side.Green && role == Role.MachineGunner)
                return !forAlly && rounds >= 30 && NewMechanics.CanUseDroppedMG(b, s);
            return s.Side != Side.Green || SquadLoadout.ClassKit(s.Role, variant % 6, 0);
        }

        static bool Acquire(
            BattleSimulation b,
            Soldier s,
            Role role,
            int variant,
            int loaded,
            bool forAlly,
            WeaponUpgrade upgrade
        )
        {
            if (
                loaded <= 0
                || s.HasBackWeapon
                || s.Side == Side.Green && !SquadLoadout.ClassKit(s.Role, variant % 6, 0)
            )
                return false;
            // One rifle in hand, exactly one on the back. No reserve ammo is transferred.
            if (Total(s) > 0)
            {
                if (!forAlly)
                    return false;
                s.HasBackWeapon = true;
                s.BackGunRole = role;
                s.BackVariant = variant;
                s.BackWeaponUpgrade = upgrade;
                s.BackAmmo = loaded;
                s.BackReserve = 0;
                s.BackMagazines = new MagazineStore(0);
            }
            else
            {
                s.HasBackWeapon = true;
                s.BackGunRole = s.GunRole;
                s.BackVariant = WeaponBalance.Index(s);
                s.BackWeaponUpgrade = s.WeaponUpgrade;
                s.BackAmmo = s.Ammo;
                s.BackReserve = s.Reserve;
                s.BackMagazines = s.Magazines;
                s.Magazines = new MagazineStore(0);
                s.GunRoleOverride = (int)role;
                s.WeaponVariant = variant;
                s.WeaponUpgrade = upgrade;
                s.Ammo = loaded;
                s.Reserve = 0;
                s.ActiveRecoveredWeapon = true;
                s.BurstRounds = 0;
                s.ReloadUntil = 0;
                s.NextShot = Math.Max(s.NextShot, b.Time + .65f);
            }

            s.EquipmentRecoveryRoute = null;
            s.LootGroundId = s.LootTargetId = -1;
            s.LootRetryAt = b.Time + 1f;
            s.WeaponPickupPriority = false;
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Destination = s.Position;
            s.NextDecision = b.Time;
            s.Order = forAlly ? "Нашёл запасное оружие для товарища" : "Подобрал оружие";
            b.Log(s.Side, s.Name + ": подобрал оружие с магазином " + loaded + " патр.");
            return true;
        }

        public static bool Collect(BattleSimulation b, Soldier looter, Soldier corpse)
        {
            if (!CombatTraining.Skill(looter, 2, "loot"))
                return false;
            if (looter.Side == Side.Green && corpse.DroppedWeaponRole == Role.MachineGunner)
                return NewMechanics.InheritMG(b, looter);
            bool needy = CanTake(b, looter, corpse),
                carry = CanCarry(b, looter);
            if (
                (!needy && !carry)
                || corpse.DroppedAmmo <= 0
                || corpse.WeaponClaimed
                || V2.Distance(looter.Position, corpse.DroppedWeaponPosition) > 1.55f
            )
                return false;
            if (
                !Acquire(
                    b,
                    looter,
                    corpse.DroppedWeaponRole,
                    corpse.DroppedWeaponVariant,
                    corpse.DroppedAmmo,
                    carry,
                    corpse.DroppedWeaponUpgrade
                )
            )
                return false;
            corpse.WeaponClaimed = true;
            corpse.DroppedAmmo = corpse.DroppedReserve = 0;
            return true;
        }

        public static bool CollectGround(BattleSimulation b, Soldier looter, GroundWeapon item)
        {
            if (
                item != null
                && item.FromSoldierId >= 0
                && item.FromSoldierId < b.Soldiers.Count
                && !b.Soldiers[item.FromSoldierId].Alive
                && !CombatTraining.Skill(looter, 2, "loot")
            )
                return false;
            if (
                item == null
                || item.Collected
                || item.Magazine <= 0
                || item.Flying(b.Time)
                || !SceneEquipment.Reach(b, looter.Position, item.To, item.Scene, 1.55f)
            )
                return false;
            if (looter.Side == Side.Green && item.GunRole == Role.MachineGunner)
                return NewMechanics.InheritMG(b, looter);
            bool carry = CanCarry(b, looter);
            if (!Empty(looter) && !carry)
                return false;
            if (!Acquire(b, looter, item.GunRole, item.Variant, item.Magazine, carry, item.Upgrade))
                return false;
            item.Collected = true;
            item.Magazine = 0;
            SceneEquipment.Consume(b, item.Scene);
            return true;
        }

        static bool SwitchBack(BattleSimulation b, Soldier s)
        {
            if (!s.HasBackWeapon)
                return false;
            bool oldWasRecovered = s.ActiveRecoveredWeapon;
            Role oldRole = s.GunRole;
            int oldVariant = WeaponBalance.Index(s),
                oldAmmo = s.Ammo,
                oldReserve = s.Reserve;
            var oldMagazines = s.Magazines;
            var oldUpgrade = s.WeaponUpgrade;
            s.WeaponUpgrade = s.BackWeaponUpgrade;
            s.BackWeaponUpgrade = oldUpgrade;
            s.GunRoleOverride = (int)s.BackGunRole;
            s.WeaponVariant = s.BackVariant;
            s.Ammo = s.BackAmmo;
            s.Magazines = s.BackMagazines;
            s.BackMagazines = oldMagazines;
            s.BackGunRole = oldRole;
            s.BackVariant = oldVariant;
            s.BackAmmo = oldAmmo;
            s.BackReserve = oldReserve;
            // If a looted weapon ran dry, discard it and retrieve the original from the back.
            if (oldWasRecovered && oldAmmo + oldReserve == 0)
            {
                Place(
                    b,
                    oldRole,
                    oldVariant,
                    0,
                    s.Position,
                    s.Id,
                    s.Position + new V2(.5f, -.25f),
                    upgrade: oldUpgrade
                );
                s.HasBackWeapon = false;
                s.ActiveRecoveredWeapon = false;
                s.BackAmmo = s.BackReserve = 0;
                b.Log(s.Side, s.Name + ": бросил пустое подобранное оружие");
            }
            else
            {
                s.ActiveRecoveredWeapon = true;
            }

            s.BurstRounds = 0;
            s.ReloadUntil = 0;
            s.NextShot = Math.Max(s.NextShot, b.Time + .4f);
            return true;
        }

        public static bool SupplyThrow(BattleSimulation b, Soldier helper, Soldier receiver)
        {
            if (!CombatTraining.Skill(helper, 1, "toss"))
                return false;
            if (
                helper == null
                || receiver == null
                || helper.Research.BorrowedMG
                || helper.Side != receiver.Side
                || !helper.HasBackWeapon
                || helper.BackAmmo <= 0
                || !helper.Available
                || !receiver.Alive
                || receiver.Incapacitated
                || !Empty(receiver)
                || TacticalOrders.Emergency(b.Team(helper))
            )
                return false;
            float distance = V2.Distance(helper.Position, receiver.Position);
            if (distance > 4f || distance < .6f || !b.Map.LineOfSight(helper.Position, receiver.Position))
                return false;
            // Use the receiver's current position. One real magazine moves through the air.
            var ground = Place(
                b,
                helper.BackGunRole,
                helper.BackVariant,
                helper.BackAmmo,
                helper.Position,
                helper.Id,
                receiver.Position,
                .38f + .11f * distance,
                helper.BackWeaponUpgrade
            );
            helper.BackAmmo = helper.BackReserve = 0;
            helper.HasBackWeapon = false;
            if (helper.WeaponAidTargetId >= 0)
                ReleaseRoute(helper);
            helper.WeaponAidTargetId = -1;
            helper.WeaponAidRetryAt = b.Time + 5f;
            ReleaseRoute(receiver);
            receiver.LootGroundId = ground.Id;
            receiver.LootTargetId = -1;
            receiver.WeaponPickupPriority = true;
            receiver.NextDecision = b.Time;
            b.Log(helper.Side, helper.Name + ": перебросил оружие " + receiver.Name);
            return true;
        }

        static float Danger(BattleSimulation b, Soldier s, V2 pos)
        {
            float danger = PositionLogic.Exposure(b, s, pos) * 4f;
            foreach (var c in b.Team(s).Contacts.Values)
            {
                if (c.SeenAt > b.Time || b.Time - c.SeenAt > 8f || c.Confidence < .25f)
                    continue;
                float d = V2.Distance(pos, c.Position);
                if (d < 4f)
                    danger += 30f;
                else if (d < 8f)
                    danger += 12f * (8f - d) / 4f;
                // Search *away* from known threat when possible.
                if (d < V2.Distance(s.Position, c.Position) - 1f)
                    danger += 5f;
            }

            return danger;
        }

        // Local recovery keeps its old radius. An empty fighter may also retrieve a
        // firearm personally visible within the executor's existing twelve-cell reach.
        // This is not team-wide knowledge: directional vision, walls and smoke apply.
        static bool Searchable(BattleSimulation b, Soldier s, V2 point, float distance, bool needGun)
        {
            return distance <= 9f || needGun && distance <= 12f && BattleVisibility.Sees(b, s, point);
        }

        static bool OwnsRoute(Soldier s) =>
            s.EquipmentRecoveryRoute != null
            && Object.ReferenceEquals(s.Path, s.EquipmentRecoveryRoute)
            && V2.Distance(s.OrderAnchor, s.EquipmentRecoveryGoal) < .01f;

        static void ReleaseRoute(Soldier s)
        {
            if (OwnsRoute(s))
                TacticalAI.Stop(s);
            s.EquipmentRecoveryRoute = null;
            s.LootTargetId = s.LootGroundId = -1;
        }

        static bool Follow(BattleSimulation b, Soldier s, V2 point, int corpseId, int groundId, float maxRoute)
        {
            float direct = V2.Distance(s.Position, point);
            if (direct < 1.55f)
                return false;
            if (direct > 12f)
                return false;
            if (s.PathCursor < s.Path.Count && OwnsRoute(s) && s.LootTargetId == corpseId && s.LootGroundId == groundId)
            {
                s.MoveAuthorized = true;
                s.ManeuverPaused = false;
                return true;
            }

            if (s.PathCursor < s.Path.Count)
                return false;
            var path = LossMemory.Find(b, s, point, p => Danger(b, s, p) * .8f);
            b.Stats.Paths++;
            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > maxRoute)
                return false;
            // The extended search never authorizes a riskier route than the existing
            // recovery movement gate. Inspect the entire route before publishing it.
            if (direct > 9f)
                foreach (var step in path)
                    if (Danger(b, s, step) >= 5f)
                        return false;
            CommandHierarchy.TransferAssignment(b, s, "Передано поиску оружия и боеприпасов");
            s.Path = s.EquipmentRecoveryRoute = path;
            s.EquipmentRecoveryGoal = point;
            s.PathCursor = 0;
            s.OrderAnchor = s.Destination = point;
            s.LootTargetId = corpseId;
            s.LootGroundId = groundId;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Ищу оружие и патроны";
            s.Reason = "Безопасный путь в стороне от известных угроз";
            return true;
        }

        static Soldier Needy(BattleSimulation b, Soldier helper)
        {
            Soldier best = null;
            float distance = float.MaxValue;
            foreach (var unit in b.Soldiers)
                if (
                    unit.Side == helper.Side
                    && unit.Id != helper.Id
                    && unit.Alive
                    && !unit.Incapacitated
                    && unit.Deployed
                    && Empty(unit)
                    && V2.Distance(helper.Position, unit.Position) < distance
                )
                {
                    best = unit;
                    distance = V2.Distance(helper.Position, unit.Position);
                }

            return best;
        }

        public static bool RecoveryRouteSafe(BattleSimulation b, Soldier mover)
        {
            if (
                !OwnsRoute(mover)
                || (mover.LootTargetId < 0 && mover.LootGroundId < 0 && mover.WeaponAidTargetId < 0)
                || mover.PathCursor >= mover.Path.Count
                || !mover.Available
                || mover.Broken
                || mover.Suppression > .70f
                || mover.Morale < .25f
            )
                return false;
            var next = mover.Path[mover.PathCursor];
            return Danger(b, mover, next) < 5f; // never treats direct hostile fire as guaranteed cover
        }

        public static bool ShouldReleaseWaiting(BattleSimulation b, Soldier mover)
        {
            // When only two remain, waiting for covering fire from an unarmed partner
            // is a deadlock. Armed partner can guard the retrieval and move defensively.
            int alive = 0;
            Soldier friend = null;
            foreach (var s in b.Soldiers)
                if (s.Side == mover.Side && s.Alive && !s.Incapacitated && s.Deployed)
                {
                    alive++;
                    if (s.Id != mover.Id)
                        friend = s;
                }

            if (alive != 2 || friend == null || Total(mover) <= 0 || Total(friend) > 0)
                return false;
            if (mover.Broken || mover.Suppression > .70f || mover.Morale < .25f)
                return false;
            mover.Reason = "Напарник без БК — прикрываю поиск оружия, не жду его огня";
            return true;
        }

        public static bool ValidWeaponAid(BattleSimulation b, Soldier s)
        {
            int id = s.WeaponAidTargetId;
            if (
                s.Research.BorrowedMG
                || id < 0
                || id >= b.Soldiers.Count
                || id == s.Id
                || !s.HasBackWeapon
                || s.BackAmmo <= 0
                || Total(s) <= 0
            )
                return false;
            var target = b.Soldiers[id];
            return target.Side == s.Side
                && target.Alive
                && target.Deployed
                && !target.Incapacitated
                && Empty(target)
                && V2.Distance(s.Position, target.Position) <= 12f;
        }

        public static void MaintainWeaponAid(BattleSimulation b, Soldier s)
        {
            if (s.WeaponAidTargetId < 0 || ValidWeaponAid(b, s))
                return;
            ReleaseRoute(s);
            s.WeaponAidTargetId = -1;
            s.WeaponAidRetryAt = b.Time + 2.5f;
            s.NextDecision = 0;
            b.Team(s).NextPlan = 0;
            s.Ai.Rejected = "";
            b.Diagnostics.Add(
                b,
                s,
                "finish",
                "Supply",
                "Доставка оружия отменена: получатель или запасное оружие недоступны"
            );
        }

        public static bool TryUpdate(BattleSimulation b, Soldier s)
        {
            if (s == null || !s.Alive || !s.Deployed)
                return false;
            if (s.Research.BorrowedMG)
                NewMechanics.ReturnPrimary(b, s);
            else if (Empty(s) && s.HasBackWeapon)
                SwitchBack(b, s);
            if (TacticalOrders.Emergency(b.Team(s)))
            {
                ReleaseRoute(s);
                s.WeaponAidTargetId = -1;
                return false;
            }

            // A newer executor owns its own route. Retire only our bookkeeping;
            // never clear or re-authorize that foreign path using stale pickup IDs.
            if (s.EquipmentRecoveryRoute != null && !OwnsRoute(s))
            {
                ReleaseRoute(s);
                s.WeaponAidTargetId = -1;
                return false;
            }

            MaintainWeaponAid(b, s);
            if (!Mobile(b, s))
                return false;
            if (!s.Research.BorrowedMG && s.HasBackWeapon && s.BackAmmo > 0 && Total(s) > 0)
            {
                var need = Needy(b, s);
                if (need != null && V2.Distance(s.Position, need.Position) <= 4f)
                    SupplyThrow(b, s, need);
                else if (need != null && V2.Distance(s.Position, need.Position) <= 12f && b.Time >= s.WeaponAidRetryAt)
                {
                    s.WeaponAidTargetId = need.Id;
                    s.WeaponAidRetryAt = b.Time + 2.5f;
                    // Rendezvous near the receiver's shelter, not in the middle of enemy fire.
                    if (PositionLogic.Protected(b, need, need.Position))
                    {
                        var back = (s.Position - need.Position).Normalized;
                        var rendezvous = TacticalOrders.Open(b, need.Position + back * 2f);
                        if (Danger(b, s, rendezvous) < 6f)
                            Follow(b, s, rendezvous, -1, -1, 10f);
                    }
                }
            }

            bool needGun = Empty(s);
            bool aid = !needGun && CanCarry(b, s);
            if (!needGun && !aid)
            {
                // A loaded spare may already be travelling to a needy partner.
                // Otherwise incoming ammunition satisfies our own retrieval task.
                if (!ValidWeaponAid(b, s))
                    ReleaseRoute(s);
                return false;
            }

            if (s.LootGroundId >= 0 && s.LootGroundId < b.GroundWeapons.Count)
            {
                var item = b.GroundWeapons[s.LootGroundId];
                if (!item.Collected && item.Magazine > 0)
                {
                    if (CollectGround(b, s, item))
                        return true;
                    if (item.Flying(b.Time))
                        return true; // await projectile landing, no repeated routes
                }
            }

            // A claimed or emptied resource ends this task before another target is
            // considered. Otherwise its old route could block a valid replacement.
            if (OwnsRoute(s) && (s.LootTargetId >= 0 || s.LootGroundId >= 0))
            {
                bool bodyAvailable =
                    s.LootTargetId >= 0
                    && s.LootTargetId < b.Soldiers.Count
                    && !b.Soldiers[s.LootTargetId].Alive
                    && !b.Soldiers[s.LootTargetId].WeaponClaimed
                    && b.Soldiers[s.LootTargetId].DroppedAmmo > 0;
                bool groundAvailable =
                    s.LootGroundId >= 0
                    && s.LootGroundId < b.GroundWeapons.Count
                    && !b.GroundWeapons[s.LootGroundId].Collected
                    && b.GroundWeapons[s.LootGroundId].Magazine > 0;
                if (!bodyAvailable && !groundAvailable)
                    ReleaseRoute(s);
            }

            // Follow the chosen item until arrival, unless someone already took it.
            if (s.PathCursor < s.Path.Count)
            {
                if (s.LootTargetId >= 0 && s.LootTargetId < b.Soldiers.Count)
                {
                    var bodyTarget = b.Soldiers[s.LootTargetId];
                    if (!bodyTarget.Alive && !bodyTarget.WeaponClaimed && bodyTarget.DroppedAmmo > 0)
                    {
                        if (V2.Distance(s.Position, bodyTarget.DroppedWeaponPosition) <= 1.55f)
                            return Collect(b, s, bodyTarget);
                        return false;
                    }
                }

                if (s.LootGroundId >= 0 && s.LootGroundId < b.GroundWeapons.Count)
                {
                    var groundTarget = b.GroundWeapons[s.LootGroundId];
                    if (!groundTarget.Collected && groundTarget.Magazine > 0)
                    {
                        if (V2.Distance(s.Position, groundTarget.To) <= 1.55f)
                            return CollectGround(b, s, groundTarget);
                        return false;
                    }
                }
            }

            if (b.Time < s.LootRetryAt && s.PathCursor >= s.Path.Count)
                return false;
            Soldier body = null;
            GroundWeapon ground = null;
            float best = float.MaxValue;
            foreach (var corpse in b.Soldiers)
            {
                if (
                    !CombatTraining.Skill(s, 2, "loot")
                    || !CanPlanPickup(
                        b,
                        s,
                        corpse.DroppedWeaponRole,
                        corpse.DroppedWeaponVariant,
                        corpse.DroppedAmmo,
                        aid
                    )
                )
                    continue;
                if (
                    corpse.Alive
                    || !corpse.Deployed
                    || !corpse.WeaponDropped
                    || corpse.WeaponClaimed
                    || corpse.DroppedAmmo <= 0
                )
                    continue;
                if (!needGun && !aid)
                    continue;
                float dist = V2.Distance(s.Position, corpse.DroppedWeaponPosition);
                float score = dist + Danger(b, s, corpse.DroppedWeaponPosition) * 2.4f;
                if (score < best && Searchable(b, s, corpse.DroppedWeaponPosition, dist, needGun))
                {
                    best = score;
                    body = corpse;
                    ground = null;
                }
            }

            foreach (var item in b.GroundWeapons)
            {
                if (
                    item.FromSoldierId >= 0
                    && item.FromSoldierId < b.Soldiers.Count
                    && !b.Soldiers[item.FromSoldierId].Alive
                    && !CombatTraining.Skill(s, 2, "loot")
                )
                    continue;
                if (!CanPlanPickup(b, s, item.GunRole, item.Variant, item.Magazine, aid))
                    continue;
                if (item.Scene != null || item.Collected || item.Magazine <= 0 || item.Flying(b.Time))
                    continue;
                float dist = V2.Distance(s.Position, item.To);
                float score = dist + Danger(b, s, item.To) * 2.4f;
                if (score < best && Searchable(b, s, item.To, dist, needGun))
                {
                    best = score;
                    ground = item;
                    body = null;
                }
            }

            if (body == null && ground == null)
            {
                if (s.LootTargetId >= 0 || s.LootGroundId >= 0)
                {
                    ReleaseRoute(s);
                }

                s.LootRetryAt = b.Time + 2.5f;
                // If a helper carries a loaded spare gun, seek a local sheltered rendezvous.
                if (needGun)
                    foreach (var helper in b.Soldiers)
                    {
                        if (
                            helper.Research.BorrowedMG
                            || helper.Side != s.Side
                            || !helper.Alive
                            || helper.Id == s.Id
                            || !helper.HasBackWeapon
                            || helper.BackAmmo <= 0
                        )
                            continue;
                        float d = V2.Distance(s.Position, helper.Position);
                        if (d > 9f || d < .8f || b.Time < s.WeaponAidRetryAt)
                            continue;
                        if (
                            !PositionLogic.Protected(b, s, s.Position)
                            && PositionLogic.Protected(b, helper, helper.Position)
                        )
                        {
                            V2 helperRendezvousGoal = TacticalOrders.Open(
                                b,
                                helper.Position + (s.Position - helper.Position).Normalized * 2f
                            );
                            if (Danger(b, s, helperRendezvousGoal) < 5f)
                            {
                                Follow(b, s, helperRendezvousGoal, -1, -1, 10f);
                                s.WeaponAidRetryAt = b.Time + 2f;
                            }
                        }

                        break;
                    }

                return false;
            }

            V2 goal = body != null ? body.DroppedWeaponPosition : ground.To;
            if (V2.Distance(s.Position, goal) <= 1.55f)
            {
                if (body != null)
                    return Collect(b, s, body);
                return CollectGround(b, s, ground);
            }

            // Without ammo, seeking ammo takes priority over routine tactical holding.
            bool route = Follow(b, s, goal, body?.Id ?? -1, ground?.Id ?? -1, 12f);
            if (!route)
                s.LootRetryAt = b.Time + 2f;
            return route;
        }
    }
}
