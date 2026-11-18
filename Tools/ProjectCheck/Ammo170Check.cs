using System;
using SilentOrders.Core;
class Ammo170Check {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static Soldier Receiver(int weapon,bool western=true){return new Soldier{Side=Side.Green,WeaponVariant=weapon,WesternGun=western};}
 static void Main(){
  var donor=new Soldier{Side=Side.Green,SupplyPack=true,PackM4Magazines=2,PackM24Magazines=1,PackM249Boxes=1};
  var m4=Receiver(0);var gp=Receiver(3);var m24=Receiver(5);var m249=Receiver(1);
  Check(AmmoLogistics.PackCount(donor,m4,3)==2,"M4 stock");
  Check(AmmoLogistics.PackCount(donor,gp,3)==2,"M4 underbarrel compatibility");
  Check(AmmoLogistics.PackCount(donor,m24,3)==1,"M24 stock");
  Check(AmmoLogistics.PackCount(donor,m249,2)==1,"M249 stock");
  Check(!AmmoLogistics.PackCompatible(donor,Receiver(0,false),3),"M4 cannot feed AK");
  Check(!AmmoLogistics.PackCompatible(donor,Receiver(4,false),3),"M24 cannot feed SVD");
  Check(!AmmoLogistics.PackCompatible(donor,Receiver(1,false),2),"M249 cannot feed PKM");
  Check(!AmmoLogistics.PackCompatible(donor,m249,3),"magazine is not belt");
  Check(!AmmoLogistics.PackCompatible(donor,m4,2),"belt is not magazine");
  Check(AmmoLogistics.TakePackAmmo(donor,m24,3)&&donor.PackM24Magazines==0,"consume M24");
  Check(!AmmoLogistics.TakePackAmmo(donor,m24,3),"cannot double spend M24");
  Check(AmmoLogistics.TakePackAmmo(donor,m4,3)&&AmmoLogistics.TakePackAmmo(donor,gp,3),"M4 and GP consume same stock");
  Check(AmmoLogistics.TakePackAmmo(donor,m249,2)&&AmmoLogistics.TypedStock(donor)==0,"consume M249");
  Check(WeaponBalance.For(m24).Magazine==5,"shop M24 actual magazine capacity");
  donor.PackMagazines=1;Check(AmmoLogistics.PackCompatible(donor,Receiver(0,false),3),"legacy AK stock preserved");
  Console.WriteLine("PASS typed ammunition compatibility and consumption; no cross-family issue");
 }
}
