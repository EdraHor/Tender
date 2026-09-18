using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One place that decides how the wind blows, for every grass system at once.
///
/// It writes global shader properties rather than material ones on purpose: the tall blades and
/// the short shell grass are different shaders, and the moment they read wind from separate
/// materials they start to disagree and the seam between them shows.
///
/// The wind is WAVES rolling across the field, not noise: a front every few tens of metres, knocking
/// the grass over as it arrives and letting it stand back up behind. The paths the waves travel
/// along can bend, so the whole field is swept in an arc. This object's position is where the wind
/// blows in exactly <see cref="_direction"/>; select it to see the paths drawn in the scene.
///
/// The same maths is on the C# side (<see cref="FlowAt"/>, <see cref="GustAt"/>) for anything that
/// has to ride the wind the grass feels - GrassWindParticles, for one.
///
/// Drop one of these anywhere in the scene. There is no reason to have two.
/// </summary>
[ExecuteAlways]
public class GrassWind : MonoBehaviour
{
    [Header("Where it blows")]
    [Tooltip("Which way the wind blows at this object's position, in degrees around Y: 0 towards +Z, 90 towards +X. Same as the clouds and the sky.")]
    [Range(0f, 360f)][SerializeField] private float _direction = 45f;

    [Tooltip("How far the wind turns every 100 m it travels, in degrees. Positive turns left. It keeps turning for 800 m either side of this object, then blows straight on.")]
    [Range(-30f, 30f)][SerializeField] private float _bend = 8f;

    [Header("Waves")]
    [Tooltip("How fast the waves roll across the field, metres per second.")]
    [SerializeField] private float _speed = 6f;

    [Tooltip("Metres from one wave to the next.")]
    [SerializeField] private float _waveLength = 24f;

    [Tooltip("How far the grass leans in the calm between waves, in radians.")]
    [FormerlySerializedAs("_strength")]
    [Range(0f, 1.5f)][SerializeField] private float _calmLean = 0.2f;

    [Tooltip("How much further it leans at the crest of a wave, in radians. 0.7 is a fresh wind; 1.1 flattens the field.")]
    [Range(0f, 1.5f)][SerializeField] private float _gustLean = 0.75f;

    [Tooltip("How long a stretch of one wave front blows as a single gust, in metres. Small breaks the fronts into patches; large makes long unbroken lines.")]
    [SerializeField] private float _gustSize = 40f;

    [Tooltip("Small sideways shiver on top of the waves. Keeps the field from marching in step.")]
    [Range(0f, 1f)][SerializeField] private float _flutter = 0.3f;

    [Header("Blades")]
    [Tooltip("Hardest a blade may ever lean, in radians. Past about 1.4 blades fold flat.")]
    [Range(0.2f, 2f)][SerializeField] private float _maxLean = 1.35f;

    [Tooltip("How much blades differ in springiness. 0 means the whole field moves as one sheet.")]
    [Range(0f, 1f)][SerializeField] private float _stiffness = 0.35f;

    private static readonly int DirId = Shader.PropertyToID("_GrassWindDir");
    private static readonly int CentreId = Shader.PropertyToID("_GrassWindCentre");
    private static readonly int SpeedId = Shader.PropertyToID("_GrassWindSpeed");
    private static readonly int StrengthId = Shader.PropertyToID("_GrassWindStrength");
    private static readonly int GustId = Shader.PropertyToID("_GrassWindGust");
    private static readonly int WaveLengthId = Shader.PropertyToID("_GrassWindWaveLength");
    private static readonly int ScaleId = Shader.PropertyToID("_GrassWindScale");
    private static readonly int FlutterId = Shader.PropertyToID("_GrassWindFlutter");
    private static readonly int MaxLeanId = Shader.PropertyToID("_GrassMaxLean");
    private static readonly int StiffnessId = Shader.PropertyToID("_GrassStiffness");

    /// <summary>Metres per second the waves travel.</summary>
    public float Speed => _speed;

    /// <summary>
    /// The clock the shaders run on. URP hands shaders Time.time in Play Mode but real time in the
    /// editor, so anything following the grass has to read the same one.
    /// </summary>
    public static float ShaderTime => Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    /// <summary>The wind's direction as a unit vector in world XZ - the way a transform facing `_direction` looks.</summary>
    private Vector2 Heading => new Vector2(Mathf.Sin(_direction * Mathf.Deg2Rad), Mathf.Cos(_direction * Mathf.Deg2Rad));

    /// <summary>Radians the wind turns per metre travelled: `_bend` degrees every 100 m.</summary>
    private float Curvature => _bend * Mathf.Deg2Rad / 100f;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();
    private void Update() => Apply();     // a handful of SetGlobal calls; cheap enough to just keep live

