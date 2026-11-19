using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    public static class DecorLevels
    {
        public static bool Place(RubezhLevel l, int x, int y, string id, bool mirrored = false)
        {
            var a = StudioCatalog.Find(id);
            if (a == null || !a.Decor)
                return false;
            int count = 0;
            LevelObjectCell support = null;
            foreach (var p in l.objects)
            {
                if (!RubezhLevel.Contains(p, x, y))
                    continue;
                var pa = StudioCatalog.Find(p.assetId, p.mirrored);
                if (pa != null && pa.Decor)
                {
                    count++;
                    continue;
                }

                if (pa == null || pa.SupportHeight <= 0)
                    return false;
                support = p;
            }

            if (count >= 3)
                return false;
            if (support != null && string.IsNullOrEmpty(support.uid))
                support.uid = Guid.NewGuid().ToString();
            l.objects.Add(
                new LevelObjectCell
                {
                    uid = Guid.NewGuid().ToString(),
                    parentUid = support == null ? "" : support.uid,
                    assetId = id,
                    mirrored = mirrored,
                    x = x,
                    y = y,
                    offsetX =
                        count == 1 ? -.25f
                        : count == 2 ? .25f
                        : 0,
                    offsetY =
                        count == 1 ? -.15f
                        : count == 2 ? .15f
                        : 0,
                }
            );
            return true;
        }

        public static bool ValidOffset(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= .45f;

        public static bool Valid(RubezhLevel l, out string error)
        {
            error = "";
            var uids = new HashSet<string>();
            var occupied = new Dictionary<int, int>();
            foreach (var o in l.objects)
            {
                if (o == null)
                    continue;
                if (!string.IsNullOrEmpty(o.uid) && !uids.Add(o.uid))
                {
                    error = "Повтор идентификатора объекта.";
                    return false;
                }
            }

            foreach (var o in l.objects)
            {
                if (o == null)
                    continue;
                var a = StudioCatalog.Find(o.assetId, o.mirrored);
                if (a == null || !a.Decor)
                    continue;
                int key = o.y * l.columns + o.x;
                int count;
                occupied.TryGetValue(key, out count);
                occupied[key] = count + 1;
                if (
                    !l.Inside(o.x, o.y)
                    || count >= 3
                    || !ValidOffset(o.offsetX)
                    || !ValidOffset(o.offsetY)
                    || o.rotationDegrees != 0
                    || o.scaleX != 0 && o.scaleX != 1
                    || o.scaleY != 0 && o.scaleY != 1
                )
                {
                    error = "Неверное размещение декора.";
                    return false;
                }

                LevelObjectCell support = null;
                foreach (var p in l.objects)
                {
                    if (p == o || p == null || !RubezhLevel.Contains(p, o.x, o.y))
                        continue;
                    var pa = StudioCatalog.Find(p.assetId, p.mirrored);
                    if (pa != null && pa.Decor)
                        continue;
                    if (support != null)
                    {
                        error = "Пересечение опор.";
                        return false;
                    }

                    support = p;
                }

                if (string.IsNullOrEmpty(o.parentUid))
                {
                    if (support != null)
                    {
                        error = "Декор внутри объекта без привязки к поверхности.";
                        return false;
                    }
                }
                else if (
                    support == null
                    || support.uid != o.parentUid
                    || StudioCatalog.Find(support.assetId)?.SupportHeight <= 0
                    || StudioCatalog.Find(support.assetId) == null
                )
                {
                    error = "Нет подходящей опоры под декором.";
                    return false;
                }
            }

            return true;
        }
    }
}
