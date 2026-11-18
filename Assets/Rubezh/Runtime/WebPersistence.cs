using System.Runtime.InteropServices;

namespace Rubezh.UnityPort
{
    public static class WebPersistence
    {
#if UNITY_WEBGL && !UNITY_EDITOR
  [DllImport("__Internal")] static extern void RubezhSyncSave();
#endif
        public static void Flush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
   RubezhSyncSave();
#endif
        }
    }
}
