using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Non-Unity simulation code only ever sees the original LowCover / Wall tiles.
    // These ids select a VISUAL for a tile; they never introduce a new ballistic class.
    public enum LevelObjectKind
    {
        None = 0,
        LowCoverOriginal = 1,
        LowCoverSandbags = 2,
        LowCoverBarrel = 3,
        LowCoverConcrete = 4,
        HighWallOriginal = 5,
        TreeTall = 101,
        TreeRound = 102,
        TreeSparse = 103,
        TreeWide = 104,
        BushSmall = 105,
        BushDense = 106,
        BushSparse = 107,
        BushTall = 108,
        RockBoulder = 109,
        RockSlab = 110,
        RockPile = 111,
        RockJagged = 112,
        CrateWood = 113,
        CrateMilitary = 114,
        BarrelOlive = 115,
        BarrelRust = 116,
        SandbagShortA = 117,
        SandbagShortB = 118,
        SandbagLongA = 119,
        SandbagLongB = 120,
        SandbagCornerA = 121,
        SandbagCornerB = 122,
        SandbagBrokenA = 123,
        SandbagBrokenB = 124,
    }

    [Serializable]
    public class LevelRoofCell
    {
        public int x,
            y;
    }

    public enum LevelGroundKind
    {
        Ground = 0,
        Road = 1,
        Rubble = 2,
    }

    [Serializable]
    public class LevelObjectCell
    {
        public int x,
            y;
        public bool mirrored;
        public string assetId,
            uid,
            parentUid;
        public float offsetX,
            offsetY;
        public LevelObjectKind kind;

        // Render-only adjustments. Never change the occupied tile or its cover physics.
        public float rotationDegrees;
        public float scaleX = 1f,
            scaleY = 1f;

        public static float SafeScale(float scale)
        {
            return scale <= 0f ? 1f : Mathf.Clamp(scale, .4f, 2.5f);
        }
    }

    [Serializable]
    public class LevelGroundCell
    {
        public int x,
            y;
        public bool mirrored;
        public string assetId;
        public LevelGroundKind kind;
    }

    [CreateAssetMenu(menuName = "Рубеж/Уровень", fileName = "RubezhLevel")]
    public sealed class RubezhLevel : ScriptableObject
    {
        [Header("Назначение")]
        public List<DynamicBuildingData> dynamicBuildings = new List<DynamicBuildingData>();
        public ProceduralVisualData terrainVisual;
        public List<ProceduralVisualData> proceduralVisuals = new List<ProceduralVisualData>();
        public int proceduralCombatVersion;
        public List<ProceduralCombatCell> proceduralCombatCells = new List<ProceduralCombatCell>();
        public List<LevelRoofCell> proceduralRoofArea = new List<LevelRoofCell>();
        public List<LevelRoofCell> proceduralRoofCells = new List<LevelRoofCell>();
        public List<string> proceduralWallIds = new List<string>();
        public int formatVersion = 6;
        public List<LevelRoofCell> roof = new List<LevelRoofCell>();

        public void SetRoof(int x, int y, bool value)
        {
            if (!Inside(x, y))
                return;
            if (roof == null)
                roof = new List<LevelRoofCell>();
            roof.RemoveAll(p => p.x == x && p.y == y);
            if (value)
                roof.Add(new LevelRoofCell { x = x, y = y });
        }

        public bool enabledInGame = true;
        public bool customDefenders;

        [Header("Транспортная высадка — не больше одной машины для каждой стороны")]
        public bool greenVehicleArrival;

        [Header("Брифинг кампании — переопределение текста операции")]
        public bool overrideBriefing;

        [TextArea(2, 6)]
        public string displayName = "";
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string operationDescription = "";

        [TextArea(2, 6)]
        public string expectedThreats = "";
        public List<DefenderSpawn> defenders = new List<DefenderSpawn>();
        public int campaignMission = 1; // 0 = only sandbox / quick battle, 1..20 = campaign slot.
        public bool useInQuickBattle = true;

        [Header("Карта в клетках")]
        public int columns = 60,
            rows = 36,
            theme = 2;

        [Header("Сценарные точки (координаты клеток)")]
        public Vector2Int flag = new Vector2Int(51, 18);
        public Vector2Int playerSpawn = new Vector2Int(4, 18);
        public Vector2Int botSpawn = new Vector2Int(54, 18);

        [SerializeField]
        public List<LevelObjectCell> objects = new List<LevelObjectCell>();

        [SerializeField]
        public List<LevelGroundCell> ground = new List<LevelGroundCell>();

        public static bool IsSprite(LevelObjectKind kind)
        {
            return (int)kind >= 101 && (int)kind <= 124;
        }

        public static bool IsHigh(LevelObjectKind kind)
        {
            return kind == LevelObjectKind.HighWallOriginal;
        }

        public static bool IsLow(LevelObjectKind kind)
        {
            return kind != LevelObjectKind.None && !IsHigh(kind);
        }

        public bool Inside(int x, int y)
        {
            return x >= 0 && y >= 0 && x < columns && y < rows;
        }

        public bool IsMarker(int x, int y)
        {
            if (customDefenders && defenders != null)
                foreach (var g in defenders)
                    if (g != null && g.position.x == x && g.position.y == y)
                        return true;
            return flag.x == x && flag.y == y
                || playerSpawn.x == x && playerSpawn.y == y
                || !customDefenders && botSpawn.x == x && botSpawn.y == y;
        }

        public static bool Contains(LevelObjectCell o, int x, int y)
        {
            var a = SilentOrders.Core.StudioCatalog.Find(o.assetId, o.mirrored);
            return x >= o.x && y >= o.y && x < o.x + (a == null ? 1 : a.Width) && y < o.y + (a == null ? 1 : a.Height);
        }

        public bool Fits(LevelObjectCell o)
        {
            var a = SilentOrders.Core.StudioCatalog.Find(o.assetId, o.mirrored);
            return Inside(o.x, o.y)
                && Inside(o.x + (a == null ? 1 : a.Width) - 1, o.y + (a == null ? 1 : a.Height) - 1);
        }

        public bool SetStudioObject(int x, int y, string id, bool mirrored = false)
        {
            var a = SilentOrders.Core.StudioCatalog.Find(id, mirrored);
            if (a == null || !Inside(x, y))
                return false;
            if (a.Decor)
                return DecorLevels.Place(this, x, y, id, mirrored);
            if (a.Ground)
            {
                SetGround(x, y, LevelGroundKind.Ground);
                ground.Add(
                    new LevelGroundCell
                    {
                        x = x,
                        y = y,
                        assetId = id,
                        mirrored = mirrored,
                    }
                );
                return true;
            }

            if (!Inside(x, y) || !Inside(x + a.Width - 1, y + a.Height - 1))
                return false;
            for (int yy = y; yy < y + a.Height; yy++)
            for (int xx = x; xx < x + a.Width; xx++)
                if (a.Blocking && IsMarker(xx, yy))
                    return false;
            for (int yy = y; yy < y + a.Height; yy++)
            for (int xx = x; xx < x + a.Width; xx++)
                SetObject(xx, yy, LevelObjectKind.None);
            objects.Add(
                new LevelObjectCell
                {
                    uid = Guid.NewGuid().ToString(),
                    x = x,
                    y = y,
                    assetId = id,
                    mirrored = mirrored,
                    kind = (LevelObjectKind)(a.Blocking ? (a.High ? 5 : 1) : 0),
                }
            );
            return true;
        }

        public bool MirrorObject(LevelObjectCell obj, out string error)
        {
            var a = SilentOrders.Core.StudioCatalog.Find(obj.assetId, obj.mirrored);
            if (a == null)
            {
                error = "Нет объекта каталога";
                return false;
            }

            Reflect(obj, a);
            if (LevelValidation.Valid(this, out error))
                return true;
            Reflect(obj, SilentOrders.Core.StudioCatalog.Find(obj.assetId, obj.mirrored));
            return false;
        }

        void Reflect(LevelObjectCell obj, SilentOrders.Core.StudioAsset a)
        {
            foreach (var child in objects)
                if (!string.IsNullOrEmpty(obj.uid) && child.parentUid == obj.uid)
                {
                    int x = child.x - obj.x,
                        y = child.y - obj.y;
                    float dx = child.offsetX,
                        dy = child.offsetY;
                    child.x = obj.x + a.Height - 1 - y;
                    child.y = obj.y + a.Width - 1 - x;
                    child.offsetX = -dy;
                    child.offsetY = -dx;
                    child.mirrored = !child.mirrored;
                }

            obj.mirrored = !obj.mirrored;
        }

        public LevelObjectKind ObjectAt(int x, int y)
        {
            if (objects == null)
                return LevelObjectKind.None;
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null && Contains(objects[i], x, y))
                {
                    var a = SilentOrders.Core.StudioCatalog.Find(objects[i].assetId, objects[i].mirrored);
                    if (a != null && a.Decor)
                        continue;
                    return a == null ? objects[i].kind
                        : !a.Blocking ? LevelObjectKind.None
                        : SilentOrders.Core.StudioRules.Cell(a, x - objects[i].x, y - objects[i].y)
                        == SilentOrders.Core.Tile.Wall
                            ? LevelObjectKind.HighWallOriginal
                        : LevelObjectKind.LowCoverOriginal;
                }

            return LevelObjectKind.None;
        }

        public LevelGroundKind GroundAt(int x, int y)
        {
            if (ground == null)
                return LevelGroundKind.Ground;
            for (int i = ground.Count - 1; i >= 0; i--)
                if (ground[i] != null && ground[i].x == x && ground[i].y == y)
                    return ground[i].kind;
            return LevelGroundKind.Ground;
        }

        public void SetObject(int x, int y, LevelObjectKind kind)
        {
            SetObject(x, y, kind, 0f, 1f, 1f);
        }

        public void SetObject(int x, int y, LevelObjectKind kind, float degrees, float scaleX, float scaleY)
        {
            if (!Inside(x, y) || kind != LevelObjectKind.None && IsMarker(x, y))
                return;
            if (objects == null)
                objects = new List<LevelObjectCell>();
            var removed = new HashSet<string>();
            foreach (var o in objects)
                if (o != null && Contains(o, x, y) && !string.IsNullOrEmpty(o.uid))
                    removed.Add(o.uid);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (
                    objects[i] != null
                    && (
                        Contains(objects[i], x, y)
                        || !string.IsNullOrEmpty(objects[i].parentUid) && removed.Contains(objects[i].parentUid)
                    )
                )
                    objects.RemoveAt(i);
            if (kind != LevelObjectKind.None)
                objects.Add(
                    new LevelObjectCell
                    {
                        x = x,
                        y = y,
                        kind = kind,
                        rotationDegrees = Mathf.Clamp(degrees, -180f, 180f),
                        scaleX = LevelObjectCell.SafeScale(scaleX),
                        scaleY = LevelObjectCell.SafeScale(scaleY),
                    }
                );
        }

        public void SetGround(int x, int y, LevelGroundKind kind)
        {
            if (!Inside(x, y))
                return;
            if (ground == null)
                ground = new List<LevelGroundCell>();
            for (int i = ground.Count - 1; i >= 0; i--)
                if (ground[i] != null && ground[i].x == x && ground[i].y == y)
                    ground.RemoveAt(i);
            if (kind != LevelGroundKind.Ground)
                ground.Add(
                    new LevelGroundCell
                    {
                        x = x,
                        y = y,
                        kind = kind,
                    }
                );
        }

        public void Resize(int width, int height)
        {
            columns = Mathf.Clamp(width, 16, 136);
            rows = Mathf.Clamp(height, 12, 80);
            if (objects == null)
                objects = new List<LevelObjectCell>();
            if (ground == null)
                ground = new List<LevelGroundCell>();
            objects.RemoveAll(v => v == null || !Fits(v));
            ground.RemoveAll(v => v == null || !Inside(v.x, v.y));
            if (roof != null)
                roof.RemoveAll(v => v == null || !Inside(v.x, v.y));
            if (defenders != null)
                foreach (var g in defenders)
                    if (g != null)
                        g.position = ClampPoint(g.position);
            flag = ClampPoint(flag);
            playerSpawn = ClampPoint(playerSpawn);
            botSpawn = ClampPoint(botSpawn);
            // Scenario markers remain walkable after resizing.
            SetObject(flag.x, flag.y, LevelObjectKind.None);
            SetObject(playerSpawn.x, playerSpawn.y, LevelObjectKind.None);
            SetObject(botSpawn.x, botSpawn.y, LevelObjectKind.None);
        }

        public Vector2Int ClampPoint(Vector2Int p)
        {
            return new Vector2Int(Mathf.Clamp(p.x, 0, columns - 1), Mathf.Clamp(p.y, 0, rows - 1));
        }

        public void SetFlag(int x, int y)
        {
            flag = ClampPoint(new Vector2Int(x, y));
            SetObject(flag.x, flag.y, LevelObjectKind.None);
        }

        public void SetPlayerSpawn(int x, int y)
        {
            playerSpawn = ClampPoint(new Vector2Int(x, y));
            SetObject(playerSpawn.x, playerSpawn.y, LevelObjectKind.None);
        }

        public void SetBotSpawn(int x, int y)
        {
            botSpawn = ClampPoint(new Vector2Int(x, y));
            SetObject(botSpawn.x, botSpawn.y, LevelObjectKind.None);
        }
    }
}
