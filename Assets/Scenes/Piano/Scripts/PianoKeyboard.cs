using UnityEngine;
using System.Collections.Generic;

public class PianoKeyboard : MonoBehaviour
{
    [Header("Keys Setup")]
    public Transform[] WhiteKeys;
    public Transform[] BlackKeys;
    
    [Header("Audio Settings")]
    [SerializeField] private string _samplesPath = "Audio/SalamanderPiano";
    [SerializeField] private bool _allowPitchShifting = true;
    
    [Header("Key Behavior Settings")]
    [SerializeField] private float _maxRotation = 3f;
    [SerializeField] private float _pressSpeed = 0.05f;
    [SerializeField] private float _releaseSpeed = 0.1f;
    [SerializeField] private float _fadeOutDuration = 0.05f;
    
    private Dictionary<int, PianoKey> _keysByMidiNote = new Dictionary<int, PianoKey>();
    private AudioSourcePool _audioPool;
    
    private void Awake()
    {
        GameObject poolObj = new GameObject("AudioSourcePool");
        poolObj.transform.SetParent(transform);
        _audioPool = poolObj.AddComponent<AudioSourcePool>();
    }
    
    private void Start()
    {
        InitializeKeys();
    }
    
    private void OnValidate()
    {
        if (Application.isPlaying && _keysByMidiNote.Count > 0)
        {
            foreach (var key in _keysByMidiNote.Values)
            {
                key.SetBehaviorSettings(_maxRotation, _pressSpeed, _releaseSpeed, _fadeOutDuration);
            }
        }
    }
    
    private void InitializeKeys()
    {
        int midiNote = 21;
        
        for (int i = 0; i < WhiteKeys.Length; i++)
        {
            float pitchShift = LoadVelocityLayersWithPitch(
                midiNote, 
                out AudioClip[] velocityLayers, 
                out bool isOriginalSample
            );
            
            var key = WhiteKeys[i].gameObject.AddComponent<PianoKey>();
            
            if (_allowPitchShifting || isOriginalSample)
            {
                key.Initialize(midiNote, velocityLayers, pitchShift, _audioPool);
            }
            else
            {
                key.Initialize(midiNote, null, pitchShift, _audioPool);
            }
            
            key.SetBehaviorSettings(_maxRotation, _pressSpeed, _releaseSpeed, _fadeOutDuration);
            
            _keysByMidiNote[midiNote] = key;
            
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 4 || noteInOctave == 11)
                midiNote += 1;
            else
                midiNote += 2;
        }
        
        midiNote = 22;
        
        for (int i = 0; i < BlackKeys.Length; i++)
        {
            float pitchShift = LoadVelocityLayersWithPitch(
                midiNote, 
                out AudioClip[] velocityLayers, 
                out bool isOriginalSample
            );
            
            var key = BlackKeys[i].gameObject.AddComponent<PianoKey>();
            
            if (_allowPitchShifting || isOriginalSample)
            {
                key.Initialize(midiNote, velocityLayers, pitchShift, _audioPool);
            }
            else
            {
                key.Initialize(midiNote, null, pitchShift, _audioPool);
            }
            
            key.SetBehaviorSettings(_maxRotation, _pressSpeed, _releaseSpeed, _fadeOutDuration);
            
            _keysByMidiNote[midiNote] = key;
            
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 10)
                midiNote += 3;
            else if (noteInOctave == 3)
                midiNote += 3;
            else
                midiNote += 2;
        }
        
        Debug.Log($"Initialized {_keysByMidiNote.Count} keys (Pitch: {(_allowPitchShifting ? "ON" : "OFF")})");
    }
    
    private float LoadVelocityLayersWithPitch(
        int midiNote, 
        out AudioClip[] velocityLayers, 
        out bool isOriginalSample)
    {
        int[] availableNotes = { 
            21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 
            51, 54, 57, 60, 63, 66, 69, 72, 75, 78, 
            81, 84, 87, 90, 93, 96, 99, 102, 105, 108 
        };
        
        int closestNote = availableNotes[0];
        int minDistance = Mathf.Abs(midiNote - closestNote);
        
        foreach (int note in availableNotes)
        {
            int distance = Mathf.Abs(midiNote - note);
            if (distance < minDistance)
            {
                minDistance = distance;
                closestNote = note;
            }
        }
        
        isOriginalSample = (midiNote == closestNote);
        float pitchShift = Mathf.Pow(2f, (midiNote - closestNote) / 12f);
        
        string noteName = MidiToNoteName(closestNote);
        List<AudioClip> layers = new List<AudioClip>();
        
        for (int v = 1; v <= 16; v++)
        {
            string clipPath = $"{_samplesPath}/{noteName}v{v}";
            AudioClip clip = Resources.Load<AudioClip>(clipPath);
            
            if (clip != null)
                layers.Add(clip);
        }
        
        if (layers.Count == 0)
        {
            Debug.LogWarning($"No clips for note {closestNote} ({noteName})");
        }
        
        velocityLayers = layers.ToArray();
        return pitchShift;
    }
    
    private string MidiToNoteName(int midiNote)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midiNote / 12) - 1;
        int note = midiNote % 12;
        return $"{noteNames[note]}{octave}";
    }
    
    public void PressKey(int midiNote, float velocity = 0.5f, float duration = 0.3f)
    {
        if (_keysByMidiNote.TryGetValue(midiNote, out PianoKey key))
        {
            key.PressKey(velocity, duration);
        }
    }
    
    public void PressKeyScheduled(int midiNote, float velocity, double scheduledTime, float duration = 0.3f)
    {
        if (_keysByMidiNote.TryGetValue(midiNote, out PianoKey key))
        {
            key.PlayNoteScheduled(velocity, scheduledTime);
            key.AnimatePress(duration);  // Только анимация, без звука
        }
    }
    
    public Transform GetKeyTransform(int midiNote)
    {
        if (_keysByMidiNote.TryGetValue(midiNote, out PianoKey key))
        {
            return key.transform;
        }
        return null;
    }
    
    public Dictionary<int, PianoKey> GetAllKeys()
    {
        return _keysByMidiNote;
    }
}