using System;
using System.Collections.Generic;

namespace Rubezh.UnityPort
{
    public static class LevelValidation
    {
        public static bool Valid(RubezhLevel l, out string error)
        {
            error = "";
            if (l == null || l.columns < 16 || l.columns > 136 || l.rows < 12 || l.rows > 80)
            {
                error = "Размер карты вне диапазона 16–136 × 12–80.";
                return false;
            }

            if (!l.Inside(l.flag.x, l.flag.y) || !l.Inside(l.playerSpawn.x, l.playerSpawn.y))
            {
                error = "Флаг или спавн игрока вне карты.";
                return false;
            }

            if (l.campaignMission < 0 || l.campaignMission > 20 || l.theme < 0 || l.theme > 5)
            {
                error = "Номер миссии: 0–20; тема: 0–5.";
                return false;
            }

            if (l.objects == null || l.ground == null || l.defenders == null)
            {
                error = "В файле отсутствуют списки объектов, поверхности или групп.";
                return false;
            }

            if (!ProceduralCombat.Valid(l, out error))
                return false;
            int centerY = l.rows / 2;
            if (
                l.greenVehicleArrival
                && l.flag.y >= centerY - 2
                && l.flag.y <= centerY + 1
                && l.flag.x >= 0
                && l.flag.x <= 8
            )
            {
                error =
                    "Маршрут ГАЗ Тигр (8×4 клетки) пересекает флаг. Переместите сценарный флаг дальше от левого края.";
                return false;
            }

            bool redTransport = false;
            if (l.customDefenders && l.defenders != null)
                foreach (var g in l.defenders)
                    if (g != null && g.vehicleArrival)
                        redTransport = true;
            if (redTransport && l.flag.y >= centerY - 2 && l.flag.y <= centerY + 1 && l.flag.x >= l.columns - 9)
            {
                error = "Маршрут Humvee пересекает флаг. Переместите сценарный флаг дальше от правого края.";
                return false;
            }

            if (l.overrideBriefing && (l.operationDescription == null || l.expectedThreats == null))
            {
                error = "Брифинг не должен содержать null.";
                return false;
            }

            if (!l.Inside(l.botSpawn.x, l.botSpawn.y))
            {
                error = "Спавн красных вне карты.";
                return false;
            }

            if (!DecorLevels.Valid(l, out error))
                return false;
            // A baked dynamic wall replaces the old catalog object, including its old indestructible flag.
            var replacedWalls =
                l.dynamicBuildings != null && l.dynamicBuildings.Count > 0
                    ? new HashSet<string>(l.proceduralWallIds ?? new List<string>())
                    : new HashSet<string>();
            if (replacedWalls.Count > 0 && (l.greenVehicleArrival || redTransport))
            {
                var laneBlocked = new HashSet<int> { l.flag.y * l.columns + l.flag.x };
                foreach (var c in l.proceduralCombatCells)
                    if (c.height > .02f)
                        laneBlocked.Add(c.y * l.columns + c.x);
                foreach (var o in l.objects)
                {
                    var a = SilentOrders.Core.StudioCatalog.Find(o.assetId, o.mirrored);
                    if (a == null || a.Decor || !a.Indestructible || replacedWalls.Contains(o.uid))
                        continue;
                    for (int yy = o.y; yy < o.y + a.Height; yy++)
                    for (int xx = o.x; xx < o.x + a.Width; xx++)
                        laneBlocked.Add(yy * l.columns + xx);
                }
                if (
                    l.greenVehicleArrival
                        && SilentOrders.Core.VehicleArrivals.ProceduralLane(
                            l.columns,
                            l.rows,
                            SilentOrders.Core.Side.Green,
                            laneBlocked.Contains
                        ) < 0
                    || redTransport
                        && SilentOrders.Core.VehicleArrivals.ProceduralLane(
                            l.columns,
                            l.rows,
                            SilentOrders.Core.Side.Red,
                            laneBlocked.Contains
                        ) < 0
                )
                {
                    error = "Нет свободного маршрута транспорта рядом с процедурными зданиями.";
                    return false;
                }
            }
            var objectCells = new HashSet<int>();
            var groundCells = new HashSet<int>();
            var roofCells = new System.Collections.Generic.HashSet<int>();
            if (l.roof != null)
                foreach (var cell in l.roof)
                    if (cell == null || !l.Inside(cell.x, cell.y) || !roofCells.Add(cell.y * l.columns + cell.x))
                    {
                        error = "Неверная клетка крыши или дубликат.";
                        return false;
                    }

            foreach (var o in l.objects)
            {
                var a = o == null ? null : SilentOrders.Core.StudioCatalog.Find(o.assetId, o.mirrored);
                if (
                    o == null
                    || !l.Fits(o)
                    || (!string.IsNullOrEmpty(o.assetId) && (a == null || a.Ground))
                    || (a == null && ((int)o.kind < 1 || (int)o.kind > 5) && !RubezhLevel.IsSprite(o.kind))
                )
                {
                    error = "Недопустимый объект, дубликат или пересечение со сценарием.";
                    return false;
                }

                if (a != null && a.Decor)
                    continue;
                for (int y = o.y; y < o.y + (a == null ? 1 : a.Height); y++)
                for (int x = o.x; x < o.x + (a == null ? 1 : a.Width); x++)
                    if (
                        a != null
                        && a.Indestructible
                        && !replacedWalls.Contains(o.uid)
                        && (l.greenVehicleArrival && x <= 8 || redTransport && x >= l.columns - 9)
                        && y >= centerY - 2
                        && y <= centerY + 1
                    )
                    {
                        error = "Неразрушимый объект пересекает маршрут высадки транспорта.";
                        return false;
                    }
                    else if (!objectCells.Add(y * l.columns + x) || ((a == null || a.Blocking) && l.IsMarker(x, y)))
                    {
                        error = "Пересечение объектов или сценарной точки.";
                        return false;
                    }

                if (
                    a != null
                    && !a.Legacy
                    && (o.rotationDegrees != 0 || o.scaleX != 1 && o.scaleX != 0 || o.scaleY != 1 && o.scaleY != 0)
                )
                {
                    error = "Для каталога используйте готовую проекцию: масштаб 1, поворот 0.";
                    return false;
                }

                if (
                    float.IsNaN(o.rotationDegrees)
                    || float.IsInfinity(o.rotationDegrees)
                    || o.rotationDegrees < -180
                    || o.rotationDegrees > 180
                    || float.IsNaN(o.scaleX)
                    || float.IsInfinity(o.scaleX)
                    || float.IsNaN(o.scaleY)
                    || float.IsInfinity(o.scaleY)
                    || o.scaleX < 0
                    || o.scaleY < 0
                    || o.scaleX > 2.5f
                    || o.scaleY > 2.5f
                )
                {
                    error = "Неверный масштаб или поворот.";
                    return false;
                }
            }

            foreach (var g in l.ground)
                if (
                    g == null
                    || !l.Inside(g.x, g.y)
                    || !groundCells.Add(g.y * l.columns + g.x)
                    || (
                        !string.IsNullOrEmpty(g.assetId)
                        && (
                            SilentOrders.Core.StudioCatalog.Find(g.assetId, g.mirrored) == null
                            || !SilentOrders.Core.StudioCatalog.Find(g.assetId, g.mirrored).Ground
                        )
                    )
                    || (int)g.kind < 0
                    || (int)g.kind > 2
                )
                {
                    error = "Неверная поверхность или дубликат клетки.";
                    return false;
                }

            if (!l.customDefenders)
                return true;
            if (l.defenders == null || l.defenders.Count == 0 || l.defenders.Count > 50)
            {
                error = "Нужно от 1 до 50 точек; суммарно не более 50 защитников.";
                return false;
            }

            var cells = new HashSet<int>();
            int total = 0,
                vehicles = 0;
            foreach (var g in l.defenders)
            {
                if (
                    g == null
                    || !l.Inside(g.position.x, g.position.y)
                    || g.position == l.playerSpawn
                    || !cells.Add(g.position.y * l.columns + g.position.x)
                )
                {
                    error = "Спавны обороны должны быть внутри карты, уникальными и отдельно от спавна игрока.";
                    return false;
                }

                if (
                    float.IsNaN(g.arrivalSeconds)
                    || float.IsInfinity(g.arrivalSeconds)
                    || g.arrivalSeconds < 0
                    || g.arrivalSeconds > 480
                    || float.IsNaN(g.holdRadius)
                    || float.IsInfinity(g.holdRadius)
                    || g.holdRadius < 2
                    || g.holdRadius > 15
                )
                {
                    error = "Время появления: 0–480 с; радиус удержания: 2–15 клеток.";
                    return false;
                }

                if (
                    g.behavior != DefenderBehavior.Hold
                    && g.behavior != DefenderBehavior.Attack
                    && g.behavior != DefenderBehavior.Pullback
                )
                {
                    error = "Неизвестное поведение группы.";
                    return false;
                }

                if (g.soldiers == null || g.soldiers.Count < 1 || g.soldiers.Count > 5)
                {
                    error = "В группе должно быть 1–5 бойцов.";
                    return false;
                }

                if (g.vehicleArrival)
                {
                    vehicles++;
                    if (g.arrivalSeconds <= 0f)
                    {
                        error = "Транспорт красных доступен только группам с задержкой появления (> 0 с).";
                        return false;
                    }
                }

                total += g.soldiers.Count;
                foreach (var s in g.soldiers)
                    if (s == null || !DefenderLoadout.Valid(s.kit))
                    {
                        error =
                            "Недопустимый комплект в группе «"
                            + g.name
                            + "». Проверьте совместимость и лимит рюкзака 150.";
                        return false;
                    }
            }

            if (vehicles > 1)
            {
                error = "Разрешён только один Humvee на сторону красных.";
                return false;
            }

            if (total > 50)
            {
                error = "Превышен текущий лимит 50 красных бойцов.";
                return false;
            }

            return true;
        }
    }
}
