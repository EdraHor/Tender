using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The coarse MASS of a cloud: a short list of ellipsoids that <c>Custom/CloudStep0</c>
/// smooth-unions as true signed distances.
///
/// These lobes are an INVISIBLE SEED. They decide where the cloud's weight sits; the billow
/// layer in the shader then displaces the surface outward by tens of metres and erases them
/// from the silhouette entirely. That is why a handful of fat lobes is enough, and why the old
/// approach - dozens of small spheres hand-placed to *be* the shape - was the wrong job for the
/// wrong tool: it made the primitive visible and the authoring miserable.
///
/// Centres and radii are fractions of the render box (which is the object's scale), so a cloud
/// keeps its proportions no matter how you scale the GameObject.
///
/// Note the radii describe the FINAL cloud, not the seed: the shader shrinks every lobe by the
/// worst-case billow first, so the bulges grow the mass back out to roughly what you authored.
/// A consequence worth knowing: a lobe thinner than the billow amplitude cannot survive that
/// shrink, so keep lobes fat and let the noise do the fine work.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class CloudMass : MonoBehaviour
{
    /// <summary>Must match MAX_LOBES in CloudStep0.shader.</summary>
    public const int MaxLobes = 8;

    [System.Serializable]
    public struct Lobe
    {
        [Tooltip("Centre, as a fraction of the box (-0.5..0.5).")]
        public Vector3 Center;

        [Tooltip("Radii, as a fraction of the box.")]
        public Vector3 Radii;

        public Lobe(Vector3 center, Vector3 radii)
        {
            Center = center;
            Radii = radii;
        }
    }

    [Tooltip("The ellipsoids that make up this cloud's mass. Smooth-unioned in the shader.")]
    [SerializeField] private List<Lobe> _lobes = new List<Lobe>();

    private static readonly int LobesId = Shader.PropertyToID("_Lobes");
    private static readonly int LobeRadiiId = Shader.PropertyToID("_LobeRadii");
    private static readonly int LobeCountId = Shader.PropertyToID("_LobeCount");

    private Renderer _renderer;
    private MaterialPropertyBlock _block;
    private readonly Vector4[] _centers = new Vector4[MaxLobes];
    private readonly Vector4[] _radii = new Vector4[MaxLobes];

    public IReadOnlyList<Lobe> Lobes => _lobes;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    /// <summary>Push the current lobe list into the renderer's MaterialPropertyBlock.</summary>
    public void Apply()
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer == null || _lobes == null) return;
        if (_block == null) _block = new MaterialPropertyBlock();

        int count = Mathf.Min(_lobes.Count, MaxLobes);
        for (int i = 0; i < count; i++)
        {
            Lobe lobe = _lobes[i];
            _centers[i] = lobe.Center;
            _radii[i] = lobe.Radii;
        }
        for (int i = count; i < MaxLobes; i++)
        {
            _centers[i] = Vector4.zero;
            _radii[i] = Vector4.zero;
        }

        _renderer.GetPropertyBlock(_block);
        _block.SetVectorArray(LobesId, _centers);       // MPB arrays are not serialized ->
        _block.SetVectorArray(LobeRadiiId, _radii);     // they are re-pushed in OnEnable
        _block.SetFloat(LobeCountId, count);
        _renderer.SetPropertyBlock(_block);
    }

    public void SetLobes(IEnumerable<Lobe> lobes)
    {
        _lobes = new List<Lobe>(lobes);
        Apply();
    }

    /// <summary>Towering cumulus congestus: broad base tapering smoothly to a cauliflower crown.</summary>
    [ContextMenu("Build Tower")]
    public void BuildTower()
    {
        SetLobes(new[]
        {
            new Lobe(new Vector3( 0.00f, -0.30f,  0.00f), new Vector3(0.31f, 0.18f, 0.30f)), // broad base
            new Lobe(new Vector3( 0.02f, -0.12f, -0.02f), new Vector3(0.29f, 0.19f, 0.28f)), // lower body
            new Lobe(new Vector3(-0.02f,  0.04f,  0.02f), new Vector3(0.26f, 0.19f, 0.25f)), // mid column
            new Lobe(new Vector3( 0.03f,  0.18f, -0.02f), new Vector3(0.23f, 0.18f, 0.22f)), // upper column
            new Lobe(new Vector3( 0.00f,  0.30f,  0.01f), new Vector3(0.20f, 0.17f, 0.19f)), // shoulders
            new Lobe(new Vector3( 0.01f,  0.38f,  0.00f), new Vector3(0.16f, 0.13f, 0.15f)), // rounded cap
        });
    }

    /// <summary>Fair-weather cumulus: one wide mound with a couple of shoulders.</summary>
    [ContextMenu("Build Puff")]
    public void BuildPuff()
    {
        SetLobes(new[]
        {
            new Lobe(new Vector3( 0.00f, -0.10f,  0.00f), new Vector3(0.32f, 0.22f, 0.30f)),
            new Lobe(new Vector3(-0.14f, -0.04f,  0.06f), new Vector3(0.20f, 0.18f, 0.19f)),
            new Lobe(new Vector3( 0.15f,  0.02f, -0.05f), new Vector3(0.19f, 0.19f, 0.18f)),
            new Lobe(new Vector3( 0.02f,  0.16f,  0.02f), new Vector3(0.18f, 0.17f, 0.17f)),
        });
    }
}
