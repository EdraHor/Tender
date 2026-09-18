using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How hard it is raining, which way it falls, and the drops that land on things that can get wet.
///
/// One number, <see cref="Intensity"/>, is the single source of truth for the whole rain: the
/// drops you see (RainDrops), the wetting, the sound. One more, <see cref="DropsPerCubicMetre"/>,
/// is how much water is in the air at full intensity - it sets both how many drops are drawn and
/// how often one lands on a garment, so what you see and what gets wet agree.
///
/// The drops that matter for the cloth are thrown here: each frame a few rays go through every
/// <see cref="Wettable"/> along the rain's direction, and each one that hits leaves a wet spot and
/// a splash where it hit. Rain slants with the wind, which is why the side of a leg gets wet and
/// not just the top.
/// </summary>
public class Rain : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float _intensity;
    [SerializeField] private Vector3 _direction = new Vector3(0.35f, -1f, 0.15f);   // which way the drops fall
    [SerializeField] private float _fallSpeed = 9f;                                  // m/s along that direction
    [SerializeField] private float _dropsPerCubicMetre = 3f;                         // in the air, at full intensity
    [SerializeField] private float _spotRadius = 0.006f;                             // m, what one drop wets on landing

    [Header("The world")]
    [SerializeField, Range(0f, 1f)] private float _worldWetness;                     // ground, grass, props (Wet.hlsl)
    [SerializeField] private float _soakTime = 30f;                                  // seconds of full rain to soak the world
    [SerializeField] private float _dryTime = 180f;                                  // seconds to dry out after

    public static float Intensity { get; private set; }
    public static Vector3 Velocity { get; private set; } = new Vector3(0f, -9f, 0f);
    public static float DropsPerCubicMetre { get; private set; } = 3f;

    /// <summary>Where drops landed on wettable things lately: xyz and the time. For the splashes.</summary>
    public static readonly List<Vector4> Hits = new List<Vector4>();
    public const int MaxHits = 512;
    public const float HitLife = 0.25f;

    private static readonly int IntensityId = Shader.PropertyToID("_RainIntensity");
    private static readonly int VelocityId = Shader.PropertyToID("_RainVelocity");
    private static readonly int WetnessId = Shader.PropertyToID("_RainWetness");

    private float _owed;   // drops not yet thrown, carried between frames

    /// <summary>0 dry .. 1 pouring. A RainZone sets this as you walk in and out.</summary>
    public float Strength
    {
        get => _intensity;
        set => _intensity = Mathf.Clamp01(value);
    }

    private void OnEnable() => Publish();
    private void OnValidate() => Publish();

    private void Update()
    {
        // The world soaks while it rains and dries when it stops - slowly, as the last damp patch
        // of a path outlives the shower by a long way.
        _worldWetness += (_intensity / _soakTime - (1f - _intensity) / _dryTime) * Time.deltaTime;
        _worldWetness = Mathf.Clamp01(_worldWetness);

        Publish();
        Hits.RemoveAll(hit => Time.time - hit.w > HitLife);
        if (_intensity <= 0f) return;
        foreach (Wettable wettable in Wettable.All)
            Pour(wettable, Time.deltaTime);
    }

    private void Publish()
    {
        Intensity = _intensity;
        Velocity = _direction.normalized * _fallSpeed;
        DropsPerCubicMetre = _dropsPerCubicMetre;
        Shader.SetGlobalFloat(IntensityId, Intensity);
        Shader.SetGlobalVector(VelocityId, Velocity);
        Shader.SetGlobalFloat(WetnessId, _worldWetness);
    }

    /// <summary>Throw this frame's share of drops at one object.</summary>
    public void Pour(Wettable wettable, float seconds)
    {
        if (!wettable.TakesDrops) return;
        Bounds bounds = wettable.Bounds;
        Vector3 direction = _direction.normalized;

        // Drops crossing a square metre each second: how many are in a cubic metre times how fast
        // they fall. What the rain sees of the box: its top, plus the side it slants onto.
        float flux = _dropsPerCubicMetre * _fallSpeed * _intensity;
        float slant = new Vector2(direction.x, direction.z).magnitude / Mathf.Max(-direction.y, 0.1f);
        float exposed = bounds.size.x * bounds.size.z + bounds.size.y * Mathf.Max(bounds.size.x, bounds.size.z) * slant;
        _owed += exposed * flux * seconds;

        while (_owed >= 1f)
        {
            _owed -= 1f;
            // Aim at a random point inside the box and come at it from upstream: whichever face
            // of the object the ray meets first is the one the drop lands on.
            Vector3 target = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                Random.Range(bounds.min.y, bounds.max.y),
                Random.Range(bounds.min.z, bounds.max.z));
            Vector3 from = target - direction * 5f;
            if (Physics.Raycast(from, direction, out RaycastHit hit, 10f) &&
                hit.collider.GetComponentInParent<Wettable>() == wettable)
            {
                wettable.AddHit(hit.point, _spotRadius);
                if (Hits.Count < MaxHits) Hits.Add(new Vector4(hit.point.x, hit.point.y, hit.point.z, Time.time));
            }
        }
    }
}
