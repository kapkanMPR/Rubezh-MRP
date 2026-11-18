using System.Collections.Generic;

namespace SilentOrders.Core
{
    public struct BattleSoundEvent
    {
        public int Category, Shooter;
        public float At;
    }

    public sealed class BattleAudioEvents
    {
        // Headless simulations need no audio queue; the Unity consumer enables it on battle creation.
        public bool Enabled;
        public readonly Queue<BattleSoundEvent> Pending = new Queue<BattleSoundEvent>();
        public void Emit(float at, int category, int shooter = -1)
        {
            if (!Enabled)
                return;
            if (Pending.Count >= 1024)
                Pending.Dequeue();
            Pending.Enqueue(new BattleSoundEvent { At = at, Category = category, Shooter = shooter });
        }
    }
}
