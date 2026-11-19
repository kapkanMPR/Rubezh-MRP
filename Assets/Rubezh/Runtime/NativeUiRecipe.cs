#if UNITY_EDITOR || RUBEZH_UI_BAKE
using System;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;

namespace Rubezh.UnityPort
{
    // Preparation only. Same CPU drawing routine as the former live portrait path.
    public static class NativeUiRecipe
    {
        public const int Size = 2048;
        public static readonly int[] Icons =
        {
            8,
            9,
            34,
            36,
            6829,
            6830,
            Rubezh.Graphics.BackpackSprite(true, 2),
            Rubezh.Graphics.BackpackSprite(false, 2),
        };

        public static NativeUiArt.Catalog Bake(Action<int, uint[]> save, Action<int> ensure)
        {
            var entries = new List<NativeUiArt.Entry>();
            var pixels = new uint[Size * Size];
            int page = 0,
                x = 2,
                y = 2,
                row = 0;
            Action<string, PixelCanvas> add = (key, c) =>
            {
                if (x + c.Width + 2 > Size)
                {
                    x = 2;
                    y += row + 4;
                    row = 0;
                }
                if (y + c.Height + 2 > Size)
                {
                    save(page++, pixels);
                    pixels = new uint[Size * Size];
                    x = y = 2;
                    row = 0;
                }
                for (int j = 0; j < c.Height; j++)
                    Array.Copy(c.Pixels, j * c.Width, pixels, (y + j) * Size + x, c.Width);
                entries.Add(
                    new NativeUiArt.Entry
                    {
                        key = key,
                        page = page,
                        x = x,
                        y = y,
                        w = c.Width,
                        h = c.Height,
                    }
                );
                x += c.Width + 4;
                row = Math.Max(row, c.Height);
            };
            for (int side = 0; side < 2; side++)
            for (int person = 0; person < (side == 0 ? Rubezh.GreenLook.ActiveHeads.Length : 12); person++)
            {
                bool enemy = side == 1;
                var look = enemy ? null : new Rubezh.GreenLook { headId = Rubezh.GreenLook.ActiveHeads[person] };
                var role = enemy ? (Role)(person / 2) : Role.Rifleman;
                int identity = person % 2;
                ensure(Rubezh.PawnAtlas.Sprite(look, enemy, role, identity, 0));
                if (!enemy)
                {
                    ensure(122);
                    ensure(124);
                }
                for (int weapon = 0; weapon < 6; weapon++)
                for (int west = 0; west < 2; west++)
                {
                    bool western = west == 1;
                    int gun =
                        weapon >= 4 ? (weapon == 5 ? 35 : 34)
                        : weapon >= 2 ? (weapon == 3 ? 37 : 36)
                        : (weapon == 1 ? 9 : 8) + (enemy || western ? 13 : 0);
                    ensure(gun);
                    var c = new PixelCanvas(128, 160);
                    RubezhApp.DrawPortrait(c, weapon, 1, 1, enemy, look, western, role, identity);
                    add(NativeUiArt.PortraitKey(weapon, enemy, look, western, role, identity), c);
                }
            }
            foreach (int id in Icons)
            {
                ensure(id);
                int w,
                    h;
                Rubezh.Graphics.SpriteData(id, 0, out w, out h);
                float scale = 180f / Math.Max(w, h);
                var c = new PixelCanvas(192, 192);
                Rubezh.Graphics.ReplaySprite(c, id, 96, 96, (int)(w * scale), (int)(h * scale), 0);
                add("i" + id, c);
            }
            save(page, pixels);
            return new NativeUiArt.Catalog
            {
                version = 1,
                size = Size,
                entries = entries.ToArray(),
            };
        }
    }
}
#endif
