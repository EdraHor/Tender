using UnityEngine;
using System.Collections;

[RequireComponent(typeof(BoxCollider))]
public class PianoKey : MonoBehaviour
{
    public int MidiNote { get; private set; }
    
    [SerializeField] private float _maxRotation = 3f;
    [SerializeField] private float _pressSpeed = 0.05f;
    [SerializeField] private float _releaseSpeed = 0.1f;
    
    private AudioClip[] _velocityLayers;
    private AudioSource _audioSource;
    private BoxCollider _triggerCollider;
    private bool _isPressed;
    
    private Transform _visualTransform;
    private Quaternion _initialRotation;
    private Coroutine _animationCoroutine;
    
    public bool IsPressed => _isPressed;
    
    private void Awake()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1f;
        
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
    
    public void Initialize(int midiNote, AudioClip[] velocityLayers, float pitchShift = 1f)
    {
        MidiNote = midiNote;
        _velocityLayers = velocityLayers;
        _audioSource.pitch = pitchShift;
    }
    
    public void PlayNote(float velocity = 0.5f)
    {
        if (_velocityLayers == null || _velocityLayers.Length == 0)
        {
            Debug.LogWarning($"No audio clips for key {MidiNote}");
            return;
        }
        
        int layerIndex = Mathf.Clamp(Mathf.FloorToInt(velocity * _velocityLayers.Length), 0, _velocityLayers.Length - 1);
        
        if (_velocityLayers[layerIndex] != null)
        {
            _audioSource.PlayOneShot(_velocityLayers[layerIndex]);
        }
    }
    
    // Для ручного нажатия (PianoInteractor)
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
    
    // Для ручного отпускания (PianoInteractor)
    public void ReleaseKeyManual()
    {
        if (_isPressed)
        {
            _isPressed = false;
            
            if (_animationCoroutine != null)
                StopCoroutine(_animationCoroutine);
            _animationCoroutine = StartCoroutine(AnimateRelease());
        }
    }
    
    // Для MIDI проигрывания (автоматическое нажатие и отпускание)
    public void PressKey(float velocity = 0.5f, float duration = 0.3f)
    {
        if (!_isPressed)
        {
            _isPressed = true;
            PlayNote(velocity);
        
            if (_animationCoroutine != null)
                StopCoroutine(_animationCoroutine);
            _animationCoroutine = StartCoroutine(AnimatePressAndRelease(duration));
        }
    }
    
    private IEnumerator AnimatePressOnly()
    {
        Quaternion targetRotation = _initialRotation * Quaternion.Euler(0, -_maxRotation, 0);
        
        while (Quaternion.Angle(_visualTransform.localRotation, targetRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(_visualTransform.localRotation, targetRotation, _pressSpeed / Time.deltaTime);
            yield return null;
        }
        
        _visualTransform.localRotation = targetRotation;
    }
    
    private IEnumerator AnimateRelease()
    {
        while (Quaternion.Angle(_visualTransform.localRotation, _initialRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(_visualTransform.localRotation, _initialRotation, _releaseSpeed / Time.deltaTime);
            yield return null;
        }
        
        _visualTransform.localRotation = _initialRotation;
    }
    
    private IEnumerator AnimatePressAndRelease(float holdDuration)
    {
        Quaternion targetRotation = _initialRotation * Quaternion.Euler(0, -_maxRotation, 0);
    
        while (Quaternion.Angle(_visualTransform.localRotation, targetRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(_visualTransform.localRotation, targetRotation, _pressSpeed / Time.deltaTime);
            yield return null;
        }
    
        _visualTransform.localRotation = targetRotation;
        yield return new WaitForSeconds(holdDuration);
    
        _isPressed = false;
    
        while (Quaternion.Angle(_visualTransform.localRotation, _initialRotation) > 0.1f)
        {
            _visualTransform.localRotation = Quaternion.Lerp(_visualTransform.localRotation, _initialRotation, _releaseSpeed / Time.deltaTime);
            yield return null;
        }
    
        _visualTransform.localRotation = _initialRotation;
    }
}