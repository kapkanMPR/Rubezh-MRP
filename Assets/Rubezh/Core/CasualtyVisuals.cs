namespace SilentOrders.Core
{
    public static class CasualtyVisuals
    {
        public static float PoolScale(Soldier s) => !s.Alive ? 1.15f : 1f;

        public static bool Pool(Soldier s) => s.Deployed && (!s.Alive || s.Incapacitated || s.Bleeding);

        public static int MarkCount(int id) => 3 + (int)(((uint)id * 2654435761u + 17u) % 3);

        // Stable visual-only marks: never consume the battle random generator.
        static readonly V2[] Marks = { new V2(-6, -10), new V2(-1, -7), new V2(-5, -3), new V2(0, 1), new V2(-6, 3) };

        public static V2 Mark(int id, int index) => Marks[(int)(((uint)id + (uint)index) % 5)];
    }
}
