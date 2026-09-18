using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shadows of the sky's clouds, drifting across the ground.
///
/// The sun gets a light COOKIE - a picture it shines through - and URP darkens every lit surface by
/// it. So cloud shadows land on the grass, the terrain and anything else lit by the sun without a
/// single shader knowing about them.
///
/// The shadows are not a pattern of their own. CloudShadows.compute follows each sunbeam up to the
/// sky's cloud layer and reads it with the same function SummerSky.shader draws it with
/// (CloudLayer.hlsl), so every shadow belongs to a cloud you can see and moves exactly as it does.
/// Cloud size, cover, height and wind are therefore set on the sky material, not here.
///
/// The map covers a square around the camera, redrawn every frame.
///
/// URP frames a directional cookie with UniversalAdditionalLightData.lightCookieSize and
/// lightCookieOffset, in metres of the LIGHT's own space - not Light.cookieSize, which it ignores.
/// The map is centred on the offset and is `size` metres across.
///
/// Put it on the directional light that is the sun.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class CloudShadows : MonoBehaviour
{
    [Tooltip("The sky whose clouds cast the shadows. Empty means the scene's skybox (Lighting → Environment).")]
    [SerializeField] private Material _sky;

    [Tooltip("How much sunlight a thick cloud takes away. The sky still lights the shade, so even 1 is not black.")]
    [Range(0f, 1f)][SerializeField] private float _darkness = 0.7f;

    [Tooltip("How solid a cloud's shadow is compared with how the cloud looks. The sky paints most clouds half see-through, but even a pale one stops most of the sun. 1 = the shadow is exactly as faint as the cloud looks.")]
    [Range(1f, 8f)][SerializeField] private float _thickness = 3f;

    [Tooltip("Metres across the square of ground around the camera that gets cloud shadows. They fade out towards its edge.")]
    [SerializeField] private float _area = 3000f;

    [Header("Plumbing")]
    [SerializeField] private ComputeShader _compute;

    [Tooltip("Pixels along each side of the shadow map. 1024 over 3 km is 3 m a pixel - plenty for something as soft as a cloud.")]
    [SerializeField] private int _resolution = 1024;

    private static readonly int CookieId = Shader.PropertyToID("_Cookie");
    private static readonly int ResolutionId = Shader.PropertyToID("_Resolution");
    private static readonly int CornerId = Shader.PropertyToID("_CornerWS");
    private static readonly int AcrossId = Shader.PropertyToID("_AcrossWS");
    private static readonly int UpId = Shader.PropertyToID("_UpWS");
    private static readonly int SunTravelId = Shader.PropertyToID("_SunTravel");
    private static readonly int DriftId = Shader.PropertyToID("_Drift");
    private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
    private static readonly int DarknessId = Shader.PropertyToID("_Darkness");

    // Read off the sky material by the same names SummerSky.shader uses.
    private static readonly int CloudHeightId = Shader.PropertyToID("_CloudHeight");
    private static readonly int CloudScaleId = Shader.PropertyToID("_CloudScale");
    private static readonly int CoverageId = Shader.PropertyToID("_Coverage");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int WindSpeedId = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindAngleId = Shader.PropertyToID("_WindAngle");

    private Light _light;
    private RenderTexture _cookie;
    private int _kernel;

    private void OnEnable()
    {
        _light = GetComponent<Light>();
    }

    private void OnDisable()
    {
        if (_light != null && _light.cookie == _cookie) _light.cookie = null;
        ReleaseCookie();
    }

    private void Update()
    {
        Material sky = _sky != null ? _sky : RenderSettings.skybox;
        if (_compute == null || _light == null || sky == null || !sky.HasProperty(CloudScaleId)) return;

        // With the sun at or below the horizon its beams never reach the clouds.
        if (transform.forward.y > -0.01f)
        {
            if (_light.cookie == _cookie) _light.cookie = null;
            return;
        }

        // A different map size needs a new render texture; everything else is read live. Checked here
        // rather than in OnValidate, where destroying textures is not allowed.
        if (_cookie != null && _cookie.width != _resolution) ReleaseCookie();
        if (_cookie == null) BuildCookie();

        Vector2 centre = PlaceMap();
        Paint(sky, centre);
    }

    /// <summary>
    /// Centre the map on the camera, in the light's own X and Y, and hand that to URP.
    ///
    /// The centre is snapped to whole pixels, so as the camera walks the pixels stay over the same
    /// patches of ground instead of sliding across them and shimmering.
    /// </summary>
    private Vector2 PlaceMap()
    {
        Camera view = Camera.main;
        Vector3 focus = view != null ? view.transform.position : transform.position;
        Vector3 fromLight = focus - transform.position;

        float pixel = _area / _resolution;
        var centre = new Vector2(
            Mathf.Round(Vector3.Dot(fromLight, transform.right) / pixel) * pixel,
            Mathf.Round(Vector3.Dot(fromLight, transform.up) / pixel) * pixel);

        UniversalAdditionalLightData data = _light.GetUniversalAdditionalLightData();
        data.lightCookieSize = new Vector2(_area, _area);
        data.lightCookieOffset = centre;
        _light.cookie = _cookie;
        return centre;
    }

    /// <summary>Redraw the shadow map on the GPU for this moment.</summary>
    private void Paint(Material sky, Vector2 centre)
    {
        // The corner of the map and its two edges, as points and vectors in the world.
        Vector3 corner = transform.position
                       + transform.right * (centre.x - _area * 0.5f)
                       + transform.up * (centre.y - _area * 0.5f);

        // The clock URP gives shaders as _Time.y: Time.time in Play Mode, real time in the editor.
        // The sky moves its clouds by that clock, so the shadows must too.
        float time = Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
        float heading = sky.GetFloat(WindAngleId) * Mathf.Deg2Rad;
        Vector2 drift = new Vector2(Mathf.Sin(heading), Mathf.Cos(heading)) * sky.GetFloat(WindSpeedId) * time;

        _compute.SetTexture(_kernel, CookieId, _cookie);
        _compute.SetFloat(ResolutionId, _resolution);
        _compute.SetVector(CornerId, corner);
        _compute.SetVector(AcrossId, transform.right * _area);
        _compute.SetVector(UpId, transform.up * _area);
        _compute.SetVector(SunTravelId, transform.forward);
        _compute.SetFloat(CloudHeightId, sky.GetFloat(CloudHeightId));
        _compute.SetFloat(CloudScaleId, sky.GetFloat(CloudScaleId));
        _compute.SetFloat(CoverageId, sky.GetFloat(CoverageId));
        _compute.SetFloat(SoftnessId, sky.GetFloat(SoftnessId));
        _compute.SetVector(DriftId, drift);
        _compute.SetFloat(ThicknessId, _thickness);
        _compute.SetFloat(DarknessId, _darkness);

        int groups = Mathf.CeilToInt(_resolution / 8f);
        _compute.Dispatch(_kernel, groups, groups, 1);
        _cookie.GenerateMips();
    }

    private void BuildCookie()
    {
        _kernel = _compute.FindKernel("CSCloudShadows");
        _cookie = new RenderTexture(_resolution, _resolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
        {
            name = "CloudShadowCookie",
            enableRandomWrite = true,
            useMipMap = true,
            autoGenerateMips = false,
            wrapMode = TextureWrapMode.Clamp,     // past the edge: the border, which the compute leaves sunlit
            filterMode = FilterMode.Trilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        _cookie.Create();
    }

    private void ReleaseCookie()
    {
        if (_cookie == null) return;
        _cookie.Release();
        if (Application.isPlaying) Destroy(_cookie);
        else DestroyImmediate(_cookie);
        _cookie = null;
    }
}
