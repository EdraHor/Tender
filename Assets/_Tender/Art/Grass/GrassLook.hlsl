#ifndef TENDER_GRASS_LOOK_INCLUDED
#define TENDER_GRASS_LOOK_INCLUDED

// How the meadow is coloured and lit: the blades, AND the ground under them.
//
// Both GrassBlade.shader and MeadowGround.shader call these functions with these numbers. That is
// the trick every big grass field relies on (Breath of the Wild, Ghost of Tsushima, Far Cry 5 all
// do a version of it): blades cannot be drawn to the horizon, so past a certain distance the
// GROUND has to look exactly like grass seen from far away. If the two were coloured or lit by
// separate code, the place where the last blade stops would be a visible line on every hill.
//
// Include after URP's Lighting.hlsl and GrassCommon.hlsl. The values are globals written by
// GrassLook.cs (ground colour, drifts and light) and GrassField.cs (the grass types, the painted
// maps and the patch size), so they sit outside any material cbuffer.

float4 _GrassGroundColor;       // roots, and the ground between blades
float  _GrassHueScale;          // 1 / metres across one drift of colour
float  _GrassHueAmount;         // 0..1 how far the drift goes towards the cool colour
float  _GrassDryAmount;         // 0..1 how far poor patches go towards the dry colour
float  _GrassRootShade;         // 0..1 how dark it is down between the stems

float  _GrassWrap;              // light wrapping round the thin blades
float  _GrassTranslucency;      // glow looking towards the sun through the grass
float  _GrassSheen;             // soft highlight on the tips
float  _GrassGustBrighten;      // how much a passing gust lightens the tips
float  _GrassCarpetDistance;    // by here, blades and ground have become one carpet
float  _GrassUnderstorey;       // 0..1 how much short grass is painted on the ground between stems

float  _GrassRegionScale;       // metres across one lush or dry patch - the compute uses the same

#include "../Rain/Wet.hlsl"     // rain: the meadow darkens and glosses where it is not under cover

TEXTURE2D(_GrassPaintMap);      // GrassField's painted map: R density, G height, B lushness
SAMPLER(sampler_GrassPaintMap);
TEXTURE2D(_GrassTypeMap);       // GrassField's painted type zones: RGBA = types 1..4
SAMPLER(sampler_GrassTypeMap);
float4 _GrassPaintArea;         // xy = world XZ of both maps' corner, zw = their size in metres

/// How far towards "carpet" something at this distance is, 0..1.
///
/// Up close you see stems: dark roots, bright tips, each blade lit on its own. Far away you see
/// a surface: the tops of thousands of blades averaged together, lit like the ground they cover.
/// Blades and ground both slide from the first to the second over the same distances.
float GrassCarpet(float distance)
{
    return smoothstep(_GrassCarpetDistance * 0.25, _GrassCarpetDistance, distance);
}

/// 0..1: where this spot sits in the slow drift between a type's warm and cool tip colour.
float GrassDrift(float2 worldXZ)
{
    return GrassValueNoise(worldXZ * _GrassHueScale + 31.7) * 0.7
         + GrassValueNoise(worldXZ * _GrassHueScale * 3.1 + 5.3) * 0.3;
}

/// The tip colour one grass type has here.
///
/// Colour comes from FIELDS across the ground, not from a dice roll per blade. Random colour per
/// blade averages out to mud from ten metres away; broad drifts of warmer and cooler green, and
/// the dry patches the grass is short in, are what survive to the horizon and give a meadow its
/// painted look. Every type drifts on the same noise, so neighbouring types warm and cool together.
float3 GrassTypeTip(GrassType type, float drift, float region)
{
    float3 tip = lerp(type.tipColor.rgb, type.coolTipColor.rgb, saturate(drift * _GrassHueAmount * 1.6 - 0.2));
    return lerp(tip, type.dryTipColor.rgb, (1.0 - region) * _GrassDryAmount);
}

