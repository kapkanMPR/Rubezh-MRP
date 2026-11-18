using System;
using System.IO;
using System.Text;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEditor;

namespace Rubezh.UnityPort.Editor
{
    public static class HdGpuValidation
    {
        [MenuItem("Рубеж/Проверки/Сравнить GPU и CPU")]
        public static void Compare()
        {
            EditorSpritePacks.All();
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Validation/HD-GPU"));
            Directory.CreateDirectory(directory);
            var report = new StringBuilder("HD GPU/CPU comparison; actual Unity readback.\nInspect PNG pairs, especially cover edges and sprite orientation.\n");
            var painter = new BattleRenderer();
            var battle = new BattleSimulation(410);
            Rubezh.Graphics.AddLookPages(new System.Collections.Generic.HashSet<int>(), battle);
            Rubezh.Graphics.BakeLooks(battle);
            using (var gpu = new HdGpuRenderer(painter.Canvas))
            {
                foreach (int tick in new[]
                {
                    0,
                    80,
                    200
                }

                )
                {
                    while (battle.TickNumber < tick && battle.Outcome == Outcome.Running)
                        battle.Step();
                    painter.Render(battle, false, 0);
                    uint[] reference = (uint[])painter.Canvas.Pixels.Clone();
                    gpu.Render(painter, battle, false, 0, BattleRenderer.OutputWidth, BattleRenderer.OutputHeight);
                    var previous = RenderTexture.active;
                    var readback = new Texture2D(painter.Canvas.Width, painter.Canvas.Height, TextureFormat.RGBA32, false, true);
                    var cpu = new Texture2D(painter.Canvas.Width, painter.Canvas.Height, TextureFormat.RGBA32, false, true);
                    try
                    {
                        RenderTexture.active = (RenderTexture)gpu.Texture;
                        readback.ReadPixels(new Rect(0, 0, readback.width, readback.height), 0, 0);
                        readback.Apply();
                        var actual = readback.GetPixels32();
                        var expected = new Color32[reference.Length];
                        double sum = 0;
                        int changed = 0, w = readback.width, h = readback.height;
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                uint p = reference[y * w + x];
                                int i = (h - 1 - y) * w + x;
                                expected[i] = new Color32((byte)p, (byte)(p >> 8), (byte)(p >> 16), 255);
                                var a = actual[i];
                                var e = expected[i];
                                int delta = Math.Abs(a.r - e.r) + Math.Abs(a.g - e.g) + Math.Abs(a.b - e.b);
                                sum += delta;
                                if (delta > 24)
                                    changed++;
                            }

                        cpu.SetPixels32(expected);
                        cpu.Apply();
                        File.WriteAllBytes(Path.Combine(directory, tick + "-CPU.png"), cpu.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(directory, tick + "-GPU.png"), readback.EncodeToPNG());
                        report.AppendLine("tick=" + tick + " mean channel error=" + (sum / (reference.Length * 3)).ToString("F4") + " pixels with RGB delta >24=" + (100.0 * changed / reference.Length).ToString("F3") + "% quads=" + gpu.QuadCount + " map uploads=" + gpu.MapUploads);
                    }
                    finally
                    {
                        RenderTexture.active = previous;
                        UnityEngine.Object.DestroyImmediate(readback);
                        UnityEngine.Object.DestroyImmediate(cpu);
                    }
                }
            }

            File.WriteAllText(Path.Combine(directory, "comparison.txt"), report.ToString());
            Debug.Log(report.ToString() + "\n" + directory);
            EditorUtility.RevealInFinder(directory);
        }
    }
}
