#ifndef RUBEZH_SCENERY_ATLAS_INCLUDED
#define RUBEZH_SCENERY_ATLAS_INCLUDED

sampler2D _MainTex, _GroundTex1, _GroundTex2, _GroundTex3;
float _GroundMultiTex;

// Each original triangle has a constant selector. Both colour and replacement
// mask passes sample the original texture with the same UVs and filter state.
fixed4 SampleSceneryAtlas(float2 uv, float slot)
{
    if (_GroundMultiTex < .5 || slot < .5)
        return tex2D(_MainTex, uv);
    if (slot < 1.5)
        return tex2D(_GroundTex1, uv);
    if (slot < 2.5)
        return tex2D(_GroundTex2, uv);
    return tex2D(_GroundTex3, uv);
}

#endif
