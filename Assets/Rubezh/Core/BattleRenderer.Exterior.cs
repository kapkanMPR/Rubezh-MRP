using System;
using System.Collections.Generic;
using SilentOrders.Core;

namespace SilentOrders.Rendering
{
    public abstract partial class BattleVisualRecipes
    {
        // Visual-only scenery. Never allocates cells or writes to BattleMap/occlusion.
        struct ExteriorProp { public int X,Y,Seed;public bool House; }
        static int ExteriorHash(int x,int y) { unchecked { uint n=(uint)(x*73856093^y*19349663);n^=n>>13;n*=1274126177;return (int)(n&0x7fffffff); } }
#if UNITY_EDITOR || !UNITY_5_3_OR_NEWER
        void DrawExterior(BattleMap map)
        {
            if(cachedMap==map&&basePixels!=null&&basePixels.Length==Canvas.Pixels.Length){
                Array.Copy(basePixels,Canvas.Pixels,basePixels.Length);return;
            }
            var layout=new ExteriorLayout(map);
            bool urban=layout.Style=="town"||layout.Style=="ruins"||layout.Style=="hospital"||layout.Style=="construction"||layout.Style=="industrial";
            bool depot=layout.Style=="depot"||layout.Style=="base";
            int br=urban?60:depot?66:58,bg=urban?65:depot?68:68,bb=urban?59:depot?54:45;
            Canvas.Clear(C(br,bg,bb));
            int width=LogicalWidth(map),height=LogicalHeight(map);
            // Fine deterministic ground grain, cached with scenery rather than redrawn per frame.
            for(int y=0;y<height;y+=3)for(int x=0;x<width;x+=3){int n=ExteriorHash(x,y)%13-6;Canvas.Rect(x,y,3,3,C(br+n,bg+n,bb+n));}
            // Inverse-project the finite image bounds; don't scan a huge off-screen square.
            float halfX=width/22f+90f/11f,halfY=height/10f+90f/5f;
            int reach=(int)Math.Ceiling((halfX+halfY)*.5f)+2;
            int startX=map.Columns/2-reach,endX=map.Columns/2+reach;
            int startY=map.Rows/2-reach,endY=map.Rows/2+reach;
            // Continue actual edge surfaces and road exits in world space.
            for(int y=startY;y<endY;y++)for(int x=startX;x<endX;x++)
            {
                if(x>=0&&y>=0&&x<map.Columns&&y<map.Rows)continue;
                var foot=P(new V2(x,y));if(foot.X<-30||foot.X>width+30||foot.Y<-20||foot.Y>height+20)continue;
                var surface=layout.SurfaceAt(x,y);
                if(surface!=null)StudioDrawing.Draw(Canvas,new StudioPlaced{Asset=surface,X=x,Y=y},foot.X,foot.Y);
                else { StudioAsset unused; bool road=layout.RoadAt(x+.5f,y+.5f,0,out unused);var center=P(new V2(x+.5f,y+.5f));Canvas.Diamond((int)center.X,(int)center.Y,11,5,road?C(80,85,78):C(br,bg,bb)); }
            }
            var props=new List<ExteriorProp>();
            if(layout.Streets){
                // One reserved lot per block; authored road exits cut lots, never buildings.
                int sx=layout.StreetX+(int)Math.Floor((startX-layout.StreetX)/(float)ExteriorLayout.Block)*ExteriorLayout.Block;
                int sy=layout.StreetY+(int)Math.Floor((startY-layout.StreetY)/(float)ExteriorLayout.Block)*ExteriorLayout.Block;
                for(int y=sy;y<endY;y+=ExteriorLayout.Block)for(int x=sx;x<endX;x+=ExteriorLayout.Block){
                    int X=x+6,Y=y+6;if(!layout.LotAllowed(X,Y))continue;
                    var p=P(new V2(X,Y));if(p.X< -180||p.X>width+180||p.Y< -80||p.Y>height+180)continue;
                    props.Add(new ExteriorProp{X=X,Y=Y,Seed=ExteriorHash(X,Y),House=true});
                }
            }
            for(int y=startY;y<endY;y+=7)for(int x=startX;x<endX;x+=7){
                if(x>-6&&x<map.Columns+4&&y>-6&&y<map.Rows+4)continue;
                if(layout.Streets)continue;
                var p=P(new V2(x,y));if(p.X< -90||p.X>width+90||p.Y< -10||p.Y>height+90)continue;
                int seed=ExteriorHash(x,y);if(seed%5==0||!layout.PropAllowed(x,y))continue;
                bool house=layout.Style=="village"&&layout.TreesNear(x,y)<3&&seed%4==0&&layout.LotAllowed(x,y,6,6);
                props.Add(new ExteriorProp{X=x,Y=y,Seed=seed,House=house});
            }
            props.Sort((a,b)=>(a.Y-a.X).CompareTo(b.Y-b.X));
            foreach(var o in props)
            {
                int trees=layout.TreesNear(o.X,o.Y);
                bool building=o.House;
                if(!building&&trees==0&&layout.Style!="forest"&&layout.Style!="park"&&o.Seed%3!=0)continue;
                if(building)ExteriorHouse(o.X,o.Y,layout.Streets,o.Seed,layout.Style);
                else ExteriorTrees(o.X,o.Y,o.Seed,layout.Style=="park");
            }
            // Distance treatment applies only to the exterior image, before any playable tiles.
            for(int y=0;y<Canvas.Height;y++)for(int x=0;x<Canvas.Width;x++)
            {
                float sx=x/Canvas.Scale,sy=y/Canvas.Scale;
                float sum=(sx-width*.5f)/11f+(map.Columns+map.Rows)*.5f;
                float diff=(sy-height*.5f-10)/5f+(map.Rows-map.Columns)*.5f;
                float wx=(sum-diff)*.5f,wy=(sum+diff)*.5f;
                float distance=Math.Max(Math.Max(-wx,wx-map.Columns),Math.Max(-wy,wy-map.Rows));
                float f=Math.Min(.58f,.12f+Math.Max(0,distance)*.012f);
                int i=y*Canvas.Width+x;uint c=Canvas.Pixels[i];
                Canvas.Pixels[i]=C((int)((c&255)*(1-f)+18*f),(int)(((c>>8)&255)*(1-f)+27*f),(int)(((c>>16)&255)*(1-f)+32*f));
            }
        }
#endif

