using System;

namespace SilentOrders.Core
{
    public static class HandGrenades
    {
        // Inventory ids: 0 smoke, 1 RGD, 2 UGL, 3 F1, 4 RGN, 5 RGO.
        public static bool IsHand(int type) => type == 1 || type >= 3 && type <= 5;
        public static bool Heavy(int type) => type == 3 || type == 5;
        public static bool Impact(int type) => type == 4 || type == 5;
        public static int ArcKind(int type) => IsHand(type) ? 1 : type;
        public static int Type(ArcProjectile p) => p.HandType >= 3 ? p.HandType : p.F1 ? (p.ImpactFuse ? 5 : 3) : p.ImpactFuse ? 4 : 1;
        public static string Name(int type) => type == 3 ? "Ф-1" : type == 4 ? "РГН" : type == 5 ? "РГО" : "РГД-5";
        public static float FragmentRange(ArcProjectile p) => Type(p) == 5 ? 13f : p.F1 ? 15f : 5f;
        public static int Count(Soldier s) => GrenadePockets.Count(s, 1) + GrenadePockets.Count(s, 3) + GrenadePockets.Count(s, 4) + GrenadePockets.Count(s, 5);
        public static int Price(int type, int count)
        {
            if (type < 0 || type > 5 || count < 0 || count > 3)
                throw new ArgumentOutOfRangeException();
            if (type == 0)
                return new[]
                {
                    0,
                    40,
                    70,
                    100
                }[count];
            if (type == 2)
                return new[]
                {
                    0,
                    60,
                    110,
                    150
                }[count];
            return new[]
            {
                0,
                50,
                80,
                110
            }[count] + count * (type == 3 ? 10 : type == 4 ? 15 : type == 5 ? 20 : 0);
        }
    }
}
