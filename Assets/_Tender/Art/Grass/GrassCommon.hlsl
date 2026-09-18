#ifndef TENDER_GRASS_COMMON_INCLUDED
#define TENDER_GRASS_COMMON_INCLUDED

#include "GrassTypes.hlsl"      // brings GrassNoise.hlsl with it

// Everything both grass systems agree on.
//
// The tall instanced blades and the short shell grass share one wind field and one set of
// objects pushing them around. If the two ever disagreed, the seam where tall grass meets short
// grass would be obvious in a second, so the maths lives here exactly once and both shaders
// include it.
//
// Values come from GrassWind.cs and GrassInteraction.cs as global shader properties, so they are
// deliberately OUTSIDE any material cbuffer.

float4 _GrassWindDir;          // xy = the way the wind blows at the wind's centre, world XZ
float4 _GrassWindCentre;       // xy = world XZ of that centre, z = 1 / turning radius (+ turns left, 0 straight)
float  _GrassWindSpeed;        // metres per second the waves roll downstream
float  _GrassWindStrength;     // lean in the calm between waves, radians
float  _GrassWindGust;         // extra lean at the crest of a wave, radians
float  _GrassWindWaveLength;   // metres from one wave to the next
float  _GrassWindScale;        // 1 / metres: how long a stretch of one wave front blows as one gust
float  _GrassWindFlutter;

// The map of touched grass round the camera (GrassInteraction.cs): RG pushed, B flattened, A removed.
TEXTURE2D(_GrassInteractionMap);
SAMPLER(sampler_GrassInteractionMap);
float4 _GrassInteractionArea;  // xy = world XZ of the map's corner, zw = its size in metres (0: no map)
float  _GrassPushLean;         // radians fully pushed grass leans away
float  _GrassFlattenLean;      // radians flattened grass lies over
float  _GrassFlattenedHeight;  // share of its height fully flattened grass keeps

float  _GrassMaxLean;         // hard limit on the lean angle, radians
float  _GrassStiffness;       // how much blades differ in how easily they give
float  _GrassFlowerDistance;  // past this a seed head is painted on the blade, not modelled

/// Where a point stands in the wind's own coordinates, and which way the wind blows there.
///
/// Returns x = how far downstream the point is (waves are spaced along this), y = which path of the
/// wind it is on, in metres across the flow.
///
/// With a bend the wind turns as it travels, and every path is the SAME gentle curve shifted
/// sideways, so the whole field is swept along one arc with no point anywhere for the paths to bunch
/// into. The first version used circles round a centre off to one side instead: near that centre
/// every path and every wave front crowded together and the waves all but stood still - a knot of
/// wind sitting in one corner of the map.
///
/// Downstream is along * exp(bend * across). The slope of that points exactly along the curving flow
/// everywhere, so a wave front always lies straight across the wind. The price is that waves run a
/// little longer and faster on the outside of the bend than on the inside, which a wind sweeping round
/// a field does too.
float2 GrassWindFrame(float2 worldXZ, out float2 flow)
{
    float2 heading = normalize(_GrassWindDir.xy + 1e-5);
    float2 left = float2(-heading.y, heading.x);
    float2 offset = worldXZ - _GrassWindCentre.xy;
    float along = dot(offset, heading);
    float across = dot(offset, left);
    float bend = _GrassWindCentre.z;                        // radians the wind turns per metre, + left

    // Far off the map the wind simply stops turning, rather than curling round until it blows back.
    float turnAlong = clamp(along, -800.0, 800.0);
    float spreadAcross = clamp(across, -800.0, 800.0);

    flow = normalize(heading + left * (bend * turnAlong));
    return float2(along * exp(bend * spreadAcross), across - 0.5 * bend * turnAlong * turnAlong);
}

