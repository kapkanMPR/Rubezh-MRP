using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class SmokeCloud
    {
        public int SourceSide = -1;
        public SilentOrders.Core.V2 Position;
        public float Born;
        public float Until;
        public float Radius = 2.8f;
    }
}
