Shader "Rubezh/HybridRange/Sprite"
{
    Properties { _MainTex("Soldier atlas",2D)="white"{} }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
        struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
        v2f vert(appdata v) {v2f o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
        fixed4 depth(v2f i):SV_Target {clip(tex2D(_MainTex,i.uv).a*i.color.a-.98);return 0;}
        fixed4 color(v2f i):SV_Target {return tex2D(_MainTex,i.uv)*i.color;}
        ENDCG
        // Only the opaque core writes depth. The second pass retains all original
        // translucent edge coverage, unlike alpha-cutting the visible sprite.
        Pass
        {
            ZWrite On ZTest LEqual ColorMask 0
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            ENDCG
        }
        Pass
        {
            ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment color
            ENDCG
        }
    }
}
