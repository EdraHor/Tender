using UnityEngine;
using System.Collections.Generic;

public enum FingerType
{
    Thumb = 0,
    Index = 1,
    Middle = 2,
    Ring = 3,
    Pinky = 4
}

public enum HandSide
{
    Left,
    Right
}

[System.Serializable]
public class FingerChain
{
    public FingerType Type;
    public string Name; // Для удобства в инспекторе
    public Transform[] Nodes; // 3 фаланги (у большого пальца может быть меньше, но в Mixamo обычно 3)
    
    [HideInInspector] public float CurrentBendValue; // 0..1
    [HideInInspector] public bool IsPressed;
    
    // Состояние для анимации
    [HideInInspector] public float AnimationTime;
}

[System.Serializable]
public class HandDefinition
{
    public HandSide Side;
    public Transform RootBone; // Кисть (Wrist)
    public List<FingerChain> Fingers = new List<FingerChain>();
    
    public FingerChain GetFinger(FingerType type)
    {
        return Fingers.Find(f => f.Type == type);
    }
}

public class PianoFingerController : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private AnimationCurve _pressCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private AnimationCurve _releaseCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    [SerializeField] private float _pressSpeed = 10f;
    [SerializeField] private float _releaseSpeed = 10f;

    [Header("Bend Settings")]
    [Tooltip("Ось вращения пальца. Для Mixamo это обычно Z (0,0,1) или X (1,0,0). Включи Gizmos чтобы проверить.")]
    public Vector3 BendAxis = new Vector3(0, 0, 1); 
    
    [Range(-45, 45)] public float BaseCurlAngle = 5f; // Легкий загиб в покое
    [Range(-45, 45)] public float PressCurlAngle = 45f; // Загиб при нажатии
    public Transform RootBone;
    
    [Header("Hands")]
    public HandDefinition LeftHand = new HandDefinition { Side = HandSide.Left };
    public HandDefinition RightHand = new HandDefinition { Side = HandSide.Right };

    [Header("Debug Info")]
    [SerializeField] private bool _showGizmos = true;

    private void Update()
    {
        // Обновляем состояние анимации (значения 0..1)
        UpdateHandLogic(LeftHand);
        UpdateHandLogic(RightHand);
    }

    private void LateUpdate()
    {
        // Применяем вращение ПОВЕРХ аниматора
        ApplyHandRotations(LeftHand);
        ApplyHandRotations(RightHand);
    }

    private void UpdateHandLogic(HandDefinition hand)
    {
        foreach (var finger in hand.Fingers)
        {
            if (finger.IsPressed)
            {
                finger.AnimationTime += Time.deltaTime * _pressSpeed;
                finger.CurrentBendValue = _pressCurve.Evaluate(Mathf.Clamp01(finger.AnimationTime));
            }
            else
            {
                finger.AnimationTime += Time.deltaTime * _releaseSpeed;
                // При отпускании мы идем как бы "дальше" по времени, но используем кривую Release
                // Или проще: сбрасываем время и используем ReleaseCurve
                // Для простоты сделаем линейный возврат к 0, управляемый кривой Release
                // Но чтобы не усложнять стейт-машину, сделаем простую интерполяцию к цели
                
                // Переопределим логику для плавности:
                // Будем стремиться к 0 или 1
                finger.CurrentBendValue = Mathf.MoveTowards(finger.CurrentBendValue, 0f, Time.deltaTime * _releaseSpeed);
            }
        }
    }

    private void ApplyHandRotations(HandDefinition hand)
    {
        if (hand.RootBone == null) return;

        foreach (var finger in hand.Fingers)
        {
            if (finger.Nodes == null || finger.Nodes.Length == 0) continue;

            // Вычисляем угол: Базовый + (Нажатие * Сила)
            float targetAngle = BaseCurlAngle + (finger.CurrentBendValue * PressCurlAngle);
            
            // Применяем ко всем фалангам
            for (int i = 0; i < finger.Nodes.Length; i++)
            {
                Transform node = finger.Nodes[i];
                if (node == null) continue;

                // Коэффициент для кончика пальца (обычно кончик гнется сильнее)
                float phalanxMultiplier = 1f + (i * 0.1f); 

                // ВАЖНО: localRotation *= ... добавляет вращение к тому, что сделал Animator
                node.localRotation *= Quaternion.AngleAxis(targetAngle * phalanxMultiplier, BendAxis);
            }
        }
    }

    // --- Public API ---

    public void PressFinger(HandSide side, FingerType fingerType)
    {
        var hand = side == HandSide.Left ? LeftHand : RightHand;
        var finger = hand.GetFinger(fingerType);
        if (finger != null)
        {
            finger.IsPressed = true;
            finger.AnimationTime = 0f; // Ресет времени для кривой атаки
        }
    }

    public void ReleaseFinger(HandSide side, FingerType fingerType)
    {
        var hand = side == HandSide.Left ? LeftHand : RightHand;
        var finger = hand.GetFinger(fingerType);
        if (finger != null)
        {
            finger.IsPressed = false;
            // AnimationTime не сбрасываем, логика в Update плавно вернет к 0
        }
    }
    
    // --- Auto Setup Helpers ---
    
    public void FindBonesAutomatic(Transform root)
    {
        if (root == null) return;

        // Ищем корни рук
        Transform leftHandRoot = FindRecursive(root, "mixamorig:LeftHand");
        Transform rightHandRoot = FindRecursive(root, "mixamorig:RightHand");

        if (leftHandRoot) SetupHand(LeftHand, leftHandRoot, "Left");
        if (rightHandRoot) SetupHand(RightHand, rightHandRoot, "Right");
    }

    private void SetupHand(HandDefinition hand, Transform handRoot, string sidePrefix)
    {
        hand.RootBone = handRoot;
        hand.Fingers.Clear();

        // Mixamo naming convention: mixamorig:LeftHandIndex1, etc.
        // Или просто LeftHandIndex1 если префикса нет. Будем искать гибко.
        
        AddFingerToHand(hand, FingerType.Thumb, handRoot, sidePrefix, "Thumb");
        AddFingerToHand(hand, FingerType.Index, handRoot, sidePrefix, "Index");
        AddFingerToHand(hand, FingerType.Middle, handRoot, sidePrefix, "Middle");
        AddFingerToHand(hand, FingerType.Ring, handRoot, sidePrefix, "Ring");
        AddFingerToHand(hand, FingerType.Pinky, handRoot, sidePrefix, "Pinky");
    }

    private void AddFingerToHand(HandDefinition hand, FingerType type, Transform handRoot, string sidePrefix, string fingerName)
    {
        FingerChain chain = new FingerChain();
        chain.Type = type;
        chain.Name = fingerName;
        
        List<Transform> nodes = new List<Transform>();
        
        // Пытаемся найти 3 фаланги (1, 2, 3)
        // Паттерны поиска: "mixamorig:{Side}Hand{Finger}{i}"
        
        for (int i = 1; i <= 3; i++)
        {
            string searchName = $"mixamorig:{sidePrefix}Hand{fingerName}{i}";
            Transform bone = FindRecursive(handRoot, searchName);
            
            // Если не нашли с mixamorig, ищем без него (иногда бывает просто LeftHandIndex1)
            if (bone == null)
                bone = FindRecursive(handRoot, $"{sidePrefix}Hand{fingerName}{i}");
            
            if (bone != null)
                nodes.Add(bone);
        }

        chain.Nodes = nodes.ToArray();
        hand.Fingers.Add(chain);
    }

    private Transform FindRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private void OnDrawGizmos()
    {
        if (!_showGizmos) return;

        DrawHandGizmos(LeftHand);
        DrawHandGizmos(RightHand);
    }

    private void DrawHandGizmos(HandDefinition hand)
    {
        if (hand.RootBone == null) return;

        foreach (var finger in hand.Fingers)
        {
            if (finger.Nodes == null) continue;

            Gizmos.color = finger.IsPressed ? Color.red : Color.green;

            for (int i = 0; i < finger.Nodes.Length; i++)
            {
                Transform node = finger.Nodes[i];
                if (node == null) continue;

                Gizmos.DrawWireSphere(node.position, 0.005f);

                // Рисуем линию к следующей кости
                if (i < finger.Nodes.Length - 1 && finger.Nodes[i+1] != null)
                {
                    Gizmos.DrawLine(node.position, finger.Nodes[i+1].position);
                }
                
                // Рисуем ось вращения (синяя линия)
                Vector3 axisWorld = node.TransformDirection(BendAxis);
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(node.position, node.position + axisWorld * 0.02f);
                Gizmos.color = finger.IsPressed ? Color.red : Color.green;
            }
        }
    }
}