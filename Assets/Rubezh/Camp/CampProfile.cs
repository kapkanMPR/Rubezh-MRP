using System;
using System.Linq;
using System.Collections.Generic;
namespace Rubezh.Camp
{
    public sealed partial class CampModel
    {
        static readonly int[] ProfileHeads = {57,59,62,63,69,82,88,106,107,109,116,131,133,134,136,143};
        public void RememberAvatarHeads()
        {
            var heads = new HashSet<int>(S.avatarHeads ?? new List<int>());
            foreach (int h in S.seenHeads ?? new List<int>()) heads.Add(h);
            foreach (var id in S.gear.Keys)
                if (S.looks.TryGetValue(id, out var look) && look != null) heads.Add(look.headId);
            foreach (var shipment in (S.caseHistory ?? new Dictionary<string, Shipment>()).Values)
                foreach (var arrival in shipment.fighters ?? new List<Arrival>())
                    if (arrival.look != null) heads.Add(arrival.look.headId);
            S.avatarHeads = ProfileHeads.Where(heads.Contains).ToList();
            if (S.avatarHeads.Count == 0) S.avatarHeads.Add(82);
        }
        public int[] AvailableAvatars()
        {
            RememberAvatarHeads();
            return S.avatarHeads.ToArray();
        }
        public void SelectAvatar(int head)
        {
            Check(AvailableAvatars().Contains(head), "Этот персонаж ещё не получен");
            S.avatarHead = head;
        }
        public static string CleanNickname(string value)
        {
            return string.Join(" ", (value ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
        public void RenamePlayer(string value)
        {
            value = CleanNickname(value);
            Check(value.Length >= 1 && value.Length <= 24, "Ник должен содержать от 1 до 24 символов");
            Check(value.All(c => char.IsLetterOrDigit(c) || c==' ' || c=='_' || c=='-' || c=='.'), "Используйте буквы, цифры, пробел, точку, дефис или подчёркивание");
            S.nickname = value;
        }
        void NormalizeProfile()
        {
            try { RenamePlayer(S.nickname); } catch (ArgumentException) { S.nickname = "Kapkan"; } catch (InvalidOperationException) { S.nickname = "Kapkan"; }
            var heads = AvailableAvatars();
            if (!heads.Contains(S.avatarHead)) S.avatarHead = heads.Contains(82) ? 82 : heads[0];
        }
        public Unit[] ProfileFighters()
        {
            return Roster().OrderByDescending(u => Person(u.id).level)
                .ThenByDescending(u => Person(u.id).credit)
                .ThenBy(u => u.id, StringComparer.Ordinal).Take(5).ToArray();
        }
    }
}
