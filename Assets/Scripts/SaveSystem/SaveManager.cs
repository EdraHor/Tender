using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System;

public class SaveManager : MonoBehaviour
{
    private string SavePath => Application.isEditor 
        ? Path.Combine(Application.dataPath, "..", "Saves")
        : Application.persistentDataPath;
    
    private SettingsData settings = new SettingsData();
    
    [Header("Settings")]
    [SerializeField] private int currentSlot = 1;
    
    public SettingsData Settings => settings;
    public int CurrentSlot { get => currentSlot; set => currentSlot = value; }
    
    // --- СОБЫТИЯ ---
    public event Action<int> OnSaveStarted;
    public event Action<int> OnSaveCompleted;
    public event Action<int> OnLoadStarted;
    public event Action<int> OnLoadCompleted;
    public event Action OnSettingsLoaded;

    // --- DEBUG DATA FOR INSPECTOR ---
    [Header("Debug Tool")] 
    public SaveData inspectorSaveData; 

    private void Awake()
    {
        if (!Directory.Exists(SavePath))
            Directory.CreateDirectory(SavePath);
    }
    
    private void Start()
    {
        LoadSettings();
        
        if (G.Graphics != null)
            G.Graphics.OnSettingsApplied += SaveSettings;
            
        if (G.Audio != null)
            G.Audio.OnSettingsApplied += SaveSettings;
    }
    
    // --- ОСНОВНАЯ ЛОГИКА ---
    
    public void SaveGame(int slot)
    {
        OnSaveStarted?.Invoke(slot);
        
        var storage = GetStorage();
        if (storage == null) return;
        
        // Получаем актуальные данные
        var saveData = storage.GetSaveData();
        saveData.metadata.saveDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        saveData.metadata.playTime = Time.realtimeSinceStartup;
        
        // Обновляем инспектор для удобства, чтобы видеть что сохранилось
        inspectorSaveData = saveData; 

        string json = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(GetSavePath(slot), json);
        
        OnSaveCompleted?.Invoke(slot);
        Debug.Log($"✓ Saved slot {slot}");
    }
    
    public void LoadGame(int slot)
    {
        string path = GetSavePath(slot);
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Save {slot} не существует");
            return;
        }
        
        OnLoadStarted?.Invoke(slot);
        
        var storage = GetStorage();
        if (storage == null) return;
        
        string json = File.ReadAllText(path);
        var saveData = JsonUtility.FromJson<SaveData>(json);
        
        storage.LoadSaveData(saveData);
        currentSlot = slot;
        
        // Обновляем данные в инспекторе, чтобы видеть что загрузилось
        inspectorSaveData = saveData;
        
        OnLoadCompleted?.Invoke(slot);
        Debug.Log($"✓ Loaded slot {slot}");
    }

    // --- ИНСТРУМЕНТЫ ОТЛАДКИ (КОНТЕКСТНОЕ МЕНЮ) ---

    [ContextMenu("Fetch Current Game State")]
    public void DebugFetchFromGame()
    {
        var storage = GetStorage();
        if (storage != null)
        {
            inspectorSaveData = storage.GetSaveData();
            inspectorSaveData.metadata.playTime = Time.realtimeSinceStartup;
            inspectorSaveData.metadata.saveDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            Debug.Log("Saved data fetched from Game to Inspector.");
        }
    }

    [ContextMenu("Apply Inspector Data to Game")]
    public void DebugApplyToGame()
    {
        var storage = GetStorage();
        if (storage != null && inspectorSaveData != null)
        {
            storage.LoadSaveData(inspectorSaveData);
            Debug.Log("Inspector data applied to Game Logic.");
        }
    }

    [ContextMenu("Read File to Inspector (No Load)")]
    public void DebugReadFile()
    {
        string path = GetSavePath(currentSlot);
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            inspectorSaveData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log($"File (Slot {currentSlot}) loaded into Inspector.");
        }
        else
        {
            Debug.LogWarning($"File for slot {currentSlot} not found.");
        }
    }

    [ContextMenu("Write Inspector Data to File")]
    public void DebugWriteFile()
    {
        if (inspectorSaveData == null) return;

        // Обновляем мету перед записью
        inspectorSaveData.metadata.saveDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        
        string json = JsonUtility.ToJson(inspectorSaveData, true);
        File.WriteAllText(GetSavePath(currentSlot), json);
        Debug.Log($"Inspector data written to File (Slot {currentSlot}).");
    }

    // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---
    
    private SaveVariableStorage GetStorage()
    {
        var storage = G.Dialogue?.VariableStorage as SaveVariableStorage;
        if (storage == null) Debug.LogError("SaveVariableStorage не найден!");
        return storage;
    }

    public void SaveSettings()
    {
        if (G.Graphics != null)
        {
            settings.graphicsPreset = G.Graphics.GetCurrentPreset();
            settings.shadowDistance = G.Graphics.GetShadowDistance();
            settings.shadowResolution = G.Graphics.GetShadowResolution();
            settings.shadowCascades = G.Graphics.GetShadowCascades();
            settings.vSync = G.Graphics.GetVSync();
        }
        
        if (G.Audio != null)
        {
            settings.masterVolume = G.Audio.GetMasterVolume();
            settings.musicVolume = G.Audio.GetMusicVolume();
            settings.voiceVolume = G.Audio.GetVoiceVolume();
            settings.sfxVolume = G.Audio.GetSFXVolume();
        }
        string json = JsonUtility.ToJson(settings, true);
        File.WriteAllText(Path.Combine(SavePath, "settings.json"), json);
    }
    
    public void LoadSettings()
    {
        string path = Path.Combine(SavePath, "settings.json");
        if (!File.Exists(path))
        {
            OnSettingsLoaded?.Invoke();
            return;
        }
        
        string json = File.ReadAllText(path);
        settings = JsonUtility.FromJson<SettingsData>(json);
        
        if (G.Graphics != null)
        {
            if (settings.graphicsPreset >= 0)
                G.Graphics.ApplyPreset(settings.graphicsPreset);
            else
            {
                G.Graphics.SetShadowDistance(settings.shadowDistance, false);
                G.Graphics.SetShadowResolution(settings.shadowResolution, false);
                G.Graphics.SetShadowCascades(settings.shadowCascades, false);
                G.Graphics.SetVSync(settings.vSync, false);
            }
        }
        
        if (G.Audio != null)
        {
            G.Audio.LoadSettings(settings);
        }
        OnSettingsLoaded?.Invoke();
    }
    
    public SaveMetadata[] GetAllSaves()
    {
        var saves = new List<SaveMetadata>();
        for (int i = 1; i <= 10; i++)
        {
            string path = GetSavePath(i);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var save = JsonUtility.FromJson<SaveData>(json);
                    saves.Add(save.metadata);
                }
                catch { saves.Add(null); }
            }
            else saves.Add(null);
        }
        return saves.ToArray();
    }
    
    public bool SaveExists(int slot) => File.Exists(GetSavePath(slot));
    
    private string GetSavePath(int slot) => Path.Combine(SavePath, $"save{slot}.json");

    [ContextMenu("Reset All Saves")]
    private void DebugReset()
    {
        for (int i = 1; i <= 10; i++)
        {
            string path = GetSavePath(i);
            if (File.Exists(path)) File.Delete(path);
        }
        
        string settingsPath = Path.Combine(SavePath, "settings.json");
        if (File.Exists(settingsPath)) File.Delete(settingsPath);
            
        Debug.Log("✓ Reset complete");
    }
}