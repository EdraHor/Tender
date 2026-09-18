using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// The tall grass. Drives GrassPlacement.compute and issues three indirect draw calls.
///
/// Nothing here knows how many blades there are. For every camera about to render, a grid of
/// candidate positions is laid over the ground around it, the compute shader throws away the
/// ones that are off the terrain, too far, too steep or behind the camera, and sorts the
/// survivors into three detail levels. Then three draw calls render the lot. The blade count
/// never crosses back to the CPU, which is exactly why it can be enormous.
///
/// The grid origin is SNAPPED to the cell size. Without that, blades would slide around as the
/// camera moved - each candidate has to keep its own patch of ground to stay still.
///
/// The grid is laid in rings, not as one square: full density out to _fullDensityDistance, then
/// every time the distance doubles, one blade in four carries on, twice as wide. Each ring costs
/// the same, so hundreds of metres of field cost a handful of dispatches. The rules for keeping the
/// seams invisible live in GrassPlacement.compute.
///
/// WHAT grows is a list of GrassType assets. The first grows everywhere; each one after it grows in
/// its own zones - noise, plus whatever is painted into the type map - and wins over the ones
/// before it. Each blade picks its type on the GPU as it is placed.
/// </summary>
[ExecuteAlways]
public class GrassField : MonoBehaviour
{
    /// <summary>The base type plus seven. Elements 1-4 can also be painted, one per type map channel.</summary>
    public const int MaxTypes = 8;

    [Header("Where")]
    [Tooltip("The ground the grass grows on. Its heightmap is baked once when this enables.")]
    [SerializeField] private Terrain _terrain;

    [Header("What grows")]
    [Tooltip("The first type grows everywhere. Each type after it grows in its own zones and wins over the ones before it. Up to 8; elements 1-4 can also be painted with the type map.")]
    [SerializeField] private GrassType[] _types = new GrassType[1];

    [Header("How far")]
    [Tooltip("Grass is placed out to this distance, in metres.")]
    [SerializeField] private float _viewDistance = 400f;

    [Tooltip("Full density out to here. Past it, each doubling of distance keeps a quarter of the blades and makes them twice as wide.")]
    [FormerlySerializedAs("_densityFadeStart")]
    [SerializeField] private float _fullDensityDistance = 30f;

    [Tooltip("Distances at which the blade drops to the middle and then the cheapest mesh.")]
    [SerializeField] private Vector2 _lodDistances = new Vector2(14f, 35f);

    [Tooltip("Steepest ground that still grows grass, in degrees from flat.")]
    [Range(0f, 89f)][SerializeField] private float _maxSlope = 45f;

    [Header("Regions")]
    [Tooltip("How far across one patch of similar grass is, in metres. Small values give a busy speckle; 40-80 m reads as landscape.")]
    [SerializeField] private float _regionScale = 55f;

    [Tooltip("How much shorter the poorest patch is. 0.5 means half height where the grass is worst.")]
    [Range(0f, 0.9f)][SerializeField] private float _regionHeight = 0.45f;

    [Tooltip("Band of off-screen grass kept so a fast camera turn does not show the field assembling itself, in metres.")]
    [SerializeField] private float _cullMargin = 10f;

    [Header("Seed heads")]
    [Tooltip("Past this distance the modelled head is dropped and the blade tip is painted instead, in metres. How many heads there are and what colour is set per grass type.")]
    [SerializeField] private float _flowerDistance = 28f;

    [Header("Painted maps")]
    [Tooltip("Optional map over the terrain. White leaves the grass alone; darker means less, shorter or drier. Paint it with the brush in this component's inspector.")]
    [SerializeField] private Texture2D _paintMap;

    [Tooltip("Optional map over the terrain. Black leaves the zones alone; each channel paints in the zone of one type from the list above - R element 1, G element 2, B element 3, A element 4.")]
    [SerializeField] private Texture2D _typeMap;

    [Header("Plumbing")]
    [SerializeField] private ComputeShader _placement;
    [SerializeField] private Material _material;
    [SerializeField] private Material _flowerMaterial;

