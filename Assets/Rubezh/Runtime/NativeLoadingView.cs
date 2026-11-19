#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        GameObject nativeLoadingObject;
        UIDocument nativeLoadingDocument;
        PanelSettings nativeLoadingPanel;
        FontAsset nativeLoadingFont;
        Image nativeLoadingImage;
        VisualElement nativeLoadingFade;
        Label nativeLoadingTitle,
            nativeLoadingError;
        Button nativeLoadingRetry;

        void CreateNativeLoading()
        {
            nativeLoadingObject = new GameObject("Loading · UI Toolkit");
            nativeLoadingDocument = nativeLoadingObject.AddComponent<UIDocument>();
            nativeLoadingPanel = ScriptableObject.CreateInstance<PanelSettings>();
            nativeLoadingPanel.scaleMode = PanelScaleMode.ConstantPixelSize;
            nativeLoadingPanel.sortingOrder = 400;
            nativeLoadingPanel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");
            nativeLoadingDocument.panelSettings = nativeLoadingPanel;
            var root = nativeLoadingDocument.rootVisualElement;
            root.style.flexGrow = 1;
            root.style.backgroundColor = new Color(.055f, .065f, .07f);
            var font = Resources.Load<Font>("Fonts/RubezhSans");
            if (font != null)
            {
                nativeLoadingFont = FontAsset.CreateFontAsset(
                    font,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic,
                    true
                );
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(nativeLoadingFont);
            }
            root.style.color = ink;
            var sheet = Resources.Load<StyleSheet>("NativeBattle/Hud");
            if (sheet != null)
                root.styleSheets.Add(sheet);
            nativeLoadingImage = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            root.Add(nativeLoadingImage);
            nativeLoadingTitle = new Label("РУБЕЖ") { pickingMode = PickingMode.Ignore };
            nativeLoadingTitle.style.fontSize = 54;
            nativeLoadingTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(nativeLoadingTitle);
            nativeLoadingFade = new VisualElement { pickingMode = PickingMode.Ignore };
            root.Add(nativeLoadingFade);
            nativeLoadingError = new Label { pickingMode = PickingMode.Ignore };
            nativeLoadingError.style.fontSize = 23;
            nativeLoadingError.style.whiteSpace = WhiteSpace.Normal;
            nativeLoadingError.style.color = muted;
            root.Add(nativeLoadingError);
            nativeLoadingRetry = new Button(StartLoading) { text = "Повторить" };
            nativeLoadingRetry.AddToClassList("battle-button");
            nativeLoadingRetry.style.fontSize = 24;
            root.Add(nativeLoadingRetry);
        }

        partial void UpdateNativeLoading()
        {
            if (!loading)
            {
                HideNativeLoading();
                return;
            }
            if (nativeLoadingObject == null)
                CreateNativeLoading();
            var safe = Screen.safeArea;
            float scale = Mathf.Min(safe.height / H, safe.width / 1400f);
            W = safe.width / scale;
            nativeLoadingPanel.scale = scale;
            var root = nativeLoadingDocument.rootVisualElement;
            root.style.marginLeft = safe.x / scale;
            root.style.marginRight = (Screen.width - safe.xMax) / scale;
            root.style.marginTop = (Screen.height - safe.yMax) / scale;
            root.style.marginBottom = safe.y / scale;
            Texture art =
                loadingFx != null && fireLoadingAvailable
                    ? loadingFx.Render(loadingShown, loadingError == null ? Time.unscaledTime : runnerClock)
                    : null;
            if (nativeLoadingImage.image != art)
                nativeLoadingImage.image = art;
            Place(nativeLoadingImage, 0, 0, W, H);
            nativeLoadingImage.style.display = art != null ? DisplayStyle.Flex : DisplayStyle.None;
            Place(nativeLoadingTitle, W / 2 - 261, H / 2 - 50, 520, 100);
            nativeLoadingTitle.style.display = art == null ? DisplayStyle.Flex : DisplayStyle.None;
            Place(nativeLoadingFade, 0, 0, W, H);
            nativeLoadingFade.style.backgroundColor = new Color(0, 0, 0, art != null ? loadingFade : 0);
            Place(nativeLoadingError, W * .1f, H * .72f, W * .8f, 90);
            if (nativeLoadingError.text != loadingError)
                nativeLoadingError.text = loadingError ?? "";
            nativeLoadingError.style.display = loadingError == null ? DisplayStyle.None : DisplayStyle.Flex;
            Place(nativeLoadingRetry, W / 2 - 150, H * .81f, 300, 48);
            nativeLoadingRetry.style.display = loadingError == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        partial void HideNativeLoading()
        {
            if (loadingFx != null)
                loadingFx.Release();
            if (nativeLoadingObject != null)
                Destroy(nativeLoadingObject);
            if (nativeLoadingPanel != null)
                Destroy(nativeLoadingPanel);
            if (nativeLoadingFont != null)
            {
                foreach (var atlas in nativeLoadingFont.atlasTextures)
                    if (atlas != null)
                        Destroy(atlas);
                if (nativeLoadingFont.material != null)
                    Destroy(nativeLoadingFont.material);
                Destroy(nativeLoadingFont);
            }
            nativeLoadingObject = null;
            nativeLoadingPanel = null;
            nativeLoadingDocument = null;
            nativeLoadingFont = null;
            nativeLoadingImage = null;
            nativeLoadingFade = null;
            nativeLoadingTitle = nativeLoadingError = null;
            nativeLoadingRetry = null;
        }
    }
}
#endif

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        partial void UpdateNativeLoading();

        partial void HideNativeLoading();
    }
}