        void ExteriorHouse(int x,int y,bool industrial,int seed,string style)
        {
            string family=style=="village"&&seed%2==0?"logs":ExteriorSprites.Family(style);
            int damage=style=="ruins"?1+(seed%2):seed%3,angle=(seed/7)%4;
            string variant=industrial?(style=="industrial"||style=="depot"||style=="base"?"long":seed%2==0?"complex":"twofloor"):null;
            var sprite=ExteriorSprites.Load?.Invoke(variant!=null?"street-"+variant+"-"+(damage*4+angle):family+"-"+(damage*4+angle));
            if(sprite==null)return;
            var p=P(new V2(x,y));float scale=Canvas.Scale;
            float left=(p.X+sprite.Left)*scale,top=(p.Y+sprite.Top)*scale,w=sprite.Width*scale,h=sprite.Height*scale;
            int x0=Math.Max(0,(int)Math.Floor(left)),x1=Math.Min(Canvas.Width,(int)Math.Ceiling(left+w));
            int y0=Math.Max(0,(int)Math.Floor(top)),y1=Math.Min(Canvas.Height,(int)Math.Ceiling(top+h));
            for(int py=y0;py<y1;py++)for(int px=x0;px<x1;px++){
                int sx=Math.Min(sprite.PixelWidth-1,Math.Max(0,(int)((px+.5f-left)/w*sprite.PixelWidth))),sy=Math.Min(sprite.PixelHeight-1,Math.Max(0,(int)((py+.5f-top)/h*sprite.PixelHeight)));
                uint c=sprite.Pixels[sy*sprite.PixelWidth+sx];int a=(int)(c>>24);if(a==0)continue;
                int i=py*Canvas.Width+px;if(a==255){Canvas.Pixels[i]=c;continue;}uint old=Canvas.Pixels[i];
                Canvas.Pixels[i]=C(((int)(c&255)*a+(int)(old&255)*(255-a))/255,((int)((c>>8)&255)*a+(int)((old>>8)&255)*(255-a))/255,((int)((c>>16)&255)*a+(int)((old>>16)&255)*(255-a))/255);
            }
        }
        void ExteriorTrees(int x,int y,int seed,bool park)
        {
            int count=park?2:4;
            for(int t=0;t<count;t++)
            {
                int k=ExteriorHash(seed,t);var p=P(new V2(x+(k%31)/10f,y+((k/31)%31)/10f));
                var a=StudioCatalog.Find("legacy-"+(101+k%4),false);
                StudioDrawing.Draw(Canvas,new StudioPlaced{Asset=a,X=x,Y=y},p.X,p.Y);
            }
        }
    }
}
