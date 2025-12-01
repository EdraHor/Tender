using UnityEngine;
using UnityEngine.UI;

public class TabSystem : MonoBehaviour
{
    [SerializeField] private Button[] _tabButtons;
    [SerializeField] private GameObject[] _tabPanels;
    [SerializeField] private Color _activeColor = Color.white;
    [SerializeField] private Color _inactiveColor = new Color(0.7f, 0.7f, 0.7f);
    
    private int _currentTab;
    
    private void Start()
    {
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            int index = i;
            _tabButtons[i].onClick.AddListener(() => SwitchTab(index));
        }
        
        SwitchTab(0);
    }
    
    public void SwitchTab(int index)
    {
        _currentTab = index;
        
        for (int i = 0; i < _tabPanels.Length; i++)
            _tabPanels[i].SetActive(i == index);
        
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            var img = _tabButtons[i].GetComponent<Image>();
            if (img) img.color = i == index ? _activeColor : _inactiveColor;
        }
    }
    
    public void NextTab()
    {
        SwitchTab((_currentTab + 1) % _tabPanels.Length);
    }
    
    public void PreviousTab()
    {
        SwitchTab((_currentTab - 1 + _tabPanels.Length) % _tabPanels.Length);
    }
}
