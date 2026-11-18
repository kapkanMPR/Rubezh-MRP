using System.Collections;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        RubezhLoadingFx loadingFx;
        float loadingFade;
        bool fireLoadingAvailable;

        void PrepareFireLoading()
        {
            loadingFade = 0;
            if (loadingFx == null) loadingFx = gameObject.AddComponent<RubezhLoadingFx>();
            fireLoadingAvailable = loadingFx.Initialize();
        }

        IEnumerator FinishFireLoading()
        {
            // Completed work only. The loading effect never unlocks a not-yet-ready scene.
            float elapsed = 0;
            while (elapsed < .24f)
            {
                elapsed += Time.unscaledDeltaTime;
                loadingFade = Mathf.Clamp01(elapsed / .24f);
                yield return null;
            }
            HideNativeLoading();
        }

    }
}
