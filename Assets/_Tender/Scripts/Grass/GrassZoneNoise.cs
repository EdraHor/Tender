using System;
using UnityEngine;

/// <summary>
/// Turns "this grass covers 5% of the ground" into the number the zone shader compares against.
///
/// A zone is wherever a smooth noise rises above a threshold. The obvious guess - threshold 0.95
/// for 5% - is badly wrong, because two blended octaves of value noise are not spread evenly over
/// 0..1: they bunch up around the middle, and 0.95 is almost never reached. So this runs the SAME
/// noise as GrassTypes.hlsl on the CPU, many times, sorts the answers and reads the threshold off
/// the sorted list. Done once, the first time any type asks.
/// </summary>
public static class GrassZoneNoise
{
    private const int Samples = 16384;
    private static float[] _sorted;

    public static float ThresholdFor(float coverage)
    {
        if (_sorted == null)
        {
            _sorted = new float[Samples];
            var random = new System.Random(1234);
            for (int i = 0; i < Samples; i++)
            {
                var p = new Vector2((float)random.NextDouble() * 4000f, (float)random.NextDouble() * 4000f);
                _sorted[i] = Zone(p);
            }
            Array.Sort(_sorted);
        }

        int index = Mathf.Clamp(Mathf.RoundToInt((1f - coverage) * (Samples - 1)), 0, Samples - 1);
        return _sorted[index];
    }

    // Mirrors GrassZoneProcedural in GrassTypes.hlsl.
    private static float Zone(Vector2 p) =>
        GrassNoise.ValueNoise(p) * 0.7f + GrassNoise.ValueNoise(p * 2.7f + new Vector2(11.3f, 11.3f)) * 0.3f;
}
