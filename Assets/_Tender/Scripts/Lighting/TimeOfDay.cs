using UnityEngine;

/// <summary>
/// One slider from midnight to midnight: where the sun stands, what colour its light is, what colour
/// the sky is, and - because Unity lights the world with the sky - how bright the shade is.
///
/// Everything keys off a single number, the SUN'S HEIGHT above the horizon in degrees. Every colour
/// below is a gradient across that height: its left end is deep night (sun 18 degrees below the
/// horizon), a quarter of the way along is the horizon itself - sunrise and sunset - and the right end
/// is the sun high in the sky. So a sunset is not a separate setting; it is just what the gradients
/// hold near the horizon.
///
/// The same directional light is the sun by day and the moon by night. The moon stands opposite the
/// sun, and the light swaps over in the dark stretch of twilight when neither is bright enough to see
/// the jump.
///
/// The sky material is not edited. Its colours are painted over through shader globals
/// (SummerSky.shader, _SkyTimeOfDay), so the asset on disk stays as it is while you scrub the hour.
/// </summary>
[ExecuteAlways]
public class TimeOfDay : MonoBehaviour
{
    [Header("Clock")]
    [Tooltip("The hour, 0 to 24.")]
    [Range(0f, 24f)][SerializeField] private float _hour = 15.5f;

    [Tooltip("Game hours that pass per real minute in Play Mode. 0 stops the clock; 24 is a whole day in a minute.")]
    [SerializeField] private float _hoursPerMinute;

    [Tooltip("In Play Mode, holding these winds the clock back and forward - three hours a second. Handy for looking at the same view at dusk and at night.")]
    [SerializeField] private KeyCode _windBack = KeyCode.LeftBracket;
    [SerializeField] private KeyCode _windForward = KeyCode.RightBracket;

    [Header("Sun path")]
    [Tooltip("The directional light that is the sun by day and the moon by night.")]
    [SerializeField] private Light _light;

    [Range(0f, 24f)][SerializeField] private float _sunrise = 5.5f;
    [Range(0f, 24f)][SerializeField] private float _sunset = 20.5f;

    [Tooltip("How high the sun climbs at noon, in degrees above the horizon.")]
    [Range(5f, 90f)][SerializeField] private float _highestSun = 60f;

    [Tooltip("Which way you look to see the sun at noon, in degrees around Y (0 towards +Z, 90 towards +X). It rises 90 degrees to one side of this and sets 90 to the other.")]
    [Range(0f, 360f)][SerializeField] private float _noonBearing = 290f;

    [Header("Sunlight (across the sun's height: night | horizon | high)")]
    [SerializeField] private Gradient _sunColour = Across(
        (0f, new Color(1f, 0.35f, 0.12f)), (6f, new Color(1f, 0.62f, 0.36f)),
        (15f, new Color(1f, 0.86f, 0.68f)), (30f, new Color(1f, 0.955f, 0.885f)));

    [Tooltip("Sun intensity against its height in degrees.")]
    [SerializeField] private AnimationCurve _sunIntensity = new AnimationCurve(
        new Keyframe(-2f, 0f), new Keyframe(2f, 0.35f), new Keyframe(8f, 0.9f),
        new Keyframe(20f, 1.5f), new Keyframe(40f, 1.85f), new Keyframe(90f, 1.85f));

    [Header("Moonlight")]
    [SerializeField] private Color _moonColour = new Color(0.55f, 0.66f, 1f);
    [Range(0f, 1f)][SerializeField] private float _moonIntensity = 0.12f;

    [Header("Sky (across the sun's height: night | horizon | high)")]
    [SerializeField] private Gradient _zenith = Across(
        (-18f, new Color(0.004f, 0.006f, 0.02f)), (-6f, new Color(0.03f, 0.05f, 0.14f)),
        (0f, new Color(0.1f, 0.18f, 0.38f)), (10f, new Color(0.13f, 0.29f, 0.6f)), (30f, new Color(0.145f, 0.34f, 0.7f)));

    [SerializeField] private Gradient _horizon = Across(
        (-18f, new Color(0.01f, 0.012f, 0.03f)), (-6f, new Color(0.14f, 0.11f, 0.17f)),
        (0f, new Color(0.95f, 0.55f, 0.32f)), (6f, new Color(0.88f, 0.74f, 0.62f)), (20f, new Color(0.72f, 0.83f, 0.9f)));

    [SerializeField] private Gradient _cloudLit = Across(
        (-18f, new Color(0.02f, 0.025f, 0.04f)), (-6f, new Color(0.16f, 0.12f, 0.16f)),
        (0f, new Color(1f, 0.55f, 0.35f)), (6f, new Color(1f, 0.8f, 0.66f)), (20f, new Color(1f, 0.99f, 0.975f)));

    [SerializeField] private Gradient _cloudShade = Across(
        (-18f, new Color(0.01f, 0.012f, 0.02f)), (-6f, new Color(0.06f, 0.06f, 0.09f)),
        (0f, new Color(0.36f, 0.28f, 0.36f)), (10f, new Color(0.5f, 0.53f, 0.62f)), (30f, new Color(0.585f, 0.64f, 0.735f)));

    [Tooltip("The colour below the horizon in daylight. It darkens with the light.")]
    [SerializeField] private Color _dayGround = new Color(0.3f, 0.285f, 0.24f);

    [Header("Ambient")]
    [Tooltip("The shade is lit by the sky, and Unity has to re-read the sky to know its new colour. That costs a few milliseconds, so it is done whenever the hour has moved by this much rather than every frame.")]
    [Range(0.01f, 1f)][SerializeField] private float _ambientStep = 0.1f;