/// How hard the wind blows at a point of the wind's frame at `time`: 0 in the calm, 1 at a crest.
///
/// Waves roll downstream at _GrassWindSpeed, one every _GrassWindWaveLength metres. Each ARRIVES
/// fast and dies away slowly: the grass is knocked over in a moment and takes a while to stand back
/// up. That lopsided shape is what reads as blades pushed over one after another; a smooth sine
/// reads as the field breathing in place.
///
/// Evenly spaced, ruler-straight fronts look like corduroy, so two noises break them up. Both are
/// sampled in the frame that travels WITH the waves, so whatever shape a front has, it keeps as it
/// crosses the field instead of flickering:
///   - one pushes each front back and forth, so they wander and bunch up the way gusts do;
///   - one switches whole stretches of a front off, so a wave is a string of separate gusts with
///     calm air between them. It is stretched across the flow: a gust is wider than it is deep.
float GrassGustInFrame(float2 frame, float time)
{
    float waveLength = max(_GrassWindWaveLength, 0.5);
    float2 moving = float2(frame.x - time * _GrassWindSpeed, frame.y);

    float wander = GrassValueNoise(moving * float2(0.004, 0.007) + 5.3) * 2.0 - 1.0;
    float cycle = frac(-(moving.x + wander * waveLength * 1.5) / waveLength);
    float arrive = smoothstep(0.0, 0.18, cycle);
    float leave = 1.0 - smoothstep(0.18, 1.0, cycle);
    float wave = arrive * leave * leave;

    float2 gustPoint = moving * _GrassWindScale * float2(1.5, 1.0) + float2(0.0, time * 0.03);
    float gusty = smoothstep(0.35, 0.75, GrassValueNoise(gustPoint));
    return wave * gusty;
}

/// What has touched the grass here: RG which way it is pushed (length 0..1), B how flattened, A how
/// cleared. All zero outside the map, and fading to zero over its outer edge so it never ends in a line.
float4 GrassTouchAt(float2 worldXZ)
{
    if (_GrassInteractionArea.z <= 0.0) return 0.0;
    float2 uv = (worldXZ - _GrassInteractionArea.xy) / _GrassInteractionArea.zw;
    float edge = saturate(min(min(uv.x, uv.y), min(1.0 - uv.x, 1.0 - uv.y)) * 20.0);
    if (edge <= 0.0) return 0.0;
    return SAMPLE_TEXTURE2D_LOD(_GrassInteractionMap, sampler_GrassInteractionMap, uv, 0) * edge;
}

float GrassGustAt(float2 worldXZ, float time)
{
    float2 flow;
    return GrassGustInFrame(GrassWindFrame(worldXZ, flow), time);
}

/// How hard the wind blows here right now, 0..1.
///
/// Blades lean with it, and the look shaders brighten with it - blades AND the far ground where
/// no blade is drawn any more. That second use is what makes a whole hillside shimmer in waves,
/// which is most of what reads as "wind" from any distance.
float GrassGust(float2 worldXZ)
{
    return GrassGustAt(worldXZ, _Time.y);
}

/// The total sideways force on a blade rooted at baseWS, in world XZ.
///
/// The LENGTH of the returned vector is the lean angle at the tip in radians, and its DIRECTION
/// is the way the blade falls. Wind and pushing objects add into the same vector, so a blade
/// caught by a foot while the wind blows simply leans somewhere in between. No special case, no
/// blending rule to tune - that is the whole reason force is a vector here and not two systems.
///
/// `time` is there for motion vectors, which need to know where a blade was a frame ago. Everything
/// else calls GrassForce, which is this at the current time. What touched the grass is always read as
/// it is now - the map keeps no past frames - so only the wind is exact in the past.
float2 GrassForceAt(float3 baseWS, float time)
{
    float2 flow;
    float2 frame = GrassWindFrame(baseWS.xz, flow);
    float2 side = float2(-flow.y, flow.x);
    float lean = _GrassWindStrength + _GrassWindGust * GrassGustInFrame(frame, time);

    // Small eddies, a few metres across and carried a little faster than the waves, shiver the
    // blades sideways so the field never marches in perfect step.
    float2 eddyPoint = (frame - float2(time * _GrassWindSpeed * 1.3, 0.0)) * 0.3;
    float flutter = GrassValueNoise(eddyPoint) * 2.0 - 1.0;

    float2 force = flow * lean + side * (lean * _GrassWindFlutter * flutter);

    // Whatever is touching the grass here. Pushed grass leans away from what pushed it. Flattened grass
    // lies over each blade its own way, which is what reads as trampled rather than combed - and it
    // stays lying there after the push has sprung back, for as long as the grass takes to recover.
    float4 touch = GrassTouchAt(baseWS.xz);
    float fallAngle = GrassHash(baseWS.xz + 17.3) * 6.2831853;
    force += touch.rg * _GrassPushLean
           + float2(cos(fallAngle), sin(fallAngle)) * (touch.b * _GrassFlattenLean);

    return force;
}

float2 GrassForce(float3 baseWS) { return GrassForceAt(baseWS, _Time.y); }

