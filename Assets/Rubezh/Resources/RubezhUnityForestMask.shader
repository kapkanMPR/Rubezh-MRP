Shader "Rubezh/Unity Forest Mask" {
 Properties { [HideInInspector] _ActorRootDepth("Actor root depth offset",Float)=0 [HideInInspector] _NativeInstance("Native instance",Float)=0 [HideInInspector] _InstanceData("Native depth and clip",Vector)=(0,0,0,0) [HideInInspector] _InstanceTint("Native tint",Vector)=(1,1,1,1)  _BuildingMode("Building atlas",Float)=0 _BuildingRows("Building rows",Float)=1 _BuildingMask("Wall damage",2D)="black"{} _BuildingMeta("Wall metadata",2D)="black"{}  _MainTex("Atlas",2D)="white"{} _PictureDepth("Picture depth",2D)="black"{} }
 SubShader {
  Tags { "RenderType"="Transparent" "Queue"="Transparent" "DisableBatching"="True" }
  Cull Off ZWrite Off ZTest Always
  Pass {
   Blend One One
   BlendOp Max
   ColorMask R
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   #include "RubezhBuildingMask.cginc"
   sampler2D _MainTex, _PictureDepth;
   UNITY_INSTANCING_BUFFER_START(NativeProps)
    UNITY_DEFINE_INSTANCED_PROP(float,_NativeInstance)
    UNITY_DEFINE_INSTANCED_PROP(float,_ActorRootDepth)
    UNITY_DEFINE_INSTANCED_PROP(float4,_InstanceData)
    UNITY_DEFINE_INSTANCED_PROP(float4,_InstanceTint)
   UNITY_INSTANCING_BUFFER_END(NativeProps)
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float2 clipRange:TEXCOORD2; float4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float2 clipRange:TEXCOORD2; float canvasX:TEXCOORD3; float4 color:COLOR; };
   v2f vert(appdata v) {
    UNITY_SETUP_INSTANCE_ID(v);
    float nativeInstance=UNITY_ACCESS_INSTANCED_PROP(NativeProps,_NativeInstance);
    float4 data=UNITY_ACCESS_INSTANCED_PROP(NativeProps,_InstanceData);
    float4 tint=UNITY_ACCESS_INSTANCED_PROP(NativeProps,_InstanceTint);
    v2f o; o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.depth=nativeInstance>.5?data.xy:v.depth;
    if(nativeInstance>.5 && UNITY_ACCESS_INSTANCED_PROP(NativeProps,_ActorRootDepth)>.5)
     o.depth.x+=mul(unity_ObjectToWorld,v.vertex).z*1024.0;
    o.clipRange=nativeInstance>.5?data.zw:v.clipRange;o.canvasX=mul(unity_ObjectToWorld,v.vertex).x;o.color=v.color*(nativeInstance>.5?tint:float4(1,1,1,1));return o;
   }
   float4 frag(v2f i):SV_Target {
    if(i.canvasX<i.clipRange.x || i.canvasX>=i.clipRange.y || i.depth.y<.5 || i.depth.y>2.5)discard;
    // Transparent atlas texels cannot write the mask; avoid building metadata reads for them.
    bool solid=i.depth.y<1.5 && i.uv.x<-.5 && i.uv.y<-.5;
    if(solid)clip(i.color.a-.5);
    else {clip(tex2D(_MainTex,i.uv).a*i.color.a-.5);ClipBuilding(i.uv,i.color);}
    float z=i.depth.y>1.5 ? tex2D(_PictureDepth,i.uv).r : i.depth.x;
    return float4(z,0,0,1);
   }
   ENDCG
  }
 }
 Fallback Off
}
