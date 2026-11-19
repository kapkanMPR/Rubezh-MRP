using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        BattleMap geometryMap;
        Rect geometryCoverage;
        int geometryRevision = -1,
            geometrySerial;
        public int SceneColorBuilds { get; private set; }
        public int SceneColorHits { get; private set; }
        public int SceneGeometryHits { get; private set; }
        public long SceneColorBytes => UnityStaticColorBytes;

        public bool ReuseScene(BattleMap map)
        {
            if (
                !NativeScene
                || geometryMap != map
                || geometryRevision != map.Revision
                || geometryCoverage != depthCoverage
            )
                return false;
            foreach (var batch in objectDraws)
                batch.LastFrame = pictureFrame;
            foreach (var batch in exteriorDraws)
                batch.LastFrame = pictureFrame;
            SceneGeometryHits++;
            return true;
        }

        // Geometry reuse and exact-resolution static colour reuse are separate caches.
        void ReleaseSceneColor()
        {
            geometryMap = null;
            geometryRevision = -1;
        }
    }
}
