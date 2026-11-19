using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        float overviewHeight = 1500;

        void AtlasIcon(Rect rect, int id)
        {
            float side = Mathf.Min(rect.width, rect.height);
            var fit = new Rect(rect.x + (rect.width - side) * .5f, rect.y + (rect.height - side) * .5f, side, side);
            NativeUiArt.Draw(fit, "i" + id);
        }

        void WeaponPreview(Rect rect, int weapon)
        {
            AtlasIcon(
                rect,
                weapon == 4 ? 34
                    : weapon == 2 ? 36
                    : weapon == 1 ? 9
                    : 8
            );
        }

        void ExtraPreview(Rect rect, int extra)
        {
            if (extra == 0)
                return;
            AtlasIcon(
                rect,
                extra == 2 ? Rubezh.Graphics.BackpackSprite(true, 2)
                    : extra == 3 ? Rubezh.Graphics.BackpackSprite(false, 2)
                    : extra == 4 ? 6830
                    : 6829
            );
        }

        string KitInventory(Kit k, int slot)
        {
            var s = new Soldier
            {
                WeaponVariant = k.weapon,
                RigLevel = k.rig,
                Role =
                    k.weapon == 1 ? Role.MachineGunner
                    : k.weapon == 4 ? Role.Sniper
                    : Role.Rifleman,
            };
            var lines = new List<string>
            {
                "Патроны: " + s.MagazineSize + " в оружии + " + s.ReserveCapacity + " в запасе",
                "Жгуты: " + ((k.personalTourniquet ? (k.Class(slot) == Role.Medic ? 2 : 1) : 0) + k.bag[6]),
            };
            if (k.weapon == 2)
                lines.Add("Подствольник: 1 заряженный выстрел");
            if (k.extra == 1 || k.extra == 4)
                lines.Add(Extras[k.extra] + ": 1 готовый пуск");
            int[] types =  { k.grenade, k.grenade2, k.grenade3 },
                counts =  { k.count, k.count2, k.count3 };
            for (int j = 0; j < 3; j++)
                if (counts[j] > 0)
                    lines.Add(Grenades[types[j]] + ": " + counts[j]);
            for (int j = 0; j < k.bag.Length; j++)
                if (k.bag[j] > 0 && j != 6)
                    lines.Add("Рюкзак · " + UiCatalog.BagNames[j] + ": " + k.bag[j]);
            lines.Add(BodyArmor.Names[k.armor] + " · разгрузка " + (k.rig + 1));
            return string.Join("   •   ", lines);
        }

        void DrawSquadOverview(float x, float y, float width, float height)
        {
            float w = width - 30;
            detailScroll = TouchScroll(
                "squad-overview",
                new Rect(x, y, width, height),
                detailScroll,
                new Rect(0, 0, w, overviewHeight)
            );
            float yy = 0;
            Paragraph(0, ref yy, w, "Развед группа Кедра", equipHeading);
            yy += 12;
            for (int i = 0; i < 5; i++)
            {
                var k = Kits[i];
                Rule(0, yy, w);
                yy += 15;
                Person(new Rect(0, yy, 88, 110), k);
                Txt(105, yy, w - 370, 36, Names[i] + (WoundedSlot(i) ? " · РАНЕН" : ""), equipHeading);
                Txt(105, yy + 38, w - 370, 65, RoleName(k, i), equipNote);
                WeaponPreview(new Rect(w - 250, yy, 120, 85), k.weapon);
                ExtraPreview(new Rect(w - 120, yy, 100, 90), k.extra);
                yy += 115;
                Paragraph(10, ref yy, w - 20, WeaponName(k.weapon) + " · " + Extras[k.extra], equipStats);
                Paragraph(10, ref yy, w - 20, KitInventory(k, i), equipNote);
                yy += 20;
            }

            overviewHeight = yy + 20;
            EndTouchScroll();
        }
    }
}
