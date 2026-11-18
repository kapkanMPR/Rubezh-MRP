using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.Rendering;
#endif

namespace Rubezh.UnityPort {
 public sealed partial class HdGpuRenderer {
  public bool UnityMergedMaskCommands=true;
  public int UnityObjectMaskDraws {get;private set;}
  public int UnityRoofMaskDraws {get;private set;}
  public int UnityMaskGeometryBuilds {get;private set;}
  public int UnityMaskMergedMeshes {get;private set;}
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
  // Mask output uses component-wise MAX, not alpha compositing. It is therefore
  // safe to collect nonconsecutive triangles of the same material. Keep spatial
  // chunks so a small viewport does not submit all the occluders on a large map.
  struct MaskMergeKey : IEquatable<MaskMergeKey> {
   public long Chunk;public int Page;public bool Roof;
   public bool Equals(MaskMergeKey b)=>Chunk==b.Chunk&&Page==b.Page&&Roof==b.Roof;
   public override bool Equals(object b)=>b is MaskMergeKey&&Equals((MaskMergeKey)b);
   public override int GetHashCode(){unchecked{return (Chunk.GetHashCode()*397^Page)*397^(Roof?1:0);}}
  }
  sealed class MaskMergedMesh {public Mesh Mesh;public int Page;public bool Roof;}
  sealed class MaskMergeBuilder {
   public readonly List<Vector3> Vertices=new List<Vector3>();
   public readonly List<Vector2> UVs=new List<Vector2>(),Depths=new List<Vector2>(),Clips=new List<Vector2>();
   public readonly List<Color32> Colors=new List<Color32>();
   public readonly List<int> Indices=new List<int>();
   readonly Dictionary<int,int> remap=new Dictionary<int,int>();
   GeometryData lastData;
   public void Add(GeometryData data,int start,int count){
    if(!ReferenceEquals(lastData,data)){remap.Clear();lastData=data;}
    for(int i=start;i<start+count;i++){
     int source=data.Indices[i],index;
     if(!remap.TryGetValue(source,out index)){
      index=Vertices.Count;remap.Add(source,index);
      Vertices.Add(data.Vertices[source]);UVs.Add(data.UVs[source]);Depths.Add(data.Depths[source]);
      Clips.Add(data.Clips[source]);Colors.Add(data.Colors[source]);
     }
     Indices.Add(index);
    }
   }
   public Mesh Upload(){
    var mesh=new Mesh{name="Rubezh merged MAX occluders",indexFormat=IndexFormat.UInt32};
    mesh.SetVertices(Vertices);mesh.SetUVs(0,UVs);mesh.SetUVs(1,Depths);mesh.SetUVs(2,Clips);mesh.SetColors(Colors);
    mesh.subMeshCount=1;mesh.SetTriangles(Indices,0,false);mesh.RecalculateBounds();return mesh;
   }
  }
  readonly List<MaskMergedMesh> unityMaskMergedMeshes=new List<MaskMergedMesh>();
  readonly List<Material> unityMergedMaskMaterials=new List<Material>();
  CommandBuffer unityObjectMaskCommands,unityRoofMaskCommands;
  int unityMaskMergedGeometry=-1,unityMaskMergedMaterialRevision=-1;

  void ReleaseUnityMaskCommands(){
   // Commands are attached only around their camera.Render call and detached in finally.
   if(unityObjectMaskCommands!=null){unityObjectMaskCommands.Release();unityObjectMaskCommands=null;}
   if(unityRoofMaskCommands!=null){unityRoofMaskCommands.Release();unityRoofMaskCommands=null;}
   foreach(var batch in unityMaskMergedMeshes)UnityEngine.Object.Destroy(batch.Mesh);
   unityMaskMergedMeshes.Clear();
   foreach(var material in unityMergedMaskMaterials)UnityEngine.Object.Destroy(material);
   unityMergedMaskMaterials.Clear();unityMaskMergedGeometry=unityMaskMergedMaterialRevision=-1;
   UnityMaskMergedMeshes=UnityObjectMaskDraws=UnityRoofMaskDraws=0;
  }

