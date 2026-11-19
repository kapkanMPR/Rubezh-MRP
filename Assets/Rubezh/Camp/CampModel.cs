using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

// The native camp is the sole authority for career state. Field names intentionally
// match the 0.12.81 save, so migration never rebuilds fighters from templates.
namespace Rubezh.Camp
{
    [Serializable]
    public sealed class Item
    {
        public string id,
            name,
            type,
            img,
            stats,
            desc,
            role,
            rarity;
        public int unlock,
            price,
            capacity;
        public string[][] fields;
    }

    [Serializable]
    public sealed class Unit
    {
        public string id,
            name,
            role,
            rarity,
            unit,
            trait;
    }

    [Serializable]
    public sealed class Offer
    {
        public string id;
        public int unlock,
            price;
    }

    [Serializable]
    public sealed class Names
    {
        public string role;
        public string[] names;
    }

    [Serializable]
    public sealed class Effect
    {
        public string stat,
            unit;
        public float delta;
        public int rank;
    }

    [Serializable]
    public sealed class Skill
    {
        public string id,
            name,
            desc,
            role;
        public int level;
        public string[] pre;
        public float x,
            y,
            col,
            row;
        public Effect effect;
    }

    [Serializable]
    public sealed class Mission
    {
        public string name,
            brief;
    }

    [Serializable]
    public sealed class WarMap
    {
        public string name,
            terrain;
        public int n,
            w,
            h,
            red,
            waves;
        public float reserve;
    }

    [Serializable]
    public sealed class Catalog
    {
        public Item[] items;
        public Unit[] units;
        public Offer[] offers;
        public Skill[] skills;
        public Names[] callsigns;
        public Mission[] missions;
        public WarMap[] war;

        public Item Item(string id)
        {
            return items.FirstOrDefault(x => x.id == id);
        }

        public Unit Template(string id)
        {
            return units.FirstOrDefault(x => x.id == id);
        }

        public Skill Skill(string id)
        {
            return skills.FirstOrDefault(x => x.id == id);
        }
    }

    [Serializable]
    public sealed class Look
    {
        public int headId,
            lightId,
            familyId,
            heavyId;
    }

    [Serializable]
    public sealed class Gear
    {
        public string weapon,
            armor,
            rig,
            extra,
            extra2,
            tourniquet;
        public List<string> rigSlots = new List<string>(),
            bag = new List<string>();

        public IEnumerable<string> All()
        {
            return new[] { weapon, armor, rig, extra, extra2, tourniquet }.Concat(rigSlots).Concat(bag);
        }

        public string Get(string slot)
        {
            switch (slot)
            {
                case "weapon":
                    return weapon;
                case "armor":
                    return armor;
                case "rig":
                    return rig;
                case "extra2":
                    return extra2;
                case "extra":
                    return extra;
                case "tourniquet":
                    return tourniquet;
                default:
                    throw new ArgumentException("Недопустимый слот");
            }
        }

        public void Set(string slot, string id)
        {
            switch (slot)
            {
                case "weapon":
                    weapon = id;
                    break;
                case "armor":
                    armor = id;
                    break;
                case "rig":
                    rig = id;
                    break;
                case "extra2":
                    extra2 = id;
                    break;
                case "extra":
                    extra = id;
                    break;
                case "tourniquet":
                    tourniquet = id;
                    break;
                default:
                    throw new ArgumentException("Недопустимый слот");
            }
        }
    }

    [Serializable]
    public sealed class Person
    {
        public int level = 1,
            points,
            credit;
        public Dictionary<string, int> skills = new Dictionary<string, int>();
    }

    [Serializable]
    public sealed class Career
    {
        public int battleXpVersion;
        public int xp,
            manuals,
            levelBooks,
            freeXp,
            treeVersion = 4,
            xpCurve = 2,
            fighterCurve = 2;
        public Dictionary<string, Person> people = new Dictionary<string, Person>();
    }

    [Serializable]
    public sealed class Recruit
    {
        public string template,
            name;
    }

    [Serializable]
    public sealed class Arrival
    {
        public string template,
            name;
        public int level;
        public bool advanced;
        public Look look;
    }

    [Serializable]
    public sealed class Shipment
    {
        public List<string> gear = new List<string>();
        public List<Arrival> fighters = new List<Arrival>();
        public bool book;
    }

    [Serializable]
    public sealed class RewardView
    {
        public string key,
            summary,
            phase = "gear";
    }

    [Serializable]
    public sealed class Operation
    {
        public string mode = "war";
        public int block = 1,
            mission = 1;
    }

    [Serializable]
    public sealed class Formation
    {
        public bool quietWork;
        public int pairing,
            width = 1,
            depth = 1;
        public int[] pairSlots =  { -1, -1, -1, -1 },
            positions =  { 0, 0, 0, 0, 0 };
    }

