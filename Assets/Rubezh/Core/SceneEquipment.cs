using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Scene objects own their location. Native inventories own ammunition and collection state.
    public sealed class SceneEquipmentState
    {
        public readonly List<StudioPlaced> Items = new List<StudioPlaced>();
        public readonly Dictionary<int, ScenePickupPlan> Plans = new Dictionary<int, ScenePickupPlan>();
    }

    public sealed class ScenePickupPlan
    {
        public StudioPlaced Target;
        public int Revision;
        public float Retry,
            Until;
    }

    public static class SceneEquipment
    {
        public static V2 Position(StudioPlaced p) => new V2(p.X + .5f + p.OffsetX, p.Y + .5f + p.OffsetY);

        public static void Bind(BattleSimulation b)
        {
            b.SceneEquipment = new SceneEquipmentState();
            // A level replacement invalidates all old ground inventories and their numeric IDs.
            b.GroundWeapons.Clear();
            b.GroundLaunchers.Clear();
            b.GroundAmmo.Clear();
            b.GroundTourniquets.Clear();
            foreach (var s in b.Soldiers)
            {
                s.LootGroundId = -1;
                s.LauncherPickupId = -1;
            }

            if (b.Map.Studio == null)
                return;
            foreach (var p in b.Map.Studio.Decorations)
            {
                var a = p.Asset;
                if (string.IsNullOrEmpty(a.EquipmentKind))
                    continue;
                b.SceneEquipment.Items.Add(p);
                var at = Position(p);
                if (a.EquipmentKind == "firearm")
                    b.GroundWeapons.Add(
                        new GroundWeapon
                        {
                            Id = b.GroundWeapons.Count,
                            FromSoldierId = -1,
                            Scene = p,
                            GunRole =
                                a.EquipmentVariant % 6 == 1 ? Role.MachineGunner
                                : a.EquipmentVariant % 6 >= 4 ? Role.Sniper
                                : Role.Rifleman,
                            Variant = a.EquipmentVariant,
                            Magazine = WeaponBalance.Profiles[a.EquipmentVariant].Magazine,
                            From = at,
                            To = at,
                        }
                    );
                else if (a.EquipmentKind == "launcher")
                    b.GroundLaunchers.Add(
                        new GroundLauncher
                        {
                            Id = b.GroundLaunchers.Count,
                            FromSoldierId = -1,
                            Scene = p,
                            Kind = a.EquipmentVariant,
                            Loaded = true,
                            From = at,
                            To = at,
                        }
                    );
            }
        }

        public static bool Reach(BattleSimulation b, V2 from, V2 to, StudioPlaced p, float radius)
        {
            if (p == null)
                return InteractionReach.Can(b, from, to, radius);
            if (p.Destroyed || V2.Distance(from, to) > radius || !b.Map.Walkable(from))
                return false;
            // Only the item's own low support may be crossed by the hand. Never skip a wall or another prop.
            int steps = Math.Max(1, (int)Math.Ceiling(V2.Distance(from, to) * 12));
            int lastX = (int)Math.Floor(from.X),
                lastY = (int)Math.Floor(from.Y);
            for (int i = 0; i <= steps; i++)
            {
                var v = from + (to - from) * (i / (float)steps);
                int x = (int)Math.Floor(v.X),
                    y = (int)Math.Floor(v.Y);
                if (!b.Map.Inside(x, y))
                    return false;
                if (x != lastX && y != lastY && (!HandCell(b, p, x, lastY) || !HandCell(b, p, lastX, y)))
                    return false;
                lastX = x;
                lastY = y;
                var own = b.Map.Studio.Cells[y * b.Map.Columns + x];
                if (own == p.Support && own != null && !own.Destroyed)
                    continue;
                if (!b.Map.Walkable(v) || b.Map.At(v) == Tile.Wall)
                    return false;
            }

            return true;
        }

        static bool HandCell(BattleSimulation b, StudioPlaced p, int x, int y)
        {
            if (!b.Map.Inside(x, y))
                return false;
            var own = b.Map.Studio.Cells[y * b.Map.Columns + x];
            return own != null && own == p.Support && !own.Destroyed
                || b.Map.Walkable(new V2(x + .5f, y + .5f)) && b.Map.At(new V2(x + .5f, y + .5f)) != Tile.Wall;
        }

        public static V2 Approach(BattleSimulation b, Soldier s, StudioPlaced p)
        {
            var at = Position(p);
            if (Reach(b, s.Position, at, p, 1.55f))
                return s.Position;
            V2 best = new V2(float.NaN, float.NaN);
            float score = float.MaxValue;
            for (int y = p.Y - 1; y <= p.Y + 1; y++)
            for (int x = p.X - 1; x <= p.X + 1; x++)
            {
                var v = new V2(x + .5f, y + .5f);
                if (!Reach(b, v, at, p, 1.55f))
                    continue;
                float d = V2.Distance(s.Position, v);
                if (d < score)
                {
                    best = v;
                    score = d;
                }
            }

            return best;
        }

        public static void Consume(BattleSimulation b, StudioPlaced p)
        {
            if (p == null || p.Destroyed)
                return;
            p.Destroyed = true;
            b.Map.SceneryChanges.Record(b.Map, new[] { b.Map.Tiles.Length + b.Map.Studio.Decorations.IndexOf(p) });
        }

        static bool Ready(BattleSimulation b, Soldier s, bool bandage = false) =>
            s != null
            && s.Available
            && s.Deployed
            && !s.Broken
            && !s.Busy
            && (!s.Bleeding || bandage)
            && s.HandsBusyUntil <= b.Time
            && s.ReloadUntil <= b.Time
            && s.Suppression < .45f
            && s.Morale >= .3f
            && b.Time - s.LastIncoming >= 3
            && CombatTraining.Skill(s, 2, "loot")
            && !TacticalOrders.Emergency(b.Team(s))
            && !TacticalOrders.ServiceActive(s)
            && !ExecutionReview.ProtectedTask(b, s)
            && !SupplyRecovery.Active(s)
            && !BuddyActions.Active(s)
            && !HandLauncher.Active(s)
            && !s.GrenadeEscape.Active
            && !s.VehicleRally
            && !s.VehicleDisperse;

        static bool Wants(BattleSimulation b, Soldier s, StudioPlaced p)
        {
            if (p == null || p.Destroyed)
                return false;
            switch (p.Asset.EquipmentKind)
            {
                case "firearm":
                    return SquadLoadout.ClassKit(s.Role, p.Asset.EquipmentVariant % 6, 0)
                        && s.Ammo + s.Reserve == 0
                        && !s.HasBackWeapon;
                case "launcher":
                    return (p.Asset.EquipmentVariant != 1 || SquadLoadout.ClassKit(s.Role, 0, 4))
                        && s.Rockets == 0
                        && !s.SupplyPack
                        && !SupplyRecovery.Medical(s)
                        && (s.Role == Role.Commander || s.Role == Role.Grenadier || s.Contacts.Count > 0);
                case "tourniquet":
                    return s.Tourniquets == 0;
                case "medkit":
                    return SupplyRecovery.Medical(s) && s.Medkits == 0;
            }

            return false;
        }

        public static bool Collect(BattleSimulation b, Soldier s, StudioPlaced p)
        {
            if (
                !Ready(b, s, p?.Asset.EquipmentKind == "tourniquet")
                || !Wants(b, s, p)
                || !Reach(b, s.Position, Position(p), p, 1.55f)
            )
                return false;
            if (p.Asset.EquipmentKind == "firearm")
            {
                foreach (var g in b.GroundWeapons)
                    if (g.Scene == p)
                        return CorpseEquipment.CollectGround(b, s, g);
                return false;
            }

            if (p.Asset.EquipmentKind == "launcher")
            {
                foreach (var g in b.GroundLaunchers)
                    if (g.Scene == p)
                    {
                        if (s.Role == Role.Grenadier || s.Role == Role.Commander)
                            return LauncherLogistics.Collect(b, s, g);
                        if (g.Collected || !g.Loaded)
                            return false;
                        g.Collected = true;
                        g.Loaded = false;
                        s.Rockets = 1;
                        s.LauncherKind = g.Kind;
                        s.Mechanics.LauncherPickups++;
                        s.Mechanics.HandLauncher = true;
                        s.Mechanics.HandUntil = b.Time + 18;
                        s.Mechanics.HandRevision = b.Team(s).Revision;
                        s.Mechanics.HandNextPlan = b.Time;
                        break;
                    }
            }
            else if (p.Asset.EquipmentKind == "tourniquet")
                s.Tourniquets++;
            else if (p.Asset.EquipmentKind == "medkit")
                s.Medkits++;
            else
                return false;
            Consume(b, p);
            TacticalAI.Stop(s);
            s.OrderAnchor = s.Destination = s.Position;
            s.HandsBusyUntil = b.Time + .35f;
            b.Log(s.Side, s.Name + ": подобрал " + p.Asset.Name);
            return true;
        }

        static bool Safe(BattleSimulation b, Soldier s, V2 p) =>
            CommandPosture.Inside(b, s, p) && StraightAdvance.Inside(b, s, p) && PositionLogic.Exposure(b, s, p) < .45f;

        public static bool TryUpdate(BattleSimulation b, Soldier s)
        {
            var state = b.SceneEquipment;
            if (state == null || state.Items.Count == 0)
                return false;
            if (s.Bleeding && s.Tourniquets == 0)
                foreach (var p in state.Items)
                    if (p.Asset.EquipmentKind == "tourniquet" && Collect(b, s, p))
                        return true;
            ScenePickupPlan plan;
            if (!state.Plans.TryGetValue(s.Id, out plan))
            {
                plan = new ScenePickupPlan();
                state.Plans[s.Id] = plan;
            }

            if (plan.Target != null)
            {
                if (
                    !Ready(b, s)
                    || !Wants(b, s, plan.Target)
                    || b.Team(s).Revision != plan.Revision
                    || b.Time > plan.Until
                    || !Safe(b, s, s.Position)
                    || (s.PathCursor < s.Path.Count && !Safe(b, s, s.Path[s.PathCursor]))
                )
                {
                    plan.Target = null;
                    plan.Retry = b.Time + 2;
                    TacticalAI.Stop(s);
                    return false;
                }

                if (Collect(b, s, plan.Target))
                {
                    plan.Target = null;
                    plan.Retry = b.Time + 2;
                    return true;
                }

                if (s.PathCursor < s.Path.Count)
                {
                    s.MoveAuthorized = true;
                    s.ManeuverPaused = false;
                    return true;
                }

                plan.Target = null;
                plan.Retry = b.Time + 2;
                return false;
            }

            if (!Ready(b, s) || b.Time < plan.Retry || s.PathCursor < s.Path.Count)
                return false;
            plan.Retry = b.Time + 2;
            StudioPlaced best = null;
            V2 goal = new V2();
            float score = float.MaxValue;
            foreach (var p in state.Items)
            {
                if (!Wants(b, s, p) || V2.Distance(s.Position, Position(p)) > (s.Role == Role.Medic ? 3 : 8))
                    continue;
                if (Collect(b, s, p))
                    return true;
                var at = Approach(b, s, p);
                if (float.IsNaN(at.X) || !Safe(b, s, at))
                    continue;
                float d = V2.Distance(s.Position, at);
                if (d < score)
                {
                    best = p;
                    goal = at;
                    score = d;
                }
            }

            if (best == null)
                return false;
            var path = LossMemory.Find(b, s, goal, p => PositionLogic.Exposure(b, s, p) * 8);
            b.Stats.Paths++;
            if (path.Count == 0 || SupportAI.PathLength(s.Position, path) > (s.Role == Role.Medic ? 3 : 12))
                return false;
            foreach (var p in path)
                if (!Safe(b, s, p))
                    return false;
            plan.Target = best;
            plan.Revision = b.Team(s).Revision;
            plan.Until = b.Time + 12;
            s.Path = path;
            s.PathCursor = 0;
            s.Destination = s.OrderAnchor = goal;
            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Иду за снаряжением";
            s.Reason = best.Asset.Name;
            return true;
        }
    }
}
