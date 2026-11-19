Shader "Rubezh/Native Pooled Effect"
{
    Properties
    {
        _MainTex("Sprite", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _Effect("Kind phase density", Vector) = (0, 0, 0, 0)
        _EffectDepth("Start end depth", Vector) = (0, 0, 0, 0)
        _UVRect("Atlas region", Vector) = (0, 0, 1, 1)
        _Occlusion("Terrain depth", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Occlusion;
            UNITY_INSTANCING_BUFFER_START(EffectProps)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Effect)
            UNITY_DEFINE_INSTANCED_PROP(float4, _EffectDepth)
            UNITY_DEFINE_INSTANCED_PROP(float4, _UVRect)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(EffectProps)
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 radial : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screen : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float4 effect = UNITY_ACCESS_INSTANCED_PROP(EffectProps, _Effect);
                if (effect.x > 3.5 && effect.x < 4.5)
                {
                    float2 offset = v.radial * float2(effect.y, effect.y * .5);
                    v.vertex.xy += sign(offset) * floor(abs(offset));
                }
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screen = ComputeScreenPos(o.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 effect = UNITY_ACCESS_INSTANCED_PROP(EffectProps, _Effect);
                float4 depth = UNITY_ACCESS_INSTANCED_PROP(EffectProps, _EffectDepth);
                float4 uvRect = UNITY_ACCESS_INSTANCED_PROP(EffectProps, _UVRect);
                float z = lerp(depth.x, depth.y, effect.x > 4.5 ? i.uv.y : i.uv.x);
                if (tex2D(_Occlusion, i.screen.xy / i.screen.w).r > z + .001)
                    discard;
                if (effect.x > .5 && effect.x < 1.5)
                {
                    float2 p = i.uv * 2 - 1;
                    if (dot(p, p) > 1)
                        discard;
                }
                if (effect.x > 1.5 && effect.x < 2.5)
                {
                    float2 cell = floor(i.uv * float2(23, 15)) * 2 - float2(22, 14);
                    float pattern = fmod(fmod(cell.x + cell.y + effect.y, 7) + 7, 7);
                    if (cell.x * cell.x / 484 + cell.y * cell.y / 196 > 1 || pattern > effect.z)
                        discard;
                }
                fixed4 color = UNITY_ACCESS_INSTANCED_PROP(EffectProps, _Color);
                if ((effect.x > 2.5 && effect.x < 3.5) || effect.x > 4.5)
                    color *= tex2D(_MainTex, uvRect.xy + i.uv * uvRect.zw);
                if (effect.x > 4.5)
                    color.rgb *= color.a; // Imported crater is straight alpha.
                // Native sprite pages already contain premultiplied RGB, like the main scene shader.
                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
