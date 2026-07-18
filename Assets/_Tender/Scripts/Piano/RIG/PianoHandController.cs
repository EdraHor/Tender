using UnityEngine;
using UnityEngine.Animations.Rigging;
using System;

[Serializable]
public class HandState
{
    public int CurrentTargetNote = -1;
    public bool IsPressingKey = false;
    public bool HasActiveTarget = false;
    public Vector3 TargetHandPosition;
    public float CurrentLayerWeight = 0f;
    public float TargetLayerWeight = 0f;
}

public class PianoHandController : MonoBehaviour
{
    [Header("Components")]
    [SerializeField] private PianoFingerController _fingerController;
    [SerializeField] private PianoKeyboard _keyboard;
    [SerializeField] private Animator _animator;
    
    [Header("Left Hand Rig")]
    [SerializeField] private TwoBoneIKConstraint _leftHandIKConstraint;
    [SerializeField] private Transform _leftIKTarget;
    
    [Header("Right Hand Rig")]
    [SerializeField] private TwoBoneIKConstraint _rightHandIKConstraint;
    [SerializeField] private Transform _rightIKTarget;
    
    [Header("Note Range Distribution")]
    [SerializeField] private int _leftHandMinNote = 21;  // A0
    [SerializeField] private int _leftHandMaxNote = 59;  // B3
    [SerializeField] private int _rightHandMinNote = 60; // C4
    [SerializeField] private int _rightHandMaxNote = 108; // C8
    
    [Header("Hand Settings")]
    [SerializeField] private Vector3 _targetOffset = new Vector3(0, 0.05f, 0);
    [SerializeField] private float _handMoveSpeed = 5f;
    
    [Header("Control Mode")]
    [SerializeField] private bool _manualIKControl = true;
    
    [Header("Animation Control")]
    [SerializeField] private string _pianoLayerName = "PianoLayer";
    [SerializeField] private float _overrideBlendSpeed = 5f;
    
    [Header("Debug")]
    [SerializeField] private bool _showDebug = true;
    
    private HandState _leftHandState = new HandState();
    private HandState _rightHandState = new HandState();
    
    private int _pianoLayerIndex = -1;
    
    private void Start()
    {
        if (_fingerController == null)
            _fingerController = GetComponent<PianoFingerController>();

        if (_leftIKTarget != null)
            _leftHandState.TargetHandPosition = _leftIKTarget.position;
        
        if (_rightIKTarget != null)
            _rightHandState.TargetHandPosition = _rightIKTarget.position;
        
        if (_animator != null)
        {
            _pianoLayerIndex = _animator.GetLayerIndex(_pianoLayerName);
        }
    }
    
    private void Update()
    {
        if (!Application.isPlaying)
            return;
        
        UpdateHand(ref _leftHandState, _leftIKTarget, _pianoLayerIndex);
        UpdateHand(ref _rightHandState, _rightIKTarget, _pianoLayerIndex);
    }
    
    private void UpdateHand(ref HandState state, Transform ikTarget, int layerIndex)
    {
        // Движение руки (IK Target)
        if (state.HasActiveTarget && !_manualIKControl && ikTarget != null)
        {
            ikTarget.position = Vector3.Lerp(
                ikTarget.position,
                state.TargetHandPosition,
                Time.deltaTime * _handMoveSpeed
            );
        }
    }
    
    public void OnKeyPressed(int midiNote)
    {
        if (_keyboard == null) return;
        
        Transform keyTransform = _keyboard.GetKeyTransform(midiNote);
        if (keyTransform == null) return;
        
        bool isLeftHandNote = midiNote >= _leftHandMinNote && midiNote <= _leftHandMaxNote;
        bool isRightHandNote = midiNote >= _rightHandMinNote && midiNote <= _rightHandMaxNote;
        
        if (isLeftHandNote)
        {
            HandleHandPress(ref _leftHandState, keyTransform, HandSide.Left, midiNote);
        }
        
        if (isRightHandNote)
        {
            HandleHandPress(ref _rightHandState, keyTransform, HandSide.Right, midiNote);
        }
    }

    private void HandleHandPress(ref HandState state, Transform keyTransform, HandSide side, int note)
    {
        state.CurrentTargetNote = note;
        state.HasActiveTarget = true;
        state.TargetHandPosition = keyTransform.position + _targetOffset;
        state.IsPressingKey = true;
        state.TargetLayerWeight = 1f;

        if (_fingerController != null)
        {
            // TODO: Здесь можно добавить логику выбора пальца в зависимости от ноты
            // Пока используем Указательный (Index) для всех нот для теста
            _fingerController.PressFinger(side, FingerType.Index);
        }

        if (_showDebug)
            Debug.Log($"<color={(side == HandSide.Left ? "cyan" : "magenta")}>{side} Hand: Note {note}</color>");
    }
    
    public void OnKeyReleased(int midiNote)
    {
        if (_leftHandState.CurrentTargetNote == midiNote)
        {
            _leftHandState.IsPressingKey = false;
            if (_fingerController != null) 
                _fingerController.ReleaseFinger(HandSide.Left, FingerType.Index);
        }
        
        if (_rightHandState.CurrentTargetNote == midiNote)
        {
            _rightHandState.IsPressingKey = false;
            if (_fingerController != null) 
                _fingerController.ReleaseFinger(HandSide.Right, FingerType.Index);
        }
    }
    
    public void ResetToIdle()
    {
        _leftHandState.HasActiveTarget = false;
        _leftHandState.CurrentTargetNote = -1;
        _leftHandState.IsPressingKey = false;
        _leftHandState.TargetLayerWeight = 0f;
        
        _rightHandState.HasActiveTarget = false;
        _rightHandState.CurrentTargetNote = -1;
        _rightHandState.IsPressingKey = false;
        _rightHandState.TargetLayerWeight = 0f;
        
        if (_fingerController != null)
        {
            _fingerController.ReleaseFinger(HandSide.Left, FingerType.Index);
            _fingerController.ReleaseFinger(HandSide.Right, FingerType.Index);
        }
    }
    
    private void OnEnable()
    {
        PianoKey.OnAnyKeyPressed += OnKeyPressed;
        PianoKey.OnAnyKeyReleased += OnKeyReleased;
    }
    
    private void OnDisable()
    {
        PianoKey.OnAnyKeyPressed -= OnKeyPressed;
        PianoKey.OnAnyKeyReleased -= OnKeyReleased;
    }
}