#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using SilentOrders.Rendering;
namespace Rubezh.UnityPort.Editor {
 public static class LayerValidation {
  static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
  [MenuItem("Рубеж/Проверить слои отрисовки")]
  public static void Run(){
   Check(BattleRenderer.FacingFrame(new SilentOrders.Core.V2(1,-1))==2,"Неверно определён задний ракурс");
   Check(BattleRenderer.FacingFrame(new SilentOrders.Core.V2(-1,1))==0,"Неверно определён передний ракурс");
   Check(BattleRenderer.FacingFrame(new SilentOrders.Core.V2(1,1))==1,"Неверно определён правый ракурс");
   Check(BattleRenderer.FacingFrame(new SilentOrders.Core.V2(-1,-1))==3,"Неверно определён левый ракурс");
   var canvas=new PixelCanvas(8,8);canvas.Occlusion=new float[64];
   var queue=new OrderedCanvas();
   const uint rear=0xff0000ff,front=0xff00ff00,ui=0xffff0000;
   queue.Begin(canvas,null);
   // Deliberately reverse submission: a thrown object behind a soldier stays behind.
   queue.Rect(1,1,5,5,front,110,true);queue.Rect(1,1,5,5,rear,90,true);queue.Flush();
   Check(canvas.Pixels[18]==front,"Предмет за бойцом перекрывает его");
   canvas.Clear(0);queue.Begin(canvas,null);
   queue.Rect(1,1,5,5,rear,90,true);queue.Rect(1,1,5,5,front,110,true);queue.Flush();
   Check(canvas.Pixels[18]==front,"Порядок зависит от очередности подачи");
   canvas.Clear(0);canvas.Occlusion[18]=120;
   queue.Begin(canvas,null);queue.Rect(1,1,5,5,front,110,true);queue.Flush();
   Check(canvas.Pixels[18]==0,"Предмет просвечивает через укрытие");
   queue.Begin(canvas,null);queue.Rect(1,1,5,5,ui,0,false);queue.Rect(1,1,5,5,front,130,true);queue.Flush();
   Check(canvas.Pixels[18]==ui,"Индикатор скрыт объектом мира");
   canvas.Clear(0);Array.Clear(canvas.Occlusion,0,64);
   queue.Begin(canvas,null);queue.Stage=1;queue.Rect(1,1,5,5,front,90,true);
   queue.Stage=0;queue.Rect(1,1,5,5,rear,120,true);queue.Flush();
   Check(canvas.Pixels[18]==front,"Наземный предмет перекрывает бойца");
   queue.Begin(canvas,null);queue.Rect(1,1,5,5,rear,100,true);queue.Rect(1,1,5,5,front,100,true);queue.Flush();
   Check(canvas.Pixels[18]==front,"Нарушен стабильный порядок деталей персонажа");
   Check(Resources.Load<Shader>("RubezhNativeDepth")!=null,"Не найден шейдер упрощённой отрисовки");
   Check(Rubezh.Graphics.VehicleRenderWidth==132&&Rubezh.Graphics.VehicleRenderHeight==105,"Неверный визуальный размер машины");
   Check(SilentOrders.Core.VehicleArrivals.LengthCells==8&&SilentOrders.Core.VehicleArrivals.WidthCells==4,"Изменились игровые габариты машины");
   Debug.Log("[Рубеж] Проверка очереди слоёв, маски укрытий и размера машин пройдена. Дополнительно проверьте изображение на устройстве.");
  }
 }
}
#endif

