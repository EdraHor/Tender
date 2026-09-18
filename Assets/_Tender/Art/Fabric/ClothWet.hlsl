#ifndef CLOTH_WET_INCLUDED
#define CLOTH_WET_INCLUDED

// How wet is this point of the garment?
//
// Two sources, added together:
//   _Wetness   the whole garment, 0 dry .. 1 soaked. A slider, or driven by a script.
//   _WetMap    the water the cloth holds, texel by texel, in the garment's UV space: Wettable.cs
//              drops rain into it, spreads it through the cloth and dries it out again. Black when
//              nothing has landed, or when the material has no Wettable at all.
//
// The map holds an AMOUNT of water (1 = the cloth is full); what you see saturates well before
// that, and each fabric says how soon with `look`: nylon looks fully wet at half its water (2),
// a knit goes dark at a tenth (10) - its fibres wet through long before its pores fill. That is
// what makes a spreading spot keep a clear edge: it grows while the water thins out inside it,
// and only fades once the thread is nearly dry.
//
// What being wet LOOKS like beyond that is up to each fabric shader - that is the whole point of
// having two of them. This file only says how wet.
//
// The fabric shader declares `float _Wetness` in its own material cbuffer (so the SRP batcher
// stays happy) and includes this after it.

TEXTURE2D(_WetMap);
SAMPLER(sampler_WetMap);

float WetAt(float2 uv, float look)
{
    float water = SAMPLE_TEXTURE2D(_WetMap, sampler_WetMap, uv).r;
    return saturate(_Wetness + water * look);
}

#endif