    [Serializable]
    public sealed class State
    {
        public List<WeaponInstance> weaponInstances = new List<WeaponInstance>();
        public List<LootReceipt> lootReceipts = new List<LootReceipt>();
        public int finiteEquipmentVersion;
        public bool hideIssued;
        public int version = 1,
            story,
            nextRecruit;
        public string nickname = "Kapkan";
        public int avatarHead = 82;
        public List<int> avatarHeads = new List<int>();
        public List<CampSquad> squads = new List<CampSquad>();
        public string selectedSquad;
        public bool unlimited,
            unlockAll,
            motion = true;
        public double balance;
        public List<string> completed = new List<string>(),
            active = new List<string> { "kedr", "s1", "s2", "s3", "s4" },
            usedNames = new List<string>();
        public Dictionary<string, int> inventory = new Dictionary<string, int>();
        public Dictionary<string, Gear> gear = new Dictionary<string, Gear>();
        public List<int> seenHeads = new List<int>();
        public Dictionary<string, Look> looks = new Dictionary<string, Look>();
        public Dictionary<string, Recruit> recruits = new Dictionary<string, Recruit>();
        public Dictionary<string, Shipment> caseHistory = new Dictionary<string, Shipment>();
        public List<Arrival> pendingRecruits = new List<Arrival>();
        public Career career = new Career();
        public Operation operation = new Operation();
        public Formation formation = new Formation();
        public RewardView rewardView;
        public string lastCampReceipt;
        public RaidProgress raid = new RaidProgress();
        public int[] training = { 0, 0, 0, 0, 0 };
    }

    public sealed partial class CampModel
    {
        public readonly Catalog Data;
        public State S { get; private set; }

        readonly Func<double> random;

        public CampModel(Catalog catalog, State state = null, Func<double> rng = null)
        {
            Data = catalog ?? throw new ArgumentNullException(nameof(catalog));
            LootCrates.ApplyRarity(Data);
            random = rng ?? new Random().NextDouble;
            S = state ?? Fresh();
            Normalize();
        }

        public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 64,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        public string Serialize()
        {
            RememberAvatarHeads();
            CaptureSquad();
            return JsonConvert.SerializeObject(S, JsonSettings);
        }

        public static State Decode(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 8000000)
                throw new ArgumentException("Некорректный файл прогресса");
            var raw = Newtonsoft.Json.Linq.JObject.Parse(json);
            var s = JsonConvert.DeserializeObject<State>(json, JsonSettings);
            if (raw["career"] == null || raw["career"].Type == Newtonsoft.Json.Linq.JTokenType.Null)
                s.career = null;
            else
            {
                var c = raw["career"];
                if (c["treeVersion"] == null)
                    s.career.treeVersion = 0;
                if (c["xpCurve"] == null)
                    s.career.xpCurve = 0;
                if (c["fighterCurve"] == null)
                    s.career.fighterCurve = 0;
            }

            if (raw["caseHistory"] == null)
                s.caseHistory = null;
            if (
                s == null
                || s.version != 1
                || s.gear == null
                || !s.gear.ContainsKey("kedr")
                || s.active == null
                || s.active.Count != 5
                || (s.active[0] != null && s.active[0] != "kedr")
                || s.active.Where(id => id != null).Distinct().Count() != s.active.Count(id => id != null)
                || s.active.Any(id => id != null && !s.gear.ContainsKey(id))
            )
                throw new ArgumentException("Сохранение лагеря повреждено или имеет неизвестную версию");
            return s;
        }

        public void Restore(State state)
        {
            S = state;
            Normalize();
        }

        State Fresh()
        {
            var s = new State();
            foreach (
                var u in Data.units.Where(u =>
                    u.id != "sn-morok"
                    && u.id != "sn-manul"
                    && u.id != "med-dobry"
                    && u.id != "med-hirurg"
                    && u.id != "mg-medved"
                    && u.id != "mg-bury"
                    && u.id != "rpg-pepel"
                    && u.id != "rpg-fugas"
                    && !Data.offers.Any(o => o.id == u.id)
                )
            )
            {
                s.gear[u.id] = new Gear();
                s.career.people[u.id] = new Person();
            }

            s.finiteEquipmentVersion = 1;
            s.inventory["ak"] = 5;
            foreach (var id in s.active.Where(id => id != null && s.gear.ContainsKey(id)).Take(5))
                s.gear[id].weapon = "ak";
            return s;
        }

        public void Reset()
        {
            bool motion = S.motion;
            S = Fresh();
            S.motion = motion;
            Normalize();
        }

        public Unit Soldier(string id)
        {
            Recruit r;
            S.recruits.TryGetValue(id, out r);
            var u = Data.Template(r == null ? id : r.template);
            if (u == null)
                throw new ArgumentException("Боец не найден: " + id);
            return new Unit
            {
                id = id,
                name = u.name,
                role = u.role,
                rarity = u.rarity,
                unit = u.unit,
                trait = u.trait,
            };
        }

        public IEnumerable<Unit> Roster()
        {
            return S.gear.Keys.Select(Soldier);
        }

        public static bool Personal(string id)
        {
            return false; // All equipment is now finite stock.
        }

        public int Used(string id)
        {
            return Personal(id) ? 0 : S.gear.Values.Sum(g => g.All().Count(x => x == id));
        }

        public int Owned(string id)
        {
            int n;
            return S.inventory.TryGetValue(id, out n) ? n : 0;
        }

        public int Free(string id)
        {
            return Personal(id) ? int.MaxValue : Owned(id) - Used(id);
        }

        public bool Unlocked(Item i)
        {
            return i != null && S.story >= i.unlock;
        }

        public bool Allowed(Unit u, Item i)
        {
            if (u == null || i == null)
                return false;
            if (i.id == "medbag")
                return u.role == "Медик";
            if (u.role == "Командир")
                return true;
            if (i.type == "extra" && (u.role == "Снайпер" || u.role == "Пулемётчик"))
                return false;
            if (i.type == "extra" && u.role == "Гранатомётчик")
                return i.id == "rpg" || i.id == "rshg";
            if (i.id == "a3" && u.role == "Снайпер")
                return false;
            if (i.id == "supply")
                return u.role == "Стрелок";
            if (i.id == "rshg")
                return u.role == "Гранатомётчик";
            if (i.id == "gp" || i.id == "m4gp")
                return u.role == "Стрелок";
            return string.IsNullOrEmpty(i.role) || u.role == i.role;
        }

