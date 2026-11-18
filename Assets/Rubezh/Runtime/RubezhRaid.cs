using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using SilentOrders.Core;
using Rubezh.Camp;
namespace Rubezh.UnityPort
{
    [Serializable] public sealed class RaidSession
    {
        public CampOrder order;
        public int wave, start, cleared, kills, shots, cloth, parts, copies, supply;
        public float seconds, damage, moraleDamage, suppression;
        public int help, evacuations;
        public RaidFighter[] fighters, limits;
        public string[] choices;
        public string buff;
        public int buffUntil;
        public bool pending;
        public static string CloseSaved(RaidProgress progress)
        {
            if(string.IsNullOrEmpty(progress.session))return null;
            var run=JsonConvert.DeserializeObject<RaidSession>(progress.session);
            string summary=run==null?"Прерванный рейд завершён. Добыча сохранена.":run.Summary("Рейд завершён после выхода",progress.record);
            progress.session=null;
            progress.lastSummary=summary;
            return summary;
        }
        public string Summary(string reason,int record)
        {
            return reason+"\nСтарт: "+start+" · пройдено боёв: "+cleared+" · рекорд: "+record+
                "\nВремя: "+(int)(seconds/60)+" мин "+(int)(seconds%60)+" с · выживших: "+(fighters==null?0:fighters.Count(f=>f.alive))+
                "\nУничтожено: "+kills+" · выстрелов: "+shots+" · урон: "+Mathf.RoundToInt(damage)+
                "\nУрон морали: "+Mathf.RoundToInt(moraleDamage)+" · подавление: "+Mathf.RoundToInt(suppression)+
                "\nПомощь: "+help+" · эвакуации: "+evacuations+
                "\nПолучено: очки обеспечения +"+supply+", ветошь ×"+cloth+", запчасти ×"+parts;
        }

    }
    public sealed partial class RubezhApp
    {
        RaidSession raid;
        bool raidChanging;
        bool IsRaid => campBattle!=null&&campBattle.mode=="raid"&&raid!=null;
        Texture2D raidClothIcon,raidPartsIcon;
        void SaveRaid()
        {
            string json=JsonConvert.SerializeObject(raid);
            campStore.Change(()=>campStore.Model.S.raid.session=json);
        }
        void SaveInterruptedRaid()
        {
            if(!IsRaid||battle==null||battle.Outcome!=Outcome.Running||raidChanging)return;
            var saved=JsonConvert.DeserializeObject<RaidSession>(JsonConvert.SerializeObject(raid));
            saved.fighters=battle.Soldiers.Take(5).Select(RaidCombat.Capture).ToArray();
            saved.seconds+=battle.Time;
            foreach(var u in battle.Soldiers.Take(5)){saved.kills+=u.Kills;saved.shots+=u.Shots;saved.damage+=u.DamageDealt;saved.moraleDamage+=u.MoraleDamage;saved.suppression+=u.SuppressionDealt;saved.help+=u.HelpActions;saved.evacuations+=u.Evacuated;}
            string json=JsonConvert.SerializeObject(saved);
            campStore.Change(()=>campStore.Model.S.raid.session=json);
        }
        void StartRaid(CampOrder requested)
        {
            var progress=campStore.Model.S.raid;
            if(!string.IsNullOrEmpty(progress.session))
            {
                nativeCamp.EndSavedRaid();
                return;
            }
            raid=new RaidSession{order=requested,wave=progress.Start,start=progress.Start};
            raid.order.mission=raid.wave;
            LaunchRaidWave();
        }
        void LaunchRaidWave()
        {
            raid.order.mission=raid.wave;
            #if UNITY_5_3_OR_NEWER
            SetCampBattleContext(raid.order);raidChanging=true;BeginNativeTestLoad(raid.order);return;
#else
            var sim=BuildCampSimulation(raid.order);PrepareRaidWave(sim);
            mission=raid.wave;campaignMode=false;campBusy=true;raidChanging=true;nativeCamp.Hide();
            QueueBattleLoad(sim,()=>{campBusy=false;raidChanging=false;campBattle=raid.order;CompleteBattleStart(sim);});
#endif
        }
        void PrepareRaidWave(BattleSimulation sim)
        {
            if(raid.fighters==null)
            {
                raid.fighters=sim.Soldiers.Take(5).Select(RaidCombat.Capture).ToArray();
                raid.limits=sim.Soldiers.Take(5).Select(RaidCombat.Capture).ToArray();
            }
            for(int i=0;i<5;i++)
            {
                var soldier=sim.Soldiers[i];RaidCombat.Restore(soldier,raid.fighters[i]);
                if(raid.wave<=raid.buffUntil)
                {
                    if(raid.buff=="aim")soldier.AccuracyFactor*=1.05f;
                    if(raid.buff=="reload")soldier.ReloadFactor*=.9f;
                    if(raid.buff=="steady")soldier.RaidSuppressionFactor=.85f;
                }
            }
            SaveRaid();
        }
        void AccumulateRaid()
        {
            raid.seconds+=battle.Time;
            foreach(var s in battle.Soldiers.Take(5))
            {raid.kills+=s.Kills;raid.shots+=s.Shots;raid.damage+=s.DamageDealt;raid.moraleDamage+=s.MoraleDamage;raid.suppression+=s.SuppressionDealt;raid.help+=s.HelpActions;raid.evacuations+=s.Evacuated;}
            raid.fighters=battle.Soldiers.Take(5).Select(RaidCombat.Capture).ToArray();
        }
        void CompleteRaidWave()
        {
            AccumulateRaid();
            if(battle.Outcome!=Outcome.GreenCaptured){FinishRaid("Отряд не смог продолжить рейд",false);return;}
            raid.cleared++;
            var p=campStore.Model.S.raid;
            if(raid.wave>p.rewarded){raid.supply+=RaidProgress.Supply(raid.wave);raid.cloth+=RaidProgress.Cloth(raid.wave);raid.parts+=RaidProgress.Parts(raid.wave);}
            for(int i=0;i<5;i++)RaidCombat.Recover(raid.fighters[i],battle.Soldiers[i]);
            raid.pending=raid.wave%5==0&&raid.wave<100;
            if(raid.pending)raid.choices=RaidChoices();
            int finished=raid.wave;
            if(!raid.pending&&raid.wave<100)raid.wave++;
            string json=JsonConvert.SerializeObject(raid);
            campStore.Change(()=>{campStore.Model.AwardRaidWave(finished);campStore.Model.S.raid.session=json;});
            if(finished==100){FinishRaid("Рейд пройден",false);return;}
            if(!raid.pending){raidChanging=true;StartCoroutine(RaidNextFrame());}
        }
        System.Collections.IEnumerator RaidNextFrame()
        {
            yield return null;
            try{LaunchRaidWave();}
            catch(Exception e){raidChanging=false;message="Рейд сохранён: "+e.Message;Debug.LogException(e);}
        }
        static string RaidChoiceName(string key)
        {
            switch(key){case "heal":return "Перевязка · +50 здоровья";case "ammo":return "Полный боекомплект";case "bag":return "Магазины и короба в рюкзаках";case "grenades":return "Восстановить гранаты";case "smoke":return "Восстановить дым";case "medical":return "Пополнить медицину";case "launchers":return "Пополнить гранатомёты";case "aim":return "Точность +5% · 5 боёв";case "reload":return "Перезарядка −10% · 5 боёв";default:return "Подавление −15% · 5 боёв";}
        }
        static bool SupplyIn(string group,string field)
        {
            switch(group){case "bag":return new[]{"PackMagazines","PackBoxes","PackM4Magazines","PackM249Boxes","PackM24Magazines"}.Contains(field);
            case "grenades":return new[]{"GrenadeCharges","OtherGrenadeCharges","ThirdGrenadeCharges","LoadedUnderbarrel","CampReserveRounds","PackGrenades","PackUnderbarrel","PackF1","PackRGN","PackRGO"}.Contains(field);
            case "smoke":return field=="SmokeCharges"||field=="PackSmokes";
            case "medical":return field=="Tourniquets"||field=="Medkits";
            case "launchers":return field=="Rockets"||field=="PackRockets"||field=="PackRshg"||field=="ReserveLauncherKind";
            default:return false;}
        }
        string[] RaidChoices()
        {
            var useful=new List<string>();
            foreach(string key in new[]{"heal","ammo","bag","grenades","smoke","medical","launchers"})
            {
                bool needed=false;
                for(int i=0;i<5;i++)
                {
                    var f=raid.fighters[i];var max=raid.limits[i];if(!f.alive)continue;
                    if(key=="heal")needed|=f.health<100;
                    else if(key=="ammo")needed|=f.ammo<battle.Soldiers[i].MagazineSize||f.reserve<battle.Soldiers[i].ReserveCapacity;
                    else foreach(var pair in max.supplies)if(SupplyIn(key,pair.Key)&&f.supplies.ContainsKey(pair.Key))
                        needed|=pair.Key=="ReserveLauncherKind"?pair.Value>=0&&f.supplies[pair.Key]<0:f.supplies[pair.Key]<pair.Value;
                }
                if(needed)useful.Add(key);
            }
            var random=new System.Random(unchecked(Environment.TickCount+raid.wave*397));
            var selected=useful.OrderBy(x=>random.Next()).Take(2).ToList();
            foreach(var key in new[]{"aim","reload","steady"}.OrderBy(x=>random.Next()))if(selected.Count<3)selected.Add(key);
            return selected.ToArray();
        }
        void ChooseRaid(string key)
        {
            if(!raid.pending||!raid.choices.Contains(key))return;
            for(int i=0;i<5;i++)
            {
                var f=raid.fighters[i];if(!f.alive)continue;
                if(key=="heal")f.health=Math.Min(100,f.health+50);
                else if(key=="ammo"){f.ammo=battle.Soldiers[i].MagazineSize;f.reserve=battle.Soldiers[i].ReserveCapacity;}
                else foreach(var pair in raid.limits[i].supplies)if(SupplyIn(key,pair.Key))f.supplies[pair.Key]=pair.Value;
                if(key=="grenades")foreach(string field in new[]{"GrenadeType","OtherGrenadeType","ThirdGrenadeType","CampReserveType","PocketMask"})f.supplies[field]=raid.limits[i].supplies[field];
                if(key=="launchers")f.supplies["LauncherKind"]=raid.limits[i].supplies["LauncherKind"];
            }
            if(key=="aim"||key=="reload"||key=="steady"){raid.buff=key;raid.buffUntil=raid.wave+5;}
            raid.pending=false;raid.wave++;SaveRaid();raidChanging=true;StartCoroutine(RaidNextFrame());
        }
        void FinishRaid(string reason,bool includeCurrent)
        {
            if(raid==null)return;
            if(includeCurrent&&battle!=null&&!awarded)AccumulateRaid();
            string summary=raid.Summary(reason,campStore.Model.S.raid.record);
            campStore.Change(()=>{campStore.Model.S.raid.session=null;campStore.Model.S.raid.lastSummary=summary;});
            raid=null;campBattle=null;battle=null;raidChanging=false;campBusy=false;CloseDrawer();screen=0;ShowCamp();nativeCamp.RaidSummary(summary);
        }
    }
}
