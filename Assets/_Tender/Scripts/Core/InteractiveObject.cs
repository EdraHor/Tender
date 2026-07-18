using UnityEngine;
using UnityEngine.Events;

public class InteractiveObject : MonoBehaviour
{
    [Header("Настройки")]
    public string Description = "Интерактивный объект";
    public Color HighlightColor = Color.white;
    public UnityEvent OnClick;
    
    private Material _material;
    private Color _originalColor;
    private readonly int _baseColorID = Shader.PropertyToID("_BaseColor");
    
    void Start()
    {
        _material = GetComponent<Renderer>().material;
        _originalColor = _material.GetColor(_baseColorID);
    }
    
    void OnMouseEnter()
    {
        _material.SetColor(_baseColorID, _originalColor + HighlightColor * 0.3f);
        G.HUDController.ShowInteractionDesc(Description);
    }
    
    void OnMouseExit()
    {
        _material.SetColor(_baseColorID, _originalColor);
        G.HUDController.HideInteractionDesc();
    }
    
    void OnMouseDown()
    {
        OnClick?.Invoke();
    }
}