using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections;

public class MainMenuController : MonoBehaviour
{
    private const string PANEL_MENU = "MenuButtons";
    private const string PANEL_LOAD = "LoadGamePanel";
    private const string PANEL_SETTINGS = "SettingsPanel";
    private const string TAB_GRAPHICS = "GraphicsTab";
    private const string TAB_AUDIO = "AudioTab";
    private const float TAB_SWITCH_COOLDOWN = 0.3f;
    
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private Texture2D characterTexture;
    
    private UIDocument _document;
    private VisualElement _root;
    private GraphicsManager _graphics;
    private Label _settingsAppliedLabel;
    private Coroutine _fadeCoroutine;
    
    private string _currentPanel = PANEL_MENU;
    private int _currentTabIndex;
    private float _lastTabSwitch;
    private bool _mouseJustMoved;
    
    private readonly string[] _tabs = { TAB_GRAPHICS, TAB_AUDIO };
    private readonly string[] _tabButtons = { "GraphicsTabButton", "AudioTabButton" };
    
    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _graphics = GetComponent<GraphicsManager>();
    }

    private void OnEnable() 
    {
        if (_document == null || _graphics == null)
        {
            _document = GetComponent<UIDocument>();
            _graphics = GetComponent<GraphicsManager>();
        }
        
        _root = _document.rootVisualElement;
        SetupUI();
        
        if (characterTexture != null)
            _root.Q<VisualElement>("CharacterImage").style.backgroundImage = new StyleBackground(characterTexture);
        
        _graphics.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Audio != null)
            G.Audio.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Save != null)
            G.Save.OnSettingsLoaded += RefreshAllUI;
        StartCoroutine(EnableUIDelayed());
    }
    
    private IEnumerator EnableUIDelayed()
    {
        yield return null;
        G.Input?.EnableUI();
    }

    private void OnDisable()
    {
        if (_graphics != null)
            _graphics.OnSettingsApplied -= ShowSettingsAppliedMessage;
        if (G.Audio != null)
            G.Audio.OnSettingsApplied -= ShowSettingsAppliedMessage;
        if (G.Save != null)
            G.Save.OnSettingsLoaded -= RefreshAllUI;
    }

    private void Update()
    {
        if (G.Input == null) return;
        
        var openDropdown = _root.Query<SimpleDropdown>().Where(d => d.IsOpen).First();
        
        if (G.Input.UI.Cancel.WasPressedThisFrame())
        {
            if (HandleDropdownCancel(openDropdown)) return;
            if (HandleControlCancel()) return;
            HandleBack();
        }
        
        if (openDropdown == null && _currentPanel == PANEL_SETTINGS && Time.time - _lastTabSwitch > TAB_SWITCH_COOLDOWN)
            HandleTabSwitch();
    }
    
    private bool HandleDropdownCancel(SimpleDropdown openDropdown)
    {
        var focused = _root.focusController.focusedElement;
        
        if (focused is Button button && button.parent?.ClassListContains("simple-dropdown__popup") == true)
        {
            if (openDropdown != null)
            {
                openDropdown.ClosePopup();
                openDropdown.Focus();
                return true;
            }
        }
        
        if (focused is SimpleDropdown dropdown && dropdown.IsOpen)
        {
            dropdown.ClosePopup();
            return true;
        }
        
        return false;
    }
    
    private bool HandleControlCancel()
    {
        var focused = _root.focusController.focusedElement;
        
        if (focused is Slider || focused is Toggle)
        {
            var row = (focused as VisualElement)?.parent;
            if (row?.ClassListContains("setting-row") == true)
            {
                row.Focus();
                return true;
            }
        }
        
        return false;
    }
    
    private void HandleTabSwitch()
    {
        float tabSwitch = G.Input.UI.TabSwitch.ReadValue<float>();
        if (Mathf.Abs(tabSwitch) <= 0.5f) return;
        
        _lastTabSwitch = Time.time;
        
        if (tabSwitch > 0)
            _currentTabIndex = (_currentTabIndex + 1) % _tabs.Length;
        else
            _currentTabIndex = (_currentTabIndex - 1 + _tabs.Length) % _tabs.Length;
        
        SwitchTab(_tabs[_currentTabIndex], _tabButtons[_currentTabIndex]);
    }

    private void HandleBack()
    {
        if (_currentPanel == PANEL_LOAD || _currentPanel == PANEL_SETTINGS)
        {
            if (_currentPanel == PANEL_SETTINGS)
                _graphics.SaveSettings();
            ShowPanel(PANEL_MENU);
        }
    }

    private void SetupUI()
    {
        _root.Q<Button>("NewGameButton").clicked += () => SceneManager.LoadScene(gameSceneName);
        _root.Q<Button>("LoadGameButton").clicked += () => ShowPanel(PANEL_LOAD);
        _root.Q<Button>("SettingsButton").clicked += () => ShowPanel(PANEL_SETTINGS);
        _root.Q<Button>("ExitButton").clicked += () => {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        };
        
        _root.Q<Button>("BackFromLoadButton").clicked += () => ShowPanel(PANEL_MENU);
        _root.Q<Button>("BackFromSettingsButton").clicked += () => {
            _graphics.SaveSettings();
            ShowPanel(PANEL_MENU);
        };
        
        _root.Q<Button>("GraphicsTabButton").clicked += () => { _currentTabIndex = 0; SwitchTab(_tabs[0], _tabButtons[0]); };
        _root.Q<Button>("AudioTabButton").clicked += () => { _currentTabIndex = 1; SwitchTab(_tabs[1], _tabButtons[1]); };
        
        SetupGraphics();
        SetupAudio();
        SetupSettingsAppliedLabel();
        SetupSettingRows();
        
        _root.Query<Slider>().ForEach(s => s.focusable = false);
        _root.Query<Toggle>().ForEach(t => t.focusable = false);
        _root.Query<SimpleDropdown>().ForEach(d => d.focusable = false);
        
        SetupDropdownIsolation();
        SetupInputModeHandling();
    }
    
    private void SetupDropdownIsolation()
    {
        _root.RegisterCallback<NavigationMoveEvent>(evt => {
            var openDropdown = _root.Query<SimpleDropdown>().Where(d => d.IsOpen).First();
            if (openDropdown == null) return;
            
            var target = evt.target as VisualElement;
            if (target == null) return;
            
            var popup = target.parent;
            if (popup?.ClassListContains("simple-dropdown__popup") != true)
            {
                evt.PreventDefault();
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);
    }
    
    private void SetupInputModeHandling()
    {
        // Keyboard mode
        _root.RegisterCallback<NavigationMoveEvent>(evt => {
            if (!_root.ClassListContains("keyboard-mode"))
            {
                _root.AddToClassList("keyboard-mode");
                _mouseJustMoved = false;
                _root.Query<Button>().ForEach(btn => btn.RemoveFromClassList("hovered"));
            }
        }, TrickleDown.TrickleDown);
        
        // Mouse mode
        _root.RegisterCallback<MouseMoveEvent>(evt => {
            _mouseJustMoved = true;
            
            if (_root.ClassListContains("keyboard-mode"))
            {
                _root.RemoveFromClassList("keyboard-mode");
                
                var focused = _root.focusController?.focusedElement;
                if (focused != null && !(focused is Slider) && !(focused is Toggle))
                    focused.Blur();
            }
        }, TrickleDown.TrickleDown);
        
        // Block middle/right mouse
        _root.RegisterCallback<PointerDownEvent>(evt => {
            if (evt.button == (int)MouseButton.MiddleMouse || evt.button == (int)MouseButton.RightMouse)
            {
                evt.PreventDefault();
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);
        
        // Setup hover for all button groups
        SetupButtonHover(_root.Q(PANEL_MENU), "menu-button");
        SetupButtonHover(_root.Q(PANEL_LOAD));
        SetupButtonHover(_root.Q<Button>("BackFromSettingsButton"));
        SetupButtonHover(null, "tab-button");
    }
    
    private void SetupButtonHover(VisualElement container, string className = null)
    {
        if (container == null && className == null) return;
        
        var query = className != null ? _root.Query<Button>(className: className) : container.Query<Button>();
        
        query.ForEach(btn => {
            btn.RegisterCallback<MouseEnterEvent>(evt => {
                var button = evt.target as Button;
                if (button == null) return;
                
                if (!_root.ClassListContains("keyboard-mode"))
                {
                    button.AddToClassList("hovered");
                }
                
                if (_mouseJustMoved && _root.ClassListContains("keyboard-mode"))
                {
                    _root.RemoveFromClassList("keyboard-mode");
                    button.AddToClassList("hovered");
                    
                    var focused = _root.focusController?.focusedElement;
                    if (focused != null && focused != button)
                        focused.Blur();
                }
            });
            
            btn.RegisterCallback<MouseLeaveEvent>(evt => {
                (evt.target as Button)?.RemoveFromClassList("hovered");
            });
        });
    }
    
    private void SetupSettingRows()
    {
        _root.Query<VisualElement>(className: "setting-row").ForEach(row => {
            row.focusable = true;
        
            row.RegisterCallback<NavigationSubmitEvent>(evt => {
                var dropdown = row.Q<SimpleDropdown>();
                if (dropdown != null) 
                { 
                    dropdown.OpenPopup();
                    evt.StopPropagation(); 
                    return; 
                }
            
                var slider = row.Q<Slider>();
                if (slider != null) 
                { 
                    slider.focusable = true;
                    slider.Focus();
                    slider.RegisterCallback<BlurEvent>(e => slider.focusable = false, TrickleDown.TrickleDown);
                    evt.StopPropagation(); 
                    return; 
                }
            
                var toggle = row.Q<Toggle>();
                if (toggle != null) { toggle.value = !toggle.value; evt.StopPropagation(); }
            });
        });
    }

    private void SetupSettingsAppliedLabel()
    {
        _settingsAppliedLabel = new Label("✓ Настройки применены");
        _settingsAppliedLabel.AddToClassList("settings-applied-label");
        _settingsAppliedLabel.style.display = DisplayStyle.None;
        _root.Q(PANEL_SETTINGS).Add(_settingsAppliedLabel);
    }

    private void ShowSettingsAppliedMessage()
    {
        if (_settingsAppliedLabel == null) return;
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeLabel());
    }

    private IEnumerator FadeLabel()
    {
        _settingsAppliedLabel.style.display = DisplayStyle.Flex;
        
        float t = 0;
        while (t < 0.3f)
        {
            _settingsAppliedLabel.style.opacity = Mathf.Lerp(0, 1, t / 0.3f);
            t += Time.deltaTime;
            yield return null;
        }
        _settingsAppliedLabel.style.opacity = 1;
        
        yield return new WaitForSeconds(1.5f);
        
        t = 0;
        while (t < 0.3f)
        {
            _settingsAppliedLabel.style.opacity = Mathf.Lerp(1, 0, t / 0.3f);
            t += Time.deltaTime;
            yield return null;
        }
        
        _settingsAppliedLabel.style.display = DisplayStyle.None;
    }

    private void SetupGraphics()
    {
        var preset = _root.Q<SimpleDropdown>("QualityPreset");
        var presetNames = new System.Collections.Generic.List<string>(_graphics.GetPresetNames());
        
        int currentPreset = _graphics.GetCurrentPreset();
        if (currentPreset == -1)
            presetNames.Add("Пользовательские");
        
        preset.choices = presetNames;
        preset.index = currentPreset >= 0 ? currentPreset : presetNames.Count - 1;
        
        preset.valueChanged += e => {
            if (preset.index < _graphics.GetPresetNames().Length)
            {
                _graphics.ApplyPreset(preset.index);
                RefreshGraphicsUI();
                UpdateShadowControlsState();
            }
        };
        
        var shadowQuality = _root.Q<SimpleDropdown>("ShadowQuality");
        shadowQuality.choices = new System.Collections.Generic.List<string> { "Выключены", "Включены" };
        shadowQuality.index = _graphics.GetShadowsEnabled() ? 1 : 0;
        shadowQuality.valueChanged += e => {
            _graphics.SetShadowsEnabled(shadowQuality.index == 1);
            UpdatePresetToCustom();
            UpdateShadowControlsState();
        };
        
        var shadowDist = _root.Q<Slider>("ShadowDistance");
        shadowDist.value = _graphics.GetShadowDistance();
        shadowDist.RegisterValueChangedCallback(e => {
            _graphics.SetShadowDistance(e.newValue);
            UpdatePresetToCustom();
        });
        
        var shadowRes = _root.Q<SimpleDropdown>("ShadowResolution");
        shadowRes.choices = new System.Collections.Generic.List<string> { "512", "1024", "2048", "4096" };
        shadowRes.index = Mathf.Clamp(_graphics.GetShadowResolution(), 0, 3);
        shadowRes.valueChanged += e => {
            _graphics.SetShadowResolution(shadowRes.index);
            UpdatePresetToCustom();
        };
        
        var vsync = _root.Q<Toggle>("VSync");
        vsync.value = _graphics.GetVSync();
        vsync.RegisterValueChangedCallback(e => {
            _graphics.SetVSync(e.newValue);
            UpdatePresetToCustom();
        });
        
        UpdateShadowControlsState();
    }
    
    private void UpdateShadowControlsState()
    {
        bool enabled = _graphics.GetShadowsEnabled();
        
        var shadowDist = _root.Q<Slider>("ShadowDistance");
        var shadowRes = _root.Q<SimpleDropdown>("ShadowResolution");
        
        shadowDist.SetEnabled(enabled);
        shadowRes.SetEnabled(enabled);
        
        if (enabled)
        {
            shadowDist.parent?.RemoveFromClassList("setting-disabled");
            shadowRes.parent?.RemoveFromClassList("setting-disabled");
        }
        else
        {
            shadowDist.parent?.AddToClassList("setting-disabled");
            shadowRes.parent?.AddToClassList("setting-disabled");
        }
    }

    private void RefreshGraphicsUI()
    {
        _root.Q<SimpleDropdown>("ShadowQuality").SetValueWithoutNotify(_graphics.GetShadowsEnabled() ? "Включены" : "Выключены");
        _root.Q<Slider>("ShadowDistance").SetValueWithoutNotify(_graphics.GetShadowDistance());
        _root.Q<SimpleDropdown>("ShadowResolution").index = _graphics.GetShadowResolution();
        _root.Q<Toggle>("VSync").SetValueWithoutNotify(_graphics.GetVSync());
    }

    private void RefreshAudioUI()
    {
        if (G.Audio == null) return;

        _root.Q<Slider>("MasterVolume")?.SetValueWithoutNotify(G.Audio.GetMasterVolume() * 100f);
        _root.Q<Slider>("MusicVolume")?.SetValueWithoutNotify(G.Audio.GetMusicVolume() * 100f);
    }

    private void RefreshAllUI()
    {
        RefreshGraphicsUI();
        RefreshAudioUI();
        UpdateShadowControlsState();
        
        // Обновляем пресет
        var preset = _root.Q<SimpleDropdown>("QualityPreset");
        int currentPreset = _graphics.GetCurrentPreset();
        if (currentPreset >= 0)
            preset.SetValueWithoutNotify(preset.choices[currentPreset]);
        _root.Q<Slider>("VoiceVolume")?.SetValueWithoutNotify(G.Audio.GetVoiceVolume() * 100f);
        _root.Q<Slider>("SFXVolume")?.SetValueWithoutNotify(G.Audio.GetSFXVolume() * 100f);
    }

    private void UpdatePresetToCustom()
    {
        var preset = _root.Q<SimpleDropdown>("QualityPreset");
        
        if (_graphics.GetCurrentPreset() == -1)
        {
            if (!preset.choices.Contains("Пользовательские"))
                preset.choices = new System.Collections.Generic.List<string>(_graphics.GetPresetNames()) { "Пользовательские" };
            preset.SetValueWithoutNotify("Пользовательские");
        }
    }

    private void SetupAudio()
    {
        if (G.Audio == null) return;
        
        var masterSlider = _root.Q<Slider>("MasterVolume");
        var musicSlider = _root.Q<Slider>("MusicVolume");
        var voiceSlider = _root.Q<Slider>("VoiceVolume");
        var sfxSlider = _root.Q<Slider>("SFXVolume");
        
        // Инициализация текущими значениями (0-1 -> 0-100)
        masterSlider.SetValueWithoutNotify(G.Audio.GetMasterVolume() * 100f);
        musicSlider.SetValueWithoutNotify(G.Audio.GetMusicVolume() * 100f);
        voiceSlider.SetValueWithoutNotify(G.Audio.GetVoiceVolume() * 100f);
        sfxSlider.SetValueWithoutNotify(G.Audio.GetSFXVolume() * 100f);
        
        // Подписка на изменения (0-100 -> 0-1)
        masterSlider.RegisterValueChangedCallback(e => G.Audio.SetMasterVolume(e.newValue / 100f));
        musicSlider.RegisterValueChangedCallback(e => G.Audio.SetMusicVolume(e.newValue / 100f));
        voiceSlider.RegisterValueChangedCallback(e => G.Audio.SetVoiceVolume(e.newValue / 100f));
        sfxSlider.RegisterValueChangedCallback(e => G.Audio.SetSFXVolume(e.newValue / 100f));
    }

    private void ShowPanel(string panelName)
    {
        _root.Q(PANEL_MENU).AddToClassList("hidden");
        _root.Q(PANEL_LOAD).AddToClassList("hidden");
        _root.Q(PANEL_SETTINGS).AddToClassList("hidden");
        _root.Q(panelName).RemoveFromClassList("hidden");
        
        _currentPanel = panelName;
        
        if (panelName == PANEL_MENU)
            _root.Q<Button>("NewGameButton")?.Focus();
        else if (panelName == PANEL_SETTINGS)
        {
            _currentTabIndex = 0;
            _root.Q<Button>("GraphicsTabButton")?.Focus();
        }
    }

    private void SwitchTab(string tabName, string buttonName)
    {
        foreach (var tab in _tabs)
            _root.Q(tab).AddToClassList("hidden");
        
        foreach (var btn in _tabButtons)
            _root.Q<Button>(btn).RemoveFromClassList("tab-active");
        
        _root.Q(tabName).RemoveFromClassList("hidden");
        _root.Q<Button>(buttonName).AddToClassList("tab-active");
    }
}