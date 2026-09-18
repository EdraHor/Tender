using System.Collections.Generic;
using UnityEngine;
using Blob = CloudShape.Blob;

/// <summary>
/// Ready-made blob lists for <see cref="CloudShape"/>. A hand-placed hero cumulus, plus
/// procedural builders for a towering cumulonimbus and wall segments. To build a sky, place
/// several <see cref="CloudShape"/> objects and hand each one of these lists (varying the seed).
/// </summary>
public static class CloudPresets
{
    /// <summary>Broad fair-weather cumulus: wide base, stacked belly, rounded crown. (Hand-tuned.)</summary>
    public static List<Blob> HeroCumulus()
    {
        return new List<Blob>
        {
            // base row (broad, low)
            new Blob(new Vector3( 0.00f,-0.31f, 0.00f), 0.34f),
            new Blob(new Vector3( 0.27f,-0.29f, 0.09f), 0.23f),
            new Blob(new Vector3(-0.25f,-0.29f,-0.07f), 0.23f),
            new Blob(new Vector3( 0.10f,-0.31f, 0.25f), 0.21f),
            new Blob(new Vector3(-0.13f,-0.31f,-0.24f), 0.21f),
            new Blob(new Vector3( 0.31f,-0.27f,-0.16f), 0.17f),
            new Blob(new Vector3(-0.30f,-0.27f, 0.17f), 0.17f),
            // mid row (the belly)
            new Blob(new Vector3( 0.05f,-0.06f, 0.02f), 0.26f),
            new Blob(new Vector3( 0.23f,-0.02f,-0.07f), 0.18f),
            new Blob(new Vector3(-0.21f,-0.04f, 0.09f), 0.18f),
            new Blob(new Vector3( 0.02f,-0.02f,-0.22f), 0.16f),
            new Blob(new Vector3(-0.05f,-0.06f, 0.21f), 0.16f),
            // upper bulges
            new Blob(new Vector3( 0.05f, 0.16f, 0.00f), 0.19f),
            new Blob(new Vector3(-0.13f, 0.14f, 0.07f), 0.14f),
            new Blob(new Vector3( 0.15f, 0.12f,-0.06f), 0.13f),
            // rounded top crown
            new Blob(new Vector3( 0.00f, 0.30f, 0.02f), 0.16f),
            new Blob(new Vector3(-0.10f, 0.27f,-0.06f), 0.12f),
            new Blob(new Vector3( 0.11f, 0.26f, 0.05f), 0.12f),
            new Blob(new Vector3( 0.02f, 0.36f,-0.01f), 0.11f),
        };
    }

    /// <summary>Towering cumulonimbus: a full column of cauliflower tiers with a spreading anvil crown.</summary>
    public static List<Blob> Tower(int seed = 12) => BuildTower(seed, tiers: 6, baseWidth: 0.36f, topWidth: 0.24f, lean: 0.05f, anvil: true);

    /// <summary>One segment of a cloud wall — a varied tower to place in a row with others.</summary>
    public static List<Blob> WallChunk(int seed)
    {
        var rng = new System.Random(seed);
        int tiers = 4 + rng.Next(0, 3);                         // 4..6
        float baseW = 0.32f + (float)rng.NextDouble() * 0.10f;  // 0.32..0.42 (broad, so segments touch)
        float lean = ((float)rng.NextDouble() - 0.5f) * 0.10f;  // slight sideways lean
        bool anvil = rng.NextDouble() > 0.7;                    // only a few segments flare
        return BuildTower(seed, tiers, baseW, 0.22f, lean, anvil);
    }

    /// <summary>A broad, low mound — a back-row filler that fuses wall segments into one bank.</summary>
    public static List<Blob> LowBank(int seed)
    {
        var rng = new System.Random(seed);
        var list = new List<Blob>();
        // one wide row of overlapping bulges hugging the base
        int n = 6 + rng.Next(0, 3);
        for (int k = 0; k < n && list.Count < CloudShape.MaxBlobs; k++)
        {
            float x = Mathf.Lerp(-0.42f, 0.42f, k / (float)(n - 1));
            float y = -0.22f + ((float)rng.NextDouble() - 0.5f) * 0.10f;
            float z = ((float)rng.NextDouble() - 0.5f) * 0.30f;
            float r = 0.24f * Jitter(rng, 0.22f);
            list.Add(new Blob(new Vector3(x, y, z), r));
            // a second, lower row for a fat continuous base
            if (list.Count < CloudShape.MaxBlobs)
                list.Add(new Blob(new Vector3(x + 0.05f, -0.34f, z * 0.5f), 0.20f * Jitter(rng, 0.2f)));
        }
        return list;
    }

    // Stacks rings of blobs from base to crown; radius and lobe-count shrink with height.
    // 'anvil' adds a wide flared ring near the top (the classic cumulonimbus cap).
    private static List<Blob> BuildTower(int seed, int tiers, float baseWidth, float topWidth, float lean, bool anvil)
    {
        var rng = new System.Random(seed);
        var list = new List<Blob>();

        for (int ti = 0; ti < tiers && list.Count < CloudShape.MaxBlobs; ti++)
        {
            float f = tiers > 1 ? ti / (float)(tiers - 1) : 0f;   // 0 = base, 1 = crown
            float y = Mathf.Lerp(-0.42f, 0.30f, f);
            float width = Mathf.Lerp(baseWidth, topWidth, f);
            float cx = lean * f;                                  // lean the whole column

            // FAT central core: big enough that neighbouring tiers overlap strongly, so the
            // column stays one solid mass instead of a stack of separate puffs.
            float rc = Mathf.Lerp(0.34f, 0.22f, f) * Jitter(rng, 0.10f);
            list.Add(new Blob(new Vector3(cx, y, 0f), rc));

            // ring of side bulges (cauliflower), nested slightly BELOW the core so they merge
            int lobes = Mathf.Max(3, Mathf.RoundToInt(Mathf.Lerp(5f, 3f, f)));
            float angle0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int k = 0; k < lobes && list.Count < CloudShape.MaxBlobs; k++)
            {
                float ang = angle0 + k / (float)lobes * Mathf.PI * 2f;
                float rad = width * (0.5f + (float)rng.NextDouble() * 0.25f);   // hug the core (no strays)
                float bx = cx + Mathf.Cos(ang) * rad;
                float bz = Mathf.Sin(ang) * rad;
                float by = y - 0.03f + ((float)rng.NextDouble() - 0.5f) * 0.03f;
                float br = Mathf.Lerp(0.24f, 0.15f, f) * Jitter(rng, 0.18f);
                list.Add(new Blob(new Vector3(bx, by, bz), br));
            }
        }

        // Anvil: a wide flared ring that overlaps the crown (sits low enough to stay attached).
        if (anvil)
        {
            float ay = 0.20f;
            int n = 5;
            float angle0 = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int k = 0; k < n && list.Count < CloudShape.MaxBlobs; k++)
            {
                float ang = angle0 + k / (float)n * Mathf.PI * 2f;
                float rad = 0.24f * (0.75f + (float)rng.NextDouble() * 0.25f);
                float bx = lean * 0.9f + Mathf.Cos(ang) * rad;
                float bz = Mathf.Sin(ang) * rad;
                list.Add(new Blob(new Vector3(bx, ay, bz), 0.17f * Jitter(rng, 0.16f)));
            }
        }
        return list;
    }

    private static float Jitter(System.Random rng, float amount)
        => 1f + ((float)rng.NextDouble() - 0.5f) * 2f * amount;
}
