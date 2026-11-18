using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Rubezh.Camp;
namespace Rubezh.UnityPort
{
    public sealed partial class NativeCampView
    {
        void HubHeader(bool isolated)
        {
            var identity = V("hub-identity"); header.Add(identity);
            if(screen!="prepare" && screen!="battlePrep") {
            var player = Button(identity, "", Profile, "hub-player");
            player.Add(Avatar(M.S.avatarHead, "hub-avatar"));
            var copy = V("hub-player-copy"); player.Add(copy);
            Label(copy, S.nickname, "hub-name");
            Label(copy, "Уровень " + M.PlayerLevel, "sub");
            }
            HubIcon(identity, "camp-icon", "Лагерь", () => Navigate("home"), "hub-camp");
            var spacer = V("grow"); header.Add(spacer);
            if (screen == "operations" || isolated)
                Button(header, screen == "battlePrep" ? "В бой →" : "Подготовка →", BattleTop, "gold");
            var resources = V("resources"); header.Add(resources);
            ResourceCounter(resources, "✦ " + S.career.freeXp, "Своб. опыт");
            ResourceCounter(resources, "▤ " + S.career.manuals, "Методички");
            Button(resources, "◈ " + Money(S.balance), Currencies, "balance");
            if(screen=="prepare" || screen=="battlePrep") Button(header, "Профиль", Profile, "profile-header-button");
        }
        Button HubIcon(VisualElement parent, string image, string text, Action click, string extra = "")
        {
            var b = Button(parent, "", click, "hub-icon " + extra);
            b.tooltip = text;
            b.Add(Art("menu101/" + image + ".png", "hub-icon-art"));
            Label(b, text, "hub-icon-caption");
            return b;
        }
        void HubBackdrop(string image = "menu101/interior.png")
        {
            var bg = Art(image, "hub-backdrop");
            bg.scaleMode = ScaleMode.ScaleAndCrop;
            main.Add(bg);
        }
        void CampHub()
        {
            var scene = V("hub-scene"); main.Add(scene);
            var bg=Art("menu101/ground.png", "hub-backdrop");
            bg.scaleMode=ScaleMode.StretchToFill;bg.pickingMode=PickingMode.Ignore;scene.Add(bg);
            var canvas = V("hub-canvas"); scene.Add(canvas);
            canvas.style.left=0;canvas.style.top=0;
            canvas.style.width=Length.Percent(100);canvas.style.height=Length.Percent(100);
            canvas.pickingMode=PickingMode.Ignore;

            CampBuilding(canvas,"headquarters","Штаб",612,527,466,333,()=>Toast("Штаб"));
            CampBuilding(canvas,"medical","Медчасть",75,110,520,319,()=>Toast("Медчасть"));
            CampBuilding(canvas,"range","Стрельбище",1080,64,575,350,()=>{}, false);
            CampBuilding(canvas,"barracks","Казармы",170,485,509,364,()=>Navigate("barracks"));
            CampBuilding(canvas,"warehouse","Склад",1015,485,626,376,()=>Navigate("warehouse"));
            CampBuilding(canvas,"shop","Магазин",626,75,450,300,()=>Navigate("store"));
            var flag=Art("menu101/flag.png","hub-prop");canvas.Add(flag);PlaceHub(flag,776,367,141,193);
            var mood=V("camp-mood-filter");mood.pickingMode=PickingMode.Ignore;canvas.Add(mood);
            foreach(var tag in canvas.Children().Where(x=>x.ClassListContains("hub-location-tag")).ToList()) tag.BringToFront();
            var foot=Row(main,"hub-footer");
            foot.style.left=lastSafe.x/scale;foot.style.right=(Screen.width-lastSafe.xMax)/scale;
            foot.style.bottom=lastSafe.y/scale;
            foot.Add(V("grow"));
            HubIcon(foot,"squad-icon","Отряд",()=>Navigate("prepare"));
            HubIcon(foot,"operations-icon","Операции",BattleTop);
        }
        void PlaceHub(VisualElement e,float x,float y,float w,float h)
        {
            e.style.position=Position.Absolute;e.style.left=Length.Percent(x/1672*100);e.style.top=Length.Percent(y/941*100);
            e.style.width=Length.Percent(w/1672*100);e.style.height=Length.Percent(h/941*100);
        }
        void CampBuilding(VisualElement parent,string art,string label,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var b=Button(parent,"",action,"hub-building");b.name="camp-"+art;b.tooltip=label;
            PlaceHub(b,x,y,w,h);b.Add(Art("menu101/"+art+".png","hub-building-art"));
            var tag=Button(parent,label,action,"hub-location-tag");
            tag.name="camp-label-"+art;
            b.SetEnabled(enabled);tag.SetEnabled(enabled);
            if(!enabled){b.tooltip=tag.tooltip=label+" — пока недоступно";}
            parent.RegisterCallback<GeometryChangedEvent>(evt=>{
                float pw=evt.newRect.width,ph=evt.newRect.height;
                if(pw<=0||ph<=0)return;
                float k=Mathf.Min(pw/1672f,ph/941f);
                float bw=w*k*.78f,bh=h*k*.78f;
                float left=(x+w*.5f)/1672f*pw-bw*.5f;
                float bottom=(y+h)/941f*ph;
                float safeLeft=lastSafe.x/scale+8;
                float safeRight=pw-(Screen.width-lastSafe.xMax)/scale-8;
                float footerHeight=vh<=520&&vw>=590?68:96;
                float maxBottom=ph-lastSafe.y/scale-footerHeight-32;
                bottom=Mathf.Min(bottom,maxBottom);
                left=Mathf.Clamp(left,safeLeft,Mathf.Max(safeLeft,safeRight-bw));
                b.style.left=left;b.style.top=bottom-bh;b.style.width=bw;b.style.height=bh;
                float tw=Mathf.Min(105,Mathf.Max(76,bw*.40f));
                tag.style.left=Mathf.Clamp(left+bw*.5f-tw*.5f,safeLeft,Mathf.Max(safeLeft,safeRight-tw));
                tag.style.top=bottom-6;tag.style.width=tw;tag.style.height=28;
            });
        }
        void BarracksHub()
        {
            HubBackdrop();var page=ListPage("КАЗАРМЫ",false);
            var row=Row(page,"hub-menu");
            HubIcon(row,"squads-tile","Отряды",()=>Navigate("squads"),"hub-tile");
            HubIcon(row,"personnel-tile","Личный состав",()=>Navigate("personnel"),"hub-tile");
            HubIcon(row,"recruit-tile","Вербовка",()=>StoreTab("recruit"),"hub-tile");
        }
        void SquadList()
        {
            M.CaptureSquad();HubBackdrop();var page=ListPage("ОТРЯДЫ",false);
            var sc=Scroll(page,"list-scroll");
            foreach(var squad in S.squads)
            {
                var q=squad;var block=V("squad-block panel");sc.Add(block);
                var titleRow=Row(block,"squad-heading-row");
                Button(titleRow,q.name+(q.id==S.selectedSquad?" · выбран":"")+" →",()=>OpenSquad(q.id),"squad-title");
                Button(titleRow,"✎",()=>RenameSquadDialog(q.id),"squad-rename").tooltip="Переименовать отряд";
                var cards=Row(block,"squad-cards");
                for(int i=0;i<5;i++)
                {
                    string id=q.members[i];
                    if(id==null || !S.gear.ContainsKey(id)) Button(cards,"＋\nПустой слот",()=>OpenSquad(q.id),"card person squad-card");
                    else
                    {
                        var u=M.Soldier(id);var b=Button(cards,"",()=>PersonDetails(id),"card person squad-card");
                        b.AddToClassList("portrait-card");CardHeading(b,u);
                        Gear kit=q.id==S.selectedSquad?S.gear[id]:q.loadouts.ContainsKey(id)?q.loadouts[id]:new Gear();
                        var p=Portrait(u,null,kit);p.AddToClassList("card-portrait");b.Add(p);
                        p.RegisterCallback<GeometryChangedEvent>(e=>{if(e.newRect.width>0)p.style.height=e.newRect.width;});
                    }
                }
            }
            var foot=Row(page,"list-footer");
            Button(foot,"＋ Сформировать новый отряд",()=>Change(()=>M.CreateSquad(),()=>{selected="kedr";Navigate("prepare");}),"gold");
        }
        void OpenSquad(string id)
        {
            Change(()=>M.SelectSquad(id),()=>{selected=S.active.FirstOrDefault(x=>x!=null)??"kedr";Navigate("prepare");});
        }
        void SquadSlotPicker(int slot)
        {
            Modal(slot==0?"Выберите командира":"Выберите бойца",(c,f)=>{
                Label(c,"Назначение в другой отряд не ограничивает выбор.","sub");
                var grid=Grid(c);
                foreach(var unit in M.Roster().Where(u=>(slot==0?u.id=="kedr":u.role!="Командир")&&(!S.active.Contains(u.id)||S.active[slot]==u.id)))
                {
                    var u=unit;PersonCard(grid,u,()=>Change(()=>M.AssignSquadSlot(slot,u.id),()=>{selected=u.id;Close();Render();}));
                }
            });
        }
        void RenameSquadDialog(string id)
        {
            var squad=S.squads.First(q=>q.id==id);
            Modal("Название отряда",(c,f)=>{
                var input=new TextField {value=squad.name,maxLength=30};
                input.AddToClassList("squad-name");c.Add(input);
                Button(f,"Отмена",Close);
                Button(f,"Сохранить",()=>Change(()=>M.RenameSquad(id,input.value),()=>{Close();Render();}),"gold");
            });
        }
    }
}
