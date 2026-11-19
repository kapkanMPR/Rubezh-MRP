using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        readonly Color ink = new Color(.87f, .90f, .84f),
            muted = new Color(.61f, .68f, .65f),
            accent = new Color(.73f, .83f, .58f),
            gold = new Color(.80f, .74f, .53f),
            bg = new Color(.063f, .106f, .122f),
            panel = new Color(.078f, .13f, .15f);
        readonly List<Texture2D> uiTextures = new List<Texture2D>();
        ResidencyCache<string, Texture2D> portraits;
        readonly HashSet<Texture2D> importedPortraits = new HashSet<Texture2D>();

        Texture2D ImportedPortrait(string path)
        {
            var texture = Resources.Load<Texture2D>(path);
            if (texture != null)
                importedPortraits.Add(texture);
            return texture;
        }

        void ReleaseImportedPortraits()
        {
            foreach (var texture in importedPortraits)
                if (texture != null)
                    Resources.UnloadAsset(texture);
            importedPortraits.Clear();
            NativeUiArt.Release();
        }

        readonly HashSet<Texture2D> drawnPortraits = new HashSet<Texture2D>();
        ResidencyCache<string, Texture2D> PortraitCache =>
            portraits
            ?? (
                portraits = new ResidencyCache<string, Texture2D>(t =>
                {
                    uiTextures.Remove(t);
                    if (t != null)
                        Destroy(t);
                })
            );

        void TrimPortraits()
        {
            if (portraits != null)
                portraits.Trim(8L * 1024 * 1024, t => drawnPortraits.Contains(t));
            drawnPortraits.Clear();
        }

        GUIStyle heading,
            kicker,
            card,
            chosen,
            primary,
            danger,
            box,
            selectButton,
            caption;

        float zoom = 1;
        Vector2 pan;
        string[] pickOptions;
        string pickTitle;
        Action<int> pickApply;
        Vector2 pickScroll,
            drawerScroll;
        static readonly string[] Names = { "КЕДР", "ЛИС", "СОКОЛ", "БАРС", "ТИХИЙ" };
        static readonly string[] Weapons = CampaignUnlocks.Weapons;
        static readonly int[] WeaponIds = { 0, 1, 2, 4 };
        static readonly string[] Extras = CampaignUnlocks.Extras;
        static readonly string[] Grenades = CampaignUnlocks.Grenades;
        static readonly string[] StylesList = { "ДА НУ НАХЕР!", "Отход", "Закрепление", "Баланс", "Агрессивно" };
        static readonly int[] StyleIds = { 4, 0, 1, 2, 3 };
        static readonly string[] Maneuvers =
        {
            "По центру",
            "Прямо",
            "Фронтом",
            "Левый фланг",
            "Правый фланг",
            "По флангам",
            "Обхват слева",
            "Обхват справа",
        };
        static readonly int[] ManeuverIds = { 0, 7, 8, 1, 2, 3, 4, 5 };

        static Kit[] StandardKits()
        {
            var k = Progress.FreshKits();
            foreach (var a in k)
                a.rig = 2;
            k[0].count = 3;
            k[1].SetWeapon(1);
            k[2].SetExtra(2);
            k[3].SetExtra(1);
            k[4].SetExtra(3);
            return k;
        }

        Texture2D Surface(Color c, Color edge)
        {
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            var a = new Color[1024];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = Mathf.Max(8 - x, x - 23),
                    dy = Mathf.Max(8 - y, y - 23);
                float d = Mathf.Sqrt(Mathf.Max(0, dx) * Mathf.Max(0, dx) + Mathf.Max(0, dy) * Mathf.Max(0, dy));
                a[y * 32 + x] =
                    d > 8 ? Color.clear
                    : d > 6.5f || x < 2 || y < 2 || x > 29 || y > 29 ? edge
                    : c;
            }

            t.SetPixels(a);
            t.Apply(false, true);
            uiTextures.Add(t);
            return t;
        }

        GUIStyle Text(int size, Color c, bool bold = false)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                wordWrap = true,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            };
            s.normal.textColor = c;
            return s;
        }

        GUIStyle ButtonStyle(Color c, Color edge, Color text)
        {
            var s = new GUIStyle(GUI.skin.button)
            {
                fontSize = 24,
                wordWrap = true,
                border = new RectOffset(10, 10, 10, 10),
                padding = new RectOffset(18, 18, 10, 10),
                alignment = TextAnchor.MiddleCenter,
            };
            var normal = Surface(c, edge);
            var hover = Surface(c * 1.15f, accent);
            foreach (var st in new[] { s.normal, s.onNormal, s.focused, s.onFocused })
            {
                st.background = normal;
                st.textColor = text;
            }

            foreach (var st in new[] { s.hover, s.active, s.onHover, s.onActive })
            {
                st.background = hover;
                st.textColor = text;
            }

            return s;
        }

        void Styles()
        {
            if (body != null)
                return;
            GUI.skin.font =
                Resources.Load<Font>("Fonts/RubezhSans") ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            body = Text(27, ink);
            small = Text(23, muted);
            title = Text(54, ink, true);
            heading = Text(34, ink, true);
            kicker = Text(21, muted, true);
            caption = Text(23, gold);
            button = ButtonStyle(new Color(.105f, .17f, .18f), new Color(.22f, .29f, .29f), ink);
            chosen = ButtonStyle(new Color(.14f, .23f, .18f), accent, ink);
            primary = ButtonStyle(accent, new Color(.38f, .48f, .33f), new Color(.09f, .14f, .09f));
            primary.fontStyle = FontStyle.Bold;
            danger = ButtonStyle(new Color(.33f, .13f, .11f), new Color(.62f, .30f, .22f), ink);
            danger.fontStyle = FontStyle.Bold;
            selectButton = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft };
            card = new GUIStyle(button);
            box = new GUIStyle(button);
            box.normal.background = Surface(panel, new Color(.22f, .29f, .29f));
            InitCompactStyles();
            GUI.skin.verticalScrollbar.fixedWidth = 16;
            GUI.skin.verticalScrollbarThumb.normal.background = Surface(
                new Color(.26f, .35f, .33f),
                new Color(.26f, .35f, .33f)
            );
        }

        // All visible screens are retained Unity UI documents. No IMGUI repaint loop.

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Txt(float x, float y, float w, float h, string text, GUIStyle s = null)
        {
            GUI.Label(new Rect(x, y, w, h), text, s ?? body);
        }

        bool B(float x, float y, float w, float h, string text, GUIStyle s = null)
        {
            return TouchButton(new Rect(x, y, w, h), text, s ?? button);
        }

        void Box(Rect r, bool active = false)
        {
            GUI.Box(r, GUIContent.none, active ? chosen : box);
        }

        void Rule(float x, float y, float w)
        {
            Fill(new Rect(x, y, w, 2), new Color(.21f, .28f, .27f));
        }

        void Pick(string name, string[] options, Action<int> apply)
        {
            pickTitle = name;
            pickOptions = options;
            pickApply = apply;
            pickScroll = Vector2.zero;
        }

        void Picker()
        {
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, .65f));
            float w = Mathf.Min(1100, W - 100),
                h = Mathf.Min(800, 150 + pickOptions.Length * 90),
                x = (W - w) / 2,
                y = (H - h) / 2;
            if (OutsidePanel(new Rect(x, y, w, h)))
            {
                pickOptions = null;
                pickApply = null;
                CancelTouchInput();
                return;
            }

            Box(new Rect(x, y, w, h));
            Txt(x + 32, y + 20, w - 130, 60, pickTitle, heading);
            if (DrawerCloseButton(new Rect(x + w - 85, y + 15, 65, 60)))
            {
                pickOptions = null;
                pickApply = null;
                CancelTouchInput();
                return;
            }

            pickScroll = TouchScroll(
                "picker",
                new Rect(x + 20, y + 95, w - 40, h - 115),
                pickScroll,
                new Rect(0, 0, w - 65, pickOptions.Length * 90)
            );
            for (int i = 0; i < pickOptions.Length; i++)
            {
                if (B(0, i * 90, w - 70, 78, pickOptions[i], selectButton))
                {
                    var apply = pickApply;
                    pickOptions = null;
                    apply(i);
                    break;
                }
            }

            EndTouchScroll();
            var e = Event.current;
            if (e.isMouse || e.type == EventType.ScrollWheel)
                e.Use();
        }

        static readonly string[] ClassNames =
        {
            "Командир",
            "Пулемётчик",
            "Медик",
            "Гранатомётчик",
            "Стрелок",
            "Снайпер",
        };

        string RoleName(Kit k, int i) => ClassNames[(int)k.Class(i)];

        static string WeaponName(int w)
        {
            return w == 5 ? "М24"
                : w == 3 ? "М4 с подствольником"
                : Weapons[w == 4 ? 3 : w];
        }

        void Portrait(
            Rect rect,
            int weapon,
            int extra,
            int armor,
            int rig,
            bool enemy = false,
            Rubezh.GreenLook look = null,
            bool western = false,
            Role role = Role.Rifleman,
            int identity = 0
        )
        {
            NativeUiArt.Draw(rect, NativeUiArt.PortraitKey(weapon, enemy, look, western, role, identity));
        }

