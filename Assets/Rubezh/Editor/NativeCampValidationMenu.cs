#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace Rubezh.UnityPort.Editor {
 [InitializeOnLoad] public static class NativeCampValidationMenu {
  const string Request="Rubezh.Menu082.Capture";
  static NativeCampValidationMenu(){EditorApplication.playModeStateChanged+=state=>{if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Request,false)){SessionState.SetBool(Request,false);new GameObject("Rubezh menu capture — isolated save").AddComponent<NativeCampCapture>();}};}
  [MenuItem("Рубеж/Проверки/Снимки всех экранов лагеря")]
  public static void Capture(){if(EditorApplication.isPlaying){new GameObject("Rubezh menu capture — isolated save").AddComponent<NativeCampCapture>();return;}SessionState.SetBool(Request,true);EditorApplication.EnterPlaymode();}
 }
}
#endif

