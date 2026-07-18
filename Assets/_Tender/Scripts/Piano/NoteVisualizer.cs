using UnityEngine;
using System.Collections.Generic;

public class NoteVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MidiPlayer _midiPlayer;
    [SerializeField] private PianoKeyboard _keyboard;
    
    [Header("Visualization Settings")]
    [SerializeField] private float _lookAheadTime = 2f;
    [SerializeField] private float _spawnDistance = 5f;
    [SerializeField] private float _despawnLineOffset = 0f; // На каком расстоянии ЗА клавишей нота полностью исчезает (0 = сразу)
    [SerializeField] private NoteDirection _noteDirection = NoteDirection.Forward;
    [SerializeField] private Material _noteMaterial;
    
    [Header("Note Appearance")]
    [SerializeField] private float _noteWidth = 0.8f;
    [SerializeField] private float _noteHeight = 0.2f;
    [SerializeField] private float _depthMultiplier = 1f;
    [SerializeField] private Color _whiteKeyNoteColor = Color.cyan;
    [SerializeField] private Color _blackKeyNoteColor = Color.magenta;
    
    [Header("Info")]
    [SerializeField] private float _noteSpeed;
    
    private List<VisualNote> _activeNotes = new List<VisualNote>();
    private int _nextNoteIndex = 0;
    private bool _isPlaying;
    
    public enum NoteDirection
    {
        Up,
        Down,
        Forward,
        Back,
        Left,
        Right
    }
    
    private class VisualNote
    {
        public GameObject GameObject;
        public int MidiNote;
        public float StartTime;
        public float Duration;
        public Vector3 InitialFrontEdgePosition; // ПЕРЕДНИЙ край в момент спавна
        public Vector3 KeyPosition;
        public float InitialDepth;
    }
    
    private void Start()
    {
        if (_midiPlayer == null)
            _midiPlayer = GetComponent<MidiPlayer>();
            
        if (_keyboard == null)
            _keyboard = GetComponent<PianoKeyboard>();
        
        StartCoroutine(WaitAndStart());
    }
    
    private System.Collections.IEnumerator WaitAndStart()
    {
        yield return new WaitForSeconds(0.1f);
        
        while (!_midiPlayer.IsReady)
        {
            yield return null;
        }
        
        // Скорость для отображения в инспекторе (в реальном времени)
        _noteSpeed = _spawnDistance / _lookAheadTime;
        
        Debug.Log($"Visualizer ready with {_midiPlayer.NotesList.Count} notes, visual speed: {_noteSpeed:F2} units/sec");
        
        _isPlaying = true;
    }
    
    private void Update()
    {
        if (!_isPlaying || _midiPlayer.NotesList.Count == 0) return;
        
        // Используем время из MidiPlayer (музыкальное время с учетом playback speed)
        float currentTime = _midiPlayer.CurrentPlaybackTime;
        
        SpawnUpcomingNotes(currentTime);
        UpdateActiveNotes(currentTime);
    }
    
    private Vector3 GetDirectionVector()
    {
        switch (_noteDirection)
        {
            case NoteDirection.Up: return Vector3.up;
            case NoteDirection.Down: return Vector3.down;
            case NoteDirection.Forward: return Vector3.forward;
            case NoteDirection.Back: return Vector3.back;
            case NoteDirection.Left: return Vector3.left;
            case NoteDirection.Right: return Vector3.right;
            default: return Vector3.up;
        }
    }
    
    private int GetDepthAxisIndex()
    {
        switch (_noteDirection)
        {
            case NoteDirection.Up:
            case NoteDirection.Down:
                return 1; // Y
            case NoteDirection.Forward:
            case NoteDirection.Back:
                return 2; // Z
            case NoteDirection.Left:
            case NoteDirection.Right:
                return 0; // X
            default:
                return 1;
        }
    }
    
    private void SpawnUpcomingNotes(float currentTime)
    {
        // Конвертируем _lookAheadTime (реальные секунды) в музыкальные секунды
        float musicalLookAhead = _lookAheadTime * _midiPlayer.PlaybackSpeed;
        float spawnTime = currentTime + musicalLookAhead;
        
        while (_nextNoteIndex < _midiPlayer.NotesList.Count)
        {
            var noteInfo = _midiPlayer.NotesList[_nextNoteIndex];
            
            if (noteInfo.StartTime <= spawnTime)
            {
                SpawnNote(noteInfo);
                _nextNoteIndex++;
            }
            else
            {
                break;
            }
        }
    }
    
    private void SpawnNote(NoteInfo noteInfo)
    {
        Transform keyTransform = _keyboard.GetKeyTransform(noteInfo.MidiNote);
        
        if (keyTransform == null)
        {
            Debug.LogWarning($"Key transform not found for MIDI note {noteInfo.MidiNote}");
            return;
        }
        
        GameObject noteObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        noteObj.name = $"Note_{noteInfo.MidiNote}";
        
        Destroy(noteObj.GetComponent<Collider>());
        
        // Глубина на основе длительности ноты
        // noteInfo.Duration - музыкальное время, конвертируем в расстояние через реальную скорость
        float visualSpeed = _spawnDistance / _lookAheadTime;
        float noteDuration = noteInfo.Duration / _midiPlayer.PlaybackSpeed; // конвертируем в реальное время
        float noteDepth = noteDuration * visualSpeed * _depthMultiplier;
        
        // Размеры ноты
        Vector3 scale = new Vector3(_noteWidth, _noteHeight, _noteWidth);
        int depthAxis = GetDepthAxisIndex();
        scale[depthAxis] = noteDepth;
        
        noteObj.transform.localScale = scale;
        
        var renderer = noteObj.GetComponent<Renderer>();
        if (_noteMaterial != null)
        {
            renderer.material = new Material(_noteMaterial);
        }
        
        bool isBlackKey = IsBlackKey(noteInfo.MidiNote);
        renderer.material.color = isBlackKey ? _blackKeyNoteColor : _whiteKeyNoteColor;
        
        Vector3 keyPosition = keyTransform.position;
        Vector3 directionVector = GetDirectionVector();
        
        // ПЕРЕДНИЙ край (который будет касаться клавиши) на расстоянии _spawnDistance
        Vector3 frontEdgePos = keyPosition + directionVector * _spawnDistance;
        
        // Центр куба на половину глубины дальше от клавиши
        Vector3 centerPos = frontEdgePos + directionVector * (noteDepth * 0.5f);
        
        noteObj.transform.position = centerPos;
        
        _activeNotes.Add(new VisualNote
        {
            GameObject = noteObj,
            MidiNote = noteInfo.MidiNote,
            StartTime = noteInfo.StartTime,
            Duration = noteInfo.Duration,
            InitialFrontEdgePosition = frontEdgePos,
            KeyPosition = keyPosition,
            InitialDepth = noteDepth
        });
    }
    
    private void UpdateActiveNotes(float currentTime)
    {
        Vector3 directionVector = GetDirectionVector();
        int depthAxis = GetDepthAxisIndex();
        
        // Конвертируем реальное время в музыкальное
        float musicalLookAhead = _lookAheadTime * _midiPlayer.PlaybackSpeed;
        
        for (int i = _activeNotes.Count - 1; i >= 0; i--)
        {
            var note = _activeNotes[i];
            
            // Время относительно момента когда ПЕРЕДНИЙ КРАЙ должен коснуться клавиши (музыкальное время)
            float timeFromStart = currentTime - note.StartTime;
            
            if (timeFromStart < -musicalLookAhead)
            {
                // Нота еще не появилась
                continue;
            }
            else if (timeFromStart < 0)
            {
                // ФАЗА 1: Передний край летит к клавише
                // timeFromStart от -musicalLookAhead до 0
                // Прогресс от 0 (только заспавнилась) до 1 (касается клавиши)
                float travelProgress = (timeFromStart + musicalLookAhead) / musicalLookAhead;
                float travelDistance = travelProgress * _spawnDistance;
                
                // Передний край двигается от начальной позиции к клавише
                Vector3 currentFrontEdge = note.InitialFrontEdgePosition - directionVector * travelDistance;
                
                // Центр на половину глубины дальше от клавиши чем передний край
                Vector3 centerPos = currentFrontEdge + directionVector * (note.InitialDepth * 0.5f);
                note.GameObject.transform.position = centerPos;
                
                // Полный размер
                Vector3 scale = new Vector3(_noteWidth, _noteHeight, _noteWidth);
                scale[depthAxis] = note.InitialDepth;
                note.GameObject.transform.localScale = scale;
            }
            else if (timeFromStart < note.Duration)
            {
                // ФАЗА 2: Передний край У клавиши, нота сужается с переднего края
                float effectiveDuration = note.Duration * _depthMultiplier;
                float shrinkProgress = timeFromStart / effectiveDuration;
    
                // Если нота закончилась, но еще не до конца сузилась - продолжаем сужать
                if (shrinkProgress > 1f)
                    shrinkProgress = 1f;
    
                float currentDepth = note.InitialDepth * (1f - shrinkProgress);
    
                if (currentDepth < 0.001f)
                {
                    Destroy(note.GameObject);
                    _activeNotes.RemoveAt(i);
                    continue;
                }
    
                Vector3 frontEdge = note.KeyPosition;
                Vector3 centerPos = frontEdge + directionVector * (currentDepth * 0.5f);
                note.GameObject.transform.position = centerPos;
    
                Vector3 scale = new Vector3(_noteWidth, _noteHeight, _noteWidth);
                scale[depthAxis] = currentDepth;
                note.GameObject.transform.localScale = scale;
            }
            else
            {
                // ФАЗА 3: Нота закончилась
                // Если despawnLineOffset близок к нулю - удаляем сразу
                if (_despawnLineOffset < 0.01f)
                {
                    Destroy(note.GameObject);
                    _activeNotes.RemoveAt(i);
                    continue;
                }
                
                // Иначе - нота уходит за клавишу на расстояние _despawnLineOffset
                float timeAfterEnd = timeFromStart - note.Duration;
                
                // Конвертируем _despawnLineOffset в музыкальное время
                float visualSpeed = _spawnDistance / _lookAheadTime;
                float despawnTimeInRealSeconds = _despawnLineOffset / visualSpeed;
                float despawnTimeInMusicalSeconds = despawnTimeInRealSeconds * _midiPlayer.PlaybackSpeed;
                
                if (timeAfterEnd >= despawnTimeInMusicalSeconds)
                {
                    // Полностью исчезла
                    Destroy(note.GameObject);
                    _activeNotes.RemoveAt(i);
                }
                else
                {
                    // Двигаем остаток за клавишу
                    float despawnProgress = timeAfterEnd / despawnTimeInMusicalSeconds;
                    float distancePastKey = despawnProgress * _despawnLineOffset;
                    
                    Vector3 position = note.KeyPosition - directionVector * distancePastKey;
                    note.GameObject.transform.position = position;
                    
                    // Нота уже сжалась до минимума, оставляем маленький кубик
                    Vector3 scale = new Vector3(_noteWidth, _noteHeight, _noteWidth);
                    scale[depthAxis] = 0.001f;
                    note.GameObject.transform.localScale = scale;
                }
            }
        }
    }
    
    private bool IsBlackKey(int midiNote)
    {
        int noteInOctave = midiNote % 12;
        return noteInOctave == 1 || noteInOctave == 3 || noteInOctave == 6 || noteInOctave == 8 || noteInOctave == 10;
    }
    
    private void OnDestroy()
    {
        foreach (var note in _activeNotes)
        {
            if (note.GameObject != null)
                Destroy(note.GameObject);
        }
        _activeNotes.Clear();
    }
}