/// What a type looks like from far enough away that its seed heads are a colour, not objects.
/// A flowering meadow reads paler across the valley; a type with no heads is just its tips.
///
/// How much of the view the heads cover depends on what they are. Seed heads get only a hint: a
/// 2 cm disc on one blade in three is a few percent of the green around it. The first version mixed
/// in a third, and in linear light a third of near-white is most of the brightness - every clearing
/// on the far hills came out as a white stain. Ears are most of what you see of a wheat field, and
/// a bloom is the whole point of a flower.
float3 GrassTypeFar(GrassType type, float3 tip)
{
    int style = (int)type.headStyle;
    float cover = style == GRASS_HEAD_EAR ? 0.65 : (style == GRASS_HEAD_BLOOM ? 0.6 : 0.08);
    return lerp(tip, type.headColor.rgb, saturate(type.headShare * 2.0) * cover);
}

/// The colour of a blade's tip.
///
/// A blade near the edge of its type's zone is only partly that type's colour - `zone` of the way
/// from the base grass - so the colour eases across the mixed band along with the height.
/// The clump tint adds a little value variation on top, the only per-blade noise.
float3 GrassTipAlbedo(float2 worldXZ, float region, float tint, int typeIndex, float zone)
{
    float drift = GrassDrift(worldXZ);
    float3 tip = GrassTypeTip(_GrassTypes[0], drift, region);
    if (typeIndex > 0) tip = lerp(tip, GrassTypeTip(_GrassTypes[typeIndex], drift, region), zone);
    return tip * (0.9 + 0.2 * tint);
}

/// The far colour of whatever mixture of types grows here, weighted by their zones.
///
/// For the ground, which has no blades to pick a type for. Blending from the first type up, each by
/// its own claim, gives every type exactly the share of the ground that GrassPickType gives it of
/// the blades - so the far carpet shows the zones the near blades grew in.
///
/// Flowers (overlay types) claim only their few blades, which would leave a poppy patch invisible
/// from across the valley; seen from far away, though, a flower head covers several times the
/// ground its stem stands on, so their share of the colour is boosted.
float3 GrassZoneFar(float2 worldXZ, float region, float4 painted)
{
    float drift = GrassDrift(worldXZ);
    float3 far = GrassTypeFar(_GrassTypes[0], GrassTypeTip(_GrassTypes[0], drift, region));
    for (int index = 1; index < _GrassTypeCount; index++)
    {
        GrassType type = _GrassTypes[index];
        float claim = GrassTypeClaim(index, worldXZ, painted);
        if (type.overlay > 0.5) claim = saturate(claim * 4.0);

        float3 typeFar = GrassTypeFar(type, GrassTypeTip(type, drift, region));
        if (type.overlay > 0.5) typeFar = lerp(far, type.headColor.rgb, 0.5);   // flowers: petals over the grass below
        far = lerp(far, typeFar, claim);
    }
    return far;
}

/// How much of this pixel one layer of short strokes covers, 0..1, and a 0..1 shade per stroke.
///
/// Each cell holds one short stroke at its own angle and length. A stroke is longer than its cell,
/// so the pixel looks at its own cell and the eight around it and keeps the closest stroke.
///
/// footprint : metres one pixel covers here (fwidth of the world position). Passed in rather than
///             taken here, because a derivative inside the loop would be undefined.
float2 GrassStrokes(float2 worldXZ, float cellSize, float seed, float footprint)
{
    float2 p = worldXZ / cellSize;
    float2 home = floor(p);
    float nearest = 1e5;
    float shade = 0.0;

    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 cell = home + float2(x, y);
            float2 centre = cell + float2(GrassHash(cell + seed), GrassHash(cell + seed + 4.7));
            float angle = GrassHash(cell + seed + 9.1) * 6.2831853;
            float2 along = float2(cos(angle), sin(angle));
            float halfLength = 0.6 + 0.8 * GrassHash(cell + seed + 2.3);

            float t = clamp(dot(p - centre, along), -halfLength, halfLength);
            float taper = 1.0 - abs(t) / halfLength;             // pointed at both ends, like a blade
            float gap = length(p - (centre + along * t)) - 0.13 * taper;

            if (gap < nearest)
            {
                nearest = gap;
                shade = GrassHash(cell + seed + 6.6);
            }
        }
    }

    float pixel = footprint / cellSize;                          // pixel size in cell units
    return float2(1.0 - smoothstep(-pixel * 0.5, pixel * 0.5, nearest), shade);
}

