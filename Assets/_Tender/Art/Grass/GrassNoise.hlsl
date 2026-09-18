#ifndef TENDER_GRASS_NOISE_INCLUDED
#define TENDER_GRASS_NOISE_INCLUDED

// The random numbers every piece of the meadow agrees on.
//
// Plain maths and nothing else - no engine includes, no globals - so that the compute shader that
// PLACES the blades, the shaders that DRAW them and the shader for the ground UNDER them can all
// include it. That is the whole point of the file: the far ground has to show the same lush and
// dry patches the blades grew in, and the only way to be sure it does is to ask the very same
// function the very same question.

/// A random number in 0..1 from a 2D key.
///
/// frac() comes FIRST. That ordering is not a style choice: this hash is fed raw world positions,
/// and the usual frac(p * 123.34) spends the whole mantissa on the multiply before wrapping. By
/// a few thousand metres out the product has no fractional precision left, frac() returns one of
/// a few dozen values, and neighbouring blades stop being different from each other - the field
/// visibly bands into stripes of identical grass the further you walk from the origin.
float GrassHash(float2 p)
{
    float3 q = frac(float3(p.xyx) * 0.1031);
    q += dot(q, q.yzx + 33.33);
    return frac((q.x + q.y) * q.z);
}

float GrassValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float a = GrassHash(i);
    float b = GrassHash(i + float2(1, 0));
    float c = GrassHash(i + float2(0, 1));
    float d = GrassHash(i + float2(1, 1));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

/// How good the grass is here, 0..1. Two octaves, so there are broad areas with smaller variety
/// inside them rather than one uniform blob of noise.
///
/// The compute uses it for height, the blade and ground shaders for colour - where the grass is
/// poor it is both shorter AND drier, the way a real meadow is.
float GrassRegion(float2 worldXZ, float scale)
{
    scale = max(scale, 1.0);
    return saturate(GrassValueNoise(worldXZ / scale) * 0.68
                  + GrassValueNoise(worldXZ / (scale * 0.34)) * 0.32);
}

#endif
