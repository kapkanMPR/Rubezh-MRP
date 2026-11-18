using System;
using SilentOrders.Core;

namespace SilentOrders.Core
{
    public enum ManeuverIntent
    {
        Center,
        Left,
        Right,
        Both,
        LeftHold,
        RightHold,
        Breakthrough, // legacy serialized value 6; migrated to Center
        Straight = 7,
        Front = 8 // wide continuous line; appended to preserve serialized intent ids
    }
}
