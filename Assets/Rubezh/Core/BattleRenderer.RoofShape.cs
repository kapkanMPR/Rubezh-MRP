using System;
using System.Collections.Generic;
using SilentOrders.Core;
namespace SilentOrders.Rendering {
 public abstract partial class BattleVisualRecipes {
  // Co-planar tiles share one rectangle; holes remain in the structural mask.
  readonly Dictionary<DynamicBuilding,Tuple<int,RoofQuad[]>> shapedRoofs=new Dictionary<DynamicBuilding,Tuple<int,RoofQuad[]>>();
  void AddBuildingRoof(DynamicBuilding h) {
   Tuple<int,RoofQuad[]> cached;if(shapedRoofs.TryGetValue(h,out cached)&&cached.Item1==h.Roof.Revision){roofQuads.AddRange(cached.Item2);return;}int begin=roofQuads.Count;
   foreach(var q in h.Closures){if(q.kind==1&&!h.Roof.Contains(DynamicBuilding.Key(q.anchorX,q.anchorY)))continue;AddClosure(q);}
   // Merge contiguous collinear edge tiles; preserve every hole and height change.
   var edges = new Dictionary<Tuple<int,int>, List<RoofEdgeSegment>>();
   foreach(long k in h.Roof) {
    RoofSample r;if(!h.RoofShape.TryGetValue(k,out r)||r.a<=0)continue;
    if(!h.Roof.Contains(DynamicBuilding.Key(r.x,r.y-1)))CollectRoofEdge(edges,0,r.y,r.x,r.a,r.b);
    if(!h.Roof.Contains(DynamicBuilding.Key(r.x+1,r.y)))CollectRoofEdge(edges,1,r.x+1,r.y,r.b,r.c);
    if(!h.Roof.Contains(DynamicBuilding.Key(r.x,r.y+1)))CollectRoofEdge(edges,2,r.y+1,r.x,r.d,r.c);
    if(!h.Roof.Contains(DynamicBuilding.Key(r.x-1,r.y)))CollectRoofEdge(edges,3,r.x,r.y,r.a,r.d);
   }
   foreach(var pair in edges) {
    var list=pair.Value;list.Sort((a,b)=>a.Start.CompareTo(b.Start));
    for(int i=0;i<list.Count;) {
     var first=list[i++];int end=first.Start+1;float height=first.B,slope=first.B-first.A;
     while(i<list.Count && list[i].Start==end && Math.Abs(list[i].A-height)<.00001f && Math.Abs((list[i].B-list[i].A)-slope)<.00001f){height=list[i++].B;end++;}
     int direction=pair.Key.Item1;float line=pair.Key.Item2/8f;
     var a=direction%2==0?new V2(first.Start/8f,line):new V2(line,first.Start/8f);
     var b=direction%2==0?new V2(end/8f,line):new V2(line,end/8f);
     if(direction<2)AddRoofEdge(a,b,first.A,height,h.RoofThickness);
     else AddRoofEdge(b,a,height,first.A,h.RoofThickness);
    }
   }
   foreach(var panel in h.RoofShape.Panels) {
    AddRoof(new V2(panel.LeftTop/8f,panel.Y/8f),new V2(panel.RightTop/8f,panel.Y/8f),new V2(panel.RightBottom/8f,(panel.Y+panel.H)/8f),new V2(panel.LeftBottom/8f,(panel.Y+panel.H)/8f),panel.A,panel.B,panel.C,panel.D,panel.Shade);
   }
   shapedRoofs[h]=Tuple.Create(h.Roof.Revision,roofQuads.GetRange(begin,roofQuads.Count-begin).ToArray());
  }
  void AddClosure(StructureClosure q){
   var v=q.xyz;var a=new V2(v[0],v[1]);var b=new V2(v[3],v[4]);var c=new V2(v[6],v[7]);var d=new V2(v[9],v[10]);
   var A=P(a);var B=P(b);var C=P(c);var D=P(d);A.Y-=v[2]*24/2.8f;B.Y-=v[5]*24/2.8f;C.Y-=v[8]*24/2.8f;D.Y-=v[11]*24/2.8f;
   A=A*Canvas.Scale;B=B*Canvas.Scale;C=C*Canvas.Scale;D=D*Canvas.Scale;
   roofQuads.Add(new RoofQuad{A=A,B=B,C=C,D=D,UA=a*.25f,UB=b*.25f,UC=c*.25f,UD=d*.25f,Closure=q.kind+1,HA=v[2],HB=v[5],HC=v[8],HD=v[11],Shade=1,L=Math.Min(Math.Min(A.X,B.X),Math.Min(C.X,D.X)),R=Math.Max(Math.Max(A.X,B.X),Math.Max(C.X,D.X)),T=Math.Min(Math.Min(A.Y,B.Y),Math.Min(C.Y,D.Y)),Bot=Math.Max(Math.Max(A.Y,B.Y),Math.Max(C.Y,D.Y))});
  }
  struct RoofEdgeSegment {public int Start;public float A,B;}
  static void CollectRoofEdge(Dictionary<Tuple<int,int>,List<RoofEdgeSegment>> edges,int direction,int line,int start,float a,float b) {
   var key=Tuple.Create(direction,line);List<RoofEdgeSegment> list;
   if(!edges.TryGetValue(key,out list))edges[key]=list=new List<RoofEdgeSegment>();
   list.Add(new RoofEdgeSegment{Start=start,A=a,B=b});
  }
 }
}