/// The lean a blade rooted here actually ends up with, and which way it falls.
///
/// Both the blade shader and the seed-head shader call this. They have to agree exactly, or the
/// flower parts company with the tip it is supposed to be sitting on - so the calculation lives
/// here once instead of being written out twice and drifting.
///
/// rest     : the lean the blade has with no wind at all, in the same units as the force - how its
///            clump makes it splay. Stiffness does not scale it: a blade does not stand up
///            straighter for being stiff, it only resists being pushed further.
/// response : how much this kind of grass gives to force at all - its GrassType's wind response.
float GrassLeanAt(float3 rootWS, float2 rest, float response, float time, out float2 direction)
{
    // Stiffness is keyed on the root, so a blade keeps its own springiness for good.
    float stiffness = (1.0 - _GrassStiffness * GrassHash(rootWS.xz + 5.5)) * response;

    float2 force = GrassForceAt(rootWS, time) * stiffness + rest;
    float lean = length(force);
    direction = force / max(lean, 1e-4);

    // An unset limit means NO limit, not "no movement". Every value in this file is a shader
    // global, so a scene without a GrassWind component leaves them all at zero - and min(lean, 0)
    // would freeze the whole field solid, taking every interactor down with it. The grass would
    // simply refuse to move and nothing would say why.
    float limit = _GrassMaxLean > 0.0 ? _GrassMaxLean : 1.4;
    return min(lean, limit);
}

float GrassLean(float3 rootWS, float2 rest, float response, out float2 direction)
{
    return GrassLeanAt(rootWS, rest, response, _Time.y, direction);
}

float3 GrassRotateAxis(float3 v, float3 axis, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return v * c + cross(axis, v) * s + axis * dot(axis, v) * (1.0 - c);
}

/// Where along a blade the bending happens. 1 bends it evenly into a circular arc; 2 keeps the
/// lower half almost straight and curls the top. Real grass is a cantilever - stiff where it
/// leaves the ground, floppy at the tip - so it sits in between.
#define GRASS_TIP_BEND 1.7

/// Bend a blade into a curve that keeps its LENGTH, with most of the turn near the tip.
///
/// Keeping the length is what makes grass read as grass. Shearing or scaling the mesh instead -
/// the usual shortcut - makes blades appear to stretch as the wind rises and snap back as it
/// falls, which the eye catches immediately.
///
/// The turn at a point s along the blade (0 root, 1 tip) is lean * s^GRASS_TIP_BEND. There is no
/// neat formula for where that curve ends up, so the blade is simply WALKED: four steps of equal
/// length, each pointing the way the blade faces halfway along that step. Ghost of Tsushima gets
/// the same shape - straight base, curling tip - from a Bezier curve, which is cheaper still but
/// lets the blade change length as it moves.
///
/// local  : vertex in the blade's own space, already turned to its instance's facing.
///          y runs from 0 at the root to bladeHeight at the tip.
/// dir    : unit direction in world XZ the blade falls towards.
/// lean   : total turn angle at the tip, radians.
float3 GrassBend(float3 local, float bladeHeight, float2 dir, float lean, inout float3 normalWS)
{
    float t = saturate(local.y / max(bladeHeight, 1e-4));
    float theta = lean * pow(t, GRASS_TIP_BEND);

    float3 forward = float3(dir.x, 0, dir.y);
    float3 axis = float3(dir.y, 0, -dir.x);        // turns +Y towards forward

    const int Steps = 4;
    float stepLength = local.y / Steps;
    float up = 0.0;
    float ahead = 0.0;
    for (int i = 0; i < Steps; i++)
    {
        float s = (i + 0.5) / Steps * t;
        float turn = lean * pow(s, GRASS_TIP_BEND);
        up += cos(turn) * stepLength;
        ahead += sin(turn) * stepLength;
    }

    // The spine follows the curve; the blade's width swings with the turn at this height.
    float3 spine = float3(0, up, 0) + forward * ahead;
    float3 across = GrassRotateAxis(float3(local.x, 0, local.z), axis, theta);

    normalWS = GrassRotateAxis(normalWS, axis, theta);
    return spine + across;
}

/// Turn a blade around its own base. Instance facing is stored as a single angle, so this is all
/// the "transform" a blade ever gets.
float3 GrassYaw(float3 v, float angle)
{
    float s, c;
    sincos(angle, s, c);
    return float3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
}

#endif
