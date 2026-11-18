using System;
namespace SilentOrders.Core {
 public static class PictureStorage {
  public static ProceduralPicture Trim(ProceduralPicture p){int w=p.PixelWidth,h=p.PixelHeight,l=w,t=h,r=0,b=0;
   for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(p.Pixels[y*w+x]>>24!=0){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x+1);b=Math.Max(b,y+1);}
   if(l==w)return new ProceduralPicture{Left=p.Left,Top=p.Top,Width=p.Width/w,Height=p.Height/h,PixelWidth=1,PixelHeight=1,Pixels=new uint[1],Depth=new float[1]};
   l=Math.Max(0,l-1);t=Math.Max(0,t-1);r=Math.Min(w,r+1);b=Math.Min(h,b+1);if(l==0&&t==0&&r==w&&b==h)return p;
   var q=new ProceduralPicture{Left=p.Left+l*p.Width/w,Top=p.Top+t*p.Height/h,Width=(r-l)*p.Width/w,Height=(b-t)*p.Height/h,PixelWidth=r-l,PixelHeight=b-t,Pixels=new uint[(r-l)*(b-t)],Depth=new float[(r-l)*(b-t)]};
   for(int y=t;y<b;y++){Array.Copy(p.Pixels,y*w+l,q.Pixels,(y-t)*q.PixelWidth,q.PixelWidth);Array.Copy(p.Depth,y*w+l,q.Depth,(y-t)*q.PixelWidth,q.PixelWidth);}return q;
  }
 }
}
