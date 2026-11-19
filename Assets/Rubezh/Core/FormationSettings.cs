using System;

namespace SilentOrders.Core
{
    // Serializable formation preferences. Width/depth also bound ordinary movement;
    // personal front/rear settings only bias choices inside that shared envelope.
    [Serializable]
    public sealed class FormationSettings
    {
        public bool quietWork = false; // Missing in old saves: loud remains the default.
        public int pairing = 0; // 0 auto; 1..3 legacy pairings; 4 explicit 4-slot layout
        public int[] pairSlots = new int[] { -1, -1, -1, -1 }; // left A/B, right A/B; 1..4, -1 empty
        public int width = 1; // close, balanced, wide
        public int depth = 1; // shallow, balanced, deep
        public int[] positions = new int[5]; // -1 forward, 0 center, 1 rear; leader stays independent

        public bool Valid()
        {
            if (
                pairing < 0
                || pairing > 4
                || width < 0
                || width > 2
                || depth < 0
                || depth > 2
                || positions == null
                || positions.Length != 5
            )
                return false;
            if (pairing == 4 && pairSlots == null)
                return false;
            if (pairSlots != null)
            {
                if (pairSlots.Length != 4)
                    return false;
                for (int i = 0; i < 4; i++)
                {
                    if (pairSlots[i] < -1 || pairSlots[i] > 4 || pairSlots[i] == 0)
                        return false;
                    if (pairSlots[i] > 0)
                        for (int j = 0; j < i; j++)
                            if (pairSlots[i] == pairSlots[j])
                                return false;
                }
            }

            foreach (var p in positions)
                if (p < -1 || p > 1)
                    return false;
            return true;
        }

        public FormationSettings Copy()
        {
            var f = new FormationSettings
            {
                quietWork = quietWork,
                pairing = pairing,
                width = width,
                depth = depth,
            };
            f.positions = (int[])positions.Clone();
            f.pairSlots = pairSlots == null ? null : (int[])pairSlots.Clone();
            return f;
        }

        public static int PreferredPartner(int pairing)
        {
            return pairing >= 1 && pairing <= 3 ? pairing + 1 : -1;
        }

        public bool ManualComplete()
        {
            if (pairing != 4 || pairSlots == null || pairSlots.Length != 4 || !Valid())
                return false;
            for (int i = 0; i < 4; i++)
                if (pairSlots[i] < 1)
                    return false;
            return true;
        }

        // Earlier saves had three manual combinations with soldier 1 in the left pair.
        // Preserve those choices when opening the new four-slot editor.
        public int DisplaySlot(int slot)
        {
            if (slot < 0 || slot >= 4)
                return -1;
            if (pairing == 4)
                return pairSlots == null || pairSlots.Length != 4 ? -1 : pairSlots[slot];
            if (pairing < 1 || pairing > 3)
                return -1;
            int partner = PreferredPartner(pairing),
                first = -1,
                second = -1;
            for (int id = 2; id <= 4; id++)
                if (id != partner)
                {
                    if (first < 0)
                        first = id;
                    else
                        second = id;
                }

            return slot == 0 ? 1
                : slot == 1 ? partner
                : slot == 2 ? first
                : second;
        }

        public void EditSlots()
        {
            if (pairing == 4)
                return;
            int[] chosen = new int[4];
            for (int i = 0; i < 4; i++)
                chosen[i] = DisplaySlot(i);
            pairSlots = chosen;
            pairing = 4;
        }

        public bool Put(int slot, int soldier)
        {
            if (slot < 0 || slot >= 4 || soldier < 1 || soldier > 4)
                return false;
            if (pairing != 4)
                EditSlots();
            if (pairSlots == null || pairSlots.Length != 4)
                return false;
            for (int i = 0; i < 4; i++)
                if (i != slot && pairSlots[i] == soldier)
                    return false;
            pairSlots[slot] = soldier;
            return true;
        }

