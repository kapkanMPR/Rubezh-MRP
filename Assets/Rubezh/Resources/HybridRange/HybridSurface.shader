Shader "Rubezh/Hybrid Range Surface"
{
    Properties
    {
        _MainTex("Authored surface", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _TileScale("Tiles per world unit", Vector) = (1,1,0,0)
        _UVRect("Repeatable source area", Vector) = (0,0,1,1)
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Cull Back
        ZWrite On
        ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _TileScale, _UVRect;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : TEXCOORD1;
                fixed4 color : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _TileScale.xy;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = v.color * _Color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                // Derivatives are taken before frac: crossing a repeat seam must not
                // select a blurred mip. Original raster data stays untouched.
                float2 uv = _UVRect.xy + frac(i.uv) * _UVRect.zw;
                fixed3 albedo =
                    tex2Dgrad(_MainTex, uv, ddx(i.uv) * _UVRect.zw, ddy(i.uv) * _UVRect.zw).rgb * i.color.rgb;
                half3 normal = normalize(i.normal);
                // A fixed, soft overcast key fits the existing painted sprite art.
                // This deliberately needs no global light, shadow map, or deferred pass.
                half key = saturate(dot(normal, half3(-.3904h, .7809h, .4880h)));
                half sky = saturate(normal.y * .5h + .5h);
                half shade = .55h + .32h * key + .13h * sky;
                return fixed4(albedo * shade, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
