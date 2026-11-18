using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rubezh.Camp;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        void Training()
        {
            HubBackdrop("preparation-range-background.png");
            var page = V("list-page training-page");
            main.Add(page);
            var bar = Row(page, "training-strip wrap");
            Button(bar, "←", Back);
            Label(bar, M.Soldier(trainee).name, "strong");
            FighterLine(bar, trainee, true);
            Button(bar, "Откат навыков", () => Confirm("Сбросить навыки?", "Вернутся методички: " + M.RefundCount(trainee) + ". Очки от уровня останутся у бойца. Снаряжение из лишних ячеек вернётся на склад.", "Сбросить навыки", () => M.ResetSkills(trainee))).SetEnabled(M.Person(trainee).skills.Count > 0 || M.RefundCount(trainee) > 0);
            var scroll = Scroll(page, "tree-scroll", ScrollViewMode.VerticalAndHorizontal);
            var graph = V("skill-tree");
            scroll.Add(graph);
            var visible = M.Data.skills.Where(k => string.IsNullOrEmpty(k.role) || k.role == M.Soldier(trainee).role).ToArray();
            var columns = visible.Select(k => k.col).Distinct().OrderBy(x => x).ToArray();
            Func<float, float> left = col => 16 + Array.IndexOf(columns, col) * 104;
            Func<float, float> top = row => 16 + row * 94;
            graph.style.width = columns.Length * 104 + 16;
            graph.style.height = (visible.Max(k => k.row) + 1) * 94 + 16;
            graph.generateVisualContent += ctx =>
            {
                var painter = ctx.painter2D;
                int edge = 0;
                foreach (var k in visible)
                for (int i = 0; i < k.pre.Length; i++, edge++)
                {
                    var a = M.Data.Skill(k.pre[i]);
                    float sx = left(a.col) + 26, sy = top(a.row) + 52;
                    float tx = left(k.col) + 26 + (i - (k.pre.Length - 1) * .5f) * 10, ty = top(k.row);
                    float exit = sy + 8 + (edge % 4) * 5;
                    float entry = ty - 8 - (edge % 4) * 5;
                    float lane = left(a.col) + 68 + (edge % 4) * 5;
                    painter.strokeColor = Hex(M.Has(trainee, a.id) ? 0xd6b35e : 0x64736b);
                    painter.lineWidth = M.Has(trainee, a.id) ? 2f : 1.3f;
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(sx, sy));
                    if (a.col == k.col && k.row == a.row + 1)
                        painter.LineTo(new Vector2(sx, entry));
                    else
                    {
                        painter.LineTo(new Vector2(sx, exit));
                        painter.LineTo(new Vector2(lane, exit));
                        painter.LineTo(new Vector2(lane, entry));
                        painter.LineTo(new Vector2(tx, entry));
                    }
                    painter.LineTo(new Vector2(tx, ty));
                    painter.Stroke();
                    // Arrowhead at the dependent skill, never over the icon.
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(tx - 3, ty - 5));
                    painter.LineTo(new Vector2(tx, ty - 1));
                    painter.LineTo(new Vector2(tx + 3, ty - 5));
                    painter.Stroke();
                }
            };
            foreach (var k in visible)
            {
                bool owned = M.Has(trainee, k.id), available = M.Available(trainee, k);
                var b = Button(graph, "", () =>
                {
                    treeScroll = scroll.scrollOffset;
                    NodeInfo(k.id);
                }, "skill-node " + (owned ? "taken" : available ? "ready" : "locked"));
                b.name = "skill-" + k.id;
                b.style.left = left(k.col);
                b.style.top = top(k.row);
                var icon = Art("skills103/" + k.id + ".png", "skill-icon");
                if (owned) icon.tintColor = Hex(0xffdf83);
                b.Add(icon);
                Label(b, k.level.ToString(), "skill-level");
                if (owned) Label(b, "✓", "skill-check");
            }

            scroll.schedule.Execute(() => scroll.scrollOffset = treeScroll);
        }

        static Color Hex(int rgb, float alpha = 1)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);
        }

        void NodeInfo(string id, Action returnToCard = null)
        {
            var k = M.Data.Skill(id);
            Modal(k.name, (c, f) =>
            {
                var p = M.Person(trainee);
                Label(c, M.Soldier(trainee).name + " · ЛИЧНЫЙ НАВЫК", "eyebrow");
                Label(c, k.desc, "paragraph");
                Stat(c, "Уровень бойца", p.level + " / " + k.level);
                if (k.pre.Length == 0)
                    Label(c, "Предыдущие навыки не требуются.", "sub paragraph");
                else
                {
                    Label(c, "Необходимые навыки  " + k.pre.Count(x => M.Has(trainee, x)) + " / " + k.pre.Length, "h3");
                    foreach (var prev in k.pre)
                    {
                        bool owned = M.Has(trainee, prev);
                        var row = Row(c, "requirement " + (owned ? "met" : "missing"));
                        Label(row, owned ? "✓" : "×");
                        Label(row, M.Data.Skill(prev).name, "grow strong");
                        Label(row, owned ? "Освоен" : "Не освоен", "sub");
                    }
                }

                if(returnToCard != null) Button(f, "← К бойцу", returnToCard);
                Label(c, "Стоимость: 1 очко · в наличии: " + p.points, "paragraph");
                Button(f, M.Has(trainee, id) ? "Освоено" : "Изучить", () => Change(() => M.Spend(trainee, id), () =>
                {
                    Close();
                    Toast("Навык освоен");
                }), "gold").SetEnabled(M.Available(trainee, k));
            });
        }
    }
}
