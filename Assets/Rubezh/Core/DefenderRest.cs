using System;
using System.Runtime.CompilerServices;

namespace SilentOrders.Core
{
    // State belongs to the initial authored defender group, not to all red units.
    public static class DefenderRest
    {
        sealed class State
        {
            public bool Alert;
            public float At = -1;
            public string Reason = "";
        }

        static readonly ConditionalWeakTable<DefenderGroupState, State> Data = new ConditionalWeakTable<DefenderGroupState, State>();
        public static bool Eligible(BattleSimulation b, Soldier s) => b.DefenderRestEnabled && !b.Historical01225 && s.Side == Side.Red && s.DefenderGroup != null && b.DefenderGroups.Count > 0 && object.ReferenceEquals(s.DefenderGroup, b.DefenderGroups[0]) && !s.DefenderGroup.Attack && !s.DefenderGroup.Pullback;
        public static void Confirm(BattleSimulation b, Soldier s, string reason)
        {
            if (!Eligible(b, s))
                return;
            var g = s.DefenderGroup;
            var st = Data.GetOrCreateValue(g);
            if (st.Alert)
                return;
            st.Alert = true;
            st.At = b.Time;
            st.Reason = reason;
            b.History.Event(b, "defenderAlert", s.Id, -1, s.Position, s.Position, 0, reason);
            b.Log(Side.Red, g.Name + ": тревога");
        }

        public static bool LowCoverBlocks(BattleSimulation b, Soldier s, V2 point)
        {
            float distance = V2.Distance(s.Position, point);
            return distance > 10 && LowCoverBlocks(b, s, point, distance, Multiplier(b, s));
        }

        internal static bool LowCoverBlocks(BattleSimulation b, Soldier s, V2 point, float distance, float rest)
        {
            if (distance <= 10 || rest >= 1)
                return false;
            // Exact segment/cell intersection avoids skipping thin diagonal corners.
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(s.Position.X, point.X))), maxX = Math.Min(b.Map.Columns - 1, (int)Math.Floor(Math.Max(s.Position.X, point.X)));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(s.Position.Y, point.Y))), maxY = Math.Min(b.Map.Rows - 1, (int)Math.Floor(Math.Max(s.Position.Y, point.Y)));
            V2 d = point - s.Position;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (b.Map.Tiles[y * b.Map.Columns + x] == Tile.LowCover)
                    {
                        float lo = 0, hi = 1;
                        if (Clip(s.Position.X, d.X, x, x + 1, ref lo, ref hi) && Clip(s.Position.Y, d.Y, y, y + 1, ref lo, ref hi) && hi > 0 && lo < 1)
                            return true;
                    }

            return false;
        }

        static bool Clip(float start, float delta, float min, float max, ref float lo, ref float hi)
        {
            if (Math.Abs(delta) < .00001f)
                return start >= min && start <= max;
            float a = (min - start) / delta, z = (max - start) / delta;
            if (a > z)
            {
                float t = a;
                a = z;
                z = t;
            }

            lo = Math.Max(lo, a);
            hi = Math.Min(hi, z);
            return lo <= hi;
        }

        public static void ObserveSmoke(BattleSimulation b, Soldier s)
        {
            if (!Eligible(b, s) || Multiplier(b, s) >= 1)
                return;
            foreach (var cloud in b.Smoke)
            {
                if (cloud.SourceSide < 0 || cloud.SourceSide == (int)s.Side || cloud.Born > b.Time || cloud.Until <= b.Time)
                    continue;
                float radius = cloud.Radius * (.35f + .65f * Util.Clamp((b.Time - cloud.Born) / 1.8f, 0, 1));
                for (int k = 0; k < 9; k++)
                {
                    float angle = k * (float)Math.PI / 4;
                    V2 point = k == 8 ? cloud.Position : cloud.Position + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius * .85f;
                    if (!DirectionalVision.InView(b, s, point) || !b.Map.LineOfSight(s.Position, point))
                        continue;
                    float transmission = 1;
                    foreach (var other in b.Smoke)
                        if (!object.ReferenceEquals(other, cloud))
                            transmission *= SmokeTactics.Transmission(s.Position, point, other.Position, other.Born, b.Time);
                    if (transmission < .25f)
                        continue;
                    Confirm(b, s, "visible_enemy_smoke");
                    return;
                }
            }
        }

        public static float Multiplier(BattleSimulation b, Soldier s)
        {
            if (!Eligible(b, s))
                return 1;
            var st = Data.GetOrCreateValue(s.DefenderGroup);
            if (st.Alert)
                return 1;
            foreach (var u in s.DefenderGroup.Members)
            {
                if (u.LastIncoming >= 0 || u.LastHurt >= 0 || u.LastBlast >= 0 || u.LastFire >= 0)
                {
                    Confirm(b, u, "fire_or_damage");
                    return 1;
                }

                foreach (var c in u.Contacts.Values)
                    if (c.Visual && !c.Inferred && c.Confidence >= .7f && b.Time - c.SeenAt <= 3)
                    {
                        Confirm(b, u, c.Reported ? "confirmed_report" : "visual_contact");
                        return 1;
                    }
            }

            return .7f;
        }
    }
}
