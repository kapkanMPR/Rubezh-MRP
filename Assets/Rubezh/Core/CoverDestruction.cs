using System;

namespace SilentOrders.Core
{
    public enum CoverStrength
    {
        Weak,
        Medium,
        Strong,
    }

    public enum CoverBlast
    {
        Grenade,
        Rshg,
        Rpg,
    }

    public sealed class CoverDamageState
    {
        Tile[] source;
        public int[] BulletHits,
            GrenadeHits,
            RshgHits;

        public void Ensure(BattleMap map)
        {
            if (ReferenceEquals(source, map.Tiles))
                return;
            source = map.Tiles;
            BulletHits = new int[source.Length];
            GrenadeHits = new int[source.Length];
            RshgHits = new int[source.Length];
        }
    }

    public static class CoverDestruction
    {
        public static CoverStrength Strength(BattleMap map, int index)
        {
            var studio = StudioRules.At(map, index);
            if (studio != null)
                return (CoverStrength)studio.Asset.Strength;
            byte prop = map.LevelProps[index];
            if (prop >= 5 && prop <= 8)
                return CoverStrength.Weak;
            if (prop == 13 || prop == 14)
                return CoverStrength.Medium;
            // Original procedural cover, concrete, sandbags and barrels are sturdy.
            return CoverStrength.Strong;
        }

        static bool Eligible(BattleMap map, int i)
        {
            return i >= 0
                && i < map.Tiles.Length
                && (map.Tiles[i] == Tile.LowCover || StudioRules.Obstacle(map, i))
                && !StudioRules.Permanent(map, i)
                && (map.VehicleFootprint == null || i >= map.VehicleFootprint.Length || map.VehicleFootprint[i] == 0);
        }

        static void Emit(
            BattleMap map,
            int i,
            V2 source,
            V2 direction,
            int count,
            float reach,
            bool blast,
            bool destroyed
        )
        {
            var obj = StudioRules.At(map, i);
            map.BrokenCovers.Add(
                new CoverBreakEvent
                {
                    DebrisKey = obj?.Asset.DebrisKey,
                    HasCenter = obj != null,
                    Center =
                        obj == null
                            ? map.Center(i)
                            : new V2(obj.X + obj.Asset.Width * .5f, obj.Y + obj.Asset.Height * .5f),
                    RemnantKey = i,
                    Footprint = obj == null ? 1 : Math.Max(obj.Asset.Width, obj.Asset.Height),
                    Index = i,
                    Prop = (byte)(StudioRules.At(map, i)?.Asset.DebrisProp ?? map.LevelProps[i]),
                    LegacyStyle = (byte)(StudioRules.At(map, i)?.Asset.DebrisStyle ?? map.LowCoverStyle[i]),
                    Source = source,
                    Direction = direction,
                    MaxPieces = count,
                    MaxReach = reach,
                    Blast = blast,
                    Destroyed = destroyed,
                }
            );
        }

        static void Destroy(BattleMap map, int i)
        {
            DecorRules.RemoveSupported(map, i);
            var changed = StudioRules.Remove(map, i);
            map.CoverHealth[i] = 0;
            map.Tiles[i] = Tile.Rubble;
            map.LevelProps[i] = map.LowCoverStyle[i] = 0;
            map.SceneryChanges.Record(map, changed);
        }

        public static int Bullet(BattleMap map, V2 impact, V2 direction)
        {
            if (!map.Inside((int)Math.Floor(impact.X), (int)Math.Floor(impact.Y)))
                return 0;
            int i = map.Index(impact);
            if (!Eligible(map, i))
                return 0;
            i = StudioRules.Root(map, i);
            CoverStrength strength = Strength(map, i);
            if (strength == CoverStrength.Strong)
                return 0;
            map.CoverDamage.Ensure(map);
            int hits = ++map.CoverDamage.BulletHits[i];
            int limit = StudioRules.At(map, i)?.Asset.BulletLimit ?? 0;
            if (limit <= 0)
                limit = strength == CoverStrength.Weak ? 15 : 30;
            int interval = Math.Max(1, limit / 3);
            bool destroyed = hits >= limit;
            // Integer hit counters define durability; health is a presentation of the state.
            StudioRules.Health(map, i, Math.Min(map.CoverHealth[i], Math.Max(0, 100f * (limit - hits) / limit)));
            if (hits % interval == 0)
            {
                Emit(map, i, impact, direction, 1, 1, false, destroyed);
                if (!destroyed)
                    map.SceneryChanges.Record(map, new[] { i });
            }

            if (!destroyed)
                return 0;
            Destroy(map, i);
            return 1;
        }

        public static int Blast(BattleSimulation b, V2 impact, float radius, CoverBlast kind, V2 incoming)
        {
            var map = b.Map;
            map.CoverDamage.Ensure(map);
            int destroyed = 0;
            var visited = new System.Collections.Generic.HashSet<int>();
            int x0 = Math.Max(0, (int)Math.Floor(impact.X - radius)),
                x1 = Math.Min(map.Columns - 1, (int)Math.Floor(impact.X + radius));
            int y0 = Math.Max(0, (int)Math.Floor(impact.Y - radius)),
                y1 = Math.Min(map.Rows - 1, (int)Math.Floor(impact.Y + radius));
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * map.Columns + x;
                if (!Eligible(map, i))
                    continue;
                int root = StudioRules.Root(map, i);
                if (visited.Contains(root))
                    continue;
                V2 center = map.Center(i);
                if (V2.Distance(center, impact) > radius || !map.LineOfSight(impact, center))
                    continue;
                i = root;
                visited.Add(root);
                CoverStrength strength = Strength(map, i);
                bool breaks = StudioRules.Obstacle(map, i) || kind == CoverBlast.Rpg || strength == CoverStrength.Weak;
                if (!breaks && strength == CoverStrength.Medium)
                {
                    if (kind == CoverBlast.Rshg)
                        breaks = true;
                    else
                        breaks = ++map.CoverDamage.GrenadeHits[i] >= 2 || b.Random.Value() < .5f;
                }

                if (!breaks && strength == CoverStrength.Strong && kind == CoverBlast.Rshg)
                    breaks = ++map.CoverDamage.RshgHits[i] >= 2 || b.Random.Value() < .5f;
                bool cosmetic = strength == CoverStrength.Strong && kind == CoverBlast.Grenade;
                V2 axis = center - impact;
                if (axis.Length < .001f)
                    axis = incoming;
                int pieces = breaks ? 3 : 2;
                if (
                    strength == CoverStrength.Medium
                    && kind == CoverBlast.Grenade
                    && map.CoverDamage.GrenadeHits[i] == 1
                )
                    pieces = 2;
                Emit(map, i, impact, axis, pieces, cosmetic ? 2 : 4, true, breaks);
                if (breaks)
                {
                    Destroy(map, i);
                    destroyed++;
                }
                else if (!cosmetic)
                {
                    StudioRules.Health(map, i, Math.Min(map.CoverHealth[i], 50));
                    map.SceneryChanges.Record(map, new[] { i });
                }

                if (!cosmetic)
                    b.Stats.CoverDamaged++;
            }

            b.Stats.CoverDestroyed += destroyed;
            return destroyed;
        }
    }
}
