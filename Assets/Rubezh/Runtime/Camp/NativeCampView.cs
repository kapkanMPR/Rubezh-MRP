using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Rubezh.Camp;
using CampUnitInfo = Rubezh.Camp.Unit;

namespace Rubezh.UnityPort
{
    // A retained-mode Unity UI. No HTML, browser, polling, or JS business logic.
    public sealed partial class NativeCampView : MonoBehaviour
    {
        public CampStore Store { get; private set; }

        public CampModel M
        {
            get
            {
                return Store.Model;
            }
        }

        State S
        {
            get
            {
                return M.S;
            }
        }

        public Action<CampOrder> StartOperation;
        public Action StartHybridRange;
        public Action<string, int> SetOption;
        public Func<NativeCampOptions> GetOptions;
        public Action ResetGame;
        public bool Developer;
        UIDocument document;
        PanelSettings panel;
        VisualElement root, header, main, nav, overlay, modalContent, modalFoot;
        Label toast;
        readonly ResidencyCache<string, Texture2D> textures = new ResidencyCache<string, Texture2D>(t =>
        {
            if (t != null)
                Resources.UnloadAsset(t);
        });
        bool textureResidencyDirty;
        readonly HashSet<Texture> visibleTextures = new HashSet<Texture>();
        void CollectTextures(VisualElement v)
        {
            if (v is Image i && i.image != null)
                visibleTextures.Add(i.image);
            // Backgrounds belong to the current screen, not the global stylesheet.
            string backdrop = v.ClassListContains("portrait-card") || v.ClassListContains("fighter-card-modal") ||
                v.ClassListContains("outfit-footer") || (v.ClassListContains("outfit-drawer") && outfitDrawerOpen)
                ? "character-background.png" : v.ClassListContains("operations") ? "operation-background.png"
                : v.ClassListContains("card") ? "card-background.png" : null;
            if (backdrop != null)
            {
                var background = Texture(backdrop);
                v.style.backgroundImage = new StyleBackground(background);
                if (background != null) visibleTextures.Add(background);
            }
            for (int n = 0; n < v.childCount; n++)
                CollectTextures(v[n]);
        }

        void TrimTextures()
        {
            if (!textureResidencyDirty || root == null)
                return;
            textureResidencyDirty = false;
            visibleTextures.Clear();
            CollectTextures(root);
            textures.Trim(0, t => visibleTextures.Contains(t));
            textureResidencyDirty = false;
            visibleTextures.Clear();
        }

        readonly List<IVisualElementScheduledItem> animations = new List<IVisualElementScheduledItem>();
        readonly List<IVisualElementScheduledItem> modalAnimations = new List<IVisualElementScheduledItem>();
        string screen = "home", selected = "kedr", trainee = "kedr", storeTab = "shop", trainingFrom, filterType = "", filterRole = "", filterRarity = "", filterUnit = "", sort = "default", pickSlot;
        int pickIndex;
        float vw = 1000, vh = 600, scale = 1;
        Rect lastSafe;
        bool ready, rendering;
        Vector2 treeScroll;
        float toastUntil;
        Action reopenModal;
        public string ScreenName
        {
            get
            {
                return screen;
            }
        }

        public VisualElement Root
        {
            get
            {
                return root;
            }
        }

        public void Initialize(CampStore store, bool developer)
        {
            Store = store;
            Developer = developer;
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            panel.sortingOrder = 200;
            panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panel;
            root = document.rootVisualElement;
            root.name = "native-camp";
            root.AddToClassList("camp-root");
            root.styleSheets.Add(Resources.Load<StyleSheet>("FieldCamp/Camp"));
            InitializeFonts();
            ready = true;
            UpdateViewport();
            Render();
        }

        void Update()
        {
            if (!ready || root.resolvedStyle.display == DisplayStyle.None)
                return;
            TrimTextures();
            bool viewportChanged = Screen.safeArea != lastSafe;
#if UNITY_EDITOR
   viewportChanged&=!validationViewport;
#endif
            if (viewportChanged)
                UpdateViewport();
            if (toast != null && Time.unscaledTime > toastUntil)
                toast.style.display = DisplayStyle.None;
            if (Input.GetKeyDown(KeyCode.Escape))
                Back();
        }

