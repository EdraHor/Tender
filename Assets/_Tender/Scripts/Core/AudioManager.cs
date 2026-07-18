using UnityEngine;
using UnityEngine.Audio;
using System;

public class AudioManager : MonoBehaviour
{
    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    
    [Header("Параметры миксера")]
    [SerializeField] private string masterVolumeParameter = "MasterVolume";
    [SerializeField] private string musicVolumeParameter = "MusicVolume";
    [SerializeField] private string voiceVolumeParameter = "VoiceVolume";
    [SerializeField] private string sfxVolumeParameter = "SFXVolume";
    
    private const float MIN_DB = -80f;
    private const float MAX_DB = 0f;
    
    public event Action OnSettingsApplied;
    
    private void Awake()
    {
        if (audioMixer == null)
        {
            Debug.LogError("Audio Mixer не назначен в AudioManager!");
            return;
        }
    }
    
    /// <summary>
    /// Устанавливает общую громкость (0-1)
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        SetVolume(masterVolumeParameter, volume);
        PlayerPrefs.SetFloat("MasterVolume", volume);
        OnSettingsApplied?.Invoke();
        Debug.Log($"✓ Общая громкость: {Mathf.Round(volume * 100)}%");
    }
    
    /// <summary>
    /// Устанавливает громкость музыки (0-1)
    /// </summary>
    public void SetMusicVolume(float volume)
    {
        SetVolume(musicVolumeParameter, volume);
        PlayerPrefs.SetFloat("MusicVolume", volume);
        OnSettingsApplied?.Invoke();
        Debug.Log($"✓ Музыка: {Mathf.Round(volume * 100)}%");
    }
    
    /// <summary>
    /// Устанавливает громкость озвучки (0-1)
    /// </summary>
    public void SetVoiceVolume(float volume)
    {
        SetVolume(voiceVolumeParameter, volume);
        PlayerPrefs.SetFloat("VoiceVolume", volume);
        OnSettingsApplied?.Invoke();
        Debug.Log($"✓ Озвучка: {Mathf.Round(volume * 100)}%");
    }
    
    /// <summary>
    /// Устанавливает громкость эффектов (0-1)
    /// </summary>
    public void SetSFXVolume(float volume)
    {
        SetVolume(sfxVolumeParameter, volume);
        PlayerPrefs.SetFloat("SFXVolume", volume);
        OnSettingsApplied?.Invoke();
        Debug.Log($"✓ Эффекты: {Mathf.Round(volume * 100)}%");
    }
    
    /// <summary>
    /// Загружает настройки звука из SaveManager
    /// </summary>
    public void LoadSettings(SettingsData settings)
    {
        if (audioMixer == null) return;
        
        SetVolume(masterVolumeParameter, settings.masterVolume);
        SetVolume(musicVolumeParameter, settings.musicVolume);
        SetVolume(voiceVolumeParameter, settings.voiceVolume);
        SetVolume(sfxVolumeParameter, settings.sfxVolume);
        
        Debug.Log($"✓ Загружены настройки звука: Master={settings.masterVolume:F2}, Music={settings.musicVolume:F2}, Voice={settings.voiceVolume:F2}, SFX={settings.sfxVolume:F2}");
    }
    
    // Getters (возвращают значения 0-1)
    public float GetMasterVolume() => GetVolume(masterVolumeParameter);
    public float GetMusicVolume() => GetVolume(musicVolumeParameter);
    public float GetVoiceVolume() => GetVolume(voiceVolumeParameter);
    public float GetSFXVolume() => GetVolume(sfxVolumeParameter);
    
    /// <summary>
    /// Конвертирует линейное значение (0-1) в децибелы и устанавливает в миксере
    /// </summary>
    private void SetVolume(string parameter, float linearVolume)
    {
        linearVolume = Mathf.Clamp01(linearVolume);
        
        // Если громкость 0, устанавливаем минимум (полная тишина)
        float db = linearVolume <= 0.0001f ? MIN_DB : Mathf.Lerp(MIN_DB, MAX_DB, linearVolume);
        
        audioMixer.SetFloat(parameter, db);
    }
    
    /// <summary>
    /// Получает значение из миксера и конвертирует в линейное (0-1)
    /// </summary>
    private float GetVolume(string parameter)
    {
        if (audioMixer.GetFloat(parameter, out float db))
        {
            // Если минимум - возвращаем 0
            if (db <= MIN_DB) return 0f;
            
            // Конвертируем обратно в линейное значение
            return Mathf.InverseLerp(MIN_DB, MAX_DB, db);
        }
        
        return 1f; // Значение по умолчанию
    }
    
    /// <summary>
    /// Сбрасывает все настройки звука к значениям по умолчанию
    /// </summary>
    public void ResetToDefaults()
    {
        SetMasterVolume(1f);
        SetMusicVolume(1f);
        SetVoiceVolume(1f);
        SetSFXVolume(1f);
        
        PlayerPrefs.DeleteKey("MasterVolume");
        PlayerPrefs.DeleteKey("MusicVolume");
        PlayerPrefs.DeleteKey("VoiceVolume");
        PlayerPrefs.DeleteKey("SFXVolume");
        
        Debug.Log("✓ Настройки звука сброшены");
    }
}
