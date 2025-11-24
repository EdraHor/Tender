using UnityEngine;
using Yarn.Unity;

public class DialogueStarter : MonoBehaviour
{
    [SerializeField] private DialogueRunner dialogueRunner;
    [SerializeField] private string startNode = "Start";
    
    [SerializeField] private bool playOnStart = true; // Запустить при Start()
    
    void Start()
    {
        if (playOnStart)
        {
            StartDialogue();
        }
    }
    
    public void StartDialogue(string node = null)
    {
        // Проверяем, не идёт ли уже диалог
        if (dialogueRunner.IsDialogueRunning)
        {
            Debug.Log("Диалог уже запущен");
            return;
        }

        var nodeToStart = node ?? startNode;
        dialogueRunner.StartDialogue(nodeToStart);
    }
}