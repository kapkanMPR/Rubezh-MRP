using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class Contact
    {
        public int EnemyId;
        public int TargetPriority = 6;
        public float ObservedWeaponRange;
        public SilentOrders.Core.V2 Position;
        public V2 ObservedFacing;
        public V2 ObservedVelocity;
        public float SeenAt;
        public float Confidence;
        public float FireSeenAt = -100f;
        public bool Visual;
        public bool Reported;
        public bool Inferred;
        public float Uncertainty;
        public SilentOrders.Core.Contact Copy()
        {
            return (SilentOrders.Core.Contact)MemberwiseClone();
        }
    }
}
