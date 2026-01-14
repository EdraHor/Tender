using UnityEngine;
using System.Collections.Generic;

public class PianoKeyboard : MonoBehaviour
{
    [Header("Keys Setup")]
    [SerializeField] private Transform[] _whiteKeys;
    [SerializeField] private Transform[] _blackKeys;
    
    [Header("Audio Settings")]
    [SerializeField] private string _samplesPath = "Audio/SalamanderPiano";
    
    private Dictionary<int, PianoKey> _keysByMidiNote = new Dictionary<int, PianoKey>();
    
    private void Start()
    {
        InitializeKeys();
    }
    
    private void InitializeKeys()
    {
        // Белые клавиши: стандартное 88-клавишное пианино начинается с A0
        // A0, B0, C1, D1, E1, F1, G1, A1, B1, C2... до C8
        int midiNote = 21; // A0
        
        for (int i = 0; i < _whiteKeys.Length; i++)
        {
            AudioClip[] velocityLayers = LoadVelocityLayers(midiNote);
            float pitchShift = LoadVelocityLayersWithPitch(midiNote, out velocityLayers);
            
            var key = _whiteKeys[i].gameObject.AddComponent<PianoKey>();
            key.Initialize(midiNote, velocityLayers, pitchShift);
            _keysByMidiNote[midiNote] = key;
            
            // Переход к следующей белой клавише
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 4 || noteInOctave == 11) // E->F или B->C
                midiNote += 1;
            else
                midiNote += 2;
        }
        
        // Черные клавиши: A#0, C#1, D#1, F#1, G#1, A#1...
        midiNote = 22; // A#0
        
        for (int i = 0; i < _blackKeys.Length; i++)
        {
            AudioClip[] velocityLayers = LoadVelocityLayers(midiNote);
            
            var key = _blackKeys[i].gameObject.AddComponent<PianoKey>();
            key.Initialize(midiNote, velocityLayers);
            _keysByMidiNote[midiNote] = key;
            
            // Переход к следующей черной клавише (учитываем отсутствие E# и B#)
            int noteInOctave = midiNote % 12;
            if (noteInOctave == 10) // A# -> C#
                midiNote += 3;
            else if (noteInOctave == 3) // D# -> F#
                midiNote += 3;
            else
                midiNote += 2;
        }
        
        Debug.Log($"Initialized {_keysByMidiNote.Count} piano keys");
    }
    
    private float LoadVelocityLayersWithPitch(int midiNote, out AudioClip[] velocityLayers)
    {
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
    
        float pitchShift = Mathf.Pow(2f, (midiNote - closestNote) / 12f);
    
        string noteName = MidiToNoteName(closestNote);
        List<AudioClip> layers = new List<AudioClip>();
    
        for (int v = 1; v <= 16; v++)
        {
            string clipPath = $"{_samplesPath}/{noteName}v{v}";
            AudioClip clip = Resources.Load<AudioClip>(clipPath);
        
            if (clip != null)
            {
                layers.Add(clip);
            }
        }
    
        velocityLayers = layers.ToArray();
        return pitchShift;
    }
    
    private AudioClip[] LoadVelocityLayers(int midiNote)
    {
        // Ноты которые есть в Salamander (каждая 3-я)
        int[] availableNotes = { 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51, 54, 57, 60, 63, 66, 69, 72, 75, 78, 81, 84, 87, 90, 93, 96, 99, 102, 105, 108 };
    
        // Находим ближайшую доступную ноту
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
    
        float pitchShift = Mathf.Pow(2f, (midiNote - closestNote) / 12f);
    
        string noteName = MidiToNoteName(closestNote);
        List<AudioClip> layers = new List<AudioClip>();
    
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
            Debug.LogWarning($"No audio clips found for closest note {closestNote} ({noteName})");
        }
        else if (midiNote != closestNote)
        {
            Debug.Log($"MIDI {midiNote} using samples from {closestNote}, pitch shift: {pitchShift:F3}");
        }
    
        return layers.ToArray();
    }
    
    private string MidiToNoteName(int midiNote)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midiNote / 12) - 1;
        int note = midiNote % 12;
        return $"{noteNames[note]}{octave}";
    }
    
    public void PlayKey(int midiNote, float velocity = 0.5f)
    {
        if (_keysByMidiNote.TryGetValue(midiNote, out PianoKey key))
        {
            key.PlayNote(velocity);
        }
    }
}