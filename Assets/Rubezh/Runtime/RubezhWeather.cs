using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        Texture2D rainStreak;

        void EnsureRainStreak()
        {
            if (rainStreak == null)
            {
                rainStreak = new Texture2D(8, 32, TextureFormat.RGBA32, false);
                var pixels = new Color[8 * 32];
                for (int y = 0; y < 32; y++)
                {
                    int x = 1 + y * 5 / 32;
                    float fade = 1f - System.Math.Abs(y - 15.5f) / 16f;
                    pixels[y * 8 + x] = new Color(.68f, .79f, .88f, fade * .48f);
                    pixels[y * 8 + x + 1] = new Color(.68f, .79f, .88f, fade * .16f);
                }

                rainStreak.SetPixels(pixels);
                rainStreak.Apply();
                uiTextures.Add(rainStreak);
            }
        }

        const int LightWidth = 320,
            LightHeight = 200;
        const float NightDarkness = .28f;
    }
}
