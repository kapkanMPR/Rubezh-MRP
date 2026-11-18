using System;
using System.Collections.Generic;
using System.Diagnostics;
using SilentOrders.Core;
using UnityEngine;
namespace Rubezh.UnityPort {
 [Serializable] public sealed class DrawingPath {public float[] points;}
 [Serializable] public sealed class DrawingClip {public DrawingPath[] paths;public bool evenOdd;}
 [Serializable] public sealed class DrawingOp {public DrawingPath[] paths;public DrawingClip[] clips;public int[] clipIds;public int[] rgba;public float alpha=1,depth;public string texture;public float[] basis,uv;}
 #if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
 public static class BuildingRecipe {
  sealed class Material {public int w,h;public Color32[] pixels;}
  static readonly LinkedList<string> materialLru=new LinkedList<string>();
  static readonly Dictionary<string,Material> materials=new Dictionary<string,Material>();
  public static long MaterialBytes {get;private set;} public static int MaterialCount=>materials.Count;
  public static long GeneratedBytes {get;private set;} public static int GeneratedLayers {get;private set;}
  public static double GenerationMilliseconds {get;private set;}
  public static void BeginMap(){GeneratedBytes=0;GeneratedLayers=0;GenerationMilliseconds=0;}
  public static void ClearMaterials(){materials.Clear();materialLru.Clear();MaterialBytes=0;}
  static Material Load(string id){Material m;if(materials.TryGetValue(id,out m)){materialLru.Remove(id);materialLru.AddLast(id);return m;}
   if(id.Length!=24)throw new ArgumentException("Invalid building material");foreach(char c in id)if(!Uri.IsHexDigit(c))throw new ArgumentException("Invalid material id");
   var decoded=BuildingMaterialPixels.Read(id);m=new Material{w=decoded.width,h=decoded.height,pixels=decoded.pixels};
   // Bounded transient material cache. Rendered layers retain pixels, not materials.
   while(MaterialBytes+m.pixels.LongLength*4>64*1024*1024&&materialLru.Count>0){string old=materialLru.First.Value;materialLru.RemoveFirst();MaterialBytes-=materials[old].pixels.LongLength*4;materials.Remove(old);}materials[id]=m;materialLru.AddLast(id);MaterialBytes+=m.pixels.LongLength*4;return m;
  }
  static int Winding(DrawingPath p,float x,float y){var a=p.points;if(a==null||a.Length<6)return 0;int n=0;for(int i=0,j=a.Length-2;i<a.Length;j=i,i+=2){float ax=a[j],ay=a[j+1],bx=a[i],by=a[i+1];if(ay<=y){if(by>y&&(bx-ax)*(y-ay)-(x-ax)*(by-ay)>0)n++;}else if(by<=y&&(bx-ax)*(y-ay)-(x-ax)*(by-ay)<0)n--;}return n;}
  static bool Inside(DrawingPath[] paths,float x,float y,bool even=false){int n=0;if(paths!=null)foreach(var p in paths)n+=Winding(p,x,y);return even?(n&1)!=0:n!=0;}
  static float Clamp(float x)=>Math.Max(0,Math.Min(1,x));
  static void Blend(uint[] p,int i,float r,float g,float b,float a){a=Clamp(a);if(a<=0)return;uint old=p[i];float oa=(old>>24)/255f,outA=a+oa*(1-a);if(outA<=0)return;float k=oa*(1-a);p[i]=(uint)Math.Min(255,(r*a+(old&255)*k)/outA)|((uint)Math.Min(255,(g*a+((old>>8)&255)*k)/outA)<<8)|((uint)Math.Min(255,(b*a+((old>>16)&255)*k)/outA)<<16)|((uint)Math.Round(outA*255)<<24);}
  static void Sample(Material m,float u,float v,out float r,out float g,out float b,out float a){float fx=Clamp(u)*m.w-.5f,fy=Clamp(v)*m.h-.5f;int x=(int)Math.Floor(fx),y=(int)Math.Floor(fy);float tx=fx-x,ty=fy-y;r=g=b=a=0;for(int j=0;j<2;j++)for(int i=0;i<2;i++){int X=Math.Max(0,Math.Min(m.w-1,x+i)),Y=Math.Max(0,Math.Min(m.h-1,y+j));var c=m.pixels[(m.h-1-Y)*m.w+X];float k=(i==0?1-tx:tx)*(j==0?1-ty:ty),ca=c.a/255f;r+=c.r*ca*k;g+=c.g*ca*k;b+=c.b*ca*k;a+=ca*k;}if(a>0){r/=a;g/=a;b/=a;}}
  struct Span {public float a,b;public Span(float x,float y){a=x;b=y;}}
  struct Crossing {public float x;public int direction;public Crossing(float p,int d){x=p;direction=d;}}
  sealed class ScanWork {
   public readonly List<Crossing> Cuts=new List<Crossing>();public List<Span> A=new List<Span>(),B=new List<Span>(),C=new List<Span>();
   public readonly long[] Keys=new long[512];public readonly List<Span>[] Clips=new List<Span>[512];
   public ScanWork(){for(int i=0;i<Keys.Length;i++)Keys[i]=long.MinValue;}
  }
  static void Scan(DrawingPath[] paths,float y,bool even,List<Span> spans,List<Crossing> cuts){
   cuts.Clear();spans.Clear();if(paths!=null)foreach(var path in paths){var p=path.points;for(int i=0,j=p.Length-2;i<p.Length;j=i,i+=2){float ay=p[j+1],by=p[i+1];if((ay<=y&&by>y)||(by<=y&&ay>y))cuts.Add(new Crossing(p[j]+(y-ay)*(p[i]-p[j])/(by-ay),by>ay?1:-1));}}
   cuts.Sort((a,b)=>a.x.CompareTo(b.x));int n=0;float start=0;foreach(var c in cuts){bool before=even?(n&1)!=0:n!=0;n+=c.direction;bool after=even?(n&1)!=0:n!=0;if(!before&&after)start=c.x;else if(before&&!after&&c.x>start)spans.Add(new Span(start,c.x));}
  }
  static void Intersect(List<Span> a,List<Span> b,List<Span> result){result.Clear();int i=0,j=0;while(i<a.Count&&j<b.Count){float lo=Math.Max(a[i].a,b[j].a),hi=Math.Min(a[i].b,b[j].b);if(hi>lo)result.Add(new Span(lo,hi));if(a[i].b<b[j].b)i++;else j++;}}
  static void ClipWith(ScanWork w,List<Span> clip){Intersect(w.A,clip,w.C);var swap=w.A;w.A=w.C;w.C=swap;}
  static void Coverage(DrawingOp op,ProceduralVisualData v,float y,float sx,int x0,int x1,byte[] row,ScanWork w,int scanRow){
   Scan(op.paths,y,false,w.A,w.Cuts);if(op.clips!=null)foreach(var clip in op.clips){Scan(clip.paths,y,clip.evenOdd,w.B,w.Cuts);ClipWith(w,w.B);}
   if(op.clipIds!=null)foreach(int ci in op.clipIds){if(v.clipTable==null||ci<0||ci>=v.clipTable.Length)throw new ArgumentException("Invalid clip reference");
    long key=((long)ci<<32)|(uint)scanRow;int slot=(int)((uint)(scanRow*397)^((uint)ci*17))&511;var spans=w.Clips[slot];if(spans==null)w.Clips[slot]=spans=new List<Span>();
    if(w.Keys[slot]!=key){var clip=v.clipTable[ci];Scan(clip.paths,y,clip.evenOdd,spans,w.Cuts);w.Keys[slot]=key;}ClipWith(w,spans);
   }
   foreach(var s in w.A)for(int q=0;q<2;q++){float off=q==0?.25f:.75f;int lo=Math.Max(x0,(int)Math.Ceiling((s.a-v.left)*sx-off)),hi=Math.Min(x1,(int)Math.Ceiling((s.b-v.left)*sx-off));for(int x=lo;x<hi;x++)row[x-x0]++;}
  }


