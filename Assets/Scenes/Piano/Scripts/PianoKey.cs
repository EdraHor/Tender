using UnityEngine;
using System.Collections;

[RequireComponent(typeof(BoxCollider))]
public class PianoKey : MonoBehaviour
{
    public int MidiNote { get; private set; }
    
    private float _maxRotation = 3f;
    private float _pressSpeed = 0.05f;
    private float _releaseSpeed = 0.1f;
    private float _fadeOutDuration = 0.1f;
    
    private AudioClip[] _velocityLayers;
    private float _pitchShift = 1f;
    private AudioSourcePool _audioPool;
    private AudioSource _currentSound;
    
    private BoxCollider _triggerCollider;
    private bool _isPressed;
    
    private Transform _visualTransform;
    private Quaternion _initialRotation;
    private Coroutine _animationCoroutine;
    
    public bool IsPressed => _isPressed;
    
    private void Awake()
    {
        _triggerCollider = GetComponent<BoxCollider>();
        _triggerCollider.isTrigger = true;
        
        _visualTransform = transform.childCount > 0 ? transform.GetChild(0) : CreateVisualChild();
        _initialRotation = _visualTransform.localRotation;
    }
    
    private Transform CreateVisualChild()
    {
        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;
        
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        
        if (meshFilter != null)
        {
            MeshFilter newMeshFilter = visual.AddComponent<MeshFilter>();
            newMeshFilter.sharedMesh = meshFilter.sharedMesh;
            DestroyImmediate(meshFilter);
        }
        
        if (meshRenderer != null)
        {
            MeshRenderer newMeshRenderer = visual.AddComponent<MeshRenderer>();
            newMeshRenderer.sharedMaterials = meshRenderer.sharedMaterials;
            DestroyImmediate(meshRenderer);
        }
        
        return visual.transform;
    }
    
    public void Initialize(int midiNote, AudioClip[] velocityLayers, float pitchShift, AudioSourcePool audioPool)
    {
        MidiNote = midiNote;
        _velocityLayers = velocityLayers;
        _pitchShift = pitchShift;
        _audioPool = audioPool;
    }
    
    public void SetBehaviorSettings(float maxRotation, float pressSpeed, float releaseSpeed, float fadeOutDuration)
    {
        _maxRotation = maxRotation;
        _pressSpeed = pressSpeed;
        _releaseSpeed = releaseSpeed;
        _fadeOutDuration = fadeOutDuration;
    }
    
    public void PlayNote(float velocity = 0.5f)
    {
        if (_velocityLayers == null || _velocityLayers.Length == 0 || _audioPool == null)
            return;

        float exactLayer = velocity * (_velocityLayers.Length - 1);
        int layerIndex = Mathf.RoundToInt(exactLayer);
        layerIndex = Mathf.Clamp(layerIndex, 0, _velocityLayers.Length - 1);

        if (_velocityLayers[layerIndex] != null)
        {
            _audioPool.Play(
                _velocityLayers[layerIndex],
                transform.position,
                _pitchShift,
                1f
            );
        }
    }
    
    public void PlayNoteScheduled(float velocity, double scheduledTime)
    {
        if (_velocityLayers == null || _velocityLayers.Length == 0 || _audioPool == null)
            return;

        float exactLayer = velocity * (_velocityLayers.Length - 1);
        int layerIndex = Mathf.RoundToInt(exactLayer);
        layerIndex = Mathf.Clamp(layerIndex, 0, _velocityLayers.Length - 1);

        if (_velocityLayers[layerIndex] != null)
        {
            _audioPool.PlayScheduled(
                _velocityLayers[layerIndex],
                transform.position,
                scheduledTime,
                _pitchShift,
                1f
            );
        }
    }
    
    public void StopNote()
    {
        if (_currentSound != null)
        {
            _audioPool.Stop(_currentSound, _fadeOutDuration);
            _currentSound = null;
        }
    }
    
    public void PressKeyManual(float velocity = 0.5f)
    {
        if (!_isPressed)
        {
            _isPressed = true;
            PlayNote(velocity);
            
            if (_animationCoroutine != null)
                StopCoroutine(_animationCoroutine);
            _animationCoroutine = StartCoroutine(AnimatePressOnly());
        }
    }
    
    public void ReleaseKeyManual()
    {
        if (_isPressed)
        {
            _isPressed = false;
            StopNote();
            
            if (_animationCoroutine != null)
                StopCoroutine(_animationCoroutine);
            _animationCoroutine = StartCoroutine(AnimateRelease());
        }
    }
    
    public void PressKey(float velocity = 0.5f, float duration = 0.3f)
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
        
        _isPressed = true;
        PlayNote(velocity);
        
        _animationCoroutine = StartCoroutine(AnimatePressAndRelease(duration));
    }
    
    public void AnimatePress(float duration = 0.3f)
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
    
        _isPressed = true;
        _animationCoroutine = StartCoroutine(AnimatePressAndRelease(duration));
    }
    
    private IEnumerator AnimatePressOnly()
    {
        Quaternion targetRotation = _initialRotation * Quaternion.Euler(0, -_maxRotation, 0);
        
        while (Quaternion.Angle(_visualTransform.localRotation, targetRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(
                _visualTransform.localRotation, 
                targetRotation, 
                _pressSpeed / Time.deltaTime
            );
            yield return null;
        }
        
        _visualTransform.localRotation = targetRotation;
    }
    
    private IEnumerator AnimateRelease()
    {
        while (Quaternion.Angle(_visualTransform.localRotation, _initialRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(
                _visualTransform.localRotation, 
                _initialRotation, 
                _releaseSpeed / Time.deltaTime
            );
            yield return null;
        }
        
        _visualTransform.localRotation = _initialRotation;
    }
    
    private IEnumerator AnimatePressAndRelease(float holdDuration)
    {
        Quaternion targetRotation = _initialRotation * Quaternion.Euler(0, -_maxRotation, 0);
        
        while (Quaternion.Angle(_visualTransform.localRotation, targetRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(
                _visualTransform.localRotation, 
                targetRotation, 
                _pressSpeed / Time.deltaTime
            );
            yield return null;
        }
        
        _visualTransform.localRotation = targetRotation;
        yield return new WaitForSeconds(holdDuration);
        
        _isPressed = false;
        
        while (Quaternion.Angle(_visualTransform.localRotation, _initialRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(
                _visualTransform.localRotation, 
                _initialRotation, 
                _releaseSpeed / Time.deltaTime
            );
            yield return null;
        }
        
        _visualTransform.localRotation = _initialRotation;
    }
}