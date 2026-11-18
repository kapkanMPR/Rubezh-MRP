#if UNITY_EDITOR || DEVELOPMENT_BUILD
using SilentOrders.Core;
using UnityEngine;
using UnityEngine.Profiling;
namespace Rubezh.UnityPort {
 internal static class RubezhCpuProfiler {
  static void Begin(string name){ Profiler.BeginSample(name); }
  static void End(){ Profiler.EndSample(); }
  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  static void Bind(){ CpuSample.Begin=Begin; CpuSample.End=End; }
 }
}
#endif

