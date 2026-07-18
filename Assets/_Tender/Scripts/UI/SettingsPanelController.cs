using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System;

/// <summary>
/// Переиспользуемый контроллер панели настроек (графика и звук)
/// Не MonoBehaviour - обычный C# класс для использования в разных меню
/// </summary>
public class SettingsPanelController
{
    private const string TAB_GRAPHICS = "GraphicsTab";
    private const string TAB_AUDIO = "AudioTab";
    
    private readonly VisualElement _root;
    private readonly VisualElement _panelRoot;
    private readonly MonoBehaviour _owner; // Для StartCoroutine
    
    private Label _settingsAppliedLabel;
    private Coroutine _fadeCoroutine;
    private string _currentTab = TAB_GRAPHICS;
    private float _lastTabSwitch;
    
    // Внешний доступ для обработки input
    public float LastTabSwitch => _lastTabSwitch;
    public void SetLastTabSwitch(float time) => _lastTabSwitch = time;
    public string CurrentTab => _currentTab;
    
    public SettingsPanelController(VisualElement panelRoot, VisualElement documentRoot, MonoBehaviour owner)
    {
        _panelRoot = panelRoot;
        _root = documentRoot;
        _owner = owner;
        
        SetupUI();
        SetupControls();
    }
    
    private void SetupUI()
    {
        // Tabs
        _panelRoot.Q<Button>("GraphicsTabButton").clicked += () => SwitchTab(TAB_GRAPHICS, "GraphicsTabButton");
        _panelRoot.Q<Button>("AudioTabButton").clicked += () => SwitchTab(TAB_AUDIO, "AudioTabButton");
        
        SetupGraphicsControls();
        SetupAudioControls();
        SetupSettingRowsInteract();
        
        // Label "Saved"
        _settingsAppliedLabel = new Label("✓ Настройки сохранены");
        _settingsAppliedLabel.AddToClassList("settings-applied-label");
        _settingsAppliedLabel.style.display = DisplayStyle.None;
        _panelRoot.Add(_settingsAppliedLabel);
    }
    
