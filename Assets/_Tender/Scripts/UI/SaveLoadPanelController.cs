using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System;

/// <summary>
/// Переиспользуемый контроллер панели сохранений/загрузки
/// Поддерживает 3 режима: NewGame, Load, Save
/// </summary>
public class SaveLoadPanelController
{
    public enum Mode
    {
        NewGame,  // Выбор слота для новой игры
        Load,     // Загрузка существующего сохранения
        Save      // Сохранение текущей игры
    }
    
    private readonly VisualElement _panelRoot;
    private Mode _currentMode;
    
    // События для внешней обработки
    public event Action<int> OnSlotSelected;
    public event Action<int, bool> OnSlotAction; // (slotIndex, isEmpty)
    
    public SaveLoadPanelController(VisualElement panelRoot)
    {
        _panelRoot = panelRoot;
    }
    
    /// <summary>
    /// Открывает панель в указанном режиме
    /// </summary>
    public void Open(Mode mode)
    {
        _currentMode = mode;
        
        // Меняем заголовок
        var title = _panelRoot.Q<Label>(className: "panel-title");
        title.text = mode switch
        {
            Mode.NewGame => "// НОВАЯ ИГРА",
            Mode.Load => "// ЗАГРУЗКА",
            Mode.Save => "// СОХРАНЕНИЕ",
            _ => "// СОХРАНЕНИЯ"
        };
        
        RebuildSaveSlots();
    }
    
    /// <summary>
    /// Фокусирует первый доступный элемент
    /// </summary>
    public void FocusFirstElement()
    {
        var scroll = _panelRoot.Q<ScrollView>("SaveFilesList");
        var firstButton = scroll.Q<Button>();
        
        if (firstButton != null && firstButton.enabledSelf)
            firstButton.Focus();
        else
            _panelRoot.Q<Button>("BackFromLoadButton")?.Focus();
    }
    
    private void RebuildSaveSlots()
    {
        var listContainer = _panelRoot.Q<ScrollView>("SaveFilesList");
        listContainer.Clear();
        
        if (G.Save == null) return;
        
        var saves = G.Save.GetAllSaves();
        
        for (int i = 0; i < saves.Length; i++)
        {
            int slotNum = i + 1;
            SaveMetadata data = saves[i];
            bool isEmpty = (data == null);
            
            // Для режима Load - показываем только существующие сохранения
            if (_currentMode == Mode.Load && isEmpty)
                continue;
            
            var slotBtn = CreateSlotVisuals(slotNum, data, isEmpty);
            slotBtn.clicked += () => HandleSlotClick(slotNum, isEmpty);
            
            listContainer.Add(slotBtn);
        }
        
        // Если в режиме загрузки нет сохранений
        if (listContainer.childCount == 0 && _currentMode == Mode.Load)
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
            actionLabel.text = _currentMode switch
            {
                Mode.NewGame => "СОЗДАТЬ",
                Mode.Save => "СОХРАНИТЬ",
                _ => ""
            };
        }
        else
        {
            // Красивое форматирование времени (чч:мм)
            TimeSpan ts = TimeSpan.FromSeconds(data.playTime);
            string timeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}";
            
            detailsLabel.text = $"{data.saveDate}  |  Глава {data.chapter}  |  {timeStr}";
            actionLabel.text = _currentMode switch
            {
                Mode.NewGame => "ПЕРЕЗАПИСАТЬ",
                Mode.Load => "ЗАГРУЗИТЬ",
                Mode.Save => "ПЕРЕЗАПИСАТЬ",
                _ => ""
            };
        }
        
        infoContainer.Add(titleLabel);
        infoContainer.Add(detailsLabel);
        
        btn.Add(infoContainer);
        btn.Add(actionLabel);
        
        return btn;
    }
    
    private void HandleSlotClick(int slotNum, bool isEmpty)
    {
        OnSlotSelected?.Invoke(slotNum);
        OnSlotAction?.Invoke(slotNum, isEmpty);
    }
}
