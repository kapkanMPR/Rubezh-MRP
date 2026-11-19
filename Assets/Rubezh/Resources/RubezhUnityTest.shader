Shader "Rubezh/Unity Test Scene"
{
    Properties
    {
        [HideInInspector] _GroundMultiTex("Ground texture palette", Float) = 0
        _GroundTex1("Ground atlas 1", 2D) = "white" {}
        _GroundTex2("Ground atlas 2", 2D) = "white" {}
        _GroundTex3("Ground atlas 3", 2D) = "white" {}
        [HideInInspector] _ActorRootDepth("Actor root depth offset", Float) = 0
        _BuildingMode("Building atlas", Float) = 0
        _BuildingRows("Building rows", Float) = 1
        _BuildingMask("Wall damage", 2D) = "black" {}
        _BuildingMeta("Wall metadata", 2D) = "black" {}
        [HideInInspector] _InstanceTint("Native tint", Vector) = (1, 1, 1, 1)
        [HideInInspector] _NativeInstance("Native sprite instance", Float) = 0
        [HideInInspector] _InstanceData("Depth and clip", Vector) = (0, 0, 0, 0)
        _MainTex("Atlas", 2D) = "white" {}
        _Occlusion("Terrain depth", 2D) = "black" {}
        _PictureDepth("Picture depth", 2D) = "black" {}
        [HideInInspector] _PackedRoofMask("Roof depth in green channel", Float) = 0
        _RoofOcclusion("Roof depth", 2D) = "black" {}
        _RevealMask("Roof reveal", 2D) = "white" {}
        _ClosureWood("Closure wood", 2D) = "white" {}
        _RevealBounds("Reveal bounds", Vector) = (0, 0, 1, 1)
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
            #include "RubezhBuildingMask.cginc"
            sampler2D _ClosureWood, _Occlusion, _PictureDepth, _RoofOcclusion, _RevealMask;
            #include "RubezhSceneryAtlas.cginc"
            float _PackedRoofMask;
            float4 _RevealBounds;
            UNITY_INSTANCING_BUFFER_START(NativeProps)
            UNITY_DEFINE_INSTANCED_PROP(float, _NativeInstance)
            UNITY_DEFINE_INSTANCED_PROP(float, _ActorRootDepth)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceData)
            UNITY_DEFINE_INSTANCED_PROP(float4, _InstanceTint)
            UNITY_INSTANCING_BUFFER_END(NativeProps)
            float4 _CanvasSize, _MapInfo;
            struct input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 depth : TEXCOORD1;
                float2 clipRange : TEXCOORD2;
                float2 groundSlot : TEXCOORD3;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct output
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 screen : TEXCOORD1;
                float2 depth : TEXCOORD2;
                float2 clipRange : TEXCOORD3;
                float4 color : COLOR;
                float4 nativeScreen : TEXCOORD4;
                float groundSlot : TEXCOORD5;
            };
            output vert(input v)
            {
                output o;
                UNITY_SETUP_INSTANCE_ID(v);
                float nativeInstance = UNITY_ACCESS_INSTANCED_PROP(NativeProps, _NativeInstance);
                float4 instanceData = UNITY_ACCESS_INSTANCED_PROP(NativeProps, _InstanceData);
                float4 instanceTint = UNITY_ACCESS_INSTANCED_PROP(NativeProps, _InstanceTint);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screen = mul(unity_ObjectToWorld, v.vertex).xy / _CanvasSize.xy;
                o.depth = nativeInstance > .5 ? instanceData.xy : v.depth;
                if (nativeInstance > .5 && UNITY_ACCESS_INSTANCED_PROP(NativeProps, _ActorRootDepth) > .5)
                    o.depth.x += mul(unity_ObjectToWorld, v.vertex).z * 1024.0;
                o.clipRange = nativeInstance > .5 ? instanceData.zw : v.clipRange;
                o.color = v.color * (nativeInstance > .5 ? instanceTint : float4(1, 1, 1, 1));
                o.nativeScreen = ComputeScreenPos(o.pos);
                o.groundSlot = v.groundSlot.x;
                return o;
            }
            fixed4 frag(output i) : SV_Target
            {
                if (i.screen.x * _CanvasSize.x < i.clipRange.x || i.screen.x * _CanvasSize.x >= i.clipRange.y)
                    discard;
                // Baked building images contain large transparent margins. In this
                // premultiplied, depth-write-free pass zero RGBA leaves the target intact.
                // Compare the filtered sample, including RGB, to preserve subpixel edges.
                bool buildingPicture = _BuildingMode > .5 && i.depth.y > 1.5 && i.depth.y <= 2.5;
                fixed4 buildingColor = 0;
                if (buildingPicture)
                {
                    buildingColor = tex2D(_MainTex, i.uv);
                    if (all(buildingColor == 0))
                        discard;
                }
                float z = i.depth.y > 2.5   ? 256 + i.uv.y * 4 - i.uv.x * 4 + .06
                          : i.depth.y > 1.5 ? tex2D(_PictureDepth, i.uv).r
                                            : i.depth.x;
                float2 depthUV = i.nativeScreen.xy / i.nativeScreen.w;
                if (i.depth.y > .5 || i.depth.y < -1.5)
                {
                    float occluder;
                    if (i.depth.y > 2.5)
                    {
                        float2 roof = tex2D(_RoofOcclusion, depthUV).rg;
                        occluder = _PackedRoofMask > .5 ? roof.g : roof.r;
                    }
                    else
                        occluder = tex2D(_Occlusion, depthUV).r;
                    if (occluder > z + .001)
                        discard;
                }
                if (i.depth.y < -1.5)
                {
                    float2 cell = floor(i.uv * float2(23, 15)) * 2 - float2(22, 14);
                    float phase = -i.depth.y - 2;
                    float pattern = fmod(fmod(cell.x + cell.y + phase, 7) + 7, 7);
                    if (cell.x * cell.x / 484 + cell.y * cell.y / 196 > 1 ||
                        pattern > floor(i.color.a * 255 / 63 + .1))
                        discard;
                    return fixed4(i.color.rgb, 1);
                }
                // Rectangles/diamonds may share any atlas material without sampling it.
                // Roof world UVs may be negative too, so restrict this marker to ordinary geometry.
                bool solid = i.depth.y < 1.5 && i.uv.x < -.5 && i.uv.y < -.5;
                if (!solid)
                    ClipBuilding(i.uv, i.color);
                fixed4 c;
                if (solid)
                    c = i.color;
                else if (i.depth.y > 4.5)
                    c = tex2D(_ClosureWood, float2((i.uv.x + i.uv.y) * 2, i.depth.x / 3)) * i.color;
                else if (i.depth.y > 3.5)
                    c = fixed4(.467, .478, .471, 1) * i.color;
                else if (buildingPicture)
                    c = buildingColor;
                else
                    c = SampleSceneryAtlas(i.uv, i.groundSlot) * (_BuildingMode > .5 ? fixed4(1, 1, 1, 1) : i.color);
                if (i.depth.y > 2.5)
                {
                    float opacity = tex2D(_RevealMask, (i.uv * 4 - _RevealBounds.xy) / _RevealBounds.zw).r;
                    c.rgb *= opacity;
                    c.a *= opacity;
                }
                if (i.depth.y < -.5 && i.depth.y > -1.5)
                {
                    float2 logical = i.screen * _MapInfo.zw;
                    float sum = (logical.x - _MapInfo.z * .5) / 11 + (_MapInfo.x + _MapInfo.y) * .5;
                    float diff = (logical.y - _MapInfo.w * .5 - 10) / 5 + (_MapInfo.y - _MapInfo.x) * .5;
                    float2 world = float2(sum - diff, sum + diff) * .5;
                    float distance = max(max(-world.x, world.x - _MapInfo.x), max(-world.y, world.y - _MapInfo.y));
                    float fade = min(.58, .12 + max(0, distance) * .012);
                    c.rgb = lerp(c.rgb, float3(18, 27, 32) / 255 * c.a, fade);
                }
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
