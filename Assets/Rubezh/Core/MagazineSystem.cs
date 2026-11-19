using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Total is maintained at the only mutation boundary; Best is invalidated by each write.
    // Loaded magazine is Soldier.Ammo. Each entry below is one independent spare.
    public sealed class MagazineStore
    {
        readonly List<int> rounds = new List<int>();
        int initial,
            totalRounds,
            bestRound;
        bool ready,
            bestValid;

        public MagazineStore(int initialRounds = 0)
        {
            initial = initialRounds;
        }

        void Ensure(Soldier s)
        {
            if (ready)
                return;
            ready = true;
            Add(s, initial);
        }

        public int[] Snapshot(Soldier s)
        {
            Ensure(s);
            return rounds.ToArray();
        }

        public int Total(Soldier s)
        {
            Ensure(s);
            return totalRounds;
        }

        public void Reset(Soldier s, int total)
        {
            ready = true;
            rounds.Clear();
            totalRounds = 0;
            bestValid = false;
            Add(s, Math.Max(0, total));
        }

        public void Add(Soldier s, int amount)
        {
            Ensure(s);
            while (amount > 0)
            {
                int n = Math.Min(amount, s.MagazineSize);
                rounds.Add(n);
                unchecked
                {
                    totalRounds += n;
                }

                bestValid = false;
                amount -= n;
            }
        }

        public int Best(Soldier s)
        {
            Ensure(s);
            if (!bestValid)
            {
                bestRound = 0;
                foreach (int r in rounds)
                    bestRound = Math.Max(bestRound, r);
                bestValid = true;
            }

            return bestRound;
        }

        public int TakeBest(Soldier s)
        {
            Ensure(s);
            int index = -1;
            for (int i = 0; i < rounds.Count; i++)
                if (index < 0 || rounds[i] > rounds[index])
                    index = i;
            if (index < 0)
                return 0;
            int n = rounds[index];
            rounds.RemoveAt(index);
            unchecked
            {
                totalRounds -= n;
            }

            bestValid = false;
            return n;
        }

        // Compatibility bridge for pre-existing issuance sites; adding never tops up an old magazine.
        public void SetTotal(Soldier s, int total)
        {
            if (!ready)
            {
                Reset(s, total);
                return;
            }

            int difference = Math.Max(0, total) - Total(s);
            if (difference > 0)
            {
                Add(s, difference);
                return;
            }

            int remove = -difference;
            while (remove > 0 && rounds.Count > 0)
            {
                int i = rounds.Count - 1,
                    n = Math.Min(rounds[i], remove);
                rounds[i] -= n;
                unchecked
                {
                    totalRounds -= n;
                }

                bestValid = false;
                remove -= n;
                if (rounds[i] == 0)
                    rounds.RemoveAt(i);
            }
        }
    }

    public static class MagazineSystem
    {
        public static bool BetterMagazine(Soldier s) => s.Magazines.Best(s) > s.Ammo;

        public static void Reload(BattleSimulation b, Soldier s)
        {
            int next = s.Magazines.TakeBest(s);
            if (next <= 0)
                return;
            int previous = s.Ammo;
            if (previous > 0)
                s.Magazines.Add(s, previous);
            else
                DropEmpty(b, s);
            s.Ammo = next;
            b.History.Event(b, "magazineReload", s.Id, -1, s.Position, s.Position, next, "saved=" + previous);
        }

        public static void DropEmpty(BattleSimulation b, Soldier s)
        {
            // Visual hash only: discarded items must not consume the combat RNG.
            int serial = b.GroundAmmo.Count;
            float angle = (serial + s.Id * 17) * 2.399963f;
            V2 to = s.Position + new V2((float)Math.Cos(angle), (float)Math.Sin(angle)) * .45f;
            if (!b.Map.Walkable(to))
                to = s.Position;
            b.GroundAmmo.Add(
                new GroundAmmo
                {
                    Id = serial,
                    FromSoldierId = s.Id,
                    IntendedFor = -1,
                    Family = AmmoLogistics.Family(s),
                    Side = s.Side,
                    Box = AmmoLogistics.Box(s),
                    Rounds = 0,
                    Empty = true,
                    From = s.Position,
                    To = to,
                    Born = b.Time,
                    Duration = .32f,
                }
            );
            // Bound decorative litter without removing or renumbering real supply items.
            int empty = 0;
            for (int i = b.GroundAmmo.Count - 1; i >= 0; i--)
                if (b.GroundAmmo[i].Empty && !b.GroundAmmo[i].Collected && ++empty > 256)
                    b.GroundAmmo[i].Collected = true;
        }

        public static int ArtIndex(GroundAmmo item) =>
            item.Box ? (item.Family >= 6 ? 4 : 2)
            : item.Family % 6 >= 4 ? (item.Family >= 6 ? 5 : 3)
            : item.Family >= 6 ? 1
            : 0;
    }
}
