using System;
using System.Collections.Generic;
using SilentOrders.Rendering;
using UnityEngine;

namespace Rubezh.UnityPort
{
    public sealed partial class HdGpuRenderer
    {
        // Only final Unity instances are retained. No OrderedCanvas command replay,
        // sprite vertex staging, or second foreground composition on this path.
        sealed class NativeDraw
        {
            public NativeSpritePose Pose;
            public Vector4 Tint;
            public int Stage, Sequence, Actor;
            public float Depth;
            public int Seen, ParentKind; public bool Listed,Sprite,Dirty=true; public long Key;
        }
        readonly List<NativeDraw> nativeDraws = new List<NativeDraw>(1024);
        sealed class DirectGroup { public readonly List<NativeDraw> Parts=new List<NativeDraw>(); public int Cursor; }
        readonly Dictionary<int,DirectGroup> directGroups=new Dictionary<int,DirectGroup>();
        int directEpoch; bool directOrderDirty;
        public int DirectActorReuses { get; private set; }
        public int DirectSorts { get; private set; }
        void StartDirectGroup(int id) { DirectGroup g; if(!directGroups.TryGetValue(id,out g))directGroups.Add(id,g=new DirectGroup());g.Cursor=0; }
        internal void BeginVehicleVisual(int id){visualId=id;StartDirectGroup(id);}
        internal void EndVehicleVisual(){FinishDirectGroup(visualId);visualId=-1;}
        internal void ReuseDirectActor(int id) {
            DirectGroup group;if(!directGroups.TryGetValue(id,out group))return;
            foreach(var draw in group.Parts) {
                ActorTransform actor;
                if(id>=0&&actorTransforms.TryGetValue(id/4,out actor)){
                    float depth=draw.Pose.Data.x+actor.Capture.Depth+(draw.ParentKind==2?actor.Capture.WeaponDepth:0);
                    if(draw.Depth!=depth){draw.Depth=depth;directOrderDirty=true;}
                }
                draw.Seen=directEpoch;if(!draw.Listed){draw.Listed=true;nativeDraws.Add(draw);directOrderDirty=true;} }
            DirectActorReuses++;
        }
        void StoreDirect(NativeSpritePose pose,Vector4 tint,float depth,bool test,bool sprite=false) {
            DirectGroup group;if(!directGroups.TryGetValue(visualId,out group))directGroups.Add(visualId,group=new DirectGroup());
            int part=group.Cursor++;NativeDraw draw;
            if(part==group.Parts.Count){draw=new NativeDraw{Actor=visualId,Key=((long)(visualId+1)<<32)|(uint)part};group.Parts.Add(draw);}else draw=group.Parts[part];
            int stage=test?nativeStage:2;
            if(draw.Stage!=stage||draw.Depth!=depth)directOrderDirty=true;
            LocalizeActorPart(draw,ref pose);draw.Dirty=true;
            draw.Pose=pose;draw.Sprite=sprite;draw.Tint=tint;draw.Depth=depth;draw.Stage=stage;draw.Seen=directEpoch;
            if(!draw.Listed){draw.Listed=true;nativeDraws.Add(draw);directOrderDirty=true;}
        }
        void FinishDirectGroup(int id) {
            DirectGroup group;if(!directGroups.TryGetValue(id,out group))return;
            // Remove obsolete parts when equipment, injury or pose changes topology.
            for(int i=group.Parts.Count-1;i>=group.Cursor;i--){var draw=group.Parts[i];if(draw.Listed){nativeDraws.Remove(draw);draw.Listed=false;directOrderDirty=true;}RemoveWorldNode(draw.Key);group.Parts.RemoveAt(i);}
        }
        NativeSpriteMesh nativeSolid;
        readonly Dictionary<long,NativeSpriteMesh> nativeEllipses = new Dictionary<long,NativeSpriteMesh>();
        int nativeSpriteCount;
        public int NativeSpriteInstanceCount => nativeSpriteCount;
        bool nativeDynamic, directFrame;
        int nativeStage;
        public bool DirectComposition => UnityOwnedScene;
        public int DirectInstanceCount => nativeDraws.Count;
        public int DirectCompositionBuilds { get; private set; }
        public void BeginDynamic()
        {
            nativeDynamic = directFrame = true;
            nativeStage = 1;
            directEpoch++;StartDirectGroup(-1);
            nativeSpriteCount=0;
            DirectCompositionBuilds++;
        }
        public void SetStage(int stage) { nativeStage = stage; }
        public void EndDynamic() {
            nativeDynamic = false;FinishDirectGroup(-1);
            for(int i=nativeDraws.Count-1;i>=0;i--)if(nativeDraws[i].Seen!=directEpoch){nativeDraws[i].Listed=false;nativeDraws.RemoveAt(i);directOrderDirty=true;}
            if(directOrderDirty){
                bool unordered=false;
                for(int i=1;i<nativeDraws.Count&&!unordered;i++)unordered=CompareDirectDepth(nativeDraws[i-1],nativeDraws[i])>0;
                if(unordered){nativeDraws.Sort(CompareDirectDepth);DirectSorts++;}
                directOrderDirty=false;
            }
            nativeSpriteCount=0;foreach(var draw in nativeDraws)if(draw.Sprite)nativeSpriteCount++;
        }
        void AddDirectSprite(NativeSpritePose pose, float depth, bool test)
        {
            nativeSpriteCount++;
            StoreDirect(pose,new Vector4(1,1,1,1),depth,test,true);
        }
        NativeSpriteMesh SolidGeometry()
        {
            if (nativeSolid != null) return nativeSolid;
            float u=1.5f/atlases[0].width, v=1.5f/atlases[0].height;
            var quad=new Mesh { name="Native shared solid quad" };
            quad.vertices=new[]{new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            quad.uv=new[]{new Vector2(u,v),new Vector2(u,v),new Vector2(u,v),new Vector2(u,v)};
            quad.colors32=new[]{new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255)};
            quad.triangles=new[]{0,1,2,0,2,3};quad.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
            quad.UploadMeshData(true);
#endif
            nativeSolid=new NativeSpriteMesh { Mesh=quad, Pages=new[]{0} };
            return nativeSolid;
        }
        bool RetainNativeRect(float x,float y,float width,float height,uint color,float depth,bool test)
        {
            if (!nativeDynamic || !UnityOwnedScene) return false;
            DirectSolid(x+width*.5f,y+height*.5f,width,height,0,color,depth,test);
            return true;
        }
        void DirectSolid(float x,float y,float width,float height,float angle,uint color,float depth,bool test)
        {
            var pose=new NativeSpritePose { Geometry=SolidGeometry(),Position=new Vector3(x,y,0),
                Scale=new Vector3(width,height,1),Angle=angle,
                Data=new Vector4(depth,test?1:0,canvas.LayerLeft,canvas.LayerRight) };
            StoreDirect(pose,new Vector4((color&255)/255f,((color>>8)&255)/255f,((color>>16)&255)/255f,(color>>24)/255f),depth,test);
        }
        public bool Ellipse(float x,float y,int rx,int ry,float scale,uint color,float depth,bool test)
        {
            if(!nativeDynamic||!UnityOwnedScene)return false;
            if(rx<=0||ry<=0)return true;
            long key=((long)rx<<32)|(uint)ry;
            NativeSpriteMesh geometry;
            if(!nativeEllipses.TryGetValue(key,out geometry)){
                // Build the exact former row silhouette once, not N scene nodes per frame.
                int count=ry*2+1;var vertices=new Vector3[count*4];var uv=new Vector2[count*4];
                var colors=new Color32[count*4];var triangles=new int[count*6];
                float u=1.5f/atlases[0].width,v=1.5f/atlases[0].height;
                for(int row=-ry;row<=ry;row++){
                    int span=rx;while(span>0&&(float)span*span/(rx*rx)+(float)row*row/(ry*ry)>1f)span--;
                    int q=(row+ry)*4,t=(row+ry)*6;
                    vertices[q]=new Vector3(-span,row,0);vertices[q+1]=new Vector3(span+1,row,0);
                    vertices[q+2]=new Vector3(span+1,row+1,0);vertices[q+3]=new Vector3(-span,row+1,0);
                    for(int i=0;i<4;i++){uv[q+i]=new Vector2(u,v);colors[q+i]=new Color32(255,255,255,255);}
                    triangles[t]=q;triangles[t+1]=q+1;triangles[t+2]=q+2;triangles[t+3]=q;triangles[t+4]=q+2;triangles[t+5]=q+3;
                }
                var mesh=new Mesh{name="Native ellipse "+rx+"x"+ry};mesh.vertices=vertices;mesh.uv=uv;mesh.colors32=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
#if UNITY_5_3_OR_NEWER
                mesh.UploadMeshData(true);
#endif
                geometry=new NativeSpriteMesh{Mesh=mesh,Pages=new[]{0}};nativeEllipses.Add(key,geometry);
            }
            var pose=new NativeSpritePose{Geometry=geometry,Position=new Vector3(x,y,0),Scale=new Vector3(scale,scale,1),Data=new Vector4(depth,test?1:0,canvas.LayerLeft,canvas.LayerRight)};
            StoreDirect(pose,new Vector4((color&255)/255f,((color>>8)&255)/255f,((color>>16)&255)/255f,(color>>24)/255f),depth,test);
            return true;
        }
        public bool Line(float x0,float y0,float x1,float y1,float width,uint color,float depth,bool test)
        {
            if (!nativeDynamic || !UnityOwnedScene) return false;
            float dx=x1-x0,dy=y1-y0;
            DirectSolid((x0+x1)*.5f,(y0+y1)*.5f,(float)Math.Sqrt(dx*dx+dy*dy)+width,width,
                (float)(Math.Atan2(dy,dx)*180/Math.PI),color,depth,test);
            return true;
        }
        void AddDirectVisuals()
        {
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
            // The only pre-dynamic geometry is the retained ground decal batch.
            visualOrdinals.Clear();
            if(indices.Count>0) AddVisualRange(0,indices.Count,-1,default(Vector3));

            directSubmissionStart=unitySubmissionOrder;
            foreach(var draw in nativeDraws) {
                var pose=draw.Pose;
                SceneNode existing;
                if(!draw.Dirty&&unityActorNodes.TryGetValue(draw.Key,out existing)){
                    existing.LastFrame=pictureFrame;
                    if(!existing.Active){existing.Object.SetActive(true);existing.Active=true;}
                    int order=unitySubmissionOrder++*2;
                    if(existing.Order!=order){existing.Renderer.sortingOrder=order;existing.Order=order;}
                    continue;
                }
                AddUnityMesh(pose.Geometry.Mesh,pose.Geometry.Pages,"Native visual",draw.Actor == -2 || draw.Actor == -3 ? 30 : draw.Actor>=0?28:25,
                    pose.Position,pose.Scale,pose.Angle,pose.Data,draw.Tint,draw.Key,ActorParent(draw));
                draw.Dirty=false;
            }
#endif
        }
        void DisposeDirectVisuals()
        {
            if(nativeSolid!=null)UnityEngine.Object.Destroy(nativeSolid.Mesh);
            foreach(var ellipse in nativeEllipses.Values)UnityEngine.Object.Destroy(ellipse.Mesh);
            nativeEllipses.Clear();
            DisposeActorTransforms();
            nativeSolid=null;nativeDraws.Clear();directGroups.Clear();nativeDynamic=directFrame=false;
        }
    }
}