        void UpdateViewport()
        {
            lastSafe = Screen.safeArea;
#if UNITY_ANDROID && !UNITY_EDITOR
   using(var a=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))using(var activity=a.GetStatic<AndroidJavaObject>("currentActivity"))using(var resources=activity.Call<AndroidJavaObject>("getResources"))using(var metrics=resources.Call<AndroidJavaObject>("getDisplayMetrics"))scale=Mathf.Max(1,metrics.Get<float>("density"));
#else
            scale = 1;
#endif
            scale *= Mathf.Clamp(PlayerPrefs.GetInt("rubezh.uiScale", 100), 50, 300) / 100f;
            panel.scale = scale;
            vw = lastSafe.width / scale;
            vh = lastSafe.height / scale;
            root.style.position = Position.Absolute;
            root.style.left = lastSafe.x / scale;
            root.style.top = (Screen.height - lastSafe.yMax) / scale;
            root.style.width = vw;
            root.style.height = vh;
            root.EnableInClassList("narrow", vw <= 650);
            root.EnableInClassList("compact", vh <= 600 && vw > 650);
            root.EnableInClassList("short", vh <= 520 && vw >= 590);
            root.EnableInClassList("landscape", vw >= 590 && vw > vh);
            root.EnableInClassList("small-header", vw <= 900);
            if (ready && main != null)
            {
                Render();
                if (reopenModal != null)
                    reopenModal();
            }
        }

        public void Show(string page = null)
        {
            if (page != null)
                screen = page;
            root.style.display = DisplayStyle.Flex;
            Render();
            ResumeReward();
        }

        public void Hide()
        {
            Close();
            PauseAnimations();
            root.Clear();
            header = main = nav = null;
            toast = null;
            textures.Clear();
            visibleTextures.Clear();
            textureResidencyDirty = false;
            root.style.display = DisplayStyle.None;
        }

        void OnDestroy()
        {
            ready = false;
            PauseAnimations();
            ReleaseFonts();
            if (document != null)
                Destroy(document);
            textures.Clear();
            visibleTextures.Clear();
            textureResidencyDirty = false;
            if (panel != null)
                Destroy(panel);
        }

        public void Refresh()
        {
            Render();
        }

        public void Notify(string text)
        {
            Toast(text);
        }

        public void Navigate(string page)
        {
            screen = page;
            trainingFrom = null;
            if (page == "battlePrep" && !M.SquadReady) { screen="prepare"; Toast("Заполните все 5 слотов отряда"); }
            if (page == "warehouse")
            {
                screen = "store";
                storeTab = "warehouse";
                filterType = "";
            }
            else if (page == "store")
                storeTab = "shop";
            Close();
            Render();
        }

        void Back()
        {
            if (overlay != null)
            {
                if (S.rewardView != null)
                {
                    if (S.rewardView.phase == "done")
                        AcceptReward();
                    else
                        DrawShipment(false);
                    return;
                }

                Close();
                return;
            }

            if (screen == "training")
            {
                screen = trainingFrom ?? "barracks";
                trainingFrom = null;
            }
            else if (screen == "personnel" || screen == "squads" || screen == "store" && storeTab == "recruit")
                screen="barracks";
            else if (screen == "prepare" || screen == "battlePrep")
                screen = "home";
            else if (screen == "formation")
                screen = "battlePrep";
            else
                screen = "home";
            Render();
        }

