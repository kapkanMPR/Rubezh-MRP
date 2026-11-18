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
        public float[] ProceduralProtection, ProceduralHeight;
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
        public readonly System.Collections.Generic.List<CoverBreakEvent> BrokenCovers = new System.Collections.Generic.List<CoverBreakEvent>();
        public bool CustomLevel;
        public int ExteriorMission; // Visual environment profile only.
        public SilentOrders.Core.V2 EnemyEntry = new SilentOrders.Core.V2(50.5f, 18.5f);
        public SilentOrders.Core.V2 Entry = new SilentOrders.Core.V2(3.5f, 18.5f);
        public float[] CoverHealth = new float[2160];
        public SilentOrders.Core.Tile[] Tiles = new SilentOrders.Core.Tile[2160];
        public SilentOrders.Core.V2 Objective = new SilentOrders.Core.V2(56.5f, 18.5f);
        public const float CaptureRadius = 2.1f;
        public static readonly int[, ] Layouts = new int[20, 3]
        {
            {
                36,
                24,
                1
            },
            {
                44,
                28,
                0
            },
            {
                48,
                24,
                1
            },
            {
                42,
                32,
                2
            },
            {
                46,
                28,
                4
            },
            {
                48,
                32,
                2
            },
            {
                56,
                26,
                3
            },
            {
                46,
                30,
                2
            },
            {
                54,
                30,
                0
            },
            {
                54,
                34,
                3
            },
            {
                58,
                30,
                2
            },
            {
                60,
                24,
                1
            },
            {
                54,
                36,
                4
            },
            {
                58,
                34,
                5
            },
            {
                62,
                28,
                1
            },
            {
                56,
                38,
                5
            },
            {
                64,
                32,
                0
            },
            {
                66,
                30,
                3
            },
            {
                60,
                38,
                2
            },
            {
                68,
                32,
                5
            }
        };
        public BattleMap()
        {
            for (int i = 0; i < 36; i++)
            {
                for (int j = 0; j < 60; j++)
                {
                    Tiles[i * 60 + j] = ((i >= 17 && i <= 19) ? SilentOrders.Core.Tile.Road : SilentOrders.Core.Tile.Ground);
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
                int num = (n * 17 + 11) % 60;
                int num2 = (n * 7 + 3) % 36;
                if (At(num, num2) == SilentOrders.Core.Tile.Ground)
                {
                    Tiles[num2 * 60 + num] = SilentOrders.Core.Tile.Rubble;
                }
            }
        }

        private void Wall(int x, int y, int n, bool horizontal)
        {
            Line(x, y, n, horizontal, SilentOrders.Core.Tile.Wall);
        }

        private void Cover(int x, int y, int n, bool horizontal)
        {
            Line(x, y, n, horizontal, SilentOrders.Core.Tile.LowCover);
        }

        private void Line(int x, int y, int n, bool horizontal, SilentOrders.Core.Tile t)
        {
            for (int i = 0; i < n; i++)
            {
                int num = x + (horizontal ? i : 0);
                int num2 = y + ((!horizontal) ? i : 0);
                if (Inside(num, num2))
                {
                    Tiles[num2 * Columns + num] = t;
                }
            }
        }

        public bool Inside(int x, int y)
        {
            return x >= 0 && y >= 0 && x < Columns && y < Rows;
        }

        public SilentOrders.Core.Tile At(int x, int y)
        {
            return Inside(x, y) ? Tiles[y * Columns + x] : SilentOrders.Core.Tile.Wall;
        }

        public SilentOrders.Core.Tile At(SilentOrders.Core.V2 p)
        {
            return At((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y));
        }

        public bool Walkable(int x, int y)
        {
            SilentOrders.Core.Tile tile = At(x, y);
            return tile != SilentOrders.Core.Tile.Wall && tile != SilentOrders.Core.Tile.LowCover && !StudioRules.Obstacle(this, y * Columns + x);
        }

        public bool Walkable(SilentOrders.Core.V2 p)
        {
            return Walkable((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y));
        }

        public int Index(SilentOrders.Core.V2 p)
        {
            return (int)System.Math.Floor(p.Y) * Columns + (int)System.Math.Floor(p.X);
        }

        public SilentOrders.Core.V2 Center(int i)
        {
            return new SilentOrders.Core.V2((float)(i % Columns) + 0.5f, (float)(i / Columns) + 0.5f);
        }

        public readonly GeometryQueryCache GeometryQueries = new GeometryQueryCache();
        public bool LineOfSight(V2 a, V2 b)
        {
            return GeometryQueries.Sight(this, a, b);
        }

        public bool LineOfSightRaw(SilentOrders.Core.V2 a, SilentOrders.Core.V2 b)
        {
            V2 hit;float fraction;if(DynamicStructures.Ray(this,a,1.5f,b,1.5f,out hit,out fraction))return false;
            int num = System.Math.Max(1, (int)System.Math.Ceiling(SilentOrders.Core.V2.Distance(a, b) * 8f));
            for (int i = 1; i < num; i++)
            {
                if (At(a + (b - a) * ((float)i / (float)num)) == SilentOrders.Core.Tile.Wall && !DynamicStructures.Owns(this,Index(a+(b-a)*((float)i/num))))
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

        public float ProtectionRaw(SilentOrders.Core.V2 p, SilentOrders.Core.V2 threat, bool corners = true)
        {
            SilentOrders.Core.V2 normalized = (threat - p).Normalized;
            for (float num = 0.25f; num <= 1.55f; num += 0.16f)
            {
                switch (At(p + normalized * num))
                {
                    case SilentOrders.Core.Tile.LowCover:
                        int ci = Index(p + normalized * num);
                        if(ProceduralCells != null && ci >= 0 && ci < ProceduralCells.Length && ProceduralCells[ci]) {
                            if(ProceduralProtection[ci] > 0)return ProceduralProtection[ci];
                            break;
                        }
                        return StudioRules.Medium(this, ci) ? .65f : .50f; // Durability changes lifetime, not the intact cover protection coefficient.
                    case SilentOrders.Core.Tile.Wall:
                        return 0.75f;
                }
            }

            return corners && SilentOrders.Core.WallCornerCover.At(this, p, threat) ? SilentOrders.Core.WallCornerCover.Protection : 0f;
        }

        public SilentOrders.Core.V2 RocketImpact(SilentOrders.Core.V2 a, SilentOrders.Core.V2 target)
        {
            V2 hit;float fraction;if(DynamicStructures.Ray(this,a,1.55f,target,1.55f,out hit,out fraction))target=hit;
            float num = SilentOrders.Core.V2.Distance(a, target);
            int num2 = System.Math.Max(1, (int)(num * 12f));
            for (int i = 1; i <= num2; i++)
            {
                SilentOrders.Core.V2 v = a + (target - a) * ((float)i / (float)num2);
                int cell = Index(v);
                if(DynamicStructures.Owns(this,cell))continue;
                if (Inside((int)Math.Floor(v.X), (int)Math.Floor(v.Y)) && ProceduralCells != null && ProceduralCells[cell] && ProceduralHeight[cell] <= 0)
                    continue;
                if (!(SilentOrders.Core.V2.Distance(a, v) < 1.6f) && (At(v) == SilentOrders.Core.Tile.Wall || At(v) == SilentOrders.Core.Tile.LowCover))
                {
                    return v;
                }
            }

            return target;
        }

        public SilentOrders.Core.V2 LauncherImpact(SilentOrders.Core.V2 a, SilentOrders.Core.V2 target)
        {
            return ShotImpact(a, target, 0.75f);
        }

        public SilentOrders.Core.V2 BulletImpact(SilentOrders.Core.V2 a, SilentOrders.Core.V2 target, bool exposed)
        {
            return ShotImpact(a, target, exposed ? 1.2f : 0.75f);
        }

        private SilentOrders.Core.V2 ShotImpact(SilentOrders.Core.V2 a, SilentOrders.Core.V2 target, float targetRows)
        {
            V2 hit;float fraction;if(DynamicStructures.Ray(this,a,1.55f,target,targetRows,out hit,out fraction))target=hit;
            float num = SilentOrders.Core.V2.Distance(a, target);
            int num2 = System.Math.Max(1, (int)System.Math.Ceiling(num * 16f));
            for (int i = 1; i <= num2; i++)
            {
                float num3 = (float)i / (float)num2;
                SilentOrders.Core.V2 v = a + (target - a) * num3;
                if(DynamicStructures.Owns(this,Index(v)))continue;
                SilentOrders.Core.Tile tile = At(v);
                float num4 = 1.55f + (targetRows - 1.55f) * num3;
                int num5;
                switch (tile)
                {
                    case SilentOrders.Core.Tile.LowCover:
                        num5 = ((num4 <= StudioRules.Height(this, Index(v), 1f)) ? 1 : 0);
                        break;
                    default:
                        num5 = 0;
                        break;
                    case SilentOrders.Core.Tile.Wall:
                        num5 = 1;
                        break;
                }

                if (num5 != 0)
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
            return index >= 0 && index < Tiles.Length && Tiles[index] == Tile.LowCover && LevelProps != null && index < LevelProps.Length && LevelProps[index] >= 5 && LevelProps[index] <= 8;
        }

        // Four visual stages. Collider and protection stay intact until destruction.
        public float BushVisualScale(int index)
        {
            if (!IsBush(index))
                return 1f;
            return .76f + .06f * (float)System.Math.Ceiling(CoverHealth[index] / 25f);
        }

        public bool NearCover(V2 p)
        {
            return GeometryQueries.Near(this, p);
        }

        internal bool NearCoverRaw(SilentOrders.Core.V2 p)
        {
            int num = (int)p.X;
            int num2 = (int)p.Y;
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (Inside(num + j, num2 + i) && !Walkable(num + j, num2 + i))
                    {
                        int cell = (num2 + i) * Columns + num + j;
                        if (ProceduralCells != null && ProceduralCells[cell] && ProceduralProtection[cell] <= 0)
                            continue;
                        return true;
                    }
                }
            }

            return false;
        }

        public SilentOrders.Core.V2 NearestOpen(SilentOrders.Core.V2 p)
        {
            SilentOrders.Core.V2 result = new SilentOrders.Core.V2(0.5f, 0.5f);
            float num = float.MaxValue;
            for (int i = 0; i < Tiles.Length; i++)
            {
                if (Walkable(i % Columns, i / Columns))
                {
                    SilentOrders.Core.V2 v = Center(i);
                    float num2 = SilentOrders.Core.V2.Distance(v, p);
                    if (num2 < num)
                    {
                        result = v;
                        num = num2;
                    }
                }
            }

            return result;
        }

        public void ConfigureMission(int level)
        {
            DynamicBuildings=null;ProceduralPictures=null;TerrainPicture=null;ProceduralCells=null;ProceduralProtection=null;ProceduralHeight=null;
            int num = level - 1;
            Columns = Layouts[num, 0];
            Rows = Layouts[num, 1];
            Theme = Layouts[num, 2];
            Studio = null;
            Tiles = new SilentOrders.Core.Tile[Columns * Rows];
            CoverHealth = new float[Tiles.Length];
            LevelProps = new byte[Tiles.Length];
            LowCoverStyle = new byte[Tiles.Length];
            CustomLevel = false;
            ExteriorMission = 0;
            BrokenCovers.Clear();
            EnemyEntry = new SilentOrders.Core.V2(Columns - 9.5f, Rows / 2f);
            Entry = new SilentOrders.Core.V2(3.5f, (float)Rows / 2f + 0.5f);
            Objective = new SilentOrders.Core.V2((float)Columns - 3.5f, (float)Rows / 2f + 0.5f);
            int num2 = Rows / 2;
            for (int i = 0; i < Tiles.Length; i++)
            {
                CoverHealth[i] = 100f;
            }

            for (int j = 0; j < Columns; j++)
            {
                for (int k = num2 - 1; k <= num2 + 1; k++)
                {
                    Tiles[k * Columns + j] = SilentOrders.Core.Tile.Road;
                }
            }

            for (int l = 6; l < Columns - 5; l += 7)
            {
                int num3 = (l / 7 + level) % 3 - 1;
                Cover(l, num2 - 4 + num3, 2, horizontal: false);
                Cover(l + 1, num2 + 4 + num3, 2, horizontal: false);
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
                    Cover(n, num2 - 3, 2, horizontal: true);
                    Cover(n + 3, num2 + 3, 2, horizontal: true);
                }

                Wall(Columns - 12, num2 - 9, 5, horizontal: true);
                Wall(Columns - 12, num2 - 9, 4, horizontal: false);
                Cover(Columns - 8, num2 - 4, 3, horizontal: false);
                Cover(Columns - 8, num2 + 3, 3, horizontal: false);
            }
            else if (Theme == 2)
            {
                for (int num4 = 12; num4 < Columns - 9; num4 += 11)
                {
                    for (int num5 = 4; num5 < Rows - 6; num5 += 11)
                    {
                        if (System.Math.Abs(num5 - num2) >= 4)
                        {
                            Wall(num4, num5, 5, horizontal: true);
                            Wall(num4, num5, 4, horizontal: false);
                            Cover(num4 + 5, num5 + 4, 2, horizontal: false);
                            Cover(num4 + 1, num5 + 6, 2, horizontal: true);
                        }
                    }
                }
            }
            else if (Theme == 3)
            {
                for (int num6 = 12; num6 < Columns - 8; num6 += 9)
                {
                    for (int num7 = 4; num7 < Rows - 3; num7 += 6)
                    {
                        if (System.Math.Abs(num7 - num2) >= 3)
                        {
                            Cover(num6, num7, 3, horizontal: true);
                            Cover(num6 + 3, num7 + 2, 2, horizontal: false);
                        }
                    }
                }

                Wall(Columns - 14, 3, 8, horizontal: true);
                Wall(Columns - 14, Rows - 4, 8, horizontal: true);
            }
            else if (Theme == 4)
            {
                for (int num8 = 9; num8 < Columns - 7; num8 += 9)
                {
                    Cover(num8, 4 + (num8 + level) % 5, 3, horizontal: false);
                    Cover(num8 + 2, Rows - 9 + num8 % 3, 3, horizontal: true);
                }

                Wall(Columns - 13, 3, 5, horizontal: true);
                Cover(Columns - 11, num2 - 5, 4, horizontal: false);
                Cover(Columns - 10, num2 + 4, 3, horizontal: false);
            }
            else
            {
                for (int num9 = 12; num9 < Columns - 9; num9 += 10)
                {
                    for (int num10 = 3; num10 < Rows - 5; num10 += 9)
                    {
                        if (System.Math.Abs(num10 - num2) >= 4)
                        {
                            Wall(num9, num10, 4, horizontal: true);
                            Wall(num9 + 3, num10, 3, horizontal: false);
                            Cover(num9 - 2, num10 + 4, 3, horizontal: false);
                        }
                    }
                }

                Cover(Columns - 9, num2 - 5, 3, horizontal: false);
                Cover(Columns - 9, num2 + 4, 3, horizontal: false);
            }

            for (int num11 = 0; num11 < Columns; num11++)
            {
                for (int num12 = num2 - 1; num12 <= num2 + 1; num12++)
                {
                    Tiles[num12 * Columns + num11] = SilentOrders.Core.Tile.Road;
                }
            }

            StoryLayout(level, num2);
            for (int num13 = 0; num13 < Columns * Rows / 12; num13++)
            {
                int num14 = (num13 * 17 + level * 7) % Columns;
                int num15 = (num13 * 13 + level * 3) % Rows;
                if (num14 > 5 && At(num14, num15) == SilentOrders.Core.Tile.Ground)
                {
                    Tiles[num15 * Columns + num14] = SilentOrders.Core.Tile.Rubble;
                }
            }

            Revision++;
        }

        private void StoryLayout(int level, int mid)
        {
            int num = Columns - 12;
            switch (level)
            {
                case 1:
                    Cover(num, mid - 3, 3, horizontal: false);
                    Cover(num, mid + 3, 3, horizontal: false);
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
                    Wall(num + 1, mid - 6, 4, horizontal: true);
                    Cover(num, mid - 3, 3, horizontal: false);
                    break;
                case 6:
                    Wall(num, mid + 3, 5, horizontal: true);
                    Wall(num + 4, mid + 3, 4, horizontal: false);
                    Cover(num - 2, mid + 7, 3, horizontal: true);
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
                        Cover(num, l, 3, horizontal: true);
                    }

                    break;
                }

                case 9:
                    Cover(Columns / 2, mid - 7, 4, horizontal: false);
                    Cover(Columns / 2 + 5, mid + 5, 3, horizontal: false);
                    break;
                case 10:
                    Cover(num, mid - 5, 3, horizontal: false);
                    Cover(num, mid + 4, 3, horizontal: false);
                    Wall(num + 3, mid - 8, 4, horizontal: true);
                    break;
                case 11:
                    Wall(Columns / 2, mid - 3, 3, horizontal: false);
                    Wall(num, mid + 2, 4, horizontal: false);
                    break;
                case 12:
                    Cover(Columns / 2 - 3, mid - 2, 3, horizontal: true);
                    Cover(Columns / 2 + 6, mid + 2, 3, horizontal: true);
                    break;
                case 13:
                    Cover(num - 3, mid - 7, 4, horizontal: false);
                    Cover(num - 3, mid + 4, 4, horizontal: false);
                    break;
                case 14:
                {
                    for (int k = mid - 6; k <= mid + 6; k += 4)
                    {
                        Cover(num - 4, k, 3, horizontal: true);
                    }

                    break;
                }

                case 15:
                    Cover(Columns - 20, mid - 4, 3, horizontal: false);
                    Wall(Columns - 7, mid + 4, 5, horizontal: true);
                    break;
                case 16:
                    Wall(num - 6, mid - 7, 5, horizontal: true);
                    Wall(num - 1, mid + 5, 5, horizontal: true);
                    Cover(num - 6, mid - 4, 2, horizontal: false);
                    Cover(num - 1, mid + 2, 2, horizontal: false);
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
                    Wall(num - 8, mid - 4, 3, horizontal: false);
                    Wall(num, mid + 2, 4, horizontal: false);
                    Cover(num + 4, mid - 3, 3, horizontal: true);
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
                for (int num2 = 0; num2 < Columns; num2++)
                {
                    if (num2 < 5 || SilentOrders.Core.V2.Distance(new SilentOrders.Core.V2((float)num2 + 0.5f, (float)n + 0.5f), Objective) < 2.5f)
                    {
                        Tiles[n * Columns + num2] = ((System.Math.Abs(n - mid) <= 1) ? SilentOrders.Core.Tile.Road : SilentOrders.Core.Tile.Ground);
                    }
                }
            }
        }
    }
}
