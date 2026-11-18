// Compile-only APIs, not native rendering.
namespace UnityEngine.Rendering {public enum ShadowCastingMode{Off}public enum LightProbeUsage{Off}public enum ReflectionProbeUsage{Off}}
namespace UnityEngine.SceneManagement {
 public struct Scene {public bool isLoaded;public bool IsValid()=>true;}
 public static class SceneManager {public static Scene CreateScene(string name)=>new Scene{isLoaded=true};public static void MoveGameObjectToScene(UnityEngine.GameObject obj,Scene scene){}public static void UnloadSceneAsync(Scene scene){} }
}
namespace UnityEngine.UIElements {
 public class ClickEvent:EventBase {}
 public class PointerDownEvent:EventBase {public object target;public int pointerId;public UnityEngine.Vector3 position;public void StopPropagation(){}}
 public class PointerMoveEvent:PointerDownEvent{}public class PointerUpEvent:PointerDownEvent{}public class PointerCancelEvent:PointerDownEvent{}
 public class WheelEvent:EventBase {public UnityEngine.Vector2 mousePosition;public UnityEngine.Vector3 delta;public void StopPropagation(){}}
 public static class PointerCaptureExtensions {public static void CapturePointer(this VisualElement v,int id){}public static void ReleasePointer(this VisualElement v,int id){} }
}
