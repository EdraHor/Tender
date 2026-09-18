using UnityEngine;

/// <summary>
/// One kind of grass: how tall, how thick on the ground, how it clumps, what colour it is, whether
/// it carries seed heads - and where it grows.
///
/// A GrassField holds a short list of these. The first one is the grass that grows everywhere; each
/// one after it grows only inside its own zones and wins over the ones before it. Every blade picks
/// its type on the GPU as it is placed, so a field full of different grasses is still one field,
/// one compute pass and the same few draw calls.
///
/// Colours and seed heads can be edited while the field is running. Density and seed-head share
/// decide buffer sizes, so changing those rebuilds the field on the next frame.
/// </summary>
[CreateAssetMenu(menuName = "Tender/Grass Type", fileName = "GrassType")]
public class GrassType : ScriptableObject
{
    /// <summary>What sits on top of a stem. The numbers are what the shaders compare against.</summary>
    public enum HeadStyle
    {
        /// <summary>A little ball of seeds, drawn as a disc turned to the camera. Meadow grass.</summary>
        SeedHead = 0,
        /// <summary>The top of the blade itself grows wide and takes the head colour. Wheat ears, pampas plumes.</summary>
        Ear = 1,
        /// <summary>A flower: a ring of petals facing the sky, with a centre of its own colour.</summary>
        Bloom = 2
    }

    [Header("Blade")]
    [Tooltip("Blade height in metres.")]
    [SerializeField] private float _height = 0.85f;

    [Tooltip("How much blades differ in height. 0.3 spreads them over +/- 30%.")]
    [Range(0f, 0.6f)][SerializeField] private float _heightVariation = 0.3f;

    [Tooltip("Blade width at the root, in metres.")]
    [SerializeField] private float _width = 0.045f;

    [Tooltip("Blades per square metre close to the camera.")]
    [SerializeField] private float _density = 55f;

    [Tooltip("How much the wind moves it. 1 is ordinary grass; stiff stems like wheat sit lower.")]
    [Range(0f, 2f)][SerializeField] private float _windResponse = 1f;

    [Header("Clumps")]
    [Tooltip("How far across one tuft is, in metres.")]
    [SerializeField] private float _clumpSize = 0.7f;

    [Tooltip("How far roots are drawn in to their tuft's centre. 0 is an even lawn; 0.35 already reads as separate little bushes.")]
    [Range(0f, 0.9f)][SerializeField] private float _clumpPull = 0.12f;

    [Tooltip("How far the outermost blades of a tuft lean out, in radians.")]
    [Range(0f, 1.2f)][SerializeField] private float _clumpSplay = 0.35f;

    [Tooltip("How much tufts differ in height from each other.")]
    [Range(0f, 1f)][SerializeField] private float _clumpHeight = 0.5f;

    [Header("Colour")]
    [Tooltip("Tips of healthy grass. Keep it saturated and darker than looks right in the picker: the sun is added on top, and a pale albedo burns out to yellow-white.")]
    [SerializeField] private Color _tipColor = new Color(0.36f, 0.56f, 0.07f);

    [Tooltip("Tips where the meadow's colour drifts cool (GrassLook decides how big and how strong the drifts are).")]
    [SerializeField] private Color _coolTipColor = new Color(0.16f, 0.42f, 0.14f);

    [Tooltip("Tips of the poorest, driest patches.")]
    [SerializeField] private Color _dryTipColor = new Color(0.58f, 0.52f, 0.16f);

    [Header("Heads")]
    [SerializeField] private HeadStyle _headStyle = HeadStyle.SeedHead;

    [Tooltip("Share of blades in the best grass that carry a head. 0 means none at all; wheat and flowers want 1.")]
    [Range(0f, 1f)][SerializeField] private float _headShare;

    [Tooltip("Seed heads and ears: the whole head. Blooms: the petals.")]
    [SerializeField] private Color _headColor = new Color(0.94f, 0.95f, 0.88f);

    [Tooltip("Blooms only: the middle of the flower.")]
    [SerializeField] private Color _centerColor = new Color(0.95f, 0.75f, 0.15f);

    [Tooltip("Seed heads and blooms: size in metres.")]
    [SerializeField] private float _headSize = 0.022f;

    [Tooltip("How far up the blade the head begins. For ears this is where the stem starts to widen; for the others, where the head is painted onto the blade once it is too far away to draw.")]
    [Range(0.4f, 1f)][SerializeField] private float _headStart = 0.82f;

    [Tooltip("Ears only: how many times wider than the stem the ear or plume grows.")]
    [Range(1f, 8f)][SerializeField] private float _headWidth = 2.5f;

