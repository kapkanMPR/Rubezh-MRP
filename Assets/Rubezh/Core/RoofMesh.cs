using System;using System.Collections.Generic;
namespace SilentOrders.Core {
 public struct RoofRect {public int X,Y,W,H; public RoofRect(int x,int y,int w,int h){X=x;Y=y;W=w;H=h;}}
 public static class RoofMesh {
  public static List<RoofRect> Merge(IEnumerable<long> cells){
   var rows=new SortedDictionary<int,List<int>>();foreach(long k in cells){int x=(int)(k>>32),y=(int)k;List<int> row;if(!rows.TryGetValue(y,out row))rows[y]=row=new List<int>();row.Add(x);}
   var result=new List<RoofRect>();var previous=new Dictionary<long,int>();int last=int.MinValue;
   foreach(var row in rows){row.Value.Sort();var next=new Dictionary<long,int>();int start=0;
    for(int i=0;i<row.Value.Count;i++){int x=row.Value[i];if(i==0)start=x;if(i+1<row.Value.Count&&row.Value[i+1]==x+1)continue;
     int width=x-start+1,index;long key=DynamicBuilding.Key(start,width);
     if(row.Key==last+1&&previous.TryGetValue(key,out index)){var r=result[index];r.H++;result[index]=r;}
     else {index=result.Count;result.Add(new RoofRect(start,row.Key,width,1));}next[key]=index;if(i+1<row.Value.Count)start=row.Value[i+1];
    }previous=next;last=row.Key;
   }return result;
  }
 }
}
