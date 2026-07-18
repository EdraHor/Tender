using System.Collections;
using UnityEngine;
using Yarn.Unity;

public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private DialogueRunner dialogueRunner;
    [SerializeField] private string startNode = "Start";
    [SerializeField] private bool playOnStart = true;
    
    private SaveVariableStorage _saveStorage;
    
    private void Awake()
    {
        _saveStorage = GetComponent<SaveVariableStorage>();
        if (_saveStorage == null)
        {
            _saveStorage = gameObject.AddComponent<SaveVariableStorage>();
            Debug.Log("[DialogueSystem] SaveVariableStorage added automatically");
        }
    }
    
    private void Start()
    {
        if (playOnStart)
            StartDialogue();
    }
    
    public void StartDialogue(string? node = null)
    {
        if (dialogueRunner.IsDialogueRunning)
        {
            Debug.Log("[DialogueSystem] Dialogue already running");
            return;
        }

        dialogueRunner.StartDialogue(node ?? startNode);
    }
    
    [YarnCommand("SetEmotion")]
    public static void SetEmotion(string characterId, string emotion)
    {
        Debug.Log($"[DialogueSystem] Character '{characterId}' emotion: {emotion}");
        // TODO: Найти персонажа и установить эмоцию
        // var character = FindObjectOfType<CharacterManager>()?.GetCharacter(characterId);
        // character?.SetEmotion(emotion);
    }
    
    [YarnCommand("Wait")]
    public static IEnumerator Wait(float seconds)
    {
        Debug.Log($"[DialogueSystem] Waiting {seconds} seconds...");
        yield return new WaitForSeconds(seconds);
    }
    
    [YarnCommand("PlayAnimation")]
    public static void PlayAnimation(string animationName)
    {
        Debug.Log($"[DialogueSystem] Playing animation: {animationName}");
        // TODO: Воспроизвести анимацию
    }
    
    [YarnCommand("MoveCharacter")]
    public static void MoveCharacter(string characterId, string targetPosition)
    {
        Debug.Log($"[DialogueSystem] Moving '{characterId}' to '{targetPosition}'");
        // TODO: Переместить персонажа
    }
    
    public VariableStorageBehaviour VariableStorage => _saveStorage;
}