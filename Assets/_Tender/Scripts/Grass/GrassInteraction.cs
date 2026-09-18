using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The grass's memory of being touched, and how it recovers. Sits next to the GrassField it belongs to.
///
/// Every frame it collects the shapes of every enabled GrassInteractor and paints them into a map of the
/// ground round the camera (GrassInteraction.compute). The map remembers: grass pushed aside springs back
/// up in a moment, flattened grass takes minutes to grow back, and that difference is a path through a
/// meadow. The tall blades, the short shell grass and the ground all read the same map.
///
/// The map covers a square round the camera and slides with it a whole texel at a time, so the ground
/// under a texel never changes. Past its edge nothing is remembered - except REMOVE, which the grass
/// placement also checks directly, so a house on a far hill still has no grass through its floor.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(GrassField))]
public class GrassInteraction : MonoBehaviour
{
    /// <summary>Must be at most the shape count the GPU buffer is made for.</summary>
    public const int MaxShapes = 128;

    /// <summary>The one GrassField's interaction map in use, or null.</summary>
    public static GrassInteraction Current { get; private set; }

    [Header("How the grass recovers")]
    [Tooltip("Seconds pushed grass takes to stand back up once nothing presses on it.")]
    [SerializeField] private float _springBack = 0.6f;

    [Tooltip("Seconds flattened or cleared grass takes to grow back. Minutes leaves paths that last a while; a few seconds, grass that barely remembers.")]
    [SerializeField] private float _regrow = 90f;

    [Header("How touched grass looks")]
    [Tooltip("How far fully pushed grass leans away, in radians. 1.2 lays it almost flat.")]
    [Range(0f, 1.5f)][SerializeField] private float _pushLean = 1.1f;

    [Tooltip("How far flattened grass lies over, in radians - each blade its own way, like trampled grass.")]
    [Range(0f, 1.5f)][SerializeField] private float _flattenLean = 1.2f;

    [Tooltip("How tall fully flattened grass still is, as a share of its height.")]
    [Range(0f, 1f)][SerializeField] private float _flattenedHeight = 0.35f;

    [Header("Map")]
    [Tooltip("Metres of ground round the camera where the grass remembers being touched.")]
    [SerializeField] private float _area = 128f;

    [Tooltip("Texels along each side of the map. 128 m over 1024 texels is 12.5 cm each - about a footprint.")]
    [SerializeField] private int _resolution = 1024;

    [Tooltip("How tall a column of grass objects can touch, in metres. Anything further off the ground than this passes over the grass.")]
    [SerializeField] private float _grassHeight = 1.2f;

    [SerializeField] private ComputeShader _compute;

    private static readonly int MapId = Shader.PropertyToID("_GrassInteractionMap");
    private static readonly int AreaId = Shader.PropertyToID("_GrassInteractionArea");
    private static readonly int PushLeanId = Shader.PropertyToID("_GrassPushLean");
    private static readonly int FlattenLeanId = Shader.PropertyToID("_GrassFlattenLean");
    private static readonly int FlattenedHeightId = Shader.PropertyToID("_GrassFlattenedHeight");

    private readonly RenderTexture[] _maps = new RenderTexture[2];
    private readonly List<GrassInteractor.Gpu> _shapes = new List<GrassInteractor.Gpu>();
    private GraphicsBuffer _shapeBuffer;
    private GrassField _field;
    private int _kernel;
    private int _read;
    private Vector2Int _originCell;
    private bool _hasOrigin;
    private double _lastTime;

    /// <summary>This frame's map: RG push, B flattened, A removed.</summary>
    public Texture Map => _maps[_read];

    /// <summary>World XZ of the map's corner, then its size in metres.</summary>
    public Vector4 MapArea { get; private set; }

    public GraphicsBuffer ShapeBuffer => _shapeBuffer;
    public int ShapeCount => _shapes.Count;
    public float FlattenedHeight => _flattenedHeight;
    public float GrassHeight => _grassHeight;

    private void OnEnable()
    {
        _field = GetComponent<GrassField>();
        Current = this;
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
        Shader.SetGlobalVector(AreaId, Vector4.zero);     // shaders: no map, nothing touched
        Release();
    }

    private void OnValidate() => _hasOrigin = false;       // a new size or resolution starts a new map

