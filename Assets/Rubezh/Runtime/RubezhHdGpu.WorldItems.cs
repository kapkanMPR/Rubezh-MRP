namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        internal void BeginWorldItem(int id) { visualId = id; StartDirectGroup(id); }
        internal void EndWorldItem()
        {
            DirectGroup group;
            if(directGroups.TryGetValue(visualId,out group))
                for(int i=group.Cursor;i<group.Parts.Count;i++) RemoveWorldNode(group.Parts[i].Key);
            FinishDirectGroup(visualId); visualId = -1;
        }
        void RemoveWorldNode(long key)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            SceneNode node;
            if(unityActorNodes.TryGetValue(key,out node))
            {
                node.Object.SetActive(false);UnityEngine.Object.Destroy(node.Object); unityActorNodes.Remove(key);
            }
#endif
        }
        internal void ReuseWorldItem(int id)
        {
            DirectGroup group;
            if(!directGroups.TryGetValue(id,out group)) return;
            foreach(var draw in group.Parts)
            {
                draw.Seen = directEpoch;
                if(!draw.Listed) { draw.Listed = true; nativeDraws.Add(draw); directOrderDirty = true; }
            }
        }
        internal void RemoveWorldItem(int id)
        {
            DirectGroup group;
            if(!directGroups.TryGetValue(id,out group)) return;
            foreach(var draw in group.Parts)
            {
                if(draw.Listed) { nativeDraws.Remove(draw); draw.Listed = false; directOrderDirty = true; }
                RemoveWorldNode(draw.Key);
            }
            directGroups.Remove(id);
        }
    }
}
