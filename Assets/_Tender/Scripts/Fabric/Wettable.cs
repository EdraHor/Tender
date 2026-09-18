using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lets one mesh get wet: spot by spot where drops land, and as a whole.
///
/// Keeps a small texture in the mesh's UV space (the wet map: how much water the cloth holds at
/// each texel) and pushes it, together with the overall wetness, to the renderer through a
/// MaterialPropertyBlock - never through .material, so no material clones. Any shader that
/// includes ClothWet.hlsl reads both.
///
/// A drop lands by calling <see cref="AddHit"/> with a world position; Rain.cs does that. To know
/// which texels that touches, the mesh is drawn flat by its UVs (ClothUnwrap.shader), giving a
/// world position per texel; redrawn whenever the mesh moves, every frame if it is skinned. Then
/// each frame the water spreads through the cloth by wicking and evaporates - see WetMap.compute.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class Wettable : MonoBehaviour
{
    public static readonly List<Wettable> All = new List<Wettable>();

    [SerializeField, Range(0f, 1f)] private float _wetness;            // the whole garment
    [SerializeField] private bool _takesDrops = true;                  // spots from Rain
    [SerializeField] private float _dryTime = 60f;                     // seconds for a spot to go
    [Tooltip("How fast water spreads through the cloth, mm² per second. Nylon ~0.5, cotton ~3, a thick knit ~8.")]
    [SerializeField] private float _wicking = 0.5f;
    [SerializeField] private int _resolution = 512;
    [SerializeField] private ComputeShader _wetMapCompute;
    [SerializeField] private Shader _unwrapShader;

    private const int MaxHits = 64;

    private Renderer _renderer;
    private RenderTexture _wetMap;       // the current map
    private RenderTexture _wetMapNext;   // the one Settle writes into; the two swap each frame
    private RenderTexture _positionMap;
    private Material _unwrap;
    private ComputeBuffer _hitBuffer;
    private MaterialPropertyBlock _block;
    private readonly List<Vector4> _hits = new List<Vector4>();
    private Matrix4x4 _bakedMatrix;
    private bool _positionsBaked;

    public float Wetness
    {
        get => _wetness;
        set => _wetness = Mathf.Clamp01(value);
    }

    public bool TakesDrops => _takesDrops;
    public Bounds Bounds => _renderer.bounds;

    /// <summary>
    /// How wet the whole garment is, 0..1: the slider plus the average of the wet map, read back
    /// from the GPU twice a second. For things that need one number - the cloth simulation, a
    /// sound - not for the shader, which reads the map itself.
    /// </summary>
    public float Average => Mathf.Clamp01(_wetness + _mapAverage);

    private float _mapAverage;
    private float _nextReadback;

    public void AddHit(Vector3 position, float radius)
    {
        if (_takesDrops && _hits.Count < MaxHits) _hits.Add(new Vector4(position.x, position.y, position.z, radius));
    }

    private void OnEnable()
    {
        _renderer = GetComponent<Renderer>();
        _positionsBaked = false;
        All.Add(this);
    }

    private void OnDisable()
    {
        All.Remove(this);
        _renderer.SetPropertyBlock(null);
        if (_wetMap != null) _wetMap.Release();
        if (_wetMapNext != null) _wetMapNext.Release();
        if (_positionMap != null) _positionMap.Release();
        _hitBuffer?.Release();
        if (_unwrap != null) DestroyImmediate(_unwrap);
        _wetMap = null;
        _wetMapNext = null;
        _positionMap = null;
        _hitBuffer = null;
        _unwrap = null;
    }

    // Made on first use rather than in OnEnable: in the editor a component is enabled before its
    // fields are filled in, so the shader and compute references may not be there yet.
    private bool Ready()
    {
        if (_wetMapCompute == null || _unwrapShader == null) return false;
        if (_wetMap != null) return true;
        _wetMap = WaterMap(name + " wet");
        _wetMapNext = WaterMap(name + " wet (next)");
        _positionMap = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.ARGBFloat) { name = name + " positions" };
        _positionMap.Create();
        _unwrap = new Material(_unwrapShader);
        _hitBuffer = new ComputeBuffer(MaxHits, sizeof(float) * 4);
        _block = new MaterialPropertyBlock();
        return true;
    }

    private RenderTexture WaterMap(string mapName)
    {
        var map = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.RHalf) { enableRandomWrite = true, name = mapName };
        map.Create();
        // A new render texture holds whatever was in that memory before - a garment could start
        // out randomly damp. Dry it.
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = map;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = previous;
        return map;
    }

    // In the editor the slider works but nothing spreads or dries, so what you set is what you see.
    private void LateUpdate() => Step(Application.isPlaying ? Time.deltaTime : 0f);

    /// <summary>Advance the wet map by dt seconds and hand it to the renderer.</summary>
    public void Step(float dt)
    {
        if (!Ready()) return;

        // The texels' world positions change whenever the mesh moves or is scaled, and every frame
        // for a skinned mesh.
        if (!_positionsBaked || _renderer is SkinnedMeshRenderer || _renderer.localToWorldMatrix != _bakedMatrix) BakePositions();

        int groups = Mathf.CeilToInt(_resolution / 8f);
        if (dt > 0f)
        {
            // Diffusion is only stable when a texel moves at most a fifth of the way to its
            // neighbours per pass, so a fast-wicking cloth on a fine map takes several passes a frame.
            // The texel size is guessed from the mesh's smallest side - the compute measures the
            // real one per texel; this only picks how many passes to take.
            Vector3 size = _renderer.bounds.size;
            float texel = Mathf.Max(Mathf.Min(size.x, size.y, size.z), 0.05f) / _resolution;
            float wicking = _wicking * 1e-6f;                                  // mm² -> m²
            int passes = Mathf.Clamp(Mathf.CeilToInt(wicking * dt / (texel * texel) / 0.2f), 1, 8);
            float step = dt / passes;

            _wetMapCompute.SetFloat("_DryStep", step / Mathf.Max(_dryTime, 0.01f));
            _wetMapCompute.SetFloat("_Wicking", wicking);
            _wetMapCompute.SetFloat("_DeltaTime", step);
            _wetMapCompute.SetInt("_Resolution", _resolution);
            _wetMapCompute.SetTexture(0, "_PositionMap", _positionMap);
            for (int pass = 0; pass < passes; pass++)
            {
                _wetMapCompute.SetTexture(0, "_WetMapIn", _wetMap);
                _wetMapCompute.SetTexture(0, "_WetMapOut", _wetMapNext);
                _wetMapCompute.Dispatch(0, groups, groups, 1);
                (_wetMap, _wetMapNext) = (_wetMapNext, _wetMap);
            }
        }
        if (_hits.Count > 0)
        {
            _hitBuffer.SetData(_hits);
            _wetMapCompute.SetInt("_HitCount", _hits.Count);
            _wetMapCompute.SetBuffer(1, "_Hits", _hitBuffer);
            _wetMapCompute.SetTexture(1, "_WetMap", _wetMap);
            _wetMapCompute.SetTexture(1, "_PositionMap", _positionMap);
            _wetMapCompute.Dispatch(1, groups, groups, 1);
            _hits.Clear();
        }

        _block.SetTexture("_WetMap", _wetMap);
        _block.SetFloat("_Wetness", _wetness);
        _renderer.SetPropertyBlock(_block);

        if (Application.isPlaying && Time.time >= _nextReadback)
        {
            _nextReadback = Time.time + 0.5f;
            AsyncGPUReadback.Request(_wetMap, 0, OnWetMapRead);
        }
    }

    // The map holds an amount of water per texel; half of it already looks soaked (ClothWet.hlsl),
    // and the empty gaps between UV islands count as dry, so the figure is a little conservative.
    private void OnWetMapRead(AsyncGPUReadbackRequest request)
    {
        if (request.hasError || _wetMap == null) return;
        var water = request.GetData<ushort>();
        double sum = 0.0;
        for (int i = 0; i < water.Length; i++) sum += Mathf.HalfToFloat(water[i]);
        _mapAverage = Mathf.Clamp01((float)(sum / water.Length) * 2f);
    }

    private void BakePositions()
    {
        var cmd = new CommandBuffer { name = "Cloth unwrap" };
        cmd.SetRenderTarget(_positionMap);
        cmd.ClearRenderTarget(true, true, Color.clear);
        cmd.DrawRenderer(_renderer, _unwrap);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();
        _bakedMatrix = _renderer.localToWorldMatrix;
        _positionsBaked = true;
    }
}
