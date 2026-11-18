using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class NewMechanicsState
    {
        public bool BorrowedMG;
        public int[] Uses = new int[10];
        public float Next, VolleyUntil = -1, GuardUntil, ScoutUntil, ScoutNext, RotationAt;
        public int Revision, MoveKind, RotationShots;
        public V2 Home, Goal;
        public List<V2> Trail = new List<V2>();
    }

    // Only the two accepted mechanics may run; historical counters keep stable indices.
    public static class NewMechanics
    {
        public const int AcceptedMask = 6;
        // Compatibility default for new battles, not an active-battle switch.
        static int mask = AcceptedMask;
        public static int Mask
        {
            get
            {
                return mask;
            }

            set
            {
                mask = value & AcceptedMask;
            }
        }

        public static readonly string[] Names =
        {
            "Тайник боеприпасов",
            "Подхват пулемёта",
            "Отбросить живую гранату",
            "Согласованный первый залп",
            "Смена подавляющего бойца",
            "Разведка с возвратом",
            "Магазин от раненого",
            "Передача гранат",
            "Временное прикрытие тыла",
            "Отход по своему следу"
        };
        public static void SetMask(BattleSimulation b, int value)
        {
            if (b == null)
            {
                Mask = value;
                return;
            }

            b.Rules.SetMechanicsMask(value);
            foreach (var s in b.Soldiers)
            {
                if (s.Research.MoveKind != 0)
                {
                    TacticalAI.Stop(s);
                    s.NextDecision = b.Time;
                }

                s.Research.MoveKind = 0;
                s.Research.GuardUntil = 0;
                s.Research.VolleyUntil = -1;
            }
        }

        public static bool On(Soldier s, int bit) => s.Side == Side.Green && (s.Rules.MechanicsMask & (1 << bit)) != 0;
        public static bool On(BattleSimulation b, Soldier s, int bit) => s.Side == Side.Green && (b.Rules.MechanicsMask & (1 << bit)) != 0;
        static bool Free(BattleSimulation b, Soldier s) => s.Available && s.Deployed && !s.Broken && !s.Bleeding && !s.Busy && !TacticalOrders.ServiceActive(s) && !s.GrenadeEscape.Active && !s.VehicleDisperse && !s.VehicleRally && s.HandsBusyUntil <= b.Time && s.ReloadUntil <= b.Time && s.Suppression < .45f && !TacticalOrders.Emergency(b.Team(s));
        static void Count(BattleSimulation b, Soldier s, int k, string reason)
        {
            s.Research.Uses[k]++;
            s.Reason = reason;
            b.Diagnostics.Add(b, s, "start", "NewMechanic" + k, reason);
        }

        // Retired experiment API retained for old tools; these bits were never accepted.
        public static bool Cache(BattleSimulation b, Soldier s) => false;
        public static bool CanUseDroppedMG(BattleSimulation b, Soldier s) => On(b, s, 1) && CombatTraining.Skill(s, 2, "mg") && !s.HasBackWeapon && !s.GunMachineGunner && !s.GunSniper && s.Role != Role.Medic && !HandLauncher.Active(s) && !SupplyRecovery.Active(s);
        static bool CanTakeMG(BattleSimulation b, Soldier s) => CanUseDroppedMG(b, s) && Free(b, s) && !s.Moving;
        static bool Basic(Soldier s) => WeaponBalance.Index(s) % 6 == 0;
        static bool Priority(BattleSimulation b, Soldier s, V2 point)
        {
            if (Basic(s))
                return true;
            foreach (var a in b.Soldiers)
                if (a != s && a.Side == s.Side && CanTakeMG(b, a) && Basic(a) && InteractionReach.Can(b, a.Position, point, 1.4f))
                    return false;
            return true;
        }

        static void EquipMG(BattleSimulation b, Soldier s, int variant, int rounds, WeaponUpgrade upgrade)
        {
            s.HasBackWeapon = true;
            s.BackGunRole = s.GunRole;
            s.BackVariant = WeaponBalance.Index(s);
            s.BackWeaponUpgrade = s.WeaponUpgrade;
            s.BackAmmo = s.Ammo;
            s.BackReserve = s.Reserve;
            s.BackMagazines = s.Magazines;
            s.Magazines = new MagazineStore(0);
            s.GunRoleOverride = (int)Role.MachineGunner;
            s.WeaponVariant = variant;
            s.WeaponUpgrade = upgrade;
            s.Ammo = rounds;
            s.Reserve = 0;
            s.ActiveRecoveredWeapon = true;
            s.Research.BorrowedMG = true;
            s.WeaponAidTargetId = -1;
            s.BurstRounds = 0;
            s.HandsBusyUntil = b.Time + 1.2f;
            Count(b, s, 1, "Поднимаю пулемёт с остатком короба; основное оружие за спиной");
        }

        public static bool InheritMG(BattleSimulation b, Soldier s)
        {
            if (!CanTakeMG(b, s))
                return false;
            foreach (var c in b.Soldiers)
                if (!c.Alive && c.Deployed && c.WeaponDropped && !c.WeaponClaimed && c.DroppedWeaponRole == Role.MachineGunner && c.DroppedAmmo >= 30 && InteractionReach.Can(b, s.Position, c.DroppedWeaponPosition, 1.4f) && Priority(b, s, c.DroppedWeaponPosition))
                {
                    EquipMG(b, s, c.Side == Side.Red && c.DroppedWeaponVariant < 6 ? c.DroppedWeaponVariant + 6 : c.DroppedWeaponVariant, c.DroppedAmmo, c.DroppedWeaponUpgrade);
                    c.DroppedAmmo = 0;
                    c.WeaponClaimed = true;
                    return true;
                }

            foreach (var g in b.GroundWeapons)
                if (!g.Collected && !g.Flying(b.Time) && g.GunRole == Role.MachineGunner && g.Magazine >= 30 && SceneEquipment.Reach(b, s.Position, g.To, g.Scene, 1.55f) && Priority(b, s, g.To))
                {
                    EquipMG(b, s, g.Variant, g.Magazine, g.Upgrade);
                    g.Magazine = 0;
                    g.Collected = true;
                    SceneEquipment.Consume(b, g.Scene);
                    return true;
                }

            return false;
        }

        public static bool BoxAvailable(BattleSimulation b, Soldier s)
        {
            if (AmmoLogistics.Pending(b, s))
                return true;
            foreach (var a in b.Soldiers)
                if (a != s && a.Side == s.Side && a.Available && a.Deployed && !a.Broken && !a.Bleeding && a.SupplyPack && AmmoLogistics.PackCount(a, s, 2) > 0 && AmmoLogistics.PackCompatible(a, s, 2) && V2.Distance(a.Position, s.Position) <= 12 && Locomotion.WalkableSegment(b, a.Position, s.Position))
                    return true;
            return false;
        }

        public static bool ReturnPrimary(BattleSimulation b, Soldier s)
        {
            if (!s.Research.BorrowedMG || !s.HasBackWeapon)
                return false;
            bool quiet = s.Visible.Count == 0 && b.Time - s.LastFire > 5 && b.Time - s.LastIncoming > 5 && s.Suppression < .2f;
            foreach (var contact in s.Contacts.Values)
                if (b.Time - contact.SeenAt < 5 && contact.Confidence > .3f)
                    quiet = false;
            if (s.Reserve > 0 || s.Ammo >= 30 || (s.Ammo > 0 && !quiet) || BoxAvailable(b, s) || s.HandsBusyUntil > b.Time || s.ReloadUntil > b.Time)
                return false;
            CorpseEquipment.Place(b, s.GunRole, WeaponBalance.Index(s), s.Ammo, s.Position, s.Id, s.Position + new V2(.5f, -.25f), upgrade: s.WeaponUpgrade);
            s.GunRoleOverride = (int)s.BackGunRole;
            s.WeaponVariant = s.BackVariant;
            s.WeaponUpgrade = s.BackWeaponUpgrade;
            s.BackWeaponUpgrade = null;
            s.Ammo = s.BackAmmo;
            s.Magazines = s.BackMagazines;
            s.BackMagazines = new MagazineStore(0);
            s.BackAmmo = s.BackReserve = 0;
            s.HasBackWeapon = false;
            s.ActiveRecoveredWeapon = false;
            s.Research.BorrowedMG = false;
            s.BurstRounds = 0;
            s.ReloadUntil = 0;
            s.HandsBusyUntil = b.Time + .6f;
            s.NextShot = Math.Max(s.NextShot, s.HandsBusyUntil);
            s.Reason = "Бросаю пулемёт без доступного короба; возвращаю основное оружие";
            return true;
        }

        public static bool ThrowBack(BattleSimulation b, Soldier s)
        {
            if (!CombatTraining.ThrowBack(b, s) || (b.Rules.MechanicsMask & (1 << 2)) == 0 || !s.Available || !s.Deployed || s.Broken || s.Busy || TacticalOrders.ServiceActive(s) || s.Suppression > .4f || s.HandsBusyUntil > b.Time || s.ReloadUntil > b.Time)
                return false;
            foreach (var p in b.Projectiles)
                if (GrenadeSafety.Live(b, p) && !p.Rethrown && p.DetonateAt - b.Time > .85f && InteractionReach.Can(b, s.Position, p.Position, 1.1f))
                {
                    V2 best = p.Position;
                    float score = -1;
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = i * (float)Math.PI / 8;
                        V2 goal = p.Position + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 6;
                        if (!b.Map.Walkable(goal) || !b.Map.LineOfSight(p.Position, goal))
                            continue;
                        float nearest = 100;
                        foreach (var ally in b.Soldiers)
                            if (ally.Side == s.Side && ally.Alive && ally.Deployed)
                                nearest = Math.Min(nearest, V2.Distance(ally.Position, goal));
                        if (nearest > ArcedOrdnance.HandRadius + 1 && nearest > score)
                        {
                            score = nearest;
                            best = goal;
                        }
                    }

                    if (score < 0)
                        return false;
                    p.From = p.Position;
                    p.To = best;
                    p.Born = b.Time;
                    p.Duration = .55f;
                    p.LastProgress = 0;
                    p.Landed = false;
                    p.RoofProfileReady = false;
                    p.RoofTopHit = false;
                    p.Rethrown = true;
                    p.Height = 0;
                    FriendlyGrenadeSafety.Warn(b, s, p);
                    s.HandsBusyUntil = b.Time + .5f;
                    Count(b, s, 2, "Отбрасываю упавшую гранату; запал продолжает гореть");
                    return true;
                }

            return false;
        }

        public static bool FireAllowed(BattleSimulation b, Soldier s, Contact c) => true;
        public static bool Rotate(BattleSimulation b, Soldier s) => false;
        public static bool Scout(BattleSimulation b, Soldier s) => false;
        public static bool WoundedDonation(BattleSimulation b, Soldier s) => false;
        public static bool ShareGrenade(BattleSimulation b, Soldier s) => false;
        public static bool RearGuard(BattleSimulation b, Soldier s) => false;
        public static bool Backtrack(BattleSimulation b, Soldier s) => false;
        public static bool BlocksPlan(BattleSimulation b, Soldier s) => false;
        public static bool Move(BattleSimulation b, Soldier s, float dt)
        {
            s.Research.MoveKind = 0;
            return false;
        }

        public static void Tick(BattleSimulation b)
        {
            foreach (var unit in b.Soldiers)
                if (unit.Alive && unit.Deployed)
                    ReturnPrimary(b, unit);
            if (b.Rules.MechanicsMask == 0)
                return;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green && s.Alive && s.Deployed)
                {
                    var a = s.Research;
                    if (b.Time < a.Next)
                        continue;
                    a.Next = b.Time + 2;
                    if (InheritMG(b, s))
                        a.Next = b.Time + 8;
                }
        }
    }
}
