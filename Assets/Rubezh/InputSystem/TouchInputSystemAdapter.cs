#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Rubezh.UnityPort;
using ET = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Rubezh.UnityPort.InputAdapter
{
    public static class TouchInputSystemAdapter
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (!EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
            TouchInputSource.CountOverride = () => ET.activeTouches.Count;
            TouchInputSource.ReadOverride = i =>
            {
                var t = ET.activeTouches[i];
                return new TouchSample
                {
                    fingerId = t.touchId,
                    position = t.screenPosition,
                    phase = Phase(t.phase),
                };
            };
        }

        static UnityEngine.TouchPhase Phase(UnityEngine.InputSystem.TouchPhase phase)
        {
            switch (phase)
            {
                case UnityEngine.InputSystem.TouchPhase.Began:
                    return UnityEngine.TouchPhase.Began;
                case UnityEngine.InputSystem.TouchPhase.Moved:
                    return UnityEngine.TouchPhase.Moved;
                case UnityEngine.InputSystem.TouchPhase.Stationary:
                    return UnityEngine.TouchPhase.Stationary;
                case UnityEngine.InputSystem.TouchPhase.Ended:
                    return UnityEngine.TouchPhase.Ended;
                default:
                    return UnityEngine.TouchPhase.Canceled;
            }
        }
    }
}
#endif
