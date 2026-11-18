#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using SilentOrders.Core;
namespace Rubezh.UnityPort.Editor {
 public static class CoverSearchValidation {
  static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
  [MenuItem("Рубеж/Проверить поиск укрытий")]
  public static void Run(){
   var b=new BattleSimulation();var map=b.Map;
   for(int i=0;i<map.Tiles.Length;i++){map.Tiles[i]=Tile.Ground;map.CoverHealth[i]=100;}
   map.Entry=new V2(.5f,10.5f);map.Objective=new V2(50.5f,10.5f);
   int cover=10*map.Columns+11;map.Tiles[cover]=Tile.LowCover;map.Revision++;
   b.Tactics.Refresh(map);bool diagonal=false;
   foreach(var slot in b.Tactics.Slots)if(slot.ObjectId==cover&&V2.Distance(slot.Position,new V2(10.5f,9.5f))<.01f)diagonal=true;
   Check(diagonal,"Пропущено диагональное место у укрытия");
   map.Tiles[1]=Tile.LowCover;map.Revision++;b.Tactics.Refresh(map);bool edge=false;
   foreach(var slot in b.Tactics.Slots)if(slot.ObjectId==1&&V2.Distance(slot.Position,new V2(.5f,.5f))<.01f)edge=true;
   Check(edge,"Пропущено место у границы карты");
   var s=b.Soldiers[0];s.Position=new V2(10.5f,10.5f);s.OrderAnchor=s.Position;
   b.Team(s).Contacts.Clear();s.Contacts.Clear();
   Check(PositionLogic.Protected(b,s,s.OrderAnchor),"Тестовая позиция должна быть защищена");
   Check(!PositionLogic.AnchorLostCover(b,s),"Первая проверка не означает потерю укрытия");
   map.Revision++;Check(!PositionLogic.AnchorLostCover(b,s),"Чужое повреждение сбросило позицию");
   map.Tiles[cover]=Tile.Ground;map.Revision++;
   Check(PositionLogic.AnchorLostCover(b,s),"Не обнаружено уничтожение своего укрытия");
   s.OrderAnchor=new V2(20.5f,10.5f);PositionLogic.AnchorLostCover(b,s);map.Revision++;
   Check(!PositionLogic.AnchorLostCover(b,s),"Повреждение сбросило открытый маршрут");
   foreach(var other in b.Soldiers)if(other.Id!=s.Id)other.Deployed=false;
   b.Soldiers[1].Position=s.OrderAnchor;b.Soldiers[1].OrderAnchor=s.OrderAnchor;
   Check(!PositionLogic.Occupied(b,s,s.OrderAnchor),"Подкрепление до появления резервирует позицию");
   b.Soldiers[1].Deployed=true;
   Check(PositionLogic.Occupied(b,s,s.OrderAnchor),"Реальный боец не занимает позицию");
   // Expired coordination timer alone must not release a soldier into fire.
   b.Time=20;s.Deployed=true;s.Position=new V2(10.5f,10.5f);
   s.Path.Clear();s.Path.Add(new V2(12.5f,10.5f));s.PathCursor=0;
   s.Suppression=0;s.Morale=1;s.LastIncoming=0;s.ReloadUntil=0;
   s.CoverWaitStartedAt=0;
   var contact=new Contact{EnemyId=5,Position=new V2(18.5f,10.5f),SeenAt=20,Confidence=1,Visual=true};
   b.Team(s).Contacts[5]=contact;
   Check(!CoverWaitPolicy.Allow(b,s),"Таймер разрешил выход под известный огонь");
   b.Team(s).Contacts.Clear();s.Contacts.Clear();
   Check(CoverWaitPolicy.Allow(b,s),"Безопасное движение без контакта заблокировано");
   s.Deployed=false;
   Check(!PositionLogic.Able(s,b),"Не появившийся боец считается готовой поддержкой");
   s.Deployed=true;
   Debug.Log("[Рубеж] Регрессионные проверки поиска укрытий пройдены.");
  }
 }
}
#endif

