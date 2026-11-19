namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        int directSubmissionStart;
        public int InterpolatedOrderChanges { get; private set; }

        static int CompareDirectDepth(NativeDraw a, NativeDraw b)
        {
            int order = a.Stage.CompareTo(b.Stage);
            if (order == 0 && a.Stage < 2)
                order = a.Depth.CompareTo(b.Depth);
            return order == 0 ? a.Key.CompareTo(b.Key) : order;
        }

        void UpdateInterpolatedOrder()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            // Only a depth crossing changes transparent draw order. Translation within
            // an already sorted sequence does not trigger sorting or material submission.
            bool crossed = false;
            NativeDraw previous = null;
            foreach (var draw in nativeDraws)
            {
                ActorTransform actor;
                if (draw.Actor >= 0 && actorTransforms.TryGetValue(draw.Actor / 4, out actor) && actor.Initialized)
                    draw.Depth =
                        draw.Pose.Data.x + actor.Display.Depth + (draw.ParentKind == 2 ? actor.Display.WeaponDepth : 0);
                if (previous != null && CompareDirectDepth(previous, draw) > 0)
                    crossed = true;
                previous = draw;
            }
            if (!crossed)
                return;
            nativeDraws.Sort(CompareDirectDepth);
            DirectSorts++;
            InterpolatedOrderChanges++;
            int order = directSubmissionStart;
            foreach (var draw in nativeDraws)
            {
                SceneNode node;
                if (unityActorNodes.TryGetValue(draw.Key, out node) && node.Order != order * 2)
                {
                    node.Order = order * 2;
                    node.Renderer.sortingOrder = node.Order;
                }
                order++;
            }
#endif
        }
    }
}
