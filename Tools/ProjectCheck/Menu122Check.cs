using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections;using System.Collections.Generic;using Newtonsoft.Json;using Rubezh.Camp;using Rubezh.UnityPort;using UnityEngine;using UnityEngine.UIElements;
class Menu122Check {
 static int checks;static readonly BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static HashSet<string> requested=new HashSet<string>();
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static object Call(object x,string name,params object[] args){try{return x.GetType().GetMethod(name,flags).Invoke(x,args);}catch(TargetInvocationException e){throw new Exception(name+": "+e.InnerException,e.InnerException);}}
 static T Get<T>(object x,string name)=>(T)x.GetType().GetField(name,flags).GetValue(x);
 static IEnumerable<VisualElement> Walk(VisualElement v){yield return v;foreach(var c in v.Children())foreach(var n in Walk(c))yield return n;}
 static UnityEngine.Object Load(string p){requested.Add(p);string path="Assets/Rubezh/Resources/"+p;
 if(p=="FieldCamp/CampTheme")return new ThemeStyleSheet();if(p=="FieldCamp/Camp")return new StyleSheet();if(File.Exists(path+".ttf"))return new Font();
 if(File.Exists(path+".png")){byte[] h=File.ReadAllBytes(path+".png");int w=(h[16]<<24)|(h[17]<<16)|(h[18]<<8)|h[19],height=(h[20]<<24)|(h[21]<<16)|(h[22]<<8)|h[23];var t=new Texture2D(1,1,TextureFormat.RGBA32,false);t.width=w;t.height=height;return t;}
 if(File.Exists(path+".json"))return new TextAsset{text=File.ReadAllText(path+".json")};if(File.Exists(path+".wav"))return AudioClip.Create(p,100,1,22050,false);
 throw new Exception("Unexpected/missing resource: "+p);
 }
 static void Inspect(NativeCampView v,string name){Call(v,"TrimTextures");var images=new HashSet<Texture>(Walk(v.Root).OfType<Image>().Where(i=>i.image!=null).Select(i=>i.image));foreach(var e in Walk(v.Root))if(e.style.backgroundImage.value.texture!=null)images.Add(e.style.backgroundImage.value.texture);var cache=Get<ResidencyCache<string,Texture2D>>(v,"textures");Check(cache.Count==images.Count,"only attached UI textures "+name+" "+cache.Count+"/"+images.Count);Check(Walk(v.Root).Count(e=>e.name=="camp-main")==1,"single main panel "+name);Check(Walk(v.Root).Count(e=>e.ClassListContains("modal-overlay"))<=1,"single modal "+name);Console.WriteLine("MENU "+name+" elements="+Walk(v.Root).Count()+" textures="+cache.Count+" rgba_bytes="+cache.Bytes);}
 static void Main(){Resources.Loader=Load;Screen.safeArea=new Rect(0,0,1280,720);Screen.width=1280;Screen.height=720;
 var catalog=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText("Assets/Rubezh/Resources/FieldCamp/catalog.json"),CampModel.JsonSettings);var m=new CampModel(catalog,rng:()=>.37);m.S.story=20;m.S.balance=100000;m.S.career.xp=4000;foreach(var i in catalog.items)if(!CampModel.Personal(i.id))m.S.inventory[i.id]=10;
 string dir=Path.Combine(Path.GetTempPath(),"rubezh122-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var store=new CampStore(Path.Combine(dir,"save.json"),m);var view=new NativeCampView();view.Initialize(store,true);view.GetOptions=()=>new NativeCampOptions{quality=2,sound=true,musicOn=true};


 view.SetOption=(key,value)=>{if(key=="uiScale")PlayerPrefs.SetInt("rubezh.uiScale",Math.Max(50,Math.Min(300,value)));};
 view.GetOptions=()=>new NativeCampOptions{quality=2,uiScale=PlayerPrefs.GetInt("rubezh.uiScale",100)};
 Call(view,"Settings");
 var zoom=Walk(view.Root).OfType<SliderInt>().Single(x=>x.name=="ui-scale-slider");zoom.value=300;
 Walk(view.Root).OfType<Button>().Single(x=>x.text=="Применить").Click();
 Check(Get<float>(view,"scale")==3f,"300 percent panel scale");
 Check(Math.Abs(Get<float>(view,"vw")-1280f/3f)<.01f,"scaled viewport stays inside safe area");
 Check(Walk(view.Root).OfType<SliderInt>().Single(x=>x.name=="ui-scale-slider").value==300,"settings reflect saved scale");
 Walk(view.Root).OfType<Button>().Single(x=>x.text=="Сбросить на 100%").Click();
 Check(Get<float>(view,"scale")==1f && PlayerPrefs.GetInt("rubezh.uiScale")==100,"reset scale");
 zoom=Walk(view.Root).OfType<SliderInt>().Single(x=>x.name=="ui-scale-slider");zoom.value=50;
 Walk(view.Root).OfType<Button>().Single(x=>x.text=="Применить").Click();
 Check(Get<float>(view,"scale")==.5f && PlayerPrefs.GetInt("rubezh.uiScale")==50,"50 percent scale persisted");
 Check(Math.Abs(Get<float>(view,"vw")-2560f)<.01f,"50 percent viewport");
 Walk(view.Root).OfType<Button>().Single(x=>x.text=="Сбросить на 100%").Click();
 Call(view,"Close");
 view.Navigate("home");
 foreach(string name in new[]{"camp-range","camp-label-range"}){var button=Walk(view.Root).OfType<Button>().Single(b=>b.name==name);Check(!button.enabledInHierarchy,"range building disabled");button.Click();Check(Get<string>(view,"screen")=="home","range cannot navigate");}
 foreach(string page in new[]{"prepare","battlePrep"}){view.Navigate(page);Call(view,"Back");Check(Get<string>(view,"screen")=="home","editor Back goes home");view.Navigate(page);Walk(view.Root).OfType<Button>().Single(b=>b.text=="‹ В лагерь").Click();Check(Get<string>(view,"screen")=="home","editor footer goes home");}
 view.Navigate("training");Check(Walk(view.Root).Any(e=>e.ClassListContains("skill-tree")),"training remains available");view.Navigate("home");

 var testSkill=m.Data.skills.First(k=>string.IsNullOrEmpty(k.role));m.Person("kedr").skills[testSkill.id]=1;view.Navigate("training");
 var learned=Walk(view.Root).Single(e=>e.name=="skill-"+testSkill.id);Check(learned.ClassListContains("taken"),"learned node state");var learnedIcon=Walk(learned).OfType<Image>().Single();Check(learnedIcon.tintColor.r>.99f && learnedIcon.tintColor.g>.8f && learnedIcon.tintColor.b<.6f,"learned icon gold");m.Person("kedr").skills.Remove(testSkill.id);view.Navigate("home");

 Call(view,"StoreTab","shop");
 foreach(string group in new[]{"equipment","weapons","consumables"}){Walk(view.Root).OfType<Button>().First(b=>b.name=="shop-group-"+group).Click();Inspect(view,"shop "+group);var shown=Walk(view.Root).OfType<Button>().Where(b=>b.name!=null&&b.name.StartsWith("item-")).Select(b=>catalog.Item(b.name.Substring(5))).ToArray();Check(shown.Length>0,"group populated");foreach(var item in shown){var expected=item.type=="weapon"||item.id=="rpg"||item.id=="rshg"?"weapons":item.type=="medical"||item.type=="grenade"||item.type=="ammo"?"consumables":"equipment";Check(expected==group,"group membership");}if(group=="weapons")Check(Walk(view.Root).Count(e=>e.ClassListContains("rare-item"))==4,"four rare weapon cards");}
 // Profile model and real UI handlers (native layout is not emulated).
 Check(m.S.nickname=="Kapkan","old save default nickname");
 m.RenamePlayer("  Алекс   Дум  ");Check(m.S.nickname=="Алекс Дум","trim nickname");
 foreach(string invalid in new[]{"",new string('я',25),"<b>Ник</b>"}){bool failed=false;try{m.RenamePlayer(invalid);}catch(InvalidOperationException){failed=true;}Check(failed,"reject invalid nickname");}
 bool locked=false;try{m.SelectAvatar(999);}catch(InvalidOperationException){locked=true;}Check(locked,"locked avatar rejected");
 var avatars=m.AvailableAvatars();Check(avatars.Distinct().Count()==avatars.Length,"unique unlocked heads");
 view.Navigate("home");
 Walk(view.Root).OfType<Button>().Single(b=>b.name=="camp-shop").Click();
 Check(Get<string>(view,"screen")=="store"&&Get<string>(view,"storeTab")=="shop","camp shop opens store");
 view.Navigate("home");Walk(view.Root).OfType<Button>().Single(b=>b.name=="camp-label-shop").Click();
 Check(Get<string>(view,"storeTab")=="shop","camp shop caption opens store");
 // Empty new squad must survive redraws and every subsequent action.
 string originalSquad=m.S.selectedSquad;
 view.Navigate("squads");Walk(view.Root).OfType<Button>().Single(b=>b.text=="＋ Сформировать новый отряд").Click();
 Check(m.S.active.All(id=>id==null),"new squad empty");
 Call(view,"Render");Call(view,"Render");Inspect(view,"empty squad redraw");
 Call(view,"RenameSquadDialog",m.S.selectedSquad);Walk(view.Root).OfType<TextField>().Single().value="Новый отряд";
 Walk(view.Root).OfType<Button>().Single(b=>b.text=="Сохранить").Click();Check(m.CurrentSquad.name=="Новый отряд","empty squad rename");
 view.Navigate("home");view.Navigate("prepare");Call(view,"Render");
 for(int slot=0;slot<5;slot++){
 Call(view,"SquadSlotPicker",slot);
 var candidate=Walk(view.Root).OfType<Button>().First(b=>b.name!=null&&b.name.StartsWith("person-"));candidate.Click();
 Check(m.S.active[slot]!=null,"assign empty squad slot "+slot);Call(view,"Render");Inspect(view,"assigned squad slot "+slot);
 }
 Check(m.SquadReady,"new squad ready");
 string secondSquad=m.S.selectedSquad;
 m.S.formation.width=2;m.S.formation.depth=0;m.S.formation.positions[1]=1;
 m.SelectSquad(originalSquad);m.S.formation.width=0;m.S.formation.depth=2;m.S.formation.positions[1]=-1;
 m.SelectSquad(secondSquad);Check(m.S.formation.width==2&&m.S.formation.depth==0&&m.S.formation.positions[1]==1,"independent squad formation");
 var formationRestored=new CampModel(catalog,CampModel.Decode(m.Serialize()),()=>.37);
 formationRestored.SelectSquad(originalSquad);Check(formationRestored.S.formation.width==0&&formationRestored.S.formation.positions[1]==-1,"formation survives save reload");
 formationRestored.SelectSquad(secondSquad);Check(formationRestored.S.formation.width==2&&formationRestored.S.formation.positions[1]==1,"second formation survives save reload");
 view.Navigate("prepare");CampOrder clicked=null;view.StartOperation=q=>clicked=q;
 Walk(view.Root).OfType<Button>().Single(b=>b.name=="action-squad-formation").Click();
 Check(clicked!=null&&clicked.op=="edit-formation"&&clicked.formation.width==2,"icon opens current squad formation");
 view.Navigate("battlePrep");clicked=null;
 Walk(view.Root).OfType<Button>().Single(b=>b.text=="В бой →").Click();
 Check(clicked!=null&&clicked.op=="start"&&clicked.formation.width==2,"preparation launches saved formation directly");
 
 Call(view,"OpenSquad",originalSquad);Check(m.S.selectedSquad==originalSquad,"return original squad");
 // Inline store interactions: purchases, sorting, sale fallback and resources.
 Call(view,"StoreTab","shop");
 Walk(view.Root).OfType<Button>().First(b=>b.name=="shop-group-weapons").Click();
 Walk(view.Root).OfType<Button>().First(b=>b.name=="item-m4").Click();
 Check(!Walk(view.Root).Any(e=>e.ClassListContains("modal-overlay")),"store selection stays inline");
 int before=m.Owned("m4");double money=m.S.balance;
 Walk(view.Root).OfType<Button>().First(b=>b.text.StartsWith("Купить ·")).Click();
 Check(m.Owned("m4")==before+1&&m.S.balance==money-catalog.Item("m4").price,"inline buy updates inventory and balance");
 Call(view,"StoreTab","warehouse");
 var sorted=Walk(view.Root).OfType<Button>().Where(b=>b.name!=null&&b.name.StartsWith("item-")&&!b.name.StartsWith("item-@")).Select(b=>catalog.Item(b.name.Substring(5))).ToArray();
 Check(sorted.Select(i=>i.id).SequenceEqual(sorted.OrderByDescending(i=>i.rarity=="Редкое"?1:0).ThenByDescending(i=>i.price).ThenByDescending(i=>i.capacity).ThenBy(i=>i.name).Select(i=>i.id)),"warehouse rare then price order");
 Walk(view.Root).OfType<Button>().First(b=>b.name=="item-m4").Click();
 Walk(view.Root).OfType<Button>().First(b=>b.text.StartsWith("Продать ·")).Click();Check(m.Owned("m4")==before,"inline sell");
 Check(!Walk(view.Root).OfType<Button>().Any(b=>b.text=="В магазин"||b.text=="На склад →"),"no store crossover links");
 m.S.career.manuals=3;Call(view,"Render");
 Walk(view.Root).OfType<Button>().First(b=>b.name=="warehouse-group-resources").Click();
 Check(Walk(view.Root).Any(b=>b.name=="item-@manuals"),"owned resource visible");
 Walk(view.Root).OfType<Button>().First(b=>b.name=="item-@manuals").Click();
 Check(!Walk(view.Root).Any(e=>e.ClassListContains("modal-overlay")),"resource stays inline");
 view.Navigate("prepare");Check(!Walk(view.Root).OfType<Button>().Any(b=>b.text=="Открыть магазин"),"no shop in editor");
 m.SelectAvatar(avatars.Last());store.Save();var restored=new CampModel(catalog,CampModel.Decode(File.ReadAllText(store.Path)));Check(restored.S.nickname==m.S.nickname && restored.S.avatarHead==m.S.avatarHead,"profile survives save reload");
 var roster=m.Roster().ToArray();m.Person(roster[0].id).level=5;m.Person(roster[1].id).level=7;m.Person(roster[2].id).level=7;m.Person(roster[1].id).credit=1;m.Person(roster[2].id).credit=2;
 Check(m.ProfileFighters()[0].id==roster[2].id && m.ProfileFighters()[1].id==roster[1].id,"top five level and experience ordering");
 var legacy=Newtonsoft.Json.Linq.JObject.Parse(m.Serialize());legacy.Remove("nickname");legacy.Remove("avatarHead");legacy.Remove("avatarHeads");var migrated=new CampModel(catalog,CampModel.Decode(legacy.ToString()));Check(migrated.S.nickname=="Kapkan" && migrated.AvailableAvatars().Contains(migrated.S.avatarHead),"legacy profile migration");
 var retired=migrated.Roster().First(u=>u.id!="kedr");int retiredHead=migrated.S.looks[retired.id].headId;Call(migrated,"Remove",retired.id);Check(migrated.AvailableAvatars().Contains(retiredHead),"dismissed head stays unlocked");
 foreach(var size in new[]{new[]{1280,720},new[]{800,360},new[]{393,852}}){Screen.width=size[0];Screen.height=size[1];Screen.safeArea=new Rect(0,0,size[0],size[1]);Call(view,"UpdateViewport");view.Navigate("home");
 Check(!Walk(view.Root).OfType<Button>().Any(b=>b.text=="⚙"),"settings removed from header");
 Walk(view.Root).OfType<Button>().First(b=>b.ClassListContains("hub-player")).Click();Inspect(view,"profile");
 Check(Walk(view.Root).Count(e=>e.ClassListContains("profile-fighter"))==5,"five fighter cards");
 foreach(var caption in new[]{"Поддержка","Код","Выйти из аккаунта"})Check(!Walk(view.Root).OfType<Button>().First(b=>b.text==caption).enabledInHierarchy,"disabled "+caption);
 Call(view,"AvatarPicker");Inspect(view,"avatar picker");var cells=Walk(view.Root).OfType<Button>().Where(b=>b.ClassListContains("profile-avatar-cell")).ToArray();Check(cells.Length==avatars.Length,"only earned avatar cells");cells.Last().Click();Inspect(view,"avatar selected");
 Call(view,"RenameDialog");Walk(view.Root).OfType<TextField>().Single().value="Капкан";Walk(view.Root).OfType<Button>().First(b=>b.text=="Сохранить").Click();Inspect(view,"renamed profile");Check(m.S.nickname=="Капкан","rename save handler");
 Walk(view.Root).OfType<Button>().First(b=>b.text=="Настройки").Click();Inspect(view,"profile settings");Walk(view.Root).OfType<Button>().First(b=>b.text=="‹ Профиль").Click();Inspect(view,"settings return");Call(view,"Close");Inspect(view,"profile closed");}
 foreach(var size in new[]{new[]{1280,720},new[]{800,360},new[]{393,852}}){Screen.width=size[0];Screen.height=size[1];Screen.safeArea=new Rect(0,0,size[0],size[1]);Call(view,"UpdateViewport");
 for(int round=0;round<3;round++)foreach(string page in new[]{"home","store","warehouse","barracks","personnel","squads","prepare","operations","battlePrep","training"}){view.Navigate(page);Inspect(view,page+"/"+size[0]+"/"+round);}}
 foreach(string tab in new[]{"shop","warehouse","recruit"}){Call(view,"StoreTab",tab);Inspect(view,"store/"+tab);}
 foreach(var item in catalog.items){Call(view,"ItemDetails",item.id);Inspect(view,"item/"+item.id);Call(view,"Close");Inspect(view,"close-item");}
 foreach(var id in m.S.gear.Keys.ToArray()){Call(view,"PersonDetails",id);Inspect(view,"person/"+id);Call(view,"Close");}
 foreach(var offer in catalog.offers.Where(o=>o.id!="sup")){Call(view,"RecruitOffer",offer.id);Inspect(view,"offer/"+offer.id);Call(view,"Close");}
 foreach(string modal in new[]{"Currencies","Settings","ManualOffer","ImportDialog","Readiness","ReplacePicker"}){Call(view,modal);Inspect(view,modal);Call(view,"Close");}
 foreach(string slot in new[]{"weapon","armor","rig","extra","bag","rigSlots","tourniquet"}){Call(view,"PickEquipment",slot,0);Inspect(view,"picker/"+slot);Call(view,"Close");}
 // Real reward state and scheduled jobs, in an isolated test save.
 m.Award("war-1-10","war",1,10);m.S.motion=true;
 foreach(string phase in new[]{"gear","crew","done"}){m.S.rewardView=new RewardView{key="war-1-10",phase=phase,summary="Test"};Call(view,"DrawShipment",true);Inspect(view,"reward/"+phase);if(phase!="done")Check(Get<IList>(view,"modalAnimations").Count>0,"reward jobs scheduled");Call(view,"Close");Check(Get<IList>(view,"modalAnimations").Count==0,"closed reward jobs released");}m.S.rewardView=null;
 for(int n=1;n<=50;n++){m.S.operation=new Operation{mode="war",block=(n-1)/10+1,mission=(n-1)%10+1};view.Navigate("operations");Call(view,"Briefing");Inspect(view,"briefing-war/"+n);Call(view,"Close");}
 for(int n=1;n<=20;n++){m.S.operation=new Operation{mode="story",block=1,mission=n};Call(view,"Briefing");Inspect(view,"briefing-story/"+n);Call(view,"Close");}
 foreach(string id in m.S.gear.Keys){view.GetType().GetField("trainee",flags).SetValue(view,id);view.Navigate("training");Inspect(view,"training/"+id);}

 for(int n=0;n<5;n++){view.Hide();Check(view.Root.childCount==0&&Get<ResidencyCache<string,Texture2D>>(view,"textures").Count==0,"hide releases tree/textures");view.Show("home");Inspect(view,"reopen");}
 Check(!requested.Any(p=>p.StartsWith("SpritePacks/")||p.StartsWith("War/")||p.StartsWith("Levels/")||p.StartsWith("Audio/")),"camp does not load combat resources");
 File.WriteAllLines("Validation/Current/menu-resources.txt",requested.OrderBy(x=>x));Call(view,"OnDestroy");Directory.Delete(dir,true);Console.WriteLine("PASS menu checks="+checks+" unique_resource_requests="+requested.Count+"; API contracts only, not layout/render validation");
 }
}
