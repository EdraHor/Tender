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
    
    private Dictionary<int, PianoKey> _keysByMidiNote = new Dictionary<int, PianoKey>();
    
    private void Start()
    {
        InitializeKeys();
    }
    
    private void InitializeKeys()
    {
        // Белые клавиши: A0, B0, C1, D1, E1, F1, G1...
        int midiNote = 21; // A0
        
        for (int i = 0; i < WhiteKeys.Length; i++)
        {
            float pitchShift = LoadVelocityLayersWithPitch(midiNote, out AudioClip[] velocityLayers, out bool isOriginalSample);
            
            var key = WhiteKeys[i].gameObject.AddComponent<PianoKey>();
            
            // Если pitch shifting отключен и это не оригинальный семпл - не даем звуки
            if (!_allowPitchShifting && !isOriginalSample)
            {
                key.Initialize(midiNote, null, pitchShift);
            }
            else
            {
                key.Initialize(midiNote, velocityLayers, pitchShift);
            }
            
            _keysByMidiNote[midiNote] = key;
            
            // Переход к следующей белой клавише
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 4 || noteInOctave == 11) // E->F или B->C
                midiNote += 1;
            else
                midiNote += 2;
        }
        
        // Черные клавиши: A#0, C#1, D#1, F#1, G#1...
        midiNote = 22; // A#0
        
        for (int i = 0; i < BlackKeys.Length; i++)
        {
            float pitchShift = LoadVelocityLayersWithPitch(midiNote, out AudioClip[] velocityLayers, out bool isOriginalSample);
            
            var key = BlackKeys[i].gameObject.AddComponent<PianoKey>();
            
            // Если pitch shifting отключен и это не оригинальный семпл - не даем звуки
            if (!_allowPitchShifting && !isOriginalSample)
            {
                key.Initialize(midiNote, null, pitchShift);
            }
            else
            {
                key.Initialize(midiNote, velocityLayers, pitchShift);
            }
            
            _keysByMidiNote[midiNote] = key;
            
            // Переход к следующей черной клавише
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 10) // A# -> C#
                midiNote += 3;
            else if (noteInOctave == 3) // D# -> F#
                midiNote += 3;
            else
                midiNote += 2;
        }
        
        Debug.Log($"Initialized {_keysByMidiNote.Count} piano keys (Pitch shifting: {(_allowPitchShifting ? "ON" : "OFF")})");
    }
    
    private float LoadVelocityLayersWithPitch(int midiNote, out AudioClip[] velocityLayers, out bool isOriginalSample)
    {
        // Доступные ноты в Salamander Piano (через малую терцию)
        int[] availableNotes = { 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51, 54, 57, 60, 63, 66, 69, 72, 75, 78, 81, 84, 87, 90, 93, 96, 99, 102, 105, 108 };
    
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
        
        // Вычисляем pitch shift для получения нужной ноты
        float pitchShift = Mathf.Pow(2f, (midiNote - closestNote) / 12f);
    
        string noteName = MidiToNoteName(closestNote);
        List<AudioClip> layers = new List<AudioClip>();
    
        // Загружаем все 16 velocity layers
        for (int v = 1; v <= 16; v++)
        {
            string clipPath = $"{_samplesPath}/{noteName}v{v}";
            AudioClip clip = Resources.Load<AudioClip>(clipPath);
        
            if (clip != null)
            {
                layers.Add(clip);
            }
        }
    
        if (layers.Count == 0)
        {
            Debug.LogWarning($"No audio clips found for note {closestNote} ({noteName})");
        }
        else if (!isOriginalSample)
        {
            Debug.Log($"MIDI {midiNote} using {layers.Count} layers from {closestNote}, pitch: {pitchShift:F3}x");
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