    [Tooltip("Safety net. The grid never dispatches more candidates than this on a side.")]
    [SerializeField] private int _maxGridSide = 2048;

    [Tooltip("Log how many blades each detail level drew. Reading the count back STALLS the frame, so turn this off before measuring performance.")]
    [SerializeField] private bool _logBladeCount;

    // position(3), yaw, height, width, region, flower, tint, splay, groundNormal(2), type, zone
    private const int InstanceStride = 14 * sizeof(float);

    private const int Lods = 3;

    /// <summary>Three blade detail levels, then the seed heads.</summary>
    private const int Streams = 4;
    private const int FlowerStream = 3;

    private static readonly int InstancesId = Shader.PropertyToID("_Instances");
    private static readonly int LodId = Shader.PropertyToID("_GrassLod");
    private static readonly int LodDistancesId = Shader.PropertyToID("_GrassLodDistances");
    private static readonly int FlowerDistanceId = Shader.PropertyToID("_GrassFlowerDistance");
    private static readonly int RegionScaleId = Shader.PropertyToID("_GrassRegionScale");
    private static readonly int PaintMapId = Shader.PropertyToID("_GrassPaintMap");
    private static readonly int TypeMapId = Shader.PropertyToID("_GrassTypeMap");
    private static readonly int PaintAreaId = Shader.PropertyToID("_GrassPaintArea");
    private static readonly int TypesId = Shader.PropertyToID("_GrassTypes");
    private static readonly int TypeCountId = Shader.PropertyToID("_GrassTypeCount");

    private readonly Mesh[] _meshes = new Mesh[Streams];
    private readonly GraphicsBuffer[] _instances = new GraphicsBuffer[Streams];
    private readonly GraphicsBuffer[] _args = new GraphicsBuffer[Streams];
    private readonly MaterialPropertyBlock[] _properties = new MaterialPropertyBlock[Streams];
    private readonly Plane[] _planes = new Plane[6];
    private readonly Vector4[] _packedPlanes = new Vector4[6];
    private readonly GrassType.Gpu[] _typeData = new GrassType.Gpu[MaxTypes];

