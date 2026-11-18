using System;
using System.Collections.Generic;
using System.Linq;
namespace SilentOrders.Core
{
    [Serializable] public sealed class StructureSpan { public float lo,hi; }
    [Serializable] public sealed class StructureColumn { public List<StructureSpan> spans=new List<StructureSpan>(); }
    [Serializable] public sealed class StructureWall {
        public string id,material;public float ax,ay,bx,by,vax,vay,vbx,vby,height,thickness,sill,lintel,windowLo,windowHi;
        public bool window;public List<StructureColumn> columns=new List<StructureColumn>();
    }
    [Serializable] public sealed class StructureClosure {public int kind,anchorX,anchorY;public float[] xyz;}
    [Serializable] public sealed class StructureRoof {public int x,y;public float a,b,c,d;}
    [Serializable] public sealed class StructureFloor {public float x,y;}
    public sealed class StructureLayer {public int Wall;public ProceduralPicture Original;}
    public sealed class DynamicBuilding
    {
        public sealed class Wall {
            public StructureWall Source;public bool[] Solid,Initial;public int NX,NZ;public bool GlassBroken;public int Revision;
            public Wall(StructureWall s){Source=s;NX=s.columns.Count;NZ=Math.Max(1,(int)Math.Ceiling(s.height/.1f));Solid=new bool[NX*NZ];
                for(int x=0;x<NX;x++)for(int z=0;z<NZ;z++){float h=(z+.5f)*s.height/NZ;Solid[z*NX+x]=s.columns[x].spans.Any(v=>h>=v.lo&&h<=v.hi);}Initial=(bool[])Solid.Clone();}
            public V2 Point(float u)=>new V2(Source.ax+(Source.bx-Source.ax)*u,Source.ay+(Source.by-Source.ay)*u);
            public float U(V2 p){var d=new V2(Source.bx-Source.ax,Source.by-Source.ay);return ((p.X-Source.ax)*d.X+(p.Y-Source.ay)*d.Y)/Math.Max(.00001f,d.X*d.X+d.Y*d.Y);}
            public bool At(float u,float z){int x=Math.Min(NX-1,Math.Max(0,(int)(u*NX))),y=Math.Min(NZ-1,Math.Max(0,(int)(z/Source.height*NZ)));return u>=-.04f&&u<=1.04f&&z>=-.05f&&z<=Source.height+.05f&&Solid[y*NX+x];}
        }
        public List<StructureClosure> Closures=new List<StructureClosure>();
        public float RoofThickness=.165f;
        public readonly ProceduralRoof RoofShape;
        public readonly List<Wall> Walls;public readonly RoofMask Roof;
        int meshRoofCount=-1;List<RoofRect> roofRects;public List<RoofRect> RoofRects {get {if(roofRects==null||meshRoofCount!=Roof.Revision){roofRects=RoofMesh.Merge(Roof);meshRoofCount=Roof.Revision;}return roofRects;}}
        readonly HashSet<long> roofArea=new HashSet<long>();readonly List<StructureFloor> floors;
        readonly Dictionary<long,List<StructureFloor>> floorBins=new Dictionary<long,List<StructureFloor>>();
        int[] recordedWallVersions;readonly List<int>[] endNeighbors;
        readonly List<StructureLayer> layers;public ProceduralPicture[] Pictures;
        readonly Dictionary<StructureLayer,Tuple<int,ProceduralPicture>> rendered=new Dictionary<StructureLayer,Tuple<int,ProceduralPicture>>();
        readonly Dictionary<Wall,Tuple<int,ProceduralPicture>> capsCache=new Dictionary<Wall,Tuple<int,ProceduralPicture>>();
        public static long Key(int x,int y)=>((long)x<<32)|(uint)y;
        static int X(long k)=>(int)(k>>32);static int Y(long k)=>(int)k;
        static readonly int[] dx={1,-1,0,0},dy={0,0,1,-1};
        public DynamicBuilding(List<StructureWall> walls,List<StructureRoof> roof,List<StructureFloor> floor,List<StructureLayer> images){Walls=walls.Select(s=>new Wall(s)).ToList();Roof=new RoofMask(roof);RoofShape=new ProceduralRoof(roof,Roof);foreach(var r in roof)roofArea.Add(Key(r.x/8,r.y/8));floors=floor;
            foreach(var f in floor)for(int y=(int)Math.Floor(f.y*2);y<=(int)Math.Floor((f.y+.5f)*2);y++)for(int x=(int)Math.Floor(f.x*2);x<=(int)Math.Floor((f.x+.5f)*2);x++){
                long k=Key(x,y);List<StructureFloor> bin;if(!floorBins.TryGetValue(k,out bin))floorBins[k]=bin=new List<StructureFloor>();bin.Add(f);
            }
            endNeighbors=new List<int>[Walls.Count*2];var endpoints=new Dictionary<long,List<int>>();
            for(int w=0;w<Walls.Count;w++)for(int e=0;e<2;e++){var p=Walls[w].Point(e);long k=Key((int)Math.Floor(p.X*1000),(int)Math.Floor(p.Y*1000));List<int> bin;if(!endpoints.TryGetValue(k,out bin))endpoints[k]=bin=new List<int>();bin.Add(w*2+e);}
            for(int w=0;w<Walls.Count;w++)for(int e=0;e<2;e++){var near=endNeighbors[w*2+e]=new List<int>();var p=Walls[w].Point(e);int x=(int)Math.Floor(p.X*1000),y=(int)Math.Floor(p.Y*1000);for(int yy=y-1;yy<=y+1;yy++)for(int xx=x-1;xx<=x+1;xx++){List<int> bin;if(!endpoints.TryGetValue(Key(xx,yy),out bin))continue;foreach(int link in bin)if(link/2!=w&&V2.Distance(p,Walls[link/2].Point(link%2))<.001f)near.Add(link);}}
            layers=images;foreach(var layer in layers)if(layer.Wall>=0&&!string.IsNullOrEmpty(layer.Original.NativeKey))layer.Original.NativeWall=Walls[layer.Wall];Pictures=images.Select(l=>l.Original).ToArray();}
        public float RoofHeight(V2 p) {
            int x=(int)Math.Floor(p.X*8),y=(int)Math.Floor(p.Y*8);RoofSample r;
            if(!RoofShape.TryGetValue(Key(x,y),out r)||r.a<=0)return RoofRules.Height;
            float u=p.X*8-x,v=p.Y*8-y;
            return u>=v?r.a+(r.b-r.a)*u+(r.c-r.b)*v:r.a+(r.c-r.d)*u+(r.d-r.a)*v;
        }
        public bool Contains(V2 p){List<StructureFloor> bin;if(!floorBins.TryGetValue(Key((int)Math.Floor(p.X*2),(int)Math.Floor(p.Y*2)),out bin))return false;foreach(var f in bin)if(p.X>=f.x&&p.X<f.x+.5f&&p.Y>=f.y&&p.Y<f.y+.5f)return true;return false;}
        public bool Breakable(V2 p)=>Walls.Any(w=>V2.Distance(w.Point(Util.Clamp(w.U(p),0,1)),p)<.3f);
        // Deterministic jagged edge. No global RNG is consumed by cosmetics.
        static float Noise(int x,int y,int salt){uint n=unchecked((uint)(x*73856093^y*19349663^salt*83492791));n^=n>>13;n*=1274126177;return (n&65535)/65535f;}
        // Angular lobes keep one connected breach, with large chips rather than
        // independent pixel noise. The same impact seed reproduces the contour.
        static float BreachEdge(float across, float up, int salt)
        {
            double angle = Math.Atan2(up, across);
            double phase = Noise(17, 29, salt) * Math.PI * 2;
            return .98f + .18f * (float)Math.Sin(angle * 3 + phase)
                + .13f * (float)Math.Sin(angle * 5 - phase * 1.7)
                + .09f * (float)Math.Sin(angle * 9 + phase * .7);
        }
        public bool Blast(BattleMap map,V2 p,float radius,int kind,bool roofHit,V2 incoming,int salt,float impactHeight=1.35f,float destructionScale=1f)
        {
            bool changed=false,wallChanged=false,roofChanged=false;int debris=0;bool inside=Contains(p)&&!Walls.Any(w=>V2.Distance(w.Point(Util.Clamp(w.U(p),0,1)),p)<.035f&&w.At(w.U(p),impactHeight));
            V2 impact=p;float nearest=float.MaxValue;
            foreach(var wall in Walls){var q=wall.Point(Util.Clamp(wall.U(p),0,1));float d=V2.Distance(q,p);if(d<nearest){nearest=d;impact=q;}}
            float strength=Util.Clamp(1-nearest/Math.Max(.01f,radius),0,1);
            if((!roofHit||destructionScale>1f)&&strength>0)foreach(var w in Walls){bool hit=false;float factor=kind==2?(float)Math.Sqrt(1.5):1f;
                // sqrt(1.5) per axis gives approximately 1.5 times the area.
                float size = 1.2247449f * destructionScale;
                float horizontal=(kind==0?.16f:.28f*factor)*(float)Math.Sqrt(strength)*size,vertical=(kind==0?.13f:.85f*factor)*strength*size;
                for(int x=0;x<w.NX;x++){var q=w.Point((x+.5f)/w.NX);float d=V2.Distance(q,impact);if(d>horizontal*1.5f)continue;
                    int nx=(int)Math.Floor(q.X*12),ny=(int)Math.Floor(q.Y*12);
                    float center=impactHeight+(Noise(nx/3,ny/3,salt+19)-.5f)*.15f*strength;
                    for(int z=0;z<w.NZ;z++){int i=z*w.NX+x;if(!w.Solid[i])continue;float h=(z+.5f)*w.Source.height/w.NZ;
                        float across=(w.U(q)-w.U(impact))*V2.Distance(w.Point(0),w.Point(1))/horizontal;
                        float edge=BreachEdge(across,(h-center)/vertical,salt);
                        float ragged=edge*edge;
                        float shape=d*d/(horizontal*horizontal)+(h-center)*(h-center)/(vertical*vertical);
                        if(shape<ragged){w.Solid[i]=false;hit=changed=wallChanged=true;}}
                }
                if(hit){w.Revision++;if(w.Source.window&&!w.GlassBroken)Emit(map,w.Point(.5f),p,Outward(w),"glass",true,ref debris);w.GlassBroken=true;Emit(map,w.Point(Util.Clamp(w.U(impact),0,1)),p,incoming,w.Source.material,false,ref debris);}
            }
            float roofRadius=roofHit?radius:inside&&kind==1?.65f:kind==2?radius:0;
            if(roofRadius>0){for(int ry=(int)Math.Floor((p.Y-roofRadius)*8);ry<=(int)Math.Ceiling((p.Y+roofRadius)*8);ry++)for(int rx=(int)Math.Floor((p.X-roofRadius)*8);rx<=(int)Math.Ceiling((p.X+roofRadius)*8);rx++){long k=Key(rx,ry);if(!Roof.Contains(k))continue;var q=new V2((X(k)+.5f)/8f,(Y(k)+.5f)/8f);float r=roofRadius*(.88f+.12f*Noise(X(k)/2,Y(k)/2,salt));if(V2.Distance(p,q)<=r){Roof.Remove(k);changed=roofChanged=true;if(debris<18&&Noise(X(k),Y(k),salt)>.93f)Emit(map,q,p,incoming,"concrete",false,ref debris);}}}
            if(inside&&!roofHit)foreach(var w in Walls)if(w.Source.window&&!w.GlassBroken&&V2.Distance(w.Point(.5f),p)<=10){w.Revision++;w.GlassBroken=true;changed=true;Emit(map,w.Point(.5f),p,Outward(w),"glass",true,ref debris);}
            if(changed){var timer=System.Diagnostics.Stopwatch.StartNew();if(wallChanged)CollapseWalls(map,p,incoming,ref debris);DynamicStructures.LastWallCollapseMs+=timer.Elapsed.TotalMilliseconds;timer.Restart();if(wallChanged||roofChanged)CollapseRoof();DynamicStructures.LastRoofCollapseMs+=timer.Elapsed.TotalMilliseconds;timer.Restart();Refresh(map,wallChanged||roofChanged);DynamicStructures.LastRefreshMs+=timer.Elapsed.TotalMilliseconds;}return changed;
        }
        V2 Outward(Wall w){var d=w.Point(1)-w.Point(0);var n=new V2(-d.Y,d.X).Normalized;return Contains(w.Point(.5f)+n*.2f)?n*-1:n;}
        static void Emit(BattleMap map,V2 q,V2 source,V2 incoming,string material,bool glass,ref int count){if(count++>=24)return;V2 axis=glass?incoming:q-source;if(axis.Length<.001f)axis=incoming;if(axis.Length<.001f)axis=new V2(1,0);int cell=map.Index(q);map.BrokenCovers.Add(new CoverBreakEvent{Index=cell,RemnantKey=cell,HasCenter=true,Center=q,Source=source,Direction=axis,MaxReach=3,MaxPieces=glass?4:5,Blast=true,Destroyed=false,DebrisKey=glass?"glass":(material=="wood"||material=="logs"||material=="burnt-wood"||material=="burnt-logs")?"planks":material=="brick"?"red-brick":"concrete",FanDegrees=100});}
        bool[][] collapseSeen;
        readonly Queue<long> collapseQueue=new Queue<long>();
        void MarkSupported(int wall,int cell) {
            if(Walls[wall].Solid[cell]&&!collapseSeen[wall][cell]) {
                collapseSeen[wall][cell]=true;
                collapseQueue.Enqueue(((long)wall<<32)|(uint)cell);
            }
        }
        void CollapseWalls(BattleMap map,V2 source,V2 incoming,ref int debris)
        {
            if(collapseSeen==null||collapseSeen.Length!=Walls.Count)collapseSeen=new bool[Walls.Count][];
            for(int i=0;i<Walls.Count;i++){if(collapseSeen[i]==null||collapseSeen[i].Length!=Walls[i].Solid.Length)collapseSeen[i]=new bool[Walls[i].Solid.Length];else Array.Clear(collapseSeen[i],0,collapseSeen[i].Length);}
            var seen=collapseSeen;var q=collapseQueue;q.Clear();
            for(int w=0;w<Walls.Count;w++)for(int x=0;x<Walls[w].NX;x++)MarkSupported(w,x);
            while(q.Count>0){long t=q.Dequeue();int wi=(int)(t>>32),cell=(int)t;var w=Walls[wi];int x=cell%w.NX,z=cell/w.NX;
                if(x>0)MarkSupported(wi,cell-1);if(x+1<w.NX)MarkSupported(wi,cell+1);if(z>0)MarkSupported(wi,cell-w.NX);if(z+1<w.NZ)MarkSupported(wi,cell+w.NX);
                if(x==0||x==w.NX-1){float height=(z+.5f)*w.Source.height/w.NZ;foreach(int link in endNeighbors[wi*2+(x==0?0:1)]){int n=link/2;var other=Walls[n];int zz=(int)(height/other.Source.height*other.NZ);if(zz<0||zz>=other.NZ)continue;MarkSupported(n,zz*other.NX+(link%2==0?0:other.NX-1));}}
            }
            for(int w=0;w<Walls.Count;w++){bool lost=false;for(int i=0;i<seen[w].Length;i++)if(Walls[w].Solid[i]&&!seen[w][i]){Walls[w].Solid[i]=false;lost=true;}if(lost){Walls[w].Revision++;Emit(map,Walls[w].Point(.5f),source,incoming,Walls[w].Source.material,false,ref debris);}}
        }
        int supportedRoofRevision=-1;
        HashSet<long> supportedRoofAnchors;
        public int RoofSupportRebuilds {get;private set;}
        public int RoofSupportCacheHits {get;private set;}
        void CollapseRoof(){
            var anchors=new HashSet<long>();
            foreach(var w in Walls){var a=w.Point(0);var b=w.Point(1);for(int y=(int)Math.Floor((Math.Min(a.Y,b.Y)-.21f)*8);y<=(int)Math.Floor((Math.Max(a.Y,b.Y)+.21f)*8);y++)for(int x=(int)Math.Floor((Math.Min(a.X,b.X)-.21f)*8);x<=(int)Math.Floor((Math.Max(a.X,b.X)+.21f)*8);x++){long k=Key(x,y);if(!Roof.Contains(k))continue;var p=new V2((x+.5f)/8,(y+.5f)/8);float u=Util.Clamp(w.U(p),0,1);if(V2.Distance(w.Point(u),p)<.21f&&w.At(u,w.Source.height-.04f))anchors.Add(k);}}
            // A wall chip below its top does not change the roof support graph.
            if(supportedRoofRevision==Roof.Revision && supportedRoofAnchors!=null && supportedRoofAnchors.SetEquals(anchors)){RoofSupportCacheHits++;return;}
            RoofSupport.Collapse(Roof,anchors);RoofSupportRebuilds++;
            supportedRoofRevision=Roof.Revision;supportedRoofAnchors=anchors;
        }
        public bool BreakGlass(BattleMap map,V2 a,float az,V2 b,float bz){bool any=false;int debris=0;foreach(var w in Walls){if(!w.Source.window||w.GlassBroken)continue;var d=b-a;var e=w.Point(1)-w.Point(0);float den=d.X*e.Y-d.Y*e.X;if(Math.Abs(den)<.00001f)continue;var v=w.Point(0)-a;float t=(v.X*e.Y-v.Y*e.X)/den,u=(v.X*d.Y-v.Y*d.X)/den,z=az+(bz-az)*t;if(t>=0&&t<=1&&u>=w.Source.windowLo&&u<=w.Source.windowHi&&z>=w.Source.sill&&z<=w.Source.lintel){w.Revision++;w.GlassBroken=true;any=true;Emit(map,w.Point(u),a,d,"glass",true,ref debris);}}if(any)Refresh(map,false);return any;}
        public void Refresh(BattleMap map,bool structural=true){
            RebuildPictures();if(structural)RebuildCells(map);
            if(recordedWallVersions==null)recordedWallVersions=new int[Walls.Count];var roots=new HashSet<int>();
            for(int i=0;i<Walls.Count;i++)if(recordedWallVersions[i]!=Walls[i].Revision){recordedWallVersions[i]=Walls[i].Revision;var p=Walls[i].Point(.5f);if(map.Inside((int)Math.Floor(p.X),(int)Math.Floor(p.Y)))roots.Add(map.Index(p));}
            map.SceneryChanges.Record(map,roots.ToArray());
        }
        void RebuildCells(BattleMap map){
            var grouped=new Dictionary<int,List<Wall>>();foreach(var w in Walls){var p=w.Point(.5f);if(!map.Inside((int)Math.Floor(p.X),(int)Math.Floor(p.Y)))continue;int i=map.Index(p);if(!grouped.ContainsKey(i))grouped[i]=new List<Wall>();grouped[i].Add(w);}
            foreach(var pair in grouped){int rank=0;float height=0;bool walk=true;foreach(var w in pair.Value){float max=0;int gap=0,best=0,shot=0,shotBest=0,floorShot=0,floorBest=0;for(int x=0;x<w.NX;x++){float top=0,bottom=w.Source.height;bool found=false;for(int z=0;z<w.NZ;z++)if(w.Solid[z*w.NX+x]){top=(z+1)*w.Source.height/w.NZ;if(!found){bottom=z*w.Source.height/w.NZ;found=true;}}max=Math.Max(max,top);floorShot=bottom>=.9f?floorShot+1:0;floorBest=Math.Max(floorBest,floorShot);gap=bottom>=2.1f?gap+1:0;best=Math.Max(best,gap);bool openBand=true;for(int z=0;z<w.NZ;z++){float h=(z+.5f)*w.Source.height/w.NZ;if(h>=1&&h<=1.5f&&w.Solid[z*w.NX+x])openBand=false;}shot=openBand?shot+1:0;shotBest=Math.Max(shotBest,shot);}
                    float length=V2.Distance(w.Point(0),w.Point(1));bool pass=best*length/w.NX>=.35f;bool fire=shotBest*length/w.NX>=.35f;int r=max<.15f||pass||floorBest*length/w.NX>=.35f?0:max<=.95f?1:fire?2:3;rank=Math.Max(rank,r);height=Math.Max(height,r==2?.9f:max);walk&=pass||max<.15f;}
                int i=pair.Key;if(!map.ProceduralCells[i])continue;map.ProceduralProtection[i]=rank==3?.75f:rank==2?.65f:rank==1?.5f:0;map.ProceduralHeight[i]=rank==0?0:height;map.Tiles[i]=walk?Tile.Ground:rank==3?Tile.Wall:Tile.LowCover;
            }
            foreach(var k in roofArea){int x=X(k),y=Y(k);if(map.Inside(x,y))map.Roof[y*map.Columns+x]=false;}foreach(var k in Roof){int x=X(k)/8,y=Y(k)/8;if(map.Inside(x,y))map.Roof[y*map.Columns+x]=true;}
        }
        void RebuildPictures(){
            var result=new List<ProceduralPicture>();foreach(var l in layers){if(l.Wall<0){result.Add(l.Original);continue;}var w=Walls[l.Wall];if(!string.IsNullOrEmpty(l.Original.NativeKey)){result.Add(l.Original);continue;}Tuple<int,ProceduralPicture> cached;if(rendered.TryGetValue(l,out cached)&&cached.Item1==w.Revision){result.Add(cached.Item2);continue;}if(w.Revision==0){result.Add(l.Original);continue;}var s=w.Source;var o=l.Original;var image=new ProceduralPicture{Left=o.Left,Top=o.Top,Width=o.Width,Height=o.Height,PixelWidth=o.PixelWidth,PixelHeight=o.PixelHeight,Pixels=(uint[])o.Pixels.Clone(),Depth=o.Depth};
                for(int y=0;y<o.PixelHeight;y++)for(int x=0;x<o.PixelWidth;x++){int i=y*o.PixelWidth+x;if((image.Pixels[i]>>24)==0)continue;float px=o.Left+(x+.5f)*o.Width/o.PixelWidth,py=o.Top+(y+.5f)*o.Height/o.PixelHeight;float u=(px/11-s.vax-s.vay)/(s.vbx-s.vax+s.vby-s.vay);var p=new V2(s.vax+(s.vbx-s.vax)*u,s.vay+(s.vby-s.vay)*u);float z=(5*(p.Y-p.X)-py)*2.8f/24;bool glass=s.window&&u>=s.windowLo&&u<=s.windowHi&&z>=s.sill&&z<=s.lintel;
                    int ix=Math.Min(w.NX-1,Math.Max(0,(int)(u*w.NX))),iz=Math.Min(w.NZ-1,Math.Max(0,(int)(z/s.height*w.NZ)));bool removed=w.Initial[iz*w.NX+ix]&&!w.Solid[iz*w.NX+ix];
                    if(!w.Solid[iz*w.NX+ix]&&!removed){bool supported=false;for(int zz=Math.Max(0,iz-1);zz<=Math.Min(w.NZ-1,iz+1);zz++)for(int xx=Math.Max(0,ix-1);xx<=Math.Min(w.NX-1,ix+1);xx++)supported|=w.Solid[zz*w.NX+xx];removed=!supported;}
                    if(glass?w.GlassBroken:removed)image.Pixels[i]=0;
                }rendered[l]=Tuple.Create(w.Revision,image);result.Add(image);
            }foreach(var w in Walls){if(w.Revision==0)continue;Tuple<int,ProceduralPicture> cached;if(!capsCache.TryGetValue(w,out cached)||cached.Item1!=w.Revision){cached=Tuple.Create(w.Revision,CutCaps(w,layers.Any(l=>!string.IsNullOrEmpty(l.Original.NativeKey))));capsCache[w]=cached;}if(cached.Item2!=null)result.Add(cached.Item2);}Pictures=result.ToArray();
        }
        static ProceduralPicture CutCaps(Wall w,bool native=false){
            var faces=new List<float[]>();var axis=w.Point(1)-w.Point(0);var n=new V2(-axis.Y,axis.X).Normalized*(w.Source.thickness/3);
            Action<float,float,float,float> edge=(u0,z0,u1,z1)=>{var a=w.Point(u0);var b=w.Point(u1);faces.Add(new[]{a.X+n.X,a.Y+n.Y,z0,b.X+n.X,b.Y+n.Y,z1,b.X-n.X,b.Y-n.Y,z1,a.X-n.X,a.Y-n.Y,z0});};
            for(int z=0;z<w.NZ;z++)for(int x=0;x<w.NX;x++){int i=z*w.NX+x;if(!w.Solid[i])continue;float u=x/(float)w.NX,v=(x+1f)/w.NX,lo=z*w.Source.height/w.NZ,hi=(z+1f)*w.Source.height/w.NZ;
                if(x>0&&w.Initial[i-1]&&!w.Solid[i-1])edge(u,lo,u,hi);if(x+1<w.NX&&w.Initial[i+1]&&!w.Solid[i+1])edge(v,lo,v,hi);
                if(z>0&&w.Initial[i-w.NX]&&!w.Solid[i-w.NX])edge(u,lo,v,lo);if(z+1<w.NZ&&w.Initial[i+w.NX]&&!w.Solid[i+w.NX])edge(u,hi,v,hi);
            }if(faces.Count==0)return null;
            var projected=faces.Select(f=>{var r=new float[12];for(int i=0;i<4;i++){r[i*3]=11*(f[i*3]+f[i*3+1]);r[i*3+1]=5*(f[i*3+1]-f[i*3])-f[i*3+2]*24/2.8f;r[i*3+2]=256+f[i*3+1]-f[i*3]+.012f;}return r;}).ToArray();
            float left=(float)Math.Floor(projected.Min(f=>Math.Min(Math.Min(f[0],f[3]),Math.Min(f[6],f[9]))))-1,top=(float)Math.Floor(projected.Min(f=>Math.Min(Math.Min(f[1],f[4]),Math.Min(f[7],f[10]))))-1;
            float width=(float)Math.Ceiling(projected.Max(f=>Math.Max(Math.Max(f[0],f[3]),Math.Max(f[6],f[9]))))-left+1,height=(float)Math.Ceiling(projected.Max(f=>Math.Max(Math.Max(f[1],f[4]),Math.Max(f[7],f[10]))))-top+1;
            if(native)return new ProceduralPicture{Left=left,Top=top,Width=width,Height=height,PixelWidth=1,PixelHeight=1,NativeFaces=projected,NativeColor=(w.Source.material=="wood"||w.Source.material=="logs"||w.Source.material=="burnt-wood"||w.Source.material=="burnt-logs")?0xff788b95u:0xff8d9da2u};
            int pw=(int)Math.Ceiling(width*4.8f),ph=(int)Math.Ceiling(height*4.8f);var image=new ProceduralPicture{Left=left,Top=top,Width=width,Height=height,PixelWidth=pw,PixelHeight=ph,Pixels=new uint[pw*ph],Depth=new float[pw*ph]};
            foreach(var f in projected)foreach(var tri in new[]{new[]{0,1,2},new[]{0,2,3}}){int a=tri[0]*3,b=tri[1]*3,c=tri[2]*3;float den=(f[b+1]-f[c+1])*(f[a]-f[c])+(f[c]-f[b])*(f[a+1]-f[c+1]);if(Math.Abs(den)<.000001f)continue;
                int x0=Math.Max(0,(int)((Math.Min(f[a],Math.Min(f[b],f[c]))-left)/width*pw)),x1=Math.Min(pw-1,(int)Math.Ceiling((Math.Max(f[a],Math.Max(f[b],f[c]))-left)/width*pw));
                int y0=Math.Max(0,(int)((Math.Min(f[a+1],Math.Min(f[b+1],f[c+1]))-top)/height*ph)),y1=Math.Min(ph-1,(int)Math.Ceiling((Math.Max(f[a+1],Math.Max(f[b+1],f[c+1]))-top)/height*ph));
                for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){float xx=left+(x+.5f)*width/pw,yy=top+(y+.5f)*height/ph,aa=((f[b+1]-f[c+1])*(xx-f[c])+(f[c]-f[b])*(yy-f[c+1]))/den,bb=((f[c+1]-f[a+1])*(xx-f[c])+(f[a]-f[c])*(yy-f[c+1]))/den,cc=1-aa-bb;if(aa<-.001f||bb<-.001f||cc<-.001f)continue;int i=y*pw+x;float dep=aa*f[a+2]+bb*f[b+2]+cc*f[c+2];if(dep<image.Depth[i])continue;image.Depth[i]=dep;image.Pixels[i]=(w.Source.material=="wood"||w.Source.material=="logs"||w.Source.material=="burnt-wood"||w.Source.material=="burnt-logs")?0xff788b95u:0xff8d9da2u;}
            }return image;
        }
    }
    public static class DynamicStructures
    {
        public static double LastWallCollapseMs,LastRoofCollapseMs,LastRefreshMs;public static double LastBlastMilliseconds,MaxBlastMilliseconds;public static int BlastCount;
        public static bool Owns(BattleMap m,int cell)=>m.DynamicBuildings!=null&&m.ProceduralCells!=null&&cell>=0&&cell<m.ProceduralCells.Length&&m.ProceduralCells[cell];
        public static V2 Normal(BattleMap m,V2 hit,V2 incoming){if(m.DynamicBuildings!=null)foreach(var h in m.DynamicBuildings)foreach(var w in h.Walls){float u=w.U(hit);if(u>=-.001f&&u<=1.001f&&V2.Distance(w.Point(u),hit)<.001f){var d=w.Point(1)-w.Point(0);var n=new V2(-d.Y,d.X).Normalized;return V2.Dot(n,incoming)>0?n*-1:n;}}return incoming.Normalized*-1;}
        public static bool Ray(BattleMap m,V2 a,float az,V2 end,float ez,out V2 hit,out float fraction){
            hit=end;fraction=1;bool found=false;if(m.DynamicBuildings==null)return false;var d=end-a;
            foreach(var w in DynamicWallIndex.For(m).Query(a,end)){
                var p=w.Point(0);var q=w.Point(1);float pad=Math.Max(.025f,w.Source.thickness/3);
                if(Math.Max(a.X,end.X)<Math.Min(p.X,q.X)-pad||Math.Min(a.X,end.X)>Math.Max(p.X,q.X)+pad||Math.Max(a.Y,end.Y)<Math.Min(p.Y,q.Y)-pad||Math.Min(a.Y,end.Y)>Math.Max(p.Y,q.Y)+pad)continue;
                var e=q-p;float den=d.X*e.Y-d.Y*e.X;
                if(Math.Abs(den)<.000001f){
                    if(Math.Abs(e.X*(a.Y-p.Y)-e.Y*(a.X-p.X))/Math.Max(.00001f,e.Length)>pad)continue;
                    float u0=w.U(a),u1=w.U(end),du=u1-u0;
                    float begin=.00002f,finish=fraction;if(Math.Abs(du)>.000001f){begin=Math.Max(begin,Math.Min((0-u0)/du,(1-u0)/du));finish=Math.Min(finish,Math.Max((0-u0)/du,(1-u0)/du));}
                    for(int i=0;i<=w.NX&&begin<=finish;i++){float t=begin+(finish-begin)*i/w.NX;if(w.At(u0+du*t,az+(ez-az)*t)){fraction=t;hit=a+d*t;found=true;break;}}continue;
                }
                var v=p-a;float tt=(v.X*e.Y-v.Y*e.X)/den,u=(v.X*d.Y-v.Y*d.X)/den;
                if(tt>.00001f&&tt<=fraction&&u>=0&&u<=1&&w.At(u,az+(ez-az)*tt)){fraction=tt;hit=a+d*tt;found=true;}
            }return found;
        }
        public static bool Material(BattleMap m,V2 p,float height){if(m.DynamicBuildings==null)return false;foreach(var w in DynamicWallIndex.For(m).Query(p,p)){float u=w.U(p);if(u>=0&&u<=1&&V2.Distance(w.Point(u),p)<=Math.Max(.025f,w.Source.thickness/3)&&w.At(u,height))return true;}return false;}
        // Two uniform samples give a triangular distribution: all heights remain
        // possible, while 75% of shots lie in the middle half of the wall.
        public static float SampleWallHeight(Rng random,float height)=>height*.5f*(random.Value()+random.Value());
        public static bool RocketHeight(BattleMap map,V2 from,V2 target,Rng random,out float endHeight){
            endHeight=.75f;V2 hit;float t;if(!Ray(map,from,1.55f,target,.75f,out hit,out t)||t<.001f)return false;
            foreach(var h in map.DynamicBuildings)foreach(var w in h.Walls){float u=w.U(hit);if(u<0||u>1||V2.Distance(w.Point(u),hit)>.001f||!w.At(u,1.55f+(.75f-1.55f)*t))continue;
                float z=SampleWallHeight(random,w.Source.height);endHeight=Math.Max(0,(z-1.55f*(1-t))/t);return true;
            }return false;
        }
        public static bool CanBreach(BattleMap m,V2 origin,V2 target,V2 impact){if(!Breakable(m,impact)||V2.Distance(impact,target)>.8f)return false;V2 hit;float t;return Ray(m,origin,1.55f,target,1.3f,out hit,out t)&&V2.Distance(hit,impact)<.35f;}
        public static List<Soldier> Shielded(BattleSimulation b,V2 point,V2 incoming,float impactHeight=1.35f,float destructionScale=1f){var result=new List<Soldier>();foreach(var s in b.Soldiers){V2 hit;float t;if(s.Alive&&V2.Distance(s.Position,point)<=.8f&&Ray(b.Map,point-incoming.Normalized*.03f,impactHeight,s.Position,1.3f,out hit,out t))result.Add(s);}return result;}
        public static void BehindDamage(BattleSimulation b,Soldier shooter,V2 p,List<Soldier> victims,int kind,HashSet<int> already,float impactHeight=1.35f,float destructionScale=1f){foreach(var s in victims){if(!s.Alive||already.Contains(s.Id))continue;V2 hit;float t;if(Ray(b.Map,p,impactHeight,s.Position,1.3f,out hit,out t)||!b.Map.LineOfSight(p,s.Position))continue;float d=V2.Distance(p,s.Position),radius=kind==2?4.3f:3.3f,fall=1-d/(radius+.5f),damage=kind==2?65-15*Util.Clamp(d/radius,0,1):b.Random.Range(34.15f,54.4f)*(1-d/4.3f)*(1-CombatTraining.Cover(b,s,s.Position,p)*.55f);s.LastBlast=b.Time;b.Damage(s,shooter,.7f*damage,(kind==2?.95f*fall:.85f*(1-d/4.3f)),(kind==2?.7f*(1-.3f*Util.Clamp(d/radius,0,1)):.4f*(1-d/4.3f)));ConcussionEffects.Apply(b,s,d,radius);}}
        public static bool Breakable(BattleMap m,V2 p)=>m.DynamicBuildings!=null&&m.DynamicBuildings.Any(h=>h.Breakable(p));
        public static void Blast(BattleSimulation b,V2 p,float radius,int kind,bool roofHit,V2 incoming,float impactHeight=1.35f,float destructionScale=1f){if(b.Map.DynamicBuildings==null)return;var watch=System.Diagnostics.Stopwatch.StartNew();LastWallCollapseMs=LastRoofCollapseMs=LastRefreshMs=0;bool changed=false;foreach(var h in b.Map.DynamicBuildings)changed|=h.Blast(b.Map,p,radius,kind,roofHit,incoming,b.TickNumber,impactHeight,destructionScale);if(changed)b.Map.ProceduralPictures=b.Map.DynamicBuildings.SelectMany(h=>h.Pictures).ToArray();LastBlastMilliseconds=watch.Elapsed.TotalMilliseconds;MaxBlastMilliseconds=Math.Max(MaxBlastMilliseconds,LastBlastMilliseconds);BlastCount++;}
        public static void Glass(BattleMap m,V2 a,float az,V2 b,float bz){if(m.DynamicBuildings==null)return;bool changed=false;foreach(var h in m.DynamicBuildings)changed|=h.BreakGlass(m,a,az,b,bz);if(changed)m.ProceduralPictures=m.DynamicBuildings.SelectMany(h=>h.Pictures).ToArray();}
    }
}
