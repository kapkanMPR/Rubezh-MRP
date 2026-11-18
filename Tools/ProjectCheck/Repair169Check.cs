using System;
using System.IO;
using System.Collections.Generic;
using SilentOrders.Core;
using SilentOrders.Rendering;
class Repair169Check
{
 static void Main()
 {
  Rubezh.Graphics.ConfigurePacks();
  var battle=new BattleSimulation();
  var required=Rubezh.Graphics.SceneSprites(battle);
  var pages=new HashSet<int>();
  foreach(int id in required)pages.Add(Rubezh.SpritePackIndex.Page[id]);
  foreach(int page in pages)Rubezh.Graphics.LoadPage(page,File.ReadAllBytes("Assets/Rubezh/Editor/LegacySources/"+Rubezh.SpritePackIndex.Path(page)+".bytes"),null,null,required);
  Rubezh.Graphics.ValidateSceneSprites(required);
  var canvas=new PixelCanvas(320,200);
  for(int type=0;type<Rubezh.Graphics.AmmoArtCount;type++)
  {
   int w,h;Rubezh.Graphics.SpriteData(Rubezh.Graphics.AmmoId(type),0,out w,out h);
   if(w<=1||h<=1)throw new Exception("Missing ammunition "+type);
   Rubezh.Graphics.Ammunition(canvas,type,40+type*30,100);
  }
  int width,height;
  Rubezh.Graphics.SpriteData(-123,0,out width,out height);
  Rubezh.Graphics.ReplaySprite(canvas,-123,10,10,10,10,0);
  Rubezh.Graphics.Ammunition(canvas,4,100,100);
  if(Rubezh.Graphics.MissingSpriteCount!=1)throw new Exception("Missing sprite diagnostics not deduplicated");
  Console.WriteLine("PASS ammunition preload, six drops, missing resource frame isolation");
  var rng=new Random(169);
  for(int test=0;test<100;test++)
  {
   int size=8+test%19;var roof=new HashSet<long>();var anchors=new HashSet<long>();
   for(int y=0;y<size;y++)for(int x=0;x<size;x++)if(rng.NextDouble()>.22){long k=DynamicBuilding.Key(x,y);roof.Add(k);if(x==0||y==0)anchors.Add(k);}
   var reference=new HashSet<long>(roof);
   RoofSupportBefore169.Collapse(reference,anchors);RoofSupport.Collapse(roof,anchors);
   if(!roof.SetEquals(reference))throw new Exception("Roof support changed at seed "+test);
  }
  Console.WriteLine("PASS 100 roof support cases: pooled and original algorithms agree");
 }
}


namespace SilentOrders.Core {
 // Dense scratch grid: no hash sets or queues allocated for every candidate neck.
 public static class RoofSupportBefore169 {
  public static double MaxMilliseconds;public static long LastVisits;public static double LastMilliseconds;public static int LastCells;
  public static void Collapse(HashSet<long> roof,HashSet<long> anchors){var watch=System.Diagnostics.Stopwatch.StartNew();LastVisits=0;LastCells=roof.Count;if(roof.Count==0){LastMilliseconds=0;return;}
   int minX=int.MaxValue,minY=int.MaxValue,maxX=int.MinValue,maxY=int.MinValue;foreach(long k in roof){int x=(int)(k>>32),y=(int)k;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}minX--;minY--;int w=maxX-minX+2,h=maxY-minY+2,n=checked(w*h);var solid=new bool[n];var anchor=new bool[n];var marks=new int[n];var anchored=new int[n];var cut=new int[n];var queue=new int[n];int stamp=1;int[] delta={1,-1,w,-w};
   foreach(long k in roof)solid[((int)k-minY)*w+(int)(k>>32)-minX]=true;
   int tail=0;foreach(long k in anchors){int x=(int)(k>>32)-minX,y=(int)k-minY;if(x<=0||x>=w-1||y<=0||y>=h-1)continue;int i=y*w+x;if(solid[i]&&!anchor[i]){anchor[i]=true;marks[i]=stamp;queue[tail++]=i;}}
   for(int head=0;head<tail;head++){LastVisits++;int i=queue[head];foreach(int d in delta){int j=i+d;if(solid[j]&&marks[j]!=stamp){marks[j]=stamp;queue[tail++]=j;}}}for(int i=0;i<n;i++)if(solid[i]&&marks[i]!=stamp)solid[i]=false;
   for(int axis=0;axis<2;axis++){int outer=axis==0?w:h,inner=axis==0?h:w,step=axis==0?w:1;for(int line=1;line<outer-1;line++)for(int pos=1;pos<inner-1;){int start=axis==0?pos*w+line:line*w+pos;if(!solid[start]){pos++;continue;}int length=0;bool supported=false;while(pos+length<inner-1&&solid[start+length*step]){supported|=anchor[start+length*step];length++;}pos+=length;if(supported)continue;stamp++;for(int j=0;j<length;j++)cut[start+j*step]=stamp;
    for(int j=0;j<length;j++)foreach(int d in delta){int seed=start+j*step+d;if(!solid[seed]||cut[seed]==stamp||marks[seed]==stamp)continue;int head=0;tail=1;queue[0]=seed;marks[seed]=stamp;bool reachesAnchor=false;while(head<tail&&!reachesAnchor){int i=queue[head++];LastVisits++;if(anchor[i]){reachesAnchor=true;break;}foreach(int dd in delta){int next=i+dd;if(!solid[next]||cut[next]==stamp)continue;if(anchored[next]==stamp){reachesAnchor=true;break;}if(marks[next]!=stamp){marks[next]=stamp;queue[tail++]=next;}}}
     if(reachesAnchor){for(int q=0;q<tail;q++)anchored[queue[q]]=stamp;}else if(tail>6*length*4){for(int q=0;q<tail;q++)solid[queue[q]]=false;}
    }
   }}
   // Final support pass also removes necks detached by earlier collapses.
   stamp++;tail=0;for(int i=0;i<n;i++)if(solid[i]&&anchor[i]){marks[i]=stamp;queue[tail++]=i;}for(int head=0;head<tail;head++){int i=queue[head];LastVisits++;foreach(int d in delta){int j=i+d;if(solid[j]&&marks[j]!=stamp){marks[j]=stamp;queue[tail++]=j;}}}
   roof.RemoveWhere(k=>marks[((int)k-minY)*w+(int)(k>>32)-minX]!=stamp);LastMilliseconds=watch.Elapsed.TotalMilliseconds;MaxMilliseconds=Math.Max(MaxMilliseconds,LastMilliseconds);
  }
 }
}
