using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Rubezh.Camp
{
    [Serializable]
    public sealed class CampSquad
    {
        public string id,
            name;
        public Formation formation;
        public List<string> members = new List<string>();
        public Dictionary<string, Gear> loadouts = new Dictionary<string, Gear>();
    }

    public sealed partial class CampModel
    {
        public CampSquad CurrentSquad
        {
            get { return S.squads.First(q => q.id == S.selectedSquad); }
        }
        public bool SquadReady
        {
            get
            {
                return S.active.Count == 5
                    && S.active[0] == "kedr"
                    && S.active.All(id => id != null && S.gear.ContainsKey(id));
            }
        }

        static Gear CloneGear(Gear g)
        {
            return JsonConvert.DeserializeObject<Gear>(JsonConvert.SerializeObject(g));
        }

        public static Formation CloneFormation(Formation f)
        {
            return JsonConvert.DeserializeObject<Formation>(JsonConvert.SerializeObject(f ?? new Formation()));
        }

        public void CaptureSquad()
        {
            if (S.squads == null || S.squads.Count == 0)
                return;
            var q = S.squads.FirstOrDefault(x => x.id == S.selectedSquad);
            if (q == null)
                return;
            q.members = new List<string>(S.active);
            q.formation = CloneFormation(S.formation);
            q.loadouts = S.gear.ToDictionary(x => x.Key, x => CloneGear(x.Value));
        }

        void NormalizeSquads()
        {
            if (S.squads == null)
                S.squads = new List<CampSquad>();
            if (S.squads.Count == 0)
            {
                S.selectedSquad = "kedr-1";
                S.squads.Add(new CampSquad { id = S.selectedSquad, name = "Кедр-1" });
                CaptureSquad();
            }
            if (!S.squads.Any(q => q.id == S.selectedSquad))
                S.selectedSquad = S.squads[0].id;
            foreach (var q in S.squads)
            {
                q.members = Enumerable
                    .Range(0, 5)
                    .Select(i =>
                        q.members != null
                        && i < q.members.Count
                        && q.members[i] != null
                        && S.gear.ContainsKey(q.members[i])
                            ? q.members[i]
                            : null
                    )
                    .ToList();
                if (q.formation == null)
                    q.formation = q.id == S.selectedSquad ? CloneFormation(S.formation) : new Formation();
                if (q.loadouts == null)
                    q.loadouts = new Dictionary<string, Gear>();
                foreach (var id in q.loadouts.Keys.Where(id => !S.gear.ContainsKey(id)).ToArray())
                    q.loadouts.Remove(id);
            }
        }

        public void SelectSquad(string id)
        {
            Check(S.squads.Any(q => q.id == id), "Отряд не найден");
            if (id == S.selectedSquad)
                return;
            CaptureSquad();
            var q = S.squads.First(x => x.id == id);
            var all = S.gear.Keys.ToArray();
            S.gear = all.ToDictionary(
                uid => uid,
                uid => q.loadouts.ContainsKey(uid) ? CloneGear(q.loadouts[uid]) : new Gear()
            );
            S.active = q.members.Select(uid => uid != null && S.gear.ContainsKey(uid) ? uid : null).ToList();
            S.selectedSquad = id;
            S.formation = CloneFormation(q.formation);
            MigrateGear();
            Validate();
        }

        public void CreateSquad()
        {
            Check(S.squads.Count < 20, "Доступно до 20 отрядов");
            CaptureSquad();
            var q = new CampSquad
            {
                id = Guid.NewGuid().ToString("N"),
                name = "Кедр-" + (S.squads.Count + 1),
                members = Enumerable.Repeat<string>(null, 5).ToList(),
            };
            S.squads.Add(q);
            SelectSquad(q.id);
        }

        public int MaxSquadUse(string id)
        {
            return Math.Max(
                Used(id),
                S.squads.Where(q => q.id != S.selectedSquad)
                    .Select(q => q.loadouts.Values.Sum(g => g.All().Count(x => x == id)))
                    .DefaultIfEmpty(0)
                    .Max()
            );
        }

        public void RenameSquad(string name)
        {
            RenameSquad(S.selectedSquad, name);
        }

        public void RenameSquad(string id, string name)
        {
            name = (name ?? "").Trim();
            Check(name.Length > 0 && name.Length <= 30, "Название: от 1 до 30 символов");
            var squad = S.squads.FirstOrDefault(q => q.id == id);
            Check(squad != null, "Отряд не найден");
            squad.name = name;
        }

        public void AssignSquadSlot(int slot, string id)
        {
            Check(slot >= 0 && slot < 5 && id != null && S.gear.ContainsKey(id), "Недопустимый слот или боец");
            Check(
                slot == 0 ? id == "kedr" : Soldier(id).role != "Командир",
                "Первый слот — командир, остальные — бойцы"
            );
            Check(!S.active.Contains(id) || S.active[slot] == id, "Боец уже в этом отряде");
            S.active[slot] = id;
        }
    }
}