        public int ExtraSlots(string uid)
        {
            string role = Soldier(uid).role;
            return role == "Снайпер" || role == "Пулемётчик" ? 0
                : role == "Гранатомётчик" ? 2
                : 1;
        }

        public bool CanEquip(string uid, string slot, Item item)
        {
            if (item == null || !S.gear.ContainsKey(uid))
                return false;
            if (slot == "extra" && ExtraSlots(uid) == 0 || slot == "extra2" && ExtraSlots(uid) != 2)
                return false;
            return Fits(S.gear[uid], slot, item) && (slot == "bag" || Allowed(Soldier(uid), item));
        }

        public void Buy(string id)
        {
            Check(LootCrates.Direct(Data.Item(id)), "Оружие и снаряжение доступны только в ящиках");
            var i = Data.Item(id);
            Check(!Personal(id) && Unlocked(i), "Предмет ещё не открыт сюжетом");
            Check(S.balance >= i.price, "Недостаточно очков");
            S.balance -= i.price;
            S.inventory[id] = Owned(id) + 1;
        }

        public double Sell(string id)
        {
            var i = Data.Item(id);
            Check(
                i == null || i.type != "weapon" || WeaponStock > 5,
                "Нужно оставить минимум 5 единиц основного оружия, включая выданное"
            );
            Check(i != null && !Personal(id) && Free(id) > 0, "Продать можно только свободный предмет со склада");
            Check(
                Owned(id) > MaxSquadUse(id),
                "Этот экземпляр нужен комплекту другого отряда. Сначала снимите его там."
            );
            if (i.type == "weapon")
                RemoveWeaponInstanceForSale(id);
            S.inventory[id]--;
            double n = i.price * .75;
            S.balance += n;
            return n;
        }

        public void BuyManual()
        {
            int cost = Data.offers.First(x => x.id == "med").price * 3;
            Check(S.balance >= cost, "Недостаточно очков");
            S.balance -= cost;
            S.career.manuals++;
        }

        static readonly int[] lights =  { 51, 54, 55, 88, 83, 81, 97, 79, 62, 61, 65 },
            families =  { 52, 53, 58, 63, 64, 69, 71, 72, 75, 80, 85, 89, 91, 94, 95, 98 },
            heads =  { 57, 59, 63, 107, 116, 131, 133, 134, 136 },
            snipers =  { 62, 69, 143 };

        int Pick(IList<int> pool, int old = 0)
        {
            return pool.Contains(old) ? old
                : pool.Count == 1 ? pool[0]
                : pool[(int)(random() * pool.Count)];
        }

        int PickHead(IList<int> pool, int old)
        {
            S.seenHeads = S.seenHeads ?? new List<int>();
            foreach (
                var look in S
                    .looks.Values.Concat((S.pendingRecruits ?? new List<Arrival>()).Select(a => a.look))
                    .Concat(
                        (S.caseHistory ?? new Dictionary<string, Shipment>())
                            .Values.SelectMany(c => c.fighters ?? new List<Arrival>())
                            .Select(a => a.look)
                    )
            )
                if (look != null && !S.seenHeads.Contains(look.headId))
                    S.seenHeads.Add(look.headId);
            int head;
            if (pool.Contains(old))
                head = old;
            else
            {
                var unseen = pool.Where(h => !S.seenHeads.Contains(h)).ToArray();
                head = Pick(unseen.Length > 0 ? unseen : pool);
            }
            if (!S.seenHeads.Contains(head))
                S.seenHeads.Add(head);
            return head;
        }

        public Look MakeLook(string role, bool commander = false, Look old = null)
        {
            return new Look
            {
                headId = PickHead(
                    commander ? new[] { 82 }
                        : role == "Снайпер" ? snipers
                        : role == "Медик" ? new[] { 88, 106, 109 }
                        : heads,
                    old == null ? 0 : old.headId
                ),
                lightId = Pick(lights, old == null ? 0 : old.lightId),
                familyId = Pick(families, old == null ? 0 : old.familyId),
            };
        }

        // Archetypes are fixed; recruitment randomness selects an archetype only.
        string CanonicalTemplate(string template)
        {
            if (template == "mg")
                return new[] { "mg", "mg-medved", "mg-bury" }[Math.Min(2, (int)(random() * 3))];
            if (template == "rpg")
                return new[] { "rpg", "rpg-pepel", "rpg-fugas" }[Math.Min(2, (int)(random() * 3))];
            if (Data.Template(template).role == "Стрелок")
                return new[] { "s1", "s2", "s3" }[Math.Min(2, (int)(random() * 3))];
            if (template != "sn" && template != "med")
                return template;
            var pool =
                template == "med" ? new[] { "med", "med-dobry", "med-hirurg" } : new[] { "sn", "sn-morok", "sn-manul" };
            return pool[Math.Min(2, (int)(random() * pool.Length))];
        }

