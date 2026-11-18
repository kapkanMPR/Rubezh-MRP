using System;
using System.Collections;
using System.Collections.Generic;

namespace SilentOrders.Core
{
    // Structural precision stays 1/8 tile. Cells are bits of state, not objects or renderers.
    public sealed class RoofMask : ICollection<long>
    {
        readonly bool[] solid;
        public readonly int MinX, MinY, Width, Height;
        public int Count { get; private set; }
        public int Revision { get; private set; }
        public bool IsReadOnly => false;
        public int Capacity => solid.Length;

        public RoofMask(List<StructureRoof> source)
        {
            if (source.Count == 0) { solid = new bool[0]; return; }
            int right = int.MinValue, bottom = int.MinValue;
            int left = int.MaxValue, top = int.MaxValue;
            foreach (var cell in source)
            {
                left = Math.Min(left, cell.x); top = Math.Min(top, cell.y);
                right = Math.Max(right, cell.x); bottom = Math.Max(bottom, cell.y);
            }
            MinX = left; MinY = top; Width = checked(right-left+1); Height = checked(bottom-top+1);
            solid = new bool[checked(Width*Height)];
            foreach (var cell in source) Add(DynamicBuilding.Key(cell.x, cell.y));
        }
        public int Index(int x, int y)
        {
            x -= MinX; y -= MinY;
            return x < 0 || y < 0 || x >= Width || y >= Height ? -1 : y*Width+x;
        }
        public bool At(int x, int y) { int i=Index(x,y); return i>=0 && solid[i]; }
        public bool Contains(long key) => At((int)(key>>32), (int)key);
        public void Add(long key)
        {
            int i=Index((int)(key>>32),(int)key);
            if(i<0) throw new ArgumentOutOfRangeException(nameof(key), "Outside generated roof bounds");
            if(solid[i])return;
            solid[i]=true; Count++; Revision++;
        }
        public bool Remove(long key)
        {
            int i=Index((int)(key>>32),(int)key);
            if(i<0 || !solid[i])return false;
            solid[i]=false; Count--; Revision++; return true;
        }
        public void Clear() { if(Count==0)return; Array.Clear(solid,0,solid.Length);Count=0;Revision++; }
        public void RemoveWhere(Predicate<long> predicate)
        {
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                if(solid[y*Width+x] && predicate(DynamicBuilding.Key(MinX+x,MinY+y)))
                {solid[y*Width+x]=false;Count--;Revision++;}
        }
        public void CopyTo(long[] array,int index) { foreach(long key in this)array[index++]=key; }
        public IEnumerator<long> GetEnumerator()
        {
            for(int y=0;y<Height;y++)for(int x=0;x<Width;x++)
                if(solid[y*Width+x])yield return DynamicBuilding.Key(MinX+x,MinY+y);
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct RoofSample { public int x,y; public float a,b,c,d; }
    public struct RoofPanel
    {
        public int LeftTop,RightTop,LeftBottom,RightBottom,Y,H;
        public float A,B,C,D,Shade;
    }

    // Large generated surfaces, subdivided only where the imported roof shape
    // needs detail. Physics always samples the unchanged original heights.
    public sealed class ProceduralRoof
    {
        public const float VisualTolerance=.02f;
        struct Heights { public float A,B,C,D; }
        readonly RoofMask mask;
        readonly Heights[] heights;
        readonly bool[] present;
        readonly List<RoofPanel> panels=new List<RoofPanel>();
        int panelRevision=-1;
        public long StorageBytes => (long)mask.Capacity*18;
        public int RebuildCount { get; private set; }
        public int CachedPanelCount => panels.Count;

        public ProceduralRoof(List<StructureRoof> source, RoofMask mask)
        {
            this.mask=mask;heights=new Heights[mask.Capacity];present=new bool[mask.Capacity];
            foreach(var cell in source){
                int index=mask.Index(cell.x,cell.y);present[index]=true;
                heights[index]=new Heights{A=cell.a,B=cell.b,C=cell.c,D=cell.d};
            }
        }
        public bool TryGetValue(long key,out RoofSample sample)
        {
            int x=(int)(key>>32),y=(int)key,index=mask.Index(x,y);
            if(index<0 || !present[index]){sample=default(RoofSample);return false;}
            var h=heights[index];sample=new RoofSample{x=x,y=y,a=h.A,b=h.B,c=h.C,d=h.D};return true;
        }
        Heights Visual(int x,int y)
        {
            var h=heights[mask.Index(x,y)];
            return h.A>0?h:new Heights{A=2.8f,B=2.8f,C=2.8f,D=2.8f};
        }
        static float Height(Heights h,float u,float v)
            => u>=v?h.A+(h.B-h.A)*u+(h.C-h.B)*v:h.A+(h.C-h.D)*u+(h.D-h.A)*v;
        public List<RoofPanel> Panels
        {
            get {
                if(panelRevision!=mask.Revision){
                    panels.Clear();
                    foreach(var rect in RoofMesh.Merge(mask))Generate(rect);
                    panelRevision=mask.Revision;RebuildCount++;
                }
                return panels;
            }
        }
        void Generate(RoofRect r)
        {
            var corners=new Heights{A=Visual(r.X,r.Y).A,B=Visual(r.X+r.W-1,r.Y).B,
                C=Visual(r.X+r.W-1,r.Y+r.H-1).C,D=Visual(r.X,r.Y+r.H-1).D};
            if((r.W==1&&r.H==1)||Fits(r,corners)){
                float shade=Math.Max(.65f,Math.Min(1f,.88f+((corners.B-corners.A)*8/r.W-(corners.D-corners.A)*8/r.H)*.08f));
                panels.Add(new RoofPanel{LeftTop=r.X,RightTop=r.X+r.W,LeftBottom=r.X,RightBottom=r.X+r.W,Y=r.Y,H=r.H,A=corners.A,B=corners.B,C=corners.C,D=corners.D,Shade=shade});
                return;
            }
            if(r.W>=r.H&&r.W>1){
                int half=r.W/2;Generate(new RoofRect(r.X,r.Y,half,r.H));Generate(new RoofRect(r.X+half,r.Y,r.W-half,r.H));
            }else{
                int half=r.H/2;Generate(new RoofRect(r.X,r.Y,r.W,half));Generate(new RoofRect(r.X,r.Y+half,r.W,r.H-half));
            }
        }
        bool Fits(RoofRect r,Heights corners)
        {
            for(int y=0;y<r.H;y++)for(int x=0;x<r.W;x++){
                var h=Visual(r.X+x,r.Y+y);
                if(!Close(r,corners,x,y,h.A)||!Close(r,corners,x+1,y,h.B)||
                   !Close(r,corners,x+1,y+1,h.C)||!Close(r,corners,x,y+1,h.D))return false;
                // Intersections of the candidate diagonal and the two original
                // triangles are the other extrema of their piecewise-linear difference.
                float atTop=y*r.W/(float)r.H-x,atBottom=(y+1)*r.W/(float)r.H-x;
                if(!Edge(r,corners,h,x,y,atTop,0)||!Edge(r,corners,h,x,y,atBottom,1))return false;
                float atLeft=x*r.H/(float)r.W-y,atRight=(x+1)*r.H/(float)r.W-y;
                if(!Edge(r,corners,h,x,y,0,atLeft)||!Edge(r,corners,h,x,y,1,atRight))return false;
                if(r.W!=r.H){
                    float t=(y*(float)r.W-x*(float)r.H)/(r.H-r.W);
                    if(!Edge(r,corners,h,x,y,t,t))return false;
                }
            }
            return true;
        }
        static bool Edge(RoofRect r,Heights corners,Heights h,int x,int y,float u,float v)
            =>u<0||u>1||v<0||v>1||Close(r,corners,x+u,y+v,Height(h,u,v));
        static bool Close(RoofRect r,Heights h,float x,float y,float expected)
            =>Math.Abs(Height(h,x/r.W,y/r.H)-expected)<=VisualTolerance;
    }
}
