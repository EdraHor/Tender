using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;

[UxmlElement]
public partial class SimpleDropdown : VisualElement
{
    private static SimpleDropdown _currentOpen;
    
    private Label _label;
    private VisualElement _popup;
    private List<string> _choices = new List<string>();
    private int _index;
    private EventCallback<PointerDownEvent> _outsideClickHandler;
    
    public List<string> choices
    {
        get => _choices;
        set
        {
            _choices = value;
            UpdateLabel();
        }
    }
    
    public int index
    {
        get => _index;
        set
        {
            if (value >= 0 && value < _choices.Count)
            {
                _index = value;
                UpdateLabel();
            }
        }
    }
    
    public event Action<ChangeEvent<int>> valueChanged;
    public bool IsOpen => _popup != null;
    
    public SimpleDropdown()
    {
        AddToClassList("simple-dropdown");
        
        _label = new Label();
        _label.AddToClassList("simple-dropdown__label");
        Add(_label);
        
        RegisterCallback<ClickEvent>(evt => {
            if (IsOpen) ClosePopup();
            else OpenPopup();
            evt.StopPropagation();
        });
    }
    
    public void OpenPopup()
    {
        if (IsOpen) return;
        
        // Закрыть предыдущий открытый dropdown
        _currentOpen?.ClosePopup();
        _currentOpen = this;
        
        var root = panel.visualTree;
        _popup = new VisualElement();
        _popup.AddToClassList("simple-dropdown__popup");
        
        UINavigationHelper.ApplyPopupStyles(_popup, _label.worldBound);
        UINavigationHelper.SetupVerticalNavigation(_popup);
        
        for (int i = 0; i < _choices.Count; i++)
        {
            int idx = i;
            var item = new Button(() => SelectItem(idx));
            item.text = _choices[i];
            item.AddToClassList("simple-dropdown__item");
            
            bool isSelected = i == _index;
            if (isSelected)
                item.AddToClassList("simple-dropdown__item--selected");
            
            UINavigationHelper.SetupDropdownItemStyles(item, isSelected);
            _popup.Add(item);
        }
        
        root.Add(_popup);
        
        // Обработчик клика вне popup - закрывает его
        _outsideClickHandler = UINavigationHelper.RegisterOutsideClickHandler(
            root, _popup, this, ClosePopup);
        
        // Фокус на выбранный элемент
        (_popup[_index] as Button)?.Focus();
    }
    
    public void ClosePopup()
    {
        if (_popup == null) return;
        
        UINavigationHelper.UnregisterOutsideClickHandler(panel?.visualTree, _outsideClickHandler);
        _outsideClickHandler = null;
        
        _popup.RemoveFromHierarchy();
        _popup = null;
        
        if (_currentOpen == this)
            _currentOpen = null;
    }
    
    private void SelectItem(int idx)
    {
        if (idx >= 0 && idx < _choices.Count && idx != _index)
        {
            int oldValue = _index;
            _index = idx;
            UpdateLabel();
            
            using (var evt = ChangeEvent<int>.GetPooled(oldValue, _index))
            {
                evt.target = this;
                valueChanged?.Invoke(evt);
                SendEvent(evt);
            }
        }
        ClosePopup();
        Focus();
    }
    
    private void UpdateLabel()
    {
        _label.text = (_choices.Count > 0 && _index >= 0 && _index < _choices.Count) 
            ? _choices[_index] 
            : "";
    }
    
    public void SetValueWithoutNotify(string value)
    {
        int idx = _choices.IndexOf(value);
        if (idx >= 0)
            _index = idx;
        UpdateLabel();
    }
}