using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System;

public class MainMenuController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private Texture2D characterTexture;

    // UI Constants
    private const string PANEL_MENU = "MenuButtons";
    private const string PANEL_LOAD = "LoadGamePanel";
    private const string PANEL_SETTINGS = "SettingsPanel";

    // State
    private UIDocument _document;
    private const float TAB_SWITCH_COOLDOWN = 0.3f;
    private VisualElement _root;
    private string _currentPanel = PANEL_MENU;
    
    // Components
    private SettingsPanelController _settingsPanel;
    private SaveLoadPanelController _saveLoadPanel;
    
    // Input handlers
    private UINavigationHelper.InputModeTracker _inputModeTracker;
    private EventCallback<NavigationMoveEvent> _dropdownIsolationCallback;

    #region LIFECYCLE

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        
        if (G.Input != null)
        {
            G.Input.EnableUI();
        }
    }

    private void OnEnable()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;
        
        // Активируем UI Input
        if (G.Input != null)
        {
            G.Input.EnableUI();
        }
        
        // Инициализация компонентов
        _settingsPanel = new SettingsPanelController(
            _root.Q(PANEL_SETTINGS),
            _root,
            this
        );
        
        _saveLoadPanel = new SaveLoadPanelController(_root.Q(PANEL_LOAD));
        _saveLoadPanel.OnSlotAction += HandleSlotAction;
        
        SetupMainButtons();
        SetupSaveLoadUI();
        SetupInputHandling();
        
        if (characterTexture != null)
            _root.Q<VisualElement>("CharacterImage").style.backgroundImage = new StyleBackground(characterTexture);
        
        // Переинициализируем InputModeTracker каждый раз
        InitializeInputTracker();
    }
    
    private void InitializeInputTracker()
    {
        _inputModeTracker?.Dispose();
        _inputModeTracker = new UINavigationHelper.InputModeTracker();
        _inputModeTracker.Initialize(_root);
        
        // Даем UI системе завершить инициализацию
        _root.schedule.Execute(() => {
            _root.AddToClassList("keyboard-mode");
            _root.Q<Button>("NewGameButton")?.Focus();
        }).ExecuteLater(10); // 10ms задержка
    }

    private void OnDisable()
    {
        try { _settingsPanel?.Cleanup(); } catch { }
        
        if (_saveLoadPanel != null)
            _saveLoadPanel.OnSlotAction -= HandleSlotAction;
        
        _inputModeTracker?.Dispose();
        
        if (_dropdownIsolationCallback != null && _root != null)
        {
            _root.UnregisterCallback(_dropdownIsolationCallback, TrickleDown.TrickleDown);
        }
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
        // Используем именно Time.unscaledTime (не Time.time) чтобы избежать рассинхрона
        // после меню паузы (timescale=0) и задержек
        if (_currentPanel == PANEL_SETTINGS && Time.unscaledTime - _settingsPanel.LastTabSwitch > TAB_SWITCH_COOLDOWN)
            HandleTabSwitchInput();
    }

    #endregion

    #region SAVE / LOAD LOGIC

    private void SetupSaveLoadUI()
    {
        _root.Q<Button>("BackFromLoadButton").clicked += () => ShowPanel(PANEL_MENU);
    }

    private void OpenSaveLoadPanel(bool newGame)
    {
        var mode = newGame ? SaveLoadPanelController.Mode.NewGame : SaveLoadPanelController.Mode.Load;
        _saveLoadPanel.Open(mode);
        ShowPanel(PANEL_LOAD);
        _saveLoadPanel.FocusFirstElement();
    }

    private void HandleSlotAction(int slotIndex, bool isEmpty)
    {
        // Новая игра или загрузка
        if (!isEmpty || G.Save.SaveExists(slotIndex))
        {
            G.Save.CurrentSlot = slotIndex;
            G.Save.SaveGame(slotIndex);
        }
        else
        {
            if (isEmpty) return;
            G.Save.LoadGame(slotIndex);
        }
        
        SceneManager.LoadScene(gameSceneName);
    }

    #endregion

    #region UI NAVIGATION & INPUT

    private void SetupMainButtons()
    {
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
        
        _root.Q<Button>("BackFromSettingsButton").clicked += () => { 
            G.Graphics.SaveSettings(); 
            ShowPanel(PANEL_MENU); 
        };
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
            _settingsPanel.FocusFirstElement();
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
        _root.Query<Slider>().ForEach(s => s.focusable = false);
        _root.Query<Toggle>().ForEach(t => t.focusable = false);
        _root.Query<SimpleDropdown>().ForEach(d => d.focusable = false);
        
        SetupDropdownIsolation();
    }

    private void HandleTabSwitchInput()
    {
        if (G.Input == null) 
        {
            Debug.LogError("[MainMenu] G.Input is NULL!");
            return;
        }
    
        float val = G.Input.UI.TabSwitch.ReadValue<float>();
        Debug.Log($"[MainMenu] TabSwitch value: {val}, abs: {Mathf.Abs(val)}");
    
        if (Mathf.Abs(val) > 0.5f)
        {
            Debug.Log($"[MainMenu] Switching tab! Direction: {(val > 0 ? "right" : "left")}");
            _settingsPanel.HandleTabSwitchInput(val);
        }
    }

    #endregion

    #region DROPDOWN & FOCUS HELPERS
    
    private void SetupDropdownIsolation()
    {
        _dropdownIsolationCallback = evt => {
            var openDd = _root.Query<SimpleDropdown>().Where(d => d.IsOpen).First();
            
            if (openDd != null && (evt.target as VisualElement)?.parent?.ClassListContains("simple-dropdown__popup") != true)
            {
                evt.PreventDefault(); 
                evt.StopPropagation();
            }
        };
        
        _root.RegisterCallback(_dropdownIsolationCallback, TrickleDown.TrickleDown);
    }

    private bool TryCloseDropdown()
    {
        var openDd = _root.Query<SimpleDropdown>().Where(d => d.IsOpen).First();
        
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
            focused.parent.Focus(); 
            return true;
        }
        return false;
    }
    
    #endregion
}