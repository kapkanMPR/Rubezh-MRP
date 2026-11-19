using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace Rubezh.UnityPort
{
    [Serializable]
    public sealed class DynamicLayerData
    {
        public int wall;
        public ProceduralVisualData picture;
    }

    [Serializable]
    public sealed class DynamicBuildingData
    {
        public List<StructureClosure> closures = new List<StructureClosure>();
        public int closureVersion;
        public int version;
        public float roofThickness;
        public List<StructureWall> walls = new List<StructureWall>();
        public List<StructureRoof> roof = new List<StructureRoof>();
        public List<StructureFloor> floors = new List<StructureFloor>();
        public List<DynamicLayerData> layers = new List<DynamicLayerData>();

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);

        public void Validate()
        {
            if (
                (version != 1 && version != 2)
                || walls == null
                || walls.Count > 8192
                || roof == null
                || roof.Count > 200000
                || floors == null
                || floors.Count > 50000
                || layers == null
                || layers.Count > walls.Count + 4
            )
                throw new ArgumentException("Некорректный динамический дом");
            foreach (var w in walls)
            {
                if (
                    w == null
                    || !Finite(w.ax)
                    || !Finite(w.ay)
                    || !Finite(w.bx)
                    || !Finite(w.by)
                    || !Finite(w.vax)
                    || !Finite(w.vay)
                    || !Finite(w.vbx)
                    || !Finite(w.vby)
                    || !Finite(w.height)
                    || w.height <= 0
                    || w.height > 10
                    || !Finite(w.thickness)
                    || w.thickness < 0
                    || w.thickness > 2
                    || w.columns == null
                    || w.columns.Count < 1
                    || w.columns.Count > 256
                    || Math.Abs(w.vbx - w.vax + w.vby - w.vay) < .00001f
                )
                    throw new ArgumentException("Некорректная стена дома");
                if (
                    w.window
                    && (
                        !Finite(w.sill)
                        || !Finite(w.lintel)
                        || !Finite(w.windowLo)
                        || !Finite(w.windowHi)
                        || w.windowLo < 0
                        || w.windowHi > 1
                        || w.windowLo > w.windowHi
                        || w.sill > w.lintel
                    )
                )
                    throw new ArgumentException("Некорректное окно дома");
                foreach (var c in w.columns)
                {
                    if (c == null || c.spans == null || c.spans.Count > 64)
                        throw new ArgumentException("Некорректный профиль стены");
                    foreach (var v in c.spans)
                        if (
                            v == null
                            || !Finite(v.lo)
                            || !Finite(v.hi)
                            || v.lo < 0
                            || v.lo > v.hi
                            || v.hi > w.height + .001f
                        )
                            throw new ArgumentException(
                                "Некорректный профиль стены " + w.id + " (высота " + w.height + ")"
                            );
                }
            }
            foreach (var f in floors)
                if (f == null || !Finite(f.x) || !Finite(f.y))
                    throw new ArgumentException("Некорректный пол дома");
            foreach (var r in roof)
                if (
                    r == null
                    || !Finite(r.a)
                    || !Finite(r.b)
                    || !Finite(r.c)
                    || !Finite(r.d)
                    || r.a < 0
                    || r.b < 0
                    || r.c < 0
                    || r.d < 0
                    || r.a > 100
                    || r.b > 100
                    || r.c > 100
                    || r.d > 100
                )
                    throw new ArgumentException("Некорректная крыша дома");
            if (closures != null)
            {
                if (closures.Count > 100000)
                    throw new ArgumentException("Too many closures");
                foreach (var q in closures)
                {
                    if (q == null || q.xyz == null || q.xyz.Length != 12 || q.kind < 0 || q.kind > 1)
                        throw new ArgumentException("Invalid closure");
                    foreach (float v in q.xyz)
                        if (!Finite(v))
                            throw new ArgumentException("Invalid closure coordinate");
                }
            }
            foreach (var l in layers)
                if (l == null || l.wall < -1 || l.wall >= walls.Count || l.picture == null)
                    throw new ArgumentException("Некорректный слой дома");
        }

        public DynamicBuilding Decode()
        {
            Validate();
            if ((version != 1 && version != 2))
                throw new ArgumentException("Неподдерживаемый динамический дом");
            var images = new List<StructureLayer>();
            foreach (var l in layers)
            {
                if (l.wall < -1 || l.wall >= walls.Count)
                    throw new ArgumentException("Слой вне дома");
                images.Add(new StructureLayer { Wall = l.wall, Original = l.picture.Decode() });
            }
            return new DynamicBuilding(walls, roof, floors, images)
            {
                Closures = closures ?? new List<StructureClosure>(),
                RoofThickness = roofThickness > 0 ? roofThickness : .165f,
            };
        }
    }
}
