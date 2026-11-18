using System;
namespace SilentOrders.Core
{
    public sealed class BlastCrater
    {
        public V2 Position;
        public float Cells;
        public bool Mirror;
        public static void Record(BattleSimulation b)
        {
            foreach (var blast in b.Blasts)
            {
                if (blast.CraterRecorded) continue;
                blast.CraterRecorded = true;
                // Earth craters belong on the terrain, never suspended at roof height.
                if (blast.Height > .2f) continue;
                float size = blast.Mortar ? 2 : 1; // 2x2 or 1x1 cell footprint.
                bool duplicate = false;
                foreach (var old in b.Craters)
                    if (V2.Distance(old.Position, blast.Position) < .25f && old.Cells >= size)
                    { duplicate = true; break; }
                if (duplicate) continue;
                if (b.Craters.Count >= 256) b.Craters.RemoveAt(0);
                b.Craters.Add(new BlastCrater { Position = blast.Position, Cells = size,
                    Mirror = ((int)(blast.Position.X * 7 + blast.Position.Y * 13) & 1) != 0 });
            }
        }
    }
}
