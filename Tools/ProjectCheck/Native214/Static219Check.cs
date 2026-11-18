using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Rubezh.UnityPort;
using SilentOrders.Core;
using SilentOrders.Rendering;
using UnityEngine;
using UnityEngine.AddressableAssets;

class Static219Check
{
    static int checks;
    const System.Reflection.BindingFlags Reflect=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public;
    static int Property(object o,string name){var p=o.GetType().GetProperty(name,Reflect);return p==null?-1:Convert.ToInt32(p.GetValue(o));}
    static int Runs(object r,string name){var data=r.GetType().GetField(name,Reflect).GetValue(r);int n=0;if(data is System.Collections.IEnumerable list){foreach(var item in list)n+=((int[])item.GetType().GetField("Pages",Reflect).GetValue(item)).Length;}else n=((int[])data.GetType().GetField("Pages",Reflect).GetValue(data)).Length;return n;}

    static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
    static void Drain(IEnumerator e){try{while(e.MoveNext()){} }finally{(e as IDisposable)?.Dispose();}}
    static void Main()
    {
        var random=new Random(219);
        for(int pass=0;pass<500;pass++)
        {
            var vertices=new List<Vector3>();var indices=new List<int>();var runs=new AtlasLayout.DrawRuns();
            var pages=new List<int>();var boxes=new List<float[]>();
            for(int n=0;n<64;n++)
            {
                float x=random.Next(0,100),y=random.Next(0,100),w=random.Next(1,16),h=random.Next(1,16);
                int k=vertices.Count,page=random.Next(0,4);pages.Add(page);boxes.Add(new[]{x,y,x+w,y+h});
                vertices.Add(new Vector3(x,y,0));vertices.Add(new Vector3(x+w,y,0));
                vertices.Add(new Vector3(x+w,y+h,0));vertices.Add(new Vector3(x,y+h,0));
                indices.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});runs.AddQuad(page);
            }
            int oldRuns=runs.Items.Count;
            OrderedMeshRuns.Compact(vertices,indices,runs,2);
            Check(runs.Items.Count<=oldRuns,"draw runs never increase");
            var order=new int[64];var seen=new HashSet<int>();
            foreach(var run in runs.Items)for(int j=run.Start;j<run.Start+run.Count;j+=6)
            {
                int n=indices[j]/4;order[n]=j/6;
                Check(seen.Add(n)&&pages[n]==run.Page,"exactly one primitive with original texture");
                int k=n*4;Check(indices.Skip(j).Take(6).SequenceEqual(new[]{k,k+1,k+2,k,k+2,k+3}),"triangle order unchanged");
            }
            for(int a=0;a<64;a++)for(int b=a+1;b<64;b++)
            {
                var x=boxes[a];var y=boxes[b];
                bool disjoint=x[2]+2<y[0]||y[2]+2<x[0]||x[3]+2<y[1]||y[3]+2<x[1];
                Check(disjoint||order[a]<order[b],"every overlapping pair keeps painter order");
            }
        }
        var catalog=File.ReadAllText("Assets/Rubezh/NativeContent/catalog.json");
        var entries=JsonConvert.DeserializeObject<UnityTestAtlasCatalog>(catalog).entries;
        Resources.Loader=path=>path=="NativeExterior/catalog"?new TextAsset{text=File.ReadAllText("Assets/Rubezh/Resources/NativeExterior/catalog.json")}:path.StartsWith("NativeExterior/page-")?new Texture2D(2048,2048,TextureFormat.RGBA32,false):(path=="RubezhNativeEffectMaterial"||path=="RubezhNativeSceneMaterial")?(UnityEngine.Object)new Material(new Shader()):path=="RoofSurface"||path=="ClosureWood"?(UnityEngine.Object)new Texture2D(16,16,TextureFormat.RGBA32,false):path.StartsWith("Rubezh")?new Shader():null;
        Addressables.Loader=key=>{
            if(key=="rubezh/native/catalog")return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new TextAsset{text=catalog}};
            if(key=="rubezh/native/visual")return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=new GameObject("visual")};
            int page=int.Parse(key.Substring(key.LastIndexOf('/')+1));var atlas=new UnityEngine.U2D.SpriteAtlas();
            var texture=new Texture2D(8192,8192,TextureFormat.RGBA32,false);
            foreach(var e in entries.Where(e=>e.page==page))atlas.Items["s"+e.id.ToString("D5")]=new Sprite{texture=texture,textureRect=new Rect(0,0,e.w,e.h)};
            return new UnityEngine.ResourceManagement.AsyncOperations.State{Result=atlas};
        };
        foreach(string path in new[]{"Assets/Rubezh/Resources/Regenerated/Campaign_01.bytes","Assets/Rubezh/Resources/Regenerated/Campaign_20.bytes"})
        {
            string fixtureRoot=Environment.GetEnvironmentVariable("RUBEZH_MAP_FIXTURES")??"Tools/ProjectCheck/Optimization221/Fixtures";
            string input=File.Exists(path)?path:Path.Combine(fixtureRoot,Path.GetFileName(path));
            var map=JsonConvert.DeserializeObject<BuiltinCampaign.Map>(PackedLevel.Decode(File.ReadAllBytes(input))).Create();
            map.terrainVisual=null;map.proceduralVisuals.Clear();foreach(var house in map.dynamicBuildings)house.layers.Clear();
            var sim=new BattleSimulation(210);Check(RubezhLevelRuntime.ApplyLevel(sim,map),"map applies");
            Rubezh.Graphics.ReleaseSceneSprites();
            using(var lease=new NativeBattleAssets())
            {
                Drain(lease.Load(Rubezh.Graphics.SceneSprites(sim),()=>false));Drain(ExteriorSpriteLoader.Preload(()=>false));
                var painter=new NativeBattleComposer(1);painter.PrepareCanvas(sim.Map);
                using(var renderer=new HdGpuRenderer(painter.Canvas,true,false,true))
                {
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    renderer.Render(painter,sim,false,0,960,540,new Rect(0,0,1,1));
                    timer.Stop();
                    Console.WriteLine(path+": ground material runs "+renderer.GroundRunsBefore+" -> "+renderer.GroundRunsAfter+", compaction ms="+renderer.GroundRunCompactionMs.ToString("F2")+", portable render ms="+timer.Elapsed.TotalMilliseconds.ToString("F2"));
                    Check(renderer.GroundRunsBefore>=renderer.GroundRunsAfter,"real map runs never increase");
                    var gd=(IEnumerable)renderer.GetType().GetField("groundDraws",Reflect).GetValue(renderer);
                    var hist=new Dictionary<int,int>();var palettes=new List<int>();
                    foreach(var g in gd){var pages=(int[])g.GetType().GetField("Pages",Reflect).GetValue(g);palettes.Add(pages.Distinct().Count());foreach(int page in pages){if(!hist.ContainsKey(page))hist[page]=0;hist[page]++;}}
                    Console.WriteLine("GROUND PALETTES "+string.Join(",",palettes)+" PAGE RUNS "+string.Join(",",hist.OrderByDescending(p=>p.Value).Select(p=>p.Key+":"+p.Value)));
                    Console.WriteLine("Static object runs "+renderer.StaticRunsBefore+" -> "+renderer.StaticRunsAfter);
                    Check(renderer.StaticRunsBefore>=renderer.StaticRunsAfter,"static runs never increase");
                    Console.WriteLine("MEASURE staticCommands="+renderer.UnityExplicitDraws+" visibleNodes="+renderer.UnityStaticVisibleNodes+" maskObject="+Property(renderer,"UnityObjectMaskDraws")+" maskRoof="+Property(renderer,"UnityRoofMaskDraws")+" maskMeshes="+Property(renderer,"UnityMaskMergedMeshes")+" maskBuilds="+Property(renderer,"UnityMaskGeometryBuilds")+" legacyObjectMaskRuns="+(Runs(renderer,"sceneObjectChunks")+Runs(renderer,"pictureBatch"))+" legacyRoofMaskRuns="+(Runs(renderer,"sceneRoofChunks")+Runs(renderer,"pictureBatch")));

                }
            }
            Rubezh.Graphics.ReleaseSceneSprites();
        }
        Console.WriteLine("PASS "+checks+" checks. Real map geometry, portable Unity contracts; no GPU timings or pixel rendering.");
    }
}
