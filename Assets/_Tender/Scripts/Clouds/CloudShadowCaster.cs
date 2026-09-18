using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Makes the cloud cast a shadow on the world.
///
/// A volumetric cloud is not in URP's shadow map - it is a transparent object, and the shadow
/// pass only sees opaque geometry. So the shadow is produced the other way round: render the
/// cloud from the SUN's point of view, flip the result, and hand it to the directional light as
/// a COOKIE.
///
/// Why a cookie rather than anything cleverer: URP's <c>GetMainLight(positionWS)</c> already
/// multiplies the main light by the cookie, and the stock Lit / SimpleLit shaders already call
/// it. So terrain, props and characters all fall into cloud shadow with ZERO changes to their
/// materials. Nothing else in this project has to know the cloud exists.
///
/// Put clouds on their own layer. The shadow camera renders only that layer, and if the terrain
/// shares it the terrain will occlude the cloud in its own shadow.
///
/// Runs in PLAY MODE ONLY - see the note in OnEnable for why an edit-mode preview is unsafe.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class CloudShadowCaster : MonoBehaviour
{
    [Tooltip("The directional light this cloud shadows. Its cookie is overwritten.")]
    [SerializeField] private Light _sun;

    [Tooltip("Cookie resolution. 256 is plenty - cloud shadows are soft.")]
    [SerializeField] private int _resolution = 256;

    [Tooltip("How dark the shadow gets. 1 = black; plenty of light still reaches the ground under a real cumulus.")]
    [Range(0f, 1f)][SerializeField] private float _strength = 0.75f;

    [Tooltip("Extra margin around the cloud so its edges are not clipped by the cookie.")]
    [Range(1f, 2f)][SerializeField] private float _padding = 1.25f;

    [Tooltip("Refresh every N frames. Clouds drift slowly, so there is no need to redo this every frame.")]
    [Range(1, 30)][SerializeField] private int _refreshInterval = 3;

    private static readonly int ShadowStrengthId = Shader.PropertyToID("_ShadowStrength");

    private Renderer _renderer;
    private Camera _camera;
    private RenderTexture _view;      // the cloud as the sun sees it
    private RenderTexture _cookie;    // 1 - coverage
    private Material _invert;
    private int _counter;

    private UniversalAdditionalLightData _sunData;
    private Vector2 _savedCookieSize;     // the light's own cookie framing, put back on disable
    private Vector2 _savedCookieOffset;
    private bool _savedSettings;

    private void OnEnable()
    {
        _renderer = GetComponent<Renderer>();
        // Deliberately no Refresh() here. Rendering a second camera must happen from Update, i.e.
        // OUTSIDE any render already in flight. Doing it from OnEnable in the editor lands inside
        // one, and URP throws "Blitter is already initialized" mid-pass - which leaves the whole
        // editor drawing nothing until a domain reload. Play-mode only, on purpose.
    }

    private void OnDisable()
    {
        // Hand the light back exactly as we found it, otherwise the scene keeps a dead cookie
        // and a cookie framing sized for a cloud that is no longer there.
        if (_sun != null && _sun.cookie == _cookie) _sun.cookie = null;
        if (_sunData != null && _savedSettings)
        {
            _sunData.lightCookieSize = _savedCookieSize;
            _sunData.lightCookieOffset = _savedCookieOffset;
            _savedSettings = false;
        }
        Release();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;      // see the note in OnEnable
        if (++_counter < _refreshInterval) return;
        _counter = 0;
        Refresh();
    }

    private void Refresh()
    {
        if (_sun == null || _sun.type != LightType.Directional) return;
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer == null) return;

        EnsureResources();

        // Frame the cloud from the sun's direction. A sphere around the bounds keeps the framing
        // stable as the sun turns - using the box directly would make the shadow pop.
        Bounds bounds = _renderer.bounds;
        float radius = bounds.extents.magnitude * _padding;
        Vector3 direction = _sun.transform.forward;

        _camera.transform.SetPositionAndRotation(bounds.center - direction * (radius + 1f),
                                                 _sun.transform.rotation);
        _camera.orthographic = true;
        _camera.orthographicSize = radius;
        _camera.nearClipPlane = 0.01f;
        _camera.farClipPlane = radius * 2f + 2f;
        _camera.cullingMask = 1 << gameObject.layer;

        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _view };
        if (!RenderPipeline.SupportsRenderRequest(_camera, request)) return;
        RenderPipeline.SubmitRenderRequest(_camera, request);

        _invert.SetFloat(ShadowStrengthId, _strength);
        Graphics.Blit(_view, _cookie, _invert);

        // URP ignores Light.cookieSize for directional lights. It frames the cookie with
        // lightCookieSize / lightCookieOffset on UniversalAdditionalLightData, measured in the
        // LIGHT's own space - and their default is (1, 1), i.e. a one-metre square sitting at the
        // light's origin. Leave them alone and the cookie is real, bound, and projected onto a
        // patch of world the size of a doormat: nothing on the ground ever sees it.
        //
        // Size = the shadow camera's full width, so cookie texels and camera pixels line up.
        // Offset re-centres that square on the cloud, which is why the light itself never has to
        // be moved - a directional light's position is otherwise meaningless.
        if (_sunData == null)
        {
            _sunData = _sun.GetComponent<UniversalAdditionalLightData>();
            if (_sunData == null) _sunData = _sun.gameObject.AddComponent<UniversalAdditionalLightData>();
        }
        if (!_savedSettings)
        {
            _savedCookieSize = _sunData.lightCookieSize;
            _savedCookieOffset = _sunData.lightCookieOffset;
            _savedSettings = true;
        }

        Vector3 centerInLightSpace = _sun.transform.InverseTransformPoint(bounds.center);
        _sunData.lightCookieSize = new Vector2(radius * 2f, radius * 2f);
        _sunData.lightCookieOffset = new Vector2(centerInLightSpace.x, centerInLightSpace.y);

        _sun.cookie = _cookie;
    }

    private void EnsureResources()
    {
        int size = Mathf.Clamp(Mathf.ClosestPowerOfTwo(_resolution), 64, 2048);

        if (_view != null && _view.width != size) Release();

        if (_view == null)
        {
            _view = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
            {
                name = "CloudShadow_View",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _view.Create();
        }

        if (_cookie == null)
        {
            // Clamp, not Repeat: a directional cookie tiles by default, which would stamp the
            // cloud's shadow across the whole world.
            _cookie = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                name = "CloudShadow_Cookie",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _cookie.Create();
        }

        if (_invert == null)
        {
            var shader = Shader.Find("Hidden/Tender/CloudShadowInvert");
            if (shader == null) return;
            _invert = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        if (_camera == null)
        {
            var go = new GameObject("CloudShadowCamera") { hideFlags = HideFlags.HideAndDontSave };
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;               // we drive it by hand, never per-frame
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.allowMSAA = false;
            _camera.allowHDR = false;

            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            // Do NOT switch the colour/depth options off here: URP then builds the frame without
            // those resources and the render graph fails with a null resource index.
        }
    }

    private void Release()
    {
        if (_view != null) { _view.Release(); DestroyImmediate(_view); _view = null; }
        if (_cookie != null) { _cookie.Release(); DestroyImmediate(_cookie); _cookie = null; }
        if (_invert != null) { DestroyImmediate(_invert); _invert = null; }
        if (_camera != null) { DestroyImmediate(_camera.gameObject); _camera = null; }
    }
}
