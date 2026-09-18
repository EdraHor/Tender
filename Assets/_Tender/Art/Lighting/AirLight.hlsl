#ifndef AIR_LIGHT_INCLUDED
#define AIR_LIGHT_INCLUDED

// How much light there is in the air at a point: for things that have no surface to speak of -
// a wind streak, a raindrop, a mote of dust - and simply glow with whatever light reaches them.
//
// Sun or moon with its shadows and cloud cookie, the sky, and every lamp near enough to matter.
// Include Lighting.hlsl before this.
//
// screenUV is GetNormalizedScreenSpaceUV of the pixel: under Forward+ URP keeps a list of lights
// per patch of the screen, and that is how the loop knows which lamps are near.
float3 LightInTheAir(float3 positionWS, float2 screenUV)
{
    // With a world position GetMainLight also applies the light cookie: under a cloud's shadow
    // the air dims with the grass. (Not multiplied by distanceAttenuation - see GrassShade.)
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
    float3 light = sun.color * sun.shadowAttenuation + SampleSH(float3(0, 1, 0));

#if USE_CLUSTER_LIGHT_LOOP
    // LIGHT_LOOP_BEGIN reads these two fields by name.
    InputData inputData = (InputData)0;
    inputData.normalizedScreenSpaceUV = screenUV;
    inputData.positionWS = positionWS;

    uint lampCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lampCount)
        Light lamp = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        // A streak can pass right through a lamp, where the falloff heads for infinity.
        light += lamp.color * min(lamp.distanceAttenuation, 4.0);
    LIGHT_LOOP_END
#endif

    return light;
}

#endif
