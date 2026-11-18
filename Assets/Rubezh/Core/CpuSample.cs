using System;

namespace SilentOrders.Core
{
    public struct CpuSample : IDisposable
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RUBEZH_BENCH
        public static Action<string> Begin;
        public static Action End;
#endif
        public CpuSample(string name)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RUBEZH_BENCH
            Begin?.Invoke(name);
#endif
        }

        public void Dispose()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || RUBEZH_BENCH
            End?.Invoke();
#endif
        }
    }
}
