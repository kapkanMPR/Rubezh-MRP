using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public sealed class ReactionContact
    {
        public float Seen = -100,
            Ready;
    }

    public sealed class ReactionState
    {
        public readonly Dictionary<int, ReactionContact> Contacts = new Dictionary<int, ReactionContact>();
        public int Target = -1,
            Switches;
        public float ReadyAt,
            LastShot = -100;
        public int AimTarget = -1,
            AimProfile = -1;
        public float AimReadyAt,
            AimReactionAt;
        public double WaitSeconds;
    }

    public static class ReactionTiming
    {
        public static float Delay(float angle) => .5f + Util.Clamp((angle - 30) / 150f, 0, 1);

        public static void Observe(BattleSimulation b, Soldier s, int id, V2 position, V2 look)
        {
            if (!b.ReactionTimingEnabled || b.Historical01225)
                return;
            var st = s.Awareness.Reaction;
            ReactionContact c;
            if (!st.Contacts.TryGetValue(id, out c))
            {
                c = new ReactionContact();
                st.Contacts.Add(id, c);
            }

            if (b.Time - c.Seen > Math.Max(.6f, b.Config.PerceptionPeriod * 2.1f))
                c.Ready = b.Time + Delay(DirectionalVision.Angle(look, position - s.Position));
            c.Seen = b.Time;
        }

        // A separate, additive acquisition delay, once per target / weapon / reacquisition.
        // The contact reaction timer remains unchanged and is shared with ordnance.
        public static bool WeaponReady(BattleSimulation b, Soldier s, Contact target)
        {
            bool reactionReady = Ready(b, s, target);
            var st = s.Awareness.Reaction;
            int profile = WeaponBalance.Index(s);
            float seconds = WeaponBalance.For(s).AimSeconds;
            if (st.AimTarget != target.EnemyId || st.AimProfile != profile)
            {
                st.AimTarget = target.EnemyId;
                st.AimProfile = profile;
                st.AimReactionAt = st.ReadyAt;
                st.AimReadyAt = Math.Max(b.Time, st.ReadyAt) + seconds;
            }
            else if (st.ReadyAt > st.AimReactionAt)
            {
                st.AimReactionAt = st.ReadyAt;
                st.AimReadyAt = Math.Max(st.AimReadyAt, st.ReadyAt + seconds);
            }
            if (!reactionReady || b.Time + .0001f < st.AimReadyAt)
            {
                s.FireStatus = "Навожу оружие";
                return false;
            }
            return true;
        }

        public static bool Ready(BattleSimulation b, Soldier s, Contact target)
        {
            if (!b.ReactionTimingEnabled || b.Historical01225)
                return true;
            var st = s.Awareness.Reaction;
            ReactionContact seen;
            st.Contacts.TryGetValue(target.EnemyId, out seen);
            if (st.Target != target.EnemyId)
            {
                bool visible = seen != null && b.Time - seen.Seen <= b.Config.PerceptionPeriod * 2.1f;
                float delay = visible
                    ? .5f
                    : Delay(DirectionalVision.Angle(s.Awareness.Look, target.Position - s.Position));
                // Detection already started acquisition; do not append a second delay to it.
                st.ReadyAt = st.Target < 0 && visible ? seen.Ready : Math.Max(b.Time + delay, visible ? seen.Ready : 0);
                st.Target = target.EnemyId;
                st.Switches++;
            }

            if (seen != null && seen.Ready > st.ReadyAt)
                st.ReadyAt = seen.Ready;
            if (b.Time + .0001f < st.ReadyAt)
            {
                st.WaitSeconds += b.Config.Tick;
                s.FireStatus = "Прицеливаюсь";
                return false;
            }

            return true;
        }
    }
}