        void Try(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                Toast(e.Message);
                Debug.LogException(e);
            }
        }

        void Change(Action a, Action after = null)
        {
            Try(() =>
            {
                Store.Change(a);
                Render();
                if (after != null)
                    after();
            });
        }

        void SaveView()
        {
            Store.Save();
        }

        void Render()
        {
            if (!ready || rendering)
                return;
            rendering = true;
            PauseAnimations();
            try
            {
                if (selected == null || !S.gear.ContainsKey(selected))
                    selected = S.active.FirstOrDefault(id => id != null) ?? "kedr";
                if (trainee == null || !S.gear.ContainsKey(trainee))
                    trainee = S.active.FirstOrDefault(id => id != null) ?? "kedr";
                // Rebuild only after an action or a viewport change, never every frame.
                if (header != null)
                    header.RemoveFromHierarchy();
                if (main != null)
                    main.RemoveFromHierarchy();
                if (nav != null)
                    nav.RemoveFromHierarchy();
                bool campHome = screen == "home";
                root.EnableInClassList("camp-home", campHome);
                root.EnableInClassList("outfit-screen",screen=="prepare" || screen=="battlePrep");
                root.style.left = campHome ? 0 : lastSafe.x / scale;
                root.style.top = campHome ? 0 : (Screen.height - lastSafe.yMax) / scale;
                root.style.width = campHome ? Screen.width / scale : vw;
                root.style.height = campHome ? Screen.height / scale : vh;
                bool isolated = screen == "battlePrep" || screen == "formation" || screen == "training" && trainingFrom == "battlePrep";
                header = V("header hub-header");
                header.style.display = DisplayStyle.Flex;
                root.Insert(0, header);
                header.EnableInClassList("isolated", isolated && screen != "training");
                HubHeader(isolated);

                main = V("main");
                main.name = "camp-main";
                root.Insert(1, main);
                switch (screen)
                {
                    case "home":
                        CampHub();
                        break;
                    case "store":
                        StorePage();
                        break;
                    case "barracks":
                        BarracksHub();
                        break;
                    case "personnel":
                        Barracks();
                        break;
                    case "squads":
                        SquadList();
                        break;
                    case "prepare":
                        Prepare(false);
                        break;
                    case "battlePrep":
                        Prepare(true);
                        break;
                    case "operations":
                        Operations();
                        break;
                    case "training":
                        Training();
                        break;
                    default:
                        throw new InvalidOperationException("Неизвестный экран: " + screen);
                }

                nav = V("nav row hub-nav");
                root.Insert(2, nav);
                nav.style.display = DisplayStyle.None;
                if(screen != "home" && screen != "prepare" && !isolated)
                {
                    nav.style.display = DisplayStyle.Flex;
                    Button(nav,"‹ Назад",Back,"hub-back");
                    if (screen == "store" && storeTab == "warehouse")
                    {
                        var hide = new Toggle("Скрыть выданное"); hide.name = "warehouse-hide-issued";
                        hide.value = S.hideIssued; hide.style.marginLeft = 12;
                        hide.RegisterValueChangedCallback(e => Change(() => S.hideIssued = e.newValue));
                        nav.Add(hide);
                    }
                }

                ApplyResponsiveMetrics();
                if (campHome)
                {
                    header.style.left=lastSafe.x/scale;
                    header.style.right=(Screen.width-lastSafe.xMax)/scale;
                    header.style.top=(Screen.height-lastSafe.yMax)/scale;
                    header.BringToFront();
                }
                if (toast == null)
                {
                    toast = Label(root, "", "toast");
                    toast.pickingMode = PickingMode.Ignore;
                    toast.style.display = DisplayStyle.None;
                }

                toast.BringToFront();
                if (overlay != null)
                    overlay.BringToFront();
            }
            finally
            {
                rendering = false;
                textureResidencyDirty = true;
            }
        }

        void BattleTop()
        {
            if(!M.SquadReady) { screen="prepare";Close();Render();Toast("Заполните все 5 слотов отряда");return; }
            if (screen == "battlePrep")
                EnterFormation();
            else
            {
                screen = screen == "operations" ? "battlePrep" : "operations";
                Close();
                Render();
            }
        }

        void EnterFormation()
        {
            CampModel.Check(M.MissionOpen(S.operation.mode, S.operation.block, S.operation.mission), "Миссия ещё не открыта");
            SaveView();
            if (StartOperation == null)
                throw new InvalidOperationException("Бой недоступен");
            var order = CreateOrder();
            var absent = order.units.Where(u => u.noWeapon).Select(u => u.name).ToArray();
            CampModel.Check(absent.Length < order.units.Length, "Никто не вооружён. Выдайте оружие хотя бы одному бойцу.");
            if (absent.Length > 0)
            {
                Modal("Без оружия — остаются в лагере", (c, f) =>
                {
                    Label(c, string.Join(", ", absent) + ". Эти бойцы не выйдут в бой.", "paragraph");
                    Button(f, "Вернуться к снаряжению", Close);
                    Button(f, "Выйти остальными", () => Try(() => { Close(); StartOperation(order); }), "gold");
                });
                return;
            }
            StartOperation(order);
        }

        public CampOrder CreateOrder()
        {
            CampModel.Check(M.SquadReady,"Заполните все 5 слотов отряда");
            var o = S.operation;
            return new CampOrder
            {
                op = "start",
                mode = o.mode,
                block = o.block,
                mission = o.mission,
                formation = new SilentOrders.Core.FormationSettings
                {
                    quietWork = S.formation.quietWork,
                    pairing = S.formation.pairing,
                    pairSlots = (int[])S.formation.pairSlots.Clone(),
                    width = S.formation.width,
                    depth = S.formation.depth,
                    positions = (int[])S.formation.positions.Clone()
                },
                units = S.active.Select(id =>
                {
                    var u = M.Soldier(id);
                    var g = S.gear[id];
                    var l = S.looks[id];
                    var upgradedWeapon = M.FighterWeapon(id);
                    var skills = M.Person(id).skills.Where(p => p.Value > 0).Select(p => p.Key).ToArray();
                    Func<string, float> stat = key => M.Data.skills.Where(k => skills.Contains(k.id) && k.effect != null && k.effect.stat == key).Sum(k => Math.Abs(k.effect.delta) / 100);
                    return new CampUnit
                    {
                        fighterId = id,
                        weaponRarity = upgradedWeapon == null ? 0 : upgradedWeapon.rarity,
                        weaponLevel = upgradedWeapon == null ? 1 : upgradedWeapon.level,
                        noWeapon = !M.Armed(id),
                        name = u.name,
                        role = Array.IndexOf(new[] { "Командир", "Пулемётчик", "Медик", "Гранатомётчик", "Стрелок", "Снайпер" }, u.role) + 1,
                        weapon = g.weapon == "pkm" || g.weapon == "m249" ? 1 : g.weapon == "gp" ? 2 : g.weapon == "m4gp" ? 3 : g.weapon == "svd" ? 4 : g.weapon == "m24" ? 5 : 0,
                        westernWeapon = g.weapon == "m4" || g.weapon == "m4gp" || g.weapon == "m249" || g.weapon == "m24",
                        extra2 = g.extra2 == "rpg" ? 1 : g.extra2 == "rshg" ? 4 : 0,
                        extra = g.extra == "rpg" ? 1 : g.extra == "medbag" ? 2 : g.extra == "supply" ? 3 : g.extra == "rshg" ? 4 : 0,
                        rig = CampModel.GearLevel(g.rig),
                        armor = CampModel.GearLevel(g.armor),
                        band = g.tourniquet == "band",
                        pockets = g.rigSlots.Where(x => x != null).ToArray(),
                        bag = g.bag.Where(x => x != null && !x.StartsWith("@", StringComparison.Ordinal)).ToArray(),
                        skills = skills,
                        aim = stat("accuracy"),
                        reload = stat("reloadTime"),
                        headId = l.headId,
                        lightId = l.lightId,
                        familyId = l.familyId,
                        heavyId = l.heavyId
                    };
                }).ToArray()
            };
        }

        void ResourceCounter(VisualElement parent, string value, string caption)
        {
            var block = Button(parent, "", Currencies, "resource resource-button");
            block.tooltip = "Валюты лагеря";
            Label(block, value, "resource-value");
            if (vw > 900)
                Label(block, caption, "resource-caption");
        }

        static string Money(double n)
        {
            return n.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
        }

        VisualElement V(string classes = "")
        {
            var v = new VisualElement();
            foreach (var c in classes.Split(' '))
                if (c.Length > 0)
                    v.AddToClassList(c);
            return v;
        }

        Label Label(VisualElement parent, string text, string cls = "", bool rich = false)
        {
            var e = new Label(text);
            e.enableRichText = rich;
            foreach (var c in cls.Split(' '))
                if (c.Length > 0)
                    e.AddToClassList(c);
            parent.Add(e);
            return e;
        }

        Button Button(VisualElement parent, string text, Action action, string cls = "")
        {
            var b = new Button(() => Try(action))
            {
                text = text
            };
            b.name = "action-" + text;
            b.AddToClassList("camp-button");
            foreach (var c in cls.Split(' '))
                if (c.Length > 0)
                    b.AddToClassList(c);
            parent.Add(b);
            return b;
        }

        VisualElement Row(VisualElement parent, string cls = "")
        {
            var r = V("row " + cls);
            parent.Add(r);
            return r;
        }

        ScrollView Scroll(VisualElement parent, string cls = "", ScrollViewMode mode = ScrollViewMode.Vertical)
        {
            var s = new ScrollView(mode);
            s.AddToClassList("scroll");
            if (mode == ScrollViewMode.Vertical)
                s.AddToClassList("vertical-scroll");
            foreach (var c in cls.Split(' '))
                if (c.Length > 0)
                    s.AddToClassList(c);
            s.horizontalScrollerVisibility = mode == ScrollViewMode.Vertical ? ScrollerVisibility.Hidden : ScrollerVisibility.Auto;
            s.verticalScrollerVisibility = ScrollerVisibility.Auto;
            parent.Add(s);
            return s;
        }

        Texture2D Texture(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            path = System.IO.Path.ChangeExtension(path, null).Replace('\\', '/');
            Texture2D t;
            if (!textures.TryGetValue(path, out t) || t == null)
            {
                textures.Remove(path);
                t = Resources.Load<Texture2D>("FieldCamp/Art/" + path);
                if(t != null) textures.Add(path, t, 4L * t.width * t.height);
                textureResidencyDirty = true;
                if (t == null)
                    Debug.LogError("Нет изображения лагеря: " + path);
            }

            return t;
        }

        Image Art(string path, string cls = "")
        {
            var e = new Image
            {
                image = Texture(path),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            foreach (var c in cls.Split(' '))
                if (c.Length > 0)
                    e.AddToClassList(c);
            return e;
        }

        VisualElement Portrait(CampUnitInfo u, Look appearance = null, Gear kit = null, string direction = "front")
        {
            var canonicalArt=M.PortraitArt(u.id);
            if(canonicalArt!=null) {
                var artFrame=V("portrait canonical-portrait");
                var art=Art(canonicalArt,"canonical-art");
                art.scaleMode=ScaleMode.ScaleToFit;
                artFrame.Add(art);
                art.style.position=Position.Absolute;art.style.top=0;art.style.left=0;
                artFrame.RegisterCallback<GeometryChangedEvent>(evt=>{
                    if(art.image==null || evt.newRect.width<=0)return;
                    float w=evt.newRect.width,h=evt.newRect.height;
                    float ratio=(float)art.image.height/art.image.width;
                    // Preparation uses a height-bounded crop: show at least 72% of the source.
                    // Wide layouts must never zoom into only the head and shoulders.
                    bool waist = artFrame.ClassListContains("doll-picture") || artFrame.ClassListContains("member-picture") || artFrame.ClassListContains("outfit-hero") || artFrame.ClassListContains("outfit-thumbnail");
                    float width = waist ? Mathf.Min(w, h / (ratio * .72f)) : Mathf.Max(w,h/ratio);
                    art.style.width=width;art.style.height=width*ratio;art.style.left=(w-width)*.5f;
                });
                return artFrame;
            }
            var g = kit ?? S.gear[u.id];
            var l = appearance ?? S.looks[u.id];
            var frame = V("portrait");
            frame.pickingMode = PickingMode.Ignore;
            int headId = Array.IndexOf(Rubezh.GreenLook.ActiveHeads, l.headId) >= 0 ? l.headId : 116;
            var canvas = V("portrait-canvas");
            frame.Add(canvas);
            var pawn = Art("pawns/green-" + headId + "-" + direction + ".png", "portrait-layer");
            canvas.Add(pawn);
            Image gun = null;
            if (direction == "front" && M.Data.Item(g.weapon) != null)
            {
                gun = Art(M.Data.Item(g.weapon).img, "portrait-gun");
                canvas.Add(gun);
                gun.style.rotate = new Rotate(new Angle(-7));
            }

            frame.RegisterCallback<GeometryChangedEvent>(ev =>
            {
                float w = ev.newRect.width, h = ev.newRect.height, k = Mathf.Min(w / 176, h / 238);
                canvas.style.left = (w - 176 * k) / 2;
                canvas.style.top = (h - 238 * k) / 2;
                canvas.style.width = 176 * k;
                canvas.style.height = 238 * k;
                foreach (var e in new[]
                {
                    pawn
                }

                )
                {
                    e.style.left = 8 * k;
                    e.style.top = 7 * k;
                    e.style.width = 160 * k;
                    e.style.height = 224 * k;
                }

                if (gun != null)
                {
                    gun.style.left = 8.9f * k;
                    gun.style.top = 113.6f * k;
                    gun.style.width = 156.2f * k;
                    gun.style.height = 52.8f * k;
                }
            });
            return frame;
        }

        void AddClassBadge(VisualElement parent, string role)
        {
            int index=RoleIndex(role);
            if(index<0)return;
            var badge=Art("class-"+index+".png","portrait-class-badge");
            badge.tooltip=role;
            parent.Add(badge);
        }

        void CardHeading(VisualElement card, CampUnitInfo u)
        {
            var heading=Row(card,"person-heading");
            Label(heading,u.name,"card-title grow");
            Label(heading,"Ур. "+M.Person(u.id).level,"person-level");
            var badge=Art("class-"+RoleIndex(u.role)+".png","person-class");heading.Add(badge);
        }
        void SoftenPortrait(VisualElement portrait, Color color)
        {
            // Small UI strips approximate a smooth edge fade without duplicating textures.
            const int steps=24;
            for(int i=0;i<steps;i++) {
                float alpha=1f-(i+.5f)/steps;
                foreach(int side in new[]{0,1,2}) {
                    var strip=new VisualElement { pickingMode=PickingMode.Ignore };
                    strip.style.position=Position.Absolute;
                    strip.style.backgroundColor=new Color(color.r,color.g,color.b,alpha*alpha);
                    if(side<2) {
                        strip.style.top=0;strip.style.bottom=0;strip.style.width=Length.Percent(8f/steps+.04f);
                        if(side==0)strip.style.left=Length.Percent(i*8f/steps);else strip.style.right=Length.Percent(i*8f/steps);
                    } else {strip.style.left=0;strip.style.right=0;strip.style.height=Length.Percent(9f/steps+.04f);strip.style.bottom=Length.Percent(i*9f/steps);}
                    portrait.Add(strip);
                }
            }
        }

        void Book(VisualElement parent, string id)
        {
            if (M.CanLearn(id))
                Label(parent, "▤", "skill-book").tooltip = "Свободные очки навыков: " + M.Person(id).points;
        }

        void Toast(string text)
        {
            if (toast == null)
                return;
            toast.text = text;
            toast.style.display = DisplayStyle.Flex;
            toastUntil = Time.unscaledTime + 3.2f;
            toast.BringToFront();
        }

        void PauseAnimations()
        {
            foreach (var a in animations)
                a.Pause();
            animations.Clear();
            foreach (var job in modalAnimations) job.Pause();
            modalAnimations.Clear();
        }

        void Close()
        {
            foreach (var job in modalAnimations) job.Pause();
            modalAnimations.Clear();
            textureResidencyDirty = true;
            if (overlay != null)
                overlay.RemoveFromHierarchy();
            overlay = null;
            modalContent = modalFoot = null;
            reopenModal = null;
        }

        void Modal(string title, Action<VisualElement, VisualElement> content, string cls = "")
        {
            Close();
            reopenModal = () => Modal(title, content, cls);
            overlay = V("modal-overlay" + (cls == "currency-drawer" ? " currency-overlay" : ""));
            root.Add(overlay);
            var box = V("modal " + cls);
            overlay.Add(box);
            var head = Row(box, "sheet-head");
            Label(head, title, "modal-title");
            Button(head, "×", Close, "close");
            var sc = Scroll(box, "sheet-content");
            sc.contentContainer.AddToClassList("sheet-body");
            modalContent = sc.contentContainer;
            modalFoot = Row(box, "sheet-foot");
            content(modalContent, modalFoot);
            if (modalFoot.childCount == 0)
                modalFoot.style.display = DisplayStyle.None;
            SizeModal(box);
            box.Focus();
        }

        void Confirm(string title, string body, string accept, Action action)
        {
            Modal(title, (c, f) =>
            {
                Label(c, body, "paragraph");
                Button(f, "Отмена", Close);
                Button(f, accept, () => Change(action, Close), "gold");
            });
        }

        void Dropdown(VisualElement parent, string label, string[] choices, int index, Action<int> changed)
        {
            var f = new DropdownField(label, choices.ToList(), Mathf.Clamp(index, 0, choices.Length - 1));
            f.AddToClassList("dropdown");
            f.RegisterValueChangedCallback(e => Try(() => changed(choices.ToList().IndexOf(e.newValue))));
            parent.Add(f);
        }
    }

    [Serializable]
    public sealed class NativeCampOptions
    {
        public int fps = 60;
        public int quality, effects, music;
        public int uiScale = 100;
        public bool cpu, sound, musicOn, visibility, fog;
    }
}