/// The ground between the stems, close up.
///
/// Looking down at your feet you see between the blades, and a flat green floor there reads as bald
/// earth. In a real meadow what shows is more grass - short stems, the bottoms of the tufts - so two
/// layers of little strokes paint that. They have to stay QUIET: brighter than the floor on average
/// and they draw the eye straight to the gaps they were meant to hide. So the deep layer darkens,
/// the top layer lightens only a little, and the two roughly cancel. They fade out well before they
/// could shimmer; further away the carpet colour takes over anyway.
float3 GrassUnderstoreyAlbedo(float2 worldXZ, float3 ground, float3 tip, float distance, float footprint)
{
    // Strokes only while a cell is still several pixels wide, and only near the camera.
    float fade = _GrassUnderstorey
               * (1.0 - smoothstep(8.0, 22.0, distance))
               * saturate(2.0 - footprint / 0.02);
    if (fade <= 0.0) return ground;

    float2 deep = GrassStrokes(worldXZ, 0.07, 3.1, footprint);
    float2 top = GrassStrokes(worldXZ, 0.12, 17.9, footprint);

    float3 colour = lerp(ground, ground * 0.55, deep.x * (0.5 + 0.5 * deep.y));
    colour = lerp(colour, lerp(ground * 1.7, tip * 0.4, top.y * top.y), top.x * 0.7);
    return lerp(ground, colour, fade);
}

/// How much of the ground here lies under grass TALLER than the base meadow, 0..1: wheat, pampas.
///
/// Little light gets down between tall, dense stems. What shows there is shade and old straw, not a
/// lawn - and a green lawn glimpsed between wheat stems is exactly what makes the gaps jump out. Each
/// type counts by its zone here and by how much taller than the base grass it grows.
float GrassTallCover(float2 worldXZ, float4 painted)
{
    float cover = 0.0;
    for (int index = 1; index < _GrassTypeCount; index++)
    {
        GrassType type = _GrassTypes[index];
        if (type.overlay > 0.5) continue;       // flowers stand in the meadow, they do not shade it

        float taller = saturate((type.height - _GrassTypes[0].height) / 0.5);
        cover = max(cover, GrassTypeClaim(index, worldXZ, painted) * taller);
    }
    return cover;
}

/// The floor under tall grass: the meadow's ground, darkened, with straw in it.
float3 GrassUnderTallAlbedo(float3 ground, float cover)
{
    float3 straw = _GrassTypes[0].dryTipColor.rgb * 0.22;
    return lerp(ground, lerp(ground * 0.6, straw, 0.5), cover);
}

/// What grass looks like once single blades are too small to see: mostly tips, a little root.
float3 GrassCarpetAlbedo(float3 tip)
{
    return lerp(_GrassGroundColor.rgb, tip, 0.7);
}

/// Light from lamps: every point and spot light near this pixel - lanterns, a campfire, a torch.
///
/// Lit the same soft way as the sun below: wrapped, so a lantern lights the blades around it rather
/// than only their faces turned towards it, and glowing through blades that stand between the lamp
/// and the camera. That glow is most of what makes grass at night read as grass.
///
/// screenUV is GetNormalizedScreenSpaceUV of the pixel. Under Forward+ URP keeps a list of lights per
/// patch of the screen, and that is how the loop knows which lights are near.
float3 GrassLamps(float3 positionWS, float3 normalWS, float2 screenUV, float tipness)
{
    float3 lamps = 0.0;

#if USE_CLUSTER_LIGHT_LOOP
    float3 toCamera = normalize(GetWorldSpaceViewDir(positionWS));

    // LIGHT_LOOP_BEGIN reads these two fields by name.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = screenUV;
    inputData.positionWS = positionWS;

    uint lampCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lampCount)
        Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        float3 light = lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation);
        float diffuse = saturate((dot(normalWS, lamp.direction) + _GrassWrap) / (1.0 + _GrassWrap));
        float through = pow(saturate(dot(-toCamera, lamp.direction)), 4.0) * _GrassTranslucency * tipness;
        lamps += light * (diffuse + through);
    LIGHT_LOOP_END
