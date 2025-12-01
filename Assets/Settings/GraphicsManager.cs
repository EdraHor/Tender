using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;

public class GraphicsManager : MonoBehaviour
{
    [Serializable]
    public class GraphicsPreset
    {
        public string name;
        public float shadowDistance;
        public int mainLightShadowResolution;
        public int additionalLightShadowResolution;
        public int shadowCascades;
        public bool vSync;
    }

    private static readonly int[] SHADOW_RESOLUTIONS = { 512, 1024, 2048, 4096 };
    private const int DEFAULT_PRESET_INDEX = 2;
    private const float MIN_SHADOW_DISTANCE = 75f;

    [Header("Пресеты")]
    [SerializeField] private GraphicsPreset[] presets = new GraphicsPreset[]
    {
        new GraphicsPreset { name = "Низкое", shadowDistance = 0f, mainLightShadowResolution = 512, additionalLightShadowResolution = 512, shadowCascades = 1, vSync = false },
        new GraphicsPreset { name = "Среднее", shadowDistance = 75f, mainLightShadowResolution = 1024, additionalLightShadowResolution = 512, shadowCascades = 2, vSync = false },
        new GraphicsPreset { name = "Высокое", shadowDistance = 150f, mainLightShadowResolution = 2048, additionalLightShadowResolution = 1024, shadowCascades = 2, vSync = true },
        new GraphicsPreset { name = "Ультра", shadowDistance = 250f, mainLightShadowResolution = 4096, additionalLightShadowResolution = 2048, shadowCascades = 4, vSync = true }
    };

    private UniversalRenderPipelineAsset urpAsset;
    private int currentPresetIndex = DEFAULT_PRESET_INDEX;
    
    public event Action OnSettingsApplied;
    
    private void Awake()
    {
        urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        
        if (urpAsset == null)
        {
            Debug.LogError("URP Asset не найден! Убедитесь, что используется Universal Render Pipeline.");
            return;
        }
    }

    public string[] GetPresetNames()
    {
        string[] names = new string[presets.Length];
        for (int i = 0; i < presets.Length; i++)
            names[i] = presets[i].name;
        return names;
    }

    public void ApplyPreset(int index)
    {
        if (urpAsset == null || index < 0 || index >= presets.Length) return;

        currentPresetIndex = index;
        var preset = presets[index];

        urpAsset.shadowDistance = preset.shadowDistance;
        urpAsset.shadowCascadeCount = preset.shadowCascades;
        urpAsset.mainLightShadowmapResolution = preset.mainLightShadowResolution;
        urpAsset.additionalLightsShadowmapResolution = preset.additionalLightShadowResolution;
        QualitySettings.vSyncCount = preset.vSync ? 1 : 0;

        SavePreset(index);
        Debug.Log($"✓ Применен пресет: {preset.name} | Дальность теней: {preset.shadowDistance} | Разрешение: {preset.mainLightShadowResolution}");
    }

    public void SetShadowsEnabled(bool enabled, bool markAsCustom = true)
    {
        if (urpAsset == null) return;
        
        urpAsset.shadowDistance = enabled ? MIN_SHADOW_DISTANCE : 0f;
        SaveSetting("ShadowDistance", urpAsset.shadowDistance, markAsCustom);
        Debug.Log($"✓ Тени: {(enabled ? "Включены" : "Выключены")}");
    }
    
    public void SetShadowDistance(float distance, bool markAsCustom = true)
    {
        if (urpAsset == null) return;
        
        urpAsset.shadowDistance = Mathf.Max(distance, 0f);
        SaveSetting("ShadowDistance", urpAsset.shadowDistance, markAsCustom);
        Debug.Log($"✓ Дальность теней: {urpAsset.shadowDistance}");
    }
    