        public Look CanonicalLook(string template)
        {
            if (template == "mg" || template == "mg-medved" || template == "mg-bury")
                return new Look
                {
                    headId =
                        template == "mg" ? 57
                        : template == "mg-medved" ? 59
                        : 63,
                    lightId = 79,
                    familyId = 91,
                    heavyId = 64,
                };
            if (template == "rpg" || template == "rpg-pepel" || template == "rpg-fugas")
                return new Look
                {
                    headId =
                        template == "rpg" ? 107
                        : template == "rpg-pepel" ? 134
                        : 136,
                    lightId = 62,
                    familyId = 89,
                    heavyId = 89,
                };
            if (Data.Template(template).role == "Стрелок")
                return new Look
                {
                    headId =
                        template == "s2" ? 131
                        : (template == "s3" || template == "sup") ? 133
                        : 116,
                    lightId = 62,
                    familyId = 89,
                    heavyId = 89,
                };
            if (template == "med" || template == "med-dobry" || template == "med-hirurg")
                return new Look
                {
                    headId =
                        template == "med" ? 88
                        : template == "med-dobry" ? 106
                        : 109,
                    lightId = 83,
                    familyId = 63,
                    heavyId = 80,
                };
            if (template == "kedr")
                return new Look
                {
                    headId = 82,
                    lightId = 51,
                    familyId = 53,
                };
            if (template == "sn" || template == "sn-morok" || template == "sn-manul")
                return new Look
                {
                    headId =
                        template == "sn" ? 143
                        : template == "sn-morok" ? 62
                        : 69,
                    lightId = 55,
                    familyId = 95,
                };
            var u = Data.Template(template);
            int index = Array.FindIndex(Data.units, x => x.id == template);
            index = Math.Max(0, index);
            return new Look
            {
                headId =
                    template == "kedr" ? 82
                    : u.role == "Медик" ? 88
                    : heads[index % heads.Length],
                lightId = lights[index % lights.Length],
                familyId = families[index % families.Length],
            };
        }

        public string PortraitArt(string id)
        {
            var template = S.recruits.ContainsKey(id) ? S.recruits[id].template : id;
            if (template == "mg" || template == "mg-medved" || template == "mg-bury")
                return template == "mg" ? "canonical/val.png"
                    : template == "mg-medved" ? "canonical/medved.png"
                    : "canonical/bury.png";
            if (template == "rpg" || template == "rpg-pepel" || template == "rpg-fugas")
                return template == "rpg" ? "canonical/vektor.png"
                    : template == "rpg-pepel" ? "canonical/pepel.png"
                    : "canonical/fugas.png";
            if (Data.Template(template).role == "Стрелок")
                return template == "s2" ? "canonical/veresk.png"
                    : (template == "s3" || template == "sup") ? "canonical/granit.png"
                    : "canonical/klen.png";
            return template == "med" ? "canonical/fenix.png"
                : template == "med-dobry" ? "canonical/dobry.png"
                : template == "med-hirurg" ? "canonical/hirurg.png"
                : template == "kedr" ? "canonical/kedr.png"
                : template == "sn" ? "canonical/sedoy.png"
                : template == "sn-morok" ? "canonical/morok.png"
                : template == "sn-manul" ? "canonical/manul.png"
                : null;
        }

        public string Enlist(string template)
        {
            var o = Data.offers.FirstOrDefault(x => x.id == template);
            Check(o != null && Data.Template(template) != null, "Нет такой вакансии");
            Check(S.gear.Count < 50, "Казарма заполнена: 50 из 50");
            Check(S.story >= o.unlock, "Боец ещё не открыт сюжетом");
            Check(S.balance >= o.price, "Недостаточно очков");
            template = CanonicalTemplate(template);
            var name = Data.Template(template).name;
            string id = !S.gear.ContainsKey(template) && !S.recruits.ContainsKey(template) ? template : NextId();
            S.balance -= o.price;
            RememberName(name);
            S.recruits[id] = new Recruit { template = template, name = name };
            S.gear[id] = new Gear();
            S.career.people[id] = new Person();
            S.looks[id] = CanonicalLook(template);
            return id;
        }

        string[] AvailableNames(string role)
        {
            var used = new HashSet<string>(S.usedNames.Concat(Roster().Select(u => u.name)));
            return Data.callsigns.First(n => n.role == role).names.Where(n => !used.Contains(n)).ToArray();
        }

        void RememberName(string name)
        {
            S.usedNames = S.usedNames.Concat(Roster().Select(u => u.name)).Concat(new[] { name }).Distinct().ToList();
        }

        string NextId()
        {
            string id;
            do
            {
                id = "recruit-" + (++S.nextRecruit);
            } while (S.gear.ContainsKey(id) || S.recruits.ContainsKey(id));
            return id;
        }

        public static int Width(string id)
        {
            return id == "rpg" || id == "rshg" || id == "kit" ? 3 : 1;
        }

        public int BagLimit(string id)
        {
            var g = S.gear[id];
            if (g.extra != "supply" && g.extra != "medbag")
                return 0;
            string role = Soldier(id).role,
                prefix = role == "Стрелок" ? "rifpack"
                : role == "Медик" ? "medpack"
                : null;
            return 6 + 2 * Enumerable.Range(1, 3).Count(i => prefix != null && Has(id, prefix + i));
        }

        public bool Fits(Gear g, string slot, Item i)
        {
            if (i == null)
                return false;
            if (slot == "tourniquet")
                return i.id == "band";
            if (slot == "rigSlots")
                return i.type == "grenade";
            if (slot == "bag")
                return g.extra == "medbag"
                    ? i.type == "medical"
                    : g.extra == "supply"
                        && (i.type == "ammo" || i.type == "grenade" || i.id == "rpg" || i.id == "rshg");
            if (slot == "extra2")
                return i.id == "rpg" || i.id == "rshg";
            return slot == i.type;
        }

        static bool Place(List<string> cells, int index, string id)
        {
            int n = Width(id);
            if (index < 0 || index + n > cells.Count || cells.Skip(index).Take(n).Any(x => x != null))
                return false;
            cells[index] = id;
            for (int j = 1; j < n; j++)
                cells[index + j] = "@" + index;
            return true;
        }