    private void SetupControls()
    {
        // Подписка на события сохранения
        if (G.Graphics != null) G.Graphics.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Audio != null) G.Audio.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Save != null) G.Save.OnSettingsLoaded += RefreshAllSettingsUI;
    }
    
    public void Cleanup()
    {
        // Безопасная отписка - проверяем что системы еще существуют
        // Важно: при смене сцен G может вернуть null без создания нового объекта
        try 
        {
            if (G.Graphics != null) 
                G.Graphics.OnSettingsApplied -= ShowSettingsAppliedMessage;
        } 
        catch { /* Игнорируем ошибки при уничтожении */ }
        
        try 
        {
            if (G.Audio != null) 
                G.Audio.OnSettingsApplied -= ShowSettingsAppliedMessage;
        } 
        catch { /* Игнорируем ошибки при уничтожении */ }
        
        try 
        {
            if (G.Save != null) 
                G.Save.OnSettingsLoaded -= RefreshAllSettingsUI;
        } 
        catch { /* Игнорируем ошибки при уничтожении */ }
    }
    
    public void SwitchTab(string tabName, string btnName)
    {
        _panelRoot.Q(TAB_GRAPHICS).AddToClassList("hidden");
        _panelRoot.Q(TAB_AUDIO).AddToClassList("hidden");
        _panelRoot.Q<Button>("GraphicsTabButton").RemoveFromClassList("tab-active");
        _panelRoot.Q<Button>("AudioTabButton").RemoveFromClassList("tab-active");
        
        _panelRoot.Q(tabName).RemoveFromClassList("hidden");
        _panelRoot.Q<Button>(btnName).AddToClassList("tab-active");
        
        _currentTab = tabName;
        _lastTabSwitch = Time.unscaledTime; // Используем unscaledTime для работы при паузе
    }
    
    public void HandleTabSwitchInput(float axisValue)
    {
        if (Mathf.Abs(axisValue) <= 0.5f) return;
        
        // Простая логика переключения для двух табов
        if (_currentTab == TAB_GRAPHICS) 
            SwitchTab(TAB_AUDIO, "AudioTabButton");
        else 
            SwitchTab(TAB_GRAPHICS, "GraphicsTabButton");
    }
    
    public void FocusFirstElement()
    {
        SwitchTab(TAB_GRAPHICS, "GraphicsTabButton");
        _panelRoot.Q<Button>("GraphicsTabButton")?.Focus();
    }
    
    private void SetupGraphicsControls()
    {
        var preset = _panelRoot.Q<SimpleDropdown>("QualityPreset");
        var names = new System.Collections.Generic.List<string>(G.Graphics.GetPresetNames());
        
        if (G.Graphics.GetCurrentPreset() == -1) names.Add("Пользовательский");
        
        preset.choices = names;
        preset.index = Mathf.Max(0, G.Graphics.GetCurrentPreset());
        
        preset.valueChanged += _ => {
            if (preset.index < G.Graphics.GetPresetNames().Length)
            {
                G.Graphics.ApplyPreset(preset.index);
                RefreshGraphicsUI();
            }
        };

        BindDropdown("ShadowQuality", 
            new System.Collections.Generic.List<string> { "Выключены", "Включены" },
            () => G.Graphics.GetShadowsEnabled() ? 1 : 0, 
            idx => { G.Graphics.SetShadowsEnabled(idx == 1); ForceCustomPreset(); });
            
        BindSlider("ShadowDistance", 
            () => G.Graphics.GetShadowDistance(), 
            val => { G.Graphics.SetShadowDistance(val); ForceCustomPreset(); });
            
        BindDropdown("ShadowResolution", 
            new System.Collections.Generic.List<string> { "512", "1024", "2048", "4096" },
            () => Mathf.Clamp(G.Graphics.GetShadowResolution(), 0, 3), 
            idx => { G.Graphics.SetShadowResolution(idx); ForceCustomPreset(); });
            
        var vsync = _panelRoot.Q<Toggle>("VSync");
        vsync.value = G.Graphics.GetVSync();
        vsync.RegisterValueChangedCallback(e => { G.Graphics.SetVSync(e.newValue); ForceCustomPreset(); });
    }
    
    private void SetupAudioControls()
    {
        if (G.Audio == null) return;
        BindSlider("MasterVolume", () => G.Audio.GetMasterVolume() * 100, v => G.Audio.SetMasterVolume(v / 100));
        BindSlider("MusicVolume", () => G.Audio.GetMusicVolume() * 100, v => G.Audio.SetMusicVolume(v / 100));
        BindSlider("VoiceVolume", () => G.Audio.GetVoiceVolume() * 100, v => G.Audio.SetVoiceVolume(v / 100));
        BindSlider("SFXVolume", () => G.Audio.GetSFXVolume() * 100, v => G.Audio.SetSFXVolume(v / 100));
    }
    
    private void BindSlider(string name, Func<float> getter, Action<float> setter)
    {
        var slider = _panelRoot.Q<Slider>(name);
        if (slider == null) return;
        slider.SetValueWithoutNotify(getter());
        slider.RegisterValueChangedCallback(e => setter(e.newValue));
    }
    
    private void BindDropdown(string name, System.Collections.Generic.List<string> choices, Func<int> getter, Action<int> setter)
    {
        var dd = _panelRoot.Q<SimpleDropdown>(name);
        if (dd == null) return;
        
        dd.choices = choices;
        dd.index = getter();
        
        dd.valueChanged += _ => setter(dd.index);
    }
    
    private void RefreshAllSettingsUI()
    {
        RefreshGraphicsUI();
        RefreshAudioUI();
    }
    
    private void RefreshGraphicsUI()
    {
        _panelRoot.Q<SimpleDropdown>("QualityPreset").index = Mathf.Max(0, G.Graphics.GetCurrentPreset());
        _panelRoot.Q<SimpleDropdown>("ShadowQuality").index = G.Graphics.GetShadowsEnabled() ? 1 : 0;
        _panelRoot.Q<Slider>("ShadowDistance").SetValueWithoutNotify(G.Graphics.GetShadowDistance());
        _panelRoot.Q<SimpleDropdown>("ShadowResolution").index = G.Graphics.GetShadowResolution();
        _panelRoot.Q<Toggle>("VSync").SetValueWithoutNotify(G.Graphics.GetVSync());
        
        bool shadowOn = G.Graphics.GetShadowsEnabled();
        _panelRoot.Q<Slider>("ShadowDistance").parent.SetEnabled(shadowOn);
        _panelRoot.Q<SimpleDropdown>("ShadowResolution").parent.SetEnabled(shadowOn);
    }
    
    private void RefreshAudioUI()
    {
        if (G.Audio == null) return;
        _panelRoot.Q<Slider>("MasterVolume")?.SetValueWithoutNotify(G.Audio.GetMasterVolume() * 100);
    }
    
    private void ForceCustomPreset()
    {
        var preset = _panelRoot.Q<SimpleDropdown>("QualityPreset");
        if (G.Graphics.GetCurrentPreset() == -1 && !preset.choices.Contains("Пользовательский"))
        {
            preset.choices.Add("Пользовательский");
            preset.SetValueWithoutNotify("Пользовательский");
        }
    }
    
    private void ShowSettingsAppliedMessage()
    {
        if (_settingsAppliedLabel == null) return;
        if (_fadeCoroutine != null) _owner.StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = _owner.StartCoroutine(FadeLabelRoutine());
    }
    
    private IEnumerator FadeLabelRoutine()
    {
        _settingsAppliedLabel.style.display = DisplayStyle.Flex;
        _settingsAppliedLabel.style.opacity = 1;
        yield return new WaitForSeconds(1.5f);
        float t = 0;
        while (t < 1f) {
            t += Time.deltaTime * 2;
            _settingsAppliedLabel.style.opacity = 1 - t;
            yield return null;
        }
        _settingsAppliedLabel.style.display = DisplayStyle.None;
    }
    
    private void SetupSettingRowsInteract()
    {
        _panelRoot.Query<VisualElement>(className: "setting-row").ForEach(row => {
            row.focusable = true;
            row.RegisterCallback<NavigationSubmitEvent>(evt => {
                var dd = row.Q<SimpleDropdown>();
                if (dd != null) { dd.OpenPopup(); evt.StopPropagation(); }
                var sl = row.Q<Slider>();
                if (sl != null) { sl.focusable = true; sl.Focus(); sl.RegisterCallback<BlurEvent>(_ => sl.focusable = false); evt.StopPropagation(); }
                var tg = row.Q<Toggle>();
                if (tg != null) { tg.value = !tg.value; evt.StopPropagation(); }
            });
        });
    }
}