using UnityEngine;
using UnityEngine.UIElements;

public static class UINavigationHelper
{
    // Цветовая схема
    public static class Colors
    {
        public static readonly Color PopupBackground = new Color(0.098f, 0.098f, 0.137f); // rgb(25, 25, 35)
        public static readonly Color Border = new Color(0.392f, 0.392f, 0.471f); // rgb(100, 100, 120)
        public static readonly Color Text = new Color(0.784f, 0.784f, 0.784f); // rgb(200, 200, 200)
        public static readonly Color TextFocused = Color.white;
        
        public static readonly Color SelectedIdle = new Color(0.314f, 0.471f, 0.784f, 0.4f); // rgba(80, 120, 200, 0.4)
        public static readonly Color SelectedFocus = new Color(0.392f, 0.549f, 0.863f, 1f); // rgba(100, 140, 220, 1)
        public static readonly Color SelectedHover = new Color(0.392f, 0.549f, 0.863f, 0.7f); // rgba(100, 140, 220, 0.7)
        
        public static readonly Color UnselectedFocus = new Color(0.314f, 0.471f, 0.784f, 0.9f); // rgba(80, 120, 200, 0.9)
        public static readonly Color UnselectedHover = new Color(0.235f, 0.235f, 0.314f, 0.8f); // rgba(60, 60, 80, 0.8)
    }
    
    // Применить базовые стили popup
    public static void ApplyPopupStyles(VisualElement popup, Rect bounds)
    {
        popup.style.position = Position.Absolute;
        popup.style.left = bounds.x;
        popup.style.top = bounds.yMax + 2;
        popup.style.width = bounds.width;
        popup.pickingMode = PickingMode.Position;
        
        popup.style.backgroundColor = Colors.PopupBackground;
        SetBorders(popup, Colors.Border, 1);
        SetPadding(popup, 0);
    }
    
    // Убрать все стили кнопки
    public static void ResetButtonStyles(Button button)
    {
        SetBorders(button, Color.clear, 0);
        SetBorderRadius(button, 0);
        SetMargins(button, 0);
        button.style.color = Colors.Text;
    }
    
    // Установить границы
    public static void SetBorders(VisualElement element, Color color, int width)
    {
        element.style.borderLeftColor = element.style.borderRightColor = 
        element.style.borderTopColor = element.style.borderBottomColor = color;
        
        element.style.borderLeftWidth = element.style.borderRightWidth = 
        element.style.borderTopWidth = element.style.borderBottomWidth = width;
    }
    
    // Установить радиусы углов
    public static void SetBorderRadius(VisualElement element, int radius)
    {
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = 
        element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
    
    // Установить отступы
    public static void SetMargins(VisualElement element, int margin)
    {
        element.style.marginLeft = element.style.marginRight = 
        element.style.marginTop = element.style.marginBottom = margin;
    }
    
    // Установить padding
    public static void SetPadding(VisualElement element, int padding)
    {
        element.style.paddingLeft = element.style.paddingRight = 
        element.style.paddingTop = element.style.paddingBottom = padding;
    }
    
    // Применить стили для элемента списка (dropdown item)
    public static void SetupDropdownItemStyles(Button button, bool isSelected)
    {
        ResetButtonStyles(button);
        button.style.backgroundColor = isSelected ? Colors.SelectedIdle : Color.clear;
        
        // Focus handlers
        button.RegisterCallback<FocusInEvent>(evt => {
            var btn = evt.target as Button;
            bool selected = btn.ClassListContains("simple-dropdown__item--selected");
            btn.style.backgroundColor = selected ? Colors.SelectedFocus : Colors.UnselectedFocus;
            btn.style.color = Colors.TextFocused;
        });
        
        button.RegisterCallback<FocusOutEvent>(evt => {
            var btn = evt.target as Button;
            bool selected = btn.ClassListContains("simple-dropdown__item--selected");
            btn.style.backgroundColor = selected ? Colors.SelectedIdle : Color.clear;
            btn.style.color = Colors.Text;
        });
        
        // Hover handlers
        button.RegisterCallback<MouseEnterEvent>(evt => {
            var btn = evt.target as Button;
            if (btn != btn.panel?.focusController.focusedElement)
            {
                bool selected = btn.ClassListContains("simple-dropdown__item--selected");
                btn.style.backgroundColor = selected ? Colors.SelectedHover : Colors.UnselectedHover;
            }
        });
        
        button.RegisterCallback<MouseLeaveEvent>(evt => {
            var btn = evt.target as Button;
            if (btn != btn.panel?.focusController.focusedElement)
            {
                bool selected = btn.ClassListContains("simple-dropdown__item--selected");
                btn.style.backgroundColor = selected ? Colors.SelectedIdle : Color.clear;
            }
        });
    }
    
    // Обработка вертикальной навигации внутри контейнера
    public static void HandleVerticalNavigation(NavigationMoveEvent evt, VisualElement container)
    {
        var target = evt.target as VisualElement;
        if (target?.parent != container) return;
        
        int currentIndex = container.IndexOf(target);
        int newIndex = currentIndex;
        
        if (evt.direction == NavigationMoveEvent.Direction.Down)
            newIndex = (currentIndex + 1) % container.childCount;
        else if (evt.direction == NavigationMoveEvent.Direction.Up)
            newIndex = (currentIndex - 1 + container.childCount) % container.childCount;
        else
        {
            // Блокируем горизонтальное движение
            evt.PreventDefault();
            evt.StopPropagation();
            return;
        }
        
        (container[newIndex] as Button)?.Focus();
        evt.PreventDefault();
        evt.StopPropagation();
    }
    
    // Настройка вертикальной навигации для контейнера
    public static void SetupVerticalNavigation(VisualElement container)
    {
        container.RegisterCallback<NavigationMoveEvent>(
            evt => HandleVerticalNavigation(evt, container), 
            TrickleDown.TrickleDown);
    }
    
    // Регистрация обработчика клика вне элемента
    public static EventCallback<PointerDownEvent> RegisterOutsideClickHandler(
        VisualElement root, 
        VisualElement target, 
        VisualElement excludeElement, 
        System.Action onOutsideClick)
    {
        EventCallback<PointerDownEvent> handler = evt => {
            var clickTarget = evt.target as VisualElement;
            if (clickTarget != null && 
                !target.Contains(clickTarget) && 
                !excludeElement.Contains(clickTarget))
            {
                onOutsideClick?.Invoke();
            }
        };
        
        root.RegisterCallback(handler, TrickleDown.TrickleDown);
        return handler;
    }
    
    // Отмена регистрации обработчика
    public static void UnregisterOutsideClickHandler(
        VisualElement root, 
        EventCallback<PointerDownEvent> handler)
    {
        root?.UnregisterCallback(handler, TrickleDown.TrickleDown);
    }
}
