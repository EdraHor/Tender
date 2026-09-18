using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the BILLOW source texture: a tileable 3D Worley (cellular) noise.
///
/// Why Worley and not Perlin: an inverted Worley F1 is a field of ROUND BUMPS. Perlin is
/// soft grey haze. A cumulus cloud is bumps-on-bumps ("cauliflower"), so the billow source
/// has to be bumpy at its core — no amount of layering turns haze into cauliflower.
///
/// Four decorrelated variants are packed into RGBA so the raymarch can pick a different
/// channel per octave and never repeat the same pattern at two scales:
///   R = 4 cells   G = 4 cells (other seed)   B = 3 cells (coarse)   A = 8 cells (fine)
///
/// The noise is TILEABLE: feature points wrap across the volume edges, so the texture can be
/// sampled at any world scale with Repeat wrapping and never show a seam.
/// </summary>
public static class CloudBillowBaker
{
    private const int Resolution = 128;
    private const string OutputPath = "Assets/_Tender/Art/Clouds/CloudBillow3D.asset";

    /// <summary>Cells per axis for each channel (R, G, B, A).</summary>
    private static readonly int[] CellCounts = { 4, 4, 3, 8 };
    private static readonly int[] Seeds = { 1337, 7331, 991, 20250903 };

    [MenuItem("Tools/Clouds/Bake Billow Noise")]
    public static void Bake()
    {
        int voxels = Resolution * Resolution * Resolution;
        var channels = new byte[4][];

        for (int c = 0; c < 4; c++)
        {
            EditorUtility.DisplayProgressBar("Baking billow noise", "Channel " + c, c / 4f);
            channels[c] = BakeChannel(CellCounts[c], Seeds[c]);
        }
        EditorUtility.ClearProgressBar();

        var pixels = new Color32[voxels];
        for (int i = 0; i < voxels; i++)
            pixels[i] = new Color32(channels[0][i], channels[1][i], channels[2][i], channels[3][i]);

        var texture = new Texture3D(Resolution, Resolution, Resolution, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear
        };
        texture.SetPixels32(pixels);
        texture.Apply(true);

        AssetDatabase.CreateAsset(texture, OutputPath);
        AssetDatabase.SaveAssets();
        Debug.Log("Baked " + Resolution + "^3 tileable Worley -> " + OutputPath);
    }

    /// <summary>One channel: inverted Worley F1 (1 at a cell's feature point, 0 between cells).</summary>
    private static byte[] BakeChannel(int cells, int seed)
    {
        // Scatter one feature point per cell. Positions are normalised 0..1 across the volume.
        int cellCount = cells * cells * cells;
        var pointX = new float[cellCount];
        var pointY = new float[cellCount];
        var pointZ = new float[cellCount];
        var rng = new System.Random(seed);

        for (int i = 0; i < cellCount; i++)
        {
            int cz = i / (cells * cells);
            int cy = (i / cells) % cells;
            int cx = i % cells;
            pointX[i] = (cx + (float)rng.NextDouble()) / cells;
            pointY[i] = (cy + (float)rng.NextDouble()) / cells;
            pointZ[i] = (cz + (float)rng.NextDouble()) / cells;
        }

        var output = new byte[Resolution * Resolution * Resolution];

        for (int z = 0; z < Resolution; z++)
        {
            float pz = (z + 0.5f) / Resolution;
            int baseZ = (int)(pz * cells);

            for (int y = 0; y < Resolution; y++)
            {
                float py = (y + 0.5f) / Resolution;
                int baseY = (int)(py * cells);

                for (int x = 0; x < Resolution; x++)
                {
                    float px = (x + 0.5f) / Resolution;
                    int baseX = (int)(px * cells);

                    // Nearest feature point among the 3x3x3 neighbouring cells. Cells wrap, and a
                    // wrapped neighbour's point is offset by one volume width -> seamless tiling.
                    float best = float.MaxValue;
                    for (int oz = -1; oz <= 1; oz++)
                    {
                        int gz = baseZ + oz;
                        float wrapZ = 0f;
                        if (gz < 0) { gz += cells; wrapZ = -1f; }
                        else if (gz >= cells) { gz -= cells; wrapZ = 1f; }

                        for (int oy = -1; oy <= 1; oy++)
                        {
                            int gy = baseY + oy;
                            float wrapY = 0f;
                            if (gy < 0) { gy += cells; wrapY = -1f; }
                            else if (gy >= cells) { gy -= cells; wrapY = 1f; }

                            for (int ox = -1; ox <= 1; ox++)
                            {
                                int gx = baseX + ox;
                                float wrapX = 0f;
                                if (gx < 0) { gx += cells; wrapX = -1f; }
                                else if (gx >= cells) { gx -= cells; wrapX = 1f; }

                                int index = gz * cells * cells + gy * cells + gx;
                                float dx = pointX[index] + wrapX - px;
                                float dy = pointY[index] + wrapY - py;
                                float dz = pointZ[index] + wrapZ - pz;
                                float sqr = dx * dx + dy * dy + dz * dz;
                                if (sqr < best) best = sqr;
                            }
                        }
                    }

                    float distance = Mathf.Sqrt(best) * cells;      // in cell widths
                    float bump = Mathf.Clamp01(1f - distance);      // inverted F1 -> round bump
                    output[x + y * Resolution + z * Resolution * Resolution] = (byte)(bump * 255f);
                }
            }
        }

        return output;
    }
}
