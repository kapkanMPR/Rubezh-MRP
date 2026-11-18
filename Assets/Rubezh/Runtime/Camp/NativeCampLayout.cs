using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        static IEnumerable<VisualElement> Descendants(VisualElement root)
        {
            yield return root;
            foreach (var c in root.Children())
                foreach (var d in Descendants(c))
                    yield return d;
        }

        void ApplyResponsiveMetrics()
        {
            bool landscape = vw >= 590 && vw > vh, shortLandscape = landscape && vh <= 540;
            foreach (var e in Descendants(main).ToArray())
            {
                if (e.ClassListContains("card-portrait"))
                    e.style.height = Mathf.Clamp(vh * .16f, 90, 160);
                if (e.ClassListContains("class-image"))
                    e.style.height = Mathf.Clamp(vh * .12f, 86, 150);
                if (e.ClassListContains("sector-seal"))
                {
                    float n = landscape && vh <= 520 ? 105 : Mathf.Clamp(vh * .23f, 102, 218);
                    e.style.width = n;
                    e.style.height = n;
                }

                if (e.ClassListContains("op-badge"))
                {
                    float n = landscape && vh <= 520 ? 45 : Mathf.Clamp(vh * .13f, 65, 112);
                    e.style.width = n;
                    e.style.height = n;
                }

                if (e.ClassListContains("doll") && landscape)
                    e.style.height = shortLandscape ? Mathf.Clamp(vh * .31f, 107, 165) : Mathf.Clamp(vh * .24f, 112, 245);
                if (e.ClassListContains("slot") && landscape)
                    e.style.width = Mathf.Clamp(vw * .1f, 62, 110);
                if (e.ClassListContains("slot-label") && shortLandscape)
                    e.style.fontSize = Mathf.Clamp(vh * .023f, 9, 12);
                if (e.ClassListContains("slot-image") && landscape)
                    e.style.height = Mathf.Clamp(vh * .042f, 18, 42);
                if (e.ClassListContains("bag-cells"))
                    e.style.maxWidth = ((e.childCount + 1) / 2) * ((landscape ? Mathf.Clamp(vw * .06f, 46, 70) : 70) + 4);
                if (e.ClassListContains("pocket") && landscape)
                {
                    e.style.width = Mathf.Clamp(vw * .06f, 46, 70);
                    e.style.height = Mathf.Clamp(vh * .08f, 35, 62);
                }

                if (e.ClassListContains("pocket-image") && landscape)
                    e.style.height = Mathf.Clamp(vh * .04f, 16, 34);
                if (e.ClassListContains("pocket-text") && landscape)
                    e.style.fontSize = Mathf.Clamp(vh * .0145f, 8, 12);
                if (e.ClassListContains("member") && shortLandscape)
                    e.style.height = Mathf.Clamp(vh * .088f, 27, 45);
                if (e.ClassListContains("member-name") && shortLandscape)
                    e.style.fontSize = Mathf.Clamp(vh * .0245f, 10, 13);
                if (e.ClassListContains("prep-name") && shortLandscape)
                    e.style.fontSize = Mathf.Clamp(vh * .045f, 16, 24);
                if (e.ClassListContains("row") && e.childCount > 0)
                    e[e.childCount - 1].style.marginRight = 0;
            }
        }

        void SizeModal(VisualElement box, bool shipment = false)
        {
            if (box.ClassListContains("equipment-modal"))
            {
                box.EnableInClassList("equipment-phone", vw < 480);
                box.style.width = Mathf.Min(840, vw - 16);
                box.style.maxWidth = vw - 16;
                box.style.maxHeight = vh - 16;
                return;
            }
            if (box.ClassListContains("fighter-info"))
            {
                box.style.width = Mathf.Min(520, vw - 20);
                box.style.maxWidth = vw - 20;
                box.style.maxHeight = vh - 20;
                return;
            }
            if (box.ClassListContains("currency-drawer"))
            {
                box.style.width = Mathf.Min(380, vw - 20);
                box.style.maxWidth = vw - 20;
                box.style.maxHeight = vh - 20;
                return;
            }
            box.style.width = shipment ? Mathf.Min(760, vw - 20) : Mathf.Min(820, vw * .9f);
            box.style.maxWidth = vw - 20;
            box.style.maxHeight = shipment ? vh - 20 : vh <= 600 && vw > 650 ? vh - 16 : vh * .88f;
            if (shipment)
                box.style.height = Mathf.Min(530, vh - 20);
        }
    }
}
