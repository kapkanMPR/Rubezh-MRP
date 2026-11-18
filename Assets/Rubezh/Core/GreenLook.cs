using System;
using System.Collections.Generic;
using System.IO;
using SilentOrders.Core;
using SilentOrders.Rendering;

namespace Rubezh
{
    [Serializable]
    public sealed class GreenLook
    {
        public int headId, lightId, familyId, heavyId;
        public static readonly int[] Heads =
        {
            53,
            57,
            59,
            62,
            63,
            64,
            69,
            70,
            74,
            81,
            82,
            84,
            88,
            90,
            98,
            100,
            106,
            107,
            109,
            112,
            113,
            116,
            131,
            133,
            134,
            136,
            143,
            146
        }, Light =
        {
            51,
            54,
            55,
            61,
            62,
            65,
            79,
            81,
            83,
            88,
            97
        }, Families =
        {
            52,
            53,
            58,
            63,
            64,
            69,
            71,
            72,
            75,
            80,
            85,
            89,
            91,
            94,
            95,
            98
        }, Normal = {57,59,63,107,116,131,133,134,136},
        Medical = {88,106,109}, Sniper = {62,69,143};
        // Stable historical slot tables above are retained for saved IDs, not asset selection.
        public static readonly int[] ActiveHeads = {57,59,62,63,69,82,88,106,107,109,116,131,133,134,136,143};
        public const int VisualArmor = 1, VisualRig = 1, VisualFamily = 89;
        public const int UniformBodyStart = 7211; // medium rifleman 89, rig 2, four directions
        public const int LayerStart = 6831, BodyStart = 6943, BakedStart = 7461, BakedEnd = 7861; // exclusive; subsequent IDs are static assets
        static int[] Pool(Role role)
        {
            return role == Role.Sniper ? Sniper : role == Role.Medic ? Medical : Normal;
        }

        public void Ensure(Role role, bool kedr)
        {
            var pool = Pool(role);
            if ((kedr ? headId != 82 : Array.IndexOf(pool, headId) < 0) || Array.IndexOf(Light, lightId) < 0 || Array.IndexOf(Families, familyId) < 0)
            {
                var rng = new Random(Guid.NewGuid().GetHashCode());
                if (kedr)
                    headId = 82;
                else if (Array.IndexOf(pool, headId) < 0)
                    headId = pool[rng.Next(pool.Length)];
                if (Array.IndexOf(Light, lightId) < 0)
                    lightId = Light[rng.Next(Light.Length)];
                if (Array.IndexOf(Families, familyId) < 0)
                    familyId = Families[rng.Next(Families.Length)];
            }
        }

        public int Head(int frame)
        {
            int current = Array.IndexOf(ActiveHeads, headId) >= 0 ? headId : 116;
            return LayerStart + 4 * Array.IndexOf(Heads, current) + frame;
        }

        public int Body(int armor, int rig, int frame)
        {
            // Combat armor/rig levels remain gameplay data; pawn art is shared at every level.
            return UniformBodyStart + Math.Max(0, Math.Min(3, frame));
        }

        public static void Ensure(Soldier s)
        {
            if (s.Look == null)
                s.Look = new GreenLook();
            s.Look.Ensure(s.Role, s.Id == 0 && s.Role == Role.Commander);
        }
    }

    public static partial class Graphics
    {
        public static HashSet<int> LookSprites(BattleSimulation sim)
        {
            // Reinforcements are deployed later and receive new IDs (variant parity).
            var ids = new HashSet<int>();
            for(int id=PawnAtlas.RedStart;id<PawnAtlas.End;id++)ids.Add(id);
            foreach (var soldier in sim.Soldiers)
            {
                if (soldier.Side == Side.Green) GreenLook.Ensure(soldier);
                for (int d = 0; d < 4; d++) ids.Add(PawnAtlas.Sprite(soldier, d));
            }
            return ids;
        }

        public static void AddLookPages(HashSet<int> pages, BattleSimulation sim)
        {
            foreach (int id in LookSprites(sim)) AddPage(pages, id);
        }

        // Kept as a loading lifecycle hook for older callers. All character
        // combinations are now authored offline; no runtime pixel composition.
        public static void BakeLooks(BattleSimulation sim)
        {
            ClearStampCache();
            for (int id = GreenLook.BakedStart; id < Math.Min(GreenLook.BakedEnd, atlas.Length); id++)
                atlas[id] = null;
        }

        static readonly int[] RedWidth =
        {
            134,
            111,
            132,
            112,
            126,
            114,
            126,
            114,
            142,
            108,
            147,
            106
        };
        public static void PersonSprite(PixelCanvas c, int frame, float x, float y, int size, bool enemy, float angle, int armor, int rig, GreenLook look, Role role = Role.Rifleman, int identity = 0)
        {
            if (PrimitiveBattleMode)
            {
                WholeSoldier(c, frame, x, y, size, enemy, angle, armor, rig);
                return;
            }
            frame = Math.Max(0, Math.Min(3, frame));
            int sprite = PawnAtlas.Sprite(look, enemy, role, identity, frame);
            int sourceWidth, sourceHeight;
            SpriteData(sprite, 0, out sourceWidth, out sourceHeight);
            // Runtime sprites are trimmed offline. A shared contact point keeps
            // weapons, feet markers and corpse rotations independent of padding.
            float height = size * (197f / 256f);
            float target = size * (RedWidth[GreenLook.VisualArmor * 4 + frame] / 256f);
            float width = Math.Max(.92f * target, Math.Min(target * 1.10f, height * sourceWidth / sourceHeight));
            float dy = size * (20f / 256f);
            x -= dy * (float)Math.Sin(angle);
            y += dy * (float)Math.Cos(angle);
            Draw(c, sprite, x, y, width, height, enemy, angle);
        }
    }
}
