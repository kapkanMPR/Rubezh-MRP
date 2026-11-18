using System;
using System.Collections.Generic;
using UnityEngine;
namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        struct VisualRange { public int Id,Start,End,SpriteStart,SpriteEnd;public Vector3 Origin; }
        readonly List<VisualRange> visualRanges=new List<VisualRange>();
        int visualStart,visualSpriteStart,visualId=-1;Vector3 visualOrigin;
        sealed class VisualMesh
        {
            public readonly Mesh Mesh=new Mesh();
            public VisualMesh(){Mesh.MarkDynamic();}
            public readonly List<Vector3> Vertices=new List<Vector3>();
            public readonly List<Vector2> UV=new List<Vector2>(),Depth=new List<Vector2>(),Clip=new List<Vector2>();
            public readonly List<Color32> Colors=new List<Color32>();
            public readonly List<int> Indices=new List<int>(),Pages=new List<int>(),Starts=new List<int>(),Counts=new List<int>();
            public int[] PageArray;
            public int LastFrame;
        }
        public int NativeVisualMeshes=>visualMeshes.Count;
        public int NativeVisualUploads {get;private set;}
        public int NativeVisualHits {get;private set;}
        readonly Dictionary<long,VisualMesh> visualMeshes=new Dictionary<long,VisualMesh>();
        readonly Dictionary<int,int> visualOrdinals=new Dictionary<int,int>();
        readonly List<long> expiredVisualMeshes=new List<long>();
        void ResetVisualGroups(){visualRanges.Clear();nativeSpritePoses.Clear();visualId=-1;}
        public void BeginVisual(int soldierId,float x,float y){visualId=soldierId;if(directFrame)StartDirectGroup(soldierId);visualStart=indices.Count;visualSpriteStart=nativeSpritePoses.Count;visualOrigin=new Vector3(x*canvas.Scale,y*canvas.Scale,0);}
        public void EndVisual(){
            if(!directFrame && UnityOwnedScene&&(indices.Count>visualStart||nativeSpritePoses.Count>visualSpriteStart))
                visualRanges.Add(new VisualRange{Id=visualId,Start=visualStart,End=indices.Count,SpriteStart=visualSpriteStart,SpriteEnd=nativeSpritePoses.Count,Origin=visualOrigin});
            if(directFrame)FinishDirectGroup(visualId);
            visualId=-1;
        }
        void AddNativeVisualGroups()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            if (directFrame) { AddDirectVisuals(); return; }
            int cursor=0;visualOrdinals.Clear();
            foreach(var range in visualRanges){
                if(range.Start>cursor)AddVisualRange(cursor,range.Start,-1,default(Vector3));
                cursor=range.Start;
                for(int i=range.SpriteStart;i<range.SpriteEnd;i++){
                    var pose=nativeSpritePoses[i];
                    if(pose.At>cursor)AddVisualRange(cursor,pose.At,range.Id,range.Origin);
                    SubmitNativeSprite(pose);cursor=pose.At;
                }
                if(range.End>cursor)AddVisualRange(cursor,range.End,range.Id,range.Origin);
                cursor=range.End;
            }
            if(cursor<indices.Count)AddVisualRange(cursor,indices.Count,-1,default(Vector3));
            // Bound pools after temporary explosions or changing actor fragments.
            expiredVisualMeshes.Clear();
            foreach(var pair in visualMeshes)if(pictureFrame-pair.Value.LastFrame>120)expiredVisualMeshes.Add(pair.Key);
            foreach(var key in expiredVisualMeshes){UnityEngine.Object.Destroy(visualMeshes[key].Mesh);visualMeshes.Remove(key);}
