using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class ArcProjectile
    {
        public int DenialCover = -1,
            DenialEnemy = -1;
        public bool RoofLowArc,
            RoofTopHit,
            RoofProfileReady;
        public bool F1,
            ImpactFuse;
        public int HandType;
        public System.Collections.Generic.List<int> WarningListeners;
        public bool Landed,
            Rethrown;
        public float DetonateAt,
            DistanceTravelled;
        public V2 MuzzleOffset;
        public bool HasMuzzleOffset;
        public int ShooterId;
        public int Kind;
        public int StoryMortarIndex = -1;
        public bool HasWallAimHeight;
        public float WallAimEndHeight;
        public V2 From;
        public V2 To;
        public V2 Position;
        public float Born;
        public float Duration;
        public float LastProgress;
        public float Height;
    }
}
