using System;

namespace SilentOrders.Core
{
    public sealed class ExteriorSprite
    {
        public int PixelWidth,
            PixelHeight;
        public float Left,
            Top,
            Width,
            Height;
        public uint[] Pixels;
        public int NativePage = -1;
        public float U0,
            V0,
            U1 = 1,
            V1 = 1;
    }

    public static class ExteriorSprites
    {
        public static Func<string, ExteriorSprite> Load;

        public static string Family(string style)
        {
            return style == "hospital" ? "medical"
                : style == "industrial" || style == "depot" || style == "base" ? "warehouse"
                : style == "town" || style == "ruins" || style == "construction" ? "brick"
                : style == "village" ? "plaster"
                : "logs";
        }
    }

    // The rendered background is a finite rectangle. Clamp the viewport, not merely its centre.
    public static class ExteriorCamera
    {
        public static float Clamp(float offset, float background, float viewport, float centeredOrigin)
        {
            if (background <= viewport)
                return (viewport - background) * .5f - centeredOrigin;
            return Math.Max(viewport - background - centeredOrigin, Math.Min(-centeredOrigin, offset));
        }
    }
}
