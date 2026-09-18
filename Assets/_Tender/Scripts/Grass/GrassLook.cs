using UnityEngine;

/// <summary>
/// One place that decides what the meadow looks like: the blades, and the ground under them.
///
/// Like GrassWind, it writes global shader properties instead of material ones. The blades
/// (GrassBlade.shader) and the terrain (MeadowGround.shader) are different shaders on different
/// objects, and far away the ground has to pass for grass. The moment they read their colours from
/// two materials, somebody tweaks one and not the other, and the line where the blades end shows
/// up on every hill.
///
/// The tip colours of each kind of grass live on its GrassType asset. What is here is what all of
/// them share: the ground they grow from, how the colour drifts across the land, and the light.
///
/// Drop one of these anywhere in the scene. There is no reason to have two.
/// </summary>
[ExecuteAlways]
public class GrassLook : MonoBehaviour
{
    [Header("Colour")]
    [Tooltip("The roots, and the ground between the blades. The darkest, most saturated green.")]
    [SerializeField] private Color _groundColor = new Color(0.07f, 0.17f, 0.03f);

    [Tooltip("How far across one drift of warmer or cooler green is, in metres. Each grass type says which two colours it drifts between.")]
    [SerializeField] private float _driftSize = 70f;

    [Tooltip("How far the drifts go towards the cool colour.")]
    [Range(0f, 1f)][SerializeField] private float _driftAmount = 0.6f;

    [Tooltip("How far the poor patches go towards the dry colour.")]
    [Range(0f, 1f)][SerializeField] private float _dryAmount = 0.6f;

    [Tooltip("How dark it gets down between the stems.")]
    [Range(0f, 1f)][SerializeField] private float _rootShade = 0.55f;

    [Tooltip("Short grass painted on the ground between the stems, close up. Without it, looking down shows the gaps as bald patches.")]
    [Range(0f, 1f)][SerializeField] private float _understorey = 0.6f;

    [Header("Light")]
    [Tooltip("Light wrapping round the thin blades. 0 is a hard edge between lit and shaded.")]
    [Range(0f, 1f)][SerializeField] private float _wrap = 0.4f;

    [Tooltip("Glow through the grass when looking towards the sun.")]
    [Range(0f, 2f)][SerializeField] private float _translucency = 0.6f;

    [Tooltip("Soft highlight on the tips, strongest against the light. It rides the gusts, so it runs across the field in waves.")]
    [Range(0f, 1f)][SerializeField] private float _sheen = 0.15f;

    [Tooltip("How much paler the grass turns where a wave presses it over. This is what shows the waves from far away.")]
    [Range(0f, 1f)][SerializeField] private float _gustBrighten = 0.8f;

    [Header("Distance")]
    [Tooltip("By this distance, in metres, single blades and the ground have blended into one carpet of colour. The far ground uses the same carpet, so where the blades end cannot be seen.")]
    [SerializeField] private float _carpetDistance = 150f;

    private static readonly int GroundColorId = Shader.PropertyToID("_GrassGroundColor");
    private static readonly int HueScaleId = Shader.PropertyToID("_GrassHueScale");
    private static readonly int HueAmountId = Shader.PropertyToID("_GrassHueAmount");
    private static readonly int DryAmountId = Shader.PropertyToID("_GrassDryAmount");
    private static readonly int RootShadeId = Shader.PropertyToID("_GrassRootShade");
    private static readonly int UnderstoreyId = Shader.PropertyToID("_GrassUnderstorey");
    private static readonly int WrapId = Shader.PropertyToID("_GrassWrap");
    private static readonly int TranslucencyId = Shader.PropertyToID("_GrassTranslucency");
    private static readonly int SheenId = Shader.PropertyToID("_GrassSheen");
    private static readonly int GustBrightenId = Shader.PropertyToID("_GrassGustBrighten");
    private static readonly int CarpetDistanceId = Shader.PropertyToID("_GrassCarpetDistance");

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();
    private void Update() => Apply();     // a dozen SetGlobal calls; cheap enough to just keep live

    private void Apply()
    {
        // SetGlobalColor converts to linear for a linear-space project, just as a material's
        // colour property would, so these can be picked by eye in the inspector.
        Shader.SetGlobalColor(GroundColorId, _groundColor);
        Shader.SetGlobalFloat(HueScaleId, 1f / Mathf.Max(_driftSize, 0.01f));
        Shader.SetGlobalFloat(HueAmountId, _driftAmount);
        Shader.SetGlobalFloat(DryAmountId, _dryAmount);
        Shader.SetGlobalFloat(RootShadeId, _rootShade);
        Shader.SetGlobalFloat(UnderstoreyId, _understorey);

        Shader.SetGlobalFloat(WrapId, _wrap);
        Shader.SetGlobalFloat(TranslucencyId, _translucency);
        Shader.SetGlobalFloat(SheenId, _sheen);
        Shader.SetGlobalFloat(GustBrightenId, _gustBrighten);
        Shader.SetGlobalFloat(CarpetDistanceId, Mathf.Max(_carpetDistance, 1f));
    }
}
