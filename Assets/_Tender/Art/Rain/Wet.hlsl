#ifndef WET_INCLUDED
#define WET_INCLUDED

// What rain does to the world's surfaces.
//
// How wet a point is comes from one global number - _RainWetness, which Rain.cs raises while it
// rains and lets fall afterwards - cut by shelter (nothing under a roof gets wet, RainMap.hlsl) and
// by which way the surface faces (the underside of a leaf stays dry).
//
// What being wet does is the same everywhere, scaled by how porous the material is: water fills
// the pores, so the surface stops scattering light back out (darker and a little more saturated)
// and a film of water sits on top (a broad, colourless gloss). Soil goes very dark, stone barely.

#include "RainMap.hlsl"

float _RainWetness;   // 0 dry .. 1 soaked, from Rain.cs

float WorldWetAt(float3 positionWS, float3 normalWS)
{
    float open = 1.0 - RainShelter(positionWS);
    float facingUp = saturate(normalWS.y * 1.5 + 0.5);
    return _RainWetness * open * facingUp;
}

// Darker and deeper in colour, by how much water the material takes in.
float3 WetAlbedo(float3 albedo, float wet, float porosity)
{
    float3 deeper = albedo * albedo * 2.0;                        // saturation up, brightness down
    float3 soaked = lerp(albedo, deeper, porosity);
    return lerp(albedo, soaked * (1.0 - 0.35 * porosity), wet);
}

// The film of water: a broad gloss that the whole surface shares, whatever it is made of.
float WetGloss(float3 normalWS, float3 lightDir, float3 toCamera, float wet)
{
    float3 halfDir = normalize(lightDir + toCamera);
    float film = pow(saturate(dot(normalWS, halfDir)), 48.0) * 0.5;
    return film * wet * saturate(dot(normalWS, lightDir) * 4.0);
}

// --- Puddles and ripples: the ground only ---------------------------------------------------

TEXTURE2D(_PuddleMap);
SAMPLER(sampler_PuddleMap);
float4 _PuddleArea;               // xy: terrain corner, zw: 1 / terrain size
float4 _PuddleMapUVScaleOffset;   // texel convention shared with the grass height map
float _RainIntensity;             // from Rain.cs

// 0 on open ground .. 1 in the bottom of a dip, from the baked map (Puddles.cs).
float PuddleAt(float2 xz)
{
    float2 local = (xz - _PuddleArea.xy) * _PuddleArea.zw;
    float2 uv = local * _PuddleMapUVScaleOffset.x + _PuddleMapUVScaleOffset.y;
    return SAMPLE_TEXTURE2D(_PuddleMap, sampler_PuddleMap, uv).r;
}

// How much standing water there is here: the dips fill only once the ground is soaked, and
// they are the last thing to dry.
float StandingWater(float2 xz, float wet)
{
    return PuddleAt(xz) * saturate(wet * 2.0 - 1.0);
}

float3 RainHash3(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yxz + 33.33);
    return frac((q.xxy + q.yzz) * q.zyx);
}

// Rings on standing water where drops land: the ground is cut into cells, each cell has one drop
// at a random spot on a random beat, and from it a ring runs outward and dies. Returns how much to
// tilt the water's normal, in the ground plane.
float2 RainRipple(float2 xz)
{
    const float cell = 0.18;          // metres between drops
    const float life = 0.7;           // seconds a ring lasts
    float2 id = floor(xz / cell);
    float2 tilt = 0.0;
    // A ring reaches into the neighbouring cells, so look at the nine around.
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 near = id + float2(x, y);
            float3 h = RainHash3(near);
            float2 centre = (near + h.xy) * cell;
            float age = frac(_Time.y / life + h.z);          // 0 just landed .. 1 gone
            float2 away = xz - centre;
            float d = length(away);
            float radius = age * cell;                       // the ring runs one cell out
            float wave = sin((d - radius) * 90.0) * exp(-abs(d - radius) * 40.0) * (1.0 - age);
            tilt += wave * away / max(d, 1e-3);
        }
    return tilt;
}

// What standing water looks like: darker (the water absorbs), the sky reflected off its surface,
// a sharp sun glint, and the rain's rings on it while it rains.
float3 PuddleShade(float3 ground, float3 normalWS, float3 positionWS, float3 lightDir, float3 sunLight, float3 toCamera, float water)
{
    float2 ripple = RainRipple(positionWS.xz) * _RainIntensity * 0.35;
    float3 surface = normalize(normalWS + float3(ripple.x, 0.0, ripple.y));

    // Fresnel: a puddle looked at from above shows its bottom, at a slant it shows the sky.
    float facing = saturate(dot(surface, toCamera));
    float fresnel = 0.04 + 0.96 * pow(1.0 - facing, 5.0);
    float3 sky = SampleSH(reflect(-toCamera, surface));
    float3 halfDir = normalize(lightDir + toCamera);
    float glint = pow(saturate(dot(surface, halfDir)), 400.0) * 2.0;

    float3 bottom = ground * 0.6;
    float3 wetLook = lerp(bottom, sky, fresnel) + sunLight * glint * fresnel;
    return lerp(ground, wetLook, water);
}

#endif
