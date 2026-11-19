using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using SilentOrders.Core;

namespace Rubezh
{
    public static partial class Graphics
    {
        static readonly HashSet<int> missingSprites = new HashSet<int>();
        public static int MissingSpriteCount => missingSprites.Count;
        public static int LastMissingSprite { get; private set; } = -1;
        static readonly byte[] missingPixels = new byte[4];

        public static bool ReportMissingSprite(int id)
        {
            LastMissingSprite = id;
            return missingSprites.Add(id);
        }

        public static void ValidateSceneSprites(HashSet<int> required)
        {
            foreach (int id in required)
                if (!HasSprite(id))
                    throw new InvalidDataException("Scene sprite unavailable before battle: " + id);
        }

        static readonly HashSet<int> loadedPages = new HashSet<int>(),
            partialPages = new HashSet<int>();
        public static bool PackedMode { get; private set; }

        public static void ConfigurePacks()
        {
            lock (publishGate)
            {
                if (PackedMode && atlas != null)
                    return;
                System.Threading.Interlocked.Increment(ref generation);
                atlas = new Tex[SpritePackIndex.Count][];
                PackedMode = true;
                loadedPages.Clear();
                partialPages.Clear();
                missingSprites.Clear();
                LastMissingSprite = -1;
                OpenAtlas = null;
            }

            ClearStampCache();
        }

        public static bool HasSprite(int id)
        {
            id = PackedMode ? SpritePackIndex.Resolve(id) : id;
            var a = atlas;
            return a != null && id >= 0 && id < a.Length && a[id] != null;
        }

        public static bool HasPage(int page)
        {
            lock (publishGate)
                return loadedPages.Contains(page);
        }

        public static int LoadedPageCount
        {
            get
            {
                lock (publishGate)
                    return loadedPages.Count + partialPages.Count;
            }
        }

        static bool IsLookSprite(int id) =>
            (id >= GreenLook.LayerStart && id < RedBeltSprite) || PawnAtlas.Contains(id);

        public static bool HasPage(int page, HashSet<int> looks, HashSet<int> required = null)
        {
            lock (publishGate)
            {
                if (loadedPages.Contains(page))
                    return true;
                if (looks == null || !partialPages.Contains(page))
                    return false;
                if (required != null)
                    foreach (int id in required)
                        if (SpritePackIndex.Page[id] == page && !HasSpritePixels(id))
                            return false;
                foreach (int id in looks)
                    if (SpritePackIndex.Page[id] == page && !HasSpritePixels(id))
                        return false;
                return true;
            }
        }

        public static void ReleaseSceneSprites()
        {
            KeepPages(new HashSet<int>());
            wallMaterialApplied.Clear();
        }

        // Keep only selected appearance variants; all non-appearance content remains page-owned.
        public static void KeepLookSprites(HashSet<int> looks)
        {
            lock (publishGate)
            {
                if (!PackedMode)
                    return;
                for (int id = GreenLook.LayerStart; id < atlas.Length; id++)
                    if (IsLookSprite(id) && atlas[id] != null && !looks.Contains(id))
                    {
                        atlas[id] = null;
                        int page = SpritePackIndex.Page[id];
                        loadedPages.Remove(page);
                        partialPages.Add(page);
                    }
            }

            ClearStampCache();
        }

        public static long LoadedPixelBytes
        {
            get
            {
                long n = 0;
                var a = atlas;
                if (a != null)
                    foreach (var s in a)
                        if (s != null)
                            foreach (var t in s)
                                n += t.P.Length;
                return n;
            }
        }

        public static void KeepPages(HashSet<int> keep)
        {
            lock (publishGate)
            {
                if (!PackedMode)
                    return;
                for (int i = 0; i < atlas.Length; i++)
                    if (!keep.Contains(SpritePackIndex.Page[i]))
                        atlas[i] = null;
                loadedPages.RemoveWhere(p => !keep.Contains(p));
                partialPages.RemoveWhere(p => !keep.Contains(p));
            }

            ClearStampCache();
        }

        public static HashSet<int> CommonPages()
        {
            var p = new HashSet<int>();
            for (int i = 0; i < 223; i++)
                if (
                    !(i >= WholeSoldierStart && i < LoadoutSoldierStart + 72 && i != KedrBadge)
                    && SpritePackIndex.Page[SpritePackIndex.Resolve(i)] >= 0
                )
                    AddPage(p, i);
            p.Add(274);
            AddPage(p, 6824);
            AddPage(p, VogSprite);
            AddPage(p, WeaponAppearance.RareAk);
            return p;
        }

