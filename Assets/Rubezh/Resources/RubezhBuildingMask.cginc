#ifndef RUBEZH_BUILDING_MASK_INCLUDED
#define RUBEZH_BUILDING_MASK_INCLUDED
sampler2D _BuildingMask, _BuildingMeta;
float _BuildingMode, _BuildingRows;
float4 BuildingMeta(float row,float column){return tex2D(_BuildingMeta,float2((column+.5)/8,(row+.5)/_BuildingRows));}
void ClipBuilding(float2 uv,float4 encoded)
{
    if(_BuildingMode<.5)return;
    float row=floor(encoded.r*255+.5)+floor(encoded.g*255+.5)*256;
    float4 resolution=BuildingMeta(row,6);
    if(resolution.z<.5)return;
    float4 bounds=BuildingMeta(row,0),axis=BuildingMeta(row,1),window=BuildingMeta(row,2),limits=BuildingMeta(row,3),mask=BuildingMeta(row,4),atlas=BuildingMeta(row,5);
    float2 local=saturate((uv-atlas.xy)/atlas.zw);
    // Match the exact source texel tested by the old CPU clip, including edge extrusion.
    float2 pixel=min(floor(local*resolution.xy),resolution.xy-1);
    float2 position=bounds.xy+(pixel+.5)/resolution.xy*bounds.zw;
    float u=(position.x/11-axis.x-axis.y)/(axis.z-axis.x+axis.w-axis.y);
    float2 world=lerp(axis.xy,axis.zw,u);
    float z=(5*(world.y-world.x)-position.y)*2.8/24;
    bool glass=resolution.w>.5&&u>=window.y&&u<=window.z&&z>=limits.x&&z<=limits.y;
    if(glass){clip(.5-window.w);return;}
    float2 cell=clamp(floor(float2(u,z/window.x)*mask.xy),0,mask.xy-1);
    clip(.5-tex2D(_BuildingMask,(limits.zw+cell+.5)/mask.zw).r);
}
#endif
