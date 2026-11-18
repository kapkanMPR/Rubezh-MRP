#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
using System;
using SilentOrders.Core;
namespace SilentOrders.Rendering
{
    public abstract partial class BattleVisualRecipes
    {
        void DrawProceduralPictures(BattleMap map) { DrawPictureSet(map.ProceduralPictures); }
        void DrawPictureSet(ProceduralPicture[] pictures)
        {
            if(pictures==null)return;
            var origin=P(new V2(0,0));float scale=Canvas.Scale;
            foreach(var image in pictures)
            {
                float left=(origin.X+image.Left)*scale,top=(origin.Y+image.Top)*scale,w=image.Width*scale,h=image.Height*scale;
                int x0=Math.Max(Canvas.ClipLeft,(int)Math.Floor(left)),x1=Math.Min(Math.Min(Canvas.Width,Canvas.ClipRight),(int)Math.Ceiling(left+w)),y0=Math.Max(Canvas.ClipTop,(int)Math.Floor(top)),y1=Math.Min(Math.Min(Canvas.Height,Canvas.ClipBottom),(int)Math.Ceiling(top+h));
                if(x0>=x1||y0>=y1)continue;
                for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++){
                    int sx=Math.Min(image.PixelWidth-1,Math.Max(0,(int)((x+.5f-left)/w*image.PixelWidth))),sy=Math.Min(image.PixelHeight-1,Math.Max(0,(int)((y+.5f-top)/h*image.PixelHeight))),src=sy*image.PixelWidth+sx,dst=y*Canvas.Width+x;
                    uint c=image.Pixels[src];int a=(int)(c>>24);if(a>=128&&Canvas.RecordDepth&&Canvas.RoofOcclusion!=null&&image.Depth[src]>Canvas.RoofOcclusion[dst])Canvas.RoofOcclusion[dst]=image.Depth[src];if(a==0||image.Depth[src]+.001f<Canvas.Occlusion[dst])continue;
                    if(a==255)Canvas.Pixels[dst]=c;else{uint old=Canvas.Pixels[dst];int r=((int)(c&255)*a+(int)(old&255)*(255-a))/255,g=((int)((c>>8)&255)*a+(int)((old>>8)&255)*(255-a))/255,b=((int)((c>>16)&255)*a+(int)((old>>16)&255)*(255-a))/255;Canvas.Pixels[dst]=PixelCanvas.RGB(r,g,b);}
                    if(a>=128)Canvas.Occlusion[dst]=image.Depth[src];
                }
            }
        }
    }
}

#endif
