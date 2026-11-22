#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        const string RenderComparisonMenu = "Рубеж/Проверки/Сравнить кадры двух путей отрисовки";

        [MenuItem(RenderComparisonMenu, true)]
        static bool CanCompareRenderPaths() => EditorApplication.isPlaying;

        [MenuItem(RenderComparisonMenu)]
        static void CompareRenderPaths()
        {
            var app = UnityEngine.Object.FindFirstObjectByType<RubezhApp>();
            if (app == null || app.hdBattle?.Texture == null || app.battle == null)
                throw new InvalidOperationException("Откройте бой перед сравнением кадров.");
            app.CompareBattleFrames();
        }

        void CompareBattleFrames()
        {
            var renderer = hdBattle;
            var output = (RenderTexture)renderer.Texture;
            var readback = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            var view = renderer.FrameUV;
            bool commands = renderer.UnityCachedStaticCommands;
            bool mergedMasks = renderer.UnityMergedMaskCommands;
            bool cache = renderer.UnityStaticColorCache;
            // Both paths run in this editor callback, at the same simulation tick and interpolation.
            var views = new[]
            {
                view,
                new Rect(view.x + view.width * .023f, view.y + view.height * .019f, view.width, view.height),
                new Rect(view.x + view.width * .1f, view.y + view.height * .1f, view.width * .8f, view.height * .8f),
            };
            void Draw(Rect rect) =>
                renderer.Render(painter, battle, debug, selected, output.width, output.height, rect);
            Color32[] Read()
            {
                RenderTexture.active = output;
                readback.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0, false);
                return readback.GetPixels32();
            }
            try
            {
                renderer.UnityStaticColorCache = false;
                var expectedFrames = new Color32[views.Length][];
                renderer.UnityCachedStaticCommands = renderer.UnityMergedMaskCommands = false;
                for (int i = 0; i < views.Length; i++)
                {
                    Draw(views[i]);
                    expectedFrames[i] = Read();
                }

                renderer.UnityCachedStaticCommands = renderer.UnityMergedMaskCommands = true;
                for (int i = 0; i < views.Length; i++)
                {
                    Draw(views[i]);
                    var actual = Read();
                    var expected = expectedFrames[i];
                    int changed = 0;
                    for (int pixel = 0; pixel < actual.Length; pixel++)
                    {
                        var a = actual[pixel];
                        var b = expected[pixel];
                        if (
                            Math.Abs(a.r - b.r) > 1
                            || Math.Abs(a.g - b.g) > 1
                            || Math.Abs(a.b - b.b) > 1
                            || Math.Abs(a.a - b.a) > 1
                        )
                            changed++;
                    }
                    if (changed == 0)
                        continue;
                    string folder = Path.Combine(Application.temporaryCachePath, "RenderComparison");
                    Directory.CreateDirectory(folder);
                    File.WriteAllBytes(Path.Combine(folder, $"optimized-{i}.png"), readback.EncodeToPNG());
                    readback.SetPixels32(expected);
                    readback.Apply(false);
                    File.WriteAllBytes(Path.Combine(folder, $"reference-{i}.png"), readback.EncodeToPNG());
                    throw new InvalidOperationException($"Вид {i}: расхождение в {changed} пикселях. Кадры: {folder}");
                }
                Debug.Log("Совпали три вида боя. Допуск 1/255 на канал.");
            }
            finally
            {
                renderer.UnityCachedStaticCommands = commands;
                renderer.UnityMergedMaskCommands = mergedMasks;
                renderer.UnityStaticColorCache = cache;
                try
                {
                    Draw(view);
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.Destroy(readback);
                }
            }
        }
    }
}
#endif
