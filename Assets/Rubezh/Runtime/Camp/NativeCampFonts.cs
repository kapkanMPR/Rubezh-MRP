using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        FontAsset campFont, symbolFont;
        void InitializeFonts()
        {
            var font = Resources.Load<Font>("FieldCamp/Fonts/CampSans");
            var symbols = Resources.Load<Font>("Fonts/RubezhSans");
            if (font == null || symbols == null)
                throw new System.InvalidOperationException("Нет шрифтов лагеря");
            campFont = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            symbolFont = FontAsset.CreateFontAsset(symbols, 90, 9, GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic, true);
            campFont.fallbackFontAssetTable = new List<FontAsset>
            {
                symbolFont
            };
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(campFont);
        }

        void ReleaseFonts()
        {
            foreach (var font in new[]
            {
                campFont,
                symbolFont
            }

            )
            {
                if (font == null)
                    continue;
                foreach (var texture in font.atlasTextures)
                    if (texture != null)
                        Destroy(texture);
                if (font.material != null)
                    Destroy(font.material);
                Destroy(font);
            }

            campFont = symbolFont = null;
        }
    }
}
