using UnityEngine;

/// <summary>
/// Makes a light flicker like a flame: its brightness dips and swells, and it wanders a few
/// centimetres, which is what makes the shadows round a campfire move.
///
/// Smooth noise, not random numbers. A random value every frame strobes; Perlin noise drifts from one
/// value to the next the way a real flame does. Only runs in Play Mode, and puts the light back as it
/// was when disabled.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    [Tooltip("How far the brightness swings, as a share of the light's own intensity.")]
    [Range(0f, 1f)][SerializeField] private float _amount = 0.25f;

    [Tooltip("Roughly how many flickers a second.")]
    [SerializeField] private float _speed = 6f;

    [Tooltip("How far the light wanders from its place, in metres.")]
    [SerializeField] private float _wander = 0.04f;

    private Light _light;
    private float _intensity;
    private Vector3 _position;
    private float _seed;

    private void OnEnable()
    {
        _light = GetComponent<Light>();
        _intensity = _light.intensity;
        _position = transform.localPosition;
        _seed = Random.value * 100f;
    }

    private void OnDisable()
    {
        _light.intensity = _intensity;
        transform.localPosition = _position;
    }

    private void Update()
    {
        float t = Time.time * _speed;

        // Two octaves: a slow swell and a quick flutter on top of it.
        float noise = Mathf.PerlinNoise(_seed, t * 0.3f) * 0.6f + Mathf.PerlinNoise(_seed + 7.1f, t) * 0.4f;
        _light.intensity = _intensity * (1f + (noise * 2f - 1f) * _amount);

        var drift = new Vector3(Mathf.PerlinNoise(_seed + 3.3f, t * 0.5f) - 0.5f,
                                Mathf.PerlinNoise(_seed + 5.9f, t * 0.5f) - 0.5f,
                                Mathf.PerlinNoise(_seed + 9.4f, t * 0.5f) - 0.5f);
        transform.localPosition = _position + drift * (2f * _wander);
    }
}
