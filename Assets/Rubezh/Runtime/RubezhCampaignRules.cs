using System;
using System.Linq;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        bool WoundedSlot(int i)
        {
            return campaignMode && mission == 6 && i == 4;
        }

        int BagLimit(Kit k)
        {
            return campaignMode && mission == 16 ? 50 : CampaignProgression.BagCapacity(k.extra);
        }

        int ActiveCost
        {
            get
            {
                return draft.Cost - (WoundedSlot(4) ? draft.kits[4].Cost : 0);
            }
        }

        bool Unlocked(int after)
        {
            return !campaignMode || progress.completed >= after;
        }

        bool WeaponOpen(int w)
        {
            return Unlocked(CampaignUnlocks.Required("weapon", w));
        }

        bool ExtraOpen(int e)
        {
            return Unlocked(CampaignUnlocks.Required("extra", e));
        }

        bool BagOpen(int i)
        {
            return Unlocked(CampaignUnlocks.Required("bag", i));
        }

        bool GrenadeOpen(int i)
        {
            return Unlocked(CampaignUnlocks.Required("grenade", i));
        }

        bool ArmorOpen(int i)
        {
            return Unlocked(CampaignUnlocks.Required("armor", i));
        }

        bool RigOpen(int i)
        {
            return Unlocked(CampaignUnlocks.Required("rig", i));
        }

        void EquipExtra(Kit k, int extra)
        {
            k.SetExtra(extra);
            for (int i = 0; i < k.bag.Length; i++)
                if (!BagOpen(i))
                    k.bag[i] = 0;
            if (k.BagCost > BagLimit(k))
            {
                k.bag = new int[12];
                if (extra == 2 && BagOpen(6))
                    k.bag[6] = 1;
                else if (extra == 3 && BagOpen(0))
                    k.bag[0] = Math.Min(6, BagLimit(k) / Kit.Prices[0]);
            }
        }

        string ExtraDescription(Kit k)
        {
            var text = UiCatalog.ExtraDescriptions[k.extra].Replace("150", BagLimit(k).ToString()).Replace("200", BagLimit(k).ToString());
            if (k.extra == 2 && !Unlocked(6))
                return "Медицинская сумка: пока доступны только жгуты. Вместимость " + BagLimit(k) + " очков. Медик помогает раненым и держится за передней линией.";
            return text;
        }

        string LoadoutProblem()
        {
            if (campaignMode && (draft.training.Take(4).Any(t => t > CampaignProgression.TrainingMax(progress.completed)) || draft.training[4] > CombatTraining.Max(progress.completed) || draft.training[4] < progress.training[4]))
                return "Этот уровень подготовки ещё не открыт";
            for (int i = 0; i < 5; i++)
            {
                if (WoundedSlot(i))
                    continue;
                var k = Kits[i];
                if (!SquadLoadout.ClassKit(k.Class(i), k.weapon, k.extra))
                    return Names[i] + ": снаряжение не подходит классу";
                if (campaignMode && k.Class(i) == Role.Medic && k.classTraining > draft.training[1])
                    return "Для лечения нужен соответствующий уровень перевязки";
                if (!k.PocketsValid)
                    return Names[i] + ": превышен лимит гранат разгрузки";
                if (k.count3 > 0 && !GrenadeOpen(k.grenade3))
                    return Names[i] + ": третий тип гранат ещё не открыт";
                if (k.count2 > 0 && !GrenadeOpen(k.grenade2))
                    return Names[i] + ": второй тип гранат ещё не открыт";
                if (!WeaponOpen(k.weapon) || !ExtraOpen(k.extra) || !GrenadeOpen(k.grenade) || !RigOpen(k.rig) || !ArmorOpen(k.armor))
                    return "Снимите ещё не открытое снаряжение: " + Names[i];
                for (int j = 0; j < k.bag.Length; j++)
                    if (k.bag[j] > 0 && !BagOpen(j))
                        return "Недоступный запас в рюкзаке: " + Names[i];
                if (k.BagCost > BagLimit(k))
                    return Names[i] + ": разгрузите рюкзак до " + BagLimit(k) + " очков";
            }

            return "";
        }

        string MissionEvent
        {
            get
            {
                return !campaignMode ? "" : mission == 6 ? "ТИХИЙ ранен. В бой идут четверо; его комплект не расходует бюджет." : mission == 16 ? "Припасы остались в машине: каждый рюкзак вмещает 50 очков." : "";
            }
        }

        void ChooseWeapon(Kit k)
        {
            var ids = WeaponIds.Where(i => WeaponOpen(i) && SquadLoadout.ClassKit(k.Class(kitSelected), i, k.extra)).ToArray();
            Pick("Основное оружие", ids.Select(i => WeaponName(i) + " · " + Price(Kit.WeaponPrice(i))).ToArray(), i => k.SetWeapon(ids[i]));
        }

        void ChooseExtra(Kit k)
        {
            var ids = new[]
            {
                0,
                1,
                2,
                3
            }.Where(i => ExtraOpen(i) && SquadLoadout.ClassKit(k.Class(kitSelected), k.weapon, i)).ToArray();
            Pick("Дополнительное оборудование", ids.Select(i => (i == 1 ? "Гранатомёт · от " : Extras[i] + " · ") + Price(Kit.ExtraPrice(i))).ToArray(), i => EquipExtra(k, ids[i]));
        }

        void ChooseLauncher(Kit k)
        {
            var ids = new[]
            {
                1,
                4
            }.Where(i => ExtraOpen(i) && SquadLoadout.ClassKit(k.Class(kitSelected), k.weapon, i)).ToArray();
            Pick("Гранатомёт", ids.Select(i => Extras[i] + " · " + Price(Kit.ExtraPrice(i))).ToArray(), i => EquipExtra(k, ids[i]));
        }

        void ChooseGrenade(Kit k, int slot = 0)
        {
            var ids = Enumerable.Range(0, 6).Where(i => (i != 2 || k.weapon == 2) && GrenadeOpen(i) && (slot == 0 || i != k.grenade) && (slot != 2 || k.count2 == 0 || i != k.grenade2) && (slot != 1 || k.count3 == 0 || i != k.grenade3)).ToArray();
            Pick("Гранаты", ids.Select(i => Grenades[i]).ToArray(), i =>
            {
                if (slot == 2)
                    k.grenade3 = ids[i];
                else if (slot == 1)
                    k.grenade2 = ids[i];
                else
                    k.grenade = ids[i];
                k.NormalizePockets();
            });
        }

        void ChooseArmor(Kit k)
        {
            var ids = Enumerable.Range(0, 3).Where(ArmorOpen).ToArray();
            Pick("Бронежилет", ids.Select(i => BodyArmor.Names[i] + " · " + Price(i * 100)).ToArray(), i => k.armor = ids[i]);
        }

        void ChooseRig(Kit k)
        {
            var ids = Enumerable.Range(0, 3).Where(RigOpen).ToArray();
            Pick("Разгрузка", ids.Select(i => "Уровень " + (i + 1) + " · " + Price(i * 50) + " · " + UiCatalog.Ammo(k.weapon, i)).ToArray(), i => k.SetRig(ids[i]));
        }

        static string UnlockNotice(int level)
        {
            var names = CampaignUnlocks.Between(level - 1, level).Select(i => i.Name);
            return (level == 6 ? "ТИХИЙ возвращается в строй. " : "") + (names.Any() ? "Открыто: " + string.Join(", ", names) : "");
        }
    }
}
