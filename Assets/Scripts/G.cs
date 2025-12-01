using UnityEngine;

public static class G
{
    public static HUDController HUDController => GetOrFind(ref _hudController);
    private static HUDController _hudController;
    
    public static SaveManager Save => GetOrFind(ref _save);
    private static SaveManager _save;
    
    public static PlayerController Player => GetOrFind(ref _player);
    private static PlayerController _player;
    
    public static DialogueSystem Dialogue => GetOrFind(ref _dialogue);
    private static DialogueSystem _dialogue;
    
    public static InputController Input => GetOrFind(ref _input);
    private static InputController _input;
    
    public static GraphicsManager Graphics => GetOrFind(ref _graphics);
    private static GraphicsManager _graphics;
    
    public static AudioManager Audio => GetOrFind(ref _audio);
    private static AudioManager _audio;
    
    private static T GetOrFind<T>(ref T cached) where T : Object
    {
        if (cached == null)
        {
            var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            
            if (objects.Length == 0)
            {
                Debug.LogWarning($"[G] Система {typeof(T).Name} не найдена в сцене!");
                return null;
            }
            
            if (objects.Length > 1)
                Debug.LogWarning($"[G] Найдено {objects.Length} объектов {typeof(T).Name}! Используется первый: {objects[0].name}");
            
            cached = objects[0];
        }
        return cached;
    }
    
    /// <summary>
    /// Очистка кэша при перезапуске в редакторе Unity
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _hudController = null;
        _save = null;
        _player = null;
        _dialogue = null;
        _input = null;
        _graphics = null;
        _audio = null;
        
        Debug.Log("[G] Кэш систем очищен");
    }
}