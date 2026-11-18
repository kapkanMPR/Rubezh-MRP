using UnityEngine;
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
using UnityEngine.Rendering;
#endif

namespace Rubezh.UnityPort {
 public sealed partial class HdGpuRenderer {
  public bool UnityStaticColorCache=true;
  public bool UnityStaticColorCacheActive {get;private set;}
  public bool UnityStaticColorCacheHit {get;private set;}
  public bool UnityStaticColorDeferred {get;private set;}
  // Retained for older reports. 221 performs no CopyTexture operations.
  public int UnityStaticColorCopyCount {get;private set;}
  public int UnityStaticColorCompositeCount {get;private set;}
  public double UnityStaticColorRenderMs {get;private set;}
  public string UnityStaticColorFailure {get;private set;}
  public int UnityExecutedStaticDraws {get;private set;}
  public long UnityStaticColorBytes {get;private set;}
#if UNITY_5_3_OR_NEWER || RUBEZH_NATIVE_TEST_CONTRACT
  RenderTexture unityStaticColor,unityStaticColorTarget;
  Camera unityStaticColorCamera;
  GameObject unityStaticColorCameraObject;
  CommandBuffer unityStaticRestoreCommands;
  Material unityStaticCompositeMaterial;
  Mesh unityStaticCompositeMesh;
  bool unityStaticColorValid,unityStaticColorCapturePending,unityStaticColorFaulted;
  bool unityStaticColorViewObserved;
  Rect unityStaticColorObservedView;
  StaticColorStamp unityStaticColorStamp,unityStaticColorPendingStamp;

  struct StaticColorStamp {
   public int Commands,Masks,BuildingMasks,Materials,Pages;
   public long FullUploads,RegionUploads;
   public Color Background;
   public bool Same(StaticColorStamp other)=>Commands==other.Commands&&Masks==other.Masks&&BuildingMasks==other.BuildingMasks&&Materials==other.Materials&&Pages==other.Pages&&FullUploads==other.FullUploads&&RegionUploads==other.RegionUploads&&Background.r==other.Background.r&&Background.g==other.Background.g&&Background.b==other.Background.b&&Background.a==other.Background.a;
  }
  StaticColorStamp ReadStaticColorStamp()=>new StaticColorStamp {
   Commands=UnityStaticCommandBuilds,Masks=UnityObjectMaskBuilds,BuildingMasks=NativeBuildingMaskUpdates,
   Materials=unityStaticMaterialRevision,Pages=PicturePagesUploaded,
   FullUploads=PictureFullUploads,RegionUploads=PictureRegionUploads,Background=unityCamera.backgroundColor
  };

  void ReleaseStaticColorCache(){
   // Detach references before releasing their texture, material, mesh or camera.
   SelectStaticPrefix(unityStaticCommands,null);
   if(unityStaticColorCamera!=null){
    unityStaticColorCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha,unityStaticCommands);
    unityStaticColorCamera.targetTexture=null;unityStaticColorCamera=null;
   }
   if(unityStaticColorCameraObject!=null){UnityEngine.Object.Destroy(unityStaticColorCameraObject);unityStaticColorCameraObject=null;}
   if(unityStaticRestoreCommands!=null){unityStaticRestoreCommands.Release();unityStaticRestoreCommands=null;}
   if(unityStaticCompositeMaterial!=null){UnityEngine.Object.Destroy(unityStaticCompositeMaterial);unityStaticCompositeMaterial=null;}
   if(unityStaticCompositeMesh!=null){UnityEngine.Object.Destroy(unityStaticCompositeMesh);unityStaticCompositeMesh=null;}
   if(unityStaticColor!=null){unityStaticColor.Release();UnityEngine.Object.Destroy(unityStaticColor);unityStaticColor=null;}
   unityStaticColorTarget=null;unityStaticColorValid=unityStaticColorCapturePending=false;
   UnityStaticColorCacheActive=UnityStaticColorCacheHit=false;UnityStaticColorBytes=0;
  }

  bool CreateStaticColorCache(){
   var compositeShader=Resources.Load<Shader>("RubezhStaticColorComposite");
   if(compositeShader==null){UnityStaticColorFailure="Static colour composite shader resource missing";return false;}
   if(!compositeShader.isSupported){UnityStaticColorFailure="Static colour composite shader unsupported";return false;}
   // Same size, format, colour space and sample count as the battle target.
   // The cache is a separate completed camera render; never copy into an active
   // camera attachment. Tile renderers may discard such transfer writes when
   // the camera resumes its native render pass.
   unityStaticColor=new RenderTexture(target.width,target.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
   unityStaticColor.name="Rubezh static colour camera target";unityStaticColor.filterMode=FilterMode.Point;unityStaticColor.wrapMode=TextureWrapMode.Clamp;
   if(!unityStaticColor.Create()){UnityStaticColorFailure="Static colour render texture allocation failed";return false;}
   unityStaticColorTarget=target;UnityStaticColorBytes=(long)target.width*target.height*4;
   unityStaticColorCameraObject=new GameObject("Static colour cache camera");
   if(unityRoot!=null)unityStaticColorCameraObject.transform.SetParent(unityRoot.transform,false);
   unityStaticColorCamera=unityStaticColorCameraObject.AddComponent<Camera>();
   unityStaticColorCamera.enabled=false;unityStaticColorCamera.orthographic=true;
   unityStaticColorCamera.clearFlags=CameraClearFlags.SolidColor;
   unityStaticColorCamera.allowHDR=false;unityStaticColorCamera.allowMSAA=false;unityStaticColorCamera.useOcclusionCulling=false;
   // Only the retained static buffer is rendered. Actors, effects and roofs
   // remain live in the main camera and never become part of these pixels.
   unityStaticColorCamera.cullingMask=0;
   unityStaticColorCamera.targetTexture=unityStaticColor;
   unityStaticColorCamera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha,unityStaticCommands);
   unityStaticCompositeMaterial=new Material(compositeShader){mainTexture=unityStaticColor};
   unityStaticCompositeMesh=new Mesh{name="Rubezh static colour fullscreen triangle"};
   unityStaticCompositeMesh.vertices=new[]{new Vector3(-1,-1,0),new Vector3(-1,3,0),new Vector3(3,-1,0)};
   unityStaticCompositeMesh.triangles=new[]{0,1,2};unityStaticCompositeMesh.RecalculateBounds();
   unityStaticRestoreCommands=new CommandBuffer{name="Rubezh draw completed static colour"};
   unityStaticRestoreCommands.DrawMesh(unityStaticCompositeMesh,Matrix4x4.identity,unityStaticCompositeMaterial,0,0);
   return true;
  }