    public void SetShadowResolution(int resolutionIndex, bool markAsCustom = true)
    {
        if (urpAsset == null) return;
        
        resolutionIndex = Mathf.Clamp(resolutionIndex, 0, SHADOW_RESOLUTIONS.Length - 1);
        int resolution = SHADOW_RESOLUTIONS[resolutionIndex];
        
        urpAsset.mainLightShadowmapResolution = resolution;
        urpAsset.additionalLightsShadowmapResolution = Mathf.Max(512, resolution / 2);
        
        SaveSetting("ShadowResolution", resolutionIndex, markAsCustom);
        Debug.Log($"✓ Разрешение теней: {resolution}");
    }

    public void SetShadowCascades(int cascades, bool markAsCustom = true)
    {
        if (urpAsset == null) return;
        
        if (cascades != 1 && cascades != 2 && cascades != 4)
            cascades = 2;
        
        urpAsset.shadowCascadeCount = cascades;
        SaveSetting("ShadowCascades", cascades, markAsCustom);
        Debug.Log($"✓ Каскады теней: {cascades}");
    }
    
    public void SetVSync(bool enabled, bool markAsCustom = true)
    {
        QualitySettings.vSyncCount = enabled ? 1 : 0;
        SaveSetting("VSync", enabled ? 1 : 0, markAsCustom);
        Debug.Log($"✓ VSync: {(enabled ? "Включен" : "Выключен")}");
    }
    
    // Getters
    public int GetCurrentPreset() => currentPresetIndex;
    public bool GetShadowsEnabled() => urpAsset != null && urpAsset.shadowDistance > 0f;
    public float GetShadowDistance() => urpAsset?.shadowDistance ?? 50f;
    public bool GetVSync() => QualitySettings.vSyncCount > 0;
    
    public int GetShadowResolution()
    {
        if (urpAsset == null) return 1;
        int resolution = urpAsset.mainLightShadowmapResolution;
        
        for (int i = 0; i < SHADOW_RESOLUTIONS.Length; i++)
            if (resolution <= SHADOW_RESOLUTIONS[i])
                return i;
        
        return SHADOW_RESOLUTIONS.Length - 1;
    }

    public int GetShadowCascades() => urpAsset?.shadowCascadeCount ?? 2;
    
    public void LoadSettings()
    {
        int savedPreset = PlayerPrefs.GetInt("GraphicsPreset", DEFAULT_PRESET_INDEX);
        
        if (savedPreset >= 0 && savedPreset < presets.Length)
        {
            ApplyPreset(savedPreset);
        }
        else
        {
            currentPresetIndex = -1;
            SetShadowDistance(PlayerPrefs.GetFloat("ShadowDistance", 150f), false);
            SetShadowResolution(PlayerPrefs.GetInt("ShadowResolution", 2), false);
            SetShadowCascades(PlayerPrefs.GetInt("ShadowCascades", 2), false);
            SetVSync(PlayerPrefs.GetInt("VSync", 1) == 1, false);
        }
    }

    private void SaveSetting<T>(string key, T value, bool markAsCustom)
    {
        if (markAsCustom)
        {
            currentPresetIndex = -1;
            PlayerPrefs.SetInt("GraphicsPreset", -1);
        }
        
        if (value is float f) PlayerPrefs.SetFloat(key, f);
        else if (value is int i) PlayerPrefs.SetInt(key, i);
        
        OnSettingsApplied?.Invoke();
    }

    private void SavePreset(int index)
    {
        PlayerPrefs.SetInt("GraphicsPreset", index);
        SaveCurrentSettings();
        OnSettingsApplied?.Invoke();
    }

    private void SaveCurrentSettings()
    {
        PlayerPrefs.SetFloat("ShadowDistance", GetShadowDistance());
        PlayerPrefs.SetInt("ShadowResolution", GetShadowResolution());
        PlayerPrefs.SetInt("ShadowCascades", GetShadowCascades());
        PlayerPrefs.SetInt("VSync", GetVSync() ? 1 : 0);
    }
    
    public void SaveSettings()
    {
        SaveCurrentSettings();
        PlayerPrefs.Save();
    }
    
    public void ResetToDefaults()
    {
        PlayerPrefs.DeleteAll();
        ApplyPreset(DEFAULT_PRESET_INDEX);
        SaveSettings();
    }
}