#endif
        }
        static bool Same(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        static bool Same(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y;
        static bool Same(Color32 a,Color32 b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
        void AddVisualRange(int first,int last,int soldier,Vector3 origin)
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            int ordinal;visualOrdinals.TryGetValue(soldier,out ordinal);visualOrdinals[soldier]=ordinal+1;
            long key=((long)(soldier+1)<<32)|(uint)ordinal;
            VisualMesh target;if(!visualMeshes.TryGetValue(key,out target))visualMeshes.Add(key,target=new VisualMesh());
            target.LastFrame=pictureFrame;
            int vertexStart=first/6*4,vertexEnd=last/6*4,vertexCount=vertexEnd-vertexStart;
            bool topology=target.Vertices.Count!=vertexCount||target.Indices.Count!=last-first;
            bool positions=topology,uv=topology,depth=topology,clip=topology,color=topology;
            for(int i=vertexStart,j=0;!topology&&i<vertexEnd;i++,j++){
                var local=new Vector3(vertices[i].x-origin.x,vertices[i].y-origin.y,vertices[i].z);
                positions|=!Same(target.Vertices[j],local);uv|=!Same(target.UV[j],uvs[i]);
                depth|=!Same(target.Depth[j],depths[i]);clip|=!Same(target.Clip[j],clips[i]);color|=!Same(target.Colors[j],colors[i]);
            }
            int runIndex=0;
            foreach(var run in runs.Items){int a=Math.Max(first,run.Start),b=Math.Min(last,run.Start+run.Count);if(b<=a)continue;
                if(runIndex>=target.Pages.Count||target.Pages[runIndex]!=run.Page||target.Starts[runIndex]!=a-first||target.Counts[runIndex]!=b-a)topology=true;
                runIndex++;
            }
            topology|=runIndex!=target.Pages.Count;
            // Clear only when vertex capacity changes; Unity must not validate old indices against a smaller buffer.
            if(target.Vertices.Count!=vertexCount)target.Mesh.Clear();
            // Channel uploads only when changed; camera motion performs no mesh upload.
            if(positions){target.Vertices.Clear();for(int i=vertexStart;i<vertexEnd;i++)target.Vertices.Add(new Vector3(vertices[i].x-origin.x,vertices[i].y-origin.y,vertices[i].z));target.Mesh.SetVertices(target.Vertices);target.Mesh.RecalculateBounds();}
            if(uv){target.UV.Clear();for(int i=vertexStart;i<vertexEnd;i++)target.UV.Add(uvs[i]);target.Mesh.SetUVs(0,target.UV);}
            if(depth){target.Depth.Clear();for(int i=vertexStart;i<vertexEnd;i++)target.Depth.Add(depths[i]);target.Mesh.SetUVs(1,target.Depth);}
            if(clip){target.Clip.Clear();for(int i=vertexStart;i<vertexEnd;i++)target.Clip.Add(clips[i]);target.Mesh.SetUVs(2,target.Clip);}
            if(color){target.Colors.Clear();for(int i=vertexStart;i<vertexEnd;i++)target.Colors.Add(colors[i]);target.Mesh.SetColors(target.Colors);}
            if(topology){
                target.Indices.Clear();for(int i=first;i<last;i++)target.Indices.Add(indices[i]-vertexStart);
                target.Pages.Clear();target.Starts.Clear();target.Counts.Clear();
                foreach(var run in runs.Items){int a=Math.Max(first,run.Start),b=Math.Min(last,run.Start+run.Count);if(b<=a)continue;target.Pages.Add(run.Page);target.Starts.Add(a-first);target.Counts.Add(b-a);}
                target.Mesh.subMeshCount=target.Pages.Count;
                for(int i=0;i<target.Pages.Count;i++)target.Mesh.SetTriangles(target.Indices,target.Starts[i],target.Counts[i],i,false);
                if(target.PageArray==null||target.PageArray.Length!=target.Pages.Count)target.PageArray=new int[target.Pages.Count];
                for(int i=0;i<target.Pages.Count;i++)target.PageArray[i]=target.Pages[i];
            }
            if(topology||positions||uv||depth||clip||color)NativeVisualUploads++;else NativeVisualHits++;
            AddUnityMesh(target.Mesh,target.PageArray,"Actor marks / effects",soldier>=0?28:25,origin);
#endif
        }
        void DisposeVisualGroups(){foreach(var item in visualMeshes.Values)UnityEngine.Object.Destroy(item.Mesh);visualMeshes.Clear();visualRanges.Clear();visualOrdinals.Clear();expiredVisualMeshes.Clear();DisposeNativeSprites();}
    }
}
