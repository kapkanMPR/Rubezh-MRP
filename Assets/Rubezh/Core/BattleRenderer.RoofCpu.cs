#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
using System;using System.Collections.Generic;using SilentOrders.Core;
namespace SilentOrders.Rendering {
 public abstract partial class BattleVisualRecipes {
  static bool RoofUv(V2 p,V2 a,V2 b,V2 c,out float u,out float v){float bx=b.X-a.X,by=b.Y-a.Y,cx=c.X-a.X,cy=c.Y-a.Y,det=bx*cy-by*cx;u=v=0;if(Math.Abs(det)<1e-8f)return false;float x=p.X-a.X,y=p.Y-a.Y;u=(x*cy-y*cx)/det;v=(bx*y-by*x)/det;return u>=0&&v>=0&&u+v<=1;}
  readonly List<V2> roofActors=new List<V2>();
  void DrawCpuRoofs(BattleSimulation battle){RoofVisibility.Actors(battle,roofActors);var origin=P(new V2(0,0));float scale=Canvas.Scale;
   foreach(var q in roofQuads){int l=Math.Max(0,(int)Math.Floor(q.L)),t=Math.Max(0,(int)Math.Floor(q.T)),r=Math.Min(Canvas.Width,(int)Math.Ceiling(q.R)),b=Math.Min(Canvas.Height,(int)Math.Ceiling(q.Bot));
    for(int y=t;y<b;y++)for(int x=l;x<r;x++){float u,v,z;V2 uv;if(RoofUv(new V2(x+.5f,y+.5f),q.A,q.B,q.C,out u,out v)){uv=q.UA+(q.UB-q.UA)*u+(q.UC-q.UA)*v;z=q.HA+(q.HB-q.HA)*u+(q.HC-q.HA)*v;}
     else if(RoofUv(new V2(x+.5f,y+.5f),q.A,q.C,q.D,out u,out v)){uv=q.UA+(q.UC-q.UA)*u+(q.UD-q.UA)*v;z=q.HA+(q.HC-q.HA)*u+(q.HD-q.HA)*v;}else continue;
     var world=uv*4;int i=y*Canvas.Width+x;if(Canvas.RoofOcclusion!=null&&Canvas.RoofOcclusion[i]>256+world.Y-world.X+.061f)continue;
     float a=RoofVisibility.Opacity(world,roofActors);uint c=q.Closure==1?PixelCanvas.RGB(119,122,120):q.Closure==2?ClosureSurfacePixels.Sample((world.X+world.Y)/2,z/3):RoofSurfacePixels.Sample(world.X/4,world.Y/4),old=Canvas.Pixels[i];int rr=(int)((c&255)*a*q.Shade+(old&255)*(1-a)),gg=(int)(((c>>8)&255)*a*q.Shade+((old>>8)&255)*(1-a)),bb=(int)(((c>>16)&255)*a*q.Shade+((old>>16)&255)*(1-a));Canvas.Pixels[i]=PixelCanvas.RGB(rr,gg,bb);
    }
   }
  }
 }
 public static class ClosureSurfacePixels {
  public static uint[] Pixels;public static int Width,Height;
  public static uint Sample(float u,float v){if(Pixels==null)return PixelCanvas.RGB(112,91,64);int x=(int)((u-Math.Floor(u))*Width),y=(int)((v-Math.Floor(v))*Height);return Pixels[y*Width+x];}
 }
 public static class RoofSurfacePixels {
  public static uint[] Pixels;public static int Width,Height;
  public static uint Sample(float u,float v){if(Pixels==null)return PixelCanvas.RGB(112,113,102);int x=(int)((u-Math.Floor(u))*Width),y=(int)((v-Math.Floor(v))*Height);return Pixels[y*Width+x];}
 }
}

#endif
