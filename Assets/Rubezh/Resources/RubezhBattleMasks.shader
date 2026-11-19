Shader "Rubezh/BattleMasks"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _Knowledge, _Explored, _Records;
        float4 _Map; // projected pixels xy, world columns/rows zw
        float _Hardcore, _RecordCount, _RecordWidth;
        float4 recordAt(int index)
        {
            return tex2Dlod(_Records, float4((index + .5) / _RecordWidth, .5, 0, 0));
        }
        float2 knowledgeAt(float2 cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= _Map.z || cell.y >= _Map.w)
                return 0;
            float4 uv = float4((cell + .5) / _Map.zw, 0, 0);
            return float2(tex2Dlod(_Knowledge, uv).r, tex2Dlod(_Explored, uv).r);
        }
        float2 knowledge(float2 worldPoint)
        {
            float2 p = worldPoint - .5, cell = floor(p), f = frac(p);
            return lerp(lerp(knowledgeAt(cell), knowledgeAt(cell + float2(1, 0)), f.x),
                        lerp(knowledgeAt(cell + float2(0, 1)), knowledgeAt(cell + 1), f.x), f.y);
        }
        float4 fog(v2f_img input) : SV_Target
        {
            float2 pixel = float2(input.uv.x, 1 - input.uv.y) * _Map.xy;
            float sum = (pixel.x - _Map.x * .5) / 11 + (_Map.z + _Map.w) * .5;
            float diff = (pixel.y - _Map.y * .5 - 10) / 5 + (_Map.w - _Map.z) * .5;
            float2 worldPoint = float2(sum - diff, sum + diff) * .5;
            float2 visibility = knowledge(worldPoint);
            float light = visibility.r, explored = visibility.g, danger = 0;
            [loop] for (int index = 0; index < (int)_RecordCount; index++)
            {
                float4 patch = recordAt(index);
                float2 delta = worldPoint - patch.xy;
                float d2 = dot(delta, delta) / (patch.z * patch.z);
                if (d2 < 1)
                    danger = max(danger, patch.w * (1 - d2) * (1 - d2) * (1 - light));
            }
            float alpha = _Hardcore > .5 ? 1 - explored * (1 - .63 * (1 - light)) : .63 * (1 - light);
            float known = (_Hardcore > .5 && explored <= 0) ? 0 : 1;
            // Original Overlay truncates to bytes before the bilinear display pass.
            return floor(float4(danger, known * .015 * (1 - danger), known * .025 * (1 - danger),
                                max(alpha, danger)) *
                         255) /
                   255;
        }
        float4 night(v2f_img input) : SV_Target
        {
            float2 pixel = float2(input.uv.x, 1 - input.uv.y) * _Map.xy;
            float alpha = .28;
            [loop] for (int index = 0; index < (int)_RecordCount; index++)
            {
                float4 source = recordAt(index);
                float2 center = float2(_Map.x * .5 + (source.x + source.y - (_Map.z + _Map.w) * .5) * 11,
                                       _Map.y * .5 + (source.y - source.x - (_Map.w - _Map.z) * .5) * 5 + 10);
                float2 delta = (pixel - center) / (source.z * float2(15.55635, 7.071068));
                float d2 = dot(delta, delta);
                if (d2 < 1)
                    alpha = min(alpha, .28 * (1 - (1 - d2) * source.w));
            }
            return float4(.025, .04, .065, alpha);
        }
        v2f_img roofVertex(appdata_img input)
        {
            v2f_img output;
            // Draw matrix already includes Unity's GPU render-target projection.
            output.pos = mul(unity_ObjectToWorld, input.vertex);
            output.uv = input.texcoord;
            return output;
        }
        float4 roof(v2f_img input) : SV_Target
        {
            float distance = length((input.uv - .5) * 10);
            float t = saturate((distance - 3.25) / 1.75);
            float opacity = .5 + .5 * t * t * (3 - 2 * t);
            float shade = floor(opacity * 255 + .5) / 255;
            return float4(shade, shade, shade, 1);
        }
        sampler2D _RainStreak;
        float4 _RainScreen;
        float _RainTime;
        float4x4 _RainProjection;
        struct rainInput
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float2 id : TEXCOORD1;
        };
        v2f_img rainVertex(rainInput input)
        {
            float id = input.id.x, depth = fmod(id, 3) * .5;
            float2 at = float2(fmod(id * 173.37 + _RainTime * (37 + depth * 15), _RainScreen.x + 24) - 12,
                               fmod(id * 97.13 + _RainTime * (290 + depth * 100), _RainScreen.y + 56) - 56);
            float2 size = float2(6 + depth * 2, 23 + depth * 13);
            v2f_img output;
            output.pos = mul(_RainProjection, float4(at + input.vertex.xy * size, 0, 1));
            output.uv = input.uv;
            return output;
        }
        float4 rain(v2f_img input) : SV_Target
        {
            return float4(0, 0, 0, tex2D(_RainStreak, input.uv).a);
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment fog
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment night
            ENDCG
        }
        Pass
        {
            Blend One One
            BlendOp Min
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex roofVertex
            #pragma fragment roof
            ENDCG
        }
        Pass
        {
            ColorMask A
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex rainVertex
            #pragma fragment rain
            ENDCG
        }
    }
}