    [Tooltip("How much more the heads catch the light - sheen, glow against the sun and the brightening of a passing gust. 1 is ordinary grass; ripe wheat and pampas plumes shine.")]
    [Range(1f, 4f)][SerializeField] private float _headShine = 1f;

    [Header("Where it grows (ignored for the first type in a field)")]
    [Tooltip("Grow BETWEEN the grass below instead of replacing it. For flowers: a poppy patch is poppies scattered through the meadow, not a hole in the meadow with poppies in it. The density is then how many of these per square metre, on top of the grass.")]
    [SerializeField] private bool _overlay;

    [Tooltip("Roughly what share of the ground this type covers on its own, before anything is painted. 0 means it grows only where it is painted.")]
    [Range(0f, 0.6f)][SerializeField] private float _coverage = 0.05f;

    [Tooltip("Roughly how far across one of its patches is, in metres.")]
    [SerializeField] private float _patchSize = 20f;

    [Tooltip("How wide the band is where it mixes with the grass around it, in metres. Inside that band blades of both kinds grow side by side and this one gets shorter towards the edge, so a zone never ends in a wall.")]
    [SerializeField] private float _edgeWidth = 3f;

    public float Density => Mathf.Max(_density, 0.01f);

    /// <summary>
    /// Share of blades that put a head into the field's separate head stream. Ears are part of the
    /// blade itself, so they never do.
    /// </summary>
    public float DrawnHeadShare => _headStyle == HeadStyle.Ear ? 0f : _headShare;

    /// <summary>
    /// What the GPU gets for one type. The layout has to match struct GrassType in GrassTypes.hlsl
    /// exactly - same order, all floats - because the buffer is copied across byte for byte.
    /// </summary>
    public struct Gpu
    {
        public float Height, HeightVariation, Width, DensityShare;
        public float ClumpSize, ClumpPull, ClumpSplay, ClumpHeight;
        public float ZoneScale, ZoneThreshold, ZoneSoftness, HeadShare;
        public float HeadSize, HeadStart, WindResponse, ZoneSeed;
        public Vector4 TipColor, CoolTipColor, DryTipColor, HeadColor;
        public float HeadStyle, HeadWidth, HeadShine, Overlay;
        public Vector4 CenterColor;

        public const int Stride = 40 * sizeof(float);
    }

    /// <param name="index">Where this type sits in the field's list; it seeds the zone noise.</param>
    /// <param name="gridDensity">The field's candidate density - the densest type's.</param>
    public Gpu ToGpu(int index, float gridDensity)
    {
        float patch = Mathf.Max(_patchSize, 1f);

        return new Gpu
        {
            Height = Mathf.Max(_height, 0.01f),
            HeightVariation = _heightVariation,
            Width = Mathf.Max(_width, 0.001f),
            DensityShare = Mathf.Clamp01(Density / gridDensity),
            ClumpSize = Mathf.Max(_clumpSize, 0.05f),
            ClumpPull = _clumpPull,
            ClumpSplay = _clumpSplay,
            ClumpHeight = _clumpHeight,

            // Zone noise has one bump per noise cell, and a bump that pokes above the threshold is
            // a good deal smaller than its cell - so the cell is taken as twice the patch size.
            ZoneScale = 1f / (patch * 2f),
            ZoneThreshold = _coverage > 0f ? GrassZoneNoise.ThresholdFor(_coverage) : 2f,
            // Measured on the zone noise: at its 5% threshold it climbs by about 0.75 units per cell,
            // so a band this many metres wide is this much noise. The band is centred on the
            // threshold, which keeps the coverage right however soft the edge is.
            ZoneSoftness = Mathf.Max(_edgeWidth / (patch * 2f) * 0.75f, 0.005f),
            HeadShare = _headShare,

            HeadSize = Mathf.Max(_headSize, 0.001f),
            HeadStart = _headStart,
            WindResponse = _windResponse,
            ZoneSeed = index * 37.7f,

            // Buffers are raw data: nothing converts these for a linear-space project the way a
            // material colour or SetGlobalColor would, so it has to happen here.
            TipColor = _tipColor.linear,
            CoolTipColor = _coolTipColor.linear,
            DryTipColor = _dryTipColor.linear,
            HeadColor = _headColor.linear,

            HeadStyle = (float)_headStyle,
            HeadWidth = _headWidth,
            HeadShine = _headShine,
            Overlay = _overlay ? 1f : 0f,
            CenterColor = _centerColor.linear
        };
    }
}
