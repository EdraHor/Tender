#ifndef TENDER_CLOUD_LAYER_INCLUDED
#define TENDER_CLOUD_LAYER_INCLUDED

// The one layer of clouds over the world: a flat sheet at a fixed altitude with a pattern of cumulus
// painted on it, drifting with the wind.
//
// Two things read it, and both include this file so they cannot disagree:
//   - SummerSky.shader draws it: where the view ray crosses the sheet, how much cloud is there;
//   - CloudShadows.compute shades the ground with it: where the SUNBEAM through a spot on the ground
//     crosses the sheet, how much cloud is there.
// Same function, same numbers - so every shadow on the grass belongs to a cloud you can see.

float CloudHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float CloudValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = CloudHash21(i);
    float b = CloudHash21(i + float2(1, 0));
    float c = CloudHash21(i + float2(0, 1));
    float d = CloudHash21(i + float2(1, 1));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float CloudFbm(float2 p)
{
    float sum = 0.0;
    float amplitude = 0.5;
    for (int i = 0; i < 5; i++)
    {
        sum += CloudValueNoise(p) * amplitude;
        p = p * 2.03 + 17.3;              // the odd numbers stop the octaves lining up
        amplitude *= 0.5;
    }
    return sum;
}

/// How far the wind has carried the clouds by `time`, in metres. Heading in degrees around Y
/// (0 towards +Z, 90 towards +X), the same convention as GrassWind and CloudShadows.
float2 CloudDrift(float heading, float speed, float time)
{
    float radiansHeading = radians(heading);
    return float2(sin(radiansHeading), cos(radiansHeading)) * speed * time;
}

/// How much cloud there is at a point on the sheet, 0 clear sky to 1 solid.
///   layerXZ    - world XZ of the point on the sheet
///   drift      - CloudDrift for this moment
///   cloudScale - roughly how wide one cloud is, in metres
///   coverage   - how much of the sky is cloud
///   softness   - how gradually a cloud's edge fades
// Rain (RainZone.cs): 0 the weather the material describes .. 1 overcast. It raises the cover and
// softens the edges here, in the one function both the sky and the ground shadows call, so the
// sun goes behind the same cloud for both.
float _SkyRain;

float CloudLayerDensity(float2 layerXZ, float2 drift, float cloudScale, float coverage, float softness)
{
    coverage = lerp(coverage, 0.97, saturate(_SkyRain));
    softness = lerp(softness, 0.5, saturate(_SkyRain));

    // Minus: to see the clouds move WITH the wind, look up the spot they came from.
    float2 uv = (layerXZ - drift) / max(cloudScale, 1.0);

    // Cover is a threshold on the noise, not a multiplier: raising it grows the clouds outwards from
    // their cores instead of just making the whole sky milky.
    return saturate((CloudFbm(uv) - (1.0 - coverage)) / max(softness, 1e-4));
}

#endif
