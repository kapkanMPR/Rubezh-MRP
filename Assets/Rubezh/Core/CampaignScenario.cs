using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public static class CampaignScenario
    {
        public static readonly int[, ] Missions = new int[20, 6]
        {
            {
                2,
                0,
                0,
                0,
                0,
                0
            },
            {
                3,
                0,
                0,
                0,
                0,
                0
            },
            {
                3,
                0,
                0,
                0,
                0,
                1
            },
            {
                4,
                0,
                0,
                0,
                0,
                1
            },
            {
                4,
                1,
                0,
                0,
                0,
                0
            },
            {
                4,
                0,
                1,
                0,
                0,
                2
            },
            {
                4,
                1,
                0,
                0,
                1,
                1
            },
            {
                4,
                0,
                1,
                1,
                0,
                2
            },
            {
                5,
                1,
                1,
                1,
                0,
                2
            },
            {
                5,
                1,
                1,
                1,
                1,
                3
            },
            {
                5,
                1,
                1,
                1,
                1,
                3
            },
            {
                5,
                1,
                1,
                1,
                1,
                5
            },
            {
                5,
                2,
                1,
                0,
                1,
                3
            },
            {
                5,
                1,
                1,
                2,
                0,
                3
            },
            {
                6,
                1,
                1,
                1,
                1,
                3
            },
            {
                6,
                2,
                1,
                1,
                0,
                4
            },
            {
                7,
                2,
                1,
                1,
                1,
                4
            },
            {
                8,
                2,
                1,
                2,
                1,
                5
            },
            {
                8,
                2,
                2,
                1,
                1,
                6
            },
            {
                10,
                2,
                2,
                2,
                2,
                6
            }
        };
        private static int TrainingCost(int n)
        {
            int result;
            switch (n)
            {
                default:
                    result = 450;
                    break;
                case 2:
                    result = 250;
                    break;
                case 1:
                    result = 100;
                    break;
                case 0:
                    result = 0;
                    break;
            }

            return result;
        }

        public static int SoldierCost(SilentOrders.Core.Soldier s)
        {
            int num = (s.Sniper ? 200 : ((s.Role == SilentOrders.Core.Role.MachineGunner) ? 200 : ((s.Role == SilentOrders.Core.Role.Medic) ? 200 : ((s.Role != SilentOrders.Core.Role.Grenadier) ? (s.SupplyPack ? 150 : 0) : ((s.LauncherKind == 1) ? 200 : 150)))));
            if (s.Underbarrel)
            {
                num += 50;
            }

            num += s.RigLevel * 50 + s.ArmorLevel * 100;
            int num2 = ((s.GrenadeType == 0) ? s.SmokeCharges : s.GrenadeCharges);
            int[, ] array = new int[3, 4]
            {
                {
                    0,
                    40,
                    70,
                    100
                },
                {
                    0,
                    50,
                    80,
                    110
                },
                {
                    0,
                    60,
                    110,
                    150
                }
            };
            return num + HandGrenades.Price(3, Math.Min(3, GrenadePockets.Count(s, 3))) + HandGrenades.Price(4, Math.Min(3, GrenadePockets.Count(s, 4))) + HandGrenades.Price(5, Math.Min(3, GrenadePockets.Count(s, 5))) + array[0, Math.Min(3, s.SmokeCharges)] + array[1, Math.Min(3, GrenadePockets.Count(s, 1))] + array[2, Math.Min(3, GrenadePockets.Count(s, 2) - (s.WeaponVariant == 2 ? s.LoadedUnderbarrel : 0))];
        }

        public static int Cost(SilentOrders.Core.BattleSimulation b)
        {
            int num = 0;
            for (int i = 0; i < 5; i++)
            {
                num += SoldierCost(b.Soldiers[i]);
            }

            return num;
        }

        public static bool Apply(SilentOrders.Core.BattleSimulation b, string spec)
        {
            if (b.Time != 0f || spec == null)
            {
                return false;
            }

            string[] array = spec.Split(',');
            if (array.Length != 5 && array.Length != 6 && array.Length != 7)
            {
                return false;
            }

            int[] array2 = new int[7];
            for (int i = 0; i < array.Length; i++)
            {
                if (!int.TryParse(array[i], out array2[i]))
                {
                    return false;
                }
            }

            if (array2[0] < 1 || array2[0] > 20 || array2[1] < 0 || array2[1] > 20 || array2[0] > array2[1] + 1)
            {
                return false;
            }

            for (int j = 2; j < 6; j++)
            {
                if (array2[j] < 0 || array2[j] > CampaignProgression.TrainingMax(array2[1]))
                {
                    return false;
                }
            }

            if (array2[6] < 0 || array2[6] > CombatTraining.Max(array2[1]))
                return false;
            if (Cost(b) - (array2[0] == 6 ? SoldierCost(b.Soldiers[4]) : 0) + TrainingCost(array2[2]) + TrainingCost(array2[3]) + TrainingCost(array2[4]) + TrainingCost(array2[5]) + CombatTraining.Price(array2[6]) > CampaignProgression.Budget(array2[1]))
            {
                return false;
            }

            foreach (var trained in b.Soldiers)
                if (trained.Side == Side.Green)
                    trained.CombatTrainingLevel = array2[6];
            b.CampaignLevel = array2[0];
            b.CampaignTotal = Missions[array2[0] - 1, 0];
            b.CampaignReserve = ((array2[0] >= 15) ? (b.CampaignTotal / 2) : 0);
            b.Map.ConfigureMission(array2[0]);
            for (int k = 0; k < 5; k++)
            {
                SilentOrders.Core.Soldier soldier = b.Soldiers[k];
                soldier.ReloadFactor = 1f - (float)array2[2] * 0.05f;
                soldier.AidSeconds = 3f - (float)array2[3] * 0.2f;
                MoraleDynamics.ConfigureTraining(b, soldier, array2[4]);
                soldier.AccuracyFactor = 1f + (float)array2[5] * 0.05f;
                soldier.Position = b.Map.NearestOpen(b.Map.Entry + new SilentOrders.Core.V2((k % 2 == 0) ? 0f : (-1.5f), (float)(k - 2) * 1.8f));
                soldier.OrderAnchor = (soldier.Destination = soldier.Position);
            }

            int num = b.CampaignTotal - b.CampaignReserve;
            System.Collections.Generic.List<int> list = EnemyKits(b.CampaignLevel);
            for (int l = 0; l < 5; l++)
            {
                if (l < num)
                {
                    b.Soldiers[l + 5] = Enemy(b, l + 5, l, list[l], reserve: false);
                    continue;
                }

                b.Soldiers[l + 5].Health = 0f;
                b.Soldiers[l + 5].Deployed = false;
            }

            b.Red.LeaderId = 5;
            RedCounterattack.Configure(b, num);
            b.Red.Initialized = false;
            b.Log(SilentOrders.Core.Side.Green, "Кампания: операция " + b.CampaignLevel + " / 20. Противников: " + b.CampaignTotal + ((b.CampaignReserve > 0) ? ", часть находится в резерве" : ""));
            return true;
        }

        public static System.Collections.Generic.List<int> EnemyKits(int level)
        {
            int num = level - 1;
            System.Collections.Generic.List<int> list = new System.Collections.Generic.List<int>
            {
                0
            };
            int[] array = new int[4]
            {
                Missions[num, 1],
                Missions[num, 2],
                Missions[num, 3],
                Missions[num, 4]
            };
            while (list.Count < Missions[num, 0])
            {
                bool flag = false;
                for (int i = 0; i < 4; i++)
                {
                    if (array[i] > 0 && list.Count < Missions[num, 0])
                    {
                        list.Add(i + 1);
                        array[i]--;
                        flag = true;
                    }
                }

                if (!flag)
                {
                    list.Add(0);
                }
            }

            return list;
        }

        public static SilentOrders.Core.Soldier Enemy(SilentOrders.Core.BattleSimulation b, int id, int index, int kit, bool reserve)
        {
            int rigLevel = ((b.CampaignLevel >= 10) ? 2 : ((b.CampaignLevel >= 5) ? 1 : 0));
            SilentOrders.Core.Soldier soldier = new SilentOrders.Core.Soldier(b.Rules);
            soldier.Id = id;
            soldier.Name = (reserve ? ("РЕЗЕРВ-" + (index + 1)) : (new string[5]
            {
                "ГРАНИТ",
                "СЕВЕР",
                "ДЫМ",
                "ШИП",
                "ЯСТРЕБ"
            }

            )[index % 5]);
            soldier.Side = SilentOrders.Core.Side.Red;
            int role;
            switch (kit)
            {
                default:
                    role = 4;
                    break;
                case 3:
                    role = 3;
                    break;
                case 2:
                    role = 2;
                    break;
                case 1:
                    role = 1;
                    break;
            }

            soldier.Role = (SilentOrders.Core.Role)role;
            soldier.RigLevel = rigLevel;
            soldier.SupplyPack = kit == 4;
            soldier.BuddyId = (reserve ? id : (5 + ((index == 0) ? 1 : 0)));
            soldier.Element = index % 2;
            soldier.Facing = new SilentOrders.Core.V2(-1f, 0f);
            SilentOrders.Core.Soldier soldier2 = soldier;
            soldier2.Position = b.Map.NearestOpen(b.Map.CustomLevel ? b.Map.EnemyEntry + new SilentOrders.Core.V2((index % 2) * 1.7f, (index % 5 - 2) * 1.8f) : new SilentOrders.Core.V2((float)b.Map.Columns - (reserve ? 4.5f : (((b.CampaignLevel == 18 || b.CampaignLevel == 20) ? 19.5f : 9.5f) + (float)(index % 2 * 2))), (float)b.Map.Rows / 2f - 6f + (float)(index % 5 * 3)));
            soldier2.OrderAnchor = (soldier2.Destination = soldier2.Position);
            soldier2.Ammo = soldier2.MagazineSize;
            soldier2.Magazines.Reset(soldier2, soldier2.ReserveCapacity);
            soldier2.Tourniquets = ((kit != 2) ? 1 : 5);
            soldier2.Rockets = ((kit == 3) ? 1 : 0);
            int num = Missions[b.CampaignLevel - 1, 5];
            int num2 = ((b.CampaignLevel < 15) ? 1 : ((Missions[b.CampaignLevel - 1, 0] + 1) / 2));
            soldier2.SmokeCharges = ((index == 0) ? System.Math.Min(3, num) : ((index == num2) ? System.Math.Max(0, num - 3) : 0));
            soldier2.PackRockets = ((kit == 4) ? 2 : 0);
            soldier2.PackBoxes = ((kit == 4) ? 1 : 0);
            soldier2.PackMagazines = ((kit == 4) ? 10 : 0);
            SilentOrders.Core.CampaignEnemyEquipment.Apply(b.CampaignLevel, index, soldier2);
            WeaponProgression.Assign(b, soldier2,
                b.EnemyWeaponMission > 0 ? b.EnemyWeaponMission : b.CampaignLevel, b.EnemyWeaponEndless);
            return soldier2;
        }

        public static void Update(SilentOrders.Core.BattleSimulation b)
        {
            if (b.CustomDefenders || b.CampaignReserve == 0)
            {
                return;
            }

            bool flag = false;
            foreach (SilentOrders.Core.Soldier soldier2 in b.Soldiers)
            {
                if (soldier2.Side == SilentOrders.Core.Side.Green && soldier2.Alive && SilentOrders.Core.V2.Distance(soldier2.Position, b.Map.Objective) < 12f)
                {
                    flag = true;
                }
            }

            if (b.Alive(SilentOrders.Core.Side.Red) <= 1 || flag || !(b.Time < 110f))
            {
                System.Collections.Generic.List<int> list = EnemyKits(b.CampaignLevel);
                int num = b.CampaignTotal - b.CampaignReserve;
                for (int i = num; i < b.CampaignTotal; i++)
                {
                    int count = b.Soldiers.Count;
                    SilentOrders.Core.Soldier soldier = Enemy(b, count, i, list[i], reserve: true);
                    soldier.BuddyId = count;
                    b.Soldiers.Add(soldier);
                }

                b.CampaignReserve = 0;
                RedReserveDeployment.OnArrival(b);
                b.Red.Initialized = false;
                b.Red.Defense.Initialized = false;
                b.Red.Defense.Active = false;
                b.Red.Defense.Movers.Clear();
                b.Red.NextPlan = 0f;
                b.Log(SilentOrders.Core.Side.Red, "Резерв вступает в бой на последнем рубеже.");
            }
        }
    }
}