        static bool Limits(IEnumerable<string> ids)
        {
            return ids.Count(x => x == "vog") <= 4
                && ids.Count(x => x == "f1" || x == "rgd" || x == "rgn" || x == "rgo") <= 4;
        }

        public void SyncPack(string id)
        {
            var g = S.gear[id];
            while (g.bag.Count < BagLimit(id))
                g.bag.Add(null);
        }

        public void Equip(string uid, string slot, string id, int index = 0)
        {
            Check(S.gear.ContainsKey(uid), "Боец не найден");
            var g = S.gear[uid];
            var it = Data.Item(id);
            Check(id == null || CanEquip(uid, slot, it), "Предмет не подходит для этого слота");
            if (slot == "bag" || slot == "rigSlots")
            {
                var cells = new List<string>(slot == "bag" ? g.bag : g.rigSlots);
                Check(index >= 0 && index < cells.Count, "Нет такой клетки");
                if (cells[index] != null && cells[index].StartsWith("@", StringComparison.Ordinal))
                    index = int.Parse(cells[index].Substring(1));
                var prev = cells[index];
                if (prev == id)
                    return;
                for (int n = 0; n < Width(prev) && index + n < cells.Count; n++)
                    cells[index + n] = null;
                Check(id == null || Free(id) > 0, "Нет свободных экземпляров");
                Check(id == null || Place(cells, index, id), "Не хватает соседних свободных клеток");
                Check(
                    slot != "bag" || Limits(cells),
                    "Лимит сумки: 4 подствольных и 4 метательных, дым не учитывается"
                );
                if (slot == "bag")
                    g.bag = cells;
                else
                    g.rigSlots = cells;
                return;
            }

            if (g.Get(slot) == id)
                return;
            Check(id == null || Free(id) > 0, "Нет свободных экземпляров");
            g.Set(slot, id);
            if (slot == "extra")
                g.bag = Enumerable.Repeat<string>(null, BagLimit(uid)).ToList();
            if (slot == "rig")
                g.rigSlots = Enumerable
                    .Range(0, it == null ? 0 : it.capacity)
                    .Select(i => i < g.rigSlots.Count ? g.rigSlots[i] : null)
                    .ToList();
        }

        public void Strip(string id)
        {
            Check(S.gear.ContainsKey(id), "Боец не найден");
            S.gear[id] = new Gear();
        }

        public void Replace(int index, string id)
        {
            Check(index > 0 && index < 5, "Замена командира пока недоступна");
            Check(id != null && S.gear.ContainsKey(id) && Soldier(id).role != "Командир", "Недопустимый боец");
            Check(!S.active.Contains(id), "Боец уже в отряде");
            S.active[index] = id;
        }

        public static int Threshold(int level)
        {
            double n = level - 1;
            return 10 * (int)Math.Floor((100 * n + 2300 * n * (n - 1) / (17 * 16)) / 10 + .5);
        }

        public static int LevelAt(int xp)
        {
            int l = 1;
            while (l < 10000 && Threshold(l + 1) <= xp)
                l++;
            return l;
        }

        public int PlayerLevel
        {
            get { return LevelAt(S.career.xp); }
        }

        public static int NextCost(int level)
        {
            return level <= 14 ? 2 * level - 1 : 27 + 3 * (level - 14);
        }

        public static int BaseValue(int level)
        {
            if (level <= 15)
                return 1 + (level - 1) * (level - 1);
            int n = level - 15;
            return 197 + 27 * n + 3 * n * (n + 1) / 2;
        }

        public static int FighterValue(Person p)
        {
            return BaseValue(p.level) + p.credit;
        }

        public Person Person(string id)
        {
            Check(S.gear.ContainsKey(id) && S.career.people.ContainsKey(id), "Боец не найден");
            return S.career.people[id];
        }

        public bool Has(string id, string skill)
        {
            int n;
            return Person(id).skills.TryGetValue(skill, out n) && n > 0;
        }

        public bool Available(string id, Skill k)
        {
            var p = Person(id);
            return (string.IsNullOrEmpty(k.role) || Soldier(id).role == k.role)
                && !Has(id, k.id)
                && p.level >= k.level
                && k.pre.All(x => Has(id, x))
                && p.points > 0;
        }

        public bool CanLearn(string id)
        {
            return Person(id).points > 0 && Data.skills.Any(k => Available(id, k));
        }

        public void Spend(string id, string node)
        {
            var k = Data.Skill(node);
            Check(k != null && Available(id, k), "Требования навыка не выполнены");
            Person(id).skills[node] = 1;
            Person(id).points--;
            SyncPack(id);
        }

        void GiveXp(string id, int n)
        {
            var p = Person(id);
            int old = p.level;
            p.credit += n;
            while (p.level < PlayerLevel && p.credit >= NextCost(p.level))
            {
                p.credit -= NextCost(p.level);
                p.level++;
            }

            p.points += p.level - old;
        }

        public int InvestLimit(string id)
        {
            return Math.Min(S.career.freeXp, BaseValue(PlayerLevel) - FighterValue(Person(id)));
        }

        public void Invest(string id, int n)
        {
            Check(n > 0 && n <= InvestLimit(id), "Проверь количество опыта и предел уровня");
            S.career.freeXp -= n;
            GiveXp(id, n);
        }

        public Person Preview(string id, string source)
        {
            var p = Person(id);
            if (source == "manual")
            {
                Check(S.career.manuals > 0, "Нет учебных пособий");
                return new Person { level = p.level, credit = p.credit };
            }

            if (source == "level-book")
            {
                Check(S.career.levelBooks > 0, "Нет книг повышения уровня");
                Check(p.level < PlayerLevel, "Достигнут уровень игрока");
                return new Person { level = p.level + 1, credit = p.credit };
            }

            throw new InvalidOperationException("Неизвестный источник обучения: " + source);
        }

