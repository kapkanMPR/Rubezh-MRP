using System;
using System.Linq;
using SilentOrders.Core;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnityEngine.TextCore.LowLevel;
#endif
namespace Rubezh.UnityPort {
 public sealed partial class RubezhApp {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
  GameObject formationViewObject;UIDocument formationDocument;PanelSettings formationPanel;FontAsset formationFont;
  VisualElement formationRoot,formationMain,formationOverlay;ScrollView formationBody,formationRail;
  Button formationSave;bool formationRebuild;float formationWidth;
  void FormPlace(VisualElement e,float x,float y,float w,float h){e.style.position=Position.Absolute;e.style.left=x;e.style.top=y;e.style.width=w;e.style.height=h;}
  VisualElement FormBox(VisualElement parent,float x,float y,float w,float h){var e=new VisualElement();e.AddToClassList("battle-panel");FormPlace(e,x,y,w,h);parent.Add(e);return e;}
  Label FormLabel(VisualElement parent,string text,float x,float y,float w,float h,int size=21,Color? color=null,bool bold=false){var e=new Label(text){pickingMode=PickingMode.Ignore};e.style.fontSize=size;e.style.unityFontStyleAndWeight=bold||size>=30?FontStyle.Bold:FontStyle.Normal;e.style.whiteSpace=WhiteSpace.Normal;e.style.color=color??ink;FormPlace(e,x,y,w,h);parent.Add(e);return e;}
  Button FormButton(VisualElement parent,string text,Action action,float x,float y,float w,float h,bool selected=false,bool primary=false){
   var e=new Button(()=>{int previousRule=behaviorRuleIndex;action();if(previousRule!=behaviorRuleIndex&&formationBody!=null)formationBody.scrollOffset=Vector2.zero;formationRebuild=true;}){text=text};e.AddToClassList("battle-button");e.style.fontSize=21;
   if(selected)e.AddToClassList("battle-chosen");if(primary)e.AddToClassList("battle-primary");FormPlace(e,x,y,w,h);parent.Add(e);return e;
  }
  ScrollView FormScroll(VisualElement parent,float x,float y,float w,float h,float contentHeight){var e=new ScrollView(ScrollViewMode.Vertical);e.horizontalScrollerVisibility=ScrollerVisibility.Hidden;FormPlace(e,x,y,w,h);e.contentContainer.style.height=contentHeight;parent.Add(e);return e;}
  void CreateNativeFormationMenu(){
   formationViewObject=new GameObject("Formation and plans · UI Toolkit");formationDocument=formationViewObject.AddComponent<UIDocument>();
   formationPanel=ScriptableObject.CreateInstance<PanelSettings>();formationPanel.scaleMode=PanelScaleMode.ConstantPixelSize;formationPanel.sortingOrder=350;formationPanel.themeStyleSheet=Resources.Load<ThemeStyleSheet>("FieldCamp/CampTheme");formationDocument.panelSettings=formationPanel;
   formationRoot=formationDocument.rootVisualElement;formationRoot.style.backgroundColor=bg;formationRoot.style.flexGrow=1;
   var font=Resources.Load<Font>("Fonts/RubezhSans");if(font==null)throw new InvalidOperationException("Нет Fonts/RubezhSans");
   formationFont=FontAsset.CreateFontAsset(font,90,9,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);formationRoot.style.unityFontDefinition=FontDefinition.FromSDFFont(formationFont);formationRoot.style.color=ink;
   var sheet=Resources.Load<StyleSheet>("NativeBattle/Hud");if(sheet!=null)formationRoot.styleSheets.Add(sheet);formationRebuild=true;
  }
  void RebuildNativeFormationMenu(){
   Vector2 bodyScroll=formationBody==null?Vector2.zero:formationBody.scrollOffset,railScroll=formationRail==null?Vector2.zero:formationRail.scrollOffset;
   formationRoot.Clear();formationMain=new VisualElement();FormPlace(formationMain,0,0,W,H);formationRoot.Add(formationMain);formationOverlay=null;
   const float left=24,rail=268;float right=312,width=W-right-24;
   FormLabel(formationMain,"РУБЕЖ / ПОСТРОЕНИЕ",left,20,390,45,30);
   FormButton(formationMain,"Базовое поведение",()=>{behaviorLibrary.planning=false;behaviorRuleIndex=-1;SaveBehaviorPlans();},W-600,18,278,58,!behaviorLibrary.planning);
   FormButton(formationMain,"Планирование",()=>{behaviorLibrary.planning=true;if(behaviorDraft==null)OpenBehaviorDraft(SelectedBehaviorPlan());SaveBehaviorPlans();},W-310,18,286,58,behaviorLibrary.planning);
   if(behaviorLibrary.planning){
    FormButton(formationMain,(behaviorDraft==null?"Выбрать схему":behaviorDraft.name)+"  ▾",()=>ChooseBehavior(behaviorLibrary.plans.Select(p=>p.id).ToArray(),behaviorLibrary.plans.Select(p=>p.name).ToArray(),id=>NavigateBehavior(()=>{behaviorLibrary.selected=id;OpenBehaviorDraft(SelectedBehaviorPlan());SaveBehaviorPlans();})),left,108,W-360,58);
    FormButton(formationMain,"+ Новая схема",()=>NavigateBehavior(()=>{OpenBehaviorDraft(new BehaviorPlan{id=Guid.NewGuid().ToString("N")});behaviorCleanJson="";}),W-322,108,298,58);
   }else FormLabel(formationMain,"Привычный ИИ отряда. Двойки и приоритеты бойцов настраиваются как раньше.",left,108,W-48,58,21,muted);
   var side=FormBox(formationMain,left,184,rail,H-300);
   FormButton(side,"ПОСТРОЕНИЕ\nДвойки · дистанция · роли",()=>{behaviorRuleIndex=-1;formationBody.scrollOffset=Vector2.zero;},12,14,rail-24,74,behaviorRuleIndex<0);
   formationRail=null;
   if(behaviorLibrary.planning&&behaviorDraft!=null){
    FormButton(side,"+ Добавить блок",AddBehaviorBlock,12,102,rail-24,54).SetEnabled(behaviorDraft.rules.Count<40);
    formationRail=FormScroll(side,8,170,rail-16,H-550,behaviorDraft.rules.Count*84);
    for(int i=0;i<behaviorDraft.rules.Count;i++){int index=i;var rule=behaviorDraft.rules[i];FormButton(formationRail,(i+1)+". "+BehaviorName(rule.when,BehaviorEvents,BehaviorEventNames)+(rule.enabled?"":" · выкл."),()=>{behaviorRuleIndex=index;formationBody.scrollOffset=Vector2.zero;},0,i*84,rail-40,74,behaviorRuleIndex==i);}
    formationRail.scrollOffset=railScroll;
    FormButton(side,"+ Ещё блок",AddBehaviorBlock,12,H-366,rail-24,54,false,true).SetEnabled(behaviorDraft.rules.Count<40);
   }else FormLabel(side,"Базовая схема\n\nПланирование можно включить вверху. Сохранённые схемы останутся на месте.",18,120,rail-36,180,21,muted);
   var content=FormBox(formationMain,right,184,width,H-300);
   if(behaviorDraft!=null&&behaviorRuleIndex>=behaviorDraft.rules.Count)behaviorRuleIndex=-1;
   float contentHeight=behaviorRuleIndex<0||!behaviorLibrary.planning?1000:520+behaviorDraft.rules[behaviorRuleIndex].actions.Count*172;
   formationBody=FormScroll(content,12,14,width-24,H-328,contentHeight+(behaviorLibrary.planning?110:0));
   if(behaviorLibrary.planning){
    var field=new TextField{value=behaviorDraft.name,maxLength=64};field.style.fontSize=24;FormPlace(field,16,4,width-350,64);formationBody.Add(field);
    field.RegisterCallback<ChangeEvent<string>>(e=>{behaviorDraft.name=e.newValue;if(formationSave!=null)formationSave.text="Сохранить схему *";});
    FormButton(formationBody,"Стиль: "+BehaviorName(behaviorDraft.style,new[]{"careful","balanced","bold"},new[]{"Осторожный","Баланс","Смелый"}),()=>ChooseBehavior(new[]{"careful","balanced","bold"},new[]{"Осторожный","Баланс","Смелый"},v=>behaviorDraft.style=v),width-320,4,256,64);
   }
   var settings=new VisualElement();FormPlace(settings,0,behaviorLibrary.planning?110:0,width-48,contentHeight);formationBody.Add(settings);
   if(behaviorRuleIndex<0||!behaviorLibrary.planning)BuildNativeFormationSettings(settings,width-48);else BuildNativeBehaviorBlock(settings,width-48);
   formationBody.scrollOffset=bodyScroll;
   FormLabel(formationMain,behaviorMessage,left,H-105,W-48,30,20,muted);
   FormButton(formationMain,"← Снаряжение",()=>NavigateBehavior(()=>{if(formationOrder!=null)BackFromCampFormation();else ShowCamp();}),left,H-67,230,54);
   formationSave=null;
   if(behaviorLibrary.planning)formationSave=FormButton(formationMain,BehaviorDirty?"Сохранить схему *":"Сохранить схему",()=>CommitBehaviorDraft(),W-604,H-67,274,54,false,true);
   bool ready=FormationReady(FormationForMenu)&&(formationOrder!=null||!campaignMode||ActiveCost<=CampaignProgression.Budget(progress.completed)&&LoadoutProblem().Length==0);
   FormButton(formationMain,editingSquadFormation?"Готово":"НАЧАТЬ БОЙ",()=>{if(CommitBehaviorDraft())LaunchFromFormation();},W-314,H-67,290,54,false,true).SetEnabled(ready);
   if(behaviorPendingNavigation!=null||behaviorChoiceIds!=null||formationPickerSlot>=0)BuildNativeFormationOverlay();
   formationRebuild=false;formationWidth=W;
  }
  void FormPortrait(VisualElement parent,int id,float x,float y,float w,float h){
   var kit=FormationKit(id);Rubezh.GreenLook look;
   if(formationOrder==null)look=LegacyLook(id,kit);else{var u=formationOrder.units[id];look=new Rubezh.GreenLook{headId=u.headId,lightId=u.lightId,familyId=u.familyId,heavyId=u.heavyId};look.Ensure(kit.Class(id),id==0);}
   var image=new Image{scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};image.image=CanonicalPortrait(look);
   if(image.image==null){Texture2D t;Rect uv;NativeUiArt.Resolve(NativeUiArt.PortraitKey(kit.weapon,false,look,false,kit.Class(id),id),out t,out uv);image.image=t;image.uv=uv;}
   FormPlace(image,x,y,w,h);parent.Add(image);
  }
  void FormSlot(VisualElement parent,FormationSettings f,int slot,float x,float y,float w){
   int id=f.DisplaySlot(slot);var card=FormButton(parent,"",()=>{f.EditSlots();formationPickerSlot=slot;SaveQuickFormation();},x,y,w,172,id>0);
   if(id>0&&FormationWounded(id)){card.SetEnabled(false);FormLabel(card,FormationName(id)+" · РАНЕН",12,35,w-24,90);return;}
   if(id>0){FormPortrait(card,id,w*.5f-36,10,72,101);FormLabel(card,FormationName(id),9,117,w-18,28,24,null,true);FormLabel(card,"Нажми для замены",6,146,w-12,23,18,muted);}
   else{FormLabel(card,"+",10,25,w-20,64,44);FormLabel(card,"ВЫБРАТЬ БОЙЦА",10,100,w-20,56,18,muted);}
  }
  void BuildNativeFormationSettings(VisualElement root,float width){
   var f=FormationForMenu;float half=(width-44)*.5f;
   FormLabel(root,"Построение отряда",16,4,width-32,48,30);
   FormLabel(root,"Прежние настройки строя. Приоритеты не отменяют укрытия и самостоятельность командира.",16,58,width-32,54,21,muted);
   FormButton(root,"АВТО · ПОДБОР ИИ",()=>{f.pairing=0;f.pairSlots=new[]{-1,-1,-1,-1};SaveQuickFormation();},16,128,half,62,f.pairing==0);
   FormButton(root,"СОБРАТЬ ДВОЙКИ",()=>{f.EditSlots();SaveQuickFormation();},28+half,128,half,62,f.pairing!=0);
   float cw=(width-80)/5;FormLabel(root,"ДВОЙКА А · СЛЕВА",16,210,cw*2+12,30,18,muted);FormLabel(root,"ДВОЙКА Б · СПРАВА",16+3*(cw+12),210,cw*2+12,30,18,muted);
   for(int i=0;i<4;i++)FormSlot(root,f,i,16+(i<2?i:i+1)*(cw+12),252,cw);
   var leader=FormBox(root,16+2*(cw+12),252,cw,172);FormPortrait(leader,0,cw*.5f-38,10,76,102);FormLabel(leader,FormationName(0),8,118,cw-16,28,21,null,true);FormLabel(leader,"КОМАНДИР",8,148,cw-16,22,18,muted);
   FormLabel(root,"ПЛОТНОСТЬ ПО ШИРИНЕ",16,440,half,36,20,gold);FormLabel(root,"РАЗРЫВ ВГЛУБЬ",28+half,440,half,36,20,gold);
   for(int i=0;i<3;i++){int n=i;float each=(half-16)/3;FormButton(root,FormationChoice[i],()=>{f.width=n;SaveQuickFormation();},16+i*(each+8),484,each,57,f.width==i);FormButton(root,FormationDepthChoice[i],()=>{f.depth=n;SaveQuickFormation();},28+half+i*(each+8),484,each,57,f.depth==i);}
   FormLabel(root,"ПОЛОЖЕНИЕ КАЖДОГО",16,560,width-32,36,20,gold);
   for(int i=1;i<=4;i++){int id=i;float y=610+(i-1)*72;FormLabel(root,FormationName(i),16,y+5,200,52,21,null,true);
    if(FormationWounded(i)){FormLabel(root,"РАНЕН",226,y,width-242,50,21,muted);continue;}
    float each=(width-262)/3;for(int k=0;k<3;k++){int choice=k-1;FormButton(root,FormationPositions[k],()=>{f.positions[id]=choice;SaveQuickFormation();},226+k*(each+6),y,each,54,f.positions[id]==choice);}
   }
   FormButton(root,f.quietWork?"РАБОТАТЬ ТИХО":"РАБОТАТЬ ГРОМКО",()=>{f.quietWork=!f.quietWork;SaveQuickFormation();},16,914,width-310,60,f.quietWork);
   FormButton(root,"Сброс построения",()=>{f.pairing=0;f.pairSlots=new[]{-1,-1,-1,-1};f.width=f.depth=1;f.positions=new int[5];f.quietWork=false;formationPickerSlot=-1;SaveQuickFormation();},width-278,914,262,60);
  }
  void BuildNativeBehaviorBlock(VisualElement root,float width){
   var r=behaviorDraft.rules[behaviorRuleIndex];FormLabel(root,"ПОВЕДЕНЧЕСКИЙ БЛОК "+(behaviorRuleIndex+1),16,4,width-32,42,20,gold);
   FormButton(root,BehaviorName(r.when,BehaviorEvents,BehaviorEventNames)+"  ▾",()=>ChooseBehavior(BehaviorEvents,BehaviorEventNames,v=>r.when=v),16,58,width-32,64,true);
   float third=(width-56)/3;
   FormButton(root,r.enabled?"Включён":"Выключен",()=>r.enabled=!r.enabled,16,138,third,56,r.enabled);
   FormButton(root,"↑ Приоритет",()=>{int i=behaviorRuleIndex;behaviorDraft.rules[i]=behaviorDraft.rules[i-1];behaviorDraft.rules[i-1]=r;behaviorRuleIndex--;},28+third,138,third,56).SetEnabled(behaviorRuleIndex>0);
   FormButton(root,"↓ Приоритет",()=>{int i=behaviorRuleIndex;behaviorDraft.rules[i]=behaviorDraft.rules[i+1];behaviorDraft.rules[i+1]=r;behaviorRuleIndex++;},40+third*2,138,third,56).SetEnabled(behaviorRuleIndex+1<behaviorDraft.rules.Count);
   FormButton(root,"−",()=>r.delay=Math.Max(0,r.delay-1),16,212,62,54);FormLabel(root,"Через "+r.delay+" сек.",96,220,250,42,24);
   FormButton(root,"+",()=>r.delay=Math.Min(60,r.delay+1),350,212,62,54);FormButton(root,r.mode=="sequence"?"По очереди":"По возможности вместе",()=>r.mode=r.mode=="sequence"?"parallel":"sequence",428,212,width-444,54);
   FormLabel(root,"Совместимые приказы выполняются вместе. Занятый исполнитель ждёт, остальные продолжают.",16,284,width-32,66,21,muted);
   float y=364;for(int i=0;i<r.actions.Count;i++){var a=r.actions[i];int index=i;var card=FormBox(root,12,y,width-24,154);
    FormButton(card,BehaviorName(a.type,BehaviorActions,BehaviorActionNames)+"  ▾",()=>ChooseBehavior(BehaviorActions,BehaviorActionNames,v=>{a.type=v;if(BehaviorPlanRunner.IsMovement(v))a.who="group";}),12,12,width-128,58);
    FormButton(card,"×",()=>r.actions.RemoveAt(index),width-104,12,66,58);
    if(BehaviorPlanRunner.IsMovement(a.type))FormLabel(card,"Исполнитель: группа",16,90,width-56,44,21,muted);
    else FormButton(card,"Кто: "+BehaviorName(a.who,BehaviorActors,BehaviorActorNames)+"  ▾",()=>ChooseBehavior(BehaviorActors,BehaviorActorNames,v=>a.who=v),12,82,width-48,58);
    y+=172;
   }
   FormButton(root,"+ Добавить действие",()=>ChooseBehavior(BehaviorActions,BehaviorActionNames,v=>r.actions.Add(new BehaviorAction{type=v})),16,y+8,width-280,60,false,true).SetEnabled(r.actions.Count<12);
   FormButton(root,"Удалить блок",()=>{behaviorDraft.rules.RemoveAt(behaviorRuleIndex);behaviorRuleIndex=-1;formationBody.scrollOffset=Vector2.zero;},width-248,y+8,232,60);
  }
  void BuildNativeFormationOverlay(){
   formationMain.SetEnabled(false);formationOverlay=new VisualElement();FormPlace(formationOverlay,0,0,W,H);formationOverlay.style.backgroundColor=new Color(0,0,0,.85f);formationRoot.Add(formationOverlay);
   float width=Mathf.Min(850,W-80),x=(W-width)*.5f;var box=FormBox(formationOverlay,x,120,width,H-240);
   if(behaviorPendingNavigation!=null){
    FormLabel(box,"В схеме есть несохранённые изменения",28,38,width-56,90,30);
    FormButton(box,"Сохранить и продолжить",()=>{if(CommitBehaviorDraft()){var next=behaviorPendingNavigation;behaviorPendingNavigation=null;next();}},28,180,width-56,68,false,true);
    FormButton(box,"Продолжить без сохранения",()=>{var next=behaviorPendingNavigation;behaviorPendingNavigation=null;behaviorDraft=null;behaviorCleanJson=null;next();},28,268,width-56,68);
    FormButton(box,"Вернуться к редактированию",()=>behaviorPendingNavigation=null,28,356,width-56,68);return;
   }
   if(formationPickerSlot>=0){
    var f=FormationForMenu;int slot=formationPickerSlot;
    FormLabel(box,"ВЫБЕРИ БОЙЦА В ДВОЙКУ",28,28,width-116,58,30);FormLabel(box,slot<2?"ДВОЙКА А · ЛЕВЫЙ ФЛАНГ":"ДВОЙКА Б · ПРАВЫЙ ФЛАНГ",28,86,width-56,45,20,gold);
    FormButton(box,"×",()=>formationPickerSlot=-1,width-88,22,60,56);
    int shown=0;for(int id=1;id<=4;id++){
     bool occupied=false;for(int k=0;k<4;k++)if(k!=slot&&f.DisplaySlot(k)==id)occupied=true;
     if(occupied||FormationWounded(id))continue;
     int unit=id;float cw=(width-96)/2;var card=FormButton(box,"",()=>{if(f.Put(slot,unit)){SaveQuickFormation();formationPickerSlot=-1;}},30+(shown%2)*(cw+36),144+(shown/2)*163,cw,147,id==f.DisplaySlot(slot));
     FormPortrait(card,id,16,13,78,112);FormLabel(card,FormationName(id),108,19,cw-125,37,24,null,true);FormLabel(card,RoleName(FormationKit(id),id),108,62,cw-125,70,18,muted);shown++;
    }
    if(shown==0)FormLabel(box,"Все бойцы уже распределены. Освободи другое место для замены.",38,264,width-70,65,21,muted);
    FormButton(box,"Освободить ячейку",()=>{f.Clear(slot);SaveQuickFormation();formationPickerSlot=-1;},35,H-335,width*.45f,63);
    FormButton(box,"Закрыть",()=>formationPickerSlot=-1,width*.53f,H-335,width*.42f,63);return;
   }
   FormLabel(box,"Выбери вариант",28,28,width-130,54,30);FormButton(box,"×",()=>{behaviorChoiceIds=null;behaviorChoiceApply=null;},width-88,22,60,56);
   var choices=FormScroll(box,20,100,width-40,H-380,behaviorChoiceIds.Length*80);
   for(int i=0;i<behaviorChoiceIds.Length;i++){int index=i;FormButton(choices,behaviorChoiceNames[i],()=>{var apply=behaviorChoiceApply;var value=behaviorChoiceIds[index];behaviorChoiceIds=null;behaviorChoiceApply=null;apply(value);},0,i*80,width-68,68);}
  }
#endif
  void UpdateNativeFormationMenu(){
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
   if(screen!=6||loading){HideNativeFormationMenu();return;}
   LoadBehaviorPlans();if(behaviorLibrary.planning&&behaviorDraft==null)OpenBehaviorDraft(SelectedBehaviorPlan());
   if(formationViewObject==null)CreateNativeFormationMenu();
   var safe=Screen.safeArea;float scale=Mathf.Min(safe.height/H,safe.width/1400f);W=safe.width/scale;formationPanel.scale=scale;
   formationRoot.style.marginLeft=safe.x/scale;formationRoot.style.marginRight=(Screen.width-safe.xMax)/scale;formationRoot.style.marginTop=(Screen.height-safe.yMax)/scale;formationRoot.style.marginBottom=safe.y/scale;
   if(formationRebuild||Math.Abs(formationWidth-W)>.1f)RebuildNativeFormationMenu();
#endif
  }
  void HideNativeFormationMenu(){
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
   if(formationViewObject==null)return;
   Destroy(formationViewObject);Destroy(formationPanel);formationViewObject=null;formationDocument=null;formationPanel=null;formationRoot=formationMain=formationOverlay=null;formationBody=formationRail=null;formationSave=null;
   if(formationFont!=null){foreach(var atlas in formationFont.atlasTextures)if(atlas!=null)Destroy(atlas);if(formationFont.material!=null)Destroy(formationFont.material);Destroy(formationFont);formationFont=null;}
#endif
  }
  void DisposeNativeFormationMenu(){HideNativeFormationMenu();}
 }
}
