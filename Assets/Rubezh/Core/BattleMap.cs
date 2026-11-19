using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public sealed class BattleMap
    {
        internal DynamicWallIndex WallIndex;
        public bool[] Roof;
        public System.Collections.Generic.List<DynamicBuilding> DynamicBuildings;
        public ProceduralPicture[] ProceduralPictures;
        public ProceduralPicture TerrainPicture;
        public bool[] ProceduralCells;
        public float[] ProceduralProtection,
            ProceduralHeight;
        internal WallCornerCover.Cache CornerCache;
        public const int Width = 60;
        public const int Height = 36;
        public int Revision;
        public int Columns = 60;
        public int Rows = 36;
        public int Theme;

        // Optional authored visuals. Zero = original procedural cover, 1..16 = HD level props.
        // Low cover and high walls preserve the original Tile collision/ballistics contracts.
        public byte[] LevelProps = new byte[2160];

        // Render transforms are optional per tile. Zero scales on old maps mean 1.
        public float[] LevelPropRotation = new float[2160];
        public float[] LevelPropScaleX = new float[2160];
        public float[] LevelPropScaleY = new float[2160];
        public byte[] LowCoverStyle = new byte[2160];
        public byte[] VehicleFootprint = new byte[2160]; // non-destructible parked car, suppress generic 3D wall visuals

        // Events are only read by the visual debris generator; not used in pathfinding, hits or AI.
        public readonly System.Collections.Generic.List<CoverBreakEvent> BrokenCovers =
            new System.Collections.Generic.List<CoverBreakEvent>();
        public bool CustomLevel;
        public int ExteriorMission; // Visual environment profile only.
        public V2 EnemyEntry = new V2(50.5f, 18.5f);
        public V2 Entry = new V2(3.5f, 18.5f);
        public float[] CoverHealth = new float[2160];
        public Tile[] Tiles = new Tile[2160];
        public V2 Objective = new V2(56.5f, 18.5f);
        public const float CaptureRadius = 2.1f;
        public static readonly int[,] Layouts = new int[20, 3]
        {
            { 36, 24, 1 },
            { 44, 28, 0 },
            { 48, 24, 1 },
            { 42, 32, 2 },
            { 46, 28, 4 },
            { 48, 32, 2 },
            { 56, 26, 3 },
            { 46, 30, 2 },
            { 54, 30, 0 },
            { 54, 34, 3 },
            { 58, 30, 2 },
            { 60, 24, 1 },
            { 54, 36, 4 },
            { 58, 34, 5 },
            { 62, 28, 1 },
            { 56, 38, 5 },
            { 64, 32, 0 },
            { 66, 30, 3 },
            { 60, 38, 2 },
            { 68, 32, 5 },
        };

        public BattleMap()
        {
            for (int i = 0; i < 36; i++)
            {
                for (int j = 0; j < 60; j++)
                {
                    Tiles[i * 60 + j] = ((i >= 17 && i <= 19) ? Tile.Road : Tile.Ground);
                }
            }

            for (int k = 0; k < CoverHealth.Length; k++)
            {
                CoverHealth[k] = 100f;
            }

            for (int l = 7; l < 58; l += 6)
            {
                for (int m = 3; m < 32; m += 6)
                {
                    Cover(l, m, 4, horizontal: false);
                }

                Cover(l - 2, 16, 2, horizontal: true);
                Cover(l + 1, 21, 2, horizontal: true);
            }

            Wall(15, 5, 5, horizontal: true);
            Wall(15, 5, 4, horizontal: false);
            Wall(27, 27, 5, horizontal: true);
            Wall(31, 27, 5, horizontal: false);
            Wall(39, 6, 5, horizontal: true);
            Wall(43, 6, 4, horizontal: false);
            Wall(45, 25, 4, horizontal: true);
            Wall(45, 25, 4, horizontal: false);
            Wall(21, 11, 3, horizontal: true);
            Wall(33, 23, 3, horizontal: true);
            Cover(54, 15, 3, horizontal: true);
            Cover(54, 22, 3, horizontal: true);
            for (int n = 0; n < 220; n++)
            {
                int scatterX = (n * 17 + 11) % 60;
                int scatterY = (n * 7 + 3) % 36;
                if (At(scatterX, scatterY) == Tile.Ground)
                {
                    Tiles[scatterY * 60 + scatterX] = Tile.Rubble;
                }
            }
        }

        private void Wall(int x, int y, int n, bool horizontal)
        {
            Line(x, y, n, horizontal, Tile.Wall);
        }

        private void Cover(int x, int y, int n, bool horizontal)
        {
            Line(x, y, n, horizontal, Tile.LowCover);
        }

        private void Line(int x, int y, int n, bool horizontal, Tile t)
        {
            for (int i = 0; i < n; i++)
            {
                int cellX = x + (horizontal ? i : 0);
                int cellY = y + ((!horizontal) ? i : 0);
                if (Inside(cellX, cellY))
                {
                    Tiles[cellY * Columns + cellX] = t;
                }
            }
        }

        public bool Inside(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Columns && y < Rows;
        }

        public Tile At(int x, int y)
        {
            return Inside(x, y) ? Tiles[y * Columns + x] : Tile.Wall;
        }

        public Tile At(V2 p)
        {
            return At((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        }

        public bool Walkable(int x, int y)
        {
            Tile tile = At(x, y);
            return tile != Tile.Wall && tile != Tile.LowCover && !StudioRules.Obstacle(this, y * Columns + x);
        }

        public bool Walkable(V2 p)
        {
            return Walkable((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        }

        public int Index(V2 p)
        {
            return (int)Math.Floor(p.Y) * Columns + (int)Math.Floor(p.X);
        }

        public V2 Center(int i)
        {
            return new V2((float)(i % Columns) + 0.5f, (float)(i / Columns) + 0.5f);
        }

        public readonly GeometryQueryCache GeometryQueries = new GeometryQueryCache();

        public bool LineOfSight(V2 a, V2 b)
        {
            return GeometryQueries.Sight(this, a, b);
        }

        public bool LineOfSightRaw(V2 a, V2 b)
        {
            V2 hit;
            float fraction;
            if (DynamicStructures.Ray(this, a, 1.5f, b, 1.5f, out hit, out fraction))
                return false;
            int sampleCount = Math.Max(1, (int)Math.Ceiling(V2.Distance(a, b) * 8f));
            for (int i = 1; i < sampleCount; i++)
            {
                if (
                    At(a + (b - a) * ((float)i / (float)sampleCount)) == Tile.Wall
                    && !DynamicStructures.Owns(this, Index(a + (b - a) * ((float)i / sampleCount)))
                )
                {
                    return false;
                }
            }

            return true;
        }

        public float Protection(V2 p, V2 threat, bool corners = true)
        {
            return corners ? GeometryQueries.Protection(this, p, threat) : ProtectionRaw(p, threat, false);
        }

        public float ProtectionRaw(V2 p, V2 threat, bool corners = true)
        {
            V2 normalized = (threat - p).Normalized;
            for (float sampleRadius = 0.25f; sampleRadius <= 1.55f; sampleRadius += 0.16f)
            {
                switch (At(p + normalized * sampleRadius))
                {
                    case Tile.LowCover:
                        int ci = Index(p + normalized * sampleRadius);
                        if (ProceduralCells != null && ci >= 0 && ci < ProceduralCells.Length && ProceduralCells[ci])
                        {
                            if (ProceduralProtection[ci] > 0)
                                return ProceduralProtection[ci];
                            break;
                        }
                        return StudioRules.Medium(this, ci) ? .65f : .50f; // Durability changes lifetime, not the intact cover protection coefficient.
                    case Tile.Wall:
                        return 0.75f;
                }
            }

            return corners && WallCornerCover.At(this, p, threat) ? WallCornerCover.Protection : 0f;
        }

        public V2 RocketImpact(V2 a, V2 target)
        {
            V2 hit;
            float fraction;
            if (DynamicStructures.Ray(this, a, 1.55f, target, 1.55f, out hit, out fraction))
                target = hit;
            float sightDistance = V2.Distance(a, target);
            int sampleCount = Math.Max(1, (int)(sightDistance * 12f));
            for (int i = 1; i <= sampleCount; i++)
            {
                V2 v = a + (target - a) * ((float)i / (float)sampleCount);
                int cell = Index(v);
                if (DynamicStructures.Owns(this, cell))
                    continue;
                if (
                    Inside((int)Math.Floor(v.X), (int)Math.Floor(v.Y))
                    && ProceduralCells != null
                    && ProceduralCells[cell]
                    && ProceduralHeight[cell] <= 0
                )
                    continue;
                if (!(V2.Distance(a, v) < 1.6f) && (At(v) == Tile.Wall || At(v) == Tile.LowCover))
                {
                    return v;
                }
            }

            return target;
        }

        public V2 LauncherImpact(V2 a, V2 target)
        {
            return ShotImpact(a, target, 0.75f);
        }

        public V2 BulletImpact(V2 a, V2 target, bool exposed)
        {
            return ShotImpact(a, target, exposed ? 1.2f : 0.75f);
        }

        private V2 ShotImpact(V2 a, V2 target, float targetRows)
        {
            V2 hit;
            float fraction;
            if (DynamicStructures.Ray(this, a, 1.55f, target, targetRows, out hit, out fraction))
                target = hit;
            float shotDistance = V2.Distance(a, target);
            int sampleCount = Math.Max(1, (int)Math.Ceiling(shotDistance * 16f));
            for (int i = 1; i <= sampleCount; i++)
            {
                float progress = (float)i / (float)sampleCount;
                V2 v = a + (target - a) * progress;
                if (DynamicStructures.Owns(this, Index(v)))
                    continue;
                Tile tile = At(v);
                float shotHeight = 1.55f + (targetRows - 1.55f) * progress;
                int blocked;
                switch (tile)
                {
                    case Tile.LowCover:
                        blocked = ((shotHeight <= StudioRules.Height(this, Index(v), 1f)) ? 1 : 0);
                        break;
                    default:
                        blocked = 0;
                        break;
                    case Tile.Wall:
                        blocked = 1;
                        break;
                }

                if (blocked != 0)
                {
                    return v;
                }
            }

            return target;
        }

        public StudioMap Studio;
        public readonly SceneryChanges SceneryChanges = new SceneryChanges();
        public readonly CoverDamageState CoverDamage = new CoverDamageState();

        public bool IsBush(int index)
        {
            var studio = StudioRules.At(this, index);
            if (studio != null)
                return !studio.Destroyed && studio.Asset.Kind >= 105 && studio.Asset.Kind <= 108;
            return index >= 0
                && index < Tiles.Length
                && Tiles[index] == Tile.LowCover
                && LevelProps != null
                && index < LevelProps.Length
                && LevelProps[index] >= 5
                && LevelProps[index] <= 8;
        }

        // Four visual stages. Collider and protection stay intact until destruction.
        public float BushVisualScale(int index)
        {
            if (!IsBush(index))
                return 1f;
            return .76f + .06f * (float)Math.Ceiling(CoverHealth[index] / 25f);
        }

        public bool NearCover(V2 p)
        {
            return GeometryQueries.Near(this, p);
        }

        internal bool NearCoverRaw(V2 p)
        {
            int cellX = (int)p.X;
            int cellY = (int)p.Y;
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (Inside(cellX + j, cellY + i) && !Walkable(cellX + j, cellY + i))
                    {
                        int cell = (cellY + i) * Columns + cellX + j;
                        if (ProceduralCells != null && ProceduralCells[cell] && ProceduralProtection[cell] <= 0)
                            continue;
                        return true;
                    }
                }
            }

            return false;
        }

        public V2 NearestOpen(V2 p)
        {
            V2 result = new V2(0.5f, 0.5f);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Tiles.Length; i++)
            {
                if (Walkable(i % Columns, i / Columns))
                {
                    V2 v = Center(i);
                    float candidateDistance = V2.Distance(v, p);
                    if (candidateDistance < bestDistance)
                    {
                        result = v;
                        bestDistance = candidateDistance;
                    }
                }
            }

            return result;
        }

        public void ConfigureMission(int level)
        {
            DynamicBuildings = null;
            ProceduralPictures = null;
            TerrainPicture = null;
            ProceduralCells = null;
            ProceduralProtection = null;
            ProceduralHeight = null;
            int levelIndex = level - 1;
            Columns = Layouts[levelIndex, 0];
            Rows = Layouts[levelIndex, 1];
            Theme = Layouts[levelIndex, 2];
            Studio = null;
            Tiles = new Tile[Columns * Rows];
            CoverHealth = new float[Tiles.Length];
            LevelProps = new byte[Tiles.Length];
            LowCoverStyle = new byte[Tiles.Length];
            CustomLevel = false;
            ExteriorMission = 0;
            BrokenCovers.Clear();
            EnemyEntry = new V2(Columns - 9.5f, Rows / 2f);
            Entry = new V2(3.5f, (float)Rows / 2f + 0.5f);
            Objective = new V2((float)Columns - 3.5f, (float)Rows / 2f + 0.5f);
            int middleRow = Rows / 2;
            for (int i = 0; i < Tiles.Length; i++)
            {
                CoverHealth[i] = 100f;
            }

            for (int j = 0; j < Columns; j++)
            {
                for (int k = middleRow - 1; k <= middleRow + 1; k++)
                {
                    Tiles[k * Columns + j] = Tile.Road;
                }
            }

            for (int l = 6; l < Columns - 5; l += 7)
            {
                int roadOffset = (l / 7 + level) % 3 - 1;
                Cover(l, middleRow - 4 + roadOffset, 2, horizontal: false);
                Cover(l + 1, middleRow + 4 + roadOffset, 2, horizontal: false);
            }

            if (Theme == 0)
            {
                for (int m = 10; m < Columns - 7; m += 11)
                {
                    Cover(m, 4 + (m + level) % 4, 3, horizontal: true);
                    Cover(m + 3, Rows - 7, 2, horizontal: true);
                }

                Wall(Columns - 13, 3, 4, horizontal: true);
                Wall(Columns - 13, 3, 3, horizontal: false);
            }
            else if (Theme == 1)
            {
                for (int n = 11; n < Columns - 6; n += 12)
                {
                    Cover(n, middleRow - 3, 2, horizontal: true);
                    Cover(n + 3, middleRow + 3, 2, horizontal: true);
                }

                Wall(Columns - 12, middleRow - 9, 5, horizontal: true);
                Wall(Columns - 12, middleRow - 9, 4, horizontal: false);
                Cover(Columns - 8, middleRow - 4, 3, horizontal: false);
                Cover(Columns - 8, middleRow + 3, 3, horizontal: false);
            }
            else if (Theme == 2)
            {
                for (int blockX = 12; blockX < Columns - 9; blockX += 11)
                {
                    for (int blockY = 4; blockY < Rows - 6; blockY += 11)
                    {
                        if (Math.Abs(blockY - middleRow) >= 4)
                        {
                            Wall(blockX, blockY, 5, horizontal: true);
                            Wall(blockX, blockY, 4, horizontal: false);
                            Cover(blockX + 5, blockY + 4, 2, horizontal: false);
                            Cover(blockX + 1, blockY + 6, 2, horizontal: true);
                        }
                    }
                }
            }
            else if (Theme == 3)
            {
                for (int coverX = 12; coverX < Columns - 8; coverX += 9)
                {
                    for (int coverY = 4; coverY < Rows - 3; coverY += 6)
                    {
                        if (Math.Abs(coverY - middleRow) >= 3)
                        {
                            Cover(coverX, coverY, 3, horizontal: true);
                            Cover(coverX + 3, coverY + 2, 2, horizontal: false);
                        }
                    }
                }

                Wall(Columns - 14, 3, 8, horizontal: true);
                Wall(Columns - 14, Rows - 4, 8, horizontal: true);
            }
            else if (Theme == 4)
            {
                for (int obstacleX = 9; obstacleX < Columns - 7; obstacleX += 9)
                {
                    Cover(obstacleX, 4 + (obstacleX + level) % 5, 3, horizontal: false);
                    Cover(obstacleX + 2, Rows - 9 + obstacleX % 3, 3, horizontal: true);
                }

                Wall(Columns - 13, 3, 5, horizontal: true);
                Cover(Columns - 11, middleRow - 5, 4, horizontal: false);
                Cover(Columns - 10, middleRow + 4, 3, horizontal: false);
            }
            else
            {
                for (int structureX = 12; structureX < Columns - 9; structureX += 10)
                {
                    for (int structureY = 3; structureY < Rows - 5; structureY += 9)
                    {
                        if (Math.Abs(structureY - middleRow) >= 4)
                        {
                            Wall(structureX, structureY, 4, horizontal: true);
                            Wall(structureX + 3, structureY, 3, horizontal: false);
                            Cover(structureX - 2, structureY + 4, 3, horizontal: false);
                        }
                    }
                }

                Cover(Columns - 9, middleRow - 5, 3, horizontal: false);
                Cover(Columns - 9, middleRow + 4, 3, horizontal: false);
            }

            for (int roadX = 0; roadX < Columns; roadX++)
            {
                for (int roadY = middleRow - 1; roadY <= middleRow + 1; roadY++)
                {
                    Tiles[roadY * Columns + roadX] = Tile.Road;
                }
            }

            StoryLayout(level, middleRow);
            for (int scatterIndex = 0; scatterIndex < Columns * Rows / 12; scatterIndex++)
            {
                int scatterX = (scatterIndex * 17 + level * 7) % Columns;
                int scatterY = (scatterIndex * 13 + level * 3) % Rows;
                if (scatterX > 5 && At(scatterX, scatterY) == Tile.Ground)
                {
                    Tiles[scatterY * Columns + scatterX] = Tile.Rubble;
                }
            }

            Revision++;
        }

        private void StoryLayout(int level, int mid)
        {
            int defenseX = Columns - 12;
            switch (level)
            {
                case 1:
                    Cover(defenseX, mid - 3, 3, horizontal: false);
                    Cover(defenseX, mid + 3, 3, horizontal: false);
                    break;
                case 2:
                    Cover(Columns / 2, 4, 6, horizontal: true);
                    Cover(Columns / 2 + 6, Rows - 6, 4, horizontal: true);
                    break;
                case 3:
                    Cover(Columns / 2, mid - 2, 3, horizontal: true);
                    Cover(Columns / 2 + 7, mid + 2, 3, horizontal: true);
                    break;
                case 4:
                    Wall(Columns / 2, mid - 2, 5, horizontal: false);
                    Cover(Columns / 2 + 3, mid - 6, 3, horizontal: true);
                    Cover(Columns / 2 + 3, mid + 6, 3, horizontal: true);
                    break;
                case 5:
                    Wall(defenseX + 1, mid - 6, 4, horizontal: true);
                    Cover(defenseX, mid - 3, 3, horizontal: false);
                    break;
                case 6:
                    Wall(defenseX, mid + 3, 5, horizontal: true);
                    Wall(defenseX + 4, mid + 3, 4, horizontal: false);
                    Cover(defenseX - 2, mid + 7, 3, horizontal: true);
                    break;
                case 7:
                {
                    for (int m = 14; m < Columns - 8; m += 10)
                    {
                        Cover(m, mid - 5, 4, horizontal: true);
                        Cover(m, mid + 5, 4, horizontal: true);
                    }

                    break;
                }

                case 8:
                {
                    for (int l = mid - 5; l <= mid + 5; l += 5)
                    {
                        Cover(defenseX, l, 3, horizontal: true);
                    }

                    break;
                }

                case 9:
                    Cover(Columns / 2, mid - 7, 4, horizontal: false);
                    Cover(Columns / 2 + 5, mid + 5, 3, horizontal: false);
                    break;
                case 10:
                    Cover(defenseX, mid - 5, 3, horizontal: false);
                    Cover(defenseX, mid + 4, 3, horizontal: false);
                    Wall(defenseX + 3, mid - 8, 4, horizontal: true);
                    break;
                case 11:
                    Wall(Columns / 2, mid - 3, 3, horizontal: false);
                    Wall(defenseX, mid + 2, 4, horizontal: false);
                    break;
                case 12:
                    Cover(Columns / 2 - 3, mid - 2, 3, horizontal: true);
                    Cover(Columns / 2 + 6, mid + 2, 3, horizontal: true);
                    break;
                case 13:
                    Cover(defenseX - 3, mid - 7, 4, horizontal: false);
                    Cover(defenseX - 3, mid + 4, 4, horizontal: false);
                    break;
                case 14:
                {
                    for (int k = mid - 6; k <= mid + 6; k += 4)
                    {
                        Cover(defenseX - 4, k, 3, horizontal: true);
                    }

                    break;
                }

                case 15:
                    Cover(Columns - 20, mid - 4, 3, horizontal: false);
                    Wall(Columns - 7, mid + 4, 5, horizontal: true);
                    break;
                case 16:
                    Wall(defenseX - 6, mid - 7, 5, horizontal: true);
                    Wall(defenseX - 1, mid + 5, 5, horizontal: true);
                    Cover(defenseX - 6, mid - 4, 2, horizontal: false);
                    Cover(defenseX - 1, mid + 2, 2, horizontal: false);
                    break;
                case 17:
                    Cover(Columns / 2, 4, 5, horizontal: true);
                    Cover(Columns / 2 + 8, Rows - 6, 5, horizontal: true);
                    break;
                case 18:
                {
                    for (int j = Columns - 24; j < Columns - 5; j += 12)
                    {
                        Cover(j, mid - 6, 4, horizontal: false);
                        Cover(j, mid + 3, 4, horizontal: false);
                    }

                    break;
                }

                case 19:
                    Wall(defenseX - 8, mid - 4, 3, horizontal: false);
                    Wall(defenseX, mid + 2, 4, horizontal: false);
                    Cover(defenseX + 4, mid - 3, 3, horizontal: true);
                    break;
                case 20:
                {
                    for (int i = Columns - 26; i < Columns - 5; i += 10)
                    {
                        Wall(i, mid - 8, 5, horizontal: true);
                        Wall(i, mid + 6, 5, horizontal: true);
                        Cover(i, mid - 4, 3, horizontal: false);
                        Cover(i, mid + 3, 3, horizontal: false);
                    }

                    break;
                }
            }

            for (int n = 0; n < Rows; n++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    if (column < 5 || V2.Distance(new V2((float)column + 0.5f, (float)n + 0.5f), Objective) < 2.5f)
                    {
                        Tiles[n * Columns + column] = ((Math.Abs(n - mid) <= 1) ? Tile.Road : Tile.Ground);
                    }
                }
            }
        }
    }
}
