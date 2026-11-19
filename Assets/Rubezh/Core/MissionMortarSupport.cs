using System;

namespace SilentOrders.Core
{
    // One authored opening salvo. No inventory changes and no recurring support.
    public sealed class MissionMortarSupport
    {
        public const int ShellCount = 12;
        public const float ScatterRadius = 5;
        public const float BlastRadius = 4.3f * 1.5f + 1f;

        // Approximate world coordinates of the twelve marks on the user's mission-19 screenshot.
        public static readonly V2[] AimPoints =
        {
            new V2(69.5f, 10.5f),
            new V2(76.5f, 27),
            new V2(56.5f, 28),
            new V2(64, 30),
            new V2(60.5f, 45.5f),
            new V2(45, 44.5f),
            new V2(61, 10),
            new V2(72, 23),
            new V2(59, 23),
            new V2(51.5f, 23),
            new V2(40, 42.5f),
            new V2(46.5f, 47.5f),
        };
        readonly V2[] targets = new V2[ShellCount];
        readonly float started,
            flight;
        readonly int shooterId;
        int next;

        public MissionMortarSupport(BattleSimulation b, float flightSeconds)
        {
            started = b.Time;
            flight = Math.Max(.5f, flightSeconds);
            shooterId = -1;
            foreach (var s in b.Soldiers)
                if (s.Side == Side.Green)
                {
                    shooterId = s.Id;
                    break;
                }
            for (int i = 0; i < targets.Length; i++)
            {
                var center = AimPoints[i];
                // Uniform-area disk, not a square or a bias toward the centre.
                float angle = b.Random.Range(0, (float)(Math.PI * 2));
                float radius = ScatterRadius * (float)Math.Sqrt(b.Random.Range(0, 1f));
                var target = center + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                target.X = Util.Clamp(target.X, .5f, b.Map.Columns - .5f);
                target.Y = Util.Clamp(target.Y, .5f, b.Map.Rows - .5f);
                targets[i] = target;
            }
        }

        public void Update(BattleSimulation b)
        {
            if (shooterId < 0 || next >= ShellCount)
            {
                b.StoryMortars = null;
                return;
            }
            if (b.Time < started + next)
                return;
            var target = targets[next];
            b.Projectiles.Add(
                new ArcProjectile
                {
                    StoryMortarIndex = next,
                    ShooterId = shooterId,
                    Kind = 2,
                    From = target,
                    To = target,
                    Position = target,
                    Born = b.Time,
                    Duration = flight,
                    Height = SurfaceHeight(b, target) + 10,
                    RoofProfileReady = true,
                    HasMuzzleOffset = true,
                    MuzzleOffset = new V2(0, 0),
                }
            );
            next++;
        }

        static float SurfaceHeight(BattleSimulation b, V2 p)
        {
            if (!RoofRules.At(b.Map, p))
                return 0;
            if (b.Map.DynamicBuildings != null)
                foreach (var house in b.Map.DynamicBuildings)
                    if (house.Contains(p))
                        return house.RoofHeight(p);
            return RoofRules.Height;
        }

        public static bool UpdateProjectile(BattleSimulation b, ArcProjectile p)
        {
            float progress = Util.Clamp((b.Time - p.Born) / p.Duration, 0, 1);
            p.Height = SurfaceHeight(b, p.To) + 10 * (1 - progress);
            p.Position = p.To;
            p.LastProgress = progress;
            if (progress < 1)
                return false;
            const float radius = BlastRadius;
            bool roof = RoofRules.At(b.Map, p.To);
            b.Blasts.Add(
                new BlastEffect
                {
                    Position = p.To,
                    Height = p.Height,
                    Born = b.Time,
                    Until = b.Time + 1.1f,
                    Radius = radius,
                    Kind = 2,
                    Mortar = true,
                }
            );
            // The distinctive impact is emitted once, not on top of the normal grenade audio.
            b.AudioEvents.Emit(b.Time, 7, p.ShooterId);
            int debrisStart = b.Map.BrokenCovers.Count;
            if (roof)
                DynamicStructures.Blast(
                    b,
                    p.To,
                    radius,
                    2,
                    true,
                    new V2(0, 1),
                    impactHeight: p.Height,
                    destructionScale: 1.5f
                );
            else
                ArcedOrdnance.Resolve(
                    b,
                    b.Soldiers[p.ShooterId],
                    p.To,
                    radius,
                    50,
                    65,
                    -1,
                    0,
                    true,
                    origin: p.To,
                    impactHeight: 0,
                    destructionScale: 1.5f
                );
            for (int i = debrisStart; i < b.Map.BrokenCovers.Count; i++)
            {
                var e = b.Map.BrokenCovers[i];
                e.MaxPieces = Math.Max(e.MaxPieces, 6);
                e.MaxReach = Math.Max(e.MaxReach, 5);
                b.Map.BrokenCovers[i] = e;
            }
            return true;
        }
    }
}