    private void Update()
    {
        if (_compute == null || _field == null || _field.HeightMap == null) return;
        Current = this;

        if (_maps[0] == null || _maps[0].width != _resolution) Build();

        CollectShapes();
        Step();

        Shader.SetGlobalTexture(MapId, Map);
        Shader.SetGlobalVector(AreaId, MapArea);
        Shader.SetGlobalFloat(PushLeanId, _pushLean);
        Shader.SetGlobalFloat(FlattenLeanId, _flattenLean);
        Shader.SetGlobalFloat(FlattenedHeightId, _flattenedHeight);
    }

    private void CollectShapes()
    {
        _shapes.Clear();
        foreach (GrassInteractor interactor in GrassInteractor.All)
            interactor.CollectShapes(_shapes, MaxShapes);
        _shapeBuffer.SetData(_shapes);
    }

    /// <summary>Move the map with the camera, then run one frame of touching and recovering on the GPU.</summary>
    private void Step()
    {
        Camera view = Camera.main;
        Vector3 focus = view != null ? view.transform.position : transform.position;

        float texel = _area / _resolution;
        var originCell = new Vector2Int(Mathf.FloorToInt(focus.x / texel) - _resolution / 2,
                                        Mathf.FloorToInt(focus.z / texel) - _resolution / 2);

        // How far the map slid since last frame. A first frame, or a jump right off the map, knows nothing.
        Vector2Int shift = _hasOrigin ? originCell - _originCell : new Vector2Int(_resolution, _resolution);
        _originCell = originCell;
        _hasOrigin = true;
        MapArea = new Vector4(originCell.x * texel, originCell.y * texel, _area, _area);

        // Edit Mode has no steady Time.deltaTime, so measure real time there, capped so a long pause in
        // the editor does not heal every path in one frame.
        double now = Time.realtimeSinceStartupAsDouble;
        float deltaTime = Application.isPlaying ? Time.deltaTime : (float)System.Math.Min(now - _lastTime, 0.1);
        _lastTime = now;

        Terrain terrain = _field.Terrain;
        TerrainData data = terrain.terrainData;

        int write = 1 - _read;
        _compute.SetTexture(_kernel, "_Previous", _maps[_read]);
        _compute.SetTexture(_kernel, "_State", _maps[write]);
        _compute.SetBuffer(_kernel, "_Shapes", _shapeBuffer);
        _compute.SetInt("_ShapeCount", _shapes.Count);
        _compute.SetTexture(_kernel, "_HeightMap", _field.HeightMap);
        _compute.SetVector("_TerrainOrigin", new Vector2(terrain.transform.position.x, terrain.transform.position.z));
        _compute.SetVector("_TerrainSize", new Vector2(data.size.x, data.size.z));
        _compute.SetVector("_HeightMapUVScaleOffset", _field.HeightMapUV);
        _compute.SetVector("_MapOrigin", new Vector2(MapArea.x, MapArea.y));
        _compute.SetFloat("_TexelSize", texel);
        _compute.SetInt("_Resolution", _resolution);
        _compute.SetInts("_Shift", shift.x, shift.y);
        _compute.SetFloat("_DeltaTime", deltaTime);

        // Rates, from times: after `time` seconds the grass has made 95% of its way back.
        _compute.SetFloat("_SpringBackRate", 3f / Mathf.Max(_springBack, 0.01f));
        _compute.SetFloat("_RegrowRate", 3f / Mathf.Max(_regrow, 0.01f));
        _compute.SetFloat("_GrassHeight", _grassHeight);

        int groups = Mathf.CeilToInt(_resolution / 8f);
        _compute.Dispatch(_kernel, groups, groups, 1);
        _read = write;
    }

    private void Build()
    {
        Release();
        _kernel = _compute.FindKernel("CSInteract");
        _shapeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxShapes, GrassInteractor.Gpu.Stride);

        for (int i = 0; i < 2; i++)
        {
            _maps[i] = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = $"GrassInteractionMap{i}",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _maps[i].Create();
        }

        _hasOrigin = false;
    }

    private void Release()
    {
        _shapeBuffer?.Release();
        _shapeBuffer = null;

        for (int i = 0; i < 2; i++)
        {
            if (_maps[i] == null) continue;
            _maps[i].Release();
            if (Application.isPlaying) Destroy(_maps[i]);
            else DestroyImmediate(_maps[i]);
            _maps[i] = null;
        }
    }
}
