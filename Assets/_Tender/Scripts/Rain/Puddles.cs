using UnityEngine;

/// <summary>
/// Where puddles form on the terrain: baked once into a map (Puddles.compute) from the height
/// map GrassField already holds, and handed to the shaders as a global. How FULL the puddles are
/// is the world's wetness (Rain.cs): they show once the ground is soaked and are the last thing
/// to dry. MeadowGround.shader draws them.
/// </summary>
[ExecuteAlways]
public class Puddles : MonoBehaviour
{
    [SerializeField] private GrassField _field;
    [SerializeField] private ComputeShader _bake;
    [SerializeField] private float _reach = 8f;       // metres around a point that count as "the surroundings"
    [SerializeField] private float _depth = 0.25f;    // metres below them for a full puddle

    private static readonly int MapId = Shader.PropertyToID("_PuddleMap");
    private static readonly int AreaId = Shader.PropertyToID("_PuddleArea");

    private RenderTexture _map;
    private Texture _bakedFrom;

    private void OnDisable()
    {
        if (_map != null) _map.Release();
        _map = null;
        _bakedFrom = null;
        Shader.SetGlobalTexture(MapId, Texture2D.blackTexture);
    }

    private void LateUpdate()
    {
        if (_field == null || _bake == null || _field.HeightMap == null || _field.Terrain == null) return;
        if (_bakedFrom != _field.HeightMap) Bake();
    }

    public void Bake()
    {
        Texture heights = _field.HeightMap;
        int resolution = heights.width;
        if (_map == null || _map.width != resolution)
        {
            if (_map != null) _map.Release();
            _map = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.R8) { enableRandomWrite = true, name = "Puddle map", wrapMode = TextureWrapMode.Clamp };
            _map.Create();
        }

        TerrainData data = _field.Terrain.terrainData;
        Vector3 origin = _field.Terrain.transform.position;
        _bake.SetTexture(0, "_HeightMap", heights);
        _bake.SetTexture(0, "_PuddleMap", _map);
        _bake.SetInt("_Resolution", resolution);
        _bake.SetFloat("_TexelMetres", data.size.x / (resolution - 1));
        _bake.SetFloat("_Reach", _reach);
        _bake.SetFloat("_Depth", _depth);
        int groups = Mathf.CeilToInt(resolution / 8f);
        _bake.Dispatch(0, groups, groups, 1);
        _bakedFrom = heights;

        // The same texel convention as the grass: sample j sits at j / (resolution - 1) across the terrain.
        Shader.SetGlobalTexture(MapId, _map);
        Shader.SetGlobalVector(AreaId, new Vector4(origin.x, origin.z, 1f / data.size.x, 1f / data.size.z));
        Shader.SetGlobalVector("_PuddleMapUVScaleOffset", _field.HeightMapUV);
    }
}