        public void Promote(string id, string source)
        {
            var next = Preview(id, source);
            var p = Person(id);
            if (source == "manual")
            {
                S.career.manuals--;
                p.points++;
            }
            else
            {
                S.career.levelBooks--;

                p.points += next.level - p.level;
                p.level = next.level;
                p.credit = next.credit;
                Admit();
            }
        }

        void Remove(string id)
        {
            RememberAvatarHeads();
            foreach (var q in S.squads)
            {
                q.loadouts.Remove(id);
                for (int i = 0; i < q.members.Count; i++)
                    if (q.members[i] == id)
                        q.members[i] = null;
            }
            S.gear.Remove(id);
            S.looks.Remove(id);
            S.career.people.Remove(id);
        }

        public int DismissXp(string id)
        {
            Check(S.gear.ContainsKey(id) && id != "kedr", "Командира нельзя уволить в запас");
            return FighterValue(Person(id));
        }

        public int Dismiss(string id, string replacement = null)
        {
            // Calculate both refunds before changing the roster. Preview uses these same methods.
            int xp = DismissXp(id);
            int manuals = RefundCount(id);
            int index = S.active.IndexOf(id);
            if (index != -1 && replacement != null)
                Replace(index, replacement);
            else if (index != -1)
                S.active[index] = null;
            // Inventory contains TOTAL ownership, including equipped items. Removing all
            // live and saved loadouts releases weapon, armor, rig and bag contents;
            // adding those items to inventory again would duplicate them.
            Remove(id);
            S.recruits.Remove(id);
            S.career.freeXp += xp;
            S.career.manuals += manuals;
            Admit();
            return xp;
        }

        public int RefundCount(string id)
        {
            var p = Person(id);
            return Math.Max(0, p.points + p.skills.Values.Sum() - (p.level - 1));
        }

        public int ResetSkills(string id)
        {
            var p = Person(id);
            int n = RefundCount(id);
            p.skills.Clear();
            p.points = p.level - 1;
            S.career.manuals += n;
            MigrateGear();
            return n;
        }

        public bool BlockOpen(int block)
        {
            return block >= 1 && block <= 5 && (block == 1 || S.completed.Contains("war-" + (block - 1) + "-10"));
        }

        public bool MissionOpen(string mode, int b, int m)
        {
            if (mode == "test" || mode == "test-reference")
                return b == 1 && (m == 1 || m == 20);
            if (mode == "raid")
                return m >= 1 && m <= 100;
            return (mode == "story" && b == 1 || mode == "war" && BlockOpen(b))
                && m >= 1
                && m <= (mode == "war" ? 10 : 20)
                && (
                    m == 1
                    || S.completed.Contains(mode + "-" + b + "-" + (m - 1))
                    || S.completed.Contains(mode + "-" + b + "-" + m)
                );
        }

        public static int Payout(string mode, int b, int m)
        {
            if (mode == "story" && b == 1 && m >= 1 && m <= 20)
                return 50 * (1 + (m - 1) / 4);
            return mode == "war" && b >= 1 && b <= 5 && m >= 1 && m <= 10
                ? (m == 10 ? 250 + (b - 1) * 50 : 100 + (b - 1) * 25)
                : 0;
        }

        public static int BattleXp(string mode, int block, int mission)
        {
            return mode == "war" ? block * (mission % 5 == 0 ? 2 : 1) : 1 + (mission - 1) / 4;
        }

        void MigrateBattleXp()
        {
            if (S.career.battleXpVersion >= 2)
                return;
            int compensation = 0;
            foreach (string key in S.completed.Distinct())
            {
                var parts = key.Split('-');
                int block,
                    mission;
                if (parts.Length != 3 || !int.TryParse(parts[1], out block) || !int.TryParse(parts[2], out mission))
                    continue;
                if (
                    !(
                        parts[0] == "war" && block >= 1 && block <= 5 && mission >= 1 && mission <= 10
                        || parts[0] == "story" && block == 1 && mission >= 1 && mission <= 20
                    )
                )
                    continue;
                int previous = S.career.battleXpVersion >= 1 ? (parts[0] == "war" ? block : 1 + (mission - 1) / 4) : 1;
                compensation += 5 * (BattleXp(parts[0], block, mission) - previous);
            }
            S.career.freeXp += compensation;
            S.career.battleXpVersion = 2;
        }

        public bool Win(string mode, int b, int m, IEnumerable<string> party = null)
        {
            if (mode == "test" || mode == "test-reference")
                return false;
            Check(MissionOpen(mode, b, m), "Сначала пройди предыдущую миссию");
            string key = mode + "-" + b + "-" + m;
            if (S.completed.Contains(key))
                return false;
            S.completed.Add(key);
            S.balance += Payout(mode, b, m);
            if (mode == "story")
                S.story = Math.Max(S.story, m);
            S.career.xp += 100;
            S.career.manuals++;
            var earned = new HashSet<string>(party ?? S.active);
            int reward = BattleXp(mode, b, m);
            foreach (string id in S.gear.Keys.ToArray())
                GiveXp(id, earned.Contains(id) ? reward : 0);
            S.career.freeXp += earned.Count(id => !S.gear.ContainsKey(id)) * reward;
            Award(key, mode, b, m);
            return true;
        }