  public static ProceduralPicture Render(ProceduralVisualData v){var timer=Stopwatch.StartNew();
   if(v.width<=0||v.height<=0||float.IsInfinity(v.width)||float.IsInfinity(v.height)||float.IsNaN(v.width)||float.IsNaN(v.height))throw new ArgumentException("Invalid recipe bounds");
   float scale=Math.Min(4.8f,4096/Math.Max(v.width,v.height));int w=Math.Max(1,(int)Math.Ceiling(v.width*scale)),h=Math.Max(1,(int)Math.Ceiling(v.height*scale));float sx=w/v.width,sy=h/v.height;
   var p=new ProceduralPicture{Left=v.left,Top=v.top,Width=v.width,Height=v.height,PixelWidth=w,PixelHeight=h,Pixels=new uint[w*h],Depth=new float[w*h]};
   var work=new ScanWork();byte[] row=new byte[w];
   foreach(var op in v.recipe){if(op.paths==null||op.rgba==null||op.rgba.Length!=4)throw new ArgumentException("Invalid drawing operation");float lx=float.MaxValue,ly=float.MaxValue,hx=float.MinValue,hy=float.MinValue;foreach(var path in op.paths){if(path.points==null||path.points.Length%2!=0)throw new ArgumentException("Invalid path");for(int i=0;i<path.points.Length;i+=2){float x=path.points[i],y=path.points[i+1];lx=Math.Min(lx,x);hx=Math.Max(hx,x);ly=Math.Min(ly,y);hy=Math.Max(hy,y);}}
    int x0=Math.Max(0,(int)Math.Floor((lx-v.left)*sx)),x1=Math.Min(w,(int)Math.Ceiling((hx-v.left)*sx)),y0=Math.Max(0,(int)Math.Floor((ly-v.top)*sy)),y1=Math.Min(h,(int)Math.Ceiling((hy-v.top)*sy));if(x0>=x1||y0>=y1)continue;
    Material mat=null;float ax=0,ay=0,bx=0,by=0,dx=0,dy=0,det=1;if(!string.IsNullOrEmpty(op.texture)){mat=Load(op.texture);if(op.basis==null||op.basis.Length!=6||op.uv==null||op.uv.Length!=4)throw new ArgumentException("Invalid material coordinates");ax=op.basis[0];ay=op.basis[1];bx=op.basis[2]-ax;by=op.basis[3]-ay;dx=op.basis[4]-ax;dy=op.basis[5]-ay;det=bx*dy-by*dx;if(Math.Abs(det)<1e-10)continue;}
    for(int y=y0;y<y1;y++){Array.Clear(row,0,row.Length);float Y=v.top+(y+.5f)/sy;Coverage(op,v,Y-.25f/sy,sx,x0,x1,row,work,y*2);Coverage(op,v,Y+.25f/sy,sx,x0,x1,row,work,y*2+1);for(int x=x0;x<x1;x++){float coverage=row[x-x0]*.25f;if(coverage==0)continue;float X=v.left+(x+.5f)/sx;
     float r=op.rgba[0],g=op.rgba[1],b=op.rgba[2],a=op.rgba[3]/255f;if(mat!=null){float u=((X-ax)*dy-(Y-ay)*dx)/det,t=(bx*(Y-ay)-by*(X-ax))/det;Sample(mat,op.uv[0]+u*op.uv[2],op.uv[1]+t*op.uv[3],out r,out g,out b,out a);}a*=op.alpha*coverage;if(a<=0)continue;int k=y*w+x;Blend(p.Pixels,k,r,g,b,a);p.Depth[k]=op.depth;
    }}
   }
   p=PictureStorage.Trim(p);timer.Stop();GeneratedLayers++;GeneratedBytes+=p.Pixels.LongLength*8;GenerationMilliseconds+=timer.Elapsed.TotalMilliseconds;return p;
  }
 }
#else
 public static class BuildingRecipe {
  public static long MaterialBytes=>0;public static int MaterialCount=>0;
  public static long GeneratedBytes=>0;public static int GeneratedLayers=>0;
  public static double GenerationMilliseconds=>0;
  public static void BeginMap(){}public static void ClearMaterials(){}
  public static ProceduralPicture Render(ProceduralVisualData visual){throw new InvalidOperationException("Building recipes are Editor-only; prepare native content.");}
 }
#endif

}