  void UpdateStaticColorCache(){
   UnityStaticColorRenderMs=0;
   UnityStaticColorDeferred=false;
   UnityEngine.Profiling.Profiler.BeginSample("Rubezh.StaticColorCache");
   try {UpdateStaticColorCacheCore();}
   catch(System.Exception error){
    // A cache failure must display the ordinary complete scene this frame,
    // never a cleared target or a partly captured image. Avoid retrying a
    // failed graphics path every frame until the battle renderer is recreated.
    ReleaseStaticColorCache();unityStaticColorFaulted=true;
    UnityStaticColorFailure=error.GetType().Name+": "+error.Message;
    UnityExecutedStaticDraws=UnityExplicitDraws;
    Debug.LogWarning("Rubezh static colour cache disabled; using complete scene rendering: "+error.Message);
   }
   finally {UnityEngine.Profiling.Profiler.EndSample();}
  }
  void UpdateStaticColorCacheCore(){
   UnityStaticColorCacheHit=false;unityStaticColorCapturePending=false;
   UnityExecutedStaticDraws=UnityExplicitDraws;
   bool supported=UnityCachedStaticCommands&&UnityStaticColorCache&&!unityStaticColorFaulted&&target!=null&&target.IsCreated()&&
    target.format==RenderTextureFormat.ARGB32&&!target.sRGB&&target.antiAliasing==1;
   if(!supported){
    SelectStaticPrefix(unityStaticCommands,null);
    UnityStaticColorCacheActive=false;unityStaticColorValid=false;unityStaticColorViewObserved=false;return;
   }
   bool moving=unityStaticColorViewObserved&&unityStaticColorObservedView!=unityCommandsView;
   unityStaticColorObservedView=unityCommandsView;unityStaticColorViewObserved=true;
   if(moving){
    // During a pan or zoom these pixels would be replaced on the very next
    // frame. Draw the current geometry directly in the main camera instead of
    // paying for an extra camera and composite every frame. The first stable
    // view captures once; no previous-view pixels are ever displayed.
    SelectStaticPrefix(unityStaticCommands,null);
    UnityStaticColorCacheActive=false;unityStaticColorValid=false;UnityStaticColorDeferred=true;return;
   }
   if(unityStaticColor==null||unityStaticColorTarget!=target||unityStaticColor.width!=target.width||unityStaticColor.height!=target.height||!unityStaticColor.IsCreated()){
    ReleaseStaticColorCache();
    if(!CreateStaticColorCache()){
     ReleaseStaticColorCache();unityStaticColorFaulted=true;
     Debug.LogWarning("Rubezh static colour cache unavailable; using complete scene rendering.");return;
    }
   }
   UnityStaticColorCacheActive=true;
   unityStaticColorPendingStamp=ReadStaticColorStamp();
   if(unityStaticColorValid&&unityStaticColorStamp.Same(unityStaticColorPendingStamp)){
    UnityStaticColorCacheHit=true;
    UnityExecutedStaticDraws=unityRoofCommandDraws;
   }else{
    unityStaticColorValid=false;
    unityStaticColorCamera.backgroundColor=unityCamera.backgroundColor;
    unityStaticColorCamera.nearClipPlane=unityCamera.nearClipPlane;unityStaticColorCamera.farClipPlane=unityCamera.farClipPlane;
    unityStaticColorCamera.worldToCameraMatrix=unityCamera.worldToCameraMatrix;
    unityStaticColorCamera.projectionMatrix=unityCamera.projectionMatrix;
    unityStaticColorCamera.cullingMatrix=unityCamera.cullingMatrix;
    var previous=RenderTexture.active;var clock=System.Diagnostics.Stopwatch.StartNew();
    UnityEngine.Profiling.Profiler.BeginSample("Rubezh.StaticColorCameraRender");
    try {unityStaticColorCamera.Render();unityStaticColorCapturePending=true;}
    finally {UnityStaticColorRenderMs=clock.Elapsed.TotalMilliseconds;UnityEngine.Profiling.Profiler.EndSample();RenderTexture.active=previous;}
   }
   // Sampling a different, completed RT is a normal opaque raster draw inside
   // the main pass. Blend Off preserves the already-composited RGBA values.
   SelectStaticPrefix(unityStaticRestoreCommands,null);
  }
  void CompleteStaticColorCache(bool success){
   if(!UnityStaticColorCacheActive)return;
   if(!success){unityStaticColorValid=false;UnityStaticColorCacheHit=false;unityStaticColorCapturePending=false;return;}
   UnityStaticColorCompositeCount++;
   if(unityStaticColorCapturePending){
    unityStaticColorStamp=unityStaticColorPendingStamp;unityStaticColorValid=true;
    unityStaticColorCapturePending=false;SceneColorBuilds++;
   }else if(UnityStaticColorCacheHit){SceneColorHits++;}
  }
#endif
 }
}
