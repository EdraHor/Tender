using UnityEngine;

/// <summary>
/// Builds the blade meshes in code.
///
/// A blade is a flat strip that narrows to a point: a few quads stacked along the length, capped
/// by a single triangle. Nine vertices at the near LOD, three at the far one - the whole "model"
/// is one parameter, which is why this is code and not an FBX.
///
/// The mesh is deliberately STRAIGHT and vertical. Every curve you see in game - the resting
/// lean, the wind, the dent an object leaves - is added by the vertex shader. One mesh therefore
/// serves every blade in the field, and each blade can bend differently for free.
///
/// Normals are fanned across the width, as if the blade were a shallow cylinder rather than a
/// flat ribbon. Truly flat normals make a whole field light up or go dark together, which reads
/// as a sheet of paper; the fan keeps neighbouring blades slightly different and the field
/// stays soft.
/// </summary>
public static class GrassBladeMesh
{
    /// <summary>
    /// Blade with <paramref name="segments"/> quads plus a tip triangle.
    /// Vertex count is segments * 2 + 1, so 4 segments = 9 vertices, 1 segment = 3.
    /// The blade stands on the origin, grows along +Y and faces +Z; it is 1 unit tall and
    /// <paramref name="width"/> wide, and the renderer scales it per instance.
    /// </summary>
    public static Mesh Build(int segments, float width, float taper = 0.35f, float normalFan = 0.7f)
    {
        segments = Mathf.Max(1, segments);

        int rows = segments;                    // rows of two vertices, then one tip vertex
        var vertices = new Vector3[rows * 2 + 1];
        var normals = new Vector3[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[((segments - 1) * 2 + 1) * 3];

        Vector3 leftNormal = new Vector3(-normalFan, 0f, 1f).normalized;
        Vector3 rightNormal = new Vector3(normalFan, 0f, 1f).normalized;

        for (int row = 0; row < rows; row++)
        {
            // 0 at the root, approaching 1 at the tip - with the rows packed towards the tip,
            // because that is where GrassBend puts the curve. Evenly spaced rows leave the curl
            // drawn as two or three visible straight segments; the straight base needs few.
            float t = 1f - Mathf.Pow(1f - row / (float)segments, 1.5f);
            float halfWidth = width * 0.5f * Mathf.Lerp(1f, taper, t);

            int a = row * 2;
            vertices[a] = new Vector3(-halfWidth, t, 0f);
            vertices[a + 1] = new Vector3(halfWidth, t, 0f);
            normals[a] = leftNormal;
            normals[a + 1] = rightNormal;
            uvs[a] = new Vector2(0f, t);                // uv.y is the root-to-tip factor the
            uvs[a + 1] = new Vector2(1f, t);            // shader uses for gradient and bending
        }

        int tip = rows * 2;
        vertices[tip] = new Vector3(0f, 1f, 0f);
        normals[tip] = Vector3.forward;
        uvs[tip] = new Vector2(0.5f, 1f);

        // Wound so the FRONT face is the one the normals point out of, i.e. +Z.
        //
        // This matters because the shader flips the normal on back faces: `normal * (isFront ? 1 : -1)`.
        // Wind the triangles the other way round and every one of those tests is inverted, so the
        // side facing the sun gets shaded and the side in shadow gets lit. Wrapped lighting and
        // backlighting hide it well enough that it reads as "the grass is a bit flat" rather than
        // as a bug, which is exactly why it is worth being careful here.
        int index = 0;
        for (int row = 0; row < segments - 1; row++)
        {
            int a = row * 2;
            int b = a + 2;
            triangles[index++] = a; triangles[index++] = a + 1; triangles[index++] = b;
            triangles[index++] = a + 1; triangles[index++] = b + 1; triangles[index++] = b;
        }

        int last = (rows - 1) * 2;
        triangles[index++] = last; triangles[index++] = last + 1; triangles[index] = tip;

        var mesh = new Mesh
        {
            name = $"GrassBlade_{segments}seg",
            hideFlags = HideFlags.HideAndDontSave      // generated, never an asset on disk
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);

        // The vertex shader bends blades well outside this box, but the renderer passes its own
        // bounds to the indirect draw, so these are only ever used by the editor preview.
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// What sits on top of a stem: a flat disc, one unit across, with a scalloped rim. The flower
    /// shader turns it to the camera as a seed head, where the scallops read as fluff, or lays it
    /// facing the sky as a bloom, where they read as petals.
    ///
    /// A shape rather than a textured quad because it needs no alpha test. An alpha-tested quad
    /// this small shimmers as the camera moves, and there would be thousands of them.
    /// </summary>
    public static Mesh BuildFlower(int petals = 6, int pointsPerPetal = 4)
    {
        petals = Mathf.Clamp(petals, 3, 12);
        int sides = petals * Mathf.Max(pointsPerPetal, 2);

        var vertices = new Vector3[sides + 1];
        var normals = new Vector3[sides + 1];
        var uvs = new Vector2[sides + 1];
        var triangles = new int[sides * 3];

        vertices[0] = Vector3.zero;
        // Forward, to match the winding below and the blade mesh's convention. The flower shader
        // works out its own facing, but a mesh whose normals disagree with its faces is a trap
        // left lying around for later.
        normals[0] = Vector3.forward;
        uvs[0] = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < sides; i++)
        {
            float angle = i / (float)sides * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            // Full radius at the middle of each petal, pinched in between them.
            float radius = Mathf.Lerp(0.45f, 1f, Mathf.Abs(Mathf.Cos(angle * petals * 0.5f)));

            vertices[i + 1] = new Vector3(cos, sin, 0f) * radius;
            normals[i + 1] = Vector3.forward;
            // The UV keeps the real distance from the middle, which the shader uses to find the
            // centre of a bloom and to darken the heart of a seed head.
            uvs[i + 1] = new Vector2(0.5f + 0.5f * cos * radius, 0.5f + 0.5f * sin * radius);
        }

        for (int i = 0; i < sides; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = 1 + i;
            triangles[i * 3 + 2] = 1 + (i + 1) % sides;
        }

        var mesh = new Mesh
        {
            name = "GrassFlower",
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
