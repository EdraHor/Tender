using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class PianoInteractor : MonoBehaviour
{
    [Header("Interaction Settings")]
    [SerializeField] [Range(0f, 1f)] private float _velocity = 0.5f;
    
    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = true;
    
    private HashSet<PianoKey> _keysInside = new HashSet<PianoKey>();
    private HashSet<PianoKey> _pressedKeys = new HashSet<PianoKey>();
    
    private void OnTriggerEnter(Collider other)
    {
        PianoKey key = other.GetComponent<PianoKey>();
        if (key != null)
        {
            _keysInside.Add(key);
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        PianoKey key = other.GetComponent<PianoKey>();
        if (key != null)
        {
            _keysInside.Remove(key);
        }
    }
    
    private void Update()
    {
        // Нажимаем клавиши которые внутри, но еще не нажаты
        foreach (var key in _keysInside)
        {
            if (!_pressedKeys.Contains(key) && !key.IsPressed)
            {
                key.PressKeyManual(_velocity);
                _pressedKeys.Add(key);
                
                if (_showDebugInfo)
                {
                    Debug.Log($"Pressed key {key.MidiNote} with velocity {_velocity:F2}");
                }
            }
        }
        
        // Отпускаем клавиши которые вышли из коллайдера
        _pressedKeys.RemoveWhere(key =>
        {
            if (!_keysInside.Contains(key))
            {
                key.ReleaseKeyManual();
                
                if (_showDebugInfo)
                {
                    Debug.Log($"Released key {key.MidiNote}");
                }
                return true;
            }
            return false;
        });
    }
}