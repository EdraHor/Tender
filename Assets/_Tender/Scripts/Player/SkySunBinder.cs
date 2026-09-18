using UnityEngine;

/// <summary>
/// Hands the sun's direction to the sky shader.
///
/// A skybox shader has no main light of its own - it is drawn before URP's per-object lighting is
/// set up - so the one thing it cannot work out for itself is where the sun is. This pushes that
/// into a shader global, the same way GrassWind feeds the grass.
///
/// It runs in the editor too, so the sun painted in the sky follows the light as you drag it.
/// </summary>
[ExecuteAlways]
public class SkySunBinder : MonoBehaviour
{
    [Tooltip("The sun. Left empty, the light on this object is used.")]
    [SerializeField] private Light _sun;

    private static readonly int SunDirectionId = Shader.PropertyToID("_SkySunDirection");

    private void OnEnable()
    {
        if (_sun == null) _sun = GetComponent<Light>();
        Push();
    }

    private void Update() => Push();

    private void Push()
    {
        if (_sun == null) return;

        // A directional light points ALONG its forward axis, so the direction towards the sun is
        // the opposite of that. Getting this backwards puts the sun under your feet.
        Shader.SetGlobalVector(SunDirectionId, -_sun.transform.forward);
    }
}
