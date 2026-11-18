using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        float UIScale
        {
            get
            {
                return Mathf.Max(.001f, Mathf.Min(Screen.safeArea.height / H, Screen.safeArea.width / 1400f));
            }
        }

        void CancelTouchInput()
        {
            CancelUnityTestPointers();
        }

        void ZoomAt(Vector2 anchor, float value)
        {
            LayoutMap();
            float u = (anchor.x - mapRect.x) / mapRect.width, v = (anchor.y - mapRect.y) / mapRect.height;
            zoom = Mathf.Clamp(value, .65f, 3);
            LayoutMap();
            pan += new Vector2(anchor.x - mapRect.x - u * mapRect.width, anchor.y - mapRect.y - v * mapRect.height);
        }

        void SelectAt(Vector2 point, bool inspect)
        {
            LayoutMap();
            float best = 65;
            bool found = false;
            foreach (var unit in battle.Soldiers)
            {
                if (!unit.Deployed || !battle.Fog.CanDraw(unit))
                    continue;
                var p = ToView(painter == null ? unit.Position : painter.VisualPosition(unit));
                p.y -= 20 * mapRect.height / MapHeight;
                float distance = Vector2.Distance(p, point);
                if (distance < best)
                {
                    best = distance;
                    selected = unit.Id;
                    found = true;
                }
            }

            if (found && inspect)
            {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
                NativeDrawer("journal");
#else
                drawer = "journal";
                drawerScroll = Vector2.zero;
#endif
            }
        }

        // Unrouted IMGUI helpers remain for editor-side compatibility only.
        // Runtime gestures are owned entirely by UI Toolkit pointer capture.
        void TouchGroup(Rect r) { GUI.BeginGroup(r); }
        void EndTouchGroup() { GUI.EndGroup(); }
        bool TouchButton(Rect r, string text, GUIStyle style) { return GUI.Button(r, text, style); }
        Vector2 TouchScroll(string id, Rect viewport, Vector2 position, Rect content)
        {
            return GUI.BeginScrollView(viewport, position, content);
        }
        void EndTouchScroll() { GUI.EndScrollView(); }
    }
}
