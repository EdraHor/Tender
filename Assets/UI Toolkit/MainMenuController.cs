using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections;
using System;
using System.Linq;

public class MainMenuController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private Texture2D characterTexture;

    // UI Constants
    private const string PANEL_MENU = "MenuButtons";
    private const string PANEL_LOAD = "LoadGamePanel";
    private const string PANEL_SETTINGS = "SettingsPanel";
    private const string TAB_GRAPHICS = "GraphicsTab";
    private const string TAB_AUDIO = "AudioTab";

    // State
    private UIDocument _document;
    private const float TAB_SWITCH_COOLDOWN = 0.3f;
    private float _lastTabSwitch;
    private VisualElement _root;
    private string _currentPanel = PANEL_MENU;
    private bool _isNewGameMode; // True = Выбор слота для новой игры, False = Загрузка
    private bool _mouseJustMoved;
    
    // Helpers
    private Coroutine _fadeCoroutine;
    private Label _settingsAppliedLabel;

    #region LIFECYCLE

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        G.Input?.EnableUI();
    }

    private void OnEnable()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;
        G.Input?.EnableUI();
        
        SetupMainButtons();
        SetupSaveLoadUI();
        SetupSettingsUI();
        SetupInputHandling();

        // Подписка на события
        if (G.Graphics != null) G.Graphics.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Audio != null) G.Audio.OnSettingsApplied += ShowSettingsAppliedMessage;
        if (G.Save != null) G.Save.OnSettingsLoaded += RefreshAllSettingsUI;
        
        // Картинка персонажа
        if (characterTexture != null)
            _root.Q<VisualElement>("CharacterImage").style.backgroundImage = new StyleBackground(characterTexture);
    }

    private void OnDisable()
    {
        if (G.Graphics != null) G.Graphics.OnSettingsApplied -= ShowSettingsAppliedMessage;
        if (G.Audio != null) G.Audio.OnSettingsApplied -= ShowSettingsAppliedMessage;
        if (G.Save != null) G.Save.OnSettingsLoaded -= RefreshAllSettingsUI;
    }

    private void Update()
    {
        if (G.Input == null) return;

        // Обработка кнопки "Назад" (Esc/B)
        if (G.Input.UI.Cancel.WasPressedThisFrame())
        {
            if (TryCloseDropdown()) return;
            if (TryUnfocusControl()) return;
            GoBack();
        }

        // Переключение табов в настройках (Q/E или LB/RB)
        if (_currentPanel == PANEL_SETTINGS && Time.time - _lastTabSwitch > TAB_SWITCH_COOLDOWN)
            HandleTabSwitchInput();
    }

    #endregion

    #region SAVE / LOAD LOGIC

    private void SetupSaveLoadUI()
    {
        // Кнопка "Назад" в меню загрузки
        _root.Q<Button>("BackFromLoadButton").clicked += () => ShowPanel(PANEL_MENU);
    }

    /// <summary>
    /// Главный метод входа в меню сохранений
    /// </summary>
    /// <param name="newGame">True если это Новая Игра, False если Загрузка</param>
    private void OpenSaveLoadPanel(bool newGame)
    {
        _isNewGameMode = newGame;
        
        // 1. Меняем заголовок
        var title = _root.Q(PANEL_LOAD).Q<Label>(className: "panel-title");
        title.text = newGame ? "// НОВАЯ ИГРА" : "// ЗАГРУЗКА";

        // 2. Строим список
        RebuildSaveSlots();

        // 3. Показываем панель
        ShowPanel(PANEL_LOAD);

        // 4. Фокус на первый элемент для управления с клавиатуры
        var scroll = _root.Q<ScrollView>("SaveFilesList");
        var firstButton = scroll.Q<Button>(); // Ищем первую кнопку в скролле
        
        if (firstButton != null && firstButton.enabledSelf)
            firstButton.Focus();
        else
            _root.Q<Button>("BackFromLoadButton")?.Focus();
    }

    private void RebuildSaveSlots()
    {
        var listContainer = _root.Q<ScrollView>("SaveFilesList");
        listContainer.Clear(); // Удаляем старые кнопки/заглушки

        if (G.Save == null) return;

        var saves = G.Save.GetAllSaves(); // Получаем массив из 10 элементов

        for (int i = 0; i < saves.Length; i++)
        {
            int slotNum = i + 1;
            SaveMetadata data = saves[i];
            bool isEmpty = (data == null);

            // Если это меню ЗАГРУЗКИ и слот пуст - не показываем его (опционально)
            // Но чтобы интерфейс не прыгал, лучше показывать, но делать неактивным, 
            // или показывать как "Пусто". В данном решении:
            // Для "Загрузки" - показываем только существующие (так чище).
            // Для "Новой игры" - показываем все 10 слотов.
            
            if (!_isNewGameMode && isEmpty) 
                continue;

            // Создаем кнопку через код
            var slotBtn = CreateSlotVisuals(slotNum, data, isEmpty);
            
            // Логика нажатия
            slotBtn.clicked += () => OnSlotAction(slotNum, isEmpty);
            
            listContainer.Add(slotBtn);
        }

        // Если в режиме загрузки нет сохранений
        if (listContainer.childCount == 0 && !_isNewGameMode)
        {
            var label = new Label("Нет доступных сохранений");
            label.AddToClassList("placeholder");
            listContainer.Add(label);
        }
    }

    private Button CreateSlotVisuals(int slotNum, SaveMetadata data, bool isEmpty)
    {
        var btn = new Button();
        btn.AddToClassList("save-slot-button");
        if (isEmpty) btn.AddToClassList("save-slot-empty");

        // Левая часть (Тексты)
        var infoContainer = new VisualElement();
        infoContainer.AddToClassList("save-slot-info");

        var titleLabel = new Label($"СЛОТ {slotNum}");
        titleLabel.AddToClassList("save-slot-title");

        var detailsLabel = new Label();
        detailsLabel.AddToClassList("save-slot-details");

        // Правая часть (Действие)
        var actionLabel = new Label();
        actionLabel.AddToClassList("save-slot-action");

        // Наполнение данными
        if (isEmpty)
        {
            detailsLabel.text = "Пусто";
            actionLabel.text = _isNewGameMode ? "СОЗДАТЬ" : "";
        }
        else
        {
            // Красивое форматирование времени (чч:мм)
            TimeSpan ts = TimeSpan.FromSeconds(data.playTime);
            string timeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
            
            detailsLabel.text = $"{data.saveDate}  |  Глава {data.chapter}  |  {timeStr}";
            actionLabel.text = _isNewGameMode ? "ПЕРЕЗАПИСАТЬ" : "ЗАГРУЗИТЬ";
        }

        infoContainer.Add(titleLabel);
        infoContainer.Add(detailsLabel);
        
        btn.Add(infoContainer);
        btn.Add(actionLabel);

        // Добавляем поддержку мыши/клавиатуры (Hover эффект)
        RegisterHoverEvents(btn);

        return btn;
    }

    private void OnSlotAction(int slotIndex, bool isEmpty)
    {
        if (_isNewGameMode)
        {
            // НОВАЯ ИГРА
            Debug.Log($"[UI] Starting New Game on Slot {slotIndex}");
            
            // 1. Указываем менеджеру текущий слот
            G.Save.CurrentSlot = slotIndex;
            
            // 2. Создаем файл сохранения (начальное состояние)
            // Это важно, чтобы файл физически появился на диске сразу
            G.Save.SaveGame(slotIndex);
            
            // 3. Грузим сцену
            SceneManager.LoadScene(gameSceneName);
        }
        else
        {
            // ЗАГРУЗКА
            if (isEmpty) return; // Защита от дурака

            Debug.Log($"[UI] Loading Slot {slotIndex}");
            G.Save.LoadGame(slotIndex);
            SceneManager.LoadScene(gameSceneName);
        }
    }

    #endregion

    #region UI NAVIGATION & INPUT

    private void SetupMainButtons()
    {
        // Привязываем кнопки главного меню к новой логике
        _root.Q<Button>("NewGameButton").clicked += () => OpenSaveLoadPanel(true);
        _root.Q<Button>("LoadGameButton").clicked += () => OpenSaveLoadPanel(false);
        _root.Q<Button>("SettingsButton").clicked += () => ShowPanel(PANEL_SETTINGS);
        
        _root.Q<Button>("ExitButton").clicked += () => {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        };
    }

    private void ShowPanel(string panelName)
    {
        // Скрываем всё
        _root.Q(PANEL_MENU).AddToClassList("hidden");
        _root.Q(PANEL_LOAD).AddToClassList("hidden");
        _root.Q(PANEL_SETTINGS).AddToClassList("hidden");
        
        // Показываем нужное
        _root.Q(panelName).RemoveFromClassList("hidden");
        _currentPanel = panelName;

        // Управление фокусом при смене панелей
        if (panelName == PANEL_MENU)
            _root.Q<Button>("NewGameButton")?.Focus();
        else if (panelName == PANEL_SETTINGS)
        {
            SwitchTab(TAB_GRAPHICS, "GraphicsTabButton"); // Сброс на первый таб
            _root.Q<Button>("GraphicsTabButton")?.Focus();
        }
    }

    private void GoBack()
    {
        if (_currentPanel == PANEL_LOAD)
        {
            ShowPanel(PANEL_MENU);
        }
        else if (_currentPanel == PANEL_SETTINGS)
        {
            G.Graphics.SaveSettings();
            ShowPanel(PANEL_MENU);
        }
    }

    private void SetupInputHandling()
    {
        // Определяем режим ввода (Клавиатура vs Мышь)
        _root.RegisterCallback<NavigationMoveEvent>(evt => SwitchToKeyboardMode(), TrickleDown.TrickleDown);
        _root.RegisterCallback<MouseMoveEvent>(evt => SwitchToMouseMode(), TrickleDown.TrickleDown);

        // Регистрация Hover для статических кнопок
        _root.Query<Button>().ForEach(RegisterHoverEvents);
        
        // Блокировка фокуса на слайдерах мышью (чтобы фокус оставался на строке)
        _root.Query<Slider>().ForEach(s => s.focusable = false);
        _root.Query<Toggle>().ForEach(t => t.focusable = false);
        _root.Query<SimpleDropdown>().ForEach(d => d.focusable = false);
        
        SetupDropdownIsolation();
    }

    private void RegisterHoverEvents(Button btn)
    {
        btn.RegisterCallback<MouseEnterEvent>(evt => {
            if (_root.ClassListContains("keyboard-mode") && !_mouseJustMoved) return;
            
            // Если мышь двинулась - сбрасываем режим клавиатуры
            if (_mouseJustMoved && _root.ClassListContains("keyboard-mode"))
                SwitchToMouseMode();

            // Визуальный hover для мыши
            (evt.target as Button)?.AddToClassList("hovered");
        });

        btn.RegisterCallback<MouseLeaveEvent>(evt => {
            (evt.target as Button)?.RemoveFromClassList("hovered");
        });
    }

    private void SwitchToKeyboardMode()
    {
        if (!_root.ClassListContains("keyboard-mode"))
        {
            _root.AddToClassList("keyboard-mode");
            _mouseJustMoved = false;
            _root.Query<Button>().ForEach(btn => btn.RemoveFromClassList("hovered"));
        }
    }

    private void SwitchToMouseMode()
    {
        _mouseJustMoved = true;
        if (_root.ClassListContains("keyboard-mode"))
        {
            _root.RemoveFromClassList("keyboard-mode");
            // Снимаем фокус с кнопок, чтобы не висел "двойной выбор"
            var focused = _root.focusController?.focusedElement;
            if (focused is Button) focused.Blur();
        }
    }

    #endregion

    #region SETTINGS IMPLEMENTATION (Graphics/Audio)
    
    // Этот код я свернул для читаемости, так как он не менялся логически,
    // но он необходим для работы.
    
    private void SetupSettingsUI()
    {
        // Tabs
        _root.Q<Button>("GraphicsTabButton").clicked += () => SwitchTab(TAB_GRAPHICS, "GraphicsTabButton");
        _root.Q<Button>("AudioTabButton").clicked += () => SwitchTab(TAB_AUDIO, "AudioTabButton");
        _root.Q<Button>("BackFromSettingsButton").clicked += () => { G.Graphics.SaveSettings(); ShowPanel(PANEL_MENU); };

        SetupGraphicsControls();
        SetupAudioControls();
        SetupSettingRowsInteract();
        
        // Label "Saved"
        _settingsAppliedLabel = new Label("✓ Настройки сохранены");
        _settingsAppliedLabel.AddToClassList("settings-applied-label");
        _settingsAppliedLabel.style.display = DisplayStyle.None;
        _root.Q(PANEL_SETTINGS).Add(_settingsAppliedLabel);
    }
    
    private void SwitchTab(string tabName, string btnName)
    {
        _root.Q(TAB_GRAPHICS).AddToClassList("hidden");
        _root.Q(TAB_AUDIO).AddToClassList("hidden");
        _root.Q<Button>("GraphicsTabButton").RemoveFromClassList("tab-active");
        _root.Q<Button>("AudioTabButton").RemoveFromClassList("tab-active");
        
        _root.Q(tabName).RemoveFromClassList("hidden");
        _root.Q<Button>(btnName).AddToClassList("tab-active");
    }

    private void HandleTabSwitchInput()
    {
        float val = G.Input.UI.TabSwitch.ReadValue<float>(); // Q/E axis
        if (Mathf.Abs(val) <= 0.5f) return;
        
        _lastTabSwitch = Time.time;
        
        // Простая логика переключения для двух табов
        string currentTab = _root.Q(TAB_GRAPHICS).ClassListContains("hidden") ? TAB_AUDIO : TAB_GRAPHICS;
        if (currentTab == TAB_GRAPHICS) SwitchTab(TAB_AUDIO, "AudioTabButton");
        else SwitchTab(TAB_GRAPHICS, "GraphicsTabButton");
    }
    
    // --- Boilerplate для UI настроек ---
    
    private void RefreshAllSettingsUI()
    {
        RefreshGraphicsUI();
        RefreshAudioUI();
    }

    private void SetupGraphicsControls()
    {
        var preset = _root.Q<SimpleDropdown>("QualityPreset");
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
            
        var vsync = _root.Q<Toggle>("VSync");
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
        var slider = _root.Q<Slider>(name);
        if (slider == null) return;
        slider.SetValueWithoutNotify(getter());
        slider.RegisterValueChangedCallback(e => setter(e.newValue));
    }
    
    private void BindDropdown(string name, System.Collections.Generic.List<string> choices, Func<int> getter, Action<int> setter)
    {
        var dd = _root.Q<SimpleDropdown>(name);
        if (dd == null) return;
        
        dd.choices = choices;
        dd.index = getter();
        
        dd.valueChanged += _ => setter(dd.index);
    }

    private void RefreshGraphicsUI()
    {
        _root.Q<SimpleDropdown>("QualityPreset").index = Mathf.Max(0, G.Graphics.GetCurrentPreset());
        _root.Q<SimpleDropdown>("ShadowQuality").index = G.Graphics.GetShadowsEnabled() ? 1 : 0;
        _root.Q<Slider>("ShadowDistance").SetValueWithoutNotify(G.Graphics.GetShadowDistance());
        _root.Q<SimpleDropdown>("ShadowResolution").index = G.Graphics.GetShadowResolution();
        _root.Q<Toggle>("VSync").SetValueWithoutNotify(G.Graphics.GetVSync());
        
        bool shadowOn = G.Graphics.GetShadowsEnabled();
        _root.Q<Slider>("ShadowDistance").parent.SetEnabled(shadowOn);
        _root.Q<SimpleDropdown>("ShadowResolution").parent.SetEnabled(shadowOn);
    }
    
    private void RefreshAudioUI()
    {
        if (G.Audio == null) return;
        _root.Q<Slider>("MasterVolume")?.SetValueWithoutNotify(G.Audio.GetMasterVolume() * 100);
    }

    private void ForceCustomPreset()
    {
        var preset = _root.Q<SimpleDropdown>("QualityPreset");
        if (G.Graphics.GetCurrentPreset() == -1 && !preset.choices.Contains("Пользовательский"))
        {
            preset.choices.Add("Пользовательский");
            preset.SetValueWithoutNotify("Пользовательский");
        }
    }

    private void ShowSettingsAppliedMessage()
    {
        if (_settingsAppliedLabel == null) return;
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeLabelRoutine());
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
        _root.Query<VisualElement>(className: "setting-row").ForEach(row => {
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

    private void SetupDropdownIsolation()
    {
        _root.RegisterCallback<NavigationMoveEvent>(evt => {
            // Добавляем .ToList(), чтобы превратить UQueryBuilder в List,
            // у которого есть FirstOrDefault()
            var openDd = _root.Query<SimpleDropdown>()
                .Where(d => d.IsOpen)
                .ToList() 
                .FirstOrDefault();
            
            if (openDd != null && (evt.target as VisualElement)?.parent?.ClassListContains("simple-dropdown__popup") != true)
            {
                evt.PreventDefault(); 
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);
    }

    private bool TryCloseDropdown()
    {
        // Аналогично добавляем .ToList()
        var openDd = _root.Query<SimpleDropdown>()
            .Where(d => d.IsOpen)
            .ToList()
            .FirstOrDefault();
        
        if (openDd != null) 
        { 
            openDd.ClosePopup(); 
            openDd.Focus(); 
            return true; 
        }
        return false;
    }

    private bool TryUnfocusControl()
    {
        var focused = _root.focusController.focusedElement as VisualElement;
        if (focused?.parent?.ClassListContains("setting-row") == true && (focused is Slider || focused is Toggle))
        {
            focused.parent.Focus(); return true;
        }
        return false;
    }
    
    #endregion
}