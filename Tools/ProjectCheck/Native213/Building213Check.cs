using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using Rubezh.UnityPort;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Newtonsoft.Json;
using SilentOrders.Core;
public static class Building213Check
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception("Building213: "+message);}
    public static void Run()
    {
        var random=new Random(213);var rebuild=typeof(DynamicBuilding).GetMethod("RebuildPictures",BindingFlags.NonPublic|BindingFlags.Instance);
        for(int trial=0;trial<120;trial++){
            var source=new StructureWall{ax=1,ay=1,bx=3,by=1,vax=1,vay=1,vbx=3,vby=1,height=2.8f,thickness=.2f,material=trial%2==0?"brick":"wood",window=true,windowLo=.35f,windowHi=.65f,sill=.8f,lintel=2};
            for(int x=0;x<16;x++)source.columns.Add(new StructureColumn{spans=new List<StructureSpan>{new StructureSpan{lo=0,hi=2.8f}}});
            var original=new ProceduralPicture{Left=22,Top=-35,Width=24,Height=36,PixelWidth=96,PixelHeight=144,Pixels=Enumerable.Repeat(0xffabcdefu,96*144).ToArray(),Depth=new float[96*144]};
            var nativeOriginal=new ProceduralPicture{Left=original.Left,Top=original.Top,Width=original.Width,Height=original.Height,PixelWidth=original.PixelWidth,PixelHeight=original.PixelHeight,NativeKey="fixture:0"};
            var cpu=new DynamicBuilding(new List<StructureWall>{source},new List<StructureRoof>(),new List<StructureFloor>(),new List<StructureLayer>{new StructureLayer{Wall=0,Original=original}});
            var native=new DynamicBuilding(new List<StructureWall>{source},new List<StructureRoof>(),new List<StructureFloor>(),new List<StructureLayer>{new StructureLayer{Wall=0,Original=nativeOriginal}});
            var wall=cpu.Walls[0];var other=native.Walls[0];
            for(int i=0;i<wall.Solid.Length;i++){
                bool initial=random.NextDouble()>.18,solid=initial&&random.NextDouble()>.38;
                wall.Initial[i]=other.Initial[i]=initial;wall.Solid[i]=other.Solid[i]=solid;
            }
            wall.Revision=other.Revision=1;wall.GlassBroken=other.GlassBroken=trial%2==0;
            for(int z=0;z<wall.NZ;z++)for(int x=0;x<wall.NX;x++){
                int i=z*wall.NX+x;bool removed=wall.Initial[i]&&!wall.Solid[i];
                if(!wall.Solid[i]&&!removed){bool supported=false;for(int zz=Math.Max(0,z-1);zz<=Math.Min(wall.NZ-1,z+1);zz++)for(int xx=Math.Max(0,x-1);xx<=Math.Min(wall.NX-1,x+1);xx++)supported|=wall.Solid[zz*wall.NX+xx];removed=!supported;}
                Check(BuildingDamageMask.Removed(wall,x,z)==removed,"mask matches historical empty-neighbour/support rules");
            }
            rebuild.Invoke(cpu,null);rebuild.Invoke(native,null);
            Check(ReferenceEquals(native.Pictures[0],nativeOriginal),"original native image identity retained after damage");
            Check(native.Pictures.All(p=>p.Pixels==null&&p.Depth==null),"native damage/caps allocate no image pixels or depth arrays");
            Check(ReferenceEquals(nativeOriginal.NativeWall,other),"native visual reads original simulation wall state");
            int expectedFaces=0;
            for(int z=0;z<wall.NZ;z++)for(int x=0;x<wall.NX;x++){
                int i=z*wall.NX+x;if(!wall.Solid[i])continue;
                if(x>0&&wall.Initial[i-1]&&!wall.Solid[i-1])expectedFaces++;
                if(x+1<wall.NX&&wall.Initial[i+1]&&!wall.Solid[i+1])expectedFaces++;
                if(z>0&&wall.Initial[i-wall.NX]&&!wall.Solid[i-wall.NX])expectedFaces++;
                if(z+1<wall.NZ&&wall.Initial[i+wall.NX]&&!wall.Solid[i+wall.NX])expectedFaces++;
            }
            Check(native.Pictures.Skip(1).Sum(p=>p.NativeFaces?.Length??0)==expectedFaces,"cap quad count matches exposed fracture edges");
            for(int y=0;y<original.PixelHeight;y++)for(int x=0;x<original.PixelWidth;x++){
                float px=original.Left+(x+.5f)*original.Width/original.PixelWidth,py=original.Top+(y+.5f)*original.Height/original.PixelHeight;
                float u=(px/11-source.vax-source.vay)/(source.vbx-source.vax+source.vby-source.vay);
                float worldX=source.vax+(source.vbx-source.vax)*u,worldY=source.vay+(source.vby-source.vay)*u;
                float z=(5*(worldY-worldX)-py)*2.8f/24;
                bool glass=u>=source.windowLo&&u<=source.windowHi&&z>=source.sill&&z<=source.lintel;
                int ix=Math.Min(wall.NX-1,Math.Max(0,(int)(u*wall.NX))),iz=Math.Min(wall.NZ-1,Math.Max(0,(int)(z/source.height*wall.NZ)));
                bool removed=glass?wall.GlassBroken:BuildingDamageMask.Removed(other,ix,iz);
                Check((cpu.Pictures[0].Pixels[y*original.PixelWidth+x]==0)==removed,"shader mask formula matches historical CPU alpha at source texel");
            }
            Check(wall.Solid.SequenceEqual(other.Solid)&&wall.Initial.SequenceEqual(other.Initial),"visual generation does not mutate simulation");
        }
        CheckHeavyMap();
        Console.WriteLine("PASS Building213Check: "+checks+" mask/texel/cap invariants; GPU shader visual validation still required.");
    }
    static void CheckHeavyMap()
    {
        const string path="Build/Reports/AI-0.12.213/Buildings/story20-native-fixture.json";
        if(!File.Exists(path))throw new InvalidOperationException("Run Tools/ProjectCheck/Native213/building_source_check.py before native213 tests");
        var dto=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(File.ReadAllText(path));var level=dto.Create();
        var battle=new BattleSimulation(213);Check(RubezhLevelRuntime.ApplyLevel(battle,level),"heavy native map applies: "+RubezhLevelRuntime.LastError);
        var pictures=battle.Map.ProceduralPictures;Check(pictures.Length==1737,"actual story20 retains all1737layers");
        Check(pictures.All(p=>p.Pixels==null&&p.Depth==null&&!string.IsNullOrEmpty(p.NativeKey)),"actual heavy map loads without CPU image arrays");
        var keys=new HashSet<string>();foreach(var p in pictures)Check(keys.Add(p.NativeKey)&&p.PixelWidth>0&&p.PixelHeight>0,"actual layer key and dimensions valid");
        var sizes=new List<AtlasLayout.Size>();var regions=new List<NativeBuildingRegion>();
        foreach(var p in pictures)for(int y=0;y<p.PixelHeight;y+=512)for(int x=0;x<p.PixelWidth;x+=512){
            int w=Math.Min(512,p.PixelWidth-x),h=Math.Min(512,p.PixelHeight-y);sizes.Add(new AtlasLayout.Size(w,h));regions.Add(new NativeBuildingRegion{key=p.NativeKey,px=x,py=y,w=w,h=h});
        }
        // Real packing implementation, metadata only: catches image extent and atlas-limit failures.
        var plan=AtlasLayout.Pack(sizes,1024);
        for(int i=0;i<regions.Count;i++){var r=regions[i];var place=plan.Items[i];r.page=place.Page;r.x=place.X;r.y=place.Y;Check(r.x>0&&r.y>0&&r.x+r.w<plan.Pages[r.page].W&&r.y+r.h<plan.Pages[r.page].H,"actual heavy region extrusion fits");}
        var source=Addressables.Loader;int loads=0;
        try{
            var catalog=new NativeBuildingCatalog{version=1,pages=plan.Pages.Length,regions=regions.ToArray()};
            Addressables.Loader=address=>{loads++;if(address.EndsWith("/catalog"))return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new TextAsset{text=JsonConvert.SerializeObject(catalog)}};
                var parts=address.Split('/');int page=int.Parse(parts[parts.Length-2]);var size=plan.Pages[page];return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new Texture2D(size.W,size.H,address.EndsWith("/depth")?TextureFormat.RFloat:TextureFormat.RGBA32,false)};};
            var load=NativeBuildingAssets.Load(battle.Map,()=>false);while(load.MoveNext()){}(load as IDisposable)?.Dispose();
            Check(loads==1+plan.Pages.Length*2,"actual map loads one catalog and exact unique texture pages");
            foreach(var p in pictures)Check(NativeBuildingAssets.Get(p.NativeKey).Sum(r=>r.Data.w*r.Data.h)==p.PixelWidth*p.PixelHeight,"all actual heavy pixels covered once by imported regions");
            Console.WriteLine("PASS story20 native metadata: 1737 layers, "+regions.Count+" pieces, "+plan.Pages.Length+" atlas pages, zero CPU pixel/depth arrays. Metadata fixture, not Unity import.");
        }finally{NativeBuildingAssets.Release();Addressables.Loader=source;UnityEngine.Object.DestroyImmediate(level);}
    }

}
