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
    
    private VisualElement root;
    private VisualElement dialogueBox;
    private VisualElement optionsContainer;
    private VisualElement characterNameContainer;
    private VisualElement clickArea;
    private VisualElement historyPanel;
    private VisualElement bottomControls;
    private ScrollView historyScroll;
    
    private Label characterNameLabel;
    private Label dialogueTextLabel;
    private Button historyButton;
    private Button autoButton;
    private Button skipButton;
    private Button closeHistoryButton;
    
    private List<HistoryEntry> history = new List<HistoryEntry>();
    private bool isAutoMode = false;
    private bool isSkipping = false;
    private bool isLineComplete = false;
    private bool isFirstLine = true;
    private int currentVisibleChars = 0;
    
    private CancellationTokenSource typewriterCTS;
    private LocalizedLine currentLine;
    private DialogueOption[] currentDialogueOptions;
    private YarnTaskCompletionSource<DialogueOption?> optionCompletionSource;
    private int selectedOptionIndex = 0;
    private List<Button> optionButtons = new List<Button>();
    
    private float lastNavigationTime = 0f;
    private const float navigationCooldown = 0.2f;
    private const float navigateThreshold = 0.5f;
    
    private struct HistoryEntry
    {
        public string characterName;
        public string text;
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
        root = uiDocument.rootVisualElement;
        
        dialogueBox = root.Q<VisualElement>("DialogueBox");
        optionsContainer = root.Q<VisualElement>("OptionsContainer");
        characterNameContainer = root.Q<VisualElement>("CharacterNameContainer");
        clickArea = root.Q<VisualElement>("ClickArea");
        historyPanel = root.Q<VisualElement>("HistoryPanel");
        bottomControls = root.Q<VisualElement>("BottomControls");
        historyScroll = root.Q<ScrollView>("HistoryScroll");
        
        characterNameLabel = root.Q<Label>("CharacterName");
        dialogueTextLabel = root.Q<Label>("DialogueText");
        
        historyButton = root.Q<Button>("HistoryButton");
        autoButton = root.Q<Button>("AutoButton");
        skipButton = root.Q<Button>("SkipButton");
        closeHistoryButton = root.Q<Button>("CloseHistoryButton");
        
        // Click area для продолжения - на пустом пространстве и на самом окне
        clickArea.RegisterCallback<ClickEvent>(evt => OnContinueRequested());
        
        // Добавить клик на диалоговое окно
        dialogueBox.RegisterCallback<ClickEvent>(evt => 
        {
            // Проверить что клик не был на кнопках
            if (evt.target == dialogueBox || evt.target == dialogueTextLabel || 
                evt.target == characterNameLabel || evt.target == characterNameContainer)
            {
                OnContinueRequested();
            }
        });
        
        // Кнопки управления
        historyButton.clicked += ToggleHistory;
        autoButton.clicked += ToggleAuto;
        skipButton.clicked += ToggleSkip;
        closeHistoryButton.clicked += () => historyPanel.AddToClassList("hidden");
        
        // Скрыть все элементы по умолчанию
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
        typewriterCTS?.Cancel();
        
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
        // Этот метод теперь работает для первого срабатывания
        // Update() обрабатывает удержание
        bool isOutsideDeadzone = Mathf.Abs(value) >= navigateThreshold;
        
        if (isOutsideDeadzone && Time.time - lastNavigationTime >= navigationCooldown)
        {
            int direction = value > 0 ? 1 : -1;
            NavigateOptions(direction);
        }
    }
    
    public override YarnTask OnDialogueStartedAsync()
    {
        G.Input?.EnableDialogue();
        HideAll();
        isFirstLine = true; // Сбросить флаг для fade in первой реплики
        return YarnTask.CompletedTask;
    }
    
    private void Update()
    {
        // Постоянная проверка удержания навигации для опций
        if (optionButtons.Count > 0 && G.Input != null)
        {
            float value = G.Input.Dialogue.ChoiceNavigate.ReadValue<float>();
            
            if (Mathf.Abs(value) >= navigateThreshold)
            {
                if (Time.time - lastNavigationTime >= navigationCooldown)
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
        
        // Fade out только при завершении всего диалога
        if (useFadeEffect && dialogueBox != null && dialogueBox.style.display == DisplayStyle.Flex)
        {
            await FadeOut(dialogueBox, default);
        }
        
        HideAll();
        isAutoMode = false;
        isSkipping = false;
        UpdateButtonStates();
    }
    
    public override async YarnTask RunLineAsync(LocalizedLine line, LineCancellationToken token)
    {
        currentLine = line;
        isLineComplete = false;
        currentVisibleChars = 0;
        
        // Настройка UI
        var text = line.TextWithoutCharacterName;
        
        // Не устанавливаем текст сразу если включен typewriter
        if (!useTypewriter || isSkipping)
        {
            dialogueTextLabel.text = text.Text;
        }
        else
        {
            dialogueTextLabel.text = "";
        }
        
        if (!string.IsNullOrEmpty(line.CharacterName))
        {
            characterNameLabel.text = line.CharacterName;
            characterNameContainer.style.display = DisplayStyle.Flex;
        }
        else
        {
            characterNameContainer.style.display = DisplayStyle.None;
        }
        
        // Показываем диалоговое окно
        optionsContainer.style.display = DisplayStyle.None;
        dialogueBox.style.display = DisplayStyle.Flex;
        bottomControls.style.display = showControlButtons ? DisplayStyle.Flex : DisplayStyle.None;
        
        // Fade in только для первой реплики
        if (useFadeEffect && isFirstLine)
        {
            await FadeIn(dialogueBox, token.HurryUpToken);
            isFirstLine = false;
        }
        
        // Typewriter эффект
        if (useTypewriter && !isSkipping)
        {
            typewriterCTS?.Cancel();
            typewriterCTS = new CancellationTokenSource();
            var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(token.HurryUpToken, typewriterCTS.Token);
            
            await TypewriterEffect(text.Text, linkedCTS.Token);
        }
        
        isLineComplete = true;
        
        // Добавить в историю
        history.Add(new HistoryEntry
        {
            characterName = line.CharacterName,
            text = text.Text
        });
        
        // Авто-режим или ожидание ввода
        if (isAutoMode && !token.NextLineToken.IsCancellationRequested)
        {
            await YarnTask.Delay((int)(autoAdvanceDelay * 1000), token.NextLineToken).SuppressCancellationThrow();
        }
        else if (!isSkipping)
        {
            await YarnTask.WaitUntilCanceled(token.NextLineToken).SuppressCancellationThrow();
        }
        
        // Не делаем fade out между репликами, чтобы окно не мигало
        // Fade out только при завершении диалога в OnDialogueCompleteAsync
    }
    
    public override async YarnTask<DialogueOption?> RunOptionsAsync(DialogueOption[] dialogueOptions, CancellationToken cancellationToken)
    {
        // Скрыть диалоговое окно и показать опции
        dialogueBox.style.display = DisplayStyle.None;
        optionsContainer.style.display = DisplayStyle.Flex;
        optionsContainer.Clear();
        optionButtons.Clear();
        selectedOptionIndex = 0;
        lastNavigationTime = 0f; // Сбросить cooldown для новых опций
        
        currentDialogueOptions = dialogueOptions;
        optionCompletionSource = new YarnTaskCompletionSource<DialogueOption?>();
        
        // Создать кнопки опций
        for (int i = 0; i < dialogueOptions.Length; i++)
        {
            var option = dialogueOptions[i];
            var button = new Button();
            button.AddToClassList("option-button");
            button.text = option.Line.Text.Text;
            button.focusable = false; // Отключить keyboard focus
            
            if (!option.IsAvailable)
                button.AddToClassList("unavailable");
            
            var index = i;
            button.clicked += () => OnOptionSelected(index);
            
            // При наведении мышки - сбросить keyboard selection и подсветить этот
            button.RegisterCallback<MouseEnterEvent>(evt => 
            {
                // Убрать выделение со всех
                foreach (var btn in optionButtons)
                    btn.RemoveFromClassList("selected");
                
                // Подсветить текущий
                button.AddToClassList("selected");
                selectedOptionIndex = index;
            });
            
            optionsContainer.Add(button);
            optionButtons.Add(button);
        }
        
        // Выделить первую опцию по умолчанию
        if (optionButtons.Count > 0)
            optionButtons[0].AddToClassList("selected");
        
        // Fade in
        if (useFadeEffect)
            await FadeIn(optionsContainer, cancellationToken);
        
        // Ждать выбора
        var result = await optionCompletionSource.Task;
        
        // Fade out
        if (useFadeEffect)
            await FadeOut(optionsContainer, cancellationToken).SuppressCancellationThrow();
        
        return result;
    }
    
    private void OnContinueRequested()
    {
        if (historyPanel.ClassListContains("hidden") == false)
            return; // Не продолжать если открыта история
        
        if (dialogueRunner == null)
        {
            Debug.LogError("[DialogueView] DialogueRunner не назначен!");
            return;
        }
            
        if (!isLineComplete && useTypewriter)
        {
            // Завершить typewriter немедленно
            typewriterCTS?.Cancel();
            dialogueTextLabel.text = currentLine?.TextWithoutCharacterName.Text ?? "";
            isLineComplete = true;
            dialogueRunner.RequestHurryUpLine();
        }
        else if (isLineComplete)
        {
            // Продолжить к следующей строке
            dialogueRunner.RequestNextLine();
        }
    }
    
    private void OnForceContinueRequested()
    {
        if (historyPanel.ClassListContains("hidden") == false)
            return;
        
        if (dialogueRunner == null)
        {
            Debug.LogError("[DialogueView] DialogueRunner не назначен!");
            return;
        }
        
        // Если текст еще выводится - пропустить анимацию, но не переходить дальше
        if (!isLineComplete && useTypewriter)
        {
            typewriterCTS?.Cancel();
            dialogueTextLabel.text = currentLine?.TextWithoutCharacterName.Text ?? "";
            isLineComplete = true;
            dialogueRunner.RequestHurryUpLine();
        }
        else if (isLineComplete)
        {
            // Если текст уже полностью выведен - перейти к следующей строке
            dialogueRunner.RequestNextLine();
        }
    }
    
    private void OnOptionSelected(int index)
    {
        if (index < 0 || index >= optionButtons.Count)
            return;
            
        var button = optionButtons[index];
        if (button.ClassListContains("unavailable"))
            return;
        
        if (currentDialogueOptions != null && index < currentDialogueOptions.Length)
        {
            optionCompletionSource?.TrySetResult(currentDialogueOptions[index]);
        }
    }
    
    private void NavigateOptions(int direction)
    {
        if (optionButtons.Count == 0)
            return;
        
        // Cooldown для предотвращения дерганья со стика
        if (Time.time - lastNavigationTime < navigationCooldown)
            return;
        
        lastNavigationTime = Time.time;
        
        // Убрать выделение со всех кнопок
        foreach (var btn in optionButtons)
            btn.RemoveFromClassList("selected");
        
        selectedOptionIndex += direction;
        if (selectedOptionIndex < 0)
            selectedOptionIndex = optionButtons.Count - 1;
        else if (selectedOptionIndex >= optionButtons.Count)
            selectedOptionIndex = 0;
            
        optionButtons[selectedOptionIndex].AddToClassList("selected");
    }
    
    private void ConfirmSelectedOption()
    {
        if (optionButtons.Count == 0)
            return;
            
        OnOptionSelected(selectedOptionIndex);
    }
    
    private async YarnTask TypewriterEffect(string text, CancellationToken ct)
    {
        dialogueTextLabel.text = "";
        currentVisibleChars = 0;
        
        float delay = 1f / charactersPerSecond;
        
        foreach (char c in text)
        {
            if (ct.IsCancellationRequested)
                break;
                
            dialogueTextLabel.text += c;
            currentVisibleChars++;
            
            await YarnTask.Delay((int)(delay * 1000), ct).SuppressCancellationThrow();
        }
        
        dialogueTextLabel.text = text;
        isLineComplete = true;
    }
    
    private void ToggleHistory()
    {
        bool isHidden = historyPanel.ClassListContains("hidden");
        
        if (isHidden)
        {
            ShowHistory();
            historyPanel.RemoveFromClassList("hidden");
        }
        else
        {
            historyPanel.AddToClassList("hidden");
        }
    }
    
    private void ShowHistory()
    {
        historyScroll.Clear();
        
        foreach (var entry in history)
        {
            var entryElement = new VisualElement();
            entryElement.AddToClassList("history-entry");
            
            if (!string.IsNullOrEmpty(entry.characterName))
            {
                var nameLabel = new Label(entry.characterName);
                nameLabel.AddToClassList("history-entry-name");
                entryElement.Add(nameLabel);
            }
            
            var textLabel = new Label(entry.text);
            textLabel.AddToClassList("history-entry-text");
            entryElement.Add(textLabel);
            
            historyScroll.Add(entryElement);
        }
    }
    
    private void ToggleAuto()
    {
        isAutoMode = !isAutoMode;
        UpdateButtonStates();
    }
    
    private void ToggleSkip()
    {
        isSkipping = !isSkipping;
        UpdateButtonStates();
        
        if (isSkipping)
        {
            // Если текст еще выводится - пропустить анимацию
            if (!isLineComplete && useTypewriter && currentLine != null)
            {
                typewriterCTS?.Cancel();
                dialogueTextLabel.text = currentLine.TextWithoutCharacterName.Text;
                isLineComplete = true;
                if (dialogueRunner != null)
                    dialogueRunner.RequestHurryUpLine();
            }
            // Если текст уже полностью выведен - сразу перейти к следующей реплике
            else if (isLineComplete && dialogueRunner != null)
            {
                dialogueRunner.RequestNextLine();
            }
        }
    }
    
    private void UpdateButtonStates()
    {
        if (autoButton != null)
        {
            if (isAutoMode)
                autoButton.AddToClassList("active");
            else
                autoButton.RemoveFromClassList("active");
        }
        
        if (skipButton != null)
        {
            if (isSkipping)
                skipButton.AddToClassList("active");
            else
                skipButton.RemoveFromClassList("active");
        }
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
        if (dialogueBox != null) dialogueBox.style.display = DisplayStyle.None;
        if (optionsContainer != null) optionsContainer.style.display = DisplayStyle.None;
        if (bottomControls != null) bottomControls.style.display = DisplayStyle.None;
        if (characterNameContainer != null) characterNameContainer.style.display = DisplayStyle.None;
        if (historyPanel != null) historyPanel.AddToClassList("hidden");
    }
}