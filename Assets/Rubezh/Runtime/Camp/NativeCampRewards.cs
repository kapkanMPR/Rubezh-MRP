using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rubezh.Camp;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        string Victory(string mode, int block, int mission, string[] party)
        {
            int level = M.PlayerLevel, story = S.story;
            S.operation = new Operation
            {
                mode = mode,
                block = block,
                mission = mission
            };
            bool won = M.Win(mode, block, mission, party);
            string title = OperationName();
            if (!won)
                return title + "\nМиссия пройдена повторно. Новых наград нет.\nБаланс: ◈ " + Money(S.balance);
            string msg = title + "\nКаждому бойцу состава: +" + CampModel.BattleXp(mode, block, mission) + " опыта.\nПервая победа · опыт игрока +100 · учебные пособия +1\nОчки обеспечения: +" + CampModel.Payout(mode, block, mission) + " · баланс: ◈ " + Money(S.balance);
            if (M.PlayerLevel > level)
                msg += "\nУровень игрока: " + level + " → " + M.PlayerLevel;
            if (mode == "story" && S.story > story)
            {
                var names = M.Data.items.Where(i => !CampModel.Personal(i.id) && i.unlock > story && i.unlock <= S.story).Select(i => i.name).Concat(M.Data.units.Where(u => u.id != "sup" && M.Data.offers.Any(o => o.id == u.id && o.unlock > story && o.unlock <= S.story)).Select(u => "Вербовка: " + u.role)).ToArray();
                if (names.Length > 0)
                    msg += "\nОткрыто сюжетом: " + string.Join(", ", names) + ".";
            }

            string key = mode + "-" + block + "-" + mission;
            if (S.caseHistory.ContainsKey(key))
                S.rewardView = new RewardView
                {
                    key = key,
                    summary = msg,
                    phase = "gear"
                };
            if (mission < (mode == "war" ? 10 : 20) && !S.completed.Contains(mode + "-" + block + "-" + (mission + 1)))
                S.operation.mission = mission + 1;
            return msg;
        }

        public void CompleteOperation(string mode, int block, int mission, string[] party)
        {
            CampModel.Check(Developer, "Только для разработчика");
            string summary = null;
            Change(() => summary = Victory(mode, block, mission, party), () =>
            {
                screen = "operations";
                Render();
                if (S.rewardView != null)
                    DrawShipment(true);
                else
                    RewardSummary(summary);
            });
        }

        // Commit and acknowledge together; presentation never controls reward accounting.
        internal string CommitReceipt(CampReceipt receipt)
        {
            if (receipt == null || string.IsNullOrEmpty(receipt.id) || receipt.id == S.lastCampReceipt)
                return null;
            string summary = null;
            Store.Change(() =>
            {
                if (receipt.kind == "reset")
                    M.Reset();
                else
                {
                    if (receipt.formation != null)
                    {
                        CampModel.Check(receipt.formation.Valid(), "Повреждён замысел построения");
                        S.formation = new Formation
                        {
                            quietWork = receipt.formation.quietWork,
                            pairing = receipt.formation.pairing,
                            pairSlots = receipt.formation.pairSlots == null ? new[]
                            {
                                -1,
                                -1,
                                -1,
                                -1
                            }

                            : (int[])receipt.formation.pairSlots.Clone(),
                            width = receipt.formation.width,
                            depth = receipt.formation.depth,
                            positions = (int[])receipt.formation.positions.Clone()
                        };
                    }

                    CampModel.Check(M.MissionOpen(receipt.mode, receipt.block, receipt.mission), "Некорректный результат операции");
                    S.operation = new Operation
                    {
                        mode = receipt.mode,
                        block = receipt.block,
                        mission = receipt.mission
                    };
                    if (receipt.win)
                        summary = Victory(receipt.mode, receipt.block, receipt.mission, receipt.fighters != null && receipt.fighters.Length > 0 ? receipt.fighters : S.active.ToArray());
                }

                S.lastCampReceipt = receipt.id;
            });
            return summary;
        }

        public void Receive(CampReceipt receipt)
        {
            if (receipt == null || string.IsNullOrEmpty(receipt.id) || receipt.id == S.lastCampReceipt)
                return;
            string summary = CommitReceipt(receipt);
            if (receipt.kind == "reset")
            {
                screen = "home";
                selected = trainee = S.active[0];
                Render();
                return;
            }

            if (receipt.kind == "formation-back")
            {
                screen = "battlePrep";
                Render();
                return;
            }

            screen = "operations";
            Render();
            if (!string.IsNullOrEmpty(receipt.error))
                Toast(receipt.error);
            else if (S.rewardView != null)
                DrawShipment(true);
            else
                RewardSummary(summary ?? "Операция завершена.\nПобеды нет. Награда не выдана.");
        }

        void RewardSummary(string text)
        {
            Modal("Операция завершена", (c, f) =>
            {
                Label(c, text, "paragraph");
                Button(f, "Закрыть", Close, "gold");
            });
        }

        void ResumeReward()
        {
            if (S.rewardView != null && S.caseHistory.ContainsKey(S.rewardView.key))
                DrawShipment(false);
        }

        void AcceptReward(bool barracks = false)
        {
            Change(() => S.rewardView = null, () =>
            {
                Close();
                if (barracks)
                    Navigate("barracks");
            });
        }

        void DrawShipment(bool animate)
        {
            var view = S.rewardView;
            if (view == null || !S.caseHistory.ContainsKey(view.key))
                return;
            var r = S.caseHistory[view.key];
            bool crew = view.phase == "crew", done = view.phase == "done";
            Close();
            reopenModal = () => DrawShipment(false);
            overlay = V("modal-overlay");
            root.Add(overlay);
            var box = V("modal shipment");
            overlay.Add(box);
            SizeModal(box, true);
            var head = V("shipment-head");
            box.Add(head);
            Label(head, "КЕДР / " + (done ? "ВЕДОМОСТЬ" : crew ? "УЧЕБНЫЙ ЦЕНТР" : "СНАБЖЕНИЕ"), "eyebrow");
            var heading = Row(head);
            Label(heading, done ? "Выдача завершена" : crew ? "Прибыло пополнение" : r.book ? "Улучшенный кейс" : "Обычный кейс", "modal-title grow");
            Label(heading, done ? (r.book ? "03 / 03" : "02 / 02") : crew ? "02 / 03" : r.book ? "01 / 03" : "01 / 02", "sub");
            var scroll = Scroll(box, "shipment-scroll");
            if (done)
            {
                Label(scroll, System.Text.RegularExpressions.Regex.Replace(view.summary ?? "", "<[^>]+>", "\n"), "paragraph");
                Label(scroll, "На складе: " + string.Join(", ", r.gear.Select(id => M.Data.Item(id).name)) + ".", "paragraph");
                if (r.book)
                    Label(scroll, "Пополнение: " + string.Join(", ", r.fighters.Select(x => x.name)) + ". Книга повышения уровня ×1.", "paragraph");
                Pending(scroll);
            }
            else
            {
                var stage = V("shipment-stage");
                scroll.Add(stage);
                var crate = V("crate");
                stage.Add(crate);
                var artPrefix = crew ? "truck" : "crate";
                var rear = Art(artPrefix + "-back.png", "crate-layer");
                var lid = Art(artPrefix + "-lid.png", "crate-layer");
                var front = Art(artPrefix + "-front.png", "crate-layer");
                crate.Add(rear);
                crate.Add(lid);
                crate.Add(front);
                lid.style.translate = new Translate(0, -12);
                lid.style.rotate = new Rotate(new Angle(-5));
                var row = Row(scroll, "drop-row");
                int n = crew ? r.fighters.Count : r.gear.Count;
                var info = new Label("Нажми на карточку для осмотра.");
                info.AddToClassList("drop-info");
                for (int i = 0; i < n; i++)
                {
                    int index = i;
                    var card = Button(row, "", () =>
                    {
                        foreach (var e in row.Children())
                            e.RemoveFromClassList("selected");
                        row[index].AddToClassList("selected");
                        if (crew)
                        {
                            var a = r.fighters[index];
                            info.text = a.name + " · " + M.Data.Template(a.template).role + " · ур. " + a.level + ". " + M.Data.Template(a.template).trait + (a.advanced ? " · Усиленная подготовка." : "");
                        }
                        else
                        {
                            var item = M.Data.Item(r.gear[index]);
                            info.text = item.name + " · " + item.stats + ". " + item.desc;
                        }
                    }, "drop-card");
                    card.style.width = Length.Percent(100f / n);
                    if (crew)
                    {
                        var a = r.fighters[i];
                        var u = M.Data.Template(a.template);
                        var pic = Portrait(u, a.look, new Gear());
                        pic.AddToClassList("drop-image");
                        card.Add(pic);
                        Label(card, a.name, "drop-name");
                        Label(card, u.role + " · ур. " + a.level, "drop-caption");
                    }
                    else
                    {
                        var it = M.Data.Item(r.gear[i]);
                        card.Add(Art(it.img, "drop-image"));
                        Label(card, it.name, "drop-name");
                        Label(card, "Осмотреть", "drop-caption");
                    }

                    if (animate && S.motion)
                    {
                        float start = Time.unscaledTime + .52f + i * .11f;
                        float offset = (n - 1) * .5f - i;
                        IVisualElementScheduledItem job = null;
                        job = card.schedule.Execute(() =>
                        {
                            float t = Mathf.Clamp01((Time.unscaledTime - start) / .62f), smooth = 1 - Mathf.Pow(1 - t, 3);
                            card.style.opacity = t;
                            card.style.scale = new Scale(new Vector3(.14f + .86f * smooth, .14f + .86f * smooth, 1));
                            card.style.translate = new Translate(offset * (1 - smooth) * card.resolvedStyle.width, -48 * (1 - smooth));
                            if (t >= 1)
                                job.Pause();
                        }).Every(16);
                        modalAnimations.Add(job);
                    }
                }

                scroll.Add(info);
                if (animate && S.motion)
                {
                    float started = Time.unscaledTime;
                    IVisualElementScheduledItem job = null;
                    job = crate.schedule.Execute(() =>
                    {
                        float t = Mathf.Clamp01((Time.unscaledTime - started) / .36f);
                        crate.style.opacity = t;
                        crate.style.translate = new Translate(0, -14 * (1 - t));
                        float open = Mathf.Clamp01((Time.unscaledTime - started - .32f) / .45f);
                        lid.style.translate = new Translate(0, -12 * open);
                        lid.style.rotate = new Rotate(new Angle(-5 * open));
                        if (t >= 1 && open >= 1)
                            job.Pause();
                    }).Every(16);
                    modalAnimations.Add(job);
                }
            }

            var foot = Row(box, "shipment-foot");
            Label(foot, done ? "Награды учтены" : crew ? "Личные дела" : "Склад отряда", "sub grow");
            if (!done && animate && S.motion)
                Button(foot, "Без анимации", () =>
                {
                    PauseAnimations();
                    DrawShipment(false);
                });
            if (done)
            {
                if (S.pendingRecruits.Count > 0)
                    Button(foot, "В казарму", () => AcceptReward(true), "gold");
                Button(foot, "Принять", () => AcceptReward(), "gold");
            }
            else
                Button(foot, !crew && r.book ? "К бойцам →" : "Итоги →", () => Change(() => S.rewardView.phase = !crew && r.book ? "crew" : "done", () => DrawShipment(true)), "gold");
        }
    }
}
