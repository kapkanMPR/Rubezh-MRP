using System;

namespace SilentOrders.Core
{
    public sealed class HeavyWeaponDecision
    {
        public float Until;
        public bool Approved;
        public int Reviews, Reserved, Urgent;
    }

    public static class HeavyWeaponPolicy
    {
        public const float BalancedUseChance = .75f, ReviewSeconds = 8f;
        public static bool Urgent(BattleSimulation b, Soldier s, Contact c)
        {
            if (BreakthroughTactics.Assigned(b, s) || CounterSniper.AreaFire(b, s, c))
                return true;
            if (b.Time - s.LastIncoming < 2 && s.Suppression >= .4f)
                return true;
            foreach (var ally in b.Soldiers)
                if (ally != s && b.Team(s).Contains(ally) && ally.Available && b.Time - ally.LastIncoming < 2 && ally.Suppression >= .6f && b.Time - c.FireSeenAt < 3 && V2.Distance(ally.Position, c.Position) < b.WeaponRange(ally) + 2)
                    return true;
            int nearby = 0;
            foreach (var x in s.Contacts.Values)
                if (x.Visual && x.Confidence >= .7f && b.Time - x.SeenAt < 2.5f && V2.Distance(x.Position, c.Position) <= 2.7f)
                    nearby++;
            return nearby >= 3;
        }

        // Cache the decision per weapon, including rejection: trying another contact or another
        // pocket next frame must not turn a 75% decision into a virtually guaranteed shot.
        public static bool Allow(BattleSimulation b, Soldier s, Contact c, bool rocket)
        {
            if (b.Team(s).Style != CombatStyle.Balanced || BreakthroughTactics.Assigned(b, s))
                return true;
            var d = rocket ? s.HeavyRocket : s.HeavyUnderbarrel;
            if (Urgent(b, s, c))
            {
                if (!d.Approved)
                    d.Urgent++;
                d.Approved = true;
                d.Until = b.Time + ReviewSeconds;
                return true;
            }

            if (b.Time >= d.Until)
            {
                d.Reviews++;
                d.Approved = b.Random.Value() < BalancedUseChance;
                d.Until = b.Time + ReviewSeconds;
                if (!d.Approved)
                    d.Reserved++;
            }

            return d.Approved;
        }
    }
}

namespace SilentOrders.Core
{
    // An additional green AI objective. Reads known contacts and cover geometry only;
    // never peeks at an unseen enemy's live position or changes cover while scoring.
    public static class LauncherCoverDenial
    {
        public static int Shield(BattleMap map, V2 enemy, V2 fireFrom)
        {
            V2 axis = (fireFrom - enemy).Normalized;
            for (float d = .25f; d <= 1.55f; d += .16f)
            {
                V2 p = enemy + axis * d;
                var tile = map.At(p);
                if (tile == Tile.Wall)
                    return -1;
                if (tile == Tile.LowCover)
                {
                    int i = map.Index(p);
                    return map.VehicleFootprint != null && map.VehicleFootprint[i] != 0 ? -1 : i;
                }
            }

            return -1;
        }

        public static float BreakChance(BattleMap map, int cell, int launcher)
        {
            if (cell < 0 || cell >= map.Tiles.Length || map.Tiles[cell] != Tile.LowCover || map.VehicleFootprint != null && map.VehicleFootprint[cell] != 0)
                return 0;
            if (launcher == 0 || CoverDestruction.Strength(map, cell) != CoverStrength.Strong)
                return 1;
            map.CoverDamage.Ensure(map);
            return map.CoverDamage.RshgHits[cell] >= 1 ? 1 : .5f;
        }

        public static bool Safe(BattleSimulation b, Soldier s, Contact c, V2 aim, V2 impact)
        {
            float margin = 1.2f + c.Uncertainty + HandLauncher.ExtraScatter(s);
            if (b.Map.At(impact) == Tile.Wall || !b.Map.LineOfSight(impact, aim) || !SpecialWeapons.UsefulImpact(b, s, aim, impact) || b.FriendlyInLane(s, aim))
                return false;
            foreach (var ally in b.Soldiers)
                if (ally.Side == s.Side && ally.Alive && ally.Deployed)
                {
                    if (V2.Distance(ally.Position, impact) < SpecialWeapons.Radius(s) + margin)
                        return false;
                    int shield = Shield(b.Map, ally.Position, c.Position);
                    if (shield >= 0 && V2.Distance(b.Map.Center(shield), impact) <= SpecialWeapons.Radius(s) && b.Map.LineOfSight(impact, b.Map.Center(shield)))
                        return false;
                }

            return true;
        }

        public static float Find(BattleSimulation b, Soldier s, Contact c, out V2 aim, out int cell)
        {
            aim = c.Position;
            cell = -1;
            if (b.Historical01225)
                return 0;
            if (s.Side != Side.Green || (b.Team(s).Style != CombatStyle.Balanced && b.Team(s).Style != CombatStyle.Aggressive) || c.Inferred || c.Confidence < .7f || b.Time - c.SeenAt > 2.5f)
                return 0;
            float best = 0;
            foreach (var ally in b.Soldiers)
            {
                if (ally.Side != s.Side || !ally.Available || !ally.Deployed || ally.Ammo <= 0)
                    continue;
                bool flag = V2.Distance(c.Position, b.Map.Objective) <= 8;
                if (!flag && V2.Distance(ally.Position, c.Position) > b.WeaponRange(ally) + 3)
                    continue;
                int candidate = Shield(b.Map, c.Position, ally.Position);
                if (candidate < 0)
                    continue;
                bool pending = false;
                foreach (var shot in b.Projectiles)
                    if (shot.DenialCover == candidate && b.Soldiers[shot.ShooterId].Side == s.Side)
                    {
                        pending = true;
                        break;
                    }

                if (pending)
                    continue;
                V2 point = b.Map.Center(candidate);
                if (V2.Distance(s.Position, point) > SpecialWeapons.Range(b) || !b.Map.LineOfSight(ally.Position, c.Position))
                    continue;
                V2 impact = b.Map.LauncherImpact(s.Position, point);
                if (V2.Distance(impact, point) > SpecialWeapons.Radius(s) - .5f || !Safe(b, s, c, point, impact))
                    continue;
                // Valuable if it protects a currently useful firing position, or the objective.
                float value = 2f + (b.Time - c.FireSeenAt < 4 ? 2f : 0) + (flag ? 1f : 0);
                if (c.TargetPriority == 1 || c.TargetPriority == 2 || c.TargetPriority == 0)
                    value += 1f;
                float score = BreakChance(b.Map, candidate, s.LauncherKind) * value;
                if (score < 2.5f || score <= best)
                    continue;
                best = score;
                aim = point;
                cell = candidate;
            }

            return best;
        }
    }
}
