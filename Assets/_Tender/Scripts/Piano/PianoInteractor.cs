using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class PianoInteractor : MonoBehaviour
{
    [Header("Velocity Settings")]
    [SerializeField] private bool _useManualVelocity = false;
    [SerializeField] [Range(0f, 1f)] private float _manualVelocity = 0.5f;
    [SerializeField] private float _velocitySensitivity = 1f; // Чувствительность расчета velocity
    
    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = true;
    
    private HashSet<PianoKey> _keysInside = new HashSet<PianoKey>();
    private HashSet<PianoKey> _pressedKeys = new HashSet<PianoKey>();
    
    private Vector3 _lastPosition;
    private float _currentVelocity;
    
    private void Start()
    {
        _lastPosition = transform.position;
    }
    
    private void Update()
    {
        // Вычисляем velocity от скорости движения
        if (!_useManualVelocity)
        {
            Vector3 delta = transform.position - _lastPosition;
            float speed = delta.magnitude / Time.deltaTime;
            
            // Нормализуем скорость в диапазон 0-1
            // Типичная скорость руки: 0.5-5 м/с, настраивается через _velocitySensitivity
            _currentVelocity = Mathf.Clamp01(speed * _velocitySensitivity);
            
            _lastPosition = transform.position;
        }
        else
        {
            _currentVelocity = _manualVelocity;
        }
        
        // Нажимаем клавиши которые внутри
        foreach (var key in _keysInside)
        {
            if (!_pressedKeys.Contains(key) && !key.IsPressed)
            {
                float velocity = _useManualVelocity ? _manualVelocity : _currentVelocity;
                key.PressKeyManual(velocity);
                _pressedKeys.Add(key);
                
                if (_showDebugInfo)
                {
                    Debug.Log($"Pressed key {key.MidiNote} with velocity {velocity:F2} (speed: {_currentVelocity:F2})");
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
}