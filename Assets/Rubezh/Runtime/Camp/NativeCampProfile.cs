using System;
using Rubezh.Camp;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        Image Avatar(int head, string cls)
        {
            var image = Art("appearance/head-" + head + "-front.png", cls);
            // Reuse the existing head layer; crop its transparent body-sized margins.
            image.uv = AvatarCrop(head);
            return image;
        }

        void Profile()
        {
            Modal(
                "Профиль игрока",
                (c, f) =>
                {
                    var top = Row(c, "profile-top");
                    var avatar = Button(top, "", AvatarPicker, "profile-avatar-button");
                    avatar.tooltip = "Выбрать аватар";
                    avatar.Add(Avatar(S.avatarHead, "profile-avatar"));
                    var info = V("profile-identity");
                    top.Add(info);
                    var name = Row(info, "profile-name-row");
                    Label(name, S.nickname, "profile-name");
                    Button(name, "✎", RenameDialog, "profile-edit").tooltip = "Изменить ник";
                    int level = M.PlayerLevel;
                    int current = Math.Max(0, S.career.xp - CampModel.Threshold(level));
                    int needed = Math.Max(1, CampModel.Threshold(level + 1) - CampModel.Threshold(level));
                    Label(info, "Уровень " + level, "profile-level");
                    var track = V("profile-xp-track");
                    info.Add(track);
                    var fill = V("profile-xp-fill");
                    track.Add(fill);
                    fill.style.width = Length.Percent(Mathf.Clamp01((float)current / needed) * 100);
                    Label(info, current + " / " + needed + " опыта", "profile-xp-caption");
                    var cards = Row(c, "profile-fighters");
                    var fighters = M.ProfileFighters();
                    for (int i = 0; i < 5; i++)
                    {
                        var card = V("profile-fighter");
                        cards.Add(card);
                        if (i >= fighters.Length)
                        {
                            Label(card, "—", "profile-empty");
                            continue;
                        }
                        var unit = fighters[i];
                        var picture = Portrait(unit);
                        picture.AddToClassList("profile-fighter-picture");
                        card.Add(picture);
                        Label(card, unit.name, "profile-fighter-name");
                        var badge = Art("class-" + RoleIndex(unit.role) + ".png", "profile-fighter-class");
                        badge.tooltip = unit.role;
                        card.Add(badge);
                        Label(card, M.Person(unit.id).level.ToString(), "profile-fighter-level");
                        card.tooltip = unit.name + " · " + unit.role;
                    }
                    f.AddToClassList("profile-actions");
                    Button(f, "Поддержка", null, "profile-action").SetEnabled(false);
                    Button(f, "Код", null, "profile-action").SetEnabled(false);
                    Button(f, "Настройки", Settings, "profile-action gold");
                    Button(f, "Выйти из аккаунта", null, "profile-action").SetEnabled(false);
                },
                "profile-modal"
            );
        }

        void RenameDialog()
        {
            Modal(
                "Изменить ник",
                (c, f) =>
                {
                    var field = new TextField { value = S.nickname, maxLength = 24 };
                    field.AddToClassList("profile-nickname-input");
                    c.Add(field);
                    Label(c, "От 1 до 24 символов. Буквы, цифры, пробел, . _ -", "sub paragraph");
                    Button(f, "Отмена", Profile);
                    Button(f, "Сохранить", () => Change(() => M.RenamePlayer(field.value), Profile), "gold");
                }
            );
        }

        void AvatarPicker()
        {
            Modal(
                "Выбрать аватар",
                (c, f) =>
                {
                    var grid = V("profile-avatar-grid");
                    c.Add(grid);
                    foreach (int head in M.AvailableAvatars())
                    {
                        int id = head;
                        var cell = Button(
                            grid,
                            "",
                            () => Change(() => M.SelectAvatar(id), Profile),
                            "profile-avatar-cell"
                        );
                        cell.EnableInClassList("selected", id == S.avatarHead);
                        cell.tooltip = id == S.avatarHead ? "Текущий аватар" : "Выбрать аватар";
                        cell.Add(Avatar(id, "profile-avatar-thumb"));
                    }
                    Button(f, "‹ Профиль", Profile);
                },
                "profile-avatar-modal"
            );
        }

        static Rect AvatarCrop(int head)
        {
            switch (head)
            {
                case 106:
                    return new Rect(0.284375f, 0.575893f, 0.426563f, 0.366071f);
                case 107:
                    return new Rect(0.323437f, 0.575893f, 0.343750f, 0.377232f);
                case 109:
                    return new Rect(0.323437f, 0.575893f, 0.343750f, 0.424107f);
                case 116:
                    return new Rect(0.310937f, 0.575893f, 0.371875f, 0.366071f);
                case 131:
                    return new Rect(0.296875f, 0.575893f, 0.398438f, 0.366071f);
                case 133:
                    return new Rect(0.301563f, 0.575893f, 0.403125f, 0.366071f);
                case 134:
                    return new Rect(0.326562f, 0.575893f, 0.343750f, 0.367188f);
                case 136:
                    return new Rect(0.295312f, 0.575893f, 0.403125f, 0.366071f);
                case 143:
                    return new Rect(0.235937f, 0.575893f, 0.517188f, 0.388393f);
                case 57:
                    return new Rect(0.289062f, 0.575893f, 0.360938f, 0.366071f);
                case 59:
                    return new Rect(0.300000f, 0.575893f, 0.396875f, 0.366071f);
                case 62:
                    return new Rect(0.240625f, 0.575893f, 0.515625f, 0.388393f);
                case 63:
                    return new Rect(0.321875f, 0.603795f, 0.353125f, 0.338170f);
                case 69:
                    return new Rect(0.246875f, 0.575893f, 0.517188f, 0.388393f);
                case 82:
                    return new Rect(0.278125f, 0.575893f, 0.387500f, 0.366071f);
                case 88:
                    return new Rect(0.321875f, 0.575893f, 0.343750f, 0.422991f);
                default:
                    return new Rect(0, 0, 1, 1);
            }
        }
    }
}
