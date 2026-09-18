using UnityEngine;

/// <summary>
/// Draws the rain: one indirect draw of as many streak quads as the air holds, and one more for
/// the splashes where drops hit things that can get wet. The drops themselves live entirely in
/// RainDrop.shader - this only says how many and keeps time.
///
/// How many: Rain.DropsPerCubicMetre times the box the shader draws in, so the rain you see and
/// the rain that wets a garment are the same rain.
/// </summary>
[ExecuteAlways]
public class RainDrops : MonoBehaviour
{
    [SerializeField] private Material _material;          // Tender/RainDrop
    [SerializeField] private Material _splashMaterial;    // Tender/RainSplash

    private static readonly int SeedId = Shader.PropertyToID("_RainSeed");
    private static readonly int BoxId = Shader.PropertyToID("_Box");
    private static readonly int HeightId = Shader.PropertyToID("_Height");
    private static readonly int HitsId = Shader.PropertyToID("_Hits");
    private static readonly int HitLifeId = Shader.PropertyToID("_HitLife");
    private static readonly int NowId = Shader.PropertyToID("_Now");

    private float _seed;
    private ComputeBuffer _hits;
    private readonly Vector4[] _hitData = new Vector4[Rain.MaxHits];

    private void OnDisable()
    {
        _hits?.Release();
        _hits = null;
    }

    private void Update()
    {
        _seed += Application.isPlaying ? Time.deltaTime : 1f / 60f;
        Draw();
    }

    /// <summary>Queue this frame's drops and splashes for every camera.</summary>
    public void Draw()
    {
        Camera view = Camera.main;
        Vector3 centre = view != null ? view.transform.position : Vector3.zero;

        if (_material != null)
        {
            float box = _material.GetFloat(BoxId), height = _material.GetFloat(HeightId);
            int count = Mathf.RoundToInt(Rain.DropsPerCubicMetre * box * box * height * Rain.Intensity);
            if (count > 0)
            {
                _material.SetFloat(SeedId, _seed);
                Graphics.RenderPrimitives(Params(_material, centre), MeshTopology.Triangles, 6, count);
            }
        }

        if (_splashMaterial != null && Rain.Hits.Count > 0)
        {
            _hits ??= new ComputeBuffer(Rain.MaxHits, sizeof(float) * 4);
            Rain.Hits.CopyTo(_hitData);
            _hits.SetData(_hitData, 0, 0, Rain.Hits.Count);
            _splashMaterial.SetBuffer(HitsId, _hits);
            _splashMaterial.SetFloat(HitLifeId, Rain.HitLife);
            _splashMaterial.SetFloat(NowId, Time.time);
            Graphics.RenderPrimitives(Params(_splashMaterial, centre), MeshTopology.Triangles, 6, Rain.Hits.Count);
        }
    }

    private RenderParams Params(Material material, Vector3 centre) => new RenderParams(material)
    {
        worldBounds = new Bounds(centre, new Vector3(200f, 200f, 200f)),
        layer = gameObject.layer,
        shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
        receiveShadows = false
    };
}
