using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public struct V2
    {
        public float X;
        public float Y;
        public float Length => (float)System.Math.Sqrt(X * X + Y * Y);

        public SilentOrders.Core.V2 Normalized
        {
            get
            {
                float length = Length;
                return (length > 0.0001f) ? (this / length) : default(SilentOrders.Core.V2);
            }
        }

        public V2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static SilentOrders.Core.V2 operator +(SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            return new SilentOrders.Core.V2(a.X + b.X, a.Y + b.Y);
        }

        public static SilentOrders.Core.V2 operator -(SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            return new SilentOrders.Core.V2(a.X - b.X, a.Y - b.Y);
        }

        public static SilentOrders.Core.V2 operator *(SilentOrders.Core.V2 a, float b)
        {
            return new SilentOrders.Core.V2(a.X * b, a.Y * b);
        }

        public static SilentOrders.Core.V2 operator /(SilentOrders.Core.V2 a, float b)
        {
            return new SilentOrders.Core.V2(a.X / b, a.Y / b);
        }

        public static float Distance(SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            return (a - b).Length;
        }

        public static float Dot(SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static float SegmentDistance(SilentOrders.Core.V2 p, SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            SilentOrders.Core.V2 v = b - a;
            float num = Dot(v, v);
            float b2 = ((num < 0.0001f) ? 0f : SilentOrders.Core.Util.Clamp(Dot(p - a, v) / num, 0f, 1f));
            return Distance(p, a + v * b2);
        }
    }
}
