using UnityEngine;

/// <summary>
/// The whole mesh side of shell grass: the same flat square, stacked once per layer.
///
/// Each copy's height, from 0 at the ground to 1 at the top, goes into its vertices' Y, and
/// SimpleShellGrass.shader scales that up to the real grass height. Change the layer count, then
/// switch the component off and on to rebuild.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleShellGrass : MonoBehaviour
{
    [Range(2, 64)][SerializeField] private int _layers = 24;
    [SerializeField] private float _size = 8f;

    private Mesh _mesh;

    private void OnEnable()
    {
        var vertices = new Vector3[_layers * 4];
        var triangles = new int[_layers * 6];
        float half = _size / 2f;

        for (int layer = 0; layer < _layers; layer++)
        {
            float height = layer / (_layers - 1f);

            int v = layer * 4;
            vertices[v] = new Vector3(-half, height, -half);
            vertices[v + 1] = new Vector3(-half, height, half);
            vertices[v + 2] = new Vector3(half, height, half);
            vertices[v + 3] = new Vector3(half, height, -half);

            int t = layer * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        _mesh = new Mesh { hideFlags = HideFlags.DontSave, vertices = vertices, triangles = triangles };
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    private void OnDisable() => DestroyImmediate(_mesh);
}
