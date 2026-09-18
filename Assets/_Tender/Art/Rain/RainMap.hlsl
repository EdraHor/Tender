#ifndef RAIN_MAP_INCLUDED
#define RAIN_MAP_INCLUDED

// Where the rain lands, from RainMap.cs: a top-down map around the camera holding, for each
// point of the sky, the height of the first thing under it. Anything below that height is under
// cover. Outside the map the answer is "nothing here" (-1000 m), so rain far away falls to
// wherever the drop shader clips it.

TEXTURE2D(_RainMap);
SAMPLER(sampler_RainMap);
float4 _RainMapArea;   // xy: world XZ of the map's corner, z: 1 / size, w: size

float RainLandingHeight(float2 xz)
{
    float2 uv = (xz - _RainMapArea.xy) * _RainMapArea.z;
    return SAMPLE_TEXTURE2D_LOD(_RainMap, sampler_RainMap, uv, 0).r;
}

// 1 when something is over this point - a roof, a branch - and 0 in the open.
float RainShelter(float3 positionWS)
{
    return saturate((RainLandingHeight(positionWS.xz) - positionWS.y - 0.1) * 4.0);
}

#endif
