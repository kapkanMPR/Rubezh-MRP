using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        const string CombatTrainingDescription =
            "Общая тренировка навыков группы и слаженности, в свободное от задачи время, для приобретения новых навыков";
        static readonly string[] CombatSkills =
        {
            "Без дополнительных навыков",
            "Отбегание от гранат; работа от углов высоких стен; плотные двойки; перебрасывание предметов.",
            "Тактическое движение с контролем секторов и развёртыванием по контакту; движение вдоль стены; агрессивное подавление; помощь с перезарядкой; сохранение лечения; предупреждение СВОЯ!; подбор снаряжения погибших.",
            "Сомалийская стрельба; стрельба на ходу; эвакуация вдвоём; отбрасывание гранаты.",
        };

        void DrawCombatTraining(float w, ref float y)
        {
            Paragraph(0, ref y, w - 60, "Боевая тренировка", equipHeading);
            int current = campaignMode ? progress.training[4] : 3;
            Paragraph(
                0,
                ref y,
                w - 60,
                "Уровень " + current + " · " + CombatTraining.Price(current) + " очков всего",
                equipStats
            );
            if (B(0, y, w - 60, 42, "Навыки по уровням", compactSelect))
                Pick(
                    "Боевая тренировка",
                    Enumerable
                        .Range(1, 3)
                        .Select(i =>
                            "Уровень " + i + " (после миссии " + new[] { 0, 6, 14, 17 }[i] + "): " + CombatSkills[i]
                        )
                        .ToArray(),
                    v => { }
                );
            y += 50;
            bool old = GUI.enabled;
            GUI.enabled = old && campaignMode && current < CombatTraining.Max(progress.completed);
            if (B(0, y, w - 60, 56, "Выбрать уровень тренировки", compactSelect))
            {
                var levels = Enumerable.Range(current + 1, CombatTraining.Max(progress.completed) - current).ToArray();
                Pick(
                    "Боевая тренировка",
                    levels
                        .Select(v =>
                            "Уровень " + v + " · доплата " + (CombatTraining.Price(v) - CombatTraining.Price(current))
                        )
                        .ToArray(),
                    v => ConfirmCombatTraining(levels[v])
                );
            }

            GUI.enabled = old;
            y += 65;
        }

        void ConfirmCombatTraining(int level)
        {
            int extra = CombatTraining.Price(level) - CombatTraining.Price(progress.training[4]);
            Pick(
                "Выбор нельзя отменить. Уровень сохранится сразу. Доплата: " + extra + " очков",
                new[] { "Отмена", "Подтвердить уровень " + level },
                choice =>
                {
                    if (choice != 1)
                        return;
                    if (level <= progress.training[4] || level > CombatTraining.Max(progress.completed))
                        return;
                    if (ActiveCost + extra > CampaignProgression.Budget(progress.completed))
                    {
                        message = "Недостаточно свободных очков для боевой тренировки.";
                        return;
                    }

                    var previous = progress;
                    int oldLevel = draft.training[4];
                    draft.training[4] = level;
                    progress = JsonUtility.FromJson<Progress>(JsonUtility.ToJson(draft));
                    if (!Save())
                    {
                        progress = previous;
                        draft.training[4] = oldLevel;
                        return;
                    }

                    message = "Боевая тренировка повышена до уровня " + level + ". Выбор сохранён.";
                }
            );
        }
    }
}
