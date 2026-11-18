using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CampaignEnemyEquipment
    {
        public static readonly int[] OriginalBudgets = new int[20]
        {
            0,
            0,
            40,
            40,
            400,
            470,
            590,
            620,
            870,
            1300,
            1300,
            1370,
            1350,
            1300,
            1400,
            1490,
            1740,
            2020,
            2100,
            2600
        };
        public static readonly int[][][] Kits = new int[20][][]
        {
            new int[2][]
            {
                new int[5],
                new int[5]
            },
            new int[3][]
            {
                new int[5],
                new int[5],
                new int[5]
            },
            new int[3][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    1,
                    0
                },
                new int[5],
                new int[5]
            },
            new int[4][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    1,
                    0
                },
                new int[5],
                new int[5],
                new int[5]
            },
            new int[4][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    0,
                    1,
                    1,
                    1
                },
                new int[5]
            },
            new int[4][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    1,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    0,
                    1,
                    2,
                    1
                },
                new int[5]
            },
            new int[4][]
            {
                new int[5]
                {
                    0,
                    0,
                    1,
                    2,
                    0
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    0,
                    0,
                    0
                },
                new int[5]
                {
                    3,
                    0,
                    2,
                    1,
                    0
                }
            },
            new int[4][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    2,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    0,
                    1,
                    1,
                    0
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    1,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    0,
                    1,
                    2,
                    0
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    2
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    3,
                    0,
                    0,
                    2
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    3,
                    3,
                    2,
                    3,
                    1
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    2
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    2,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    1,
                    1,
                    1
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    3,
                    3,
                    2,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                }
            },
            new int[5][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    3,
                    1,
                    2,
                    3,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                }
            },
            new int[6][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    1,
                    1,
                    1
                },
                new int[5]
                {
                    5,
                    0,
                    0,
                    0,
                    1
                }
            },
            new int[6][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    1,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    1,
                    1,
                    1
                },
                new int[5]
                {
                    3,
                    0,
                    2,
                    3,
                    1
                }
            },
            new int[7][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    0,
                    1,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    1,
                    1,
                    1
                },
                new int[5]
                {
                    5,
                    0,
                    0,
                    0,
                    1
                }
            },
            new int[8][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    0,
                    1,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    1,
                    2,
                    2
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    3,
                    0,
                    2,
                    3,
                    1
                }
            },
            new int[8][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    1,
                    1,
                    2
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    5,
                    0,
                    0,
                    0,
                    1
                }
            },
            new int[10][]
            {
                new int[5]
                {
                    0,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    1,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    3,
                    3,
                    2,
                    3,
                    1
                },
                new int[5]
                {
                    1,
                    0,
                    0,
                    3,
                    1
                },
                new int[5]
                {
                    0,
                    2,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    4,
                    0,
                    0,
                    1
                },
                new int[5]
                {
                    0,
                    3,
                    1,
                    1,
                    1
                },
                new int[5]
                {
                    5,
                    0,
                    0,
                    0,
                    1
                }
            }
        };
        public static void Apply(int level, int index, SilentOrders.Core.Soldier s)
        {
            ApplyRaw(level, index, s);
            var roster = new System.Collections.Generic.List<SilentOrders.Core.Soldier>();
            for (int i = 0; i < Kits[level - 1].Length; i++)
            {
                var member = new SilentOrders.Core.Soldier(s.Rules);
                ApplyRaw(level, i, member);
                roster.Add(member);
            }

            SilentOrders.Core.EnemyInventory.Configure(s, roster);
        }

        private static void ApplyRaw(int level, int index, SilentOrders.Core.Soldier s)
        {
            int[] array = Kits[level - 1][index];
            s.ArmorLevel = 0; // Current campaign roster; custom defenders keep their own Kit armor.
            s.WeaponVariant = array[0];
            s.Role = ((array[0] == 5) ? SilentOrders.Core.Role.Sniper : ((array[0] == 1) ? SilentOrders.Core.Role.MachineGunner : ((array[1] == 2) ? SilentOrders.Core.Role.Medic : ((array[1] == 1 || array[1] == 4) ? SilentOrders.Core.Role.Grenadier : SilentOrders.Core.Role.Rifleman))));
            s.LauncherKind = ((array[1] == 4) ? 1 : 0);
            s.SupplyPack = array[1] == 3;
            s.GrenadeType = array[2];
            s.SmokeCharges = ((array[2] == 0) ? array[3] : 0);
            s.GrenadeCharges = ((array[2] != 0) ? array[3] : 0);
            s.RigLevel = array[4];
            GrenadePockets.Equip(s, array[2], Math.Min(array[3], Math.Min(3, GrenadePockets.Limit(s.RigLevel))));
            s.Ammo = s.MagazineSize;
            s.Magazines.Reset(s, s.ReserveCapacity);
            s.Tourniquets = ((array[1] != 2) ? 1 : 5);
            s.Rockets = ((array[1] == 1 || array[1] == 4) ? 1 : 0);
            s.PackRockets = (s.SupplyPack ? 2 : 0);
            s.PackBoxes = (s.SupplyPack ? 1 : 0);
            s.PackMagazines = (s.SupplyPack ? 10 : 0);
        }
    }
}
