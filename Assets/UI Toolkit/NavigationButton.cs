using UnityEngine;
using UnityEngine.UIElements;
using System;

/// <summary>
/// Кастомная кнопка для навигационных меню с встроенными стилями и поведением.
/// 
/// ИСПОЛЬЗОВАНИЕ:
/// - Для простых меню (пауза, магазин, списки) с однородными кнопками
/// - В UXML: <NavigationButton text="Продолжить" name="ResumeButton" />
/// - Если useHoveredClass=true, hover управляется извне (для keyboard-mode логики)
/// 
/// НЕ ПОДХОДИТ:
/// - Для главного меню с разными стилями кнопок (menu-button, tab-button)
/// - Когда нужны специфичные анимации (padding-left и т.д.)
/// 
/// В таких случаях используйте обычный Button с ручными обработчиками.
/// </summary>
[UxmlElement]
public partial class NavigationButton : Button
{
    private bool _useCustomStyles = true;
    private bool _isSelected;
    private bool _useHoveredClass = false;
    
    [UxmlAttribute]
    public bool useCustomStyles
    {
        get => _useCustomStyles;
        set
        {
            _useCustomStyles = value;
            if (value) ApplyCustomStyles();
        }
    }
    
    [UxmlAttribute]
    public bool isSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            UpdateSelectionState();
        }
    }
    
    /// <summary>
    /// Если true, использует класс "hovered" вместо прямых стилей для hover.
    /// Полезно для меню с keyboard-mode логикой.
    /// </summary>
    [UxmlAttribute]
    public bool useHoveredClass
    {
        get => _useHoveredClass;
        set => _useHoveredClass = value;
    }
    
    public NavigationButton() : base()
    {
        Initialize();
    }
    
    public NavigationButton(Action clickEvent) : base(clickEvent)
    {
        Initialize();
    }
    
    private void Initialize()
    {
        if (_useCustomStyles)
            ApplyCustomStyles();
        
        RegisterCallback<FocusInEvent>(OnFocusIn);
        RegisterCallback<FocusOutEvent>(OnFocusOut);
        
        if (!_useHoveredClass)
        {
            RegisterCallback<MouseEnterEvent>(OnMouseEnter);
            RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
        }
    }
    
    private void ApplyCustomStyles()
    {
        UINavigationHelper.ResetButtonStyles(this);
        UpdateSelectionState();
    }
    
    private void UpdateSelectionState()
    {
        if (!_useCustomStyles) return;
        
        if (_isSelected)
            AddToClassList("navigation-button--selected");
        else
            RemoveFromClassList("navigation-button--selected");
        
        style.backgroundColor = _isSelected ? UINavigationHelper.Colors.SelectedIdle : Color.clear;
    }
    
    private void OnFocusIn(FocusInEvent evt)
    {
        if (!_useCustomStyles) return;
        
        style.backgroundColor = _isSelected 
            ? UINavigationHelper.Colors.SelectedFocus 
            : UINavigationHelper.Colors.UnselectedFocus;
        style.color = UINavigationHelper.Colors.TextFocused;
    }
    
    private void OnFocusOut(FocusOutEvent evt)
    {
        if (!_useCustomStyles) return;
        
        style.backgroundColor = _isSelected 
            ? UINavigationHelper.Colors.SelectedIdle 
            : Color.clear;
        style.color = UINavigationHelper.Colors.Text;
    }
    
    private void OnMouseEnter(MouseEnterEvent evt)
    {
        if (!_useCustomStyles || this == panel?.focusController.focusedElement) return;
        
        style.backgroundColor = _isSelected 
            ? UINavigationHelper.Colors.SelectedHover 
            : UINavigationHelper.Colors.UnselectedHover;
    }
    
    private void OnMouseLeave(MouseLeaveEvent evt)
    {
        if (!_useCustomStyles || this == panel?.focusController.focusedElement) return;
        
        style.backgroundColor = _isSelected 
            ? UINavigationHelper.Colors.SelectedIdle 
            : Color.clear;
    }
}