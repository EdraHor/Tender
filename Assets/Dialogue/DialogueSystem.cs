using UnityEngine;
using Yarn.Unity;

public class DialogueSystem : MonoBehaviour
{
    [SerializeField] private DialogueRunner dialogueRunner;
    [SerializeField] private string startNode = "Start";
    [SerializeField] private bool playOnStart = true;
    
    private SaveVariableStorage saveStorage;
    
    void Awake()
    {
        saveStorage = GetComponent<SaveVariableStorage>();
        if (saveStorage == null)
        {
            saveStorage = gameObject.AddComponent<SaveVariableStorage>();
            Debug.Log("[DialogueSystem] SaveVariableStorage добавлен автоматически");
        }
    }
    
    void Start()
    {
        if (playOnStart)
            StartDialogue();
    }
    
    public void StartDialogue(string node = null)
    {
        if (dialogueRunner.IsDialogueRunning)
        {
            Debug.Log("Диалог уже запущен");
            return;
        }

        var nodeToStart = node ?? startNode;
        dialogueRunner.StartDialogue(nodeToStart);
    }
    
    public VariableStorageBehaviour VariableStorage => saveStorage;
}