    /// <summary>Gradient time 0 is this far below the horizon, 1 this far above it.</summary>
    private const float NightHeight = -18f;
    private const float HighHeight = 60f;

    private static readonly int ActiveId = Shader.PropertyToID("_SkyTimeOfDay");
    private static readonly int ZenithId = Shader.PropertyToID("_SkyZenithNow");
    private static readonly int HorizonId = Shader.PropertyToID("_SkyHorizonNow");
    private static readonly int GroundId = Shader.PropertyToID("_SkyGroundNow");
    private static readonly int SunColourId = Shader.PropertyToID("_SkySunColorNow");
    private static readonly int CloudLitId = Shader.PropertyToID("_SkyCloudColorNow");
    private static readonly int CloudShadeId = Shader.PropertyToID("_SkyCloudShadeNow");

    private float _ambientHour = float.NaN;

    /// <summary>The hour, 0 to 24.</summary>
    public float Hour
    {
        get => _hour;
        set => _hour = Mathf.Repeat(value, 24f);
    }

    private void OnEnable() => Apply();

    private void OnDisable()
    {
        Shader.SetGlobalFloat(ActiveId, 0f);
        DynamicGI.UpdateEnvironment();
    }

    private void OnValidate() => Apply();

    private void Update()
    {
        if (Application.isPlaying)
        {
            float wind = (Input.GetKey(_windForward) ? 3f : 0f) - (Input.GetKey(_windBack) ? 3f : 0f);
            Hour = _hour + Time.deltaTime * (_hoursPerMinute / 60f + wind);
        }
        Apply();
    }

    private void Apply()
    {
        if (!isActiveAndEnabled || _light == null) return;

        float height = SunHeight(out Vector3 towardsSun);
        float along = Mathf.InverseLerp(NightHeight, HighHeight, height);

        // By day the light is the sun. Once the sun is a couple of degrees down it is too dim to see,
        // and the same light turns round to be the moon, on the opposite side of the sky.
        bool day = height > -2f;
        Vector3 towardsLight = day ? towardsSun : -towardsSun;
        _light.transform.rotation = Quaternion.LookRotation(-towardsLight);

        if (day)
        {
            _light.color = _sunColour.Evaluate(along);
            _light.intensity = Mathf.Max(_sunIntensity.Evaluate(height), 0f);
        }
        else
        {
            _light.color = _moonColour;
            _light.intensity = _moonIntensity * Mathf.InverseLerp(-2f, -12f, height);
        }

        // The sky, painted over the material. The disc SummerSky draws for the sun is drawn wherever
        // the light is, so at night the same disc is the moon, in the moon's colour.
        float daylight = Mathf.InverseLerp(-8f, 20f, height);
        Shader.SetGlobalFloat(ActiveId, 1f);
        Shader.SetGlobalColor(ZenithId, _zenith.Evaluate(along));
        Shader.SetGlobalColor(HorizonId, _horizon.Evaluate(along));
        Shader.SetGlobalColor(GroundId, _dayGround * Mathf.Lerp(0.04f, 1f, daylight));
        Shader.SetGlobalColor(SunColourId, day ? _light.color : _moonColour * 0.5f);
        Shader.SetGlobalColor(CloudLitId, _cloudLit.Evaluate(along));
        Shader.SetGlobalColor(CloudShadeId, _cloudShade.Evaluate(along));

        // The ambient light is built from the sky, so it is out of date the moment the sky changes.
        float moved = Mathf.Abs(Mathf.DeltaAngle(_hour * 15f, _ambientHour * 15f)) / 15f;
        if (float.IsNaN(_ambientHour) || moved >= _ambientStep)
        {
            _ambientHour = _hour;
            DynamicGI.UpdateEnvironment();
        }
    }

    /// <summary>
    /// The sun's height above the horizon in degrees (negative at night), and the direction towards it.
    ///
    /// The day, sunrise to sunset, is the top half of a circle and the night the bottom half, so the sun
    /// climbs to _highestSun at the middle of the day, sinks to the same depth at the middle of the night,
    /// and its bearing swings round a full turn in 24 hours.
    /// </summary>
    private float SunHeight(out Vector3 towardsSun)
    {
        float dayLength = Mathf.Repeat(_sunset - _sunrise, 24f);
        float nightLength = 24f - dayLength;
        float sinceSunrise = Mathf.Repeat(_hour - _sunrise, 24f);

        float turn = sinceSunrise < dayLength
            ? Mathf.PI * sinceSunrise / Mathf.Max(dayLength, 0.01f)
            : Mathf.PI + Mathf.PI * (sinceSunrise - dayLength) / Mathf.Max(nightLength, 0.01f);

        float height = _highestSun * Mathf.Sin(turn);
        float bearing = (_noonBearing + (turn / Mathf.PI - 0.5f) * 180f) * Mathf.Deg2Rad;

        float up = height * Mathf.Deg2Rad;
        towardsSun = new Vector3(Mathf.Sin(bearing) * Mathf.Cos(up), Mathf.Sin(up), Mathf.Cos(bearing) * Mathf.Cos(up));
        return height;
    }

    /// <summary>A gradient with its keys placed at sun heights in degrees rather than at 0..1.</summary>
    private static Gradient Across(params (float height, Color colour)[] keys)
    {
        var colourKeys = new GradientColorKey[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            colourKeys[i] = new GradientColorKey(keys[i].colour, Mathf.InverseLerp(NightHeight, HighHeight, keys[i].height));

        var gradient = new Gradient();
        gradient.SetKeys(colourKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }
}
