using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    [Serializable]
    public class CampUnit
    {
        public int headId,
            lightId,
            familyId,
            heavyId;
        public string name,
            fighterId;
        public int role,
            weapon,
            extra,
            extra2,
            rig,
            armor;
        public bool band,
            westernWeapon,
            noWeapon;
        public string[] pockets,
            bag,
            skills;
        public float aim,
            reload;

        public Kit Base()
        {
            if (
                (role == 2 || role == 6) && extra != 0
                || role == 6 && armor == 2
                || extra == 2 && role != 3
                || role != 1 && (extra == 3 && role != 5 || extra == 4 && role != 4)
                || role == 4 && extra != 0 && extra != 1 && extra != 4
                || extra2 != 0 && (role != 4 || extra2 != 1 && extra2 != 4)
            )
                throw new ArgumentException("Снаряжение запрещено классу бойца");
            var k = new Kit
            {
                classCode = role,
                weapon = weapon,
                extra = extra,
                rig = Math.Max(0, rig),
                armor = Math.Max(0, armor),
                personalTourniquet = band,
            };
            string stem =
                role == 1 ? "optic"
                : role == 3 ? "medheal"
                : role == 6 ? "vision"
                : null;
            if (stem != null)
                for (int i = 1; i <= 3; i++)
                    if (skills.Contains(stem + i))
                        k.classTraining = i;
            return k;
        }

        public void Apply(BattleSimulation b, Soldier s)
        {
            s.WesternGun = westernWeapon;
            s.ArmorLevel = armor;
            s.RigLevel = rig;
            s.Magazines.Reset(s, s.ReserveCapacity);
            if (noWeapon)
            {
                s.Deployed = false;
                s.Health = 0;
                s.Ammo = 0;
                s.Reserve = 0;
            }
            s.Look = new Rubezh.GreenLook
            {
                headId = headId,
                lightId = lightId,
                familyId = familyId,
                heavyId = heavyId,
            };
            Rubezh.GreenLook.Ensure(s);
            int morale = skills.Count(x => x == "morale" || x == "morale2" || x == "morale3");
            MoraleDynamics.ConfigureTraining(b, s, morale);
            s.AidSeconds = 3f - .2f * skills.Count(x => x == "aid" || x == "aid2" || x == "aid3");
            s.Name = name;
            s.CampSkills = new HashSet<string>(skills);
            s.AccuracyFactor *= 1 + aim;
            s.ReloadFactor *= 1 - reload;
            var g = pockets
                .GroupBy(x => Array.IndexOf(new[] { "smoke", "rgd", "vog", "f1", "rgn", "rgo" }, x))
                .ToArray();
            if (g.Any(x => x.Key < 0) || pockets.Length > GrenadePockets.Limit(rig))
                throw new ArgumentException("Некорректные ячейки разгрузки");
            var rounds = g.Where(x => x.Key > 0).ToArray();
            int[] t = new int[3],
                n = new int[3];
            for (int i = 0; i < Math.Min(3, rounds.Length); i++)
            {
                t[i] = rounds[i].Key;
                n[i] = rounds[i].Count();
            }

            GrenadePockets.Equip(s, t[0], n[0], t[1], n[1], t[2], n[2]);
            s.SmokeCharges = g.Where(x => x.Key == 0).Sum(x => x.Count());
            if (s.SmokeCharges > 0)
                s.PocketMask |= 1;
            if (rounds.Length > 3)
            {
                s.CampReserveType = rounds[3].Key;
                s.CampReserveRounds = rounds[3].Count();
            }

            if (extra2 != 0)
            {
                if (s.Role != Role.Grenadier || (extra2 != 1 && extra2 != 4))
                    throw new ArgumentException("Второй дополнительный слот — только гранатомёт гранатомётчика");
                s.ReserveLauncherKind = extra2 == 4 ? 1 : 0;
                LauncherLogistics.ReadyReserve(s);
            }
            foreach (var x in bag)
                switch (x)
                {
                    case "rpg":
                        s.PackRockets++;
                        break;
                    case "rshg":
                        s.PackRshg++;
                        break;
                    case "kit":
                        s.Medkits++;
                        break;
                    case "band":
                        s.Tourniquets++;
                        break;
                    case "ammo":
                        s.PackMagazines++;
                        break;
                    case "belt":
                        s.PackBoxes++;
                        break;
                    case "smoke":
                        s.PackSmokes++;
                        break;
                    case "rgd":
                        s.PackGrenades++;
                        break;
                    case "vog":
                        s.PackUnderbarrel++;
                        break;
                    case "f1":
                        s.PackF1++;
                        break;
                    case "rgn":
                        s.PackRGN++;
                        break;
                    case "rgo":
                        s.PackRGO++;
                        break;
                }
        }
    }
}
