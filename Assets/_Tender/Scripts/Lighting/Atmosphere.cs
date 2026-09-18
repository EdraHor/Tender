using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The air in the scene: how hazy it is, how the haze thins with height, and how brightly it catches
/// the sun, the sky and the lamps. VolumetricLightFeature draws it; this only decides what it looks like.
///
/// Why it matters more than it sounds: haze is what gives a landscape its depth - far hills paler than
/// near ones - and it is the only thing that can make light itself visible. Shafts of sun where the
/// clouds part, a lantern's glow at dusk: both are light scattered by the air on its way to the eye.
///
/// One per scene. Without one, the feature draws nothing, so scenes that do not want haze pay nothing.
/// </summary>
[ExecuteAlways]
public class Atmosphere : MonoBehaviour
{
    /// <summary>The Atmosphere the renderer feature draws, or null when the scene has none.</summary>
    public static Atmosphere Current { get; private set; }

    /// <summary>Metres of clear air, live: weather (RainZone) closes it in and opens it up again.</summary>
    public float Visibility
    {
        get => _visibility;
        set => _visibility = Mathf.Max(value, 1f);
    }

    [Header("Haze")]
    [Tooltip("How far you can see at the base height before the haze swallows everything, in metres. A clear summer day is 20 000 or more, a hazy one 5 000, mist 500.")]
    [SerializeField] private float _visibility = 8000f;

    [Tooltip("World height where the haze is thickest.")]
    [SerializeField] private float _baseHeight = 0f;

    [Tooltip("Metres of height over which the haze thins to a third. Small keeps it lying in the valleys like mist; large fills the whole sky.")]
    [SerializeField] private float _heightFalloff = 150f;

    [Tooltip("How deep the haze in front of the SKY is, in metres. The skybox already paints the far atmosphere; this should be about where the farthest hills are, so they and the sky beside them match.")]
    [SerializeField] private float _skyDistance = 2000f;

    [Header("Light in the haze")]
    [Tooltip("0: the haze glows the same whichever way you look. Towards 1: it glows mainly when you look towards the light - where sun shafts and lantern halos appear.")]
    [Range(0f, 0.95f)][SerializeField] private float _anisotropy = 0.6f;

    [Tooltip("How much sunlight the haze catches. Looking towards the sun this is multiplied many times over by the anisotropy, so small values go a long way. Without tonemapping the glow round a low sun clips to a white patch with a hard edge - lower this, or turn tonemapping on.")]
    [Range(0f, 4f)][SerializeField] private float _sunStrength = 0.25f;

    [Tooltip("How much sky light the haze catches. This is the blue of distant hills.")]
    [Range(0f, 4f)][SerializeField] private float _skyStrength = 1f;

    [Tooltip("How much point and spot lights light the haze.")]
    [Range(0f, 8f)][SerializeField] private float _lampStrength = 1f;

    [Header("Quality")]
    [Tooltip("Metres along each view ray walked step by step, where shadows and lamps show in the haze. Past it the rest of the air is added in one go - still hazy, but with no shafts. Cloud shadows are hundreds of metres across, so their shafts need this long; steps bunch up near the camera, so lamps close by keep their detail.")]
    [SerializeField] private float _marchDistance = 1500f;

    [Tooltip("Steps along each ray. Fewer is faster and grainier.")]
    [Range(8, 96)][SerializeField] private int _steps = 32;

    [Tooltip("March at a quarter of the screen size instead of half. Much cheaper; soft haze hardly changes, thin shafts blur.")]
    [SerializeField] private bool _quarterResolution;

    private static readonly int DensityId = Shader.PropertyToID("_FogDensity");
    private static readonly int BaseHeightId = Shader.PropertyToID("_FogBaseHeight");
    private static readonly int HeightFalloffId = Shader.PropertyToID("_FogHeightFalloff");
    private static readonly int AnisotropyId = Shader.PropertyToID("_FogAnisotropy");
    private static readonly int AmbientId = Shader.PropertyToID("_FogAmbient");
    private static readonly int SunScatterId = Shader.PropertyToID("_FogSunScatter");
    private static readonly int LocalScatterId = Shader.PropertyToID("_FogLocalScatter");
    private static readonly int MarchDistanceId = Shader.PropertyToID("_FogMarchDistance");
    private static readonly int SkyDistanceId = Shader.PropertyToID("_FogSkyDistance");
    private static readonly int StepsId = Shader.PropertyToID("_FogSteps");

    private readonly Vector3[] _skyDirections = { Vector3.up, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
    private readonly Color[] _skyColours = new Color[5];

    /// <summary>How many times smaller than the screen the haze is marched.</summary>
    public int ResolutionDivisor => _quarterResolution ? 4 : 2;

    private void OnEnable()
    {
        Current = this;
        Apply();
    }

    private void OnDisable()
    {
        if (Current == this) Current = null;
    }

    private void OnValidate() => Apply();
    private void Update() => Apply();

    private void Apply()
    {
        if (!isActiveAndEnabled) return;
        Current = this;

        // Visibility is the friendly way to say density: at 3 / density metres only 5% of the light
        // from a far object still gets through, which is about where the eye loses it.
        Shader.SetGlobalFloat(DensityId, 3f / Mathf.Max(_visibility, 1f));
        Shader.SetGlobalFloat(BaseHeightId, _baseHeight);
        Shader.SetGlobalFloat(HeightFalloffId, _heightFalloff);
        Shader.SetGlobalFloat(AnisotropyId, _anisotropy);
        Shader.SetGlobalFloat(SunScatterId, _sunStrength);
        Shader.SetGlobalFloat(LocalScatterId, _lampStrength);
        Shader.SetGlobalFloat(MarchDistanceId, Mathf.Max(_marchDistance, 1f));
        Shader.SetGlobalFloat(SkyDistanceId, Mathf.Max(_skyDistance, 1f));
        Shader.SetGlobalFloat(StepsId, _steps);

        // The sky light the haze catches is the scene's ambient light - whatever lights the ground
        // from the sky lights the air too, so a time of day or an overcast sky carries straight
        // through. Averaged over overhead and the four horizons.
        RenderSettings.ambientProbe.Evaluate(_skyDirections, _skyColours);
        Color sky = Color.black;
        foreach (Color colour in _skyColours) sky += colour;
        sky *= RenderSettings.ambientIntensity * _skyStrength / _skyColours.Length;
        Shader.SetGlobalVector(AmbientId, new Vector4(sky.r, sky.g, sky.b, 0f));
    }
}
