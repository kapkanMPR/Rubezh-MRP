// Compile contracts only: not an Android JNI or Editor runtime.
using System;

namespace UnityEngine
{
    public class WaitForEndOfFrame { }
}

namespace UnityEngine.Profiling
{
    public static class Profiler
    {
        public static long GetTotalAllocatedMemoryLong() => 0;

        public static long GetTotalReservedMemoryLong() => 0;

        public static long GetMonoUsedSizeLong() => 0;

        public static long GetAllocatedMemoryForGraphicsDriver() => 0;

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void BeginSample(string s) { }

        [System.Diagnostics.Conditional("ENABLE_PROFILER")]
        public static void EndSample() { }
    }
}
