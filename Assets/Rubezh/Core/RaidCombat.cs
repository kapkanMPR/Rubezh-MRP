using System;
using System.Linq;
using System.Collections.Generic;
namespace SilentOrders.Core
{
    [Serializable] public sealed class RaidFighter
    {
        public bool alive, bleeding;
        public float health;
        public int ammo, reserve;
        public Dictionary<string,int> supplies=new Dictionary<string,int>();
        public int variant,rarity,level;
        public bool western, hasBack, recovered, dropped;
        public int gunRole, backRole, backVariant, backAmmo, backReserve, backRarity, backLevel;
    }
    public static class RaidCombat
    {
        public static readonly string[] SupplyFields={"Tourniquets","Medkits","Rockets","ReserveLauncherKind","LauncherKind","SmokeCharges","GrenadeType","GrenadeCharges","OtherGrenadeType","OtherGrenadeCharges","ThirdGrenadeType","ThirdGrenadeCharges","PocketMask","LoadedUnderbarrel","CampReserveType","CampReserveRounds","PackRockets","PackRshg","PackBoxes","PackMagazines","PackM4Magazines","PackM249Boxes","PackM24Magazines","PackSmokes","PackGrenades","PackUnderbarrel","PackF1","PackRGN","PackRGO"};
        static readonly System.Reflection.FieldInfo[] Fields=SupplyFields.Select(n=>typeof(Soldier).GetField(n)).ToArray();
        public static RaidFighter Capture(Soldier s)
        {
            var w=s.WeaponUpgrade;
            var f=new RaidFighter{alive=s.Deployed&&s.Alive,bleeding=s.Bleeding,health=s.Health,ammo=s.Ammo,reserve=s.Reserve,variant=s.WeaponVariant,western=s.WesternGun,rarity=w==null?0:w.Rarity,level=w==null?1:w.Level};
            f.gunRole=s.GunRoleOverride;f.hasBack=s.HasBackWeapon;f.recovered=s.ActiveRecoveredWeapon;f.dropped=s.WeaponDropped;f.backRole=(int)s.BackGunRole;f.backVariant=s.BackVariant;f.backAmmo=s.BackAmmo;f.backReserve=s.BackReserve;f.backRarity=s.BackWeaponUpgrade==null?0:s.BackWeaponUpgrade.Rarity;f.backLevel=s.BackWeaponUpgrade==null?1:s.BackWeaponUpgrade.Level;
            foreach(var field in Fields)f.supplies[field.Name]=(int)field.GetValue(s);
            return f;
        }
        public static void Restore(Soldier s,RaidFighter f)
        {
            s.Deployed=f.alive;s.Health=f.alive?Math.Min(100,f.health):0;s.Bleeding=f.alive&&f.bleeding;
            s.WeaponVariant=f.variant;s.WesternGun=f.western;s.GunRoleOverride=f.gunRole;
            s.HasBackWeapon=f.hasBack;s.ActiveRecoveredWeapon=f.recovered;s.WeaponDropped=f.dropped;s.BackGunRole=(Role)f.backRole;s.BackVariant=f.backVariant;s.BackAmmo=f.backAmmo;s.BackReserve=f.backReserve;s.BackMagazines=new MagazineStore(f.backReserve);
            s.BackWeaponUpgrade=f.backRarity>0?WeaponProgression.At(f.backVariant,f.backRarity,f.backLevel):null;
            s.WeaponUpgrade=f.rarity>0?WeaponProgression.At(WeaponBalance.Index(s),f.rarity,f.level):null;
            s.Ammo=Math.Max(0,Math.Min(s.MagazineSize,f.ammo));s.Magazines.Reset(s,Math.Max(0,Math.Min(s.ReserveCapacity,f.reserve)));
            foreach(var field in Fields)if(f.supplies.TryGetValue(field.Name,out int value))field.SetValue(s,value);
        }
        public static void Recover(RaidFighter f,Soldier template)
        {
            if(!f.alive)return;
            f.health=Math.Min(100,f.health+25);f.bleeding=false;
            int add=template.GunMachineGunner?50:template.MagazineSize;
            int inGun=Math.Min(add,Math.Max(0,template.MagazineSize-f.ammo));
            f.ammo+=inGun;f.reserve=Math.Min(template.ReserveCapacity,f.reserve+add-inGun);
        }
        public static int Enemies(int wave)=>Math.Min(10,1+(wave-1)*9/99+(wave%10==0?1:0));
        public static int Strength(int wave)=>Math.Min(20,1+(wave-1)*19/99+(wave%10==0?1:0));
        public static void ConfigureEnemies(BattleSimulation b,int wave)
        {
            b.RaidMode=true;b.CustomDefenders=false;b.DefenderGroups.Clear();b.CampaignReserve=0;b.CampaignLevel=Strength(wave);
            if(b.Soldiers.Count>5)b.Soldiers.RemoveRange(5,b.Soldiers.Count-5);
            var occupied=new List<V2>();
            int count=Enemies(wave);
            for(int i=0;i<count;i++)
            {
                int kit=wave<15?0:new[]{0,1,2,3,4}[i%5];
                var enemy=CampaignScenario.Enemy(b,5+i,i%CampaignEnemyEquipment.Kits[b.CampaignLevel-1].Length,kit,false);
                var entry=b.Map.EnemyEntry;
                bool found=false;
                for(int ring=0;ring<12&&!found;ring++)for(int n=0;n<16&&!found;n++)
                {
                    float angle=n*(float)Math.PI/8;
                    var at=b.Map.NearestOpen(entry+new V2((float)Math.Cos(angle)*(ring+1)*1.5f,(float)Math.Sin(angle)*(ring+1)*1.5f));
                    if(occupied.All(x=>V2.Distance(x,at)>=1.25f)){enemy.Position=enemy.Destination=enemy.OrderAnchor=at;occupied.Add(at);found=true;}
                }
                enemy.BuddyId=5+(i%2==0?Math.Min(i+1,count-1):i-1);
                enemy.AccuracyFactor*=wave%10==0?1.05f:1;
                b.Soldiers.Add(enemy);
            }
            WeaponProgression.Configure(b,Math.Min(50,1+(wave-1)*49/99+(wave%10==0?1:0)),true);
        }
    }
}
