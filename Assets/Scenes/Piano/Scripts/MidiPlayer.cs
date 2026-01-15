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
    
    private List<NoteInfo> _notesList = new List<NoteInfo>();
    public List<NoteInfo> NotesList => _notesList;
    public bool IsReady { get; private set; }
    private bool _isPlaying;
    
    private float _startTime;
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
        
            var notes = midi.GetNotes().OrderBy(n => n.Time).ToList();
        
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
        
        // Читаем MIDI файл через MemoryStream
        MidiFile midi;
        using (var stream = new MemoryStream(_midiFile.bytes))
        {
            midi = MidiFile.Read(stream);
        }
        
        TempoMap tempoMap = midi.GetTempoMap();
        
        // Получаем все ноты и сортируем по времени
        var notes = midi.GetNotes()
            .OrderBy(n => n.Time)
            .ToList();
        
        Debug.Log($"Загружено {notes.Count} нот, длительность: {_durationSeconds:F1}с");
        
        _startTime = Time.time;
        int currentNoteIndex = 0;
        
        while (currentNoteIndex < notes.Count)
        {
            float currentTime = (Time.time - _startTime) * _playbackSpeed;
            CurrentPlaybackTime = currentTime; // Обновляем текущее время для визуализатора
            
            // Проигрываем все ноты которые должны звучать сейчас
            while (currentNoteIndex < notes.Count)
            {
                var note = notes[currentNoteIndex];
                
                // Конвертируем MIDI время в секунды
                float noteTimeSeconds = (float)TimeConverter.ConvertTo<MetricTimeSpan>(
                    note.Time, tempoMap).TotalSeconds;
                
                if (noteTimeSeconds <= currentTime)
                {
                    // Проигрываем ноту
                    int midiNote = note.NoteNumber;
                    float velocity = note.Velocity / 127f;
                    
                    float noteDuration = (float)TimeConverter.ConvertTo<MetricTimeSpan>(
                        note.Length, tempoMap).TotalSeconds;
    
                    _keyboard.PressKey(midiNote, velocity, noteDuration);
                    
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
        Debug.Log("MIDI воспроизведение завершено");
    }
}