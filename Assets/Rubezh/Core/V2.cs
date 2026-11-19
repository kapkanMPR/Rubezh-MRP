using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public struct V2
    {
        public float X;
        public float Y;
        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        public V2 Normalized
        {
            get
            {
                float length = Length;
                return (length > 0.0001f) ? (this / length) : default(V2);
            }
        }

        public V2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static V2 operator +(V2 a, V2 b)
        {
            return new V2(a.X + b.X, a.Y + b.Y);
        }

        public static V2 operator -(V2 a, V2 b)
        {
            return new V2(a.X - b.X, a.Y - b.Y);
        }

        public static V2 operator *(V2 a, float b)
        {
            return new V2(a.X * b, a.Y * b);
        }

        public static V2 operator /(V2 a, float b)
        {
            return new V2(a.X / b, a.Y / b);
        }

        public static float Distance(V2 a, V2 b)
        {
            return (a - b).Length;
        }

        public static float Dot(V2 a, V2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static float SegmentDistance(V2 p, V2 a, V2 b)
        {
            V2 v = b - a;
            float lengthSquared = Dot(v, v);
            float b2 = ((lengthSquared < 0.0001f) ? 0f : Util.Clamp(Dot(p - a, v) / lengthSquared, 0f, 1f));
            return Distance(p, a + v * b2);
        }
    }
}