        static void AddPage(HashSet<int> pages, int id)
        {
            id = SpritePackIndex.Resolve(id);
            if (id < 0 || id >= SpritePackIndex.Count || SpritePackIndex.Page[id] < 0)
                throw new InvalidDataException("Removed or unknown sprite: " + id);
            pages.Add(SpritePackIndex.Page[id]);
        }

        static void AddAsset(HashSet<int> pages, StudioAsset a)
        {
            if (a == null)
                return;
            foreach (var s in a.Sprites)
                AddPage(pages, s.Id);
            if (a.Caps != null)
                foreach (var c in a.Caps)
                    AddPage(pages, c.Sprite.Id);
            AddDebris(pages, a.DebrisKey);
            if (a.DebrisExtraKeys != null)
                foreach (var key in a.DebrisExtraKeys)
                    AddDebris(pages, key);
        }

        static void AddDebris(HashSet<int> pages, string key)
        {
            if (string.IsNullOrEmpty(key))
                return;
            int[] list;
            if (DebrisCatalog.Small.TryGetValue(key, out list))
                foreach (int id in list)
                    AddPage(pages, DebrisCatalog.Sprite(id));
            if (DebrisCatalog.Large.TryGetValue(key, out list))
                foreach (int id in list)
                    AddPage(pages, DebrisCatalog.Sprite(id));
        }

        public static HashSet<int> PagesFor(BattleMap map)
        {
            var pages = CommonPages();
            if (map != null && map.DynamicBuildings != null)
            {
                AddDebris(pages, "glass");
                AddDebris(pages, "concrete");
                foreach (var h in map.DynamicBuildings)
                foreach (var w in h.Walls)
                    AddDebris(
                        pages,
                        w.Source.material == "wood" ? "planks"
                            : w.Source.material == "brick" ? "red-brick"
                            : "concrete"
                    );
            }
            if (map != null)
                for (int tree = 101; tree <= 104; tree++)
                    AddAsset(pages, StudioCatalog.Find("legacy-" + tree, false));
            if (map != null && map.Studio != null)
            {
                foreach (var a in map.Studio.Ground)
                    AddAsset(pages, a);
                foreach (var o in map.Studio.Objects)
                    AddAsset(pages, o.Asset);
                foreach (var o in map.Studio.Decorations)
                    AddAsset(pages, o.Asset);
            }

            return pages;
        }

        public static HashSet<int> SceneSprites(BattleSimulation sim)
        {
            var ids = new HashSet<int>();
            // Common IDs, not whole pack pages: a page can also contain unrelated HD props.
            for (int id = 0; id < 223; id++)
                if (!(id >= WholeSoldierStart && id < LoadoutSoldierStart + 72 && id != KedrBadge))
                {
                    int resolved = SpritePackIndex.Resolve(id);
                    if (SpritePackIndex.Page[resolved] >= 0)
                        ids.Add(resolved);
                }
            Action<int> add = id =>
            {
                id = SpritePackIndex.Resolve(id);
                if (id < 0 || id >= SpritePackIndex.Count || SpritePackIndex.Page[id] < 0)
                    throw new InvalidDataException("Scene references removed sprite: " + id);
                ids.Add(id);
            };
            Action<string> debris = key =>
            {
                if (string.IsNullOrEmpty(key))
                    return;
                int[] list;
                if (DebrisCatalog.Small.TryGetValue(key, out list))
                    foreach (int id in list)
                        add(DebrisCatalog.Sprite(id));
                if (DebrisCatalog.Large.TryGetValue(key, out list))
                    foreach (int id in list)
                        add(DebrisCatalog.Sprite(id));
            };
            Action<StudioAsset> asset = a =>
            {
                if (a == null)
                    return;
                foreach (var s in a.Sprites)
                    add(s.Id);
                if (a.Caps != null)
                    foreach (var c in a.Caps)
                        add(c.Sprite.Id);
                debris(a.DebrisKey);
                if (a.DebrisExtraKeys != null)
                    foreach (var key in a.DebrisExtraKeys)
                        debris(key);
            };
            // Reserve every possible future fragment of the materials actually in this scene.
            if (sim.Map.DynamicBuildings != null)
            {
                debris("glass");
                debris("concrete");
                foreach (var h in sim.Map.DynamicBuildings)
                foreach (var w in h.Walls)
                {
                    string m = w.Source.material;
                    debris(
                        m == "wood" || m == "logs" || m == "burnt-wood" || m == "burnt-logs" ? "planks"
                        : m == "brick" ? "red-brick"
                        : "concrete"
                    );
                }
            }
            add(WeaponAppearance.RareAk);
            add(WeaponAppearance.EpicAk);
            foreach (int id in LooseIds)
                add(id);
            for (int type = 0; type < AmmoArtCount; type++)
                add(AmmoId(type));
            add(VogSprite);
            add(GrenadeSprite);
            add(TourniquetSprite);
            for (int id = 6825; id <= 6830; id++)
                add(id);
            for (int tree = 101; tree <= 104; tree++)
                asset(StudioCatalog.Find("legacy-" + tree, false));
            if (sim.Map.Studio != null)
            {
                foreach (var a in sim.Map.Studio.Ground)
                    asset(a);
                foreach (var o in sim.Map.Studio.Objects)
                    asset(o.Asset);
                foreach (var o in sim.Map.Studio.Decorations)
                    asset(o.Asset);
            }
            foreach (int id in LookSprites(sim))
                add(id);
            return ids;
        }

