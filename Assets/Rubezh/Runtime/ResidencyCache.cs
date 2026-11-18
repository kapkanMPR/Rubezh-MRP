using System;
using System.Collections.Generic;

namespace Rubezh.UnityPort
{
    // Owns values; only unpinned least-recently-used entries may be released.
    public sealed class ResidencyCache<K, V>
    {
        sealed class Entry
        {
            public V Value;
            public long Bytes;
            public LinkedListNode<K> Node;
        }

        readonly Dictionary<K, Entry> entries = new Dictionary<K, Entry>();
        readonly LinkedList<K> lru = new LinkedList<K>();
        readonly Action<V> release;
        public long Bytes { get; private set; }
        public int Count => entries.Count;

        public ResidencyCache(Action<V> release)
        {
            this.release = release;
        }

        public bool TryGetValue(K key, out V value)
        {
            Entry e;
            if (!entries.TryGetValue(key, out e))
            {
                value = default(V);
                return false;
            }

            lru.Remove(e.Node);
            lru.AddLast(e.Node);
            value = e.Value;
            return true;
        }

        public void Remove(K key)
        {
            Entry e;if(!entries.TryGetValue(key,out e))return;
            entries.Remove(key);lru.Remove(e.Node);Bytes-=e.Bytes;release(e.Value);
        }

        public void Add(K key, V value, long bytes)
        {
            if (entries.ContainsKey(key))
                throw new ArgumentException("Duplicate residency key");
            var e = new Entry
            {
                Value = value,
                Bytes = Math.Max(0, bytes),
                Node = lru.AddLast(key)
            };
            entries.Add(key, e);
            Bytes += e.Bytes;
        }

        public void Trim(long budget, Func<V, bool> pinned)
        {
            var node = lru.First;
            while (Bytes > budget && node != null)
            {
                var next = node.Next;
                var e = entries[node.Value];
                if (pinned == null || !pinned(e.Value))
                {
                    entries.Remove(node.Value);
                    lru.Remove(node);
                    Bytes -= e.Bytes;
                    release(e.Value);
                }

                node = next;
            }
        }

        public void Clear()
        {
            foreach (var e in entries.Values)
                release(e.Value);
            entries.Clear();
            lru.Clear();
            Bytes = 0;
        }
    }
}
