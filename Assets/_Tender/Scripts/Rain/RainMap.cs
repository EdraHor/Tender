using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A map of where the rain lands: looking straight down over the player, the height of the first
/// thing under each point of the sky - a roof, a rock, a leaf, or the ground.
///
/// The same idea as the grass interaction map: one top-down texture around the camera that every
/// other part of the system reads. Drops (RainDrops) end where it says; anything below that
/// height is sheltered and stays dry (RainMap.hlsl, RainShelter); splashes go where drops end.
///
/// Built with one command buffer: the terrain from the height map GrassField already baked, then
/// every renderer inside the area drawn with a shader that writes nothing but its height. The
/// grass blades are not in it - they are drawn by the grass system, not by renderers - so the rain
/// lands on the soil under them, which is what a meadow does with rain anyway. A mesh that should
/// not count as a roof either (shell grass, a decal) gets a <see cref="RainMapIgnore"/>.
/// </summary>
[ExecuteAlways]
public class RainMap : MonoBehaviour
{
    [SerializeField] private float _size = 64f;          // metres across, centred on the camera
    [SerializeField] private int _resolution = 512;
    [SerializeField] private int _everyFrames = 4;       // a roof does not move much
    [SerializeField] private Shader _heightShader;       // Tender/RainHeight
    [SerializeField] private GrassField _field;          // for the terrain heights it baked

    private static readonly int MapId = Shader.PropertyToID("_RainMap");
    private static readonly int AreaId = Shader.PropertyToID("_RainMapArea");

    private RenderTexture _map;
    private Material _height;
    private Mesh _ground;
    private Camera _eye;
    private int _frame;

    public RenderTexture Map => _map;

    private void OnEnable()
    {
        // No depth buffer: the height shader keeps the highest value by blending (BlendOp Max).
        _map = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.RFloat)
        {
            name = "Rain map",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _map.Create();
        _height = new Material(_heightShader);
        _ground = GroundGrid(128);

        // A camera that never renders: it only works out the view and projection of looking down.
        var eye = new GameObject("Rain map eye") { hideFlags = HideFlags.HideAndDontSave };
        _eye = eye.AddComponent<Camera>();
        _eye.enabled = false;
        _eye.orthographic = true;
        _eye.nearClipPlane = 0.1f;
        _eye.farClipPlane = 500f;
    }

    private void OnDisable()
    {
        _map.Release();
        DestroyImmediate(_height);
        DestroyImmediate(_ground);
        if (_eye != null) DestroyImmediate(_eye.gameObject);
    }

    private void LateUpdate()
    {
        if (_frame++ % _everyFrames == 0) Build();
    }

    public void Build()
    {
        Camera view = Camera.main;
        if (view == null || _height == null) return;

        // Snap the area to whole texels so the map does not swim as the camera moves.
        float texel = _size / _resolution;
        Vector3 centre = view.transform.position;
        centre.x = Mathf.Round(centre.x / texel) * texel;
        centre.z = Mathf.Round(centre.z / texel) * texel;
        Vector2 corner = new Vector2(centre.x - _size * 0.5f, centre.z - _size * 0.5f);

        _eye.orthographicSize = _size * 0.5f;
        _eye.transform.SetPositionAndRotation(new Vector3(centre.x, centre.y + 250f, centre.z), Quaternion.LookRotation(Vector3.down, Vector3.forward));

        var cmd = new CommandBuffer { name = "Rain map" };
        cmd.SetRenderTarget(_map);
        cmd.ClearRenderTarget(false, true, new Color(-1000f, 0f, 0f, 0f));   // nothing here: far below everything
        // No render-into-texture flip: sampling this map with (x, z) as (u, v) then lands on the
        // right texel, which was checked by reading the map back against the terrain.
        cmd.SetViewProjectionMatrices(_eye.worldToCameraMatrix, GL.GetGPUProjectionMatrix(_eye.projectionMatrix, false));

        if (_field != null && _field.HeightMap != null && _field.Terrain != null)
        {
            TerrainData data = _field.Terrain.terrainData;
            Vector3 origin = _field.Terrain.transform.position;
            _height.SetTexture("_HeightMap", _field.HeightMap);
            _height.SetVector("_TerrainOrigin", new Vector4(origin.x, origin.z, 0f, 0f));
            _height.SetVector("_TerrainSize", new Vector4(data.size.x, data.size.z, 0f, 0f));
            _height.SetVector("_HeightMapUVScaleOffset", _field.HeightMapUV);
            cmd.DrawMesh(_ground, Matrix4x4.TRS(new Vector3(corner.x, 0f, corner.y), Quaternion.identity, new Vector3(_size, 1f, _size)), _height, 0, 1);
        }

        var area = new Bounds(centre, new Vector3(_size, 500f, _size));
        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
            if (renderer.GetComponent<RainMapIgnore>() != null) continue;
            Bounds bounds = renderer.bounds;
            if (bounds.min.y > centre.y + 60f) continue;     // clouds are where the rain comes FROM
            if (!area.Intersects(bounds)) continue;
            for (int sub = 0; sub < renderer.sharedMaterials.Length; sub++)
                cmd.DrawRenderer(renderer, _height, sub, 0);
        }

        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        Shader.SetGlobalTexture(MapId, _map);
        Shader.SetGlobalVector(AreaId, new Vector4(corner.x, corner.y, 1f / _size, _size));
    }

    // A unit square in XZ, cells x cells, that the terrain pass lifts to the terrain's height.
    private static Mesh GroundGrid(int cells)
    {
        int side = cells + 1;
        var vertices = new Vector3[side * side];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
                vertices[z * side + x] = new Vector3((float)x / cells, 0f, (float)z / cells);

        var triangles = new int[cells * cells * 6];
        int t = 0;
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }

        var mesh = new Mesh { name = "Rain map ground", hideFlags = HideFlags.HideAndDontSave };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.bounds = new Bounds(new Vector3(0.5f, 0f, 0.5f), new Vector3(1f, 1000f, 1f));
        return mesh;
    }
}
