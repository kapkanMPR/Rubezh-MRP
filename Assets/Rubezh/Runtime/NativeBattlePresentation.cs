#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using System;
using System.Collections.Generic;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;
using UnityEngine.UIElements;
namespace Rubezh.UnityPort
{
    public sealed partial class RubezhApp
    {
        Image nativeFog, nativeForeground, nativeNight;
        VisualElement nativeWorldOverlay;
        Image nativeRainOverlay;
        readonly List<VisualElement> nativeMarks=new List<VisualElement>();
        readonly List<Image> nativeGhosts=new List<Image>();
        int nativeMarkCount;
        void CreateNativeWorldOverlays(VisualElement root)
        {
            nativeWorldOverlay=HudBox(root,0,0,W,H);nativeWorldOverlay.pickingMode=PickingMode.Ignore;
            nativeNight=NativeOverlayImage(nativeWorldOverlay);
            nativeFog=NativeOverlayImage(nativeWorldOverlay);nativeForeground=NativeOverlayImage(nativeWorldOverlay);
            if(battle.CampaignLevel==20)nativeRainOverlay=NativeOverlayImage(nativeWorldOverlay);
        }
        Image NativeOverlayImage(VisualElement parent)
        {
            var image=new Image();image.pickingMode=PickingMode.Ignore;image.scaleMode=ScaleMode.StretchToFill;
            image.style.position=Position.Absolute;parent.Add(image);return image;
        }
        void UpdateNativeWorldOverlays()
        {
            if(nativeWorldOverlay==null)return;
            Place(nativeWorldOverlay,0,0,W,H);
            if(battle.CampaignLevel==20){UpdateNativeNightMask();nativeNight.image=nativeNightTarget;Place(nativeNight,mapRect.x,mapRect.y,mapRect.width,mapRect.height);
                UpdateNativeRain();nativeRainOverlay.image=nativeRainTarget;Place(nativeRainOverlay,0,0,W,H);}

            if(!fullVisibility){UpdateNativeFogMask();nativeFog.image=nativeFogTarget;Place(nativeFog,mapRect.x,mapRect.y,mapRect.width,mapRect.height);
                nativeForeground.image=hdBattle?.ForegroundTexture;Place(nativeForeground,0,0,W,H);
            }else {nativeFog.image=null;nativeForeground.image=null;}
            int ghosts=0;
            if(!fullVisibility)foreach(var ghost in battle.Fog.Ghosts.Values){
                if(battle.Fog.Visible.Contains(ghost.Id))continue;
                float opacity=Math.Max(0,1-(battle.Time-ghost.SeenAt)/2.5f);if(opacity<=0)continue;
                if(ghosts==nativeGhosts.Count){EnsureGhostFigure();var image=NativeOverlayImage(nativeWorldOverlay);image.image=ghostFigure;nativeGhosts.Add(image);}
                var item=nativeGhosts[ghosts++];var point=ToView(ghost.Position);float size=mapRect.height/MapHeight;Place(item,point.x-7*size,point.y-27*size,14*size,27*size);item.tintColor=new Color(1,1,1,opacity*.8f);item.style.display=DisplayStyle.Flex;
            }
            for(int i=ghosts;i<nativeGhosts.Count;i++)nativeGhosts[i].style.display=DisplayStyle.None;
            nativeMarkCount=0;
            foreach(var unit in battle.Soldiers){
                if(!unit.Deployed||!unit.Alive||!battle.Fog.CanDraw(unit))continue;
                var p=ToView(painter == null ? unit.Position : painter.VisualPosition(unit));if(p.x<-30||p.x>W+30||p.y<-20||p.y>H+100)continue;
                if(unit.Broken){float scale=Mathf.Clamp(mapRect.height/MapHeight,.65f,1.45f),d=5*scale,t=Mathf.Max(1.7f,2*scale),y=p.y-44*mapRect.height/MapHeight;var red=new Color(.96f,.23f,.22f);
                    for(int k=-2;k<=2;k++){float step=k*d*.38f;NativeMark(p.x+step-t*.5f,y+step-t*.5f,t,t,red);NativeMark(p.x+step-t*.5f,y-step-t*.5f,t,t,red);}NativeMark(p.x-d-1,y+d+2*scale,2*d+2,t,red);}
                if(ShouldShowConcussionStars(unit)){float scale=Mathf.Clamp(mapRect.height/MapHeight,.55f,1.6f),y=p.y-25*mapRect.height/MapHeight,u=Mathf.Max(2,2.6f*scale);
                    var tint=new Color(1,.88f,.34f,Mathf.Clamp(ConcussionEffects.Current(battle,unit)*2,.35f,1));
                    for(int i=0;i<3;i++){float a=Time.unscaledTime*4.3f+unit.Id*.43f+i*2.0943951f,x=p.x+(float)Math.Cos(a)*12*scale,sy=y+(float)Math.Sin(a)*5*scale;
                        NativeMark(x-u*.5f,sy-u*1.5f,u,u*3,tint);NativeMark(x-u*1.5f,sy-u*.5f,u*3,u,tint);}}
            }
            for(int i=nativeMarkCount;i<nativeMarks.Count;i++)nativeMarks[i].style.display=DisplayStyle.None;
        }
        void NativeMark(float x,float y,float w,float h,Color tint)
        {
            if(nativeMarkCount==nativeMarks.Count){var mark=HudBox(nativeWorldOverlay,x,y,w,h);mark.pickingMode=PickingMode.Ignore;nativeMarks.Add(mark);}
            var item=nativeMarks[nativeMarkCount++];Place(item,x,y,w,h);item.style.backgroundColor=tint;item.style.display=DisplayStyle.Flex;
        }
        void ReleaseNativeWorldOverlays(){ReleaseNativeMasks();nativeWorldOverlay=null;nativeFog=nativeForeground=nativeNight=null;ReleaseNativeRain();nativeRainOverlay=null;nativeMarks.Clear();nativeGhosts.Clear();}
        void UpdateNativeSpeech()
        {
            foreach(var label in nativeSpeechLabels)label.style.display=DisplayStyle.None;
            if(nativeMeasuredSpeechMode!=speechMode){nativeMeasuredSpeechMode=speechMode;nativeSpeechSizes.Clear();}

            nativeSpeechRects.Clear();nativeSpeechRects.Add(new Rect(10,20,Mathf.Min(720,W*.4f),330));
            nativeSpeechRects.Add(new Rect(W/2-175,0,350,80));nativeSpeechRects.Add(new Rect(W-260,15,245,80));
            nativeSpeechRects.Add(new Rect(0,H/2-65,150,130));nativeSpeechRects.Add(new Rect(W-150,H/2-65,150,130));nativeSpeechRects.Add(new Rect(0,H-120,W,120));
            bool smallScreen=Screen.safeArea.height<700;int limit=speechMode==SpeechMode.Full?(smallScreen?3:4):(smallScreen?2:3),slot=0;
            if(speechMode!=SpeechMode.Off)foreach(var unit in SpeechPresentation.Choose(battle,selected,speechMode,limit,s=>importantCommands.BubbleText(s).Length>0)){
                string held=importantCommands.BubbleText(unit),spoken=held.Length>0?held:unit.Speech;
                string text=speechMode==SpeechMode.Full?unit.Name+": "+spoken:held.Length>0?held:unit.SpeechShort;
                var label=nativeSpeechLabels[slot];Vector2 extent;
                if(label.text!=text||!nativeSpeechSizes.TryGetValue(label,out extent)){
                    if(label.text!=text)label.text=text;
                    float maxWidth=speechMode==SpeechMode.Full?430:340;
                    var natural=label.MeasureTextSize(text,0,VisualElement.MeasureMode.Undefined,0,VisualElement.MeasureMode.Undefined);
                    float fittedWidth=Mathf.Min(maxWidth,natural.x+24);
                    var measured=label.MeasureTextSize(text,fittedWidth-20,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined);
                    extent=new Vector2(fittedWidth,measured.y+24);nativeSpeechSizes[label]=extent;
                }
                float width=extent.x,height=extent.y;var point=ToView(painter == null ? unit.Position : painter.VisualPosition(unit));point.y-=48*mapRect.height/MapHeight;
                var rect=new Rect(point.x-width/2,point.y-height-8,width,height);
                bool overlap=false;foreach(var used in nativeSpeechRects)if(used.Overlaps(rect)){overlap=true;break;}
                if(rect.x<2||rect.xMax>W-2||rect.y<2||rect.yMax>H-2||overlap){continue;}
                label.style.display=DisplayStyle.Flex;Place(label,rect.x,rect.y,rect.width,rect.height);nativeSpeechRects.Add(rect);
                label.style.color=unit.Side==Side.Red&&unit.RetreatQuoteUntil>battle.Time?new Color(1,.29f,.26f):ImportantCommandDisplay.BubbleImportant(spoken)?new Color(.35f,1,.48f):ink;
                if(++slot>=limit)break;
            }
            float elapsed=AmbushElapsed();
            if(elapsed>=0&&elapsed<8){var unit=battle.Soldiers.FirstOrDefault(s=>s.Id==battle.GreenVehicle.AmbushCommanderId);if(unit!=null){
                var label=nativeSpeechLabels[nativeSpeechLabels.Count-1];label.style.display=DisplayStyle.Flex;label.text=elapsed<4?"Группа контакт! ЗАСАДА!":"Рассредоточиться! В стороны!";
                float width=Mathf.Min(W-24,520);var p=ToView(painter == null ? unit.Position : painter.VisualPosition(unit));Place(label,Mathf.Clamp(p.x-width/2,12,W-width-12),Mathf.Clamp(p.y-145,100,H-215),width,80);label.style.color=ink;}}
        }
        void UpdateNativeDialogue()
        {
            if(nativeDrawer!="dialogue"||nativeDialogueLine!=dialogueIndex){
                CloseNativeDrawer();nativeDrawer="dialogue";nativeDialogueLine=dialogueIndex;
                var shade=HudBox(testDocument.rootVisualElement,0,0,W,H);shade.style.backgroundColor=new Color(0,0,0,.55f);testOverlay=shade;
                shade.RegisterCallback<PointerUpEvent>(e=>{AdvanceDialogue();e.StopPropagation();});
                float width=Mathf.Min(W-128,1480),left=(W-width)/2;
                var line=missionDialogue.lines[dialogueIndex];
                var measure=new Label(line.text);measure.style.unityFontDefinition=FontDefinition.FromSDFFont(nativeBattleFont);measure.style.fontSize=30;measure.style.whiteSpace=WhiteSpace.Normal;shade.Add(measure);
                float textHeight=measure.MeasureTextSize(line.text,width-72,VisualElement.MeasureMode.Exactly,0,VisualElement.MeasureMode.Undefined).y;measure.RemoveFromHierarchy();
                float height=Mathf.Clamp(textHeight+110,285,430),top=H-48-height;
                var nameBacking=HudBox(shade,left,top-70,width,70);nameBacking.style.backgroundColor=new Color(.035f,.04f,.035f,.74f);
                var panel=HudBox(shade,left,top,width,height);panel.style.backgroundColor=new Color(.035f,.04f,.035f,.94f);
                float portraitWidth=310;var texture=dialoguePortraits[dialogueIndex];
                if(texture!=null){portraitWidth=300*texture.width/(texture.height*.58f);var image=NativeOverlayImage(shade);image.image=texture;image.uv=new Rect(0,.42f,1,.58f);Place(image,left+20,top-300,portraitWidth,300);}
                HudLabel(shade,line.speaker??"",left+portraitWidth+42,top-57,width-portraitWidth-78,45,34).style.unityFontStyleAndWeight=FontStyle.Bold;
                nativeDialogueText=HudLabel(panel,"",36,24,width-72,height-90,30);nativeDialogueText.style.color=new Color(.88f,.88f,.84f);
                nativeDialogueHint=HudLabel(panel,"",36,height-47,width-72,32,23);nativeDialogueHint.style.color=muted;
            }
            nativeDialogueText.text=dialogueText;
            nativeDialogueHint.text=storySalvoActive?"Миномётный залп…":dialogueVisible<dialogueCharacters.Length?"Коснитесь экрана — показать реплику целиком":dialogueIndex+1==missionDialogue.lines.Length?"Коснитесь экрана — начать бой":"Коснитесь экрана — следующая реплика";
        }
        void NativeResults()
        {
            if(IsRaid){NativeRaidSupport();return;}
            if(newlyUnlocked.Length>0){
                float unlockWidth=Mathf.Min(1100,W-60);
                var unlocked=NativePanel("unlocks","ОТКРЫТО СНАРЯЖЕНИЕ",unlockWidth,(W-unlockWidth)/2,40,H-80,true);
                NativeParagraph(unlocked,"Теперь доступно в подготовке отряда.");
                foreach(var item in newlyUnlocked){NativeParagraph(unlocked,item.Name,24,true,ink);NativeParagraph(unlocked,item.Detail,20);}
                NativeAction(unlocked,"К статистике →",()=>{DismissUnlocks();NativeResults();},65,true);return;
            }
            string title=battle.Outcome==Outcome.GreenCaptured?(battle.VictoryByElimination?"ПРОТИВНИК РАЗГРОМЛЕН":"РУБЕЖ ЗАНЯТ"):"ОПЕРАЦИЯ ЗАВЕРШЕНА";
            float width=Mathf.Min(1600,W-60);
            var body=NativePanel("results",title,width,(W-width)/2,30,H-60,true);
            NativeParagraph(body,"Время "+((int)battle.Time/60).ToString("00")+":"+((int)battle.Time%60).ToString("00")+" · В строю: Кедр "+battle.Alive(Side.Green)+" · Гранит "+battle.Alive(Side.Red));
            if(testBattle)NativeParagraph(body,"Тестовый бой · награды отключены",23,false,gold);
            var assault=battle.Green.Breakthrough;
            if(assault.Starts>0)NativeParagraph(body,"Огневой прорыв: запусков "+assault.Starts+" · завершено "+assault.Completed+" · отменено "+assault.Aborted+" · выстрелов в залпе "+assault.Shots,20);
            foreach(var side in new[]{Side.Green,Side.Red}){
                NativeParagraph(body,side==Side.Green?"Отделение «Кедр»":"Противник",24,true,ink);
                NativeResultRow(body,new[]{"Боец","Урон HP","Урон морали","Подавление","Спасено","Помощь","Эвакуации"});
                foreach(var unit in battle.Soldiers)if(unit.Deployed&&unit.Side==side)NativeResultRow(body,new[]{unit.Name+(!unit.Alive?" · погиб":unit.Incapacitated?" · ранен":""),Mathf.RoundToInt(unit.DamageDealt).ToString(),Mathf.RoundToInt(unit.MoraleDamage).ToString(),Mathf.RoundToInt(unit.SuppressionDealt).ToString(),unit.SavedComrades.Count.ToString(),unit.HelpActions.ToString(),unit.Evacuated.ToString()});
            }
            body.style.bottom=110;
            var footer=body.parent;float buttonWidth=WatchingInteresting?Mathf.Min(430,(width-100)/2):Mathf.Min(480,width-100);
            if(WatchingInteresting){
                NativeParagraph(body,interestingSession.ResultStatus,23,false,gold);
                HudButton(footer,"Смотреть заново",()=>StartInteresting(interestingSession.Entry),35,H-145,buttonWidth,65);
                HudButton(footer,"К интересным боям",()=>{HideUnityTestHud();screen=7;CloseDrawer();},width-35-buttonWidth,H-145,buttonWidth,65);
            }else{
                var exit=HudButton(footer,testBattle?"Выйти из теста":"В полевой лагерь",()=>{if(testBattle){ExitUnityTest();return;}campBattle=null;battle=null;screen=0;CloseDrawer();ShowCamp();},width-35-buttonWidth,H-145,buttonWidth,65);
                exit.AddToClassList("battle-primary");
                if(testBattle||DeveloperBuild.DebugAvailable)HudButton(footer,"Сохранить отчёт",ExportUnityTestReport,35,H-145,Mathf.Min(350,width-buttonWidth-110),65);
            }

        }
        void NativeResultRow(VisualElement root,string[] values)
        {
            var row=new VisualElement();row.style.flexDirection=FlexDirection.Row;row.style.minHeight=64;row.style.flexShrink=0;root.Add(row);
            for(int i=0;i<values.Length;i++){var cell=NativeParagraph(row,values[i],20);cell.style.width=Length.Percent(i==0?25:12.5f);cell.style.marginBottom=0;}
        }
        void NativeRaidSupport()
        {
            var body=NativePanel("results",raid.pending?"РЕЙД · ПОДДЕРЖКА ПОСЛЕ УРОВНЯ "+raid.wave:"РЕЙД · СЛЕДУЮЩИЙ БОЙ",Mathf.Min(1100,W-40),(W-Mathf.Min(1100,W-40))/2,25,H-50,true);
            if(raid.pending){NativeParagraph(body,"Выберите одно улучшение. Погибшие не возвращаются.");foreach(var choice in raid.choices){string key=choice;NativeAction(body,RaidChoiceName(key),()=>{ChooseRaid(key);},70,true);}}
            else NativeParagraph(body,raidChanging?"Подготовка следующей локации…":message);
            if(!raidChanging)NativeAction(body,"Завершить рейд",()=>FinishRaid("Рейд завершён игроком",false));
        }
    }
}
#endif
