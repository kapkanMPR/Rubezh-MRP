using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class Util
    {
        public static float Clamp(float v, float lo, float hi)
        {
            return System.Math.Max(lo, System.Math.Min(v, hi));
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
