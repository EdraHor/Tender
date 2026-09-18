using UnityEngine;

/// <summary>
/// Pale streaks that ride the wind, so you can SEE where it is blowing - the wind lines of Breath of
/// the Wild, in their simplest form.
///
/// An ordinary particle system spawns the particles around the camera and draws their trails. This
/// only steers them: every frame each particle asks GrassWind which way the wind blows where it is
/// and how hard, and is given that velocity and that visibility. Because GrassWind answers with the
/// same maths the grass shaders use, the streaks follow exactly the waves the grass is bending in -
/// they bunch up and brighten on a wave front and fade in the calm behind it.
///
/// The particles are born HERE, not by the system's own emitter: the emitter cannot know where the
/// ground is, and a particle born at the wrong height and moved afterwards drags its trail up from
/// the birth point - a vertical line standing in the grass at the head of every streak. So the
/// emission module is switched off and each frame this spawns the same number of particles itself,
/// each already at its height above the terrain. The rate is still the one set on the module.
///
/// Put it on a GameObject with a ParticleSystem (world simulation space, a trail module) and give it
/// the scene's GrassWind.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class GrassWindParticles : MonoBehaviour
{
    [SerializeField] private GrassWind _wind;

    [Tooltip("Streaks are spawned in a square this many metres across around the camera.")]
    [SerializeField] private float _area = 70f;

    [Tooltip("Height above the ground a streak is spawned at, in metres.")]
    [SerializeField] private Vector2 _height = new Vector2(0.4f, 2.5f);

    [Tooltip("How visible a streak is in the calm between waves, against 1 on a wave front.")]
    [Range(0f, 1f)][SerializeField] private float _calmVisibility = 0.1f;

    [Tooltip("How quickly a streak weaves from side to side, in radians per second. 0 draws straight lines.")]
    [SerializeField] private float _curl = 2.5f;

    private ParticleSystem _system;
    private ParticleSystem.Particle[] _particles;
    private float _perSecond;    // the emission module's rate, taken over by Spawn
    private float _owed;         // particles due but not yet spawned (fractions carry over between frames)

    private void OnEnable()
    {
        _system = GetComponent<ParticleSystem>();
        var emission = _system.emission;
        _perSecond = emission.rateOverTime.constant;
        emission.enabled = false;
    }

    /// <summary>Born at a random spot in the area round the camera, already at its height above the ground.</summary>
    private void Spawn(float deltaTime, Terrain terrain)
    {
        _owed += _perSecond * deltaTime;
        var born = new ParticleSystem.EmitParams();
        while (_owed >= 1f)
        {
            _owed -= 1f;
            Vector3 position = transform.position + new Vector3(Random.Range(-0.5f, 0.5f) * _area, 0f, Random.Range(-0.5f, 0.5f) * _area);
            float ground = terrain != null ? terrain.SampleHeight(position) + terrain.transform.position.y : 0f;
            position.y = ground + Random.Range(_height.x, _height.y);
            born.position = position;
            born.applyShapeToPosition = false;
            _system.Emit(born, 1);
        }
    }

    private void LateUpdate()
    {
        if (_wind == null) return;
        Camera camera = Camera.main;
        if (camera != null)
        {
            // The emitter follows the camera; particles already in the air stay where they are,
            // because the system simulates in world space.
            Vector3 eye = camera.transform.position;
            transform.SetPositionAndRotation(new Vector3(eye.x, 0f, eye.z), Quaternion.identity);
        }

        Terrain terrain = Terrain.activeTerrain;
        Spawn(Time.deltaTime, terrain);

        int capacity = _system.main.maxParticles;
        if (_particles == null || _particles.Length < capacity) _particles = new ParticleSystem.Particle[capacity];

        int count = _system.GetParticles(_particles);
        float time = GrassWind.ShaderTime;

        for (int i = 0; i < count; i++)
        {
            ParticleSystem.Particle particle = _particles[i];
            Vector3 position = particle.position;

            Vector2 flow = _wind.FlowAt(position);
            float gust = _wind.GustAt(position, time);

            // Carried along at the speed the waves travel, faster in the thick of one - and weaving
            // a little up, down and sideways on its own rhythm, so the trail curls instead of ruling
            // a straight line across the field.
            float speed = _wind.Speed * Mathf.Lerp(0.7f, 1.3f, gust);
            var along = new Vector3(flow.x, 0f, flow.y);
            var across = new Vector3(-flow.y, 0f, flow.x);
            float age = particle.startLifetime - particle.remainingLifetime;
            float swirl = age * _curl + particle.randomSeed % 628 / 100f;
            Vector3 weave = Vector3.up * Mathf.Sin(swirl) * 0.5f + across * Mathf.Cos(swirl * 0.7f);
            particle.velocity = (along + weave * 0.25f) * speed;

            // Seen where the wind is, faded in the calm - and in and out over its own life, so no
            // streak pops.
            float life = 1f - particle.remainingLifetime / particle.startLifetime;
            float fade = Mathf.SmoothStep(0f, 1f, life / 0.2f) * Mathf.SmoothStep(0f, 1f, (1f - life) / 0.3f);
            Color32 colour = particle.startColor;
            colour.a = (byte)(255f * fade * Mathf.Lerp(_calmVisibility, 1f, gust));
            particle.startColor = colour;

            _particles[i] = particle;
        }

        _system.SetParticles(_particles, count);
    }
}
