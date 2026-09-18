using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A cloud is a LIST OF SPHERES ("blobs"). This component holds that list and feeds it to the
/// <c>Custom/CloudHero</c> shader, which raymarches their smooth union and carves it with noise.
///
/// The same shader + component makes a hero cumulus, a towering cumulonimbus, or a segment of a
/// cloud wall — only the list changes. That is the whole modularity story: to build a sky, place
/// several of these and give each its own list (see <see cref="CloudPresets"/>).
///
/// Blob centres are in OBJECT space (the render box is -0.5..0.5); radius is a fraction of the box,
/// so a cloud keeps its shape no matter how you scale the GameObject.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class CloudShape : MonoBehaviour
{
    /// <summary>Must match <c>MAX_BLOBS</c> in CloudHero.shader.</summary>
    public const int MaxBlobs = 32;

    [System.Serializable]
    public struct Blob
    {
        public Vector3 Center;                       // object space, box is -0.5..0.5
        [Range(0.03f, 0.6f)] public float Radius;    // fraction of the box

        public Blob(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }

    [Tooltip("The spheres that make up this cloud. They are smooth-unioned in the shader.")]
    [SerializeField] private List<Blob> _blobs = new List<Blob>();

    private static readonly int BlobsId = Shader.PropertyToID("_Blobs");
    private static readonly int BlobCountId = Shader.PropertyToID("_BlobCount");

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private readonly Vector4[] _packed = new Vector4[MaxBlobs];

    public IReadOnlyList<Blob> Blobs => _blobs;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    /// <summary>Push the current blob list into the renderer's MaterialPropertyBlock.</summary>
    public void Apply()
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer == null || _blobs == null) return;
        if (_mpb == null) _mpb = new MaterialPropertyBlock();

        int n = Mathf.Min(_blobs.Count, MaxBlobs);
        for (int i = 0; i < n; i++)
        {
            Blob b = _blobs[i];
            _packed[i] = new Vector4(b.Center.x, b.Center.y, b.Center.z, b.Radius);
        }
        for (int i = n; i < MaxBlobs; i++)
            _packed[i] = Vector4.zero;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetVectorArray(BlobsId, _packed);   // MPB arrays aren't serialized -> re-pushed in OnEnable
        _mpb.SetFloat(BlobCountId, n);
        _renderer.SetPropertyBlock(_mpb);
    }

    /// <summary>Replace the whole list (used by preset builders and tooling), then push it.</summary>
    public void SetBlobs(IEnumerable<Blob> blobs)
    {
        _blobs = new List<Blob>(blobs);
        Apply();
    }
}