  static void AddMergedMaskData(Dictionary<MaskMergeKey,MaskMergeBuilder> groups,GeometryData data,long chunk,bool roof){
   if(data==null)return;
   foreach(var run in data.Runs){
    if(run.Count==0)continue;
    var key=new MaskMergeKey{Chunk=chunk,Page=run.Page,Roof=roof};MaskMergeBuilder builder;
    if(!groups.TryGetValue(key,out builder)){builder=new MaskMergeBuilder();groups.Add(key,builder);}
    builder.Add(data,run.Start,run.Count);
   }
  }
  bool PrepareUnityMaskCommands(Rect view){
   if(!UnityMergedMaskCommands)return false;
   // The direct path remains available for a partially initialized scene.
   if(pictureBatch.Mesh!=null&&pictureBatch.Pages.Length>0&&pictureBatch.Data==null)return false;
   foreach(var batch in objectDraws)if(batch.Data==null)return false;
   if(unityMaskMergedGeometry!=geometrySerial){
    var groups=new Dictionary<MaskMergeKey,MaskMergeBuilder>();
    AddMergedMaskData(groups,pictureBatch.Data,long.MinValue,false);
    AddMergedMaskData(groups,pictureBatch.Data,long.MinValue,true);
    foreach(var batch in objectDraws){
     AddMergedMaskData(groups,batch.Data,batch.Chunk,false);
     if(batch.BlocksRoof)AddMergedMaskData(groups,batch.Data,batch.Chunk,true);
    }
    foreach(var batch in unityMaskMergedMeshes)UnityEngine.Object.Destroy(batch.Mesh);
    unityMaskMergedMeshes.Clear();
    foreach(var entry in groups)unityMaskMergedMeshes.Add(new MaskMergedMesh{Mesh=entry.Value.Upload(),Page=entry.Key.Page,Roof=entry.Key.Roof});
    unityMaskMergedGeometry=geometrySerial;UnityMaskGeometryBuilds++;UnityMaskMergedMeshes=unityMaskMergedMeshes.Count;
   }
   while(unityMergedMaskMaterials.Count<unityMaterials.Count)unityMergedMaskMaterials.Add(new Material(unityMaskShader));
   if(unityMaskMergedMaterialRevision!=unityStaticMaterialRevision){
    for(int i=0;i<unityMaterials.Count;i++){
     var material=unityMergedMaskMaterials[i];material.CopyPropertiesFromMaterial(unityMaterials[i]);material.shader=unityMaskShader;
     // Static geometry supplies these values in vertex channels, never a native instance block.
     material.SetFloat("_NativeInstance",0);material.SetFloat("_ActorRootDepth",0);
    }
    unityMaskMergedMaterialRevision=unityStaticMaterialRevision;
   }
   if(unityObjectMaskCommands==null){
    unityObjectMaskCommands=new CommandBuffer{name="Rubezh grouped object MAX mask"};
    unityRoofMaskCommands=new CommandBuffer{name="Rubezh grouped roof MAX mask"};
   }
   unityObjectMaskCommands.Clear();unityRoofMaskCommands.Clear();UnityObjectMaskDraws=UnityRoofMaskDraws=0;
   if(DisableScenery)return true;
   float padX=Math.Abs(view.width)*canvas.Width/Math.Max(1,UnityMaskWidth)*2;
   float padY=Math.Abs(view.height)*canvas.Height/Math.Max(1,UnityMaskHeight)*2;
   var pixels=new Rect(view.xMin*canvas.Width-padX,view.yMin*canvas.Height-padY,view.width*canvas.Width+2*padX,view.height*canvas.Height+2*padY);
   foreach(var batch in unityMaskMergedMeshes){
    var bounds=batch.Mesh.bounds;
    if(bounds.max.x<pixels.xMin||bounds.min.x>pixels.xMax||bounds.max.y<pixels.yMin||bounds.min.y>pixels.yMax)continue;
    var commands=batch.Roof?unityRoofMaskCommands:unityObjectMaskCommands;
    commands.DrawMesh(batch.Mesh,Matrix4x4.identity,unityMergedMaskMaterials[batch.Page],0,0);
    if(batch.Roof)UnityRoofMaskDraws++;else UnityObjectMaskDraws++;
   }
   return true;
  }
  void RenderUnityMergedMask(CommandBuffer commands,RenderTexture output,bool vehicles){
   unityMaskCamera.targetTexture=output;unityMaskCamera.cullingMask=vehicles?1<<30:0;
   unityMaskCamera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha,commands);
   try {unityMaskCamera.RenderWithShader(unityMaskShader,"");}
   finally {unityMaskCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha,commands);}
  }
#endif
 }
}
