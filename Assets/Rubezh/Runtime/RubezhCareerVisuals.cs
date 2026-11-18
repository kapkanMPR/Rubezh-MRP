using System;
using SilentOrders.Core;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {

        // This warning is advisory: it never prevents beginning an operation.
        string SquadCompositionWarning(Kit[] kits)
        {
            bool medic = false, supplier = false;
            if (kits != null)
                foreach (var k in kits)
                {
                    if (k == null)
                        continue;
                    if (k.extra == 2)
                        medic = true;
                    if (k.extra == 3)
                        supplier = true;
                }

            if (!medic && !supplier)
                return "⚠ Нет медика    •    ⚠ Нет снабженца";
            if (!medic)
                return "⚠ В отряде нет медика";
            if (!supplier)
                return "⚠ В отряде нет снабженца";
            return "";
        }

        // Save() handles the existing main save and backup. Settings and quick-battle loadouts are untouched.
        bool ConfirmCareerReset()
        {
            var previous = progress;
            progress = new Progress();
            if (!Save())
            {
                progress = previous;

                return false;
            }

            draft = new Progress();
            battle = null;
            awarded = false;
            paused = false;
            mission = 1;
            kitSelected = 0;
            campaignMode = true;
            screen = 4;
            menuScroll = Vector2.zero;
            detailScroll = Vector2.zero;

            drawer = "";
            pickOptions = null;
            pickApply = null;
            CancelTouchInput();
            message = "Карьера сброшена. Доступна первая операция.";
            return true;
        }

        void DrawCareerResetConfirm()
        {
            // While this dialog is shown the campaign grid is not drawn or interactive.
            Fill(new Rect(0, 0, W, H), bg);
            float width = Mathf.Min(820, W - 100), x = (W - width) * .5f;
            Box(new Rect(x, 280, width, 470));
            Txt(x + 30, 315, width - 60, 65, "СБРОСИТЬ КАРЬЕРУ?", heading);
            Txt(x + 30, 395, width - 60, 155, "Прогресс операций, заработанный опыт, снаряжение кампании и подготовка отряда будут сброшены. Быстрый бой и настройки игры сохранятся.", small);
            Txt(x + 30, 565, width - 60, 55, "Если сохранение уже было, его копия останется в .bak.", caption);
            if (B(x + 30, 655, (width - 80) * .5f, 72, "Отмена", compactButton))
            {

                CancelTouchInput();
            }

            if (B(x + 50 + (width - 80) * .5f, 655, (width - 80) * .5f, 72, "Да, сбросить", danger))
                ConfirmCareerReset();
        }

        bool ShouldShowConcussionStars(Soldier soldier)
        {
            return battle != null && soldier != null && soldier.Alive && soldier.Deployed && ConcussionEffects.Current(battle, soldier) > 0.03f;
        }

        // Small UI-space sprites follow the head in both GPU and high-detail renderers.
        // Their movement never affects simulation or allocates a new texture.
        // Small red crossed badge means psychological collapse (Broken).
    }
}