        public string Equipment(bool priority = false)
        {
            var all = Data.items.Where(i => !Personal(i.id) && Unlocked(i)).ToArray();
            var pool = priority
                ? all.Where(i => i.type == "weapon" || i.type == "armor" || i.type == "rig" || i.type == "extra")
                    .ToArray()
                : all;
            if (pool.Length == 0)
                pool = all;
            double roll = random() * pool.Sum(i => 1d / i.price);
            foreach (var i in pool)
            {
                roll -= 1d / i.price;
                if (roll < 0)
                    return i.id;
            }

            return pool.Last().id;
        }

        public static int[] RecruitRange(int b, int cap, bool advanced = false)
        {
            int[][] rows =
            {
                new[] { 1, 2, 3, 3 },
                new[] { 2, 3, 4, 5 },
                new[] { 3, 5, 6, 7 },
                new[] { 4, 6, 7, 9 },
                new[] { 5, 7, 9, 11 },
            };
            var r = rows[b - 1];
            int i = advanced ? 2 : 0;
            return new[] { Math.Min(cap, r[i]), Math.Min(cap, r[i + 1]) };
        }

        Arrival Reserve(int block, bool advanced)
        {
            var open = Data
                .units.Where(u => u.id != "sup" && Data.offers.Any(o => o.id == u.id && o.unlock <= S.story))
                .ToArray();
            var u = open[(int)(random() * open.Length)];
            u = Data.Template(CanonicalTemplate(u.id));
            var name = u.name;
            RememberName(name);
            var range = RecruitRange(block, PlayerLevel, advanced);
            return new Arrival
            {
                template = u.id,
                name = name,
                level = range[0] + (int)(random() * (range[1] - range[0] + 1)),
                advanced = advanced,
                look = CanonicalLook(u.id),
            };
        }

        public Shipment Award(string key, string mode, int b, int m)
        {
            int size =
                mode == "war"
                    ? (
                        m == 10 ? 5
                        : m == 5 ? 3
                        : 0
                    )
                    : 0;
            if (size == 0 || S.caseHistory.ContainsKey(key))
                return null;
            var r = new Shipment { book = size == 5 };
            for (int i = 0; i < size; i++)
                r.gear.Add(Equipment(size == 5 && i == 0));
            if (size == 5)
                for (int i = 0; i < 4; i++)
                    r.fighters.Add(Reserve(b, i == 3));
            S.caseHistory[key] = r;
            foreach (string id in r.gear)
                S.inventory[id] = Owned(id) + 1;
            if (r.book)
                S.career.levelBooks++;
            S.pendingRecruits.AddRange(r.fighters);
            Admit();
            return r;
        }

        public void Admit()
        {
            while (S.pendingRecruits.Count > 0 && S.gear.Count < 50)
            {
                var f = S.pendingRecruits[0];
                S.pendingRecruits.RemoveAt(0);
                string id = NextId();
                S.recruits[id] = new Recruit { template = f.template, name = f.name };
                S.gear[id] = new Gear();
                S.looks[id] = f.look = CanonicalLook(f.template);
                S.career.people[id] = new Person { level = f.level, points = f.level - 1 };
            }
        }

        public void Normalize()
        {
            S.unlockAll = S.unlimited = false;
            S.recruits = S.recruits ?? new Dictionary<string, Recruit>();
            S.usedNames = S.usedNames ?? new List<string>();
            S.looks = S.looks ?? new Dictionary<string, Look>();
            S.inventory = S.inventory ?? new Dictionary<string, int>();
            S.pendingRecruits = S.pendingRecruits ?? new List<Arrival>();
            foreach (var recruit in S.recruits.Values)
            {
                if (recruit.template == "sn" && recruit.name == "Морок")
                    recruit.template = "sn-morok";
                if (recruit.template == "sn" && recruit.name == "Манул")
                    recruit.template = "sn-manul";
                if (recruit.template == "med" && recruit.name == "Добрый")
                    recruit.template = "med-dobry";
                if (recruit.template == "med" && recruit.name == "Хирург")
                    recruit.template = "med-hirurg";
                var definition = Data.Template(recruit.template);
                if (definition != null)
                    recruit.name = definition.name;
            }
            foreach (
                var arrival in S.pendingRecruits.Concat(
                    (S.caseHistory ?? new Dictionary<string, Shipment>()).Values.SelectMany(x =>
                        x.fighters ?? new List<Arrival>()
                    )
                )
            )
            {
                if (arrival.template == "sn" && arrival.name == "Морок")
                    arrival.template = "sn-morok";
                if (arrival.template == "sn" && arrival.name == "Манул")
                    arrival.template = "sn-manul";
                if (arrival.template == "med" && arrival.name == "Добрый")
                    arrival.template = "med-dobry";
                if (arrival.template == "med" && arrival.name == "Хирург")
                    arrival.template = "med-hirurg";
                var definition = Data.Template(arrival.template);
                if (definition != null)
                {
                    arrival.name = definition.name;
                    arrival.look = CanonicalLook(arrival.template);
                }
            }

            S.completed = S.completed ?? new List<string>();
            S.operation = S.operation ?? new Operation();
            S.raid = S.raid ?? new RaidProgress();
            S.formation = S.formation ?? new Formation();
            if (S.formation.pairSlots == null && S.formation.pairing != 4)
                S.formation.pairSlots = new[] { -1, -1, -1, -1 };
            if (S.career == null)
                S.career = new Career { xp = S.completed.Count * 100, manuals = S.completed.Count };
            var c = S.career;
            c.people = c.people ?? new Dictionary<string, Person>();
            if (c.treeVersion != 4)
            {
                foreach (var p in c.people.Values)
                {
                    p.points += p.skills.Values.Sum();
                    p.skills.Clear();
                }

                c.treeVersion = 4;
            }

            foreach (var id in S.gear.Keys)
                if (!c.people.ContainsKey(id))
                    c.people[id] = new Person();
            if (c.xpCurve == 0)
            {
                c.xp = Math.Max(c.xp, c.people.Values.Max(p => Threshold(p.level)));
                c.xpCurve = 2;
            }

            if (c.fighterCurve == 0)
            {
                foreach (var p in c.people.Values)
                    p.credit = (int)Math.Floor(p.credit * NextCost(p.level) / Math.Pow(2, p.level - 1));
                c.fighterCurve = 2;
            }

            if (S.caseHistory == null)
            {
                S.caseHistory = new Dictionary<string, Shipment>();
                foreach (string key in S.completed)
                {
                    var a = key.Split('-');
                    if (a.Length == 3 && a[0] == "war")
                        Award(key, a[0], int.Parse(a[1]), int.Parse(a[2]));
                }
            }

            MigrateBattleXp();
            MigrateFiniteEquipment();
            MigrateGear();
            NormalizeSquads();
            NormalizeProfile();
            Validate();
        }

