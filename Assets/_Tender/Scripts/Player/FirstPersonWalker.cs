using UnityEngine;

/// <summary>
/// A plain first-person walker for looking around a scene: mouse to aim, WASD to move, Shift to
/// run, Space to jump, Escape to get the cursor back.
///
/// This is a LAB tool, not the game's character. The game's own movement lives in
/// <c>Scripts/Player/PlayerController</c>; this exists so a scene like GrassLab can be walked
/// through at eye height while it is being tuned, which is the only honest way to judge how grass
/// reads from where the player will actually stand.
///
/// It uses the legacy Input class because the project is set to "Both" and the rest of the
/// character code already does. Yaw turns the body, pitch turns the camera, which is the whole
/// reason the camera is a child rather than this component sitting on the camera itself.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalker : MonoBehaviour
{
    [Header("Looking")]
    [Tooltip("Degrees turned per unit of mouse movement.")]
    [SerializeField] private float _sensitivity = 2.2f;

    [Tooltip("How far up and down you can look, in degrees.")]
    [Range(60f, 89f)][SerializeField] private float _pitchLimit = 85f;

    [Tooltip("The eyes. Left empty, the first child camera is used.")]
    [SerializeField] private Transform _view;

    [Header("Moving")]
    [SerializeField] private float _walkSpeed = 4f;
    [SerializeField] private float _runSpeed = 9f;

    [Tooltip("How quickly the walker reaches the speed you asked for. Lower is more slippery.")]
    [SerializeField] private float _acceleration = 14f;

    [SerializeField] private float _jumpHeight = 1.1f;
    [SerializeField] private float _gravity = -22f;

    [Header("Start")]
    [Tooltip("Grab and hide the cursor as soon as the scene starts playing.")]
    [SerializeField] private bool _captureCursorOnStart = true;

    private CharacterController _controller;
    private float _yaw;
    private float _pitch;
    private Vector3 _velocity;          // horizontal, smoothed
    private float _fallSpeed;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (_view == null)
        {
            Camera child = GetComponentInChildren<Camera>();
            if (child != null) _view = child.transform;
        }

        // Start from whatever the scene was authored with, so dropping this on an existing camera
        // rig does not snap the view somewhere else on the first frame.
        _yaw = transform.eulerAngles.y;
        if (_view != null) _pitch = NormalizeAngle(_view.localEulerAngles.x);
    }

    private void Start()
    {
        if (_captureCursorOnStart) CaptureCursor(true);
    }

    private void Update()
    {
        HandleCursor();
        if (Cursor.lockState == CursorLockMode.Locked) Look();
        Move();
    }

    private void HandleCursor()
    {
        // Escape lets go, a click takes hold again. Without the release you cannot get out of play
        // mode with the mouse, which is maddening in the editor.
        if (Input.GetKeyDown(KeyCode.Escape)) CaptureCursor(false);
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) CaptureCursor(true);
    }

    private static void CaptureCursor(bool capture)
    {
        Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !capture;
    }

    private void Look()
    {
        // Mouse deltas are already per-frame amounts, so they must NOT be multiplied by deltaTime -
        // doing that makes the aim speed depend on frame rate, which feels like drag.
        _yaw += Input.GetAxisRaw("Mouse X") * _sensitivity;
        _pitch -= Input.GetAxisRaw("Mouse Y") * _sensitivity;
        _pitch = Mathf.Clamp(_pitch, -_pitchLimit, _pitchLimit);

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        if (_view != null) _view.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void Move()
    {
        float forward = Input.GetAxisRaw("Vertical");
        float strafe = Input.GetAxisRaw("Horizontal");

        Vector3 wish = transform.right * strafe + transform.forward * forward;
        if (wish.sqrMagnitude > 1f) wish.Normalize();      // no free speed on the diagonal

        bool running = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        Vector3 target = wish * (running ? _runSpeed : _walkSpeed);

        _velocity = Vector3.MoveTowards(_velocity, target, _acceleration * Time.deltaTime);

        if (_controller.isGrounded)
        {
            // A small push into the ground, otherwise isGrounded flickers on slopes and the walker
            // stutters as it repeatedly starts falling.
            _fallSpeed = -2f;
            if (Input.GetKeyDown(KeyCode.Space)) _fallSpeed = Mathf.Sqrt(-2f * _jumpHeight * _gravity);
        }
        else
        {
            _fallSpeed += _gravity * Time.deltaTime;
        }

        Vector3 motion = _velocity;
        motion.y = _fallSpeed;
        _controller.Move(motion * Time.deltaTime);
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees;
    }
}
