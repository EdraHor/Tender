using UnityEngine;

/// <summary>
/// GrassNoise.hlsl, line for line, on the CPU.
///
/// A few things on the C# side have to get exactly the numbers the shaders get: the zone thresholds
/// (GrassZoneNoise) and the wind, which particles and gizmos follow. Any difference here and the
/// particles drift through a gust the grass beneath them does not feel.
/// </summary>
public static class GrassNoise
{
    public static float Hash(Vector2 p)
    {
        var q = new Vector3(Frac(p.x * 0.1031f), Frac(p.y * 0.1031f), Frac(p.x * 0.1031f));
        float shift = Vector3.Dot(q, new Vector3(q.y, q.z, q.x) + new Vector3(33.33f, 33.33f, 33.33f));
        q += new Vector3(shift, shift, shift);
        return Frac((q.x + q.y) * q.z);
    }

    public static float ValueNoise(Vector2 p)
    {
        var i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        var f = p - i;
        f = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));

        float a = Hash(i);
        float b = Hash(i + new Vector2(1f, 0f));
        float c = Hash(i + new Vector2(0f, 1f));
        float d = Hash(i + new Vector2(1f, 1f));
        return Mathf.Lerp(Mathf.Lerp(a, b, f.x), Mathf.Lerp(c, d, f.x), f.y);
    }

    public static float Frac(float x) => x - Mathf.Floor(x);
}
