using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // A bounded revision journal, shared by independent battle/foreground renderers.
    public sealed class SceneryChanges
    {
        sealed class Change
        {
            public int Revision;
            public int[] Roots;
        }

        Tile[] tiles;
        readonly List<Change> history = new List<Change>();

        public void Record(BattleMap map, int[] roots)
        {
            if (!ReferenceEquals(tiles, map.Tiles))
            {
                tiles = map.Tiles;
                history.Clear();
            }

            map.Revision++;
            history.Add(new Change { Revision = map.Revision, Roots = roots });
            if (history.Count > 128)
                history.RemoveAt(0);
        }

        public bool Since(BattleMap map, int revision, out HashSet<int> roots)
        {
            roots = new HashSet<int>();
            if (!ReferenceEquals(tiles, map.Tiles))
                return false;
            int next = revision + 1;
            foreach (var entry in history)
            {
                if (entry.Revision < next)
                    continue;
                if (entry.Revision != next)
                    return false;
                foreach (int i in entry.Roots)
                    roots.Add(i);
                next++;
            }

            return next == map.Revision + 1;
        }
    }
}
