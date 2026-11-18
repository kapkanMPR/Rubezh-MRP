using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class ShotTrace
    {
        public bool Fragment;
        public bool Ballistic, Ricochet;
        public V2 Tail;
        public float TailHeight;
        public float FromHeight = 1.55f, ToHeight = 1f, Born;
        public V2 MuzzleOffset, NativeMuzzleOffset;
        public int ShooterId = -1;
        public bool Rocket;
        public SilentOrders.Core.V2 From;
        public SilentOrders.Core.V2 To;
        public SilentOrders.Core.Side Side;
        public float Until;
        public bool Hit;
    }
}