        public void MigrateGear()
        {
            foreach (var pair in S.gear)
            {
                string id = pair.Key;
                var g = pair.Value;
                var u = Soldier(id);
                Look old;
                S.looks.TryGetValue(id, out old);
                S.looks[id] = CanonicalLook(S.recruits.ContainsKey(id) ? S.recruits[id].template : id);
                if (S.recruits.ContainsKey(id))
                    S.recruits[id].name = u.name;
                if (Data.Item(g.weapon) == null)
                    g.weapon = null;
                if (Data.Item(g.armor) == null)
                    g.armor = null;
                if (Data.Item(g.rig) == null)
                    g.rig = null;
                if (g.extra != null && !CanEquip(id, "extra", Data.Item(g.extra)))
                {
                    g.extra = null;
                    g.bag.Clear();
                }

                if (g.extra2 != null && !CanEquip(id, "extra2", Data.Item(g.extra2)))
                    g.extra2 = null;
                if (!Allowed(u, Data.Item(g.armor)))
                    g.armor = null;
                if (!Allowed(u, Data.Item(g.weapon)))
                    g.weapon = null;
                g.rigSlots = Enumerable
                    .Range(0, Data.Item(g.rig)?.capacity ?? 0)
                    .Select(i => i < g.rigSlots.Count ? g.rigSlots[i] : null)
                    .ToList();
                var oldBag = g.bag;
                g.bag = Enumerable.Repeat<string>(null, BagLimit(id)).ToList();
                for (int i = 0; i < oldBag.Count; i++)
                {
                    string v = oldBag[i];
                    if (!Fits(g, "bag", Data.Item(v)))
                        continue;
                    int at = Place(g.bag, i, v) ? i : -1;
                    if (at < 0)
                        for (int j = 0; j < g.bag.Count; j++)
                            if (Place(g.bag, j, v))
                            {
                                at = j;
                                break;
                            }

                    if (at >= 0 && !Limits(g.bag))
                        for (int j = 0; j < Width(v); j++)
                            g.bag[at + j] = null;
                }
            }
        }

        public void Validate()
        {
            var f = S.formation;
            Check(
                f != null
                    && f.pairing >= 0
                    && f.pairing <= 4
                    && f.width >= 0
                    && f.width <= 2
                    && f.depth >= 0
                    && f.depth <= 2
                    && f.positions != null
                    && f.positions.Length == 5
                    && f.positions.All(x => x >= -1 && x <= 1)
                    && f.pairSlots != null
                    && f.pairSlots.Length == 4
                    && f.pairSlots.All(x => x == -1 || x >= 1 && x <= 4)
                    && f.pairSlots.Where(x => x > 0).Distinct().Count() == f.pairSlots.Count(x => x > 0),
                "Повреждён замысел построения"
            );
            Check(
                S.active.Count == 5
                    && (S.active[0] == null || S.active[0] == "kedr")
                    && S.active.Where(id => id != null).Distinct().Count() == S.active.Count(id => id != null)
                    && S.active.All(id => id == null || S.gear.ContainsKey(id)),
                "Повреждён состав отряда"
            );
            Check(
                S.gear.Count <= 50
                    && S.balance >= 0
                    && !double.IsNaN(S.balance)
                    && !double.IsInfinity(S.balance)
                    && S.career.xp >= 0
                    && S.career.freeXp >= 0
                    && S.career.manuals >= 0
                    && S.career.levelBooks >= 0,
                "Повреждены ресурсы лагеря"
            );
            foreach (var pair in S.gear)
            {
                var p = Person(pair.Key);
                Check(p.level >= 1 && p.level <= 10000 && p.credit >= 0 && p.points >= 0, "Повреждён опыт бойца");
                Check(
                    pair.Value.All()
                        .Where(x => x != null && !x.StartsWith("@", StringComparison.Ordinal))
                        .All(x => Data.Item(x) != null),
                    "Неизвестное снаряжение"
                );
            }

            foreach (var kv in S.inventory)
                Check(kv.Value >= 0 && Free(kv.Key) >= 0, "Снаряжение выдано сверх наличия");
            foreach (
                var id in S
                    .gear.Values.SelectMany(g => g.All())
                    .Where(x => x != null && !x.StartsWith("@", StringComparison.Ordinal))
                    .Distinct()
            )
                Check(Free(id) >= 0, "Снаряжение выдано сверх наличия");
        }

        public static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
    }
}
