using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A patch of short grass, built as a stack of shells.
///
/// It generates ONE mesh that already contains every layer: the same grid of ground triangles
/// repeated <see cref="_shells"/> times, each copy carrying its own height in UV1. The shader
/// lifts each copy along the normal and punches blade-shaped holes in it.
///
/// Because the shells live in the mesh rather than in repeated draw calls, the patch is an
/// ordinary MeshRenderer: it batches, it culls, it casts shadows, and nothing runs per frame on
/// the CPU. The cost is vertices - shells times grid - which is why this is for short grass over
/// small areas, and GrassField handles the meadow.
///
/// The grid is dropped onto the terrain when it is built, so a patch hugs whatever it sits on.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ShellGrassPatch : MonoBehaviour
{
    [Tooltip("Ground to sit on. Empty means the patch stays flat at its own height.")]
    [SerializeField] private Terrain _terrain;

    [Tooltip("Size of the patch in metres.")]
    [SerializeField] private Vector2 _size = new Vector2(16f, 16f);

    [Tooltip("Ground triangles across the patch. Only needs to be fine enough to follow the terrain.")]
    [Range(2, 128)][SerializeField] private int _resolution = 24;

    [Tooltip("How many layers. This is the cost of the patch - every layer is another full pass of overdraw - and it is also what decides whether TALL grass is possible at all. The shells have to sit closer together than the blades are wide, or you see straight between them: roughly, shells = 3 x height x blades-per-metre. Ankle-high grass needs about 16 and knee-high about 48, which is why going taller means going sparser.")]
    [Range(2, 64)][SerializeField] private int _shells = 16;

    [Tooltip("How far the patch sits above the ground, in metres. A couple of centimetres keeps the terrain from poking through between the patch's own vertices.")]
    [Range(0f, 0.2f)][SerializeField] private float _lift = 0.02f;

    [Header("Grass Blades")]
    [Tooltip("Grass blade height in metres. Shell grass is cheap at ankle height and expensive at knee height, because the shell count has to rise with it - read the Shells tooltip before going tall. Past roughly half a metre the tall grass system (GrassField) is the better answer.")]
    [Range(0.01f, 0.6f)][SerializeField] private float _bladeHeight = 0.09f;

    [Tooltip("Blade width / thickness multiplier.")]
    [Range(0.1f, 3f)][SerializeField] private float _bladeWidth = 1.2f;

    [Tooltip("Blade density (blades per metre).")]
    [Range(5f, 150f)][SerializeField] private float _density = 45f;

    [Header("Colour")]
    [Tooltip("Take the colours of the meadow around it - GrassLook's ground colour and the GrassField's base grass type - so the patch reads as the same field cut shorter. The colours below are then ignored. Needs a GrassLook and a GrassField in the scene.")]
    [SerializeField] private bool _matchMeadow;

    [Tooltip("Colour at the root, where the blade meets the soil.")]
    [SerializeField] private Color _rootColor = new Color(0.09f, 0.17f, 0.05f);

    [Tooltip("Colour at the tip.")]
    [SerializeField] private Color _tipColor = new Color(0.38f, 0.62f, 0.18f);

    [Tooltip("Share of blades that have dried out to straw. Also tints the thin patches, which is the variation distant grass has left.")]
    [Range(0f, 1f)][SerializeField] private float _dryShare = 0.2f;

    [Header("Clumping")]
    [Tooltip("Tufts per metre. 3-4 gives grass growing in clumps the size of a hand; high values read as a busy speckle.")]
    [Range(0.5f, 12f)][SerializeField] private float _clumpScale = 3.5f;

    [Tooltip("How much a tuft changes the height around it. This is the structure that survives to any distance, so it is also what keeps far grass from reading as a flat sheet. Note that it only ever shortens, so raising it lowers the average height of the patch.")]
    [Range(0f, 1f)][SerializeField] private float _clumping = 0.5f;

    [Header("Shape & Motion")]
    [Tooltip("Shortest blade, as a fraction of the full height. Lower gives a raggeder, uneven-cut look; 1 makes every blade the same height.")]
    [Range(0f, 1f)][SerializeField] private float _minBlade = 0.35f;

    [Tooltip("How far a blade leans when there is no wind at all, measured at the tip in blade spacings. Zero makes every blade stand dead straight, which is the loudest tell that grass is a shader.")]
    [Range(0f, 1f)][SerializeField] private float _lean = 0.5f;

    [Tooltip("How far the tips sway in the wind, in metres. Shares the same wind field the tall grass uses, so the two stay in step.")]
    [Range(0f, 0.5f)][SerializeField] private float _sway = 0.05f;

    [Tooltip("How much blades fatten up when looked at edge-on, which is what closes the seams between the shells. Raise it if tall grass separates into visible layers from a low camera; drop it to zero to see what the stack really looks like.")]
    [Range(0f, 3f)][SerializeField] private float _grazeFill = 1f;

    [Tooltip("Samples each layer at a random height inside the gap below it, so the ordered steps along blade edges - the fern-like ribbing - dissolve into fine grain. Zero shows the raw stack, for comparison.")]
    [Range(0f, 1f)][SerializeField] private float _slabJitter = 1f;

    [Header("Detail limit")]
    [Tooltip("Blades start merging into each other once one screen pixel covers this many blade cells. Raise it to keep separate blades visible further away; lower it if distant grass shimmers.")]
    [Range(0.1f, 4f)][SerializeField] private float _detailFrom = 1.2f;

    [Tooltip("Past this many cells per pixel the grass is a solid carpet at full height. The gap between the two values is how long the blend takes.")]
    [Range(0.2f, 8f)][SerializeField] private float _detailTo = 4.5f;

    private Mesh _mesh;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _propBlock;

    private static readonly int ShellHeightProp = Shader.PropertyToID("_ShellHeight");
    private static readonly int ThicknessProp = Shader.PropertyToID("_Thickness");
    private static readonly int BladesPerMetreProp = Shader.PropertyToID("_BladesPerMetre");
    private static readonly int MatchMeadowProp = Shader.PropertyToID("_MatchMeadow");
    private static readonly int RootColorProp = Shader.PropertyToID("_RootColor");
    private static readonly int TipColorProp = Shader.PropertyToID("_TipColor");
    private static readonly int MinBladeProp = Shader.PropertyToID("_MinBlade");
    private static readonly int SwayProp = Shader.PropertyToID("_Sway");
    private static readonly int DetailFromProp = Shader.PropertyToID("_DetailFrom");
    private static readonly int DetailToProp = Shader.PropertyToID("_DetailTo");
    private static readonly int DryShareProp = Shader.PropertyToID("_DryShare");
    private static readonly int ClumpScaleProp = Shader.PropertyToID("_ClumpScale");
    private static readonly int ClumpingProp = Shader.PropertyToID("_Clumping");
    private static readonly int LeanProp = Shader.PropertyToID("_Lean");
    private static readonly int GrazeFillProp = Shader.PropertyToID("_GrazeFill");
    private static readonly int SlabJitterProp = Shader.PropertyToID("_SlabJitter");
    private static readonly int ShellStepProp = Shader.PropertyToID("_ShellStep");
    private static readonly int ShellFrameProp = Shader.PropertyToID("_ShellFrame");

    /// <summary>One layer in this many casts a shadow; see the ShadowCaster pass for why.</summary>
    private const int ShadowStride = 3;

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += SetNoiseFrame;
        ApplyProperties();
        Rebuild();
    }

    private void OnDisable() => RenderPipelineManager.beginCameraRendering -= SetNoiseFrame;

    /// <summary>
    /// The slab jitter's noise moves on every frame only for a camera that averages frames.
    ///
    /// Under temporal anti-aliasing a new pattern each frame is exactly what makes the grain
    /// average away into solid blades. Without it, the same moving pattern just boils - so every
    /// other camera, the scene view included, is handed frame zero forever and sees a still grain.
    /// Every patch sets the same global; that is harmless, and simpler than sharing one setter.
    /// </summary>
    private void SetNoiseFrame(ScriptableRenderContext context, Camera camera)
    {
        bool temporal = camera.TryGetComponent(out UniversalAdditionalCameraData data)
                        && data.antialiasing == AntialiasingMode.TemporalAntiAliasing;
        Shader.SetGlobalFloat(ShellFrameProp, temporal ? Time.frameCount % 16 : 0);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplyProperties();
        // OnValidate can fire while Unity is serialising, and building a mesh is not allowed
        // there - so the rebuild is pushed to the next editor tick.
        if (isActiveAndEnabled) UnityEditor.EditorApplication.delayCall += SafeRebuild;
    }

    private void SafeRebuild()
    {
        if (this == null || !isActiveAndEnabled) return;
        Rebuild();
    }
