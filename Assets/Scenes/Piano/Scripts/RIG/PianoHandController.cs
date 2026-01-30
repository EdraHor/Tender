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
    [Header("Character References")]
    [SerializeField] private Transform _characterRoot;
    [SerializeField] private Animator _animator;
    
    [Header("Left Hand Rig")]
    [SerializeField] private TwoBoneIKConstraint _leftHandIKConstraint;
    [SerializeField] private Transform _leftIKTarget;
    
    [Header("Right Hand Rig")]
    [SerializeField] private TwoBoneIKConstraint _rightHandIKConstraint;
    [SerializeField] private Transform _rightIKTarget;
    
    [Header("Keyboard Reference")]
    [SerializeField] private PianoKeyboard _keyboard;
    
    [Header("Note Range Distribution")]
    [SerializeField] private int _leftHandMinNote = 21;  // A0
    [SerializeField] private int _leftHandMaxNote = 59;  // B3
    [SerializeField] private int _rightHandMinNote = 60; // C4
    [SerializeField] private int _rightHandMaxNote = 108; // C8
    
    [Header("Hand Settings")]
    [SerializeField] private Vector3 _targetOffset = new Vector3(0, 0.05f, 0);
    [SerializeField] private float _handMoveSpeed = 5f;
    [SerializeField] private float _fingerBendSpeed = 10f;
    
    [Header("Control Mode")]
    [SerializeField] private bool _manualIKControl = true;
    
    [Header("Animation Control")]
    [SerializeField] private string _pianoLayerName = "PianoLayer";
    [SerializeField] private float _overrideBlendSpeed = 5f;
    
    [Header("Pose Preview (Editor Only)")]
    [SerializeField] private bool _previewPose = false;
    [SerializeField] private HandPose _restPose = new HandPose() { PoseName = "Rest" };
    [SerializeField] private HandPose _pressPose = new HandPose() { PoseName = "Press" };
    
    [Header("Debug")]
    [SerializeField] private bool _showDebug = true;
    
    // Левая рука - кости
    private Transform _leftHandBone;
    private Transform _leftThumbBone1, _leftThumbBone2, _leftThumbBone3;
    private Transform _leftIndexBone1, _leftIndexBone2, _leftIndexBone3;
    private Transform _leftMiddleBone1, _leftMiddleBone2, _leftMiddleBone3;
    private Transform _leftRingBone1, _leftRingBone2, _leftRingBone3;
    private Transform _leftPinkyBone1, _leftPinkyBone2, _leftPinkyBone3;
    
    // Правая рука - кости
    private Transform _rightHandBone;
    private Transform _rightThumbBone1, _rightThumbBone2, _rightThumbBone3;
    private Transform _rightIndexBone1, _rightIndexBone2, _rightIndexBone3;
    private Transform _rightMiddleBone1, _rightMiddleBone2, _rightMiddleBone3;
    private Transform _rightRingBone1, _rightRingBone2, _rightRingBone3;
    private Transform _rightPinkyBone1, _rightPinkyBone2, _rightPinkyBone3;
    
    private HandState _leftHandState = new HandState();
    private HandState _rightHandState = new HandState();
    
    private int _pianoLayerIndex = -1;
    
    private void Start()
    {
        FindMixamoBones();
        
        if (_leftIKTarget != null)
            _leftHandState.TargetHandPosition = _leftIKTarget.position;
        
        if (_rightIKTarget != null)
            _rightHandState.TargetHandPosition = _rightIKTarget.position;
        
        if (_animator != null)
        {
            _pianoLayerIndex = _animator.GetLayerIndex(_pianoLayerName);
            if (_pianoLayerIndex < 0)
                Debug.LogWarning($"Layer '{_pianoLayerName}' не найден в аниматоре!");
        }
    }
    
    private void FindMixamoBones()
    {
        if (_characterRoot == null)
        {
            Debug.LogError("Character root not assigned!");
            return;
        }
        
        // Левая рука
        FindHandBones(
            "mixamorig:LeftHand",
            out _leftHandBone,
            out _leftThumbBone1, out _leftThumbBone2, out _leftThumbBone3,
            out _leftIndexBone1, out _leftIndexBone2, out _leftIndexBone3,
            out _leftMiddleBone1, out _leftMiddleBone2, out _leftMiddleBone3,
            out _leftRingBone1, out _leftRingBone2, out _leftRingBone3,
            out _leftPinkyBone1, out _leftPinkyBone2, out _leftPinkyBone3
        );
        
        // Правая рука
        FindHandBones(
            "mixamorig:RightHand",
            out _rightHandBone,
            out _rightThumbBone1, out _rightThumbBone2, out _rightThumbBone3,
            out _rightIndexBone1, out _rightIndexBone2, out _rightIndexBone3,
            out _rightMiddleBone1, out _rightMiddleBone2, out _rightMiddleBone3,
            out _rightRingBone1, out _rightRingBone2, out _rightRingBone3,
            out _rightPinkyBone1, out _rightPinkyBone2, out _rightPinkyBone3
        );
        
        if (_showDebug)
        {
            Debug.Log("<color=cyan>Found bones for BOTH hands:</color>");
            Debug.Log($"Left Index: {(_leftIndexBone1 ? "✓" : "✗")} {(_leftIndexBone2 ? "✓" : "✗")} {(_leftIndexBone3 ? "✓" : "✗")}");
            Debug.Log($"Right Index: {(_rightIndexBone1 ? "✓" : "✗")} {(_rightIndexBone2 ? "✓" : "✗")} {(_rightIndexBone3 ? "✓" : "✗")}");
        }
    }
    
    private void FindHandBones(
        string handPrefix,
        out Transform handBone,
        out Transform thumb1, out Transform thumb2, out Transform thumb3,
        out Transform index1, out Transform index2, out Transform index3,
        out Transform middle1, out Transform middle2, out Transform middle3,
        out Transform ring1, out Transform ring2, out Transform ring3,
        out Transform pinky1, out Transform pinky2, out Transform pinky3)
    {
        handBone = FindBoneRecursive(_characterRoot, handPrefix);
        
        thumb1 = FindBoneRecursive(_characterRoot, handPrefix + "Thumb1");
        thumb2 = FindBoneRecursive(_characterRoot, handPrefix + "Thumb2");
        thumb3 = FindBoneRecursive(_characterRoot, handPrefix + "Thumb3");
        
        index1 = FindBoneRecursive(_characterRoot, handPrefix + "Index1");
        index2 = FindBoneRecursive(_characterRoot, handPrefix + "Index2");
        index3 = FindBoneRecursive(_characterRoot, handPrefix + "Index3");
        
        middle1 = FindBoneRecursive(_characterRoot, handPrefix + "Middle1");
        middle2 = FindBoneRecursive(_characterRoot, handPrefix + "Middle2");
        middle3 = FindBoneRecursive(_characterRoot, handPrefix + "Middle3");
        
        ring1 = FindBoneRecursive(_characterRoot, handPrefix + "Ring1");
        ring2 = FindBoneRecursive(_characterRoot, handPrefix + "Ring2");
        ring3 = FindBoneRecursive(_characterRoot, handPrefix + "Ring3");
        
        pinky1 = FindBoneRecursive(_characterRoot, handPrefix + "Pinky1");
        pinky2 = FindBoneRecursive(_characterRoot, handPrefix + "Pinky2");
        pinky3 = FindBoneRecursive(_characterRoot, handPrefix + "Pinky3");
    }
    
    private Transform FindBoneRecursive(Transform parent, string boneName)
    {
        if (parent.name == boneName)
            return parent;
        
        foreach (Transform child in parent)
        {
            Transform found = FindBoneRecursive(child, boneName);
            if (found != null)
                return found;
        }
        
        return null;
    }
    
    private void Update()
    {
        if (!Application.isPlaying)
            return;
        
        UpdateHand(
            ref _leftHandState,
            _leftIKTarget,
            _pianoLayerIndex,
            _leftIndexBone1, _leftIndexBone2, _leftIndexBone3
        );
        
        UpdateHand(
            ref _rightHandState,
            _rightIKTarget,
            _pianoLayerIndex,
            _rightIndexBone1, _rightIndexBone2, _rightIndexBone3
        );
    }
    
    private void UpdateHand(
        ref HandState state,
        Transform ikTarget,
        int layerIndex,
        Transform indexBone1, Transform indexBone2, Transform indexBone3)
    {
        // Плавно меняем вес слоя
        if (layerIndex >= 0 && _animator != null)
        {
            state.CurrentLayerWeight = Mathf.Lerp(
                state.CurrentLayerWeight,
                state.TargetLayerWeight,
                Time.deltaTime * _overrideBlendSpeed
            );
            _animator.SetLayerWeight(layerIndex, state.CurrentLayerWeight);
        }
        
        // Движение руки
        if (state.HasActiveTarget && !_manualIKControl && ikTarget != null)
        {
            ikTarget.position = Vector3.Lerp(
                ikTarget.position,
                state.TargetHandPosition,
                Time.deltaTime * _handMoveSpeed
            );
        }
        
        // Анимация пальцев
        HandPose targetPose = state.IsPressingKey ? _pressPose : _restPose;
        float t = Time.deltaTime * _fingerBendSpeed;
        targetPose.Index.Lerp(targetPose.Index, t, indexBone1, indexBone2, indexBone3);
    }
    
    public void OnKeyPressed(int midiNote)
    {
        if (_keyboard == null) return;
        
        Transform keyTransform = _keyboard.GetKeyTransform(midiNote);
        if (keyTransform == null) return;
        
        // Определяем какая рука должна реагировать
        bool isLeftHandNote = midiNote >= _leftHandMinNote && midiNote <= _leftHandMaxNote;
        bool isRightHandNote = midiNote >= _rightHandMinNote && midiNote <= _rightHandMaxNote;
        
        if (isLeftHandNote)
        {
            _leftHandState.CurrentTargetNote = midiNote;
            _leftHandState.HasActiveTarget = true;
            _leftHandState.TargetHandPosition = keyTransform.position + _targetOffset;
            _leftHandState.IsPressingKey = true;
            _leftHandState.TargetLayerWeight = 1f;
            
            if (_showDebug)
                Debug.Log($"<color=cyan>LEFT Hand: Note {midiNote}</color>");
        }
        
        if (isRightHandNote)
        {
            _rightHandState.CurrentTargetNote = midiNote;
            _rightHandState.HasActiveTarget = true;
            _rightHandState.TargetHandPosition = keyTransform.position + _targetOffset;
            _rightHandState.IsPressingKey = true;
            _rightHandState.TargetLayerWeight = 1f;
            
            if (_showDebug)
                Debug.Log($"<color=magenta>RIGHT Hand: Note {midiNote}</color>");
        }
    }
    
    public void OnKeyReleased(int midiNote)
    {
        if (_leftHandState.CurrentTargetNote == midiNote)
        {
            _leftHandState.IsPressingKey = false;
            if (_showDebug)
                Debug.Log($"<color=cyan>LEFT Hand: Released {midiNote}</color>");
        }
        
        if (_rightHandState.CurrentTargetNote == midiNote)
        {
            _rightHandState.IsPressingKey = false;
            if (_showDebug)
                Debug.Log($"<color=magenta>RIGHT Hand: Released {midiNote}</color>");
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
    
    private void OnValidate()
    {
        if (_previewPose && _characterRoot != null)
        {
            FindMixamoBones();
            
            if (!Application.isPlaying)
            {
                ApplyPose(_pressPose);
            }
        }
    }
    
    private void ApplyPose(HandPose pose)
    {
        // Левая рука
        pose.Index.Apply(_leftIndexBone1, _leftIndexBone2, _leftIndexBone3);
        pose.Thumb.Apply(_leftThumbBone1, _leftThumbBone2, _leftThumbBone3);
        pose.Middle.Apply(_leftMiddleBone1, _leftMiddleBone2, _leftMiddleBone3);
        pose.Ring.Apply(_leftRingBone1, _leftRingBone2, _leftRingBone3);
        pose.Pinky.Apply(_leftPinkyBone1, _leftPinkyBone2, _leftPinkyBone3);
        
        // Правая рука
        pose.Index.Apply(_rightIndexBone1, _rightIndexBone2, _rightIndexBone3);
        pose.Thumb.Apply(_rightThumbBone1, _rightThumbBone2, _rightThumbBone3);
        pose.Middle.Apply(_rightMiddleBone1, _rightMiddleBone2, _rightMiddleBone3);
        pose.Ring.Apply(_rightRingBone1, _rightRingBone2, _rightRingBone3);
        pose.Pinky.Apply(_rightPinkyBone1, _rightPinkyBone2, _rightPinkyBone3);
    }
    
    [ContextMenu("Capture Rest Pose")]
    private void CaptureRestPose()
    {
        FindMixamoBones();
        // Пока только левая рука для примера
        _restPose.Index.Capture(_leftIndexBone1, _leftIndexBone2, _leftIndexBone3);
        Debug.Log("<color=green>Rest pose captured!</color>");
    }
    
    [ContextMenu("Capture Press Pose")]
    private void CapturePressPose()
    {
        FindMixamoBones();
        // Пока только левая рука для примера
        _pressPose.Index.Capture(_leftIndexBone1, _leftIndexBone2, _leftIndexBone3);
        Debug.Log("<color=green>Press pose captured!</color>");
    }
    
    [ContextMenu("Toggle Preview Press/Rest")]
    private void TogglePreview()
    {
        _leftHandState.IsPressingKey = !_leftHandState.IsPressingKey;
        OnValidate();
    }
}