using System;

namespace SilentOrders.Core
{
    public static class GrenadePockets
    {
        public static int Limit(int rig) => rig < 0 ? 0 : rig == 0 ? 2 : rig == 1 ? 3 : 4;
        public static bool Valid(int rig, int type, int count, int type2, int count2, bool underbarrel, int type3 = 0, int count3 = 0)
        {
            int[] ts =
            {
                type,
                type2,
                type3
            }, ns =
            {
                count,
                count2,
                count3
            };
            int total = 0;
            if (rig < 0 || rig > 2)
                return false;
            for (int i = 0; i < 3; i++)
            {
                if (ts[i] < 0 || ts[i] > 5 || ns[i] < 0 || ns[i] > 3 || i > rig && ns[i] > 0 || ts[i] == 2 && !underbarrel && (i == 0 || ns[i] > 0))
                    return false;
                total += ns[i];
                for (int j = 0; j < i; j++)
                    if (ns[i] > 0 && ns[j] > 0 && ts[i] == ts[j])
                        return false;
            }

            return total <= Limit(rig);
        }

        public static void Equip(Soldier s, int type, int count, int type2 = 0, int count2 = 0, int type3 = 0, int count3 = 0)
        {
            s.LoadedUnderbarrel = OrdnanceExperiment.InitialLoaded(s);
            s.PocketMask = 1 << type;
            s.SmokeCharges = s.GrenadeCharges = s.OtherGrenadeCharges = s.ThirdGrenadeCharges = 0;
            s.GrenadeType = s.OtherGrenadeType = s.ThirdGrenadeType = 0;
            int[] ts =
            {
                type,
                type2,
                type3
            }, ns =
            {
                count,
                count2,
                count3
            };
            int slot = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i > 0 && ns[i] <= 0)
                    continue;
                s.PocketMask |= 1 << ts[i];
                if (ts[i] == 0)
                {
                    s.SmokeCharges = ns[i];
                    continue;
                }

                if (slot == 0)
                {
                    s.GrenadeType = ts[i];
                    s.GrenadeCharges = ns[i];
                }
                else if (slot == 1)
                {
                    s.OtherGrenadeType = ts[i];
                    s.OtherGrenadeCharges = ns[i];
                }
                else
                {
                    s.ThirdGrenadeType = ts[i];
                    s.ThirdGrenadeCharges = ns[i];
                }

                slot++;
            }
        }

        public static int Count(Soldier s, int type) => (type == 2 && s.Underbarrel ? s.LoadedUnderbarrel : 0) + (type == 0 ? s.SmokeCharges : (s.GrenadeType == type ? s.GrenadeCharges : 0) + (s.OtherGrenadeType == type ? s.OtherGrenadeCharges : 0) + (s.ThirdGrenadeType == type ? s.ThirdGrenadeCharges : 0));
        public static bool CanAdd(Soldier s, int type)
        {
            int mask = s.PocketMask != 0 ? s.PocketMask : 1 << s.GrenadeType;
            return (mask & (1 << type)) != 0 && (type != 2 || s.Underbarrel) && Count(s, type) - (type == 2 && s.Underbarrel ? s.LoadedUnderbarrel : 0) < 3 && s.SmokeCharges + s.GrenadeCharges + s.OtherGrenadeCharges + s.ThirdGrenadeCharges < Limit(s.RigLevel);
        }

        public static void Add(Soldier s, int type)
        {
            if (!CanAdd(s, type))
                return;
            if (type == 0)
                s.SmokeCharges++;
            else if (s.GrenadeType == type)
                s.GrenadeCharges++;
            else if (s.OtherGrenadeType == type)
                s.OtherGrenadeCharges++;
            else if (s.ThirdGrenadeType == type)
                s.ThirdGrenadeCharges++;
        }

        public static void Swap(Soldier s, bool third = false)
        {
            int t = s.GrenadeType, n = s.GrenadeCharges;
            if (third)
            {
                s.GrenadeType = s.ThirdGrenadeType;
                s.GrenadeCharges = s.ThirdGrenadeCharges;
                s.ThirdGrenadeType = t;
                s.ThirdGrenadeCharges = n;
            }
            else
            {
                s.GrenadeType = s.OtherGrenadeType;
                s.GrenadeCharges = s.OtherGrenadeCharges;
                s.OtherGrenadeType = t;
                s.OtherGrenadeCharges = n;
            }

            s.OrdnanceAimTarget = -1;
        }
    }
}