#endif

    public void ApplyProperties()
    {
        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) return;

        if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
        _renderer.GetPropertyBlock(_propBlock);
        _propBlock.SetFloat(ShellHeightProp, _bladeHeight);
        _propBlock.SetFloat(ThicknessProp, _bladeWidth);
        _propBlock.SetFloat(BladesPerMetreProp, _density);
        _propBlock.SetFloat(MatchMeadowProp, _matchMeadow ? 1f : 0f);
        _propBlock.SetColor(RootColorProp, _rootColor);
        _propBlock.SetColor(TipColorProp, _tipColor);
        _propBlock.SetFloat(MinBladeProp, _minBlade);
        _propBlock.SetFloat(SwayProp, _sway);
        _propBlock.SetFloat(DryShareProp, _dryShare);
        _propBlock.SetFloat(ClumpScaleProp, _clumpScale);
        _propBlock.SetFloat(ClumpingProp, _clumping);
        _propBlock.SetFloat(LeanProp, _lean);
        _propBlock.SetFloat(GrazeFillProp, _grazeFill);
        _propBlock.SetFloat(SlabJitterProp, _slabJitter);
        _propBlock.SetFloat(ShellStepProp, 1f / Mathf.Max(_shells - 1, 1));
        _propBlock.SetFloat(DetailFromProp, _detailFrom);

        // Set below the value above, the blend has nowhere to happen: the shader clamps the
        // divide and blades snap to carpet within a pixel, drawing a hard ring on the ground.
        _propBlock.SetFloat(DetailToProp, Mathf.Max(_detailTo, _detailFrom + 0.05f));
        _renderer.SetPropertyBlock(_propBlock);
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        int side = _resolution + 1;
        int perShell = side * side;

        var vertices = new List<Vector3>(perShell * _shells);
        var normals = new List<Vector3>(perShell * _shells);
        var uvs = new List<Vector2>(perShell * _shells);
        var shellHeights = new List<Vector2>(perShell * _shells);
        var triangles = new List<int>(_resolution * _resolution * 6 * _shells);

        // The ground layer is built once and reused for every shell: only its height in UV1
        // changes, and the shader does the lifting.
        var groundPositions = new Vector3[perShell];
        var groundNormals = new Vector3[perShell];

        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                float u = x / (float)_resolution;
                float v = z / (float)_resolution;

                var local = new Vector3((u - 0.5f) * _size.x, 0f, (v - 0.5f) * _size.y);
                Vector3 world = transform.TransformPoint(local);
                Vector3 normal = Vector3.up;

                if (_terrain != null)
                {
                    world.y = _terrain.SampleHeight(world) + _terrain.transform.position.y;
                    normal = _terrain.terrainData.GetInterpolatedNormal(
                        Mathf.Clamp01((world.x - _terrain.transform.position.x) / _terrain.terrainData.size.x),
                        Mathf.Clamp01((world.z - _terrain.transform.position.z) / _terrain.terrainData.size.z));
                }

                int index = z * side + x;
                groundPositions[index] = transform.InverseTransformPoint(world + normal * _lift);
                groundNormals[index] = transform.InverseTransformDirection(normal);
            }
        }

        // Built from the TOP layer down, so the mesh's triangles are already in roughly
        // front-to-back order when you are looking at the patch from above. The shader discards,
        // so the depth buffer cannot be filled early - but the depth TEST still rejects, and
        // ordering the stack this way lets it. Ground-first is the worst possible order: every
        // layer above the soil is nearer than what is already drawn, so nothing is ever rejected
        // and all sixteen shells shade every pixel. The picture is identical either way.
        for (int layer = 0; layer < _shells; layer++)
        {
            int shell = _shells - 1 - layer;
            float shellT = _shells == 1 ? 0f : shell / (float)(_shells - 1);
            float castsShadow = layer % ShadowStride == 0 ? 1f : 0f;     // layer 0 is the top
            int offset = layer * perShell;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int index = z * side + x;
                    vertices.Add(groundPositions[index]);
                    normals.Add(groundNormals[index]);
                    uvs.Add(new Vector2(x / (float)_resolution, z / (float)_resolution));
                    shellHeights.Add(new Vector2(shellT, castsShadow));
                }
            }

            for (int z = 0; z < _resolution; z++)
            {
                for (int x = 0; x < _resolution; x++)
                {
                    int a = offset + z * side + x;
                    int b = a + 1;
                    int c = a + side;
                    int d = c + 1;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        if (_mesh == null)
        {
            // Adopt whatever is already on the filter before allocating anything.
            //
            // _mesh is a plain field, so a script recompile wipes it - but the native Mesh it
            // pointed at survives the reload and is still hanging off the MeshFilter. Allocating
            // a fresh one here would strand that mesh, and HideAndDontSave means it is exempt
            // from UnloadUnusedAssets, so it would stay resident for the whole session. Half an
            // hour of shader iteration leaves a pile of them.
            Mesh existing = GetComponent<MeshFilter>().sharedMesh;
            _mesh = existing != null && existing.hideFlags == HideFlags.HideAndDontSave
                ? existing
                : new Mesh { name = "ShellGrassPatch", hideFlags = HideFlags.HideAndDontSave };
        }

        _mesh.Clear();
        _mesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        _mesh.SetVertices(vertices);
        _mesh.SetNormals(normals);
        _mesh.SetUVs(0, uvs);
        _mesh.SetUVs(1, shellHeights);
        _mesh.SetTriangles(triangles, 0);
        _mesh.RecalculateBounds();

        // The shader lifts the top shell off the surface, so the bounds have to allow for it or
        // the patch pops out of view when only its grass is on screen.
        Bounds bounds = _mesh.bounds;
        bounds.Expand(new Vector3(0f, 1f, 0f));
        _mesh.bounds = bounds;

        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    /// <summary>The generated mesh is nobody else's, so this component has to clean it up.</summary>
    private void OnDestroy()
    {
        if (_mesh == null) return;

        if (Application.isPlaying) Destroy(_mesh);
        else DestroyImmediate(_mesh);
        _mesh = null;
    }
}
