using UnityEngine;

public class InputController : MonoBehaviour
{
    private GameInputs _actions;
    
    public GameInputs.PlayerActions Player => _actions.Player;
    public GameInputs.DialogueActions Dialogue => _actions.Dialogue;
    public GameInputs.UIActions UI => _actions.UI;
    public GameInputs.MinigameActions Minigame => _actions.Minigame;
    
    private void Awake()
    {
        _actions = new GameInputs();
        DontDestroyOnLoad(gameObject);
        
        _actions.Player.Enable();
    }
    
    private void OnDestroy()
    {
        _actions?.Dispose();
    }
    
    public void EnablePlayer()
    {
        DisableAll();
        _actions.Player.Enable();
        Debug.Log("Включен ввод для игрока");
    }
    
    public void EnableDialogue()
    {
        DisableAll();
        _actions.Dialogue.Enable();
        Debug.Log("Включен ввод для диалогов");
    }
    
    public void EnableUI()
    {
        DisableAll();
        _actions.UI.Enable();
        Debug.Log("Включен ввод для UI");
    }
    
    public void EnableMinigame()
    {
        DisableAll();
        _actions.Minigame.Enable();
        Debug.Log("Включен ввод миниигр");
    }
    
    public void DisableAll()
    {
        _actions.Player.Disable();
        _actions.Dialogue.Disable();
        _actions.UI.Disable();
        _actions.Minigame.Disable();
    }
}