using UnityEngine;

/// <summary>
/// Инициализирует сцену при ее загрузке
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    public enum InputMode { Player, UI, Dialogue }
    
    [SerializeField] private InputMode _initialInputMode = InputMode.Player;
    
    private void Awake()
    {
        switch (_initialInputMode)
        {
            case InputMode.Player:
                G.Input?.EnablePlayer();
                break;
            case InputMode.UI:
                G.Input?.EnableUI();
                break;
            case InputMode.Dialogue:
                G.Input?.EnableDialogue();
                break;
        }
        
        Debug.Log($"[SceneInitializer] {gameObject.scene.name} → {_initialInputMode} режим");
    }
}