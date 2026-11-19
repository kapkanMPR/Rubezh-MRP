using System;
using SilentOrders.Core;

namespace Rubezh
{
    // One finished head+body sprite per direction, packed offline. Equipment
    // levels affect stats only; weapon/hand animation remains independent.
    public static class PawnAtlas
    {
        public const int GreenStart = 7862,
            RedStart = 7926,
            End = 7974;

        public static bool Contains(int id) => id >= GreenStart && id < End;

        public static int Sprite(GreenLook look, bool enemy, Role role, int identity, int frame)
        {
            frame = Math.Max(0, Math.Min(3, frame));
            if (enemy)
            {
                int cls = Math.Max(0, Math.Min(5, (int)role));
                int variant = (int)((uint)identity & 1);
                return RedStart + (cls * 2 + variant) * 4 + frame;
            }
            int i = look == null ? -1 : Array.IndexOf(GreenLook.ActiveHeads, look.headId);
            if (i < 0)
                i = Array.IndexOf(GreenLook.ActiveHeads, 116);
            return GreenStart + i * 4 + frame;
        }

        public static int Sprite(Soldier soldier, int frame) =>
            Sprite(soldier.Look, soldier.Side == Side.Red, soldier.Role, soldier.Id, frame);
    }
}
