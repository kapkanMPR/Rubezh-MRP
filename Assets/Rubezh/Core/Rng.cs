using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class Rng
    {
        private uint state;

        public Rng(int seed)
        {
            Reseed(seed);
        }

        public void Reseed(int seed)
        {
            state = (uint)(seed + 1);
            if (state == 0)
            {
                state = 1u;
            }
        }

        public uint Next()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        public float Value()
        {
            return (float)(Next() & 0xFFFFFFu) / 16777216f;
        }

        public float Range(float a, float b)
        {
            return a + (b - a) * Value();
        }

        public int Int(int n)
        {
            return (int)(Next() % (uint)n);
        }
    }
}