        public bool Clear(int slot)
        {
            if (slot < 0 || slot >= 4)
                return false;
            if (pairing != 4)
                EditSlots();
            if (pairSlots == null || pairSlots.Length != 4)
                return false;
            pairSlots[slot] = -1;
            return true;
        }
    }

    public static class FormationAI
    {
        public static FormationSettings Settings(Squad q)
        {
            return q.Formation ?? new FormationSettings();
        }

        public static bool HasPreference(Squad q)
        {
            var f = q.Formation;
            return f != null
                && f.Valid()
                && (
                    f.pairing != 0
                    || f.width != 1
                    || f.depth != 1
                    || f.positions[1] != 0
                    || f.positions[2] != 0
                    || f.positions[3] != 0
                    || f.positions[4] != 0
                );
        }

        public static bool PairPreferred(Squad q, int a, int b)
        {
            var f = q.Formation;
            if (f == null || !f.Valid() || f.pairing == 0 || q.LeaderId != 0)
                return false;
            if (a < 1 || a > 4 || b < 1 || b > 4 || a == b)
                return false;
            if (f.pairing == 4)
            {
                return f.ManualComplete()
                    && (
                        a == f.pairSlots[0] && b == f.pairSlots[1]
                        || a == f.pairSlots[1] && b == f.pairSlots[0]
                        || a == f.pairSlots[2] && b == f.pairSlots[3]
                        || a == f.pairSlots[3] && b == f.pairSlots[2]
                    );
            }

            int partner = FormationSettings.PreferredPartner(f.pairing);
            return (a == 1 && b == partner)
                || (b == 1 && a == partner)
                || (a != 1 && a != partner && b != 1 && b != partner);
        }

        public static float LateralMultiplier(Squad q)
        {
            return q.Formation == null ? 1f
                : q.Formation.width == 0 ? .72f
                : q.Formation.width == 2 ? 1.32f
                : 1f;
        }

        public static float DepthMultiplier(Squad q)
        {
            return q.Formation == null ? 1f
                : q.Formation.depth == 0 ? .76f
                : q.Formation.depth == 2 ? 1.35f
                : 1f;
        }

        public static int Position(Squad q, Soldier s)
        {
            var f = q.Formation;
            return f == null || f.positions == null || s.Id == q.LeaderId || s.Id >= f.positions.Length || s.Id < 0
                ? 0
                : f.positions[s.Id];
        }

        // Personal bias is clamped to the shared width/depth envelope.
        public static V2 Desired(BattleSimulation b, Soldier s, V2 wanted, bool flank)
        {
            var q = b.Team(s);
            var f = q.Formation;
            if (f == null || !f.Valid())
                return wanted;
            var axis = PositionLogic.Axis(b) * (s.Side == Side.Green ? 1f : -1f);
            var lateral = new V2(-axis.Y, axis.X);
            int role = Position(q, s);
            // Higher depth allows a rearmost soldier to follow more loosely; it does
            // not cause automatic withdrawal when the position already contributes fire.
            float lane = flank ? 0f : (f.width - 1) * 1.45f;
            float side = (s.Id % 2 == 0 ? 1f : -1f);
            float fore =
                role < 0 ? (1.9f + (f.depth - 1) * .35f)
                : role > 0 ? -(1.9f + (f.depth - 1) * 1.25f)
                : 0f;
            if (role == 0 && f.depth == 2 && s.Id != q.LeaderId)
                fore = -.45f;
            if (role == 0 && f.depth == 0 && s.Id != q.LeaderId)
                fore = .35f;
            return wanted + axis * fore + lateral * lane * side;
        }

        // Slight tactical preference for front fighters; specialists still hold
        // their usual weapon/safety roles and the leader remains free to coordinate.
        public static int MovePriority(Squad q, Soldier s)
        {
            int pos = Position(q, s);
            return pos < 0 ? -5
                : pos > 0 ? 6
                : 0;
        }

        public static float PreferredSupportDistance(Squad q)
        {
            return 5.5f * DepthMultiplier(q);
        }
    }
}
