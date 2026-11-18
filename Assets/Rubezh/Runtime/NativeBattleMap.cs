using System;
namespace Rubezh.UnityPort {
 public static class NativeBattleMap {
  public static string Address(string mode,int block,int mission) {
   bool test=mode=="test"||mode=="test-reference";
   bool story=mode=="story"||(test&&mission==20);
   int index=test?(mission==20?20:1):mode=="raid"?(mission+1)/2:story?mission:(block-1)*10+mission;
   if(index<1||index>(story?20:50))throw new ArgumentOutOfRangeException(nameof(mission));
   return "rubezh/native/map/"+(story?"story/":"war/")+index.ToString("D2");
  }
 }
}