#if UNITY_EDITOR || RUBEZH_UI_BAKE
        public static void DrawPortrait(
            PixelCanvas c,
            int weapon,
            int armor,
            int rig,
            bool enemy,
            Rubezh.GreenLook look = null,
            bool western = false,
            Role role = Role.Rifleman,
            int identity = 0
        )
        {
            // Menus always use the real art, regardless of the texture-free battle setting.
            bool old = Rubezh.Graphics.PrimitiveBattleMode;
            Rubezh.Graphics.PrimitiveBattleMode = false;
            try
            {
                Rubezh.Graphics.PersonSprite(c, 0, 64, 78, 150, enemy, 0, armor, rig, look, role, identity);
                float wy = enemy ? 115f : 98f;
                const float wx = 77f,
                    angle = -.2f;
                if (weapon >= 4)
                    SniperSprites.Draw(c, weapon, wx, wy, 94, SniperSprites.Height(weapon, 94), angle);
                else if (weapon == 2 || weapon == 3)
                    UnderbarrelSprites.Draw(c, weapon, wx, wy, 94, UnderbarrelSprites.Height(weapon, 94), angle);
                else
                    PawnSprites.Draw(
                        c,
                        weapon == 1 ? 9 : 8,
                        wx,
                        wy,
                        94,
                        BattleVisualRecipes.WeaponHeight(weapon == 1 ? 9 : 8, 94, enemy || western),
                        enemy || western,
                        angle
                    );
                if (!enemy)
                {
                    const float scale = 94f / 21f;
                    float dx = (float)Math.Cos(angle),
                        dy = (float)Math.Sin(angle);
                    Rubezh.Graphics.Hand(
                        c,
                        false,
                        false,
                        wx - 3 * scale * dx - 1.2f * scale * dy,
                        wy - 3 * scale * dy + 1.2f * scale * dx,
                        angle,
                        false,
                        scale
                    );
                    Rubezh.Graphics.Hand(
                        c,
                        true,
                        false,
                        wx + 3 * scale * dx - 1.2f * scale * dy,
                        wy + 3 * scale * dy + 1.2f * scale * dx,
                        angle,
                        false,
                        scale
                    );
                }
            }
            finally
            {
                Rubezh.Graphics.PrimitiveBattleMode = old;
            }
        }