        public static void KeepRequiredSprites(HashSet<int> required)
        {
            lock (publishGate)
            {
                if (!PackedMode)
                    return;
                for (int id = 0; id < atlas.Length; id++)
                    if (atlas[id] != null && !required.Contains(id))
                    {
                        atlas[id] = null;
                        int page = SpritePackIndex.Page[id];
                        loadedPages.Remove(page);
                        partialPages.Add(page);
                    }
            }
            ClearStampCache();
        }

        // Decode in a worker; atomically publish only a complete page. No Unity API here.
        public static void LoadPage(
            int page,
            byte[] bytes,
            Func<bool> cancelled = null,
            HashSet<int> looks = null,
            HashSet<int> required = null
        )
        {
            int epoch = System.Threading.Volatile.Read(ref generation);
            var items = new Dictionary<int, Tex[]>();
            bool complete = true;
            byte[] skip = new byte[8192];
            using (var stream = new MemoryStream(bytes, false))
            using (var zip = new GZipStream(stream, CompressionMode.Decompress))
            using (var r = new BinaryReader(zip))
            {
                int format = r.ReadInt32();
                bool legacy = format == 0x50475152;
                if (!legacy && format != 0x32475152)
                    throw new InvalidDataException("Invalid sprite page");
                int count = r.ReadInt32();
                if (count < 1 || count > 32)
                    throw new InvalidDataException("Invalid page count");
                for (int n = 0; n < count; n++)
                {
                    if (cancelled != null && cancelled())
                        throw new OperationCanceledException();
                    int id = r.ReadInt32(),
                        levels = r.ReadInt32();
                    if (
                        id < 0
                        || id >= SpritePackIndex.Count
                        || SpritePackIndex.Page[id] != page
                        || levels < 1
                        || levels > 32
                    )
                        throw new InvalidDataException("Invalid page entry");
                    bool wanted =
                        (looks == null || !IsLookSprite(id) || looks.Contains(id))
                        && (required == null || required.Contains(id));
                    if (!wanted)
                        complete = false;
                    var mip = wanted ? new Tex[levels] : null;
                    for (int j = 0; j < levels; j++)
                    {
                        int w = r.ReadInt32(),
                            h = r.ReadInt32();
                        if (w < 1 || h < 1 || w > 8192 || h > 8192)
                            throw new InvalidDataException("Invalid sprite size");
                        int size = checked(w * h * 4);
                        if (!wanted)
                        {
                            int left = size;
                            while (left > 0)
                            {
                                int nread = r.Read(skip, 0, Math.Min(skip.Length, left));
                                if (nread <= 0)
                                    throw new EndOfStreamException();
                                left -= nread;
                            }

                            continue;
                        }

                        var pixels = r.ReadBytes(size);
                        if (pixels.Length != size)
                            throw new EndOfStreamException();
                        if (legacy)
                        {
                            for (int k = 0; k < size; k += 4)
                            {
                                int a = pixels[k + 3];
                                if (id < 139 && pixels[k] <= a && pixels[k + 1] <= a && pixels[k + 2] <= a)
                                    continue;
                                for (int c = 0; c < 3; c++)
                                    pixels[k + c] = (byte)((pixels[k + c] * a + 127) / 255);
                            }
                        }

                        mip[j] = new Tex
                        {
                            W = w,
                            H = h,
                            P = pixels,
                        };
                    }

                    if (wanted)
                        items.Add(id, mip);
                }
            }

            lock (publishGate)
            {
                if (epoch != generation || !PackedMode || cancelled != null && cancelled())
                    throw new OperationCanceledException();
                foreach (var item in items)
                    atlas[item.Key] = item.Value;
                if (complete)
                {
                    loadedPages.Add(page);
                    partialPages.Remove(page);
                }
                else if (!loadedPages.Contains(page))
                    partialPages.Add(page);
            }
        }

        static int ReadyId(int id)
        {
            if (PackedMode)
                id = SpritePackIndex.Resolve(id);
            if (!HasSprite(id))
                throw new InvalidOperationException("Sprite not preloaded: " + id);
            return id;
        }
    }
}