    private Texture2D _heightMap;
    private GraphicsBuffer _typeBuffer;
    private GraphicsBuffer _noShapes;           // bound when there is no GrassInteraction: a compute must have a buffer
    private int _kernel;
    private float _cellSize;
    private Vector2 _heightMapUV;
    private Terrain _bakedTerrain;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] _counts;
    private bool _rebuildWanted;

    // What the buffers were sized for. Types are separate assets, so editing one never reaches this
    // component's OnValidate - the field notices the numbers changed and rebuilds instead.
    private float _builtDensity;
    private float _builtHeadShare;

    /// <summary>The ground the grass grows on.</summary>
    public Terrain Terrain => _terrain;

    /// <summary>World height of the ground as a texture, baked on the first render; null before that.</summary>
    public Texture HeightMap => _heightMap;

    /// <summary>Turns a 0..1 position across the terrain into a HeightMap UV: x scale, y offset.</summary>
    public Vector2 HeightMapUV => _heightMapUV;

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        Release();
    }

    private void Build()
    {
        _kernel = _placement.FindKernel("CSPlace");
        BuildMeshes();
        BuildBuffers();
    }

    /// <summary>
    /// Half of this component reads its settings live every frame; the other half turns them into
    /// buffer sizes exactly once. Edit a value and the two disagree - quietly, because nothing on
    /// the GPU checks. So an edit throws the built half away and the next camera builds it again.
    ///
    /// The baked heights are NOT part of that half. They depend on the terrain alone, and on a
    /// 1.6 km terrain they are a 16 MB texture plus twice that in temporary arrays: rebaking them
    /// for every edit meant dragging a slider re-uploaded the whole heightmap on every frame of
    /// the drag.
    ///
    /// Rebuilding here rather than in OnValidate is deliberate: OnValidate can run while Unity is
    /// serialising, where allocating GPU buffers is not allowed.
    /// </summary>
    private void OnValidate() => _rebuildWanted = true;

    /// <summary>
    /// Placing and drawing hangs off the camera event, not off Update, and that is the whole
    /// reason the field is steady in the editor.
    ///
    /// RenderMeshIndirect is an immediate-mode call: it draws in the frame it is issued in and
    /// not one frame longer. Update is not a per-frame callback in edit mode - the editor ticks
    /// the player loop only when it decides something changed, and throttles it to almost
    /// nothing once the window loses focus, while the scene view carries on repainting on its
    /// own. Every repaint that arrived without a tick had no draw call in it: that is the
    /// flicker, and losing focus turned it into the field disappearing outright. Running off the
    /// camera event instead means one dispatch and one draw per camera that is actually about to
    /// render, whatever it was that asked for the render.
    ///
    /// It also means each camera is culled for ITSELF rather than one camera's survivors being
    /// shown to all of them, so the scene view and the game view can look in different
    /// directions and both be right.
    /// </summary>
    private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (_placement == null || _material == null || _terrain == null) return;
        if (_types == null || _types.Length == 0 || _types[0] == null) return;

        // Material thumbnails, asset previews and reflection probes all render through here, and
        // each one would otherwise pay for a full placement pass to fill a picture nobody is
        // looking at grass in.
        if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView) return;

        // A different terrain means the baked heights belong to the wrong ground.
        if (_bakedTerrain != _terrain)
        {
            ReleaseHeightMap();
            BakeHeightMap();
        }

        if (_rebuildWanted || GridDensity() != _builtDensity || MaxHeadShare() != _builtHeadShare)
        {
            _rebuildWanted = false;
            ReleaseBuffers();
        }

        if (_instances[0] == null) Build();
        if (_instances[0] == null) return;

        Dispatch(camera);
        Draw(camera);
    }

    private void Dispatch(Camera camera)
    {
        Vector3 eye = camera.transform.position;
        TerrainData data = _terrain.terrainData;
        Vector3 terrainPosition = _terrain.transform.position;

        GeometryUtility.CalculateFrustumPlanes(camera, _planes);
        for (int i = 0; i < 6; i++)
        {
            _packedPlanes[i] = new Vector4(_planes[i].normal.x, _planes[i].normal.y,
                                           _planes[i].normal.z, _planes[i].distance);
        }

        for (int stream = 0; stream < Streams; stream++) _instances[stream].SetCounterValue(0);

        _placement.SetBuffer(_kernel, "_Lod0", _instances[0]);
        _placement.SetBuffer(_kernel, "_Lod1", _instances[1]);
        _placement.SetBuffer(_kernel, "_Lod2", _instances[2]);
        _placement.SetBuffer(_kernel, "_Flowers", _instances[FlowerStream]);
        _placement.SetTexture(_kernel, "_HeightMap", _heightMap);
        _placement.SetTexture(_kernel, "_PaintMap", _paintMap != null ? (Texture)_paintMap : Texture2D.whiteTexture);
        _placement.SetFloat("_UsePaintMap", _paintMap != null ? 1f : 0f);
        _placement.SetTexture(_kernel, "_TypeMap", _typeMap != null ? (Texture)_typeMap : Texture2D.blackTexture);
        _placement.SetFloat("_UseTypeMap", _typeMap != null ? 1f : 0f);

        int typeCount = UploadTypes();
        _placement.SetBuffer(_kernel, TypesId, _typeBuffer);
        _placement.SetInt(TypeCountId, typeCount);

        // What has touched the grass (GrassInteraction): flattened and cleared ground from its map, and
        // every object that removes grass, asked directly so it works past the map's edge too.
        GrassInteraction touched = GrassInteraction.Current;
        bool hasMap = touched != null && touched.Map != null && touched.ShapeBuffer != null;
        _placement.SetTexture(_kernel, "_InteractionMap", hasMap ? touched.Map : Texture2D.blackTexture);
        _placement.SetVector("_InteractionArea", hasMap ? touched.MapArea : Vector4.zero);
        _placement.SetBuffer(_kernel, "_Shapes", hasMap ? touched.ShapeBuffer : _noShapes);
        _placement.SetInt("_ShapeCount", hasMap ? touched.ShapeCount : 0);
        _placement.SetFloat("_FlattenedHeight", hasMap ? touched.FlattenedHeight : 1f);
        _placement.SetFloat("_TouchableHeight", hasMap ? touched.GrassHeight : 1f);

        _placement.SetVector("_TerrainOrigin", new Vector2(terrainPosition.x, terrainPosition.z));
        _placement.SetVector("_TerrainSize", new Vector2(data.size.x, data.size.z));
        _placement.SetVector("_HeightMapUVScaleOffset", _heightMapUV);
        _placement.SetFloat("_CellSize", _cellSize);
        _placement.SetFloat("_FullDensityDistance", FullDensityDistance);
        _placement.SetVector("_CameraPosition", eye);
        _placement.SetVectorArray("_FrustumPlanes", _packedPlanes);
        _placement.SetVector("_LodDistances", _lodDistances);
        _placement.SetFloat("_MaxSlope", Mathf.Cos(_maxSlope * Mathf.Deg2Rad));
        _placement.SetFloat("_RegionScale", _regionScale);
        _placement.SetFloat("_RegionHeight", _regionHeight);
        _placement.SetFloat("_CullMargin", _cullMargin);
        _placement.SetFloat("_FlowerDistance", _flowerDistance);

        // The blade shader needs the same numbers: where to start painting tips instead of heads, and
        // where each mesh hands over, to morph into the next one before it does.
        Shader.SetGlobalFloat(FlowerDistanceId, _flowerDistance);
        Shader.SetGlobalVector(LodDistancesId, _lodDistances);

        // The blades read their type's colours, and the ground under the grass (MeadowGround.shader)
        // is coloured from the same types, the same painted maps and the same patches, so it has to
        // be handed the same inputs. Compute shaders do not see global properties, which is why
        // these go out twice.
        Shader.SetGlobalBuffer(TypesId, _typeBuffer);
        Shader.SetGlobalInteger(TypeCountId, typeCount);   // SetGlobalInt would write a float
        Shader.SetGlobalFloat(RegionScaleId, _regionScale);
        Shader.SetGlobalTexture(PaintMapId, _paintMap != null ? (Texture)_paintMap : Texture2D.whiteTexture);
        Shader.SetGlobalTexture(TypeMapId, _typeMap != null ? (Texture)_typeMap : Texture2D.blackTexture);
        Shader.SetGlobalVector(PaintAreaId, new Vector4(terrainPosition.x, terrainPosition.z, data.size.x, data.size.z));

        // One dispatch per ring. They all append into the same four buffers, which were emptied
        // above, so the draw calls neither know nor care how many rings there were.
        int rings = RingCount();
        for (int ring = 0; ring < rings; ring++)
            DispatchRing(eye, ring, ring == rings - 1);

        // Byte 4 of the indirect arguments is instanceCount: the GPU tells the draw call how
        // many blades it decided on, and the CPU never finds out.
        for (int stream = 0; stream < Streams; stream++)
            GraphicsBuffer.CopyCount(_instances[stream], _args[stream], sizeof(uint));

        if (_logBladeCount) LogBladeCounts();
    }

    private float FullDensityDistance => Mathf.Clamp(_fullDensityDistance, 1f, _viewDistance);

    private int TypeCount => Mathf.Min(_types.Length, MaxTypes);

    /// <summary>The candidate grid is as dense as the densest type; sparser types keep a share of it.</summary>
    private float GridDensity()
    {
        float density = 0.01f;
        for (int i = 0; i < TypeCount; i++)
            if (_types[i] != null) density = Mathf.Max(density, _types[i].Density);
        return density;
    }

    /// <summary>
    /// The largest share of candidates any one type could turn into drawn heads, if it covered the
    /// whole view. A type as sparse as flowers has a head on every stem and still only a few heads.
    /// </summary>
    private float MaxHeadShare()
    {
        float gridDensity = GridDensity();
        float share = 0f;
        for (int i = 0; i < TypeCount; i++)
            if (_types[i] != null) share = Mathf.Max(share, _types[i].DrawnHeadShare * _types[i].Density / gridDensity);
        return share;
    }

    /// <summary>
    /// Copies every type to the GPU. Every frame, not once: it is a few hundred bytes, and it means a
    /// colour dragged in a type's inspector shows up in the field while you drag.
    /// </summary>
    private int UploadTypes()
    {
        float gridDensity = GridDensity();
        int count = TypeCount;

        for (int i = 0; i < count; i++)
        {
            // An empty slot still has to hold its place, or every type after it would slide down a
            // channel of the type map. It becomes the base grass with no zones of its own.
            GrassType type = _types[i] != null ? _types[i] : _types[0];
            _typeData[i] = type.ToGpu(i, gridDensity);
            if (_types[i] == null) _typeData[i].ZoneThreshold = 2f;
        }

        _typeBuffer.SetData(_typeData, 0, 0, count);
        return count;
    }

    /// <summary>
    /// Rings double in size until the next one would overshoot the view distance by half; the
    /// last ring then stretches to the view distance itself, rather than leaving a thin sliver
    /// of a ring after it with a fade too short to hide.
    /// </summary>
    private int RingCount()
    {
        int count = 1;
        while (FullDensityDistance * (1 << (count - 1)) * 1.5f < _viewDistance) count++;
        return count;
    }

    /// <summary>A ring's band of distances, and how many full-density cells one of its candidates spans on a side.</summary>
    private void GetRing(int ring, bool last, out float inner, out float outer, out int stride)
    {
        stride = 1 << ring;
        inner = ring == 0 ? 0f : FullDensityDistance * (stride / 2);
        outer = last ? _viewDistance : FullDensityDistance * stride;
    }

    private void DispatchRing(Vector3 eye, int ring, bool last)
    {
        GetRing(ring, last, out float inner, out float outer, out int stride);
        float cell = _cellSize * stride;

        // A square of candidates centred on the camera, its corner snapped to this ring's grid so
        // blades stay put while the camera moves. The corner is carried as an integer cell index
        // rather than a world position: the compute keys every blade's randomness on it, and a
        // float that rounds differently from one frame to the next re-rolls the whole field.
        int side = Mathf.CeilToInt(outer * 2f / cell) + 2;

        // If the safety clamp bites, take the missing rows off BOTH sides. Shrinking only the
        // far edge leaves the camera standing near one corner of its own grid, so the field
        // simply stops a few metres in front of you while stretching away behind.
        side = Mathf.Min(side, _maxGridSide);
        int half = side / 2;

        int originCellX = Mathf.FloorToInt(eye.x / cell) - half;
        int originCellZ = Mathf.FloorToInt(eye.z / cell) - half;

        _placement.SetInt("_GridOriginX", originCellX);
        _placement.SetInt("_GridOriginZ", originCellZ);
        _placement.SetInt("_GridSide", side);
        _placement.SetInt("_RingLevel", ring);
        _placement.SetVector("_Ring", new Vector4(inner, outer, last ? 1f : 0f, 0f));

        int groups = Mathf.CeilToInt(side / 8f);
        _placement.Dispatch(_kernel, groups, groups, 1);
    }

    /// <summary>
    /// Read the instance counts back out of the indirect arguments and log them.
    ///
    /// This BLOCKS until the GPU catches up, so it is a debug switch and nothing else: leave it
    /// on while measuring frame time and the number you measure is the stall, not the grass.
    /// </summary>
    private void LogBladeCounts()
    {
        // One element of the buffer's own struct, not five uints: GetData counts in the BUFFER's
        // elements, so asking for five uints' worth of a one-element buffer reads past its end.
        _counts ??= new GraphicsBuffer.IndirectDrawIndexedArgs[1];

        uint total = 0;
        string line = "grass blades ";
        for (int stream = 0; stream < Streams; stream++)
        {
            _args[stream].GetData(_counts);
            uint count = _counts[0].instanceCount;
            if (stream < Lods) total += count;
            line += stream == FlowerStream ? $" flowers={count}" : $" lod{stream}={count}";
        }

        Debug.Log($"{line}  total={total}");
    }

    private void Draw(Camera camera)
    {
        Vector3 eye = camera.transform.position;
        var bounds = new Bounds(eye, new Vector3(_viewDistance * 2f, 10000f, _viewDistance * 2f));

        for (int stream = 0; stream < Streams; stream++)
        {
            bool isFlower = stream == FlowerStream;
            Material material = isFlower ? _flowerMaterial : _material;
            if (material == null) continue;

            var parameters = new RenderParams(material)
            {
                worldBounds = bounds,
                matProps = _properties[stream],
                layer = gameObject.layer,
                receiveShadows = true,

                // Draw ONLY into the camera the compute just culled for. Left unset, an indirect
                // draw goes to every camera in the scene while the instance buffer holds one
                // camera's frustum - so in the editor the game view shows only the grass the
                // scene view happens to be looking at, and a second game camera shows a wedge of
                // field cut to somewhere else entirely.
                camera = camera,
                // Grass moves in the wind while the ground under it stands still, so temporal
                // anti-aliasing needs the blades' own motion or it smears them. Object mode is what
                // makes URP run the shaders' MotionVectors pass for these draws.
                motionVectorMode = MotionVectorGenerationMode.Object,
                // The far LOD does not cast: a shadow map full of sub-pixel blades is pure noise
                // and costs a second full pass over the field for something nobody can see. Seed
                // heads are smaller still, so they do not cast either.
                shadowCastingMode = (stream == 2 || isFlower)
                    ? ShadowCastingMode.Off
                    : ShadowCastingMode.On
            };

            Graphics.RenderMeshIndirect(parameters, _meshes[stream], _args[stream], 1);
        }
    }

    /// <summary>
    /// Terrain heights, in world units, baked into a texture the compute shader can sample.
    ///
    /// Unity's own heightmap texture is normalised in a way that has changed between versions,
    /// so this bakes the answer the shader actually wants and stops worrying about it. Rebaked
    /// whenever the terrain reference changes; sculpting the terrain while the field is live
    /// needs a manual "Rebake height map".
    /// </summary>
    private void BakeHeightMap()
    {
        TerrainData data = _terrain.terrainData;
        int resolution = data.heightmapResolution;

        // Terrain heights are samples at cell CORNERS - sample j sits at j / (resolution - 1)
        // across the terrain - while a texture's texel j sits at (j + 0.5) / resolution. Sampling
        // one with the other's coordinates shifts every blade by half a texel.
        _heightMapUV = new Vector2((resolution - 1f) / resolution, 0.5f / resolution);
        _bakedTerrain = _terrain;
        float[,] heights = data.GetHeights(0, 0, resolution, resolution);
        float baseY = _terrain.transform.position.y;
        float scaleY = data.size.y;

        _heightMap = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
        {
            name = "GrassHeightMap",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        // Plain floats, one per texel, straight into the texture. SetPixels would want a Color
        // per texel - four floats for one channel - and on a 2049x2049 terrain that is 67 MB of
        // garbage every time the field rebuilds.
        var pixels = new float[resolution * resolution];
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
                pixels[z * resolution + x] = baseY + heights[z, x] * scaleY;
        }

        _heightMap.SetPixelData(pixels, 0);
        _heightMap.Apply(false, false);
    }

    private void BuildMeshes()
    {
        // Segment counts, not separate models: four quads up close, one flat card far away.
        _meshes[0] = GrassBladeMesh.Build(4, 1f);
        _meshes[1] = GrassBladeMesh.Build(2, 1f);
        _meshes[2] = GrassBladeMesh.Build(1, 1f);
        _meshes[FlowerStream] = GrassBladeMesh.BuildFlower();
    }

    private void BuildBuffers()
    {
        _builtDensity = GridDensity();
        _builtHeadShare = MaxHeadShare();
        _cellSize = 1f / Mathf.Sqrt(_builtDensity);

        _typeBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTypes, GrassType.Gpu.Stride);
        _noShapes = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, GrassInteractor.Gpu.Stride);

        float lod0 = Mathf.Min(_lodDistances.x, _viewDistance);
        float lod1 = Mathf.Min(_lodDistances.y, _viewDistance);

        // Sized as if the whole field were the densest type and every blade had the most heads any
        // type has. Anything less, and painting that type across the view overflows a buffer.
        //
        // But not for the whole CIRCLE around the camera: only what can be in front of it. Near
        // the camera the cull margin keeps most of the circle, so the nearest band is sized in
        // full; further out a 65 degree view keeps about a fifth of each ring (measured: 21% of
        // the far band), so half is generous and still halves the memory of a dense field.
        float nearShare = 0.75f;
        float farShare = 0.5f;
        int[] capacities =
        {
            Capacity(0f, lod0),
            Mathf.CeilToInt(Capacity(lod0, lod1) * nearShare),
            Mathf.CeilToInt(Capacity(lod1, _viewDistance) * farShare),
            // Heads are a share of the blades inside the distance they are drawn at.
            Mathf.CeilToInt(Capacity(0f, Mathf.Min(_flowerDistance, _viewDistance)) * nearShare * _builtHeadShare) + 1024
        };

        for (int lod = 0; lod < Streams; lod++)
        {
            _instances[lod] = new GraphicsBuffer(
                GraphicsBuffer.Target.Append | GraphicsBuffer.Target.Structured,
                capacities[lod], InstanceStride);

            _args[lod] = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                                            GraphicsBuffer.IndirectDrawIndexedArgs.size);

            var arguments = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
            arguments[0].indexCountPerInstance = _meshes[lod].GetIndexCount(0);
            arguments[0].instanceCount = 0;            // filled in by CopyCount every frame
            arguments[0].startIndex = _meshes[lod].GetIndexStart(0);
            arguments[0].baseVertexIndex = _meshes[lod].GetBaseVertex(0);
            arguments[0].startInstance = 0;
            _args[lod].SetData(arguments);

            _properties[lod] = new MaterialPropertyBlock();
            _properties[lod].SetBuffer(InstancesId, _instances[lod]);

            // Which mesh this draw uses, so the blade shader knows which next-coarser shape to morph
            // towards before its blades switch over.
            _properties[lod].SetFloat(LodId, lod);
        }
    }

    /// <summary>
    /// How many blades can land between two distances. Sizing each buffer to its own band instead
    /// of to the whole field is the difference between 20 MB and 200 MB.
    ///
    /// Walks the placement rings, since each one is four times sparser than the one inside it.
    /// </summary>
    private int Capacity(float from, float to)
    {
        float blades = 0f;
        int rings = RingCount();
        for (int ring = 0; ring < rings; ring++)
        {
            GetRing(ring, ring == rings - 1, out float inner, out float outer, out int stride);
            float a = Mathf.Max(from, inner);
            float b = Mathf.Min(to, outer);
            if (b <= a) continue;

            float cell = _cellSize * stride;
            blades += Mathf.PI * (b * b - a * a) / (cell * cell);
        }

        return Mathf.Clamp(Mathf.CeilToInt(blades * 1.3f) + 1024, 1024, 8_000_000);
    }

    /// <summary>Sculpting the terrain does not tell anyone, so this is the manual way back.</summary>
    [ContextMenu("Rebake height map")]
    private void RebakeHeightMap() => _bakedTerrain = null;

    private void Release()
    {
        ReleaseBuffers();
        ReleaseHeightMap();
    }

    private void ReleaseBuffers()
    {
        _typeBuffer?.Release();
        _typeBuffer = null;
        _noShapes?.Release();
        _noShapes = null;

        for (int lod = 0; lod < Streams; lod++)
        {
            _instances[lod]?.Release();
            _instances[lod] = null;
            _args[lod]?.Release();
            _args[lod] = null;

            if (_meshes[lod] != null)
            {
                if (Application.isPlaying) Destroy(_meshes[lod]);
                else DestroyImmediate(_meshes[lod]);
                _meshes[lod] = null;
            }
        }
    }

    private void ReleaseHeightMap()
    {
        _bakedTerrain = null;

        if (_heightMap != null)
        {
            if (Application.isPlaying) Destroy(_heightMap);
            else DestroyImmediate(_heightMap);
            _heightMap = null;
        }
    }
}