#endif

        Texture2D CanonicalPortrait(Rubezh.GreenLook look)
        {
            if (look == null)
                return null;
            if (look.headId == 82 && look.lightId == 51 && look.familyId == 53)
                return ImportedPortrait("FieldCamp/Art/canonical/kedr");
            if (look.lightId == 79 && look.familyId == 91)
            {
                string mg =
                    look.headId == 57 ? "val"
                    : look.headId == 59 ? "medved"
                    : look.headId == 63 ? "bury"
                    : null;
                if (mg != null)
                    return ImportedPortrait("FieldCamp/Art/canonical/" + mg);
            }
            if (look.lightId == 83 && look.familyId == 63)
            {
                string medic =
                    look.headId == 88 ? "fenix"
                    : look.headId == 106 ? "dobry"
                    : look.headId == 109 ? "hirurg"
                    : null;
                if (medic != null)
                    return ImportedPortrait("FieldCamp/Art/canonical/" + medic);
            }
            if (look.lightId == 62 && look.familyId == 89)
            {
                string rifle =
                    look.headId == 116 ? "klen"
                    : look.headId == 131 ? "veresk"
                    : look.headId == 133 ? "granit"
                    : look.headId == 107 ? "vektor"
                    : look.headId == 134 ? "pepel"
                    : look.headId == 136 ? "fugas"
                    : null;
                if (rifle != null)
                    return ImportedPortrait("FieldCamp/Art/canonical/" + rifle);
            }
            if (look.lightId != 55 || look.familyId != 95)
                return null;
            string name =
                look.headId == 143 ? "sedoy"
                : look.headId == 62 ? "morok"
                : look.headId == 69 ? "manul"
                : null;
            return name == null ? null : ImportedPortrait("FieldCamp/Art/canonical/" + name);
        }

        bool DrawCanonicalPortrait(Rect r, Rubezh.GreenLook look)
        {
            var art = CanonicalPortrait(look);
            if (art == null)
                return false;
            GUI.DrawTexture(r, art, ScaleMode.ScaleToFit, true);
            return true;
        }

        void Person(Rect r, Kit k)
        {
            if (DrawCanonicalPortrait(r, LegacyLook(Math.Max(0, Array.IndexOf(Kits, k)), k)))
                return;
            Portrait(r, k.weapon, k.extra, k.armor, k.rig, false, LegacyLook(Math.Max(0, Array.IndexOf(Kits, k)), k));
        }

        void Person(Rect r, Soldier s)
        {
            if (
                s.Side == Side.Green
                && (
                    s.Role == Role.Sniper
                    || s.Role == Role.Commander
                    || s.Role == Role.Medic
                    || s.Role == Role.Rifleman
                    || s.Role == Role.Grenadier
                    || s.Role == Role.MachineGunner
                )
                && DrawCanonicalPortrait(r, s.Look)
            )
                return;
            Portrait(
                r,
                s.WeaponModel,
                Extra(s),
                s.ArmorLevel,
                s.RigLevel,
                s.Side == Side.Red,
                s.Look,
                WeaponBalance.Index(s) >= 6,
                s.Role,
                s.Id
            );
        }

        void Field(float y, float w, string name, float height)
        {
            Box(new Rect(0, y, w, height));
            Fill(new Rect(24, y - 12, Mathf.Min(w - 48, name.Length * 18 + 28), 40), bg);
            Txt(40, y - 13, w - 80, 45, name, caption);
        }

        void LayoutMap()
        {
            float mw = (battle.Map.Columns + battle.Map.Rows) * 11f,
                mh = (battle.Map.Columns + battle.Map.Rows) * 5f + 70;
            float f = Mathf.Min((W - 180) / mw, (H - 210) / mh) * zoom;
            float originX = W / 2 - MapWidth * .5f * f,
                originY = H / 2 - (MapHeight * .5f + 10) * f;
            pan.x = ExteriorCamera.Clamp(pan.x, MapWidth * f, W, originX);
            pan.y = ExteriorCamera.Clamp(pan.y, MapHeight * f, H, originY);
            mapRect = new Rect(originX + pan.x, originY + pan.y, MapWidth * f, MapHeight * f);
        }

        Vector2 ToView(V2 pos)
        {
            var p = BattleVisualRecipes.Project(pos, battle.Map);
            return new Vector2(
                mapRect.x + p.X / MapWidth * mapRect.width,
                mapRect.y + p.Y / MapHeight * mapRect.height
            );
        }

        void Focus()
        {
            pan = Vector2.zero;
            LayoutMap();
            var unit = battle.Soldiers.FirstOrDefault(s => s.Id == selected);
            if (unit == null)
                return;
            var p = ToView(unit.Position);
            pan = new Vector2(W / 2 - p.x, H / 2 - p.y);
        }

        void CloseDrawer()
        {
            drawerScroll = Vector2.zero;
            CancelTouchInput();
        }

        bool DrawerCloseButton(Rect r)
        {
            // Mouse release is handled explicitly: IMGUI control IDs change when a drawer opens.
            bool clicked = TouchButton(r, "×", button);
            var e = Event.current;
            if (GUI.enabled && e.type == EventType.MouseUp && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                clicked = true;
            }

            return clicked;
        }

        bool OutsidePanel(Rect bounds)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && !bounds.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            return false;
        }

        VehicleArrivalState ambushSpeechVehicle;
        float ambushSpeechClock;

        float AmbushElapsed()
        {
            var vehicle = battle.GreenVehicle;
            if (vehicle == null || vehicle.AmbushSpeechAt < 0)
                return -1f;
            if (ambushSpeechVehicle != vehicle)
            {
                ambushSpeechVehicle = vehicle;
                ambushSpeechClock = Time.unscaledTime;
            }

            return Time.unscaledTime - ambushSpeechClock;
        }

        string ActionName(Soldier s)
        {
            if (s.Available && ConcussionEffects.Current(battle, s) > 0)
                return "КОНТУЖЕН";
            switch (s.State)
            {
                case ActionState.Move:
                    return "Движется";
                case ActionState.Fire:
                    return "Ведёт огонь";
                case ActionState.Suppress:
                    return "Подавляет";
                case ActionState.Reload:
                    return "Перезарядка";
                case ActionState.Treat:
                    return "Оказывает помощь";
                case ActionState.Down:
                    return "Выбыл";
                case ActionState.Pinned:
                    return "Прижат огнём";
                case ActionState.Recover:
                    return "Восстанавливается";
                case ActionState.Cower:
                    return "Прижался";
                case ActionState.WaitCover:
                    return "Ждёт прикрытия";
                case ActionState.Incapacitated:
                    return "Тяжело ранен";
                case ActionState.Rescue:
                    return "Идёт на помощь";
                case ActionState.Drag:
                    return "Эвакуирует";
                case ActionState.Dragged:
                    return "Эвакуируется";
                case ActionState.Launch:
                    return "Пуск гранатомёта";
                case ActionState.GrenadeAim:
                    return "Готовит гранату";
                default:
                    return s.Incapacitated ? "Ранен" : "Наблюдает";
            }
        }

        string LiveRole(Soldier s)
        {
            return s.Side == Side.Green ? ClassNames[(int)s.Role]
                : s.Role == Role.Commander ? "Командир"
                : s.Role == Role.MachineGunner ? "Пулемётчик"
                : s.Role == Role.Medic ? "Медик"
                : s.Role == Role.Sniper ? "Снайпер"
                : s.Role == Role.Grenadier ? "Гранатомётчик"
                : s.SupplyPack ? "Снабженец"
                : "Стрелок";
        }

        int Extra(Soldier s)
        {
            return s.SupplyPack ? 3
                : SupplyRecovery.Medical(s) ? 2
                : s.Role == Role.Grenadier ? (s.LauncherKind == 1 ? 4 : 1)
                : 0;
        }
    }
}
