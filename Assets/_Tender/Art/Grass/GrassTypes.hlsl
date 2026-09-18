#ifndef TENDER_GRASS_TYPES_INCLUDED
#define TENDER_GRASS_TYPES_INCLUDED

#include "GrassNoise.hlsl"

// The kinds of grass in a field, and where each one grows.
//
// GrassField.cs uploads one GrassType per asset in its list. Type 0 grows everywhere. Every type
// after it grows inside its own ZONES - wherever a slow noise rises above a threshold, or wherever
// it was painted - and wins over the types before it.
//
// Plain maths plus one buffer, like GrassNoise.hlsl, because three very different shaders have to
// agree on the answer: the compute that places blades, the shaders that draw them, and the ground
// under them, which has to show a wheat field golden from across the valley where no blade is drawn.

// Must match GrassType.Gpu in GrassType.cs field for field: the buffer is copied across byte for byte.
struct GrassType
{
    float height;
    float heightVariation;
    float width;
    float densityShare;         // share of the field's candidates this type keeps, 0..1

    float clumpSize;
    float clumpPull;
    float clumpSplay;
    float clumpHeight;

    float zoneScale;            // 1 / metres of zone noise
    float zoneThreshold;        // noise above this is inside the zone; 2 means "only where painted"
    float zoneSoftness;         // noise units across the mixed band at the edge
    float headShare;

    float headSize;
    float headStart;
    float windResponse;
    float zoneSeed;

    float4 tipColor;
    float4 coolTipColor;
    float4 dryTipColor;
    float4 headColor;

    float headStyle;            // GRASS_HEAD_SEED, GRASS_HEAD_EAR or GRASS_HEAD_BLOOM
    float headWidth;            // ears: how many stems wide the ear grows
    float headShine;            // 1 = ordinary; more makes the heads catch the light
    float overlay;              // 1 = grows between the types below instead of replacing them

    float4 centerColor;         // blooms: the middle of the flower
};

// Must match GrassType.HeadStyle.
#define GRASS_HEAD_SEED  0
#define GRASS_HEAD_EAR   1
#define GRASS_HEAD_BLOOM 2

StructuredBuffer<GrassType> _GrassTypes;
int _GrassTypeCount;

// One blade, as GrassPlacement.compute writes it and the blade and seed-head shaders read it.
// Must match GrassField.InstanceStride.
struct GrassInstance
{
    float3 position;        // world position of the blade's root
    float  yaw;             // facing, radians
    float  height;
    float  width;
    float  region;          // 0..1 patch value: lush and tall at 1, short and dry at 0
    float  flower;          // 1 if this blade carries a seed head, 0 if not
    float  tint;            // 0..1, one value for a whole clump
    float  splay;           // resting lean along the way the blade faces, radians
    float2 groundNormal;    // x and z of the ground's normal under the root; y is rebuilt from them
    float  type;            // index into _GrassTypes
    float  zone;            // 0..1, how deep inside its type's zone the blade stands
};

/// How far away a blade counts as, for every "is it far enough to simplify" decision: which mesh draws
/// it, where its seed head is dropped. Its real distance, stretched or squeezed by up to 15%.
///
/// With every blade switching at exactly the same distance, the switch is a circle round the camera -
/// and when the camera walks, you watch that circle walk with it, a band of grass changing shape just
/// ahead of your feet. Giving each blade its own share of the distance scatters the switch over a
/// band thirty percent wide, one blade at a time, and there is no circle left to see.
///
/// Keyed on the blade's root, which the compute and the shaders both have, so they agree exactly.
float3 _GrassDebugEye; // DEBUG-TEMP
float GrassLodDistance(float3 rootWS, float3 cameraWS)
{
    return length(rootWS - cameraWS - _GrassDebugEye) * (0.85 + 0.3 * GrassHash(rootWS.xz + 83.1));
}

/// A 0..1 threshold per candidate cell that thins a grid EVENLY: keep the cell if this is below the
/// share you want.
///
/// A coin per cell keeps the right NUMBER of blades but not the right spacing - by chance several
/// neighbours all lose at once, and looking down you see bald patches between the stems. This is a
/// 4x4 ordered-dither (Bayer) matrix instead: any share keeps cells spread as far apart as a 4x4
/// block allows, so a half-density type is a checkerboard of candidates, not a scatter with holes.
/// Each candidate is still jittered inside its cell, so no lattice shows.
float GrassEvenThreshold(int2 cell)
{
    static const float bayer[16] = { 0, 8, 2, 10,
                                    12, 4, 14, 6,
                                     3, 11, 1, 9,
                                    15, 7, 13, 5 };
    int2 p = cell & 3;              // two's complement: negative cells wrap the same way
    return (bayer[p.y * 4 + p.x] + 0.5) / 16.0;
}

/// How strongly the zones of `type` claim this spot, 0..1, before anything is painted.
float GrassZoneProcedural(GrassType type, float2 worldXZ)
{
    if (type.zoneThreshold > 1.0) return 0.0;

    // The same two octaves GrassZoneNoise.cs samples to turn "5% of the ground" into a threshold.
    float2 p = worldXZ * type.zoneScale + type.zoneSeed;
    float noise = GrassValueNoise(p) * 0.7 + GrassValueNoise(p * 2.7 + 11.3) * 0.3;

    // Centred on the threshold, so half the band is outside the coverage and half inside, and a
    // softer edge does not quietly shrink the zone.
    float half = type.zoneSoftness * 0.5;
    return smoothstep(type.zoneThreshold - half, type.zoneThreshold + half, noise);
}

/// How strongly type `index` claims this spot, 0..1.
///
/// painted : the type map here - channel 0 is type 1, channel 1 is type 2, and so on. Painting
///           ADDS a zone; it never takes one away, so a painted zone and a procedural one simply
///           overlap. To have a type only where it is painted, give it zero coverage.
float GrassZoneWeight(int index, float2 worldXZ, float4 painted)
{
    if (index <= 0) return 1.0;
    return max(GrassZoneProcedural(_GrassTypes[index], worldXZ), painted[min(index - 1, 3)]);
}

/// How much of the ground a type really takes over here: its zone weight, and for an overlay type
/// only its own share of the blades - the rest stay whatever grows underneath.
float GrassTypeClaim(int index, float2 worldXZ, float4 painted)
{
    float claim = GrassZoneWeight(index, worldXZ, painted);
    GrassType type = _GrassTypes[index];
    return type.overlay > 0.5 ? claim * type.densityShare : claim;
}

/// Which type a blade standing here is, and how deep inside that type's zone it stands.
///
/// Asked from the last type down, each with its own coin: a spot half inside a zone gets that type
/// for half its blades. That is what gives the edge of a zone its mixed band - blades of both kinds
/// side by side - instead of a line. The ground shader blends the same weights in the reverse
/// order (GrassZoneFar), which comes out at exactly the same shares.
///
/// An OVERLAY type - flowers - claims only its own density's share of the blades, so a poppy patch
/// is a few poppies standing in the meadow rather than a bald spot with poppies in it.
int GrassPickType(float2 worldXZ, float4 painted, float2 key, out float weight)
{
    for (int index = _GrassTypeCount - 1; index >= 1; index--)
    {
        float zone = GrassZoneWeight(index, worldXZ, painted);
        GrassType type = _GrassTypes[index];
        float claim = type.overlay > 0.5 ? zone * type.densityShare : zone;
        if (GrassHash(key + index * 17.31) < claim)
        {
            weight = zone;
            return index;
        }
    }

    weight = 1.0;
    return 0;
}

#endif
