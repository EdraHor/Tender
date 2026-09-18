using UnityEngine;

/// <summary>
/// A place where it rains. Walk into the box and the sky clouds over, the rain comes up over a
/// few seconds, the haze closes in; walk out and it stops, the sky clears and the world dries.
///
/// Sits on the same object as a local Volume with the rainy-weather post-processing profile, the
/// way Post_Zone does: the Volume blends the colours in, this blends the weather in. One box, one
/// weather.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RainZone : MonoBehaviour
{
    [SerializeField] private Rain _rain;
    [SerializeField, Range(0f, 1f)] private float _strength = 1f;    // how hard it rains in here
    [SerializeField] private float _fadeTime = 4f;                    // seconds for the rain to come up or die away
    [SerializeField] private float _cloudTime = 20f;                  // seconds for the sky to cloud over or clear (slower than the rain)
    [SerializeField] private float _visibility = 1500f;               // metres of haze while it rains (the Atmosphere's clear value returns after)

    private static readonly int SkyRainId = Shader.PropertyToID("_SkyRain");   // read by CloudLayer.hlsl and SummerSky.shader

    private BoxCollider _box;
    private float _clearVisibility;
    private float _inside;    // 0 out .. 1 in, eased at the rain's pace
    private float _clouds;    // the same, at the sky's slower pace

    private void OnEnable() => _box = GetComponent<BoxCollider>();

    private void OnDisable() => Shader.SetGlobalFloat(SkyRainId, 0f);

    private void Update()
    {
        Camera view = Camera.main;
        bool inside = view != null && _box.bounds.Contains(view.transform.position);
        float target = inside ? 1f : 0f;
        _inside = Mathf.MoveTowards(_inside, target, Time.deltaTime / Mathf.Max(_fadeTime, 0.01f));
        _clouds = Mathf.MoveTowards(_clouds, target, Time.deltaTime / Mathf.Max(_cloudTime, 0.01f));

        if (_rain != null) _rain.Strength = _inside * _strength;
        Shader.SetGlobalFloat(SkyRainId, _clouds * _strength);

        Atmosphere atmosphere = Atmosphere.Current;
        if (atmosphere == null) return;
        // The clear-weather value is read the first time it is needed, not in OnEnable: the
        // Atmosphere may not have registered itself yet then, and a zero here would leave the
        // world in fog after the rain.
        if (_clearVisibility <= 0f) _clearVisibility = atmosphere.Visibility;
        atmosphere.Visibility = Mathf.Lerp(_clearVisibility, _visibility, _inside);
    }
}
