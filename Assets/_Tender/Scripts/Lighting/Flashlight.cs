using UnityEngine;

/// <summary>
/// A torch in the player's hand: a spot light on the camera, switched with F.
///
/// Mostly a test light for night scenes. A spot light moving with the camera shows at once whether the
/// grass takes lamp light, whether its shadows work, and how its beam looks in the haze.
/// </summary>
[RequireComponent(typeof(Light))]
public class Flashlight : MonoBehaviour
{
    [SerializeField] private KeyCode _toggleKey = KeyCode.F;

    private Light _light;

    private void Awake() => _light = GetComponent<Light>();

    private void Update()
    {
        // The legacy Input class, like FirstPersonWalker that moves this camera.
        if (Input.GetKeyDown(_toggleKey)) _light.enabled = !_light.enabled;
    }
}
