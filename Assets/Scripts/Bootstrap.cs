using UnityEngine;

/// <summary>
/// Инициализирует все системы игры при запуске
/// Создаётся автоматически, не нужно добавлять в сцену вручную
/// </summary>
public class Bootstrap : MonoBehaviour
{
    /// <summary>
    /// Автоматический запуск ДО загрузки первой сцены
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Создаём GameObject который переживёт смену сцен
        var bootstrapObject = new GameObject("[Bootstrap]");
        DontDestroyOnLoad(bootstrapObject);
        bootstrapObject.AddComponent<Bootstrap>();
        
        Debug.Log("[Bootstrap] Создан");
    }
    
    private void Awake()
    {
        Debug.Log("=== Bootstrap запущен ===");

        // Спавним систему ввода если ее нет
        if (FindAnyObjectByType<InputController>(FindObjectsInactive.Include) == null)
        {
            var inputObject = new GameObject("[InputController]");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<InputController>();
        }
        if (FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include) == null)
        {
            var inputObject = new GameObject("[SaveManager]");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<SaveManager>();
        }
        if (FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include) == null)
        {
            var inputObject = new GameObject("[AudioManager]");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<AudioManager>();
        }
        
        // Принудительно инициализируем системы в нужном порядке
        _ = G.Save;
        _ = G.Input;
        _ = G.Dialogue;
        _ = G.Player;
        _ = G.HUDController;
        
        Debug.Log("=== Системы готовы ===");
    }
}