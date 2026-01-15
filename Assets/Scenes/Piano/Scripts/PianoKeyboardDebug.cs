using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class PianoKeyboardDebug : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PianoKeyboard _keyboard;
    
    [Header("Display Settings")]
    [SerializeField] private Vector3 _textOffset = new Vector3(0, 0.02f, 0);
    [SerializeField] private float _textSize = 0.5f;
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _pressedColor = Color.yellow;
    
    [Header("Info")]
    [SerializeField] private int _totalKeysCount;
    
    private Dictionary<Transform, string> _noteNames = new Dictionary<Transform, string>();
    
    private void OnValidate()
    {
        if (_keyboard == null)
            _keyboard = GetComponent<PianoKeyboard>();
            
        UpdateKeyCount();
    }
    
    private void Awake()
    {
        if (_keyboard == null)
            _keyboard = GetComponent<PianoKeyboard>();
            
        CacheKeysAndNotes();
    }
    
    private void UpdateKeyCount()
    {
        if (_keyboard == null) return;
        
        int whiteCount = _keyboard.WhiteKeys != null ? _keyboard.WhiteKeys.Length : 0;
        int blackCount = _keyboard.BlackKeys != null ? _keyboard.BlackKeys.Length : 0;
        _totalKeysCount = whiteCount + blackCount;
    }
    
    private void CacheKeysAndNotes()
    {
        _noteNames.Clear();
        
        // Белые клавиши
        if (_keyboard.WhiteKeys != null)
        {
            int midiNote = 21; // A0
            for (int i = 0; i < _keyboard.WhiteKeys.Length; i++)
            {
                if (_keyboard.WhiteKeys[i] != null)
                {
                    _noteNames[_keyboard.WhiteKeys[i]] = MidiToNoteName(midiNote);
                }
                
                int noteInOctave = midiNote % 12;
                if (noteInOctave == 4 || noteInOctave == 11)
                    midiNote += 1;
                else
                    midiNote += 2;
            }
        }
        
        // Черные клавиши
        if (_keyboard.BlackKeys != null)
        {
            int midiNote = 22; // A#0
            for (int i = 0; i < _keyboard.BlackKeys.Length; i++)
            {
                if (_keyboard.BlackKeys[i] != null)
                {
                    _noteNames[_keyboard.BlackKeys[i]] = MidiToNoteName(midiNote);
                }
                
                int noteInOctave = midiNote % 12;
                if (noteInOctave == 10)
                    midiNote += 3;
                else if (noteInOctave == 3)
                    midiNote += 3;
                else
                    midiNote += 2;
            }
        }
    }
    
    private string MidiToNoteName(int midiNote)
    {
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int octave = (midiNote / 12) - 1;
        int note = midiNote % 12;
        return $"{noteNames[note]}{octave}";
    }
    
    private void OnDrawGizmos()
    {
        if (_keyboard == null || !enabled) return;
        
        if (Application.isPlaying)
        {
            DrawRuntimeDebug();
        }
        else
        {
            DrawEditorDebug();
        }
    }
    
    private void DrawRuntimeDebug()
    {
        if (_keyboard.WhiteKeys == null || _keyboard.BlackKeys == null) return;
        
        foreach (var key in _keyboard.WhiteKeys)
        {
            if (key == null) continue;
            DrawKeyDebug(key);
        }
        
        foreach (var key in _keyboard.BlackKeys)
        {
            if (key == null) continue;
            DrawKeyDebug(key);
        }
    }
    
    private void DrawEditorDebug()
    {
        // Белые клавиши
        if (_keyboard.WhiteKeys != null)
        {
            int midiNote = 21;
            foreach (var key in _keyboard.WhiteKeys)
            {
                if (key != null)
                {
                    DrawKeyDebugEditor(key, MidiToNoteName(midiNote), false);
                }
                
                int noteInOctave = midiNote % 12;
                if (noteInOctave == 4 || noteInOctave == 11)
                    midiNote += 1;
                else
                    midiNote += 2;
            }
        }
        
        // Черные клавиши
        if (_keyboard.BlackKeys != null)
        {
            int midiNote = 22;
            foreach (var key in _keyboard.BlackKeys)
            {
                if (key != null)
                {
                    DrawKeyDebugEditor(key, MidiToNoteName(midiNote), false);
                }
                
                int noteInOctave = midiNote % 12;
                if (noteInOctave == 10)
                    midiNote += 3;
                else if (noteInOctave == 3)
                    midiNote += 3;
                else
                    midiNote += 2;
            }
        }
    }
    
    private void DrawKeyDebug(Transform key)
    {
        var pianoKey = key.GetComponent<PianoKey>();
        if (pianoKey == null) return;
        
        string noteName;
        if (!_noteNames.TryGetValue(key, out noteName))
        {
            noteName = MidiToNoteName(pianoKey.MidiNote);
        }
        
        bool isPressed = pianoKey.IsPressed;
        
        DrawKeyDebugEditor(key, noteName, isPressed);
    }
    
    private void DrawKeyDebugEditor(Transform key, string noteName, bool isPressed)
    {
        Vector3 position = key.position + _textOffset;
        
#if UNITY_EDITOR
        GUIStyle style = new GUIStyle();
        style.normal.textColor = isPressed ? _pressedColor : _normalColor;
        style.fontSize = Mathf.RoundToInt(_textSize * 50);
        style.alignment = TextAnchor.MiddleCenter;
        style.fontStyle = FontStyle.Bold;
        
        Handles.Label(position, noteName, style);
#endif
    }
}