using System;

namespace SilentOrders.Core
{
    public static class WeaponBalance
    {
        public sealed class Spec
        {
            public float Range,
                Accuracy,
                Damage,
                FireSeconds,
                Suppression,
                Morale,
                DistanceFalloff;
            public int Magazine;
            public float DamageBonus,
                CriticalBonus,
                Spread = 1f,
                AccuracyAfterPenalties = 1f;
            public float AimSeconds = 1f;

            public Spec(
                float range,
                float accuracy,
                float damage,
                float interval,
                int magazine,
                float suppression,
                float morale,
                float falloff
            )
            {
                Range = range;
                Accuracy = accuracy;
                Damage = damage;
                FireSeconds = interval;
                Magazine = magazine;
                Suppression = suppression;
                Morale = morale;
                DistanceFalloff = falloff;
            }
        }

        public static readonly Spec[] Profiles =
        {
            new Spec(14.5f, 1f, 1f, 0.665f, 30, 0.22f, 0.1f, 22f), // АК-74
            new Spec(19.9f, 1f, 1.14f, 0.12f, 100, 0.28f, 0.125f, 25f) { AimSeconds = 1.5f }, // ПКМ
            new Spec(14.5f, 0.94f, 1f, 0.665f, 30, 0.22f, 0.1f, 22f), // АК-74 + подствольник
            new Spec(14.5f, 0.94f, 1f, 0.665f, 30, 0.22f, 0.1f, 22f), // АК-74 + второй подствольник
            new Spec(23f, 9.9f, 1.25f, 3.7f, 10, 0.22f, 0.1f, 22f) { AimSeconds = 1.5f }, // СВД
            new Spec(23f, 9.9f, 1.25f, 5.4f, 5, 0.22f, 0.1f, 22f) { AimSeconds = 1.5f }, // Винтовка 2
            new Spec(14.5f, 1.1f, 1f, 0.57826087f, 30, 0.22f, 0.1f, 22f) { Spread = .9f }, // М4
            new Spec(19.9f, 1.1f, 1.14f, 0.12f, 100, 0.28f, 0.125f, 25f)
            {
                AimSeconds = 1.5f,
                DamageBonus = -2f,
                Spread = .9f,
            }, // М249
            new Spec(14.5f, 1.034f, 1f, 0.57826087f, 30, 0.22f, 0.1f, 22f) { Spread = .9f }, // М4 + подствольник
            new Spec(14.5f, 1.034f, 1f, 0.57826087f, 30, 0.22f, 0.1f, 22f) { Spread = .9f }, // М4 + подствольник (вариант 2)
            new Spec(23f, 10.89f, 1.25f, 3.7f, 10, 0.22f, 0.1f, 22f)
            {
                AimSeconds = 1.75f,
                DamageBonus = 5f,
                CriticalBonus = .1f,
                AccuracyAfterPenalties = 1.1f,
            }, // М24 (снайперская винтовка)
            new Spec(23f, 10.89f, 1.25f, 5.4f, 5, 0.22f, 0.1f, 22f)
            {
                AimSeconds = 1.75f,
                DamageBonus = 5f,
                CriticalBonus = .1f,
                AccuracyAfterPenalties = 1.1f,
            }, // М24 (второй вариант)
        };
        public static readonly Spec[] ReferenceProfiles;

        static WeaponBalance()
        {
            ReferenceProfiles = (Spec[])Profiles.Clone();
            for (int i = 0; i < Profiles.Length; i++)
                Profiles[i] = WeaponProgression.BuildSpec(i, 0);
        }

        public static int Index(Soldier soldier)
        {
            int id = soldier.WeaponVariant;
            if ((soldier.Side == Side.Red || soldier.WesternGun) && !soldier.ActiveRecoveredWeapon && id >= 0 && id < 6)
                id += 6;
            return id >= 0 && id < Profiles.Length ? id : 0;
        }

        public static float CriticalChance(Soldier s, bool opening)
        {
            if (!s.GunSniper && !opening)
                return 0;
            return Math.Min(
                1f,
                (s.Side == Side.Green && s.Role == Role.Rifleman && s.GunSniper && !opening ? .1f : .2f)
                    + For(s).CriticalBonus
            );
        }

        public static float BulletDamage(Soldier s, float baseDamage) =>
            Math.Max(0, baseDamage * For(s).Damage + For(s).DamageBonus + (s.GunSniper ? 5 : 0));

        public static float ReloadSeconds(Soldier s) => (s.GunMachineGunner ? 6f : 2.7f) * s.ReloadFactor;

        public static Spec For(Soldier soldier)
        {
            int variant = Index(soldier);
            var upgrade = soldier.WeaponUpgrade;
            return upgrade != null && upgrade.Variant == variant ? upgrade.Spec : Profiles[variant];
        }
    }
}
