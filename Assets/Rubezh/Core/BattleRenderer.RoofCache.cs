using System;using System.Collections.Generic;using SilentOrders.Core;
namespace SilentOrders.Rendering {
 public interface ICanvasViewportGpu { bool Visible(float left,float top,float right,float bottom); }
 public interface IRoofVisibilityGpu {void RoofVisibility(BattleSimulation battle);}
 public interface IClosureGpu {void ClosureFace(V2 a,V2 b,V2 c,V2 d,V2 ua,V2 ub,V2 uc,V2 ud,float ha,float hb,float hc,float hd,int kind);}
 public interface IShapedRoofGpu {void RoofFace(V2 a,V2 b,V2 c,V2 d,V2 ua,V2 ub,V2 uc,V2 ud,float shade);}
 public interface IRoofPatchGpu {void RoofPatch(float x0,float y0,float x1,float y1,float x2,float y2,float x3,float y3,float u0,float v0,float u1,float v1);}
 public abstract partial class BattleVisualRecipes {
  Action drawNativeRoofQuads;
  void DrawNativeRoofQuads(){var gpu=(IRoofPatchGpu)Canvas.Gpu;foreach(var q in roofQuads)SendRoof(gpu,q);}
  struct RoofQuad {public V2 A,B,C,D,UA,UB,UC,UD;public int Closure;public float HA,HB,HC,HD;public float Shade, L,T,R,Bot,U0,V0,U1,V1;}
  readonly List<RoofQuad> roofQuads=new List<RoofQuad>();BattleMap roofMap;int roofRevision=-1,roofGeometrySerial;
  readonly List<int> roofCellCounts=new List<int>();
  bool[] legacyRoofSnapshot;
  bool RoofGeometryChanged(BattleMap map) {
   bool changed=roofMap!=map;
   int count=map.DynamicBuildings==null?0:map.DynamicBuildings.Count;
   if(roofCellCounts.Count!=count)changed=true;
   for(int i=0;i<count;i++)if(i>=roofCellCounts.Count||roofCellCounts[i]!=map.DynamicBuildings[i].Roof.Revision)changed=true;
   int length=map.Roof==null?0:map.Roof.Length;
   if(legacyRoofSnapshot==null||legacyRoofSnapshot.Length!=length)changed=true;
   else for(int i=0;i<length;i++)if(legacyRoofSnapshot[i]!=map.Roof[i]){changed=true;break;}
   if(changed) {
    roofCellCounts.Clear();for(int i=0;i<count;i++)roofCellCounts.Add(map.DynamicBuildings[i].Roof.Revision);
    legacyRoofSnapshot=map.Roof==null?new bool[0]:(bool[])map.Roof.Clone();
   }
   return changed;
  }
  public int RoofDrawPieces=>roofQuads.Count;
  void AddRoof(V2 a,V2 b,V2 c,V2 d,float ha=2.8f,float hb=2.8f,float hc=2.8f,float hd=2.8f,float shadeOverride=-1){var ua=a*.25f;var ub=b*.25f;var uc=c*.25f;var ud=d*.25f;float shade=Math.Max(.65f,Math.Min(1f,.88f+((hb-ha)/Math.Max(.125f,b.X-a.X)-(hd-ha)/Math.Max(.125f,d.Y-a.Y))*.08f));if(shadeOverride>=0)shade=shadeOverride;float u0=a.X/4,v0=a.Y/4,u1=c.X/4,v1=c.Y/4;a=P(a);b=P(b);c=P(c);d=P(d);a.Y-=ha*24/2.8f;b.Y-=hb*24/2.8f;c.Y-=hc*24/2.8f;d.Y-=hd*24/2.8f;
   a=a*Canvas.Scale;b=b*Canvas.Scale;c=c*Canvas.Scale;d=d*Canvas.Scale;
   roofQuads.Add(new RoofQuad{A=a,B=b,C=c,D=d,UA=ua,UB=ub,UC=uc,UD=ud,Shade=shade,U0=u0,V0=v0,U1=u1,V1=v1,L=Math.Min(Math.Min(a.X,b.X),Math.Min(c.X,d.X)),R=Math.Max(Math.Max(a.X,b.X),Math.Max(c.X,d.X)),T=Math.Min(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y)),Bot=Math.Max(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))});}
  void SendRoof(IRoofPatchGpu gpu,RoofQuad q){if(q.Closure>0&&gpu is IClosureGpu){((IClosureGpu)gpu).ClosureFace(q.A,q.B,q.C,q.D,q.UA,q.UB,q.UC,q.UD,q.HA,q.HB,q.HC,q.HD,q.Closure-1);return;}var shape=gpu as IShapedRoofGpu;if(shape!=null)shape.RoofFace(q.A,q.B,q.C,q.D,q.UA,q.UB,q.UC,q.UD,q.Shade);else gpu.RoofPatch(q.A.X,q.A.Y,q.B.X,q.B.Y,q.C.X,q.C.Y,q.D.X,q.D.Y,q.U0,q.V0,q.U1,q.V1);}
  void AddRoofEdge(V2 a,V2 b,float ha,float hb,float thickness){
   var A=P(a);var B=P(b);A.Y-=ha*24/2.8f;B.Y-=hb*24/2.8f;
   var D=new V2(A.X,A.Y+thickness*24/2.8f);var C=new V2(B.X,B.Y+thickness*24/2.8f);
   A=A*Canvas.Scale;B=B*Canvas.Scale;C=C*Canvas.Scale;D=D*Canvas.Scale;
   roofQuads.Add(new RoofQuad{A=A,B=B,C=C,D=D,UA=a*.25f,UB=b*.25f,UC=b*.25f,UD=a*.25f,Shade=.6f,L=Math.Min(A.X,B.X),R=Math.Max(A.X,B.X),T=Math.Min(A.Y,B.Y),Bot=Math.Max(C.Y,D.Y)});
  }
  protected void DrawCachedRoofs(BattleSimulation b){var visibility=Canvas.Gpu as IRoofVisibilityGpu;var gpu=Canvas.Gpu as IRoofPatchGpu;
   if(roofMap!=b.Map||roofRevision!=b.Map.Revision){
    bool changed=RoofGeometryChanged(b.Map);roofRevision=b.Map.Revision;
    if(changed){if(roofMap!=b.Map)shapedRoofs.Clear();roofQuads.Clear();roofMap=b.Map;roofGeometrySerial++;
    if(b.Map.Roof!=null)for(int i=0;i<b.Map.Roof.Length;i++)if(b.Map.Roof[i]&&(b.Map.DynamicBuildings==null||!b.Map.DynamicBuildings.Exists(h=>h.Contains(b.Map.Center(i))))){var p=b.Map.Center(i);AddRoof(new V2(p.X-.5f,p.Y-.5f),new V2(p.X+.5f,p.Y-.5f),new V2(p.X+.5f,p.Y+.5f),new V2(p.X-.5f,p.Y+.5f));}
    if(b.Map.DynamicBuildings!=null)foreach(var h in b.Map.DynamicBuildings)AddBuildingRoof(h);
    }
   }
   if(visibility!=null&&roofQuads.Count>0)visibility.RoofVisibility(b);
   if(gpu==null){
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
    DrawCpuRoofs(b);return;
#else
    throw new InvalidOperationException("Native roof sink required.");
#endif
   }
   var native=Canvas.Gpu as IStaticSceneGpu;if(native!=null&&native.NativeScene){if(drawNativeRoofQuads==null)drawNativeRoofQuads=DrawNativeRoofQuads;native.RoofMesh(b.Map,roofGeometrySerial,drawNativeRoofQuads);return;}
   var view=Canvas.Gpu as ICanvasViewportGpu;foreach(var q in roofQuads)if(view==null||view.Visible(q.L,q.T,q.R,q.Bot))SendRoof(gpu,q);
  }
 }
}
