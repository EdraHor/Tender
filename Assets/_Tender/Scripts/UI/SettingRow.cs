using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class SettingRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _label;
    [SerializeField] private TMP_Dropdown _dropdown;
    [SerializeField] private Slider _slider;
    [SerializeField] private Toggle _toggle;
    [SerializeField] private TextMeshProUGUI _sliderValue;
    
    private void Awake()
    {
        HideAll();
    }
    
    private void HideAll()
    {
        if (_dropdown) _dropdown.gameObject.SetActive(false);
        if (_slider) _slider.gameObject.SetActive(false);
        if (_toggle) _toggle.gameObject.SetActive(false);
        if (_sliderValue) _sliderValue.gameObject.SetActive(false);
    }
    
    public void SetupDropdown(string label, string[] options, UnityAction<int> callback)
    {
        HideAll();
        _label.text = label;
        _dropdown.gameObject.SetActive(true);
        _dropdown.ClearOptions();
        _dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
        _dropdown.onValueChanged.AddListener(callback);
    }
    
    public void SetupSlider(string label, float min, float max, float value, UnityAction<float> callback)
    {
        HideAll();
        _label.text = label;
        _slider.gameObject.SetActive(true);
        _slider.minValue = min;
        _slider.maxValue = max;
        _slider.value = value;
        _slider.onValueChanged.AddListener(callback);
        
        if (_sliderValue)
        {
            _sliderValue.gameObject.SetActive(true);
            _slider.onValueChanged.AddListener(v => _sliderValue.text = Mathf.RoundToInt(v).ToString());
            _sliderValue.text = Mathf.RoundToInt(value).ToString();
        }
    }
    
    public void SetupToggle(string label, bool value, UnityAction<bool> callback)
    {
        HideAll();
        _label.text = label;
        _toggle.gameObject.SetActive(true);
        _toggle.isOn = value;
        _toggle.onValueChanged.AddListener(callback);
    }
    
    public void SetEnabled(bool enabled)
    {
        if (_dropdown) _dropdown.interactable = enabled;
        if (_slider) _slider.interactable = enabled;
        if (_toggle) _toggle.interactable = enabled;
    }
}