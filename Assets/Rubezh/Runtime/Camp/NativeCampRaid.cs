using System;
using Rubezh.Camp;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        void RaidOperationCard(VisualElement board)
        {
            string ended = CloseSavedRaid();
            var p = S.raid;
            var c = Scroll(board, "sector-scroll");
            c.style.maxWidth = 960;
            c.style.minHeight = 0;
            c.style.flexGrow = 1;
            c.style.flexShrink = 1;
            c.style.alignSelf = Align.Center;
            c.style.width = Length.Percent(100);
            var title = Label(c, "РЕЙД", "op-title");
            title.style.fontSize = 22;
            RaidInfo(c, "Начало: бой " + p.Start + "     ·     Рекорд: бой " + p.record, "gold-text");
            string reward =
                p.Start <= p.rewarded
                    ? "За повторные бои награда уже получена. Новая добыча — после боя " + p.rewarded + "."
                    : "Минимальная награда за бой: очки обеспечения +"
                        + RaidProgress.Supply(p.Start)
                        + " · ветошь ×"
                        + RaidProgress.Cloth(p.Start)
                        + (RaidProgress.Parts(p.Start) > 0 ? " · запчасти ×" + RaidProgress.Parts(p.Start) : "");
            RaidInfo(c, reward, "gold-text");
            RaidInfo(
                c,
                "НАГРАДЫ: очки обеспечения и ветошь за новые пройденные бои. Каждые 5 боёв — запчасти. Чем дальше, тем больше награда."
            );
            RaidInfo(
                c,
                "После победы: +25% здоровья, один магазин или 50 патронов пулемёту — не выше вместимости. Погибшие не возвращаются. Каждый бой начинается с исходных позиций."
            );
            RaidInfo(
                c,
                "Каждые 5 боёв — выбор поддержки: лечение, пополнение боезапаса, гранат, медицины и другие варианты. Каждый десятый бой усилен и сохраняет контрольную точку."
            );
            RaidInfo(
                c,
                "При выходе заход завершается: добыча остаётся, следующий рейд начинается с контрольной точки. Повторно награды за уже пройденные бои не выдаются."
            );
            RaidInfo(c, "В наличии: ветошь ×" + p.cloth + " · запчасти ×" + p.parts);
            var actions = Row(board, "raid-actions");
            actions.style.justifyContent = Justify.Center;
            actions.style.flexShrink = 0;
            RaidMenuButton(actions, "Подготовить отряд", () => Navigate("battlePrep"), "gold");
            if (!string.IsNullOrEmpty(p.lastSummary))
                RaidMenuButton(actions, "Итоги последнего рейда", () => RaidSummary(p.lastSummary));
            if (ended != null)
                RaidSummary(ended);
        }

        void RaidInfo(VisualElement parent, string text, string cls = "paragraph")
        {
            var label = Label(parent, text, cls);
            label.style.fontSize = vh < 440 ? 12 : 14;
            label.style.marginTop = 4;
            label.style.marginBottom = 3;
            label.style.flexShrink = 0;
            label.style.maxWidth = Length.Percent(100);
        }

        void RaidMenuButton(VisualElement parent, string text, Action action, string cls = "")
        {
            var button = Button(parent, text, action, cls);
            button.style.width = 220;
            button.style.maxWidth = Length.Percent(100);
            button.style.height = 36;
            button.style.minHeight = 36;
            button.style.flexGrow = 0;
            button.style.alignSelf = Align.Center;
            button.style.fontSize = 14;
            button.style.marginTop = 4;
            button.style.paddingTop = 4;
            button.style.paddingBottom = 4;
        }

        string CloseSavedRaid()
        {
            if (string.IsNullOrEmpty(S.raid.session))
                return null;
            string summary = null;
            // Rewards were credited at wave completion. Closing only consumes the session.
            Store.Change(() => summary = RaidSession.CloseSaved(S.raid));
            return summary;
        }

        public void EndSavedRaid()
        {
            string summary = CloseSavedRaid();
            if (summary != null)
                RaidSummary(summary);
        }

        public void RaidSummary(string summary)
        {
            S.operation.mode = "raid";
            S.operation.mission = S.raid.Start;
            screen = "operations";
            Render();
            Modal(
                "ИТОГИ РЕЙДА",
                (c, f) =>
                {
                    Label(c, summary, "paragraph");
                    Button(f, "В лагерь", Close, "gold");
                }
            );
        }
    }
}
