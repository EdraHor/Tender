using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;
using Yarn.Unity;

public class DialogueView : DialoguePresenterBase
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private DialogueRunner dialogueRunner;
    [SerializeField] private bool useFadeEffect = true;
    [SerializeField] private float fadeUpDuration = 0.3f;
    [SerializeField] private float fadeDownDuration = 0.2f;
    [SerializeField] private bool useTypewriter = true;
    [SerializeField] private int charactersPerSecond = 40;
    [SerializeField] private float autoAdvanceDelay = 2f;
    [SerializeField] private bool showControlButtons = true;
    
    private VisualElement _root;
    private VisualElement _dialogueBox;
    private VisualElement _optionsContainer;
    private VisualElement _characterNameContainer;
    private VisualElement _clickArea;
    private VisualElement _historyPanel;
    private VisualElement _bottomControls;
    private ScrollView _historyScroll;
    
    private Label _characterNameLabel;
    private Label _dialogueTextLabel;
    private Button _historyButton;
    private Button _autoButton;
    private Button _skipButton;
    private Button _closeHistoryButton;
    
    private List<HistoryEntry> _history = new List<HistoryEntry>();
    private bool _isAutoMode = false;
    private bool _isSkipping = false;
    private bool _isLineComplete = false;
    private bool _isFirstLine = true;
    
    // Блокировка ввода во время фейда
    private bool _isTransitioning = false;
    
    // FIX: Задача для ожидания клика внутри текста
    private YarnTaskCompletionSource<bool> _waitingForClick; 
    
    private int _currentVisibleChars = 0;
    
    private CancellationTokenSource _typewriterCTS;
    private LocalizedLine _currentLine;
    private string _currentDisplayText;
    private DialogueOption[] _currentDialogueOptions;
    private YarnTaskCompletionSource<DialogueOption?> _optionCompletionSource;
    private int _selectedOptionIndex = 0;
    private List<Button> _optionButtons = new List<Button>();
    
    private float _lastNavigationTime = 0f;
    private const float NavigationCooldown = 0.2f;
    private const float NavigateThreshold = 0.5f;
    
    private struct HistoryEntry
    {
        public string CharacterName;
        public string Text;
    }
    
    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        
        if (dialogueRunner == null)
            dialogueRunner = GetComponent<DialogueRunner>();
            
        InitializeUI();
        SetupInputActions();
    }
    
    private void InitializeUI()
    {
        _root = uiDocument.rootVisualElement;
        
        _dialogueBox = _root.Q<VisualElement>("DialogueBox");
        _optionsContainer = _root.Q<VisualElement>("OptionsContainer");
        _characterNameContainer = _root.Q<VisualElement>("CharacterNameContainer");
        _clickArea = _root.Q<VisualElement>("ClickArea");
        _historyPanel = _root.Q<VisualElement>("HistoryPanel");
        _bottomControls = _root.Q<VisualElement>("BottomControls");
        _historyScroll = _root.Q<ScrollView>("HistoryScroll");
        
        _characterNameLabel = _root.Q<Label>("CharacterName");
        _characterNameLabel.enableRichText = true;
        _dialogueTextLabel = _root.Q<Label>("DialogueText");
        _dialogueTextLabel.enableRichText = true;
        
        _historyButton = _root.Q<Button>("HistoryButton");
        _autoButton = _root.Q<Button>("AutoButton");
        _skipButton = _root.Q<Button>("SkipButton");
        _closeHistoryButton = _root.Q<Button>("CloseHistoryButton");
        
        _clickArea.RegisterCallback<ClickEvent>(evt => OnContinueRequested());
        
        _dialogueBox.RegisterCallback<ClickEvent>(evt => 
        {
            if (evt.target == _dialogueBox || evt.target == _dialogueTextLabel || 
                evt.target == _characterNameLabel || evt.target == _characterNameContainer)
            {
                OnContinueRequested();
            }
        });
        
        _historyButton.clicked += ToggleHistory;
        _autoButton.clicked += ToggleAuto;
        _skipButton.clicked += ToggleSkip;
        _closeHistoryButton.clicked += () => _historyPanel.AddToClassList("hidden");
        
        HideAll();
    }
    
    private void SetupInputActions()
    {
        var input = G.Input;
        if (input == null) return;
        
        input.Dialogue.Continue.performed += ctx => OnContinueRequested();
        input.Dialogue.ForceContinue.performed += ctx => OnForceContinueRequested();
        input.Dialogue.Skip.performed += ctx => ToggleSkip();
        input.Dialogue.Auto.performed += ctx => ToggleAuto();
        input.Dialogue.Log.performed += ctx => ToggleHistory();
        input.Dialogue.ChoiceNavigate.performed += ctx => OnNavigateInput(ctx.ReadValue<float>());
        input.Dialogue.Submit.performed += ctx => ConfirmSelectedOption();
    }
    
    private void OnDestroy()
    {
        _typewriterCTS?.Cancel();
        // FIX: Очистка задачи если она висит
        _waitingForClick?.TrySetResult(true);
        
        var input = G.Input;
        if (input != null)
        {
            input.Dialogue.Continue.performed -= ctx => OnContinueRequested();
            input.Dialogue.ForceContinue.performed -= ctx => OnForceContinueRequested();
            input.Dialogue.Skip.performed -= ctx => ToggleSkip();
            input.Dialogue.Auto.performed -= ctx => ToggleAuto();
            input.Dialogue.Log.performed -= ctx => ToggleHistory();
            input.Dialogue.ChoiceNavigate.performed -= ctx => OnNavigateInput(ctx.ReadValue<float>());
            input.Dialogue.Submit.performed -= ctx => ConfirmSelectedOption();
        }
    }
    
    private void OnNavigateInput(float value)
    {
        if (_isTransitioning) return;
        
        bool isOutsideDeadzone = Mathf.Abs(value) >= NavigateThreshold;
        
        if (isOutsideDeadzone && Time.time - _lastNavigationTime >= NavigationCooldown)
        {
            int direction = value > 0 ? 1 : -1;
            NavigateOptions(direction);
        }
    }
    
    public override async YarnTask OnDialogueStartedAsync()
    {
        HideAll();
        _isFirstLine = true;
        _waitingForClick = null;
        
        // Задержка чтобы сбросить Input System
        await YarnTask.Yield();
        G.Input?.EnableDialogue();
    }
    
    private void Update()
    {
        if (_optionButtons.Count > 0 && G.Input != null && !_isTransitioning)
        {
            float value = G.Input.Dialogue.ChoiceNavigate.ReadValue<float>();
            
            if (Mathf.Abs(value) >= NavigateThreshold)
            {
                if (Time.time - _lastNavigationTime >= NavigationCooldown)
                {
                    int direction = value > 0 ? 1 : -1;
                    NavigateOptions(direction);
                }
            }
        }
    }
    
    public override async YarnTask OnDialogueCompleteAsync()
    {
        G.Input?.EnablePlayer();
        _waitingForClick?.TrySetResult(true);
        
        if (useFadeEffect && _dialogueBox != null && _dialogueBox.style.display == DisplayStyle.Flex)
        {
            await FadeOut(_dialogueBox, default);
        }
        
        HideAll();
        _isAutoMode = false;
        _isSkipping = false;
        UpdateButtonStates();
    }
    
    public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        _currentLine = line;
        _isLineComplete = false;
        _currentVisibleChars = 0;
        _waitingForClick = null; // Сброс
        
        var fullText = line.Text.Text;
        string characterName = line.CharacterName;
        string textWithoutName = line.TextWithoutCharacterName.Text;
        
        if (string.IsNullOrEmpty(characterName))
        {
            (characterName, textWithoutName) = DialogueUtils.ExtractCharacterName(fullText);
        }
        
        string textForProcessing = DialogueUtils.RemoveYarnMarkers(textWithoutName);
        _currentDisplayText = DialogueUtils.GetDisplayText(textForProcessing);
        var cleanText = DialogueUtils.GetCleanText(textForProcessing);
        
        if (!useTypewriter || _isSkipping)
        {
            _dialogueTextLabel.text = _currentDisplayText;
        }
        else
        {
            _dialogueTextLabel.text = "";
        }
        
        if (!string.IsNullOrEmpty(characterName))
        {
            _characterNameLabel.text = characterName;
            _characterNameContainer.style.display = DisplayStyle.Flex;
        }
        else
        {
            _characterNameContainer.style.display = DisplayStyle.None;
        }
        
        _optionsContainer.style.display = DisplayStyle.None;
        _dialogueBox.style.display = DisplayStyle.Flex;
        _bottomControls.style.display = showControlButtons ? DisplayStyle.Flex : DisplayStyle.None;
        
        if (useFadeEffect && _isFirstLine)
        {
            _isTransitioning = true;
            await FadeIn(_dialogueBox, token.NextLineToken);
            _isTransitioning = false;
            _isFirstLine = false;
        }
        
        if (token.HurryUpToken.IsCancellationRequested)
        {
             _dialogueTextLabel.text = _currentDisplayText;
        }
        else if (useTypewriter && !_isSkipping)
        {
            _typewriterCTS?.Cancel();
            _typewriterCTS = new CancellationTokenSource();
            
            token.HurryUpToken.Register(() =>
            {
                _typewriterCTS?.Cancel();
                // FIX: Если нас торопят, отменяем ожидание клика
                _waitingForClick?.TrySetResult(true);
            });
            
            await TypewriterEffect(textForProcessing, _typewriterCTS.Token);
        }
        
        _isLineComplete = true;
        _waitingForClick = null;
        
        _history.Add(new HistoryEntry
        {
            CharacterName = characterName,
            Text = cleanText
        });
        
        if (_isAutoMode && !token.NextLineToken.IsCancellationRequested)
        {
            await YarnTask.Delay((int)(autoAdvanceDelay * 1000), token.NextLineToken).SuppressCancellationThrow();
        }
        else if (!_isSkipping)
        {
            await YarnTask.WaitUntilCanceled(token.NextLineToken).SuppressCancellationThrow();
        }
    }
    
    public override async YarnTask<DialogueOption?> RunOptionsAsync(DialogueOption[] dialogueOptions, CancellationToken cancellationToken)
    {
        _dialogueBox.style.display = DisplayStyle.None;
        _optionsContainer.style.display = DisplayStyle.Flex;
        _optionsContainer.Clear();
        _optionButtons.Clear();
        _selectedOptionIndex = 0;
        _lastNavigationTime = 0f;
        
        _currentDialogueOptions = dialogueOptions;
        _optionCompletionSource = new YarnTaskCompletionSource<DialogueOption?>();
        
        for (int i = 0; i < dialogueOptions.Length; i++)
        {
            var option = dialogueOptions[i];
            var button = new Button();
            button.AddToClassList("option-button");
            button.text = option.Line.Text.Text;
            button.focusable = false;
            
            if (!option.IsAvailable)
                button.AddToClassList("unavailable");
            
            var index = i;
            button.clicked += () => OnOptionSelected(index);
            
            button.RegisterCallback<MouseEnterEvent>(evt => 
            {
                foreach (var btn in _optionButtons)
                    btn.RemoveFromClassList("selected");
                
                button.AddToClassList("selected");
                _selectedOptionIndex = index;
            });
            
            _optionsContainer.Add(button);
            _optionButtons.Add(button);
        }
        
        if (_optionButtons.Count > 0)
            _optionButtons[0].AddToClassList("selected");
        
        if (useFadeEffect)
        {
            _isTransitioning = true;
            await FadeIn(_optionsContainer, cancellationToken);
            _isTransitioning = false;
        }
        
        var result = await _optionCompletionSource.Task;
        
        if (useFadeEffect)
        {
            _isTransitioning = true;
            await FadeOut(_optionsContainer, cancellationToken).SuppressCancellationThrow();
            _isTransitioning = false;
        }
        
        return result;
    }
    
    private void OnContinueRequested()
    {
        if (_isTransitioning) return;
        
        if (_historyPanel.ClassListContains("hidden") == false)
            return;
        
        if (dialogueRunner == null)
            return;
            
        // FIX: Если мы ждем [click] внутри текста — просто завершаем ожидание и выходим,
        // чтобы typewriter продолжил печатать дальше
        if (_waitingForClick != null)
        {
            _waitingForClick.TrySetResult(true);
            return;
        }
            
        if (!_isLineComplete && useTypewriter)
        {
            _typewriterCTS?.Cancel();
            _dialogueTextLabel.text = _currentDisplayText ?? "";
            _isLineComplete = true;
            dialogueRunner.RequestHurryUpLine();
        }
        else if (_isLineComplete)
        {
            dialogueRunner.RequestNextLine();
        }
    }
    
    private void OnForceContinueRequested()
    {
        if (_isTransitioning) return;
        
        if (_historyPanel.ClassListContains("hidden") == false)
            return;
        
        if (dialogueRunner == null)
            return;
            
        // FIX: Аналогично для ForceContinue
        if (_waitingForClick != null)
        {
            _waitingForClick.TrySetResult(true);
            return;
        }
        
        if (!_isLineComplete && useTypewriter)
        {
            _typewriterCTS?.Cancel();
            _dialogueTextLabel.text = _currentDisplayText ?? "";
            _isLineComplete = true;
            dialogueRunner.RequestHurryUpLine();
        }
        else if (_isLineComplete)
        {
            dialogueRunner.RequestNextLine();
        }
    }
    
    private void OnOptionSelected(int index)
    {
        if (_isTransitioning) return;
        
        if (index < 0 || index >= _optionButtons.Count)
            return;
            
        var button = _optionButtons[index];
        if (button.ClassListContains("unavailable"))
            return;
        
        if (_currentDialogueOptions != null && index < _currentDialogueOptions.Length)
        {
            _optionCompletionSource?.TrySetResult(_currentDialogueOptions[index]);
        }
    }
    
    private void NavigateOptions(int direction)
    {
        if (_optionButtons.Count == 0) return;
        if (Time.time - _lastNavigationTime < NavigationCooldown) return;
        
        _lastNavigationTime = Time.time;
        foreach (var btn in _optionButtons) btn.RemoveFromClassList("selected");
        
        _selectedOptionIndex += direction;
        if (_selectedOptionIndex < 0) _selectedOptionIndex = _optionButtons.Count - 1;
        else if (_selectedOptionIndex >= _optionButtons.Count) _selectedOptionIndex = 0;
            
        _optionButtons[_selectedOptionIndex].AddToClassList("selected");
    }
    
    private void ConfirmSelectedOption()
    {
        if (_optionButtons.Count == 0) return;
        OnOptionSelected(_selectedOptionIndex);
    }
    
    private async YarnTask TypewriterEffect(string textWithMarkers, CancellationToken ct)
    {
        _dialogueTextLabel.text = "";
        _currentVisibleChars = 0;
        
        float delay = 1f / charactersPerSecond;
        var markers = DialogueUtils.ExtractSpecialMarkers(textWithMarkers);
        int markerIndex = 0;
        var parts = DialogueUtils.ParseRichText(textWithMarkers);
        string currentText = "";
        int currentCleanPosition = 0;
        
        foreach (var part in parts)
        {
            if (ct.IsCancellationRequested)
            {
                _dialogueTextLabel.text = _currentDisplayText;
                return;
            }
            
            if (part.IsTag)
            {
                currentText += part.Content;
                _dialogueTextLabel.text = currentText;
            }
            else
            {
                foreach (char c in part.Content)
                {
                    if (ct.IsCancellationRequested)
                    {
                        _dialogueTextLabel.text = _currentDisplayText;
                        return;
                    }
                    
                    while (markerIndex < markers.Count && markers[markerIndex].Position == currentCleanPosition)
                    {
                        var marker = markers[markerIndex];
                        
                        if (marker.Type == DialogueUtils.SpecialMarker.MarkerType.Wait)
                        {
                            await YarnTask.Delay((int)(marker.WaitTime * 1000), ct).SuppressCancellationThrow();
                        }
                        else if (marker.Type == DialogueUtils.SpecialMarker.MarkerType.Click)
                        {
                            // FIX: Создаем задачу ожидания вместо return
                            _waitingForClick = new YarnTaskCompletionSource<bool>();
                            
                            // Ждем, пока юзер не нажмет кнопку (OnContinueRequested),
                            // либо пока токен отмены (ct) не сработает (например, Skip или HurryUp)
                            using (ct.Register(() => _waitingForClick.TrySetResult(true)))
                            {
                                await _waitingForClick.Task;
                            }
                            
                            _waitingForClick = null;
                            
                            // Если после ожидания нас отменили (нажали Skip во время паузы), выходим
                            if (ct.IsCancellationRequested)
                            {
                                _dialogueTextLabel.text = _currentDisplayText;
                                return;
                            }
                        }
                        
                        markerIndex++;
                    }
                    
                    currentText += c;
                    _dialogueTextLabel.text = currentText;
                    currentCleanPosition++;
                    _currentVisibleChars++;
                    
                    await YarnTask.Delay((int)(delay * 1000), ct).SuppressCancellationThrow();
                }
            }
        }
        
        _dialogueTextLabel.text = _currentDisplayText;
    }
    
    private void ToggleHistory()
    {
        bool isHidden = _historyPanel.ClassListContains("hidden");
        if (isHidden) { ShowHistory(); _historyPanel.RemoveFromClassList("hidden"); }
        else { _historyPanel.AddToClassList("hidden"); }
    }
    
    private void ShowHistory()
    {
        _historyScroll.Clear();
        foreach (var entry in _history)
        {
            var entryElement = new VisualElement();
            entryElement.AddToClassList("history-entry");
            if (!string.IsNullOrEmpty(entry.CharacterName))
            {
                var nameLabel = new Label(entry.CharacterName);
                nameLabel.AddToClassList("history-entry-name");
                entryElement.Add(nameLabel);
            }
            var textLabel = new Label(entry.Text);
            textLabel.AddToClassList("history-entry-text");
            entryElement.Add(textLabel);
            _historyScroll.Add(entryElement);
        }
    }
    
    private void ToggleAuto()
    {
        _isAutoMode = !_isAutoMode;
        UpdateButtonStates();
    }
    
    private void ToggleSkip()
    {
        _isSkipping = !_isSkipping;
        UpdateButtonStates();
        
        if (_isSkipping)
        {
            // FIX: Если мы в режиме пропуска и висит пауза на клике — снимаем её
            if (_waitingForClick != null)
            {
                _waitingForClick.TrySetResult(true);
            }
            
            if (!_isLineComplete && useTypewriter && _currentLine != null)
            {
                _typewriterCTS?.Cancel();
                _dialogueTextLabel.text = _currentDisplayText ?? "";
                _isLineComplete = true;
                if (dialogueRunner != null)
                    dialogueRunner.RequestHurryUpLine();
            }
            else if (_isLineComplete && dialogueRunner != null)
            {
                dialogueRunner.RequestNextLine();
            }
        }
    }
    
    private void UpdateButtonStates()
    {
        if (_autoButton != null) { if (_isAutoMode) _autoButton.AddToClassList("active"); else _autoButton.RemoveFromClassList("active"); }
        if (_skipButton != null) { if (_isSkipping) _skipButton.AddToClassList("active"); else _skipButton.RemoveFromClassList("active"); }
    }
    
    private async YarnTask FadeIn(VisualElement element, CancellationToken ct)
    {
        element.style.opacity = 0;
        float elapsed = 0;
        while (elapsed < fadeUpDuration && !ct.IsCancellationRequested)
        {
            elapsed += Time.deltaTime;
            element.style.opacity = Mathf.Lerp(0, 1, elapsed / fadeUpDuration);
            await YarnTask.Yield();
        }
        element.style.opacity = 1;
    }
    
    private async YarnTask FadeOut(VisualElement element, CancellationToken ct)
    {
        element.style.opacity = 1;
        float elapsed = 0;
        while (elapsed < fadeDownDuration && !ct.IsCancellationRequested)
        {
            elapsed += Time.deltaTime;
            element.style.opacity = Mathf.Lerp(1, 0, elapsed / fadeDownDuration);
            await YarnTask.Yield();
        }
        element.style.opacity = 0;
    }
    
    private void HideAll()
    {
        if (_dialogueBox != null) _dialogueBox.style.display = DisplayStyle.None;
        if (_optionsContainer != null) _optionsContainer.style.display = DisplayStyle.None;
        if (_bottomControls != null) _bottomControls.style.display = DisplayStyle.None;
        if (_characterNameContainer != null) _characterNameContainer.style.display = DisplayStyle.None;
        if (_historyPanel != null) _historyPanel.AddToClassList("hidden");
    }
}