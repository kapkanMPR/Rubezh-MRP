using System;using System.Collections.Generic;
namespace SilentOrders.Core {
 // Geometry stays fixed during destruction; entries refer to live wall masks.
 public sealed class DynamicWallIndex {
  const float Cell=4;readonly List<DynamicBuilding> source;readonly int count;
  readonly Dictionary<long,List<DynamicBuilding.Wall>> bins=new Dictionary<long,List<DynamicBuilding.Wall>>();
  readonly List<DynamicBuilding.Wall> result=new List<DynamicBuilding.Wall>();readonly HashSet<DynamicBuilding.Wall> seen=new HashSet<DynamicBuilding.Wall>();
  public long Queries,Candidates;public int WallCount;
  DynamicWallIndex(List<DynamicBuilding> buildings){source=buildings;count=buildings.Count;foreach(var h in buildings)foreach(var w in h.Walls){WallCount++;var a=w.Point(0);var b=w.Point(1);float pad=Math.Max(.025f,w.Source.thickness/3);for(int y=Bin(Math.Min(a.Y,b.Y)-pad);y<=Bin(Math.Max(a.Y,b.Y)+pad);y++)for(int x=Bin(Math.Min(a.X,b.X)-pad);x<=Bin(Math.Max(a.X,b.X)+pad);x++){long k=DynamicBuilding.Key(x,y);List<DynamicBuilding.Wall> list;if(!bins.TryGetValue(k,out list))bins[k]=list=new List<DynamicBuilding.Wall>();list.Add(w);}}}
  static int Bin(float v)=>(int)Math.Floor(v/Cell);
  public static DynamicWallIndex For(BattleMap map){if(map.WallIndex==null||!ReferenceEquals(map.WallIndex.source,map.DynamicBuildings)||map.WallIndex.count!=map.DynamicBuildings.Count)map.WallIndex=new DynamicWallIndex(map.DynamicBuildings);return map.WallIndex;}
  public List<DynamicBuilding.Wall> Query(V2 a,V2 b){result.Clear();seen.Clear();Queries++;for(int y=Bin(Math.Min(a.Y,b.Y));y<=Bin(Math.Max(a.Y,b.Y));y++)for(int x=Bin(Math.Min(a.X,b.X));x<=Bin(Math.Max(a.X,b.X));x++){List<DynamicBuilding.Wall> list;if(!bins.TryGetValue(DynamicBuilding.Key(x,y),out list))continue;foreach(var w in list)if(seen.Add(w))result.Add(w);}Candidates+=result.Count;return result;}
 }
}