#endif

    return lamps;
}

/// Light the meadow.
///
/// tipness  : 0 at a root, 1 at a tip. Glow and sheen live on the tips; the carpet sits at 0.7.
/// gust     : GrassGust here, 0 calm .. 1 the crest of a wave.
/// ao       : darkening for being down among the stems.
/// screenUV : GetNormalizedScreenSpaceUV of the pixel, for finding the lamps near it.
///
/// Deliberately not a toon ramp. Breath of the Wild's terrain is lit realistically - it is the
/// COLOURS that are painted, and the soft wrapped light that keeps them from going muddy.
float3 GrassShade(float3 albedo, float3 normalWS, float3 positionWS, float tipness, float gust, float ao, float2 screenUV)
{
    // GetMainLight with a world position is what picks up the light cookie, which is how the
    // volumetric cloud's shadow lands on the grass for free.
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light sun = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));

    // NOT multiplied by sun.distanceAttenuation, and that is a fix, not an oversight. Under the
    // Forward+ renderer URP fills that value in only for shaders compiled with its
    // _CLUSTER_LIGHT_LOOP keyword; everywhere else it reads a per-object slot Forward+ leaves at
    // zero. The tall grass used to multiply by it and was lit by nothing but the sky - dull and
    // blue-grey, with no sun on it at all. A directional light has no distance to attenuate over
    // anyway.
    float3 light = sun.color * sun.shadowAttenuation;
    float3 toCamera = normalize(GetWorldSpaceViewDir(positionWS));

    // A wave presses the blades over and turns their pale flat sides up to the sky - lighter and a
    // little silvery. That band of paler grass rolling across the field is what makes waves visible
    // in flat light, when the sheen has nothing to catch, and from far enough away that no single
    // blade can be seen to move.
    // Capped at the tip's own strength: shiny heads (tipness above 1) would otherwise go fully silver
    // on every wave and a golden wheat field turns white.
    float3 pressedOver = lerp(albedo, dot(albedo, float3(0.3, 0.59, 0.11)).xxx, 0.3) * 1.8;
    albedo = lerp(albedo, pressedOver, saturate(_GrassGustBrighten * gust * min(tipness, 1.0)) * 0.7);

    // Rain. Wet grass and wet soil go darker and deeper in colour, and a film of water glosses
    // them - the same in every meadow shader because it happens here. Nothing under a roof.
    float wet = WorldWetAt(positionWS, normalWS);
    albedo = WetAlbedo(albedo, wet, 0.6);

    // A blade is thin enough that light bends round it, so a hard N.L makes grass look like
    // painted metal. Wrapping the term softens the terminator.
    float diffuse = saturate((dot(normalWS, sun.direction) + _GrassWrap) / (1.0 + _GrassWrap));

    // Looking towards the sun, light comes THROUGH the blades: the backlit meadow glow.
    float through = pow(saturate(dot(-toCamera, sun.direction)), 4.0) * _GrassTranslucency * tipness;

    // The silvery sheen on a field seen against the light. Riding it on the gust makes the
    // highlight run across the grass with the wind - what the Breath of the Wild team described
    // as wanting to see "highlights flowing" through the grass from far away.
    float3 halfDir = normalize(sun.direction + toCamera);
    float sheen = pow(saturate(dot(normalWS, halfDir)), 12.0) * _GrassSheen * tipness * lerp(0.5, 1.5, gust);
    sheen += WetGloss(normalWS, sun.direction, toCamera, wet) * 0.35;

    float3 ambient = SampleSH(normalWS);
    float3 lamps = GrassLamps(positionWS, normalWS, screenUV, tipness);
    return albedo * ao * (light * (diffuse + through) + ambient + lamps) + light * sheen;
}

#endif
