using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        static void WeaponRarityCard(VisualElement card, Item item)
        {
            if (item == null)
                return;
            if (item.rarity == "Редкое" || LootCrates.Foreign(item.id))
            {
                LootRareCard(card);
                return;
            }
            if (item.type != "weapon" || item.rarity != "Обычное")
                return;
            Color edge = new Color(.31f, .60f, .34f);
            card.style.backgroundColor = new Color(.09f, .18f, .12f);
            card.style.borderTopColor =
                card.style.borderBottomColor =
                card.style.borderLeftColor =
                card.style.borderRightColor =
                    edge;
            card.style.borderTopWidth =
                card.style.borderBottomWidth =
                card.style.borderLeftWidth =
                card.style.borderRightWidth =
                    2;
        }
    }
}
