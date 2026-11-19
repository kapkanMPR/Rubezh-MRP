Shader "Rubezh/Static Color Composite"
{
    Properties
    {
        _MainTex("Completed static scene", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct input
            {
                float4 vertex : POSITION;
            };
            struct output
            {
                float4 pos : SV_POSITION;
                float4 screen : TEXCOORD0;
            };
            output vert(input v)
            {
                output o;
                o.pos = float4(v.vertex.xy, 0, 1);
                // Same RT orientation as the existing camera occlusion-mask projection.
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }
            float4 frag(output i) : SV_Target
            {
                return tex2D(_MainTex, i.screen.xy / i.screen.w);
            }
            ENDCG
        }
    }
    Fallback Off
}
