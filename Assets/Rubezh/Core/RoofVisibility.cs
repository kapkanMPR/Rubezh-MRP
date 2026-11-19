using System;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    public static class RoofVisibility
    {
        public const float Radius = 5,
            FadeStart = 3.25f,
            InsideOpacity = .5f;

        public static void Actors(BattleSimulation b, List<V2> list)
        {
            list.Clear();
            foreach (var s in b.Soldiers)
            {
                if (!s.Deployed || !s.Alive || !b.Fog.CanDraw(s))
                    continue;
                bool inside = false;
                if (b.Map.DynamicBuildings != null)
                    foreach (var h in b.Map.DynamicBuildings)
                        if (h.Contains(s.Position))
                        {
                            inside = true;
                            break;
                        }
                if (inside && list.Count < 128)
                    list.Add(s.Position);
            }
        }

        public static float Opacity(V2 p, List<V2> actors)
        {
            float d = float.MaxValue;
            foreach (var a in actors)
                d = Math.Min(d, V2.Distance(p, a));
            float t = Util.Clamp((d - FadeStart) / (Radius - FadeStart), 0, 1);
            return InsideOpacity + (1 - InsideOpacity) * t * t * (3 - 2 * t);
        }
    }
}
