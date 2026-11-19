using SilentOrders.Core;

namespace Rubezh
{
    public static class WeaponAppearance
    {
        public const int RareAk = 7974,
            EpicAk = 7975;

        public static int Sprite(int variant, WeaponUpgrade upgrade) =>
            variant == 0 && upgrade != null && upgrade.Variant == 0 && upgrade.Rarity >= 2
                ? (upgrade.Rarity >= 3 ? EpicAk : RareAk)
                : -1;

        public static string ItemPath(string id, string original, int rarity) =>
            id == "ak" && rarity >= 2 ? "weapons/ak-" + (rarity >= 3 ? "epic" : "rare") + ".png" : original;

        public static float Aspect(int id) => id == RareAk ? 0.25622171945701355f : 0.25f;

        public static float BarrelY(int id) => id == RareAk ? -0.10927152317880795f : -0.11751152073732718f;
    }
}
