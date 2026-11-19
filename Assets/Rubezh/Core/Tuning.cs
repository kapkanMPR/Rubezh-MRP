using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    [Serializable]
    public sealed class Tuning
    {
        public float Tick = 0.05f;
        public float SightRange = 14f;
        public float WeaponRange = 14.5f;
        public float MoveSpeed = 1.65f;
        public float PerceptionPeriod = 0.25f;
        public float DecisionPeriod = 0.9f;
        public float SquadPeriod = 1.4f;
        public float RadioDelay = 0.7f;
        public float ContactLifetime = 17f;
        public float CaptureSeconds = 12f;
        public float TimeLimit = 900f;
        public float BaseAccuracy = 0.245f;
        public float DamageMin = 18f;
        public float DamageMax = 30f;
    }
}
