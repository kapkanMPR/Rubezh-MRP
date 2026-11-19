using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        Texture2D ghostFigure;
        const int FogWidth = 480,
            FogHeight = 300;

        void ReleaseBattleOverlayTextures()
        {
            foreach (var image in new[] { ghostFigure, rainStreak })
                if (image != null)
                {
                    uiTextures.Remove(image);
                    Destroy(image);
                }
            ghostFigure = rainStreak = null;
        }

        void SaveVisibility()
        {
            PlayerPrefs.SetInt("rubezh.visibility", fullVisibility ? 1 : 0);
            PlayerPrefs.SetInt("rubezh.hardcoreFog", hardcoreFog ? 1 : 0);
            PlayerPrefs.Save();
            ApplyVisibility();
            renderedBattle = null;
        }

        void ApplyVisibility()
        {
            if (battle == null)
                return;
            battle.Fog.Enabled = !fullVisibility;
            battle.Fog.Hardcore = hardcoreFog;
            battle.Fog.Update(battle);
            if (
                !fullVisibility
                && selected >= 0
                && selected < battle.Soldiers.Count
                && !battle.Fog.CanDraw(battle.Soldiers[selected])
            )
                selected = battle.Green.LeaderId;
        }

        void EnsureGhostFigure()
        {
            if (ghostFigure == null)
            {
                ghostFigure = new Texture2D(12, 24, TextureFormat.RGBA32, false);
                var pixels = new Color[288];
                for (int y = 0; y < 24; y++)
                for (int x = 0; x < 12; x++)
                {
                    bool head = (x - 5.5f) * (x - 5.5f) + (y - 19) * (y - 19) < 8;
                    bool body = y >= 7 && y < 17 && x >= 3 && x <= 8;
                    bool legs = y < 8 && (x == 3 || x == 4 || x == 7 || x == 8);
                    bool arms = y >= 9 && y < 16 && (x == 1 || x == 2 || x == 9 || x == 10);
                    pixels[y * 12 + x] = new Color(.8f, .24f, .2f, head || body || legs || arms ? 1 : 0);
                }

                ghostFigure.SetPixels(pixels);
                ghostFigure.Apply();
                ghostFigure.filterMode = FilterMode.Bilinear;
                uiTextures.Add(ghostFigure);
            }
        }
    }
}