    private void Apply()
    {
        Vector2 heading = Heading;
        Shader.SetGlobalVector(DirId, new Vector4(heading.x, heading.y, 0f, 0f));
        Shader.SetGlobalVector(CentreId, new Vector4(transform.position.x, transform.position.z, Curvature, 0f));
        Shader.SetGlobalFloat(SpeedId, _speed);
        Shader.SetGlobalFloat(StrengthId, _calmLean);
        Shader.SetGlobalFloat(GustId, _gustLean);
        Shader.SetGlobalFloat(WaveLengthId, Mathf.Max(_waveLength, 0.5f));
        Shader.SetGlobalFloat(ScaleId, 1f / Mathf.Max(_gustSize, 0.01f));
        Shader.SetGlobalFloat(FlutterId, _flutter);

        // These two are global rather than per-material because the blade and its seed head are
        // drawn by different shaders and have to bend by exactly the same amount.
        Shader.SetGlobalFloat(MaxLeanId, _maxLean);
        Shader.SetGlobalFloat(StiffnessId, _stiffness);
    }

    /// <summary>
    /// Where a point stands in the wind's own frame (x downstream, y across, in metres) and which way
    /// the wind blows there. GrassWindFrame in GrassCommon.hlsl, line for line.
    /// </summary>
    public Vector2 Frame(Vector3 position, out Vector2 flow)
    {
        Vector2 heading = Heading;
        var left = new Vector2(-heading.y, heading.x);
        var offset = new Vector2(position.x - transform.position.x, position.z - transform.position.z);
        float along = Vector2.Dot(offset, heading);
        float across = Vector2.Dot(offset, left);
        float bend = Curvature;

        float turnAlong = Mathf.Clamp(along, -800f, 800f);
        float spreadAcross = Mathf.Clamp(across, -800f, 800f);

        flow = (heading + left * (bend * turnAlong)).normalized;
        return new Vector2(along * Mathf.Exp(bend * spreadAcross), across - 0.5f * bend * turnAlong * turnAlong);
    }

    /// <summary>The way the wind blows at a point, as a unit vector in world XZ.</summary>
    public Vector2 FlowAt(Vector3 position)
    {
        Frame(position, out Vector2 flow);
        return flow;
    }

    /// <summary>How hard the wind blows at a point, 0 calm .. 1 a crest. GrassGustInFrame, line for line.</summary>
    public float GustAt(Vector3 position, float time)
    {
        Vector2 frame = Frame(position, out _);
        float waveLength = Mathf.Max(_waveLength, 0.5f);
        var moving = new Vector2(frame.x - time * _speed, frame.y);

        float wander = GrassNoise.ValueNoise(Vector2.Scale(moving, new Vector2(0.004f, 0.007f)) + new Vector2(5.3f, 5.3f)) * 2f - 1f;
        float cycle = GrassNoise.Frac(-(moving.x + wander * waveLength * 1.5f) / waveLength);
        float arrive = Mathf.SmoothStep(0f, 1f, cycle / 0.18f);
        float leave = 1f - Mathf.SmoothStep(0f, 1f, (cycle - 0.18f) / 0.82f);
        float wave = arrive * leave * leave;

        Vector2 gustPoint = Vector2.Scale(moving / Mathf.Max(_gustSize, 0.01f), new Vector2(1.5f, 1f)) + new Vector2(0f, time * 0.03f);
        float gusty = Mathf.SmoothStep(0f, 1f, (GrassNoise.ValueNoise(gustPoint) - 0.35f) / 0.4f);
        return wave * gusty;
    }

    /// <summary>
    /// The paths the wind travels along, so a bend can be set by eye: seven lines across the field,
    /// each followed for 600 m, with a tick where every wave front stands right now.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        Vector2 heading = Heading;
        var left = new Vector3(-heading.y, 0f, heading.x);
        float time = ShaderTime;
        Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.9f);

        for (int line = -3; line <= 3; line++)
        {
            Vector3 point = transform.position + left * (line * 40f) - new Vector3(heading.x, 0f, heading.y) * 300f;
            float previousGust = GustAt(point, time);

            for (int step = 0; step < 150; step++)
            {
                Vector2 flow = FlowAt(point);
                Vector3 next = point + new Vector3(flow.x, 0f, flow.y) * 4f;
                Gizmos.DrawLine(point, next);

                float gust = GustAt(next, time);
                if (gust > 0.5f && previousGust <= 0.5f)
                    Gizmos.DrawLine(next - left * 3f + Vector3.up, next + left * 3f + Vector3.up);
                previousGust = gust;
                point = next;
            }
        }
    }
}
