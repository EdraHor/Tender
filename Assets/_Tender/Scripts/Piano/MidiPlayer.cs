using UnityEngine;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

[System.Serializable]
public class NoteInfo
{
    public int MidiNote;
    public float StartTime;
    public float Duration;
    public float Velocity;
}

public class MidiPlayer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PianoKeyboard _keyboard;
    
    [Header("MIDI File")]
    [SerializeField] private TextAsset _midiFile;
    
    [Header("Playback Settings")]
    [SerializeField] private float _playbackSpeed = 1f;
    
    [Header("Info")]
    [SerializeField] private int _totalNotes;
    [SerializeField] private float _durationSeconds;
    
    [SerializeField] private int[] _allowedChannels = { 0, 1 };
    
    private List<NoteInfo> _notesList = new List<NoteInfo>();
    public List<NoteInfo> NotesList => _notesList;
    public bool IsReady { get; private set; }
    private bool _isPlaying;
    
    private double _startDspTime;
    public float CurrentPlaybackTime { get; private set; }
    public float PlaybackSpeed => _playbackSpeed;
    public bool IsPlaying => _isPlaying;
    
    private void OnValidate()
    {
        if (_midiFile != null)
        {
            try
            {
                using (var stream = new MemoryStream(_midiFile.bytes))
                {
                    var midi = MidiFile.Read(stream);
                    var tempoMap = midi.GetTempoMap();
                    
                    var notes = midi.GetNotes().ToArray();
                    _totalNotes = notes.Length;
                    
                    if (notes.Length > 0)
                    {
                        var lastNote = notes.OrderBy(n => n.EndTime).Last();
                        _durationSeconds = (float)TimeConverter.ConvertTo<MetricTimeSpan>(
                            lastNote.EndTime, tempoMap).TotalSeconds;
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Ошибка чтения MIDI: {e.Message}");
            }
        }
    }
    
    private void Start()
    {
        if (_keyboard == null)
            _keyboard = GetComponent<PianoKeyboard>();
        
        if (_midiFile != null)
        {
            PrepareNotes();
            IsReady = true;
            StartCoroutine(PlayMidi());
        }
    }
    
    private void PrepareNotes()
    {
        _notesList.Clear();
    
        using (var stream = new MemoryStream(_midiFile.bytes))
        {
            var midi = MidiFile.Read(stream);
            var tempoMap = midi.GetTempoMap();
        
            var notes = midi.GetNotes()
                .Where(n => _allowedChannels.Contains(n.Channel))
                .OrderBy(n => n.Time)
                .ToList();
        
            foreach (var note in notes)
            {
                float startTime = (float)TimeConverter.ConvertTo<MetricTimeSpan>(
                    note.Time, tempoMap).TotalSeconds;
            
                float duration = (float)TimeConverter.ConvertTo<MetricTimeSpan>(
                    note.Length, tempoMap).TotalSeconds;
        
                _notesList.Add(new NoteInfo
                {
                    MidiNote = note.NoteNumber,
                    StartTime = startTime,
                    Duration = duration,
                    Velocity = note.Velocity / 127f
                });
            }
        }
    }
    
    private IEnumerator PlayMidi()
    {
        _isPlaying = true;
    
        Debug.Log($"Загружено {_notesList.Count} нот");
    
        _startDspTime = AudioSettings.dspTime + 0.5;
        int currentNoteIndex = 0;
    
        while (currentNoteIndex < _notesList.Count)
        {
            double currentDspTime = AudioSettings.dspTime;
            double currentPlaybackTime = (currentDspTime - _startDspTime) * _playbackSpeed;
            CurrentPlaybackTime = (float)currentPlaybackTime;
        
            while (currentNoteIndex < _notesList.Count)
            {
                var noteInfo = _notesList[currentNoteIndex];
    
                if (noteInfo.StartTime <= currentPlaybackTime)
                {
                    double scheduledDspTime = _startDspTime + (noteInfo.StartTime / _playbackSpeed);
        
                    // Если время уже прошло - играй сразу, но с поправкой
                    if (scheduledDspTime < AudioSettings.dspTime)
                    {
                        scheduledDspTime = AudioSettings.dspTime + 0.01; // Небольшой буфер
                    }
        
                    _keyboard.PressKeyScheduled(
                        noteInfo.MidiNote, 
                        noteInfo.Velocity, 
                        scheduledDspTime, 
                        noteInfo.Duration
                    );
        
                    currentNoteIndex++;
                }
                else
                {
                    break;
                }
            }
        
            yield return null;
        }
    
        _isPlaying = false;
    }
}