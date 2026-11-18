using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public enum BulletStop
    {
        None,
        Soldier,
        LowCover,
        Wall,
        Vehicle,
        Ground,
        Boundary,
        Range
    }

    public sealed class Bullet
    {
        public int SurpriseTarget = -1;
        public float SurpriseMultiplier = 1;
        public int ShooterId, IntendedId = -1;
        public Side Side;
        public V2 Position, Direction;
        public float MaxDistance, Height, Slope, Speed, Remaining, Damage, Suppression, Morale, LastAt, Travelled;
        public V2 EndPoint;
        public float EndHeight, Quality;
        public int EndCell;
        public BulletStop EndKind;
        public bool OpeningCritical;
        public bool Fragment;
        public bool Ricocheted, Critical, AreaFire;
        public ShotTrace Trace;
        public int ShotId;
        public bool SniperShot, Somali;
        public V2 SourcePosition;
        internal readonly HashSet<StudioPlaced> DecorHits = new HashSet<StudioPlaced>();
        internal readonly HashSet<int> PassedCoverRoots = new HashSet<int>();
        internal byte[] CrouchRoll = new byte[0];
        internal float[] Stress = new float[0];
    }

    public struct BulletCollision
    {
        public BulletStop Kind;
        public float Distance;
        public V2 Point, Normal;
        public float Height;
        public int SoldierId, Cell;
        public bool Corner, VerticalFace;
    }

    public struct BulletSpark
    {
        public V2 Origin, Direction;
        public float Height, Born;
        public bool Dust;
    }

    public sealed class BallisticState
    {
        public readonly List<Bullet> Active = new List<Bullet>(32);
        public readonly List<BulletSpark> Sparks = new List<BulletSpark>(64);
        public int CrossingPauses, CrossingWaits, CrossingDetours;
        internal readonly Stack<Bullet> Pool = new Stack<Bullet>(64);
        internal V2[] Previous = new V2[0];
        internal bool[] WasDeployed = new bool[0];
        internal float StepAt = -1;
        public int AreaLaunched, PartialPasses;
        public int Launched, Hits, FriendlyHits, StrayHits, Ricochets, WallStops, VehicleStops, LowCoverStops, GroundStops;
    }

    // Finite aim-cell projectiles: swept body cylinders along the whole flight;
    // only the selected destination cell can stop a bullet with scenery. Muzzle/LOS
    // legality is checked separately before firing. Gameplay is independent of sprites,
    // not renderer sprites or per-bullet GameObjects. All distances are game cells.
    public static class Ballistics
    {
        public const float LowHeight = 1f, WallHeight = 2.6f, MuzzleHeight = 1.55f, BodyRadius = .26f;
        public const float RicochetRange = 4f, TraceSeconds = .075f, TracerLength = .65f;
        const float Epsilon = .0001f;
        public static float BodyHeight(Soldier s) => s.Incapacitated || s.Broken ? .45f : s.Crouched ? 1.25f : 1.75f;
        public static void BeginStep(BattleSimulation b)
        {
            var state = b.Ballistics;
            if (state.Previous.Length < b.Soldiers.Count)
            {
                state.Previous = new V2[b.Soldiers.Count];
                state.WasDeployed = new bool[b.Soldiers.Count];
            }

            for (int i = 0; i < b.Soldiers.Count; i++)
            {
                state.Previous[i] = b.Soldiers[i].Position;
                state.WasDeployed[i] = b.Soldiers[i].Deployed;
            }

            state.StepAt = b.Time;
        }

        public static float DistanceBonus(float distance, float range) => 1f + .25f * (1f - Util.Clamp(distance / Math.Max(1, range), 0, 1));
        // Neutral preview uses distanceBonus=1: no range bonus, penalties or fighter effects.
        public static float AccuracyBeforePenalties(Tuning config, Soldier s, float distanceBonus = 1f)
        {
            var w = WeaponBalance.For(s);
            float term = config.BaseAccuracy * s.AccuracyFactor * distanceBonus;
            float quality = Util.Clamp(term * (w.Accuracy / w.AccuracyAfterPenalties), 0, .98f);
            if (s.Side == Side.Green && s.Role == Role.Rifleman && s.GunSniper)
                quality = (quality + Util.Clamp(term, 0, .98f)) * .5f;
            return quality;
        }
        public static float NeutralAccuracy(Tuning config, Soldier s) => Util.Clamp(AccuracyBeforePenalties(config, s) * WeaponBalance.For(s).AccuracyAfterPenalties, 0, .98f);
        public static float Accuracy(BattleSimulation b, Soldier s, V2 point, bool moving, bool crouched, float virtualCover = 0, bool posture = false, bool corners = true, bool ignoreCover = false)
        {
            var w = WeaponBalance.For(s);
            // Cap the weapon/training term before debuffs so sniper multipliers cannot erase them.
            float quality = AccuracyBeforePenalties(b.Config, s, DistanceBonus(V2.Distance(s.Position, point), w.Range));
            quality *= (ignoreCover ? 1 : (1 - LayeredCover.Protection(b.Map, point, s.Position, virtualCover, posture, corners) * .84f)) * (1 - s.Suppression * .65f) * ConcussionEffects.AccuracyMultiplier(ConcussionEffects.Current(b, s)) * (s.Health < 45 ? .78f : 1) * MotionCombat.Accuracy(b, s) * MotionCombat.SideAccuracy(b, s, point) * (.75f + .25f * Util.Clamp(s.Morale, 0, 1)) * (moving ? .7f : 1) * b.SmokeTransmission(s.Position, point);
            if (s.SuppressiveFire)
                quality *= .65f;
            if (V2.Distance(s.Position, point) > w.Range)
                quality *= .5f;
            return Util.Clamp(quality * w.AccuracyAfterPenalties, 0, .98f);
        }

        public static float AimQuality(BattleSimulation b, Soldier s, Contact contact)
        {
            bool visual = contact.Visual && !contact.Inferred && s.Visible.Contains(contact.EnemyId) && b.Time - contact.SeenAt <= b.Config.PerceptionPeriod * 2;
            Soldier target = visual && contact.EnemyId >= 0 && contact.EnemyId < b.Soldiers.Count ? b.Soldiers[contact.EnemyId] : null;
            // Hidden current position, posture and movement never enter an aiming solution.
            bool opening = visual && ReconOpening.BurstEligible(b, s, contact);
            if (opening && b.OpeningFixedAccuracyEnabled)
                return V2.Distance(s.Position, contact.Position) > WeaponBalance.For(s).Range ? .49f : .98f;
            float result = Accuracy(b, s, contact.Position, target != null && target.Moving, target != null && target.Crouched, target == null ? 0 : ShoulderPair.Protection(b, target, s.Position), target != null && CombatCrouch.Protected(target), target != null && CombatTraining.Corners(b, target), opening && b.OpeningIgnoreCoverEnabled) * (visual ? 1 : .16f);
            return opening && b.OpeningDoubleAccuracyEnabled ? Util.Clamp(result * 2, 0, .98f) : result;
        }

        public static V2 ObservedVelocity(BattleSimulation b, Contact previous, V2 point)
        {
            if (previous == null || !previous.Visual || previous.Reported)
                return new V2();
            float age = b.Time - previous.SeenAt;
            if (age < .02f || age > b.Config.PerceptionPeriod * 2)
                return new V2();
            V2 velocity = (point - previous.Position) / age;
            return velocity.Normalized * Math.Min(velocity.Length, b.Config.MoveSpeed * 1.6f);
        }

        static V2 AimCenter(BattleSimulation b, Soldier shooter, Contact contact, float speed)
        {
            if (contact.Inferred && contact.Uncertainty > 0)
                return ClampPoint(b.Map, CounterSniper.Scatter(b, contact.Position, contact.Uncertainty));
            bool visual = contact.Visual && !contact.Inferred && shooter.Visible.Contains(contact.EnemyId) && b.Time - contact.SeenAt <= b.Config.PerceptionPeriod * 2;
            if (!visual || contact.EnemyId < 0 || contact.EnemyId >= b.Soldiers.Count || !b.Soldiers[contact.EnemyId].Moving)
                return contact.Position;
            float horizon = Math.Max(0, b.Time - contact.SeenAt) + V2.Distance(shooter.Position, contact.Position) / speed;
            V2 lead = contact.ObservedVelocity * horizon;
            lead = lead.Normalized * Math.Min(1f, lead.Length);
            return ClampPoint(b.Map, contact.Position + lead);
        }

        public static V2 AimPoint(BattleSimulation b, V2 muzzle, V2 aim, float quality, out bool onTarget, float spread = 1f)
        {
            onTarget = b.Random.Value() < quality;
            if (onTarget)
            {
                float angle = b.Random.Value() * (float)Math.PI * 2, radius = b.Random.Range(0, .08f) * spread;
                return ClampPoint(b.Map, aim + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius);
            }

            // Weighted reservoir over the eight neighbouring cells. A miss cannot cross
            // the stationary intended body, otherwise the probability would be counted twice.
            V2 chosen = aim;
            float total = 0;
            int cx = (int)Math.Floor(aim.X), cy = (int)Math.Floor(aim.Y);
            for (int y = cy - 1; y <= cy + 1; y++)
                for (int x = cx - 1; x <= cx + 1; x++)
                {
                    if ((x == cx && y == cy) || !b.Map.Inside(x, y))
                        continue;
                    V2 candidate = new V2(x + .5f + b.Random.Range(-.2f, .2f), y + .5f + b.Random.Range(-.2f, .2f));
                    candidate = ClampPoint(b.Map, aim + (candidate - aim) * spread);
                    if (V2.Distance(candidate, muzzle) < .7f || V2.SegmentDistance(aim, muzzle, candidate) < BodyRadius + .1f)
                        continue;
                    var tile = b.Map.At(candidate);
                    float weight = tile == Tile.LowCover || tile == Tile.Wall ? 2 : 1;
                    total += weight;
                    if (b.Random.Value() * total < weight)
                        chosen = candidate;
                }

            if (total > 0)
                return chosen;
            // Tiny/boundary arenas: select the safer of the two lateral points, never an infinite ray.
            V2 d = (aim - muzzle).Normalized, side = new V2(-d.Y, d.X), a = ClampPoint(b.Map, aim + side * (.65f * spread)), c = ClampPoint(b.Map, aim - side * (.65f * spread));
            return V2.SegmentDistance(aim, muzzle, a) > V2.SegmentDistance(aim, muzzle, c) ? a : c;
        }

        static V2 ClampPoint(BattleMap map, V2 p) => new V2(Util.Clamp(p.X, .02f, map.Columns - .02f), Util.Clamp(p.Y, .02f, map.Rows - .02f));
        public static bool TryMuzzle(BattleSimulation b, Soldier s, V2 aim, out V2 muzzle)
        {
            V2 dir = (aim - s.Position).Normalized;
            muzzle = s.Position + dir * .32f;
            if (dir.Length < .1f || !Inside(b.Map, muzzle))
                return false;
            var hit = Geometry(b.Map, s.Position, MuzzleHeight, dir, 0, .32f);
            return hit.Kind == BulletStop.None;
        }

        public static void TraceObstacles(BattleSimulation b, V2 from, float z, V2 to, float endZ, HashSet<StudioPlaced> seen, V2 direction)
        {
            var map = b.Map;
            if (map.Studio == null)
                return;
            float distance = V2.Distance(from, to);
            int steps = Math.Max(1, (int)Math.Ceiling(distance / .15f));
            for (int n = 0; n <= steps; n++)
            {
                float t = (float)n / steps;
                V2 p = from + (to - from) * t;
                if (!map.Inside((int)Math.Floor(p.X), (int)Math.Floor(p.Y)))
                    continue;
                var o = StudioRules.At(map, map.Index(p));
                if (o == null || o.Destroyed || !o.Asset.NoCover || seen.Contains(o))
                    continue;
                float h = z + (endZ - z) * t;
                if (h < 0 || h > 1.3f)
                    continue;
                seen.Add(o);
                b.Stats.CoverDestroyed += CoverDestruction.Bullet(map, p, direction);
            }
        }

        public static Bullet Launch(BattleSimulation b, Soldier shooter, Contact contact, V2 muzzle)
        {
            float speed = shooter.GunSniper ? 90 : shooter.GunMachineGunner ? 80 : 70;
            float quality = AimQuality(b, shooter, contact);
            bool somali = shooter.SomaliFiring;
            float suppressionMultiplier = 1f, moraleMultiplier = 1f, damageMultiplier = 1f;
            if (somali)
                SomaliFire.Modifiers(ref quality, ref damageMultiplier, ref suppressionMultiplier, ref moraleMultiplier);
            bool onTarget;
            V2 aim = AimPoint(b, muzzle, AimCenter(b, shooter, contact, speed), quality, out onTarget, WeaponBalance.For(shooter).Spread);
            if (somali)
            {
                float scatter = 1.5f + (10 - shooter.SomaliRemaining) * .12f;
                aim = ClampPoint(b.Map, aim + new V2(b.Random.Range(-scatter, scatter), b.Random.Range(-scatter, scatter)));
                onTarget = false;
            }
            else if (AreaSuppression.Active(b, shooter))
            {
                aim = contact.Position;
                onTarget = true;
            }

            float distance = Math.Max(.05f, V2.Distance(muzzle, aim));
            V2 direction = (aim - muzzle).Normalized;
            // A precise shot aims at observed body height; misses finish on local ground
            // or the chosen cover. An evaded precise shot expires in that same cell, never homes.
            float aimHeight = onTarget ? 1.05f : 0;
            if (onTarget && contact.Visual && !contact.Inferred && shooter.Visible.Contains(contact.EnemyId) && b.Time - contact.SeenAt <= b.Config.PerceptionPeriod * 2 && contact.EnemyId >= 0 && contact.EnemyId < b.Soldiers.Count)
            {
                var target = b.Soldiers[contact.EnemyId];
                aimHeight = target.Incapacitated || target.Broken ? .28f : target.Crouched ? .88f : 1.05f;
            }

            float launchHeight = somali ? SomaliFire.MuzzleHeight(shooter) : MuzzleHeight;
            float slope = (aimHeight - launchHeight) / distance;
            var w = WeaponBalance.For(shooter);
            bool openingEligible = ReconOpening.CriticalEligible(b, shooter, contact);
            bool critical = (shooter.GunSniper || openingEligible) && b.Random.Value() < WeaponBalance.CriticalChance(shooter, openingEligible);
            if (openingEligible)
                ReconOpening.RecordCritical(b, critical);
            float damage = WeaponBalance.BulletDamage(shooter, b.Random.Range(b.Config.DamageMin, b.Config.DamageMax)) * damageMultiplier * (critical ? 1.5f : 1f);
            var p = Spawn(b, shooter, muzzle, launchHeight, direction, slope, speed, distance, damage, w.Suppression * suppressionMultiplier, w.Morale * moraleMultiplier, contact.EnemyId);
            p.Quality = quality;
            p.Critical = critical;
            p.OpeningCritical = openingEligible && critical;
            p.Somali = somali;
            if (somali)
            {
                shooter.SomaliShots++;
                b.Stats.SomaliShots++;
            }

            p.SurpriseMultiplier = SurpriseBurst.ShotMultiplier(b, shooter, contact);
            p.SurpriseTarget = p.SurpriseMultiplier > 1 ? contact.EnemyId : -1;
            if (p.SurpriseMultiplier > 1)
                b.History.Event(b, "surpriseShot", shooter.Id, contact.EnemyId, muzzle, p.EndPoint, p.SurpriseMultiplier, "Урон только исходной цели", p.ShotId);
            b.History.Event(b, "bulletLaunch", shooter.Id, contact.EnemyId, muzzle, p.EndPoint, p.Damage, shooter.SuppressiveFire ? "suppression" : "aimed", p.ShotId, quality);
            // Capture the launch pose once; subsequent movement/aim changes cannot drag a tracer.
            SilentOrders.Rendering.BattleVisualRecipes.WeaponPose(b, shooter, false, out var center, out var barrel, out var angle, out var weapon, out var left);
            p.Trace.MuzzleOffset = barrel - SilentOrders.Rendering.BattleVisualRecipes.Project(muzzle, b.Map);
            V2 screenDir = new V2((shooter.Facing.X + shooter.Facing.Y) * 11, (shooter.Facing.X - shooter.Facing.Y) * 5).Normalized;
            float gunLength = shooter.GunSniper ? 23 : shooter.GunMachineGunner ? 20 : 18;
            V2 baseOffset = SilentOrders.Rendering.BattleVisualRecipes.Project(shooter.Position, b.Map) - SilentOrders.Rendering.BattleVisualRecipes.Project(muzzle, b.Map);
            p.Trace.NativeMuzzleOffset = new V2(baseOffset.X + screenDir.X * (9 + gunLength * .5f), -baseOffset.Y + (shooter.Crouched ? 12 : 17) - (shooter.Side == Side.Green ? 1.5f : 3) + screenDir.Y * (5 + gunLength * .5f));
            if (somali)
            {
                p.Trace.MuzzleOffset = new V2(0, shooter.SomaliLow ? -36 : -14);
                p.Trace.NativeMuzzleOffset = new V2(0, shooter.SomaliLow ? 29 : 14);
            }

            return p;
        }

        public static Bullet Spawn(BattleSimulation b, Soldier shooter, V2 position, float height, V2 direction, float slope, float speed, float range, float damage, float suppression = .22f, float morale = .1f, int intended = -1)
        {
            var state = b.Ballistics;
            var p = state.Pool.Count > 0 ? state.Pool.Pop() : new Bullet();
            if (p.CrouchRoll.Length < b.Soldiers.Count)
                p.CrouchRoll = new byte[b.Soldiers.Count];
            else
                Array.Clear(p.CrouchRoll, 0, p.CrouchRoll.Length);
            p.SurpriseTarget = -1;
            p.SurpriseMultiplier = 1;
            p.PassedCoverRoots.Clear();
            p.DecorHits.Clear();
            p.Fragment = false;
            p.AreaFire = AreaSuppression.Active(b, shooter);
            p.ShotId = state.Launched + 1;
            p.SniperShot = shooter.GunSniper;
            p.SourcePosition = shooter.Position;
            p.ShooterId = shooter.Id;
            p.IntendedId = intended;
            p.Side = shooter.Side;
            p.Position = position;
            p.Height = height;
            p.Direction = direction.Normalized;
            p.Somali = shooter.SomaliFiring;
            p.Slope = slope;
            p.Speed = Math.Max(1, speed);
            p.Remaining = Math.Max(0, range);
            p.Damage = damage;
            p.Suppression = suppression;
            p.Morale = morale;
            p.MaxDistance = Math.Max(range, WeaponBalance.For(shooter).Range);
            p.Ricocheted = p.Critical = p.OpeningCritical = false;
            p.LastAt = b.Time;
            p.Travelled = 0;
            p.Quality = 0;
            SetEnd(b.Map, p, p.Remaining);
            if (p.Stress.Length < b.Soldiers.Count)
                p.Stress = new float[b.Soldiers.Count];
            else
                Array.Clear(p.Stress, 0, p.Stress.Length);
            p.Trace = Trace(b, p, position, height, false);
            state.Active.Add(p);
            state.Launched++;
            if (p.AreaFire)
                state.AreaLaunched++;
            return p;
        }

        public static void Shrapnel(BattleSimulation b, Soldier shooter, V2 point, int kind, float radius, float min, float max, float rangeOverride = -1f)
        {
            int count = kind == 3 ? 8 + (int)(b.Random.Value() * 5) : (kind == 2 ? 5 : 4) + (int)(b.Random.Value() * 3);
            count = Math.Min(kind == 3 ? 12 : kind == 2 ? 7 : 6, count);
            float damage = b.Random.Range(min, max) * (kind == 3 ? .5f : .3f);
            for (int i = 0; i < count; i++)
            {
                float angle = b.Random.Value() * (float)Math.PI * 2;
                var p = Spawn(b, shooter, point, .65f, new V2((float)Math.Cos(angle), (float)Math.Sin(angle)), 0, 70, rangeOverride > 0 ? rangeOverride : kind == 3 ? ArcedOrdnance.HandRadius * 2 * 3 : radius * 2, damage, WeaponBalance.Profiles[0].Suppression * .75f, WeaponBalance.Profiles[0].Morale * .75f);
                p.Fragment = true;
                p.MaxDistance = p.Remaining;
                p.Trace.Fragment = true;
                p.SniperShot = p.Somali = p.AreaFire = false;
                p.SourcePosition = point;
                b.History.Event(b, "fragmentLaunch", shooter.Id, -1, point, p.EndPoint, damage, kind == 3 ? "f1" : kind == 2 ? "ugl" : "grenade", p.ShotId);
            }
        }

        static ShotTrace Trace(BattleSimulation b, Bullet p, V2 point, float height, bool ricochet)
        {
            var t = new ShotTrace
            {
                ShooterId = p.ShooterId,
                Side = p.Side,
                From = point,
                To = point,
                Until = b.Time + TraceSeconds,
                Ballistic = true,
                Ricochet = ricochet,
                FromHeight = height,
                ToHeight = height,
                Tail = point,
                TailHeight = height,
                Born = b.Time
            };
            b.Traces.Add(t);
            return t;
        }

        static void SetEnd(BattleMap map, Bullet p, float range)
        {
            float limit = range;
            p.EndKind = BulletStop.Range;
            if (p.Slope < 0 && -p.Height / p.Slope <= limit + Epsilon)
            {
                limit = Math.Min(limit, -p.Height / p.Slope);
                p.EndKind = BulletStop.Ground;
            }

            float edge = limit;
            if (p.Direction.X > 0)
                edge = Math.Min(edge, (map.Columns - p.Position.X) / p.Direction.X);
            else if (p.Direction.X < 0)
                edge = Math.Min(edge, -p.Position.X / p.Direction.X);
            if (p.Direction.Y > 0)
                edge = Math.Min(edge, (map.Rows - p.Position.Y) / p.Direction.Y);
            else if (p.Direction.Y < 0)
                edge = Math.Min(edge, -p.Position.Y / p.Direction.Y);
            if (edge < limit - Epsilon)
            {
                limit = Math.Max(0, edge);
                p.EndKind = BulletStop.Boundary;
            }

            p.Remaining = limit;
            p.EndPoint = p.Position + p.Direction * limit;
            p.EndHeight = Math.Max(0, p.Height + p.Slope * limit);
            V2 cellPoint = p.EndKind == BulletStop.Boundary ? p.EndPoint - p.Direction * .001f : p.EndPoint;
            p.EndCell = Inside(map, cellPoint) ? map.Index(cellPoint) : -1;
        }

        static bool Inside(BattleMap map, V2 p) => p.X >= 0 && p.Y >= 0 && p.X < map.Columns && p.Y < map.Rows;
        // Parametric slab intersection with a cell prism. Distance is horizontal travel.
        static bool Slab(float origin, float velocity, float min, float max, ref float enter, ref float leave, ref V2 normal, V2 axis, ref bool vertical, ref bool corner)
        {
            if (Math.Abs(velocity) < .000001f)
                return origin >= min && origin <= max;
            float near = (min - origin) / velocity, far = (max - origin) / velocity;
            V2 n = axis * (velocity > 0 ? -1 : 1);
            if (near > far)
            {
                float t = near;
                near = far;
                far = t;
            }

            if (near > enter + Epsilon)
            {
                enter = near;
                normal = n;
                vertical = axis.Length < .1f;
                corner = false;
            }
            else if (Math.Abs(near - enter) <= Epsilon && near >= 0)
            {
                corner = true;
            }

            leave = Math.Min(leave, far);
            return enter <= leave + Epsilon;
        }

        static void Cell(BattleMap map, int x, int y, V2 from, float z, V2 dir, float slope, float max, ref BulletCollision best, HashSet<int> ignored = null)
        {
            if (!map.Inside(x, y) || ignored != null && ignored.Contains(StudioRules.Root(map, y * map.Columns + x)))
                return;
            if(DynamicStructures.Owns(map,y*map.Columns+x))return;
            Tile tile = map.At(new V2(x + .5f, y + .5f));
            if (tile != Tile.LowCover && tile != Tile.Wall)
                return;
            float enter = -100000, leave = max;
            V2 normal = new V2();
            bool vertical = false, corner = false;
            if (!Slab(from.X, dir.X, x, x + 1, ref enter, ref leave, ref normal, new V2(1, 0), ref vertical, ref corner) || !Slab(from.Y, dir.Y, y, y + 1, ref enter, ref leave, ref normal, new V2(0, 1), ref vertical, ref corner) || !Slab(z, slope, 0, StudioRules.Height(map, y * map.Columns + x, tile == Tile.Wall ? WallHeight : LowHeight), ref enter, ref leave, ref normal, new V2(), ref vertical, ref corner) || leave < 0)
                return;
            enter = Math.Max(0, enter);
            if (enter > max + Epsilon || enter > best.Distance + Epsilon)
                return;
            int cell = y * map.Columns + x;
            bool vehicle = map.VehicleFootprint != null && cell < map.VehicleFootprint.Length && map.VehicleFootprint[cell] != 0;
            best = new BulletCollision
            {
                Kind = vehicle ? BulletStop.Vehicle : tile == Tile.Wall ? BulletStop.Wall : BulletStop.LowCover,
                Distance = enter,
                Point = from + dir * enter,
                Height = z + slope * enter,
                Normal = normal,
                Corner = corner || leave - enter < Epsilon,
                VerticalFace = vertical,
                Cell = y * map.Columns + x,
                SoldierId = -1
            };
        }

        public static BulletCollision Geometry(BattleMap map, V2 from, float z, V2 dir, float slope, float distance, HashSet<int> ignored = null)
        {
            var best = new BulletCollision
            {
                Kind = BulletStop.None,
                Distance = distance,
                Cell = -1,
                SoldierId = -1
            };
            if (!Inside(map, from))
            {
                best.Kind = BulletStop.Boundary;
                best.Distance = 0;
                best.Point = from;
                return best;
            }

            if (z <= 0)
            {
                best.Kind = BulletStop.Ground;
                best.Distance = 0;
                best.Point = from;
                return best;
            }

            float exit = distance;
            if (dir.X > 0)
                exit = Math.Min(exit, (map.Columns - from.X) / dir.X);
            else if (dir.X < 0)
                exit = Math.Min(exit, -from.X / dir.X);
            if (dir.Y > 0)
                exit = Math.Min(exit, (map.Rows - from.Y) / dir.Y);
            else if (dir.Y < 0)
                exit = Math.Min(exit, -from.Y / dir.Y);
            if (exit < distance)
            {
                best.Kind = BulletStop.Boundary;
                best.Distance = Math.Max(0, exit);
            }

            if (slope < 0 && -z / slope <= best.Distance)
            {
                best.Kind = BulletStop.Ground;
                best.Distance = -z / slope;
            }

            int x = (int)Math.Floor(from.X), y = (int)Math.Floor(from.Y), sx = dir.X > 0 ? 1 : -1, sy = dir.Y > 0 ? 1 : -1;
            float dx = Math.Abs(dir.X) < .000001f ? float.PositiveInfinity : 1 / Math.Abs(dir.X), dy = Math.Abs(dir.Y) < .000001f ? float.PositiveInfinity : 1 / Math.Abs(dir.Y);
            float tx = float.IsInfinity(dx) ? dx : ((sx > 0 ? x + 1 : x) - from.X) / dir.X, ty = float.IsInfinity(dy) ? dy : ((sy > 0 ? y + 1 : y) - from.Y) / dir.Y;
            for (int count = 0; count < map.Columns + map.Rows + 8 && map.Inside(x, y); count++)
            {
                Cell(map, x, y, from, z, dir, slope, distance, ref best, ignored);
                float next = Math.Min(tx, ty);
                if (next > best.Distance + Epsilon)
                    break;
                if (Math.Abs(tx - ty) < Epsilon)
                {
                    Cell(map, x + sx, y, from, z, dir, slope, distance, ref best, ignored);
                    Cell(map, x, y + sy, from, z, dir, slope, distance, ref best, ignored);
                    if (best.Kind != BulletStop.None && Math.Abs(best.Distance - next) < Epsilon)
                        best.Corner = true;
                    x += sx;
                    y += sy;
                    tx += dx;
                    ty += dy;
                }
                else if (tx < ty)
                {
                    x += sx;
                    tx += dx;
                }
                else
                {
                    y += sy;
                    ty += dy;
                }
            }

            V2 dynamicHit;float fraction;
            if(DynamicStructures.Ray(map,from,z,from+dir*best.Distance,z+slope*best.Distance,out dynamicHit,out fraction)){
                best.Kind=BulletStop.Wall;best.Distance*=fraction;best.Cell=map.Index(dynamicHit);best.Normal=DynamicStructures.Normal(map,dynamicHit,dir);best.VerticalFace=true;
            }
            best.Point = from + dir * best.Distance;
            best.Height = z + slope * best.Distance;
            return best;
        }

        static bool BodyIntersection(V2 relative, V2 delta, float z, float dz, float height, out float t)
        {
            t = 0;
            float a = V2.Dot(delta, delta), c = V2.Dot(relative, relative) - BodyRadius * BodyRadius, lo = 0, hi = 1;
            if (a < .0000001f)
            {
                if (c > 0)
                    return false;
            }
            else
            {
                float bb = V2.Dot(relative, delta), disc = bb * bb - a * c;
                if (disc < 0)
                    return false;
                float root = (float)Math.Sqrt(disc);
                lo = Math.Max(0, (-bb - root) / a);
                hi = Math.Min(1, (-bb + root) / a);
                if (lo > hi)
                    return false;
            }

            if (Math.Abs(dz) < .000001f)
            {
                if (z < 0 || z > height)
                    return false;
            }
            else
            {
                float u = -z / dz, v = (height - z) / dz;
                if (u > v)
                {
                    float tmp = u;
                    u = v;
                    v = tmp;
                }

                lo = Math.Max(lo, u);
                hi = Math.Min(hi, v);
                if (lo > hi)
                    return false;
            }

            t = lo;
            return true;
        }

        static V2 Previous(BattleSimulation b, Soldier s)
        {
            var st = b.Ballistics;
            return st.StepAt == b.Time && s.Id < st.Previous.Length && st.WasDeployed[s.Id] ? st.Previous[s.Id] : s.Position;
        }

        static BulletCollision Collision(BattleSimulation b, Bullet p, float length, float startFraction, float endFraction)
        {
            var hit = new BulletCollision
            {
                Kind = length >= p.Remaining - Epsilon ? p.EndKind : BulletStop.None,
                Distance = length,
                Point = p.Position + p.Direction * length,
                Height = Math.Max(0, p.Height + p.Slope * length),
                Cell = -1,
                SoldierId = -1
            };
            if (p.Fragment || p.PassedCoverRoots.Count > 0)
            {
                var geometry = Geometry(b.Map, p.Position, p.Height, p.Direction, p.Slope, length, p.PassedCoverRoots);
                if (geometry.Kind != BulletStop.None)
                    hit = geometry;
            }
            else if (p.EndCell >= 0)
                Cell(b.Map, p.EndCell % b.Map.Columns, p.EndCell / b.Map.Columns, p.Position, p.Height, p.Direction, p.Slope, length, ref hit);
            foreach (var s in b.Soldiers)
            {
                if (!s.Alive || !s.Deployed || s.Id == p.ShooterId)
                    continue;
                V2 old = Previous(b, s), motion = s.Position - old, from = old + motion * startFraction, delta = p.Direction * length - motion * (endFraction - startFraction);
                float t;
                if (BodyIntersection(p.Position - from, delta, p.Height, p.Slope * length, BodyHeight(s), out t) && t * length <= hit.Distance + Epsilon && (hit.Kind != BulletStop.Soldier || t * length < hit.Distance - Epsilon))
                {
                    if (p.Fragment && s.Id < p.CrouchRoll.Length && p.CrouchRoll[s.Id] == 1)
                        continue;
                    hit = new BulletCollision
                    {
                        Kind = BulletStop.Soldier,
                        SoldierId = s.Id,
                        Distance = t * length,
                        Point = p.Position + p.Direction * (t * length),
                        Height = p.Height + p.Slope * (t * length),
                        Cell = -1
                    };
                }
            }

            if (hit.Kind == BulletStop.Soldier && p.Fragment && FriendlyGrenadeSafety.Bypass(b, p, b.Soldiers[hit.SoldierId]))
                return Collision(b, p, length, startFraction, endFraction);
            return hit;
        }

        public static bool RicochetDirection(V2 incoming, V2 normal, float random, out V2 direction)
        {
            direction = new V2();
            float dot = V2.Dot(incoming, normal), incidence = -dot;
            if (incidence <= .00001f || incidence > .7071068f)
                return false;
            V2 tangent = (incoming - normal * dot).Normalized;
            float angle = (float)Math.Asin(Util.Clamp(incidence, 0, 1));
            float outgoing = Util.Clamp(angle + (random * 2 - 1) * .1745329f, .0174533f, (float)Math.PI * .5f - angle);
            direction = tangent * (float)Math.Cos(outgoing) + normal * (float)Math.Sin(outgoing);
            return true;
        }

        static void Stress(BattleSimulation b, Bullet p, V2 from, float startHeight, V2 to, float endHeight, BulletCollision hit)
        {
            var shooter = b.Soldiers[p.ShooterId];
            bool cover = hit.Kind == BulletStop.Wall || hit.Kind == BulletStop.Vehicle || hit.Kind == BulletStop.LowCover;
            foreach (var s in b.Soldiers)
            {
                if (!s.Alive || !s.Deployed || s.Id == p.ShooterId || s.Id == hit.SoldierId || s.Side == p.Side)
                    continue;
                V2 d = to - from;
                float len2 = V2.Dot(d, d), t = len2 < .000001f ? 0 : Util.Clamp(V2.Dot(s.Position - from, d) / len2, 0, 1);
                float height = startHeight + (endHeight - startHeight) * t;
                float distance = V2.Distance(s.Position, from + d * t);
                float near = distance < 1.2f && height < BodyHeight(s) + 1 && b.Map.LineOfSight(from, s.Position) ? (1 - distance / 1.2f) * .085f : 0;
                float impact = V2.Distance(s.Position, to);
                float around = hit.Kind != BulletStop.None && impact < 2.3f && endHeight < 2.8f && b.Map.LineOfSight(to, s.Position) ? (1 - impact / 2.3f) * (cover ? .16f : .07f) : 0;
                float amount = Math.Max(near, around) * (p.Suppression / .22f) * RifleStress.Multiplier(s, from);
                if (s.Id >= p.Stress.Length)
                    Array.Resize(ref p.Stress, b.Soldiers.Count);
                float extra = amount - p.Stress[s.Id];
                if (extra < .004f)
                    continue;
                if (p.Somali && shooter != null)
                    shooter.SomaliPressure += extra;
                bool first = p.Stress[s.Id] == 0;
                p.Stress[s.Id] = amount;
                b.RecordStress(shooter, s, extra, extra * .055f);
                float before = s.Suppression;
                s.Suppression = Util.Clamp(before + extra*s.RaidSuppressionFactor, 0, 1);
                s.Morale = Util.Clamp(s.Morale - extra * .055f, 0, s.MoraleMaximum);
                s.LastIncoming = b.Time;
                IncomingFire.Observe(b, s, p, from, to);
                if (first)
                {
                    CounterSniper.Incoming(b, s, p);
                    if (around > near && cover)
                        b.Stats.CoverImpacts++;
                    else
                        b.Stats.NearMisses++;
                }

                if (before < .65f && s.Suppression >= .65f)
                    Dialogue.Say(b, s, 8, urgent: true);
            }
        }

        public static bool CrossingDanger(BattleSimulation b, Soldier mover, V2 next)
        {
            foreach (var p in b.Ballistics.Active)
            {
                if (p.Side != mover.Side || p.ShooterId == mover.Id)
                    continue;
                float length = Math.Min(p.Remaining, p.Speed * b.Config.Tick), t;
                if (BodyIntersection(p.Position - mover.Position, p.Direction * length - (next - mover.Position), p.Height, p.Slope * length, BodyHeight(mover), out t))
                    return true;
            }

            return false;
        }

        static void Impact(BattleSimulation b, BulletCollision hit)
        {
            bool dust = hit.Kind == BulletStop.Ground;
            if (!dust && hit.Kind != BulletStop.Wall && hit.Kind != BulletStop.Vehicle && hit.Kind != BulletStop.LowCover)
                return;
            // Visual hash does not consume the gameplay RNG; rendering never changes accuracy.
            for (int i = 0; i < (dust ? 2 : 5); i++)
            {
                float angle = (b.Ballistics.Launched * 2.399963f + i * 1.256637f), radius = .18f + .06f * i;
                var list = b.Ballistics.Sparks;
                if (list.Count >= 256)
                    list.RemoveAt(0);
                list.Add(new BulletSpark { Origin = hit.Point, Height = hit.Height, Born = b.Time, Dust = dust, Direction = new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius });
            }
        }

        public static V2 SparkPosition(BulletSpark spark, float time, out float height)
        {
            float t = Util.Clamp((time - spark.Born) / .22f, 0, 1);
            height = Math.Max(0, spark.Height + .15f * (float)Math.Sin(t * Math.PI) - .1f * t);
            return spark.Origin + spark.Direction * t;
        }

        public static void Update(BattleSimulation b)
        {
            var st = b.Ballistics;
            st.Sparks.RemoveAll(s => b.Time - s.Born > .22f);
            for (int i = st.Active.Count - 1; i >= 0; i--)
            {
                var p = st.Active[i];
                float time = Math.Min(b.Config.Tick, Math.Max(0, b.Time - p.LastAt));
                if (time <= 0)
                    continue;
                p.LastAt = b.Time;
                bool alive = true;
                float elapsed = 0;
                for (int segment = 0; segment < 16 && alive && elapsed < time; segment++)
                {
                    float length = Math.Min(p.Remaining, p.Speed * (time - elapsed));
                    if (length <= Epsilon)
                    {
                        alive = false;
                        break;
                    }

                    float segmentTime = length / p.Speed;
                    var hit = Collision(b, p, length, elapsed / time, (elapsed + segmentTime) / time);
                    V2 from = p.Position;
                    float oldHeight = p.Height;
                    p.Position = hit.Point;
                    p.Height = hit.Height;
                    p.Remaining -= hit.Distance;
                    p.Travelled += hit.Distance;
                    elapsed += hit.Distance / p.Speed;
                    p.Trace.To = p.Position;
                    p.Trace.ToHeight = p.Height;
                    p.Trace.Until = b.Time + TraceSeconds;
                    float tail = Math.Min(p.Fragment ? TracerLength / 3 : TracerLength, V2.Distance(p.Trace.From, p.Position));
                    p.Trace.Tail = p.Position - p.Direction * tail;
                    p.Trace.TailHeight = p.Height - p.Slope * tail;
                    DynamicStructures.Glass(b.Map, from, oldHeight, p.Position, p.Height);
                    DecorRules.Trace(b.Map, from, oldHeight, p.Position, p.Height, p.DecorHits);
                    TraceObstacles(b, from, oldHeight, p.Position, p.Height, p.DecorHits, p.Direction);
                    Impact(b, hit);
                    Stress(b, p, from, oldHeight, p.Position, p.Height, hit);
                    if (hit.Kind != BulletStop.None)
                        b.History.Event(b, "bulletImpact", p.ShooterId, hit.SoldierId, from, p.Position, p.Damage, hit.Kind.ToString(), p.ShotId, p.Quality);
                    if (hit.Kind == BulletStop.Soldier)
                    {
                        var victim = b.Soldiers[hit.SoldierId];
                        var shooter = b.Soldiers[p.ShooterId];
                        float bonus = RifleStress.Multiplier(victim, from);
                        bool wasAlive = victim.Alive;
                        CounterSniper.Incoming(b, victim, p);
                        IncomingFire.Observe(b, victim, p, from, p.Position);
                        float hpBefore = victim.Health;
                        bool armorBlocked = BodyArmor.BlocksFragment(victim.ArmorLevel, p.Fragment, b.Random);
                        if (!armorBlocked)
                            b.Damage(victim, shooter, SurpriseBurst.Damage(p, victim.Id), p.Suppression * bonus, p.Morale * bonus);
                        else
                            b.History.Event(b, "fragmentArmorBlock", shooter.Id, victim.Id, from, p.Position, 0);
                        if (p.Somali && shooter != null)
                            shooter.SomaliDamageDealt += Math.Max(0, hpBefore - victim.Health);
                        if (SomaliFire.Visual(victim, b.Time))
                            victim.SomaliDamageReceived += Math.Max(0, hpBefore - victim.Health);
                        p.Trace.Hit = true;
                        st.Hits++;
                        b.Stats.Hits++;
                        if (p.Somali && victim.Side != p.Side)
                        {
                            shooter.SomaliHits++;
                            b.Stats.SomaliHits++;
                            if (wasAlive && !victim.Alive)
                            {
                                shooter.SomaliKills++;
                                b.Stats.SomaliKills++;
                            }
                        }

                        if (p.Critical)
                            shooter.CriticalHits++;
                        if (p.OpeningCritical)
                            ReconOpening.RecordCriticalHit(b);
                        if (victim.Side == p.Side)
                            st.FriendlyHits++;
                        else if (victim.Id != p.IntendedId)
                            st.StrayHits++;
                        alive = false;
                    }
                    else if (hit.Kind == BulletStop.Wall)
                    {
                        V2 reflected;
                        if (!p.Fragment && !p.Ricocheted && !hit.Corner && !hit.VerticalFace && RicochetDirection(p.Direction, hit.Normal, b.Random.Value(), out reflected))
                        {
                            st.Ricochets++;
                            p.Ricocheted = true;
                            p.Direction = reflected;
                            p.Damage *= .4f;
                            p.Suppression *= .5f;
                            p.Morale *= .5f;
                            p.Speed *= .55f;
                            float range = b.Random.Range(2, RicochetRange);
                            p.Position += hit.Normal * .001f;
                            p.Slope = -p.Height / range;
                            SetEnd(b.Map, p, range);
                            p.Trace = Trace(b, p, p.Position, p.Height, true);
                        }
                        else
                        {
                            st.WallStops++;
                            alive = false;
                        }
                    }
                    else if (hit.Kind != BulletStop.None)
                    {
                        if (hit.Kind == BulletStop.LowCover)
                        {
                            bool partial = StudioRules.Partial(b.Map, hit.Cell);
                            int root = StudioRules.Root(b.Map, hit.Cell);
                            b.Stats.CoverDestroyed += CoverDestruction.Bullet(b.Map, b.Map.Center(hit.Cell), p.Direction);
                            if (partial && b.Random.Value() < .20f)
                            {
                                p.PassedCoverRoots.Add(root);
                                st.PartialPasses++;
                                // Continue along the same ray within weapon range; never roll the same object twice.
                                SetEnd(b.Map, p, Math.Max(p.Remaining, p.MaxDistance - p.Travelled));
                                b.History.Event(b, "partialCoverPass", p.ShooterId, -1, from, p.Position, p.Damage, "20%: cover damage applied", p.ShotId);
                                if (p.Remaining > Epsilon)
                                    continue;
                            }

                            st.LowCoverStops++;
                        }

                        if (hit.Kind == BulletStop.Vehicle)
                            st.VehicleStops++;
                        if (hit.Kind == BulletStop.Ground)
                            st.GroundStops++;
                        alive = false;
                    }
                    else if (p.Remaining <= Epsilon)
                        alive = false;
                }

                if (!alive)
                {
                    st.Active.RemoveAt(i);
                    if (st.Pool.Count < 128)
                        st.Pool.Push(p);
                }
            }
        }
    }
}
