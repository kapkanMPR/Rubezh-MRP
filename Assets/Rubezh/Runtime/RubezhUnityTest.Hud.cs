using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
#endif
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
        VisualElement nativeHudControls, nativeOrderRule;
        Label nativeOrder, nativeOrderTitle, nativeRaid, nativeCompletion;
        Button nativePause, nativeZoom;
        VisualElement nativePauseIcon;
        readonly List<Button> nativeSpeedButtons = new List<Button>();
        readonly List<Action> nativeBindings = new List<Action>();
        readonly List<Label> nativeSpeechLabels = new List<Label>();
        readonly List<Rect> nativeSpeechRects = new List<Rect>();
        readonly Dictionary<Label,Vector2> nativeSpeechSizes=new Dictionary<Label,Vector2>();
        SpeechMode nativeMeasuredSpeechMode;
        string nativeDrawer = "";
        Action nativeOverlayRebuild;
        float nativeDetailsAt, nativeHudWidth;
        FontAsset nativeBattleFont;
        Label nativeDialogueText, nativeDialogueHint;
        int nativeDialogueLine = -1;
        bool nativeResultsShown, nativePauseState;
        readonly List<Label> nativeRaidCounts=new List<Label>();

        void InitializeBattleFont(VisualElement root)
        {
            // Exact source font used by the main battle interface; CampSans has different metrics.
            var source = Resources.Load<Font>("Fonts/RubezhSans");
            if (source == null) throw new InvalidOperationException("Отсутствует боевой шрифт Fonts/RubezhSans");
            nativeBattleFont = FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            root.style.unityFontDefinition = FontDefinition.FromSDFFont(nativeBattleFont);
            root.style.color = ink;
        }
        void ReleaseBattleFont()
        {
            if (nativeBattleFont == null) return;
            foreach (var atlas in nativeBattleFont.atlasTextures) if (atlas != null) Destroy(atlas);
            if (nativeBattleFont.material != null) Destroy(nativeBattleFont.material);
            Destroy(nativeBattleFont); nativeBattleFont = null;
        }
        VisualElement HudBox(VisualElement root, float x, float y, float width, float height)
        {
            var element = new VisualElement(); Place(element, x, y, width, height); root.Add(element); return element;
        }
        static void Place(VisualElement element, float x, float y, float width, float height)
        {
            element.style.position = Position.Absolute; element.style.left = x; element.style.top = y;
            element.style.width = width; element.style.height = height;
        }
        Button HudButton(VisualElement root, string text, Action action, float x, float y, float width, float height)
        {
            var result = new Button(action) { text = text }; result.AddToClassList("battle-button");
            result.style.unityFontDefinition=FontDefinition.FromSDFFont(nativeBattleFont);
            Place(result, x, y, width, height); root.Add(result); return result;
        }
        Label HudLabel(VisualElement root, string text, float x, float y, float width, float height, int size)
        {
            var label = new Label(text); Place(label, x, y, width, height);
            label.style.unityFontDefinition=FontDefinition.FromSDFFont(nativeBattleFont); label.style.fontSize = size; label.style.whiteSpace = WhiteSpace.Normal;
            label.pickingMode = PickingMode.Ignore; root.Add(label); return label;
        }
        Label NativeParagraph(VisualElement root, string text, int size = 23, bool bold = false, Color? color = null)
        {
            var label = new Label(text); label.style.unityFontDefinition=FontDefinition.FromSDFFont(nativeBattleFont); label.style.fontSize = size; label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.color = color ?? muted; label.style.marginBottom = 18; label.style.flexShrink = 0;
            label.pickingMode = PickingMode.Ignore; root.Add(label); return label;
        }
        Label NativeBound(VisualElement root, Func<string> text, int size = 23, bool bold = false, Color? color = null)
        {
            var label = NativeParagraph(root, text(), size, bold, color);
            nativeBindings.Add(() => { var next=text(); if(label.text!=next)label.text=next; }); return label;
        }
        Button NativeAction(VisualElement root, string text, Action action, float height = 65, bool primaryAction = false)
        {
            var result = new Button(action) { text = text }; result.AddToClassList("battle-button");
            result.style.unityFontDefinition=FontDefinition.FromSDFFont(nativeBattleFont);
            result.style.height = height; result.style.minHeight = height; result.style.flexShrink = 0;
            result.style.marginBottom = 15;
            if (primaryAction) result.AddToClassList("battle-primary");
            root.Add(result); return result;
        }
        void NativeRule(VisualElement root)
        {
            var line = new VisualElement(); line.style.height = 2; line.style.marginTop = 12;
            line.style.marginBottom = 25; line.style.backgroundColor = new Color(.21f, .28f, .27f); root.Add(line);
        }
        VisualElement NativeIcon(VisualElement root, int kind, float x, float y, float width, float height)
        {
            // Same geometry as DrawIcon. Glyph substitutions depend on OS/font and were visibly wrong.
            var icon = HudBox(root, x, y, width, height); icon.pickingMode = PickingMode.Ignore;
            Action<float,float,float,float> rect = (a,b,w,h) => {
                var bit=HudBox(icon,a,b,w,h);bit.style.backgroundColor=ink;bit.pickingMode=PickingMode.Ignore;
            };
            float w0=width-8,h0=height-8;
            if(kind==3){for(int i=0;i<3;i++)rect(4,4+i*h0/3,w0,3);}
            else if(kind==6){rect(4,4,5,h0);rect(4+w0-5,4,5,h0);}
            else if(kind==5){for(int i=0;i<12;i++)rect(4+i*w0/12,4+i*h0/24,w0/12+1,h0-i*h0/12);}
            else if(kind==7){rect(4+w0/2-1,4,3,h0);rect(4,4+h0/2-1,w0,3);}
            else if(kind==8){rect(4,4,w0,3);rect(4,4+h0-3,w0,3);rect(4,4,3,h0);rect(4+w0-3,4,3,h0);}
            else for(int i=0;i<12;i++){
                float a=i/11f,px=4+a*w0,py=4+(a<.5f?a:1-a)*h0;
                if(kind==4)py=4+h0-(py-4);
                if(kind==1||kind==2){px=4+(a<.5f?a:1-a)*w0;py=4+a*h0;if(kind==2)px=4+w0-(px-4);}
                rect(px,py,3,3);
            }
            return icon;
        }
        Button NativeIconButton(VisualElement root, string text, int icon, Action action, float x, float y, float w, float h, bool vertical=false)
        {
            var button=HudButton(root,"",action,x,y,w,h);
            if(text.Length==0)NativeIcon(button,icon,w/2-13,h/2-13,26,26);
            else {
                NativeIcon(button,icon,vertical?w/2-12:18,vertical?18:h/2-12,24,24);
                var label=HudLabel(button,text,vertical?4:47,vertical?60:0,vertical?w-8:w-55,vertical?38:h,23);
                label.style.unityTextAlign=TextAnchor.MiddleCenter;
            }
            return button;
        }
        void CreateNativeBattleControls(VisualElement root)
        {
            if(nativeHudControls==null){var sheet=Resources.Load<StyleSheet>("NativeBattle/Hud");if(sheet!=null)root.styleSheets.Add(sheet);}
            nativeHudWidth=W; nativeSpeedButtons.Clear(); nativeSpeechLabels.Clear();nativeSpeechSizes.Clear();
            nativeHudControls=HudBox(root,0,0,W,H);nativeHudControls.pickingMode=PickingMode.Ignore;
            var title=HudLabel(nativeHudControls,"РУБЕЖ",30,32,380,50,34);title.style.unityFontStyleAndWeight=FontStyle.Bold;
            var subtitle=HudLabel(nativeHudControls,"ВИД С ДРОНА",30,88,480,30,21);subtitle.style.color=muted;subtitle.style.unityFontStyleAndWeight=FontStyle.Bold;
            nativeOrderTitle=HudLabel(nativeHudControls,"",43,125,650,40,23);
            nativeOrderRule=HudBox(nativeHudControls,20,135,4,133);nativeOrderRule.pickingMode=PickingMode.Ignore;
            nativeOrder=HudLabel(nativeHudControls,"",43,168,Mathf.Min(700,W*.38f),140,23);
            NativeIconButton(nativeHudControls,"РАДИОКАНАЛ",0,()=>NativeDrawer("radio"),W/2-185,0,370,72);
            NativeIconButton(nativeHudControls,"Журнал",3,()=>NativeDrawer("journal"),W-250,22,225,70);
            NativeIconButton(nativeHudControls,"СТИЛЬ",1,()=>NativeDrawer("style"),0,H/2-60,150,120,true);
            NativeIconButton(nativeHudControls,"МАНЁВР",2,()=>NativeDrawer("maneuver"),W-150,H/2-60,150,120,true);
            NativeIconButton(nativeHudControls,"ОТРЯД · КЕДР",4,()=>NativeDrawer("squad"),W/2-195,H-72,390,72);
            var controls=HudBox(nativeHudControls,20,H-103,680,82);controls.AddToClassList("battle-panel");
            nativePause=HudButton(controls,"",()=>{paused=!paused;UpdatePauseIcon();},10,10,65,62);UpdatePauseIcon();
            for(int i=0;i<3;i++){int value=1<<i;var button=HudButton(controls,value+"x",()=>speed=value,88+i*65,10,60,62);button.style.fontSize=21;nativeSpeedButtons.Add(button);}
            HudButton(controls,"−",()=>NativeZoomAt(zoom-.25f),290,10,60,62).style.fontSize=21;
            nativeZoom=HudButton(controls,"100%",()=>{zoom=1;pan=Vector2.zero;renderedBattle=null;},355,10,105,62);nativeZoom.style.fontSize=21;
            HudButton(controls,"+",()=>NativeZoomAt(zoom+.25f),465,10,60,62).style.fontSize=21;
            NativeIconButton(controls,"",7,()=>{Focus();renderedBattle=null;},535,10,65,62);
            if(DeveloperBuild.DebugAvailable)NativeIconButton(controls,"",8,()=>debug=!debug,610,10,50,62);
            if(WatchingInteresting){
                HudLabel(nativeHudControls,"ПРОСМОТР · БОЙ №"+interestingSession.Entry.id,30,330,620,45,23).style.color=gold;
                HudButton(nativeHudControls,"К интересным боям",()=>{paused=true;HideUnityTestHud();screen=7;CloseDrawer();},W-400,110,370,60).style.fontSize=21;
            }
            if(testBattle)HudButton(nativeHudControls,"Диагностика",()=>NativeDrawer("diagnostic"),W-250,105,225,55).style.fontSize=21;
            testStatus=HudLabel(nativeHudControls,"",20,295,Mathf.Min(1100,W-40),150,23);testStatus.AddToClassList("battle-diagnostic");
            if(DeveloperBuild.Enabled&&!WatchingInteresting)NativeDeveloperReportsButton();
            nativeCompletion=HudLabel(nativeHudControls,"",W/2-240,85,480,40,23);nativeCompletion.style.color=gold;
            nativeRaidCounts.Clear();
            if(IsRaid){float x=W/2+197,w=Mathf.Min(360,W-260-x),y=4;if(w<240){w=280;x=W/2+15;y=82;}
                var raidPanel=HudBox(nativeHudControls,x,y,w,76);raidPanel.style.backgroundColor=new Color(.055f,.105f,.11f,.94f);
                nativeRaid=HudLabel(raidPanel,"",10,4,w-20,28,18);
                if(raidClothIcon==null)raidClothIcon=Resources.Load<Texture2D>("FieldCamp/Art/resource-cloth");
                if(raidPartsIcon==null)raidPartsIcon=Resources.Load<Texture2D>("FieldCamp/Art/resource-parts");
                float cell=(w-16)/3;
                for(int i=0;i<3;i++){
                    var texture=i==1?raidClothIcon:i==2?raidPartsIcon:null;
                    if(texture!=null){var icon=new Image{image=texture,scaleMode=ScaleMode.ScaleToFit};Place(icon,8+i*cell,37,24,26);icon.pickingMode=PickingMode.Ignore;raidPanel.Add(icon);}
                    else HudLabel(raidPanel,i==0?"◇":i==1?"В":"З",8+i*cell,37,24,26,18);
                    nativeRaidCounts.Add(HudLabel(raidPanel,"",34+i*cell,37,cell-26,30,18));
                }}
            else nativeRaid=null;
            for(int i=0;i<6;i++){var label=HudLabel(nativeHudControls,"",0,0,430,100,24);label.style.unityTextAlign=TextAnchor.MiddleCenter;label.style.unityFontStyleAndWeight=FontStyle.Bold;nativeSpeechLabels.Add(label);}
        }
        void UpdatePauseIcon(){nativePauseState=paused;if(nativePauseIcon!=null)nativePauseIcon.RemoveFromHierarchy();nativePauseIcon=NativeIcon(nativePause,paused?5:6,65/2f-13,62/2f-13,26,26);}
        void NativeZoomAt(float value){ZoomAt(new Vector2(W/2,H/2),Mathf.Clamp(value,.65f,3));renderedBattle=null;}
        void CloseNativeDrawer()
        {
            testOverlay?.RemoveFromHierarchy(); testOverlay=null; nativeDrawer="";nativeOverlayRebuild=null;nativeBindings.Clear();
            nativeDialogueText=nativeDialogueHint=null;nativeDialogueLine=-1;CancelUnityTestPointers();
        }
        ScrollView NativePanel(string kind,string title,float width,float left,float top,float height,bool modal=false)
        {
            CloseNativeDrawer(); nativeDrawer=kind;
            var shade=HudBox(testDocument.rootVisualElement,0,0,W,H);shade.style.backgroundColor=new Color(0,0,0,modal?.7f:.42f);testOverlay=shade;
            top=Mathf.Max(0,top);height=Mathf.Min(height,H-top);
            var panelRoot=HudBox(shade,left,top,width,height);panelRoot.AddToClassList("battle-panel");
            if(!modal)shade.RegisterCallback<PointerDownEvent>(e=>{if(ReferenceEquals(e.target,shade)){CloseNativeDrawer();e.StopPropagation();}});
            var header=HudLabel(panelRoot,title,35,38,width-140,70,34);header.style.unityFontStyleAndWeight=FontStyle.Bold;
            if(!modal)HudButton(panelRoot,"×",CloseNativeDrawer,width-100,25,70,70);
            var body=new ScrollView(kind=="squad"?ScrollViewMode.VerticalAndHorizontal:ScrollViewMode.Vertical);body.style.position=Position.Absolute;
            body.style.left=35;body.style.right=35;body.style.top=125;body.style.bottom=25;panelRoot.Add(body);
            return body;
        }
        void NativeDrawer(string kind)
        {
            if(nativeDrawer==kind){CloseNativeDrawer();return;}
            if(WatchingInteresting&&(kind=="style"||kind=="maneuver"))return;
            float width=kind=="radio"?Mathf.Min(1600,W-180):kind=="squad"?W-50:Mathf.Min(850,W*.55f);
            float left=kind=="style"?0:kind=="radio"?(W-width)/2:kind=="squad"?25:W-width;
            string title=kind=="style"?"Стиль боя":kind=="maneuver"?"Направление и охват":kind=="radio"?"Радиоканал · Кедр":kind=="squad"?"Отделение Кедр":kind=="reports"?"Отчёты":kind=="settings"?"Настройки":kind=="diagnostic"?"Тест · диагностика":"Журнал и сведения";
            var content=NativePanel(kind,title,width,left,kind=="radio"?60:kind=="squad"?250:0,kind=="radio"?850:kind=="squad"?H-235:H);
            if(kind=="style"||kind=="maneuver")NativePriorityPanel(content,kind);
            else if(kind=="radio")NativeRadioPanel(content);
            else if(kind=="squad")CreateNativeSquadCards(content,width);
            else if(kind=="journal")NativeJournalPanel(content);
            else if(kind=="settings")NativeSettingsPanel(content);
            else if(kind=="reports")NativeDeveloperReports(content);
            else NativeDiagnosticPanel(content);
            nativeDetailsAt=0;nativeOverlayRebuild=()=>NativeDrawer(kind);
        }
        void NativePriorityPanel(VisualElement content,string kind)
        {
            NativeParagraph(content,kind=="style"?"ДА НУ НАХЕР! — немедленно отменить задачи и бежать назад. Остальные приоритеты передаются по радио.":"Двойки работают на своих флангах; при обхвате противоположный фланг прикрывает.").style.minHeight=100;
            var names=kind=="style"?StylesList:Maneuvers;
            for(int i=0;i<names.Length;i++){
                int id=kind=="style"?StyleIds[i]:ManeuverIds[i];
                var choice=NativeAction(content,names[i],()=>{
                    if(kind=="style"){style=id;if(style!=3&&maneuver==6)maneuver=0;}else maneuver=id;
                    CommanderPriorities.Request(battle,style,maneuver);CloseNativeDrawer();
                },kind=="style"?72:66);
                choice.style.unityTextAlign=TextAnchor.MiddleLeft;
                if((kind=="style"?style:maneuver)==id)choice.AddToClassList("battle-chosen");
            }
            NativeRule(content);
            NativeParagraph(content,kind=="style"?"Аварийный отход сбрасывает боевые задачи без лечения и пополнения БК. Закрепление — удержание и пополнение в затишье. Агрессия — наступление по замыслу с оценкой прикрытия.":"Центр — компактный срединный сектор. Фронтом — растянуться широко по линии; Прямо — сохранить прежние полосы. Обхват — часть прикрывает, другая обходит.");
        }
        void NativeRadioPanel(VisualElement content)
        {
            NativeBound(content,()=>"НАБЛЮДЕНИЕ · "+((int)battle.Time/60).ToString("00")+":"+((int)battle.Time%60).ToString("00")+" · Сценарий "+battle.Seed+" · Бой "+battle.CombatSeed,34,true,ink);
            NativeBound(content,()=>"КЕДР "+battle.Alive(Side.Green)+"/5     ГРАНИТ "+battle.Alive(Side.Red)+"/"+(battle.CampaignTotal>0?battle.CampaignTotal:5),34,true,ink);
            NativeLiveMeter(content,"Точка «Рубеж»",()=>battle.Capture,accent);
            NativeBound(content,()=>battle.Green.PriorityStatus,23,false,gold);
            NativeBound(content,()=>battle.Green.CommanderOrder+"\nПоддержка: "+battle.Green.SupportStatus);
            var help=new VisualElement();
            NativeAction(content,(commandHelpOpen?"− ":"+ ")+"Как командир понимает приоритеты",()=>{commandHelpOpen=!commandHelpOpen;CloseNativeDrawer();NativeDrawer("radio");});
            if(commandHelpOpen)foreach(var line in UiCatalog.CommandHelp)NativeParagraph(content,line);
        }
        void NativeLiveMeter(VisualElement root,string title,Func<float> read,Color color,bool compact=false)
        {
            var meter=new VisualElement();meter.style.height=68;meter.style.flexShrink=0;meter.style.marginBottom=compact?0:12;root.Add(meter);
            var caption=new Label(title);caption.style.fontSize=23;caption.style.color=muted;meter.Add(caption);
            var value=new Label();value.style.position=Position.Absolute;value.style.right=0;value.style.top=0;value.style.fontSize=23;value.style.color=muted;meter.Add(value);
            var track=new VisualElement();track.style.position=Position.Absolute;track.style.left=0;track.style.right=0;track.style.top=42;track.style.height=9;track.style.backgroundColor=new Color(.04f,.08f,.09f);meter.Add(track);
            var fill=new VisualElement();fill.style.height=9;fill.style.backgroundColor=color;track.Add(fill);
            float shown=-1;Action update=()=>{float amount=Mathf.Clamp01(read());if(amount==shown)return;shown=amount;string text=Mathf.RoundToInt(amount*100)+"%";if(value.text!=text)value.text=text;fill.style.width=Length.Percent(amount*100);};update();nativeBindings.Add(update);
        }
        void NativeJournalPanel(VisualElement content)
        {
            // Keep the same fixed two-column controls as the original battle journal.
            var scroll=(ScrollView)content;scroll.style.top=385;scroll.style.bottom=20;
            var parent=scroll.parent;float width=Mathf.Min(850,W*.55f),bw=(width-80)/2;
            HudButton(parent,WatchingInteresting?"Интересные бои":"Состав в бою",()=>{if(WatchingInteresting){paused=true;HideUnityTestHud();screen=7;CloseDrawer();}else NativeDrawer("squad");},25,120,bw,55);
            HudButton(parent,"Радиоканал",()=>NativeDrawer("radio"),40+bw,120,bw,55);
            HudButton(parent,"Реплики: "+SpeechPresentation.Name(speechMode),()=>{
                NativeChoice("Режим реплик",SpeechPresentation.ModeNames,i=>{speechMode=(SpeechMode)i;PlayerPrefs.SetInt("rubezh.speechMode",i);PlayerPrefs.Save();NativeDrawer("journal");});
            },25,185,bw,55);
            if(DeveloperBuild.DebugAvailable)HudButton(parent,"Экспорт лога",Export,40+bw,185,bw,55);
            HudButton(parent,testBattle?"Покинуть тест":"Покинуть бой",NativeExitPrompt,25,250,bw,55);
            if(DeveloperBuild.DebugAvailable)HudButton(parent,debug?"Замысел: вкл":"Замысел: выкл",()=>{debug=!debug;CloseNativeDrawer();NativeDrawer("journal");},40+bw,250,bw,55);
            HudButton(parent,"Настройки",()=>NativeDrawer("settings"),25,315,bw,55);
            NativeParagraph(content,"ИНСПЕКЦИЯ БОЙЦА",21,true);
            var unit=battle.Soldiers.FirstOrDefault(s=>s.Id==selected)??battle.Soldiers.FirstOrDefault(s=>s.Deployed);
            if(unit==null){NativeParagraph(content,"Нет бойцов для инспекции.");return;}
            var identity=new VisualElement();identity.style.height=145;identity.style.flexShrink=0;content.Add(identity);
            NativePortrait(identity,unit,100,125);
            HudLabel(identity,unit.Name,125,0,width-210,55,34).style.unityFontStyleAndWeight=FontStyle.Bold;
            HudLabel(identity,LiveRole(unit),125,65,width-210,65,23).style.color=gold;
            NativeBound(content,()=>ActionName(unit)+" · "+(PositionLogic.Protected(battle,unit,unit.Position)?"За укрытием":"На открытом месте"));
            NativeLiveMeter(content,"Состояние",()=>unit.Health/100,accent);
            NativeLiveMeter(content,"Подавление",()=>unit.Suppression,gold);
            NativeLiveMeter(content,"Мораль (макс. "+(unit.MoraleMaximum*100).ToString("0")+")",()=>unit.Morale/Math.Max(.01f,unit.MoraleMaximum),new Color(.45f,.74f,.72f));
            NativeBound(content,()=>InventoryPresentation.Personal(unit));
            NativeBound(content,()=>InventoryPresentation.SpareWeapon(unit),23,false,gold);
            NativeBound(content,()=>InventoryPresentation.Backpack(unit));
            NativeBound(content,()=>unit.Reason+"\n"+(SquadBrain.Ready(battle,unit,battle.Team(unit.Side))?"Прикрытие готово":"Прикрытие не подтверждено"));
            NativeAction(content,(rulesOpen?"− ":"+ ")+"Задача и правила симуляции",()=>{rulesOpen=!rulesOpen;CloseNativeDrawer();NativeDrawer("journal");});
            if(rulesOpen)foreach(var line in UiCatalog.SimulationRules)NativeParagraph(content,line);
            NativeRule(content);NativeParagraph(content,"ЖУРНАЛ · ОБЕ СТОРОНЫ",34,true,ink);
            var log=NativeParagraph(content,"");int last=-1;
            nativeBindings.Add(()=>{if(last==battle.Events.Count)return;last=battle.Events.Count;
                var text=new System.Text.StringBuilder();for(int i=last-1;i>=Math.Max(0,last-40);i--){var entry=battle.Events[i];text.Append(entry.Side==Side.Green?"СВОИ · ":"ПРОТИВНИК · ").Append(entry.Text).Append("\n\n");}log.text=text.ToString();});
        }
        Image NativePortrait(VisualElement root,Soldier unit,float width,float height)
        {
            var portrait=new Image();portrait.style.width=width;portrait.style.height=height;portrait.scaleMode=ScaleMode.ScaleToFit;
            portrait.pickingMode=PickingMode.Ignore;
            portrait.image=unit.Side==Side.Green?CanonicalPortrait(unit.Look):null;
            if(portrait.image==null){Texture2D texture;Rect uv;
                NativeUiArt.Resolve(NativeUiArt.PortraitKey(unit.WeaponModel,unit.Side==Side.Red,unit.Look,WeaponBalance.Index(unit)>=6,unit.Role,unit.Id),out texture,out uv);
                portrait.image=texture;portrait.uv=uv;
            }
            root.Add(portrait);return portrait;
        }
        Image NativeEquipmentIcon(VisualElement root,int weapon,float x,float y,float width,float height)
        {
            Texture2D texture;Rect uv;NativeUiArt.Resolve("i"+(weapon==4?34:weapon==2?36:weapon==1?9:8),out texture,out uv);
            var image=new Image { image=texture,uv=uv,scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore };
            Place(image,x,y,width,height);root.Add(image);return image;
        }
        void CreateNativeSquadCards(VisualElement body,float width)
        {
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;body.Add(row);float cw=Mathf.Max(285,(width-110)/5);
            for(int i=0;i<Math.Min(5,battle.Soldiers.Count);i++){
                var unit=battle.Soldiers[i];int id=unit.Id;
                var card=new VisualElement();card.AddToClassList("battle-squad-card");card.style.width=cw;card.style.minWidth=cw;
                card.style.minHeight=688;card.style.marginRight=16;card.style.paddingLeft=20;card.style.paddingRight=20;card.style.paddingTop=15;row.Add(card);
                if(battle.CampaignLevel==6&&i==4){NativeParagraph(card,unit.Name+" · РАНЕН",24,true,ink);NativeParagraph(card,"Остался вне операции. Вернётся после госпиталя.",20);continue;}
                card.RegisterCallback<ClickEvent>(e=>{selected=id;Focus();renderedBattle=null;});
                var head=new VisualElement();head.style.height=181;head.style.flexShrink=0;card.Add(head);
                var portrait=NativePortrait(head,unit,85,106);Place(portrait,-10,0,85,106);HudLabel(head,unit.Name,85,10,cw-120,40,24).style.unityFontStyleAndWeight=FontStyle.Bold;
                HudLabel(head,LiveRole(unit),85,55,cw-120,60,20).style.color=muted;
                var secondary=HudLabel(head,"",49,113,cw-84,62,20);secondary.style.color=muted;
                Image secondaryIcon=null;int shownBack=-1;
                nativeBindings.Add(()=>{
                    string text=unit.HasBackWeapon?"2-е: "+unit.BackAmmo+" патр.":"";
                    if(secondary.text!=text)secondary.text=text;
                    if(unit.HasBackWeapon&&shownBack!=unit.BackVariant){secondaryIcon?.RemoveFromHierarchy();secondaryIcon=NativeEquipmentIcon(head,unit.BackVariant,-5,110,48,60);shownBack=unit.BackVariant;}
                    if(secondaryIcon!=null)secondaryIcon.style.display=unit.HasBackWeapon?DisplayStyle.Flex:DisplayStyle.None;
                    card.EnableInClassList("battle-chosen",selected==id);
                });
                NativeBound(card,()=>ActionName(unit),23,false,gold).style.minHeight=35;
                NativeLiveMeter(card,"ЗДР",()=>unit.Health/100,accent,true);
                NativeLiveMeter(card,"ПОД",()=>unit.Suppression,gold,true);
                NativeLiveMeter(card,"МОР (макс. "+(unit.MoraleMaximum*100).ToString("0")+")",()=>unit.Morale/Math.Max(.01f,unit.MoraleMaximum),new Color(.45f,.74f,.72f),true);
                NativeBound(card,()=>InventoryPresentation.Personal(unit),20).style.marginTop=12;
                NativeBound(card,()=>InventoryPresentation.Backpack(unit),20);
            }
        }
        void NativeChoice(string title,string[] choices,Action<int> chosen)
        {
            var body=NativePanel("choice",title,Mathf.Min(1100,W-100),(W-Mathf.Min(1100,W-100))/2,100,H-200,true);
            for(int i=0;i<choices.Length;i++){int value=i;NativeAction(body,choices[i],()=>{CloseNativeDrawer();chosen(value);},78);}
            nativeOverlayRebuild=()=>NativeChoice(title,choices,chosen);
        }
        void NativeExitPrompt()
        {
            if(testBattle){ExitUnityTest();return;}
            NativeChoice(IsRaid?"Завершить рейд? Полученная добыча сохранится.":"Покинуть бой без награды?",new[]{"Остаться","Покинуть бой"},i=>{
                if(i==0)return;if(IsRaid){FinishRaid("Рейд завершён игроком",true);return;}
                if(campBattle!=null)CampReceiptFor(campBattle,false);campBattle=null;battle=null;paused=true;screen=0;ShowCamp();
            });
        }
        void NativeDiagnosticPanel(VisualElement content)
        {

            NativeAction(content,"Дым: показать / скрыть",()=>{diagDisableSmoke=!diagDisableSmoke;renderedBattle=null;});
            NativeAction(content,"Крыши: показать / скрыть",()=>{diagDisableRoofs=!diagDisableRoofs;renderedBattle=null;});
            NativeAction(content,"Окружение: показать / скрыть",()=>{diagDisableScenery=!diagDisableScenery;renderedBattle=null;});
            NativeAction(content,"Сохранить отчёт",ExportUnityTestReport);NativeAction(content,"Выйти",NativeExitPrompt);
        }
        void NativeSettingsPanel(VisualElement content)
        {
            NativeAction(content,"Диагностика графики",()=>NativeDrawer("diagnostic"));
            NativeAction(content,"Частота кадров: "+frameRateLimit,()=>NativeChoice("Частота кадров",Array.ConvertAll(FrameRateChoices,x=>x+" FPS"),i=>{SetFrameRate(FrameRateChoices[i]);NativeDrawer("settings");}));
            NativeAction(content,"Графика",()=>NativeChoice("Графика",new[]{"ПИКСЕЛИ!!!","Низкое","Среднее","Высокое","Ультра"},i=>{SetGraphics(i-1);NativeDrawer("settings");}));
            NativeAction(content,"Звуки: "+(soundEnabled?"включены":"выключены"),()=>{SetSound(!soundEnabled);CloseNativeDrawer();NativeDrawer("settings");});
            NativeAction(content,"Музыка: "+(musicEnabled?"включена":"выключена"),()=>{ApplyCampOption("musicOn",musicEnabled?0:1);CloseNativeDrawer();NativeDrawer("settings");});
            NativeAction(content,"Видимость: "+(fullVisibility?"полная":"разведка"),()=>{fullVisibility=!fullVisibility;SaveVisibility();CloseNativeDrawer();NativeDrawer("settings");});
            if(!fullVisibility)NativeAction(content,"Туман войны: "+(hardcoreFog?"жёсткий":"обычный"),()=>{hardcoreFog=!hardcoreFog;SaveVisibility();CloseNativeDrawer();NativeDrawer("settings");});
            NativeAction(content,"Реплики: "+SpeechPresentation.Name(speechMode),()=>NativeChoice("Режим реплик",SpeechPresentation.ModeNames,i=>{speechMode=(SpeechMode)i;PlayerPrefs.SetInt("rubezh.speechMode",i);PlayerPrefs.Save();NativeDrawer("settings");}));
            NativeAction(content,"Полный сброс прогресса",()=>NativeChoice("Удалить весь прогресс и начать заново?",new[]{"Отмена","Удалить весь прогресс"},i=>{
                if(i==0){NativeDrawer("settings");return;}
                try{ResetCareer(true);}catch(Exception e){message="Не удалось сбросить прогресс: "+e.Message;NativeDrawer("settings");}
            }));
            foreach(string key in new[]{"effects","music"}){string option=key;var slider=new SliderInt(option=="effects"?"Громкость эффектов":"Громкость музыки",0,100);slider.value=Mathf.RoundToInt((option=="effects"?effectsVolume:musicVolume)*100);slider.RegisterValueChangedCallback(e=>ApplyCampOption(option,e.newValue));slider.style.height=60;content.Add(slider);}
        }
        void UpdateNativeBattleControls()
        {
            if(nativeHudControls==null)return;
            if(Math.Abs(nativeHudWidth-W)>2){var rebuild=nativeOverlayRebuild;CloseNativeDrawer();nativeHudControls.RemoveFromHierarchy();CreateNativeBattleControls(testDocument.rootVisualElement);if(rebuild!=null)rebuild();nativeResultsShown=false;}
            nativeHudControls.SetEnabled(testOverlay==null&&!MissionDialogueActive&&battle.Outcome==Outcome.Running);
            if(nativePauseState!=paused)UpdatePauseIcon();
            string zoomLabel=Mathf.RoundToInt(zoom*100)+"%";if(nativeZoom.text!=zoomLabel)nativeZoom.text=zoomLabel;
            for(int i=0;i<nativeSpeedButtons.Count;i++){if(speed==(1<<i))nativeSpeedButtons[i].AddToClassList("battle-chosen");else nativeSpeedButtons[i].RemoveFromClassList("battle-chosen");}
            bool important=importantCommands.Text.Length>0,spoken=speechMode!=SpeechMode.Off;
            nativeOrderTitle.text=spoken?(important?"КЕДР · ВАЖНАЯ КОМАНДА":"КЕДР · КОМАНДИР"):"";
            string orderText=spoken?(important?importantCommands.Text:battle.Green.CommanderOrder):"";
            if(nativeOrder.text!=orderText){
                nativeOrder.text=orderText;
                var measured=nativeOrder.MeasureTextSize(orderText,Mathf.Min(700,W*.38f),VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined);
                float height=Mathf.Max(100,measured.y);nativeOrder.style.height=height;nativeOrderRule.style.height=height+33;
            }
            Color tint=important?new Color(.35f,1,.48f):gold;
            nativeOrderTitle.style.color=tint;nativeOrder.style.color=important?tint:muted;
            nativeOrder.style.unityFontStyleAndWeight=important?FontStyle.Bold:FontStyle.Normal;
            nativeOrderRule.style.backgroundColor=spoken?tint:Color.clear;
            nativeCompletion.text=battle.EnemyClearedAt>=0&&battle.Outcome==Outcome.Running?"Завершение операции: "+Mathf.CeilToInt(MissionCompletion.Remaining(battle))+" с":"";
            if(nativeRaid!=null){nativeRaid.text="ВОЛНА "+raid.wave;nativeRaidCounts[0].text=raid.supply.ToString();nativeRaidCounts[1].text=raid.cloth.ToString();nativeRaidCounts[2].text=raid.parts.ToString();}
            testStatus.style.display=debug||testBattle||!string.IsNullOrEmpty(unityTestError)?DisplayStyle.Flex:DisplayStyle.None;
            if(MissionDialogueActive){UpdateNativeDialogue();return;}
            if(nativeDrawer=="dialogue")CloseNativeDrawer();
            if(battle.Outcome!=Outcome.Running){if(!nativeResultsShown){nativeResultsShown=true;NativeResults();}return;}
            UpdateNativeSpeech();
            if(Time.unscaledTime<nativeDetailsAt)return;nativeDetailsAt=Time.unscaledTime+.15f;
            foreach(var update in nativeBindings)update();
        }
#endif
    }
}
