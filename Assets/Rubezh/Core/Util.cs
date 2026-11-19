using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Util
    {
        public static float Clamp(float v, float lo, float hi)
        {
            return Math.Max(lo, Math.Min(v, hi));
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
