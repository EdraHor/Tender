using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System;

public class PauseMenuController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private Texture2D characterTexture;

    // UI Constants
    private const string PANEL_PAUSE = "PauseButtons";
    private const string PANEL_SAVE = "SaveGamePanel";
    private const string PANEL_LOAD = "LoadGamePanel";
    private const string PANEL_SETTINGS = "SettingsPanel";
    private const string PANEL_CONFIRM = "ConfirmExitPanel";

    // State
    private UIDocument _document;
    private const float TAB_SWITCH_COOLDOWN = 0.3f;
    private VisualElement _root;
    private string _currentPanel = PANEL_PAUSE;
    private bool _isPaused = false;
    private bool _isSubscribed = false;
    
    // Храним ссылку на Action для правильной подписки/отписки
    private Action<UnityEngine.InputSystem.InputAction.CallbackContext> _pauseMenuAction;
    
    // Components
    private SettingsPanelController _settingsPanel;
    private SaveLoadPanelController _savePanel;
    private SaveLoadPanelController _loadPanel;
    
    // Input handlers
    private UINavigationHelper.InputModeTracker _inputModeTracker;
    private EventCallback<NavigationMoveEvent> _dropdownIsolationCallback;

    #region LIFECYCLE

    private void Awake()
    {
        _document = GetComponent<UIDocument>();
        _pauseMenuAction = ctx => OnPauseMenuPressed();
    }

    private void OnEnable()
    {
        if (_document == null) _document = GetComponent<UIDocument>();
        _root = _document.rootVisualElement;
        
        // Инициализация компонентов
        _settingsPanel = new SettingsPanelController(
            _root.Q(PANEL_SETTINGS),
            _root,
            this
        );
        
        _savePanel = new SaveLoadPanelController(_root.Q(PANEL_SAVE));
        _savePanel.OnSlotAction += HandleSaveSlotAction;
        
        _loadPanel = new SaveLoadPanelController(_root.Q(PANEL_LOAD));
        _loadPanel.OnSlotAction += HandleLoadSlotAction;
        
        SetupPauseButtons();
        SetupSaveLoadUI();
        SetupConfirmDialog();
        SetupInputHandling();
        
        if (characterTexture != null)
            _root.Q<VisualElement>("CharacterImage").style.backgroundImage = new StyleBackground(characterTexture);
        
        HideMenu();
        TrySubscribeToPauseInput();
    }

    private void OnDisable()
    {
        UnsubscribeFromPauseInput();
        
        try { _settingsPanel?.Cleanup(); } catch { }
        
        if (_savePanel != null) _savePanel.OnSlotAction -= HandleSaveSlotAction;
        if (_loadPanel != null) _loadPanel.OnSlotAction -= HandleLoadSlotAction;
        
        _inputModeTracker?.Dispose();
        
        if (_dropdownIsolationCallback != null && _root != null)
        {
            _root.UnregisterCallback(_dropdownIsolationCallback, TrickleDown.TrickleDown);
        }
        
        if (_isPaused)
        {
            Time.timeScale = 1f;
        }
    }

    private void Update()
    {
        // Пытаемся подписаться если еще не подписаны
        if (!_isSubscribed)
        {
            TrySubscribeToPauseInput();
        }

        // Обработка Cancel только когда меню открыто
        if (_isPaused && G.Input != null && G.Input.UI.Cancel.WasPressedThisFrame())
        {
            if (_currentPanel != PANEL_PAUSE && _currentPanel != PANEL_CONFIRM)
            {
                if (TryCloseDropdown()) return;
                if (TryUnfocusControl()) return;
                GoBack();
            }
            else if (_currentPanel == PANEL_CONFIRM)
            {
                CloseConfirmDialog();
            }
            else if (_currentPanel == PANEL_PAUSE)
            {
                ResumeGame();
            }
        }

        // Переключение табов
        if (_isPaused && _currentPanel == PANEL_SETTINGS && 
            Time.unscaledTime - _settingsPanel.LastTabSwitch > TAB_SWITCH_COOLDOWN)
        {
            HandleTabSwitchInput();
        }
    }

    #endregion

    #region INPUT SUBSCRIPTION

    private void TrySubscribeToPauseInput()
    {
        if (_isSubscribed || G.Input == null) return;
        
        try
        {
            G.Input.Player.OpenMenu.performed += _pauseMenuAction;
            _isSubscribed = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[PauseMenu] Subscribe failed: {e.Message}");
        }
    }
    
    private void UnsubscribeFromPauseInput()
    {
        if (!_isSubscribed) return;
        
        try
        {
            if (G.Input != null)
            {
                G.Input.Player.OpenMenu.performed -= _pauseMenuAction;
            }
        }
        catch { }
        finally
        {
            _isSubscribed = false;
        }
    }
    
    private void OnPauseMenuPressed()
    {
        if (!_isPaused)
        {
            PauseGame();
        }
    }

    #endregion

    #region PAUSE LOGIC

    public void PauseGame()
    {
        _isPaused = true;
        Time.timeScale = 0f;
        
        // Показываем UI
        _root.style.display = DisplayStyle.Flex;
        ShowPanel(PANEL_PAUSE);
        
        // Устанавливаем максимальный sorting order
        var uiDoc = GetComponent<UIDocument>();
        if (uiDoc != null)
        {
            uiDoc.sortingOrder = 1000;
        }
        
        // Активируем UI input
        if (G.Input == null)
        {
            Debug.LogError("[PauseMenu] G.Input is null!");
            return;
        }
        
        G.Input.EnableUI();
        
        // Переинициализируем InputModeTracker
        _inputModeTracker?.Dispose();
        _inputModeTracker = new UINavigationHelper.InputModeTracker();
        _inputModeTracker.Initialize(_root);
    }

    public void ResumeGame()
    {
        _isPaused = false;
        Time.timeScale = 1f;
        
        HideMenu();
        G.Input?.EnablePlayer();
    }

    private void HideMenu()
    {
        _root.style.display = DisplayStyle.None;
    }

    #endregion

    #region SAVE / LOAD LOGIC

    private void SetupSaveLoadUI()
    {
        _root.Q<Button>("BackFromSaveButton").clicked += () => ShowPanel(PANEL_PAUSE);
        _root.Q<Button>("BackFromLoadButton").clicked += () => ShowPanel(PANEL_PAUSE);
    }

    private void OpenSavePanel()
    {
        _savePanel.Open(SaveLoadPanelController.Mode.Save);
        ShowPanel(PANEL_SAVE);
        _savePanel.FocusFirstElement();
    }

    private void OpenLoadPanel()
    {
        _loadPanel.Open(SaveLoadPanelController.Mode.Load);
        ShowPanel(PANEL_LOAD);
        _loadPanel.FocusFirstElement();
    }

    private void HandleSaveSlotAction(int slotIndex, bool isEmpty)
    {
        G.Save.CurrentSlot = slotIndex;
        G.Save.SaveGame(slotIndex);
        ShowPanel(PANEL_PAUSE);
    }

    private void HandleLoadSlotAction(int slotIndex, bool isEmpty)
    {
        if (isEmpty) return;
        
        Time.timeScale = 1f;
        G.Save.LoadGame(slotIndex);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    #endregion

    #region CONFIRM DIALOG

    private void SetupConfirmDialog()
    {
        _root.Q<Button>("ConfirmExitYes").clicked += () => {
            Time.timeScale = 1f;
            enabled = false;
            SceneManager.LoadScene(mainMenuSceneName);
        };
        
        _root.Q<Button>("ConfirmExitNo").clicked += CloseConfirmDialog;
    }

    private void ShowConfirmDialog()
    {
        ShowPanel(PANEL_CONFIRM);
        _root.Q<Button>("ConfirmExitYes")?.Focus();
    }

    private void CloseConfirmDialog()
    {
        ShowPanel(PANEL_PAUSE);
    }

    #endregion

    #region UI NAVIGATION & INPUT

    private void SetupPauseButtons()
    {
        _root.Q<Button>("ResumeButton").clicked += ResumeGame;
        _root.Q<Button>("SaveButton").clicked += OpenSavePanel;
        _root.Q<Button>("LoadButton").clicked += OpenLoadPanel;
        _root.Q<Button>("SettingsButton").clicked += () => ShowPanel(PANEL_SETTINGS);
        _root.Q<Button>("MainMenuButton").clicked += ShowConfirmDialog;
        _root.Q<Button>("BackFromSettingsButton").clicked += () => { 
            G.Graphics.SaveSettings(); 
            ShowPanel(PANEL_PAUSE); 
        };
    }

    private void ShowPanel(string panelName)
    {
        _root.Q(PANEL_PAUSE).AddToClassList("hidden");
        _root.Q(PANEL_SAVE).AddToClassList("hidden");
        _root.Q(PANEL_LOAD).AddToClassList("hidden");
        _root.Q(PANEL_SETTINGS).AddToClassList("hidden");
        _root.Q(PANEL_CONFIRM).AddToClassList("hidden");
        
        _root.Q(panelName).RemoveFromClassList("hidden");
        _currentPanel = panelName;

        if (panelName == PANEL_PAUSE)
            _root.Q<Button>("ResumeButton")?.Focus();
        else if (panelName == PANEL_SETTINGS)
            _settingsPanel.FocusFirstElement();
    }

    private void GoBack()
    {
        if (_currentPanel == PANEL_SAVE || _currentPanel == PANEL_LOAD)
        {
            ShowPanel(PANEL_PAUSE);
        }
        else if (_currentPanel == PANEL_SETTINGS)
        {
            G.Graphics.SaveSettings();
            ShowPanel(PANEL_PAUSE);
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
        if (G.Input == null) return;
        
        float val = G.Input.UI.TabSwitch.ReadValue<float>();
        if (Mathf.Abs(val) > 0.5f)
        {
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