using System;
using UnityEngine;

namespace Rubezh.UnityPort
{
    // Optional Input System adapter supplies the same samples without a mandatory package dependency.
    public struct TouchSample
    {
        public int fingerId;
        public Vector2 position;
        public TouchPhase phase;
    }

    public static class TouchInputSource
    {
        public static Func<int> CountOverride;
        public static Func<int, TouchSample> ReadOverride;
        public static int Count
        {
            get
            {
                if (CountOverride != null)
                    return CountOverride();
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
                return Input.touchCount;
#else
    return 0;
#endif
            }
        }

        public static TouchSample Read(int index)
        {
            if (ReadOverride != null)
                return ReadOverride(index);
#if ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            var t = Input.GetTouch(index);
            return new TouchSample
            {
                fingerId = t.fingerId,
                position = t.position,
                phase = t.phase
            };
#else
   return default(TouchSample);
#endif
        }
    }
}
