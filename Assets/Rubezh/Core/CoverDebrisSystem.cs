using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Includes partial chips as well as final destruction; preserve material before clearing the tile.
    public struct CoverBreakEvent
    {
        public string DebrisKey;
        public V2 Center;
        public bool HasCenter, Decor;
        public int RemnantKey, Footprint, SizePixels;
        public int Index;
        public byte Prop, LegacyStyle;
        public V2 Source, Direction;
        public int MaxPieces;
        public float MaxReach, FanDegrees;
        public bool Blast, Destroyed;
    }

    // Decorative only: no collision bodies, damage or navigation changes.
    public sealed class CoverDebrisPiece
    {
        public V2 From, To;
        public float Born, Duration, Peak, Angle, Spin;
        public int SpriteId, Width, HeightPixels;
        public bool Flying(float time)
        {
            return time < Born + Duration;
        }

        public float Progress(float time)
        {
            return Util.Clamp((time - Born) / Duration, 0, 1);
        }

        public V2 Position(float time)
        {
            float t = Progress(time);
            return From + (To - From) * (1 - (1 - t) * (1 - t));
        }

        public float Height(float time)
        {
            float t = Progress(time);
            return Peak * 4 * t * (1 - t);
        }

        public float Rotation(float time)
        {
            float t = Math.Min(Math.Max(0, time - Born), Duration);
            return Angle + Spin * t;
        }
    }

    public static class CoverDebrisSystem
    {
        public const int Limit = 512, ShellLimit = 320;
        public const int RifleCasingSprite = 37, AssaultCasingSprite = 38;
        // Large piles stay at the destroyed cell; only individual fragments fly.
        static readonly int[] BranchPieces =
        {
            13,
            15,
            16,
            17
        }, WoodPieces =
        {
            7,
            9,
            12
        }, MetalPieces =
        {
            19,
            20,
            22,
            24
        }, StonePieces =
        {
            2,
            5,
            33
        };
        public static int SpriteFor(byte prop, byte style, int variant)
        {
            if (prop == 25)
                return 39;
            if (prop >= 1 && prop <= 8)
                return BranchPieces[variant % BranchPieces.Length];
            if (prop == 13 || prop == 14)
                return WoodPieces[variant % WoodPieces.Length];
            if (prop == 15 || prop == 16 || prop == 0 && style == 2)
                return MetalPieces[variant % MetalPieces.Length];
            if (prop >= 17 && prop <= 24 || prop == 0 && style == 1)
                return 25 + variant % 6;
            return StonePieces[variant % StonePieces.Length];
        }

        static readonly int[] WoodPiles =
        {
            8,
            10,
            11
        }, StonePiles =
        {
            1,
            3,
            4,
            6,
            34,
            35,
            36
        };
        public static int RemnantSprite(byte prop, byte style, int variant)
        {
            if (prop == 25)
                return 40;
            if (prop >= 1 && prop <= 8)
                return variant % 2 == 0 ? 14 : 18;
            if (prop == 13 || prop == 14)
                return WoodPiles[variant % WoodPiles.Length];
            if (prop == 15 || prop == 16 || prop == 0 && style == 2)
                return variant % 2 == 0 ? 21 : 23;
            if (prop >= 17 && prop <= 24 || prop == 0 && style == 1)
                return variant % 2 == 0 ? 31 : 32;
            return StonePiles[variant % StonePiles.Length];
        }

        public static IEnumerable<CoverDebrisPiece> Visuals(BattleSimulation b)
        {
            foreach (var piece in b.CoverRemnants.Values)
                yield return piece;
            foreach (var piece in b.CoverDebris)
                yield return piece;
            foreach (var piece in b.ShellCasings)
                yield return piece;
        }

        static void LeaveRemnant(BattleSimulation b, CoverBreakEvent e, V2 center)
        {
            int variant = (int)(unchecked((uint)b.Seed * 73856093u ^ (uint)(e.HasCenter ? e.RemnantKey : e.Index) * 19349663u) & 0x7fffffffu);
            int sprite = DebrisCatalog.Pick(e.DebrisKey, true, variant);
            if (sprite == 0)
                sprite = RemnantSprite(e.Prop, e.LegacyStyle, variant);
            int width = e.Prop >= 5 && e.Prop <= 8 ? 24 : e.Prop == 13 || e.Prop == 14 ? 28 : 32;
            if (e.HasCenter)
                width = e.Decor ? (e.SizePixels > 0 ? e.SizePixels : 5) : Math.Min(40, 18 * Math.Max(1, e.Footprint));
            width = Math.Max(3, (int)Math.Round(width * .65f));
            b.CoverRemnants[e.HasCenter ? e.RemnantKey : e.Index] = new CoverDebrisPiece
            {
                From = center,
                To = center,
                Born = b.Time - 1,
                Duration = 1,
                SpriteId = sprite,
                Width = width,
                HeightPixels = DebrisDimensions.Height(sprite, width)
            };
        }

        static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xffffff) / 16777216f;
        }

        public static void EjectCasing(BattleSimulation b, Soldier shooter, V2 muzzle, V2 target)
        {
            if (b == null || shooter == null || !shooter.Alive)
                return;
            uint random = unchecked((uint)b.Seed * 73856093u ^ (uint)shooter.Id * 19349663u ^ (uint)shooter.Shots * 83492791u);
            if (random == 0)
                random = 1;
            bool rifleCase = shooter.GunMachineGunner || shooter.GunSniper;
            int sprite = rifleCase ? RifleCasingSprite : AssaultCasingSprite;
            V2 fire = (target - muzzle).Normalized;
            if (fire.Length < .01f)
                fire = shooter.Facing.Normalized;
            if (fire.Length < .01f)
                fire = new V2(1, 0);
            V2 right = new V2(fire.Y, -fire.X), left = new V2(-fire.Y, fire.X), lateral = shooter.GunMachineGunner ? left : right;
            float portForward = rifleCase ? .18f : .14f, portSide = rifleCase ? .15f : .12f;
            V2 origin = muzzle + fire * portForward + lateral * portSide;
            float baseAngle = (float)Math.Atan2(lateral.Y, lateral.X);
            float angle = baseAngle + (-.22f + Next(ref random) * .44f);
            V2 dir = new V2((float)Math.Cos(angle), (float)Math.Sin(angle));
            float distance = (1f + Next(ref random));
            V2 to = origin + dir * distance;
            to.X = Util.Clamp(to.X, .05f, b.Map.Columns - .05f);
            to.Y = Util.Clamp(to.Y, .05f, b.Map.Rows - .05f);
            int width = 2;
            int height = 3;
            float spin = (Next(ref random) < .5f ? -1f : 1f) * (7f + Next(ref random) * 8f);
            b.ShellCasings.Add(new CoverDebrisPiece { From = origin, To = to, Born = b.Time, Duration = (.24f + Next(ref random) * .18f), Peak = (.14f + Next(ref random) * .14f), Angle = (-.5f + Next(ref random)), Spin = spin, SpriteId = sprite, Width = width, HeightPixels = height });
            if (b.ShellCasings.Count > ShellLimit)
                b.ShellCasings.RemoveRange(0, b.ShellCasings.Count - ShellLimit);
            b.CoverDebrisRevision++;
        }

        public static void Consume(BattleSimulation b)
        {
            var pending = b.Map.BrokenCovers;
            for (int i = 0; i < pending.Count; i++)
            {
                var e = pending[i];
                if (e.Index < 0 || e.Index >= b.Map.Tiles.Length)
                    continue;
                var obj = StudioRules.At(b.Map, e.Index);
                V2 center = obj == null ? b.Map.Center(e.Index) : new V2(obj.X + obj.Asset.Width * .5f, obj.Y + obj.Asset.Height * .5f);
                if (e.HasCenter)
                    center = e.Center;
                if (e.Destroyed || e.MaxPieces == 0)
                    LeaveRemnant(b, e, center);
                if (e.Decor)
                    continue;
                uint rng = unchecked((uint)(b.Seed * 73856093 ^ e.Index * 19349663 ^ b.TickNumber * 83492791 ^ (i + 1) * 1640531513));
                if (rng == 0)
                    rng = 1;
                // Legacy vehicle clearing is treated as a small blast, without durability rolls.
                int max = e.MaxPieces > 0 ? e.MaxPieces : 3;
                float reach = e.MaxReach > 0 ? e.MaxReach : 4;
                bool blast = e.Blast || e.MaxPieces == 0;
                int count = max == 1 ? 1 : 1 + (int)(Next(ref rng) * max);
                float axis = e.Direction.Length > .001f ? (float)Math.Atan2(e.Direction.Y, e.Direction.X) : Next(ref rng) * (float)(Math.PI * 2);
                for (int j = 0; j < count; j++)
                {
                    float angle = blast ? axis + (Next(ref rng) - .5f) * (float)(Math.PI * (e.FanDegrees > 0 ? e.FanDegrees : 60) / 180) : Next(ref rng) * (float)(Math.PI * 2);
                    float distance = (.2f + .8f * Next(ref rng)) * reach;
                    V2 direction = new V2((float)Math.Cos(angle), (float)Math.Sin(angle));
                    // Clip distance along the original ray; do not reflect pieces back toward a blast at map edges.
                    if (direction.X > 0)
                        distance = Math.Min(distance, (b.Map.Columns - .05f - center.X) / direction.X);
                    if (direction.X < 0)
                        distance = Math.Min(distance, (.05f - center.X) / direction.X);
                    if (direction.Y > 0)
                        distance = Math.Min(distance, (b.Map.Rows - .05f - center.Y) / direction.Y);
                    if (direction.Y < 0)
                        distance = Math.Min(distance, (.05f - center.Y) / direction.Y);
                    // Decorative pieces do not fly through another high wall.
                    for (float d = .2f; d < distance; d += .15f)
                        if (b.Map.At(center + direction * d) == Tile.Wall)
                        {
                            distance = Math.Max(0, d - .16f);
                            break;
                        }

                    int variant = (int)(Next(ref rng) * 240);
                    int sprite = DebrisCatalog.Pick(e.DebrisKey, false, variant);
                    if (sprite == 0)
                        sprite = SpriteFor(e.Prop, e.LegacyStyle, variant);
                    int width = string.IsNullOrEmpty(e.DebrisKey) ? 12 + (int)(Next(ref rng) * 3) : 6 + (int)(Next(ref rng) * 3); // tourniquet is 9 x 4 logical pixels
                    width = Math.Max(3, (int)Math.Round(width * .70f));
                    int height = DebrisDimensions.Height(sprite, width);
                    b.CoverDebris.Add(new CoverDebrisPiece { From = center, To = center + direction * Math.Max(0, distance), Born = b.Time, Duration = blast ? .45f + Next(ref rng) * .35f : .2f + Next(ref rng) * .15f, Peak = blast ? .8f : .2f, Angle = (Next(ref rng) - .5f) * .8f, Spin = 0, SpriteId = sprite, Width = width, HeightPixels = height });
                }
            }

            if (pending.Count > 0)
            {
                if (b.CoverDebris.Count > Limit)
                    b.CoverDebris.RemoveRange(0, b.CoverDebris.Count - Limit);
                b.CoverDebrisRevision++;
            }

            pending.Clear();
        }
    }
}
