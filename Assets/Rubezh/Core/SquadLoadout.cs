using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class SquadLoadout
    {
        public static bool ClassKit(Role cls, int w, int e)
        {
            return cls == Role.Commander || ((w != 2 && w != 3 || cls == Role.Rifleman) && (e != 2 || cls == Role.Medic) && (e != 3 || cls == Role.Rifleman) && (e != 4 || cls == Role.Grenadier));
        }

        public static bool Apply(SilentOrders.Core.BattleSimulation b, string encoded)
        {
            if (b.Time != 0f || encoded == null)
                return false;
            string[] rows = encoded.Split(';');
            if (rows.Length != 5)
                return false;
            int[, ] values = new int[5, 25];
            for (int i = 0; i < 5; i++)
            {
                string[] parts = rows[i].Split(',');
                if (parts.Length != 3 && parts.Length != 4 && parts.Length != 5 && parts.Length != 13 && parts.Length != 15 && parts.Length != 16 && parts.Length != 17 && parts.Length != 19 && parts.Length != 21 && parts.Length != 22 && parts.Length != 24 && parts.Length != 25)
                    return false;
                values[i, 3] = 2;
                for (int j = 0; j < parts.Length; j++)
                    if (!int.TryParse(parts[j], out values[i, j]))
                        return false;
                if (parts.Length < 25)
                    values[i, 24] = 1;
                if (values[i, 24] < 0 || values[i, 24] > 1 || values[i, 15] < 0 || values[i, 15] > 2)
                    return false;
                int weapon = values[i, 0], extra = values[i, 1], grenade = values[i, 4];
                int code = values[i, 22], rank = values[i, 23];
                if (code < 0 || code > 6 || rank < 0 || rank > 3)
                    return false;
                if (code != 0)
                {
                    Role cls = (Role)(code - 1);
                    if ((i == 0) != (cls == Role.Commander) || !ClassKit(cls, weapon, extra))
                        return false;
                }

                if (weapon < 0 || weapon > 5 || extra < 0 || extra > 4 || values[i, 2] < 0 || values[i, 2] > 3 || values[i, 3] < 0 || values[i, 3] > 2 || grenade < 0 || grenade > 5 || (grenade == 2 && weapon != 2 && weapon != 3) || (code == 0 && (weapon == 1 || weapon >= 4) && extra != 0))
                    return false;
                if (parts.Length == 3 && i == 0)
                    values[i, 2] = 3;
                if (parts.Length < 15)
                    values[i, 2] = Math.Min(values[i, 2], GrenadePockets.Limit(values[i, 3]));
                if (!GrenadePockets.Valid(values[i, 3], grenade, values[i, 2], values[i, 13], values[i, 14], (weapon == 2 || weapon == 3), values[i, 19], values[i, 20]))
                    return false;
                if (parts.Length < 13)
                {
                    if (extra == 3)
                    {
                        values[i, 5] = 6;
                        values[i, 6] = 1;
                        values[i, 7] = 2;
                    }
                    else if (extra == 2)
                    {
                        values[i, 11] = 2;
                        values[i, 12] = 1;
                    }
                }

                int[] prices =
                {
                    5,
                    50,
                    35,
                    20,
                    25,
                    30,
                    40,
                    60
                }, caps =
                {
                    30,
                    3,
                    4,
                    7,
                    6,
                    5,
                    3,
                    2
                };
                int cost = 0;
                for (int j = 0; j < 8; j++)
                {
                    int n = values[i, j + 5];
                    if (n < 0 || n > caps[j] || n > 0 && !((extra == 3 && j < 6) || (extra == 2 && j >= 6)))
                        return false;
                    cost += n * prices[j];
                }

                if (values[i, 16] < 0 || values[i, 16] > 6 || values[i, 16] > 0 && extra != 3)
                    return false;
                cost += values[i, 16] * 30;
                for (int j = 17; j < 19; j++)
                {
                    if (values[i, j] < 0 || values[i, j] > 6 || values[i, j] > 0 && extra != 3)
                        return false;
                    cost += values[i, j] * (j == 17 ? 35 : 40);
                }

                if (values[i, 21] < 0 || values[i, 21] > 4 || values[i, 21] > 0 && extra != 3)
                    return false;
                cost += values[i, 21] * 50;
                if (cost > CampaignProgression.BagCapacity(extra))
                    return false;
            }

            for (int i = 0; i < 5; i++)
            {
                Soldier u = b.Soldiers[i];
                int w = values[i, 0], e = values[i, 1];
                u.Role = w >= 4 ? Role.Sniper : w == 1 ? Role.MachineGunner : e == 2 ? Role.Medic : (e == 1 || e == 4) ? Role.Grenadier : (i == b.Green.LeaderId && e == 0) ? Role.Commander : Role.Rifleman;
                if (values[i, 22] > 0)
                {
                    u.Role = (Role)(values[i, 22] - 1);
                    u.ClassRank = values[i, 23];
                    u.Mechanics.MedicalBag = e == 2;
                    u.ClassKitAssigned = true;
                    u.GunRoleOverride = w >= 4 ? (int)Role.Sniper : w == 1 ? (int)Role.MachineGunner : (int)Role.Rifleman;
                }

                u.ArmorLevel = values[i, 15];
                u.WeaponVariant = w;
                u.WeaponUpgrade = u.BackWeaponUpgrade = null;
                u.LauncherKind = e == 4 ? 1 : 0;
                u.ReserveLauncherKind = -1;
                u.GrenadeType = values[i, 4];
                u.SupplyPack = e == 3;
                u.RigLevel = values[i, 3];
                u.Ammo = u.MagazineSize;
                u.Magazines.Reset(u, u.ReserveCapacity);
                u.Tourniquets = values[i, 24] * (u.Role == Role.Medic ? 2 : 1) + values[i, 11];
                u.Medkits = values[i, 12];
                u.Rockets = (e == 1 || e == 4) ? 1 : 0;
                GrenadePockets.Equip(u, values[i, 4], values[i, 2], values[i, 13], values[i, 14], values[i, 19], values[i, 20]);
                u.PackRshg = values[i, 21];
                u.PackRGN = values[i, 17];
                u.PackRGO = values[i, 18];
                u.PackF1 = values[i, 16];
                u.PackMagazines = values[i, 5];
                u.PackBoxes = values[i, 6];
                u.PackRockets = values[i, 7];
                u.PackSmokes = values[i, 8];
                u.PackGrenades = values[i, 9];
                u.PackUnderbarrel = values[i, 10];
                u.TreatmentKind = 0;
                u.TreatmentDonorId = -1;
            }

            return true;
        }
    }
}
