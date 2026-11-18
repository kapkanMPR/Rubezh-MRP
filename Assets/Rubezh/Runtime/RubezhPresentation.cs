using System;
using System.Linq;
using System.Collections.Generic;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        SpeechMode speechMode = SpeechMode.Full;
        GUIStyle accuracyHeading, equipNote, equipStats, equipHeading, compactButton, compactChosen, compactSelect, hudText;
        bool rulesOpen, commandHelpOpen;
        float trainingContentHeight = 900;
        Vector2 trainingScroll;
        void InitCompactStyles()
        {
            accuracyHeading = Text(24, accent, true);
            equipNote = Text(20, muted);
            equipStats = Text(20, accent);
            equipHeading = Text(24, ink, true);
            compactButton = new GUIStyle(button)
            {
                fontSize = 21,
                wordWrap = false,
                padding = new RectOffset(9, 9, 6, 6)
            };
            compactChosen = new GUIStyle(chosen)
            {
                fontSize = 21,
                wordWrap = false,
                padding = new RectOffset(9, 9, 6, 6)
            };
            compactSelect = new GUIStyle(compactButton)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            hudText = Text(23, ink);
            hudText.wordWrap = false;
            hudText.alignment = TextAnchor.MiddleCenter;
        }

        string Price(int cost)
        {
            return cost + " опыта";
        }

        void Paragraph(float x, ref float y, float width, string text, GUIStyle style)
        {
            float h = Mathf.Max(style.fontSize + 8, style.CalcHeight(new GUIContent(text), width) + 8);
            Txt(x, y, width, h, text, style);
            y += h;
        }

        void BlockHeader(ref float y, float w, string text)
        {
            Rule(0, y, w);
            y += 12;
            Txt(14, y, w - 28, 35, text, equipHeading);
            y += 45;
        }

        int Stepper(float x, float y, int value, int min, int max)
        {
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && value > min;
            if (B(x, y, 50, 52, "−", compactButton))
                value--;
            GUI.enabled = enabled;
            Txt(x + 54, y + 8, 65, 38, value.ToString(), equipHeading);
            GUI.enabled = enabled && value < max;
            if (B(x + 120, y, 50, 52, "+", compactButton))
                value++;
            GUI.enabled = enabled;
            return value;
        }

        void DrawBagEditor(Kit kit, float w, ref float y)
        {
            y += 12;
            Paragraph(14, ref y, w - 28, "СОДЕРЖИМОЕ РЮКЗАКА · " + kit.BagCost + " / " + BagLimit(kit) + " очков", equipStats);
            Paragraph(14, ref y, w - 28, "Загрузка включена в цену рюкзака. Опыт повторно не списывается.", equipNote);
            var available = Enumerable.Range(0, kit.bag.Length).Where(i => kit.Allows(i) && BagOpen(i)).ToArray();
            foreach (int i in available)
            {
                Txt(14, y, w - 210, 48, UiCatalog.BagNames[i] + " · " + Kit.Prices[i] + " очк./шт.", equipNote);
                int max = kit.bag[i] + Math.Max(0, (BagLimit(kit) - kit.BagCost) / Kit.Prices[i]);
                kit.bag[i] = Stepper(w - 185, y, kit.bag[i], 0, Math.Min(Kit.Caps[i], max));
                y += 62;
            }

            if (B(14, y, (w - 40) / 2, 48, "Разгрузить", compactButton))
                kit.bag = new int[12];
            if (B(26 + (w - 40) / 2, y, (w - 40) / 2, 48, "Рекомендуемый набор", compactButton))
                EquipExtra(kit, kit.extra);
            y += 60;
        }

        void DrawTraining(float x, float y, float w, float h)
        {
            Box(new Rect(x, y, w, h));
            DrawSquadIcon(x + 16, y + 12, 30);
            Txt(x + 58, y + 10, w - 75, 40, "ПОДГОТОВКА ОТРЯДА", equipHeading);
            float contentW = w - 55;
            int columns = contentW >= 850 ? 3 : contentW >= 540 ? 2 : 1;
            float gap = 12, cardW = (contentW - gap * (columns - 1)) / columns;
            float rowY = 0, rowHeight = 0;
            trainingScroll = TouchScroll("training", new Rect(x + 15, y + 60, w - 30, h - 72), trainingScroll, new Rect(0, 0, contentW, trainingContentHeight));
            if (!campaignMode)
            {
                Paragraph(0, ref rowY, contentW, "Прокачки применяются в кампании. Быстрый бой использует базовую подготовку.", equipNote);
                rowY += 10;
            }

            for (int i = 0; i < Progress.TrainingCount; i++)
            {
                if (i > 0 && i % columns == 0)
                {
                    rowY += rowHeight + gap;
                    rowHeight = 0;
                }

                int index = i;
                float cardX = (i % columns) * (cardW + gap);
                TouchGroup(new Rect(cardX, rowY, cardW, 2000));
                float ty = 10;
                if (i == 4)
                {
                    DrawCombatTraining(cardW + 36, ref ty);
                }
                else
                {
                    Paragraph(10, ref ty, cardW - 20, UiCatalog.TrainingNames[i], equipHeading);
                    int level = campaignMode ? draft.training[i] : 0;
                    Paragraph(10, ref ty, cardW - 20, UiCatalog.TrainingEffect(i, level), equipNote);
                    ty += 8;
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && campaignMode && (level > 0 || CampaignProgression.TrainingMax(progress.completed) > 0);
                    if (B(10, ty, cardW - 20, 48, "Уровень " + level + " · " + new[] { 0, 100, 250, 450 }[level] + " опыта", compactSelect))
                        Pick(UiCatalog.TrainingNames[i], Enumerable.Range(0, CampaignProgression.TrainingMax(progress.completed) + 1).Select(v => "Уровень " + v + " · " + new[] { 0, 100, 250, 450 }[v] + " опыта всего · " + UiCatalog.TrainingEffect(index, v)).ToArray(), v => draft.training[index] = v);
                    GUI.enabled = enabled;
                    ty += 56;
                    if (B(10, ty, cardW - 20, 42, "Описание", compactSelect))
                        Pick(UiCatalog.TrainingDescriptions[i], new[] { "Понятно" }, v =>
                        {
                        });
                    ty += 50;
                }

                Rule(0, ty, cardW);
                EndTouchGroup();
                rowHeight = Mathf.Max(rowHeight, ty + 8);
            }

            trainingContentHeight = rowY + rowHeight + 12;
            EndTouchScroll();
        }

        void DrawSquadIcon(float x, float y, float size)
        {
            float u = size / 12;
            for (int i = 0; i < 3; i++)
            {
                Fill(new Rect(x + i * 4 * u, y + (i == 1 ? 0 : 2 * u), 3 * u, 3 * u), accent);
                Fill(new Rect(x + i * 4 * u, y + (i == 1 ? 4 : 6) * u, 3 * u, 5 * u), accent);
            }
        }

        void DrawIcon(Rect r, int kind)
        {
            float x = r.x + 4, y = r.y + 4, w = r.width - 8, h = r.height - 8;
            if (kind == 3)
            {
                for (int i = 0; i < 3; i++)
                    Fill(new Rect(x, y + i * h / 3, w, 3), ink);
                return;
            }

            if (kind == 6)
            {
                Fill(new Rect(x, y, 5, h), ink);
                Fill(new Rect(x + w - 5, y, 5, h), ink);
                return;
            }

            if (kind == 5)
            {
                for (int i = 0; i < 12; i++)
                    Fill(new Rect(x + i * w / 12, y + i * h / 24, w / 12 + 1, h - i * h / 12), ink);
                return;
            }

            if (kind == 7)
            {
                Fill(new Rect(x + w / 2 - 1, y, 3, h), ink);
                Fill(new Rect(x, y + h / 2 - 1, w, 3), ink);
                return;
            }

            if (kind == 8)
            {
                Fill(new Rect(x, y, w, 3), debug ? accent : ink);
                Fill(new Rect(x, y + h - 3, w, 3), debug ? accent : ink);
                Fill(new Rect(x, y, 3, h), debug ? accent : ink);
                Fill(new Rect(x + w - 3, y, 3, h), debug ? accent : ink);
                return;
            }

            for (int i = 0; i < 12; i++)
            {
                float a = i / 11f;
                float px = x + a * w, py = y + (a < .5f ? a : 1 - a) * h;
                if (kind == 4)
                    py = y + h - (py - y);
                if (kind == 1 || kind == 2)
                {
                    px = x + (a < .5f ? a : 1 - a) * w;
                    py = y + a * h;
                    if (kind == 2)
                        px = x + w - (px - x);
                }

                Fill(new Rect(px, py, 3, 3), ink);
            }
        }

        bool IconButton(Rect r, string text, int icon, bool vertical = false)
        {
            bool clicked = TouchButton(r, "", compactButton);
            if (text == "")
                DrawIcon(new Rect(r.x + r.width / 2 - 13, r.y + r.height / 2 - 13, 26, 26), icon);
            else if (vertical)
            {
                DrawIcon(new Rect(r.x + r.width / 2 - 12, r.y + 18, 24, 24), icon);
                GUI.Label(new Rect(r.x + 4, r.y + 60, r.width - 8, 38), text, hudText);
            }
            else
            {
                DrawIcon(new Rect(r.x + 18, r.y + r.height / 2 - 12, 24, 24), icon);
                GUI.Label(new Rect(r.x + 47, r.y, r.width - 55, r.height), text, hudText);
            }

            return clicked;
        }
    }
}
