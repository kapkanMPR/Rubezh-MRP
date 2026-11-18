Shader "Rubezh/HD GPU" {
 Properties { [HideInInspector] _GroundMultiTex("Ground texture palette",Float)=0 _GroundTex1("Ground atlas 1",2D)="white"{} _GroundTex2("Ground atlas 2",2D)="white"{} _GroundTex3("Ground atlas 3",2D)="white"{}  _BuildingMode("Building atlas",Float)=0 _BuildingRows("Building rows",Float)=1 _BuildingMask("Wall damage",2D)="black"{} _BuildingMeta("Wall metadata",2D)="black"{}  _MainTex("Atlas",2D)="white"{} _Occlusion("Terrain depth",2D)="black"{} _PictureDepth("Picture depth",2D)="black"{} _RoofOcclusion("Roof depth",2D)="black"{} _RevealMask("Roof reveal",2D)="white"{} _ClosureWood("Closure wood",2D)="white"{} _RevealBounds("Reveal bounds",Vector)=(0,0,1,1) }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" }
  Cull Off ZWrite Off ZTest Always
  Pass {
   Blend One OneMinusSrcAlpha
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "RubezhBuildingMask.cginc"
   sampler2D _ClosureWood, _MainTex, _Occlusion, _PictureDepth, _RoofOcclusion, _RevealMask;
   float4 _RevealBounds;
   float4 _CanvasSize, _MapInfo; float _NativeDepth;
   float4x4 _PixelToClip, _DepthPixelToClip;
   struct input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float2 clipRange:TEXCOORD2; float4 color:COLOR; };
   struct output { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 screen:TEXCOORD1; float2 depth:TEXCOORD2; float2 clipRange:TEXCOORD3; float4 color:COLOR; float2 nativeUV:TEXCOORD4; };
   output vert(input v){output o;o.pos=mul(_PixelToClip,v.vertex);o.uv=v.uv;o.screen=v.vertex.xy/_CanvasSize.xy;o.depth=v.depth;o.clipRange=v.clipRange;o.color=v.color;
    float4 depthPos=mul(_DepthPixelToClip,v.vertex);
    o.nativeUV=depthPos.xy/depthPos.w*.5+.5;
    #if UNITY_UV_STARTS_AT_TOP
    o.nativeUV.y=1-o.nativeUV.y;
    #endif
    return o;}
   fixed4 frag(output i):SV_Target {
    if(i.screen.x*_CanvasSize.x<i.clipRange.x || i.screen.x*_CanvasSize.x>=i.clipRange.y)discard;
    float z=i.depth.y>2.5 ? 256+i.uv.y*4-i.uv.x*4+.06 : i.depth.y>1.5 ? tex2D(_PictureDepth,i.uv).r : i.depth.x;
    float2 depthUV=_NativeDepth>.5 ? i.nativeUV : i.screen;
    if((i.depth.y>.5 || i.depth.y<-1.5) && (i.depth.y>2.5?tex2D(_RoofOcclusion,depthUV).r:tex2D(_Occlusion,depthUV).r)>z+.001)discard;
    if(i.depth.y<-1.5){
     float2 cell=floor(i.uv*float2(23,15))*2-float2(22,14);
     float phase=-i.depth.y-2;
     float pattern=fmod(fmod(cell.x+cell.y+phase,7)+7,7);
     if(cell.x*cell.x/484+cell.y*cell.y/196>1 || pattern>floor(i.color.a*255/63+.1))discard;
     return fixed4(i.color.rgb,1);
    }
    ClipBuilding(i.uv,i.color);
    fixed4 c=tex2D(_MainTex,i.uv)*(_BuildingMode>.5?fixed4(1,1,1,1):i.color);
    if(i.depth.y>3.5)c=i.depth.y>4.5?tex2D(_ClosureWood,float2((i.uv.x+i.uv.y)*2,i.depth.x/3))*i.color:fixed4(.467,.478,.471,1)*i.color;
    if(i.depth.y>2.5){float opacity=tex2D(_RevealMask,(i.uv*4-_RevealBounds.xy)/_RevealBounds.zw).r;c.rgb*=opacity;c.a*=opacity;}
    if(i.depth.y<-.5 && i.depth.y>-1.5){
     float2 logical=i.screen*_MapInfo.zw;
     float sum=(logical.x-_MapInfo.z*.5)/11+(_MapInfo.x+_MapInfo.y)*.5;
     float diff=(logical.y-_MapInfo.w*.5-10)/5+(_MapInfo.y-_MapInfo.x)*.5;
     float2 world=float2(sum-diff,sum+diff)*.5;
     float distance=max(max(-world.x,world.x-_MapInfo.x),max(-world.y,world.y-_MapInfo.y));
     float fade=min(.58,.12+max(0,distance)*.012);
     c.rgb=lerp(c.rgb,float3(18,27,32)/255*c.a,fade);
    }
    return c;
   }
   ENDCG
  }
  Pass {
   Blend Off
   CGPROGRAM
   #pragma vertex vert_background
   #pragma fragment frag
   #include "UnityCG.cginc"
   #include "RubezhBuildingMask.cginc"
   sampler2D _MainTex;
   float4x4 _PixelToClip, _DepthPixelToClip;
   float _CachedScene;
   v2f_img vert_background(appdata_img v){
    v2f_img o;o.pos=mul(_PixelToClip,v.vertex);o.uv=v.texcoord;
    if(_CachedScene>.5){
     float4 p=mul(_DepthPixelToClip,v.vertex);o.uv=p.xy/p.w*.5+.5;
     #if UNITY_UV_STARTS_AT_TOP
     o.uv.y=1-o.uv.y;
     #endif
    }
    return o;
   }
   fixed4 frag(v2f_img i):SV_Target{fixed4 c=tex2D(_MainTex,i.uv);c.a=1;return c;}
   ENDCG
  }
  Pass {
   Blend Off
   CGPROGRAM
   #pragma vertex vert_img
   #pragma fragment straight_alpha
   #include "UnityCG.cginc"
   #include "RubezhBuildingMask.cginc"
   sampler2D _MainTex;
   fixed4 straight_alpha(v2f_img i):SV_Target {
    fixed4 c=tex2D(_MainTex,i.uv);
    return c.a>0.0001 ? fixed4(c.rgb/c.a,c.a) : fixed4(0,0,0,0);
   }
   ENDCG
  }

  // Viewport-resolution static occlusion. Max blending is independent of object order.
  Pass {
   Blend One One
   BlendOp Max
   ColorMask R
   CGPROGRAM
   #pragma vertex vert_depth
   #pragma fragment frag_depth
   #pragma target 3.0
   #include "UnityCG.cginc"
   #include "RubezhBuildingMask.cginc"
   sampler2D _ClosureWood, _MainTex, _PictureDepth;
   float4x4 _PixelToClip, _DepthPixelToClip;
   struct depth_input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float2 clipRange:TEXCOORD2; float4 color:COLOR; };
   struct depth_output { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; float2 depth:TEXCOORD1; float2 clipRange:TEXCOORD2; float canvasX:TEXCOORD3; float4 color:COLOR; };
   depth_output vert_depth(depth_input v){depth_output o;o.pos=mul(_DepthPixelToClip,v.vertex);o.uv=v.uv;o.depth=v.depth;o.clipRange=v.clipRange;o.canvasX=v.vertex.x;o.color=v.color;return o;}
   float4 frag_depth(depth_output i):SV_Target {
    if(i.canvasX<i.clipRange.x || i.canvasX>=i.clipRange.y || i.depth.y<.5 || i.depth.y>2.5)discard;
    ClipBuilding(i.uv,i.color);
    clip(tex2D(_MainTex,i.uv).a*i.color.a-.5);
    float z=i.depth.y>1.5 ? tex2D(_PictureDepth,i.uv).r : i.depth.x;
    return float4(z,0,0,1);
   }
   ENDCG
  }

 }
 Fallback Off
}
