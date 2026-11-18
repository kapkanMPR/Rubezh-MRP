using System;
using System.Linq;

namespace Rubezh.Camp
{
    public sealed partial class CampModel
    {
        public long WeaponStock => Data.items.Where(i => i.type == "weapon").Sum(i => (long)Owned(i.id));
        public bool CanSellStock(string id)
        {
            var item = Data.Item(id);
            return item != null && Free(id) > 0 && Owned(id) > MaxSquadUse(id)
                && (item.type != "weapon" || WeaponStock > 5);
        }
        public int WarehouseCount(string id, bool hideIssued) => hideIssued ? Math.Max(0, Free(id)) : Owned(id);
        public bool Armed(string uid) => uid != null && S.gear.ContainsKey(uid) && Data.Item(S.gear[uid].weapon)?.type == "weapon";
        public static int GearLevel(string id) => string.IsNullOrEmpty(id) ? -1 : int.Parse(id.Substring(1)) - 1;
        public string ItemName(string id, string empty) => Data.Item(id)?.name ?? empty;

        void MigrateFiniteEquipment()
        {
            if (S.finiteEquipmentVersion >= 1) return;
            // Old basic items were unlimited and never stored in inventory.
            // Squad presets share stock, so preserve the largest use, not the sum.
            foreach (var id in new[] { "ak", "a1", "v1" })
            {
                int count = S.gear.Values.Sum(g => g.All().Count(x => x == id));
                if (S.squads != null)
                    foreach (var squad in S.squads)
                        if (squad.loadouts != null)
                            count = Math.Max(count, squad.loadouts.Values.Sum(g => g.All().Count(x => x == id)));
                S.inventory[id] = Math.Max(Owned(id), count);
            }
            if (WeaponStock < 5) S.inventory["ak"] = Owned("ak") + (int)(5 - WeaponStock);
            S.finiteEquipmentVersion = 1;
        }
    }
}
