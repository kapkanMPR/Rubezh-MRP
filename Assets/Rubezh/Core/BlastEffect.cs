using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class BlastEffect
    {
        public V2 Position;
        public float Born,
            Height;
        public float Until;
        public float Radius;
        public int Kind;
        public bool Mortar,
            CraterRecorded;
    }
}
