Shader "Rubezh/LoadingFireReveal"
{
    Properties
    {
        _MainTex ("Top title artwork", 2D) = "white" {}
        _ColdTex ("Empty grayscale city", 2D) = "white" {}
        _Progress ("Progress", Range(0,1)) = 0
        _Clock ("Unscaled clock", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            sampler2D _MainTex, _ColdTex;
            float _Progress, _Clock;
            float front(float2 uv)
            {
                float t = _Clock;
                float wave = 0.042 * sin(uv.y * 37.0 + t * 1.5 + sin(uv.y * 17.0 - t));
                wave += 0.022 * sin(uv.y * 89.0 - t * 2.5 + sin(uv.x * 27.0));
                wave += 0.012 * sin(uv.y * 173.0 + uv.x * 39.0 - t * 3.8);
                return lerp(-0.10, 1.12, _Progress) + wave;
            }
            float reveal(float2 uv)
            {
                if (_Progress <= 0) return 0.0;
                if (_Progress >= 1) return 1.0;
                float wisps = 0.009 * sin(uv.x * 117.0 + uv.y * 93.0 - _Clock * 4.1);
                return 1.0 - smoothstep(-0.045, 0.045, uv.x - front(uv) + wisps);
            }
            float flash(float2 uv, float2 center, float seed)
            {
                float beat = floor(_Clock * 6.0 + seed);
                float random = frac(sin(beat * 12.9898 + seed * 78.233) * 43758.5453);
                float pulse = step(0.70, random) * (1.0 - smoothstep(0.02, 0.30, frac(_Clock * 6.0 + seed)));
                float2 d = (uv - center) * float2(1.77778, 1.0);
                return exp(-dot(d, d) / 0.00012) * pulse * reveal(center);
            }
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 hot = tex2D(_MainTex, uv).rgb;
                float luma = dot(hot, float3(0.2126, 0.7152, 0.0722));
                // Select only warm pigments in upper title. City, counters and gaps stay unchanged.
                float mask = smoothstep(0.004, 0.012, hot.r - max(hot.g, hot.b)) * smoothstep(0.50, 0.54, uv.y);
                float3 cold = tex2D(_ColdTex, uv).rgb;
                float shown = reveal(uv);
                float3 c = lerp(cold, hot, shown * smoothstep(0.50, 0.54, uv.y));
                float fire = 0.045 * sin(_Clock * 3.7 + uv.x * 31.0) + 0.025 * sin(_Clock * 7.1 - uv.y * 43.0);
                c += hot * fire * mask * shown;
                float edge = front(uv);
                float rim = exp(-abs(uv.x - edge) * 80.0) * mask * step(0.001, _Progress) * (1.0 - step(0.999, _Progress));
                c += float3(0.22, 0.075, 0.01) * rim;
                float flashes = flash(uv, float2(0.510, 0.628), 0.13) + flash(uv, float2(0.689, 0.629), 0.67);
                c += float3(0.65, 0.39, 0.14) * flashes * mask;
                // Soft lower scrim, no hard-edged rectangle behind the progress/status.

                return fixed4(saturate(c), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
