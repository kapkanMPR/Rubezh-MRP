using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    // A vehicle uses an 8 x 4 oriented footprint, matching the 132 x 105
    // sprite at the 22 x 10 pixel isometric tile projection. It travels four
    // cells along X. One vehicle is permitted per side.
    public sealed class VehicleArrivalState
    {
        public Side Side;
        public V2 Center;
        public V2 Landing;
        public float StartTime;
        public float AmbushSpeechAt = -1f;
        public int AmbushCommanderId = -1;
        public bool Active, Stopped, AmbushShotAttempted;
        public DefenderGroupState DefenderGroup;
        public int Direction
        {
            get
            {
                return Side == Side.Green ? 2 : 0;
            }
        }
    }

    public static class VehicleArrivals
    {
        public const int LengthCells = 8;
        public const int WidthCells = 4;
        const float HalfLength = LengthCells * .5f;
        const float HalfWidth = WidthCells * .5f;
        // Extra clearance for the edges of tires and animated soldier feet.
        const float SoldierClearance = .48f;
        const float Travel = 4f;
        const float Seconds = 4f;
        // Keep transport away from new procedural walls: legacy clearing only removes catalog objects.
        public static int ProceduralLane(int columns, int rows, Side side, Func<int,bool> occupied)
        {
            int middle=rows/2;
            for(int distance=0;distance<rows;distance++)foreach(int y in new[]{middle-distance,middle+distance})
            {
                if(y<2||y>rows-2)continue;
                bool clear=true;int left=side==Side.Green?0:columns-9,right=side==Side.Green?8:columns-1;
                for(int yy=y-2;yy<=y+1&&clear;yy++)for(int x=left;x<=right;x++)if(occupied(yy*columns+x)){clear=false;break;}
                if(clear)return y;
            }
            return -1;
        }
        public static VehicleArrivalState Start(BattleSimulation b, Side side, DefenderGroupState group = null)
        {
            var existing = side == Side.Green ? b.GreenVehicle : b.RedVehicle;
            if (existing != null)
                return existing;
            float from = side == Side.Green ? 1f : b.Map.Columns - 1f;
            float stop = from + (side == Side.Green ? Travel : -Travel);
            // Integer centres place even-sized (8 x 4) hull between grid cell centres.
            // Initial position may be partly outside the map: transport drives in from the edge.
            float centerY = (float)Math.Floor(b.Map.Rows * .5f);
            if(b.Map.DynamicBuildings!=null&&b.Map.DynamicBuildings.Count>0){
                int lane=ProceduralLane(b.Map.Columns,b.Map.Rows,side,i=>i==b.Map.Index(b.Map.Objective)||b.Map.ProceduralCells!=null&&b.Map.ProceduralCells[i]&&b.Map.ProceduralHeight[i]>.02f||StudioRules.Permanent(b.Map,i));
                if(lane<0)throw new InvalidOperationException("Нет свободного маршрута транспорта");
                centerY=lane;
            }
            var state = new VehicleArrivalState
            {
                Side = side,
                StartTime = b.Time,
                Active = true,
                Stopped = false,
                Center = new V2(from, centerY),
                Landing = new V2(stop, centerY),
                DefenderGroup = group
            };
            if (side == Side.Green)
            {
                b.GreenVehicle = state;
                foreach (var s in b.Soldiers)
                    if (s.Side == Side.Green && s.Alive)
                    {
                        s.Deployed = false;
                        s.Moving = false;
                        s.Visible.Clear();
                    }
            }
            else
                b.RedVehicle = state;
            b.Log(side, side == Side.Green ? "ГАЗ Тигр: транспорт приближается" : "Humvee: прибытие подкрепления");
            return state;
        }

        public static void Update(BattleSimulation b)
        {
            Tick(b, b.GreenVehicle);
            Tick(b, b.RedVehicle);
        }

        static void Tick(BattleSimulation b, VehicleArrivalState v)
        {
            if (v == null || !v.Active || v.Stopped)
                return;
            float t = Math.Max(0f, Math.Min(1f, (b.Time - v.StartTime) / Seconds));
            float from = v.Side == Side.Green ? 1f : b.Map.Columns - 1f;
            float centerY = v.Landing.Y;
            v.Center = new V2(from + (v.Side == Side.Green ? Travel : -Travel) * t, centerY);
            ClearPath(b, v.Center);
            if (t < 1f)
                return;
            v.Center = v.Landing;
            // Use the very same footprint as the moving bulldozer and spawn safety.
            // Solid, infinite-health walls block movement, shots and grenades while the
            // visual vehicle replaces their generic wall geometry.
            for (int y = 0; y < b.Map.Rows; y++)
                for (int x = 0; x < b.Map.Columns; x++)
                {
                    int i = y * b.Map.Columns + x;
                    if (!InsideFootprint(b.Map.Center(i), v.Center))
                        continue;
                    b.Map.Tiles[i] = Tile.LowCover;
                    b.Map.CoverHealth[i] = float.PositiveInfinity;
                    b.Map.LevelProps[i] = 0;
                    b.Map.LowCoverStyle[i] = 0;
                    b.Map.VehicleFootprint[i] = 1;
                }

            b.Map.Revision++;
            v.Stopped = true;
            if (v.Side == Side.Green)
            {
                DeployGreen(b, v);
                foreach (var commander in b.Soldiers)
                    if (commander.Side == Side.Green && commander.Role == Role.Commander && commander.Deployed && commander.Alive)
                    {
                        v.AmbushCommanderId = commander.Id;
                        v.AmbushSpeechAt = b.Time;
                        b.Log(Side.Green, "Группа контакт! ЗАСАДА!");
                        break;
                    }
            }

            b.Log(v.Side, "Машина остановилась, началась высадка");
            AmbushShot(b, v);
        }

        public static void AmbushShot(BattleSimulation b, VehicleArrivalState v)
        {
            if (v == null || !v.Stopped || v.Side != Side.Green || b.CampaignLevel != 15 || v.AmbushShotAttempted)
                return;
            v.AmbushShotAttempted = true;
            Soldier shooter = null;
            float nearest = float.MaxValue;
            foreach (var s in b.Soldiers)
            {
                if (s.Side != Side.Red || !s.Deployed || !s.Available || s.Busy || s.Rockets <= 0 || s.LauncherKind != 0)
                    continue;
                float distance = V2.Distance(s.Position, v.Center);
                if (distance < 4.5f)
                    continue;
                var impact = b.Map.LauncherImpact(s.Position, v.Center);
                if (V2.Distance(s.Position, impact) > SpecialWeapons.Range(b) || !VehicleArrivals.InsideFootprint(impact, v.Center) || b.FriendlyInLane(s, impact))
                    continue;
                bool safe = true;
                foreach (var mate in b.Soldiers)
                    if (mate.Side == s.Side && mate.Deployed && mate.Alive && V2.Distance(mate.Position, impact) < SpecialWeapons.Radius(s) + .4f)
                        safe = false;
                if (safe && distance < nearest)
                {
                    shooter = s;
                    nearest = distance;
                }
            }

            if (shooter == null)
            {
                b.Log(Side.Red, "Засада: вступительный пуск невозможен — нет готового гранатомётчика с открытой линией к машине");
                return;
            }

            SpecialWeapons.LaunchProjectile(b, shooter, v.Center);
            shooter.Reason = "Засада: машина остановилась — пуск по транспорту";
            b.Log(Side.Red, shooter.Name + ": вступительный выстрел по остановившейся машине");
        }

        public static bool InsideFootprint(V2 p, V2 center)
        {
            return Math.Abs(p.X - center.X) < HalfLength && Math.Abs(p.Y - center.Y) < HalfWidth;
        }

        static bool TooCloseToVehicle(V2 p, V2 center)
        {
            return Math.Abs(p.X - center.X) < HalfLength + SoldierClearance && Math.Abs(p.Y - center.Y) < HalfWidth + SoldierClearance;
        }

        static void ClearPath(BattleSimulation b, V2 center)
        {
            var map = b.Map;
            int minX = Math.Max(0, (int)Math.Floor(center.X - HalfLength));
            int maxX = Math.Min(map.Columns - 1, (int)Math.Ceiling(center.X + HalfLength));
            int minY = Math.Max(0, (int)Math.Floor(center.Y - HalfWidth));
            int maxY = Math.Min(map.Rows - 1, (int)Math.Ceiling(center.Y + HalfWidth));
            bool dirty = false;
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    if (!InsideFootprint(map.Center(y * map.Columns + x), center))
                        continue;
                    int index = y * map.Columns + x;
                    if (StudioRules.Permanent(map, index))
                        continue;
                    if (map.Tiles[index] == Tile.LowCover || map.Tiles[index] == Tile.Wall)
                    {
                        if (map.Tiles[index] == Tile.LowCover)
                            map.BrokenCovers.Add(new CoverBreakEvent { Index = index, Prop = (byte)(StudioRules.At(map, index)?.Asset.DebrisProp ?? map.LevelProps[index]), LegacyStyle = (byte)(StudioRules.At(map, index)?.Asset.DebrisStyle ?? map.LowCoverStyle[index]) });
                        StudioRules.Remove(map, index);
                        map.Tiles[index] = Tile.Rubble;
                        map.CoverHealth[index] = 0f;
                        map.LevelProps[index] = map.LowCoverStyle[index] = 0;
                        dirty = true;
                    }
                }

            // Move occupied soldiers sideways, never damage them. Also recalculate destinations.
            foreach (var soldier in b.Soldiers)
            {
                if (!soldier.Deployed || !soldier.Alive || !TooCloseToVehicle(soldier.Position, center))
                    continue;
                float side = soldier.Position.Y < center.Y ? -1f : 1f;
                var preferred = new V2(soldier.Position.X, center.Y + side * (HalfWidth + SoldierClearance + .5f));
                var safe = SafeNear(b, preferred, center);
                soldier.Position = soldier.Destination = soldier.OrderAnchor = safe;
                soldier.Path.Clear();
                soldier.PathCursor = 0;
                soldier.NextDecision = 0;
            }

            if (dirty)
                map.Revision++;
        }

        public static V2 SafeNear(BattleSimulation b, V2 preferred, V2 exclude)
        {
            // Prefer free cells outside the tires, then cells already occupied if every
            // nearby free cell is taken. Never fall back INSIDE the vehicle.
            int fallback = -1, free = -1;
            float freeDist = float.MaxValue, anyDist = float.MaxValue;
            for (int i = 0; i < b.Map.Tiles.Length; i++)
            {
                var p = b.Map.Center(i);
                if (TooCloseToVehicle(p, exclude) || !b.Map.Walkable(p))
                    continue;
                float d = V2.Distance(p, preferred);
                if (d < anyDist)
                {
                    anyDist = d;
                    fallback = i;
                }

                bool occupied = false;
                foreach (var soldier in b.Soldiers)
                    if (soldier.Deployed && soldier.Alive && V2.Distance(soldier.Position, p) < .72f)
                    {
                        occupied = true;
                        break;
                    }

                if (!occupied && d < freeDist)
                {
                    freeDist = d;
                    free = i;
                }
            }

            if (free >= 0)
                return b.Map.Center(free);
            if (fallback >= 0)
                return b.Map.Center(fallback);
            // Degenerate maps should fail loudly rather than silently spawn inside solid tiles.
            throw new InvalidOperationException("No walkable disembarkation cell outside vehicle");
        }

        static void DeployGreen(BattleSimulation b, VehicleArrivalState v)
        {
            // Existing green positions are provisional while a transport is driving.
            // Unregister them first so soldiers do not count as occupying their own
            // obsolete starting cells during disembarkation.
            foreach (var soldier in b.Soldiers)
                if (soldier.Side == Side.Green && soldier.Alive)
                    soldier.Deployed = false;
            int i = 0;
            foreach (var soldier in b.Soldiers)
            {
                if (soldier.Side != Side.Green || !soldier.Alive)
                    continue;
                float side = i % 2 == 0 ? -1f : 1f;
                var near = new V2(v.Center.X + (i % 5 - 2) * 1.5f, v.Center.Y + side * (HalfWidth + SoldierClearance + .7f));
                var p = SafeNear(b, near, v.Center);
                soldier.Position = soldier.Destination = soldier.OrderAnchor = p;
                soldier.Path.Clear();
                soldier.PathCursor = 0;
                soldier.NextDecision = b.Time;
                soldier.Deployed = true;
                BeginDispersal(b, soldier, v, i);
                i++;
            }

            // The procedural entry may now be inside the newly parked vehicle.
            b.Map.Entry = SafeNear(b, new V2(v.Center.X - HalfLength - 1f, v.Center.Y), v.Center);
            b.Green.Initialized = false;
            b.Green.NextPlan = b.Time;
        }

        // Transport deployment stays distinct from patrol/assault logic. The soldiers
        // first move out from BOTH flanks, spread into a forward line and prefer cover.
        public static void BeginDispersal(BattleSimulation b, Soldier soldier, VehicleArrivalState vehicle, int ordinal)
        {
            float side = soldier.Position.Y < vehicle.Center.Y ? -1f : 1f;
            float forward = vehicle.Side == Side.Green ? 1f : -1f;
            float x = vehicle.Center.X + forward * (HalfLength + 1.4f + (ordinal / 4 % 6) * .88f);
            float y = vehicle.Center.Y + side * (HalfWidth + 1.65f + (ordinal / 12) * .85f);
            var ideal = new V2(x, y);
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < b.Map.Tiles.Length; i++)
            {
                var pos = b.Map.Center(i);
                if (!b.Map.Walkable(pos) || TooCloseToVehicle(pos, vehicle.Center))
                    continue;
                float deviation = V2.Distance(pos, ideal);
                if (deviation > 8f)
                    continue;
                bool occupied = false;
                foreach (var other in b.Soldiers)
                {
                    if (other == soldier || !other.Alive || !other.Deployed)
                        continue;
                    if (V2.Distance(other.Position, pos) < .85f || (other.VehicleDisperse && V2.Distance(other.VehicleDisperseTarget, pos) < 2.2f))
                    {
                        occupied = true;
                        break;
                    }
                }

                if (occupied)
                    continue;
                // Cover neighbouring the destination is desirable, but never worth
                // crossing the vehicle or packing two soldiers onto the same spot.
                float bonus = 0f;
                int cx = (int)pos.X, cy = (int)pos.Y;
                for (int yy = Math.Max(0, cy - 1); yy <= Math.Min(b.Map.Rows - 1, cy + 1); yy++)
                    for (int xx = Math.Max(0, cx - 1); xx <= Math.Min(b.Map.Columns - 1, cx + 1); xx++)
                    {
                        var tile = b.Map.Tiles[yy * b.Map.Columns + xx];
                        if (tile == Tile.LowCover || tile == Tile.Wall)
                            bonus = 1.25f;
                    }

                float score = deviation + V2.Distance(pos, soldier.Position) * .09f - bonus;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            // Constrained maps must still produce a valid point outside the tyres.
            var target = best >= 0 ? b.Map.Center(best) : SafeNear(b, ideal, vehicle.Center);
            soldier.VehicleDisperse = true;
            soldier.VehicleDisperseTarget = target;
            soldier.VehicleDisperseUntil = b.Time + 9f;
            soldier.VehicleRouteAt = b.Time;
            soldier.Path.Clear();
            soldier.PathCursor = 0;
            soldier.MoveAuthorized = true;
            soldier.ManeuverPaused = false;
            soldier.OrderAnchor = soldier.Destination = target;
            soldier.Job = Job.Reposition;
            soldier.Order = "Рассредоточиться и занять рубеж";
            soldier.Reason = "Высадка: развертывание по флангам";
            soldier.NextDecision = b.Time;
        }

        public static bool Disembarking(BattleSimulation b, Side side)
        {
            var vehicle = side == Side.Green ? b.GreenVehicle : b.RedVehicle;
            if (vehicle != null && vehicle.Active && !vehicle.Stopped)
                return true;
            foreach (var s in b.Soldiers)
                if (s.Deployed && s.Alive && s.Side == side && s.VehicleDisperse)
                    return true;
            return false;
        }

        public static bool Disperse(BattleSimulation b, Soldier s, float dt)
        {
            if (!s.VehicleDisperse)
                return false;
            var target = s.VehicleDisperseTarget;
            if (V2.Distance(s.Position, target) < .85f || b.Time >= s.VehicleDisperseUntil)
            {
                s.VehicleDisperse = false;
                s.Path.Clear();
                s.PathCursor = 0;
                s.OrderAnchor = s.Destination = s.Position;
                s.Job = Job.Hold;
                s.MoveAuthorized = true;
                s.Crouched = true;
                s.NextDecision = b.Time;
                if (s.DefenderGroup != null)
                    s.DefenderGroup.Command.NextPlan = b.Time;
                else
                    b.Green.NextPlan = b.Time;
                return false;
            }

            if (!s.Alive || !s.Deployed || s.Incapacitated || s.Broken)
                return false;
            if (s.PathCursor >= s.Path.Count || b.Time >= s.VehicleRouteAt)
            {
                s.Path = LossMemory.Find(b, s, target, p => PositionLogic.Occupied(b, s, p) ? 12f : 0f);
                s.PathCursor = 0;
                s.VehicleRouteAt = b.Time + 1.2f;
            }

            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Разбегаемся, занимаем рубеж";
            s.Reason = "Рассредоточение после высадки";
            s.Moving = false;
            s.Crouched = false;
            if (s.PathCursor < s.Path.Count)
            {
                Locomotion.AdvanceWaypoint(b, s);
                if (s.PathCursor < s.Path.Count)
                {
                    var delta = s.Path[s.PathCursor] - s.Position;
                    var speed = CombatMovement.Speed(b, s);
                    V2 next;
                    if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, speed * dt), out next))
                    {
                        s.DistanceTravelled += V2.Distance(s.Position, next);
                        s.Position = next;
                        s.Facing = delta.Normalized;
                        s.Moving = true;
                        s.LastMoved = b.Time;
                        if (V2.Distance(next, s.Path[s.PathCursor]) < .07f)
                            s.PathCursor++;
                    }
                    else
                    {
                        s.Path.Clear();
                        s.PathCursor = 0;
                        s.VehicleRouteAt = b.Time + .3f;
                    }
                }
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }

        // Red units must reach their AUTHORED spawn before their own hold/attack AI takes over.
        public static bool Rally(BattleSimulation b, Soldier s, float dt)
        {
            if (!s.VehicleRally || s.DefenderGroup == null)
                return false;
            int slot = s.DefenderGroup.Members.IndexOf(s);
            var offset = new V2((slot % 3 - 1) * 2.4f, (slot / 3 == 0 ? -1 : 1) * 2.4f);
            var target = b.Map.NearestOpen(s.DefenderGroup.Spawn + offset);
            if (V2.Distance(s.Position, target) < 1.4f || V2.Distance(s.Position, s.DefenderGroup.Spawn) <= Math.Max(3f, s.DefenderGroup.Radius) || s.Visible.Count > 0 || b.Time - s.LastIncoming < 2f && b.Time > 2f)
            {
                s.VehicleRally = false;
                s.Path.Clear();
                s.PathCursor = 0;
                s.OrderAnchor = s.Destination = s.Position;
                s.NextDecision = b.Time;
                s.DefenderGroup.Command.NextPlan = b.Time;
                return false;
            }

            if (s.Broken || s.Incapacitated)
                return false;
            if (s.PathCursor >= s.Path.Count || b.Time >= s.VehicleRouteAt)
            {
                s.Path = LossMemory.Find(b, s, target, p => PositionLogic.Occupied(b, s, p) ? 12f : 0f);
                s.PathCursor = 0;
                s.VehicleRouteAt = b.Time + 1.2f;
            }

            s.Job = Job.Reposition;
            s.MoveAuthorized = true;
            s.ManeuverPaused = false;
            s.Order = "Приоритет: добраться до назначенного спавна";
            s.Reason = "Высадка с Humvee — переход к рубежу";
            s.Moving = false;
            if (s.PathCursor < s.Path.Count)
            {
                Locomotion.AdvanceWaypoint(b, s);
                if (s.PathCursor < s.Path.Count)
                {
                    var delta = s.Path[s.PathCursor] - s.Position;
                    var speed = CombatMovement.Speed(b, s);
                    V2 next;
                    if (Locomotion.TryStep(b, s, delta.Normalized * Math.Min(delta.Length, speed * dt), out next))
                    {
                        s.DistanceTravelled += V2.Distance(s.Position, next);
                        s.Position = next;
                        s.Facing = delta.Normalized;
                        s.Moving = true;
                        s.LastMoved = b.Time;
                        if (V2.Distance(next, s.Path[s.PathCursor]) < .07f)
                            s.PathCursor++;
                    }
                    else
                    {
                        s.Path.Clear();
                        s.PathCursor = 0;
                        s.VehicleRouteAt = b.Time + .3f;
                    }
                }
            }

            s.State = s.Moving ? ActionState.Move : ActionState.Observe;
            return true;
        }
    }
}
