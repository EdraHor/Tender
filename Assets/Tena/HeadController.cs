using UnityEngine;
using UnityEngine.Animations.Rigging;

public class HeadController : MonoBehaviour
{
    public enum LookMode
    {
        HeadOnly,
        EyesOnly,
        HeadAndEyes
    }
    
    public enum TargetMode
    {
        WorldRaycast,    // Raycast в мир
        CameraPlane      // Привязка к плоскости камеры
    }
    
    [Header("Settings")]
    [SerializeField] private LookMode lookMode = LookMode.HeadAndEyes;
    [SerializeField] private TargetMode targetMode = TargetMode.WorldRaycast;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float lookDistance = 10f;
    
    [Header("Head")]
    [SerializeField] private MultiAimConstraint headConstraint;
    [SerializeField] private Transform headBone;
    [SerializeField] private float headSpeed = 5f;
    [SerializeField] private float maxHeadRotationSpeed = 90f; // Градусов в секунду
    
    [Header("Eyes Blend Shapes")]
    [SerializeField] private SkinnedMeshRenderer faceMesh;
    [SerializeField] private string blendShapeLeft = "EyeLeft";
    [SerializeField] private string blendShapeRight = "EyeRight";
    [SerializeField] private string blendShapeUp = "EyeUp";
    [SerializeField] private string blendShapeDown = "EyeDown";
    [SerializeField] private float eyeSpeed = 10f;
    [SerializeField] private float eyeIntensity = 100f;
    
    private Transform headTarget;
    private int indexLeft, indexRight, indexUp, indexDown;
    private float currentWeightLeft, currentWeightRight, currentWeightUp, currentWeightDown;
    private Vector3 targetVelocity;
    
    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        
        if (headConstraint != null && headConstraint.data.sourceObjects.Count > 0)
            headTarget = headConstraint.data.sourceObjects[0].transform;
        
        if (faceMesh != null)
        {
            indexLeft = faceMesh.sharedMesh.GetBlendShapeIndex(blendShapeLeft);
            indexRight = faceMesh.sharedMesh.GetBlendShapeIndex(blendShapeRight);
            indexUp = faceMesh.sharedMesh.GetBlendShapeIndex(blendShapeUp);
            indexDown = faceMesh.sharedMesh.GetBlendShapeIndex(blendShapeDown);
        }
    }
    
    void Update()
    {
        Vector3 mouseWorld = GetMouseWorldPosition();
        
        // Двигаем голову с ограничением скорости
        if (lookMode != LookMode.EyesOnly && headTarget != null)
        {
            // Вычисляем максимальное перемещение за кадр
            float maxDistance = maxHeadRotationSpeed * Mathf.Deg2Rad * Vector3.Distance(headBone.position, headTarget.position) * Time.deltaTime;
            
            // SmoothDamp для плавности + ограничение максимальной скорости
            Vector3 newPos = Vector3.SmoothDamp(headTarget.position, mouseWorld, ref targetVelocity, 1f / headSpeed, maxDistance / Time.deltaTime);
            headTarget.position = newPos;
        }
        
        // Считаем веса для blend shapes
        if (lookMode != LookMode.HeadOnly && faceMesh != null && headBone != null)
        {
            Vector3 localDir = headBone.InverseTransformDirection((mouseWorld - headBone.position).normalized);
            
            float targetLeft = Mathf.Clamp(-localDir.x, 0, 1) * eyeIntensity;
            float targetRight = Mathf.Clamp(localDir.x, 0, 1) * eyeIntensity;
            float targetUp = Mathf.Clamp(localDir.y, 0, 1) * eyeIntensity;
            float targetDown = Mathf.Clamp(-localDir.y, 0, 1) * eyeIntensity;
            
            currentWeightLeft = Mathf.Lerp(currentWeightLeft, targetLeft, Time.deltaTime * eyeSpeed);
            currentWeightRight = Mathf.Lerp(currentWeightRight, targetRight, Time.deltaTime * eyeSpeed);
            currentWeightUp = Mathf.Lerp(currentWeightUp, targetUp, Time.deltaTime * eyeSpeed);
            currentWeightDown = Mathf.Lerp(currentWeightDown, targetDown, Time.deltaTime * eyeSpeed);
        }
    }
    
    void LateUpdate()
    {
        if (lookMode != LookMode.HeadOnly && faceMesh != null)
        {
            if (indexLeft >= 0) faceMesh.SetBlendShapeWeight(indexLeft, currentWeightLeft);
            if (indexRight >= 0) faceMesh.SetBlendShapeWeight(indexRight, currentWeightRight);
            if (indexUp >= 0) faceMesh.SetBlendShapeWeight(indexUp, currentWeightUp);
            if (indexDown >= 0) faceMesh.SetBlendShapeWeight(indexDown, currentWeightDown);
        }
    }
    
    Vector3 GetMouseWorldPosition()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        
        if (targetMode == TargetMode.CameraPlane)
        {
            // Привязка к плоскости на фиксированном расстоянии от камеры
            return ray.origin + ray.direction * lookDistance;
        }
        else
        {
            // Raycast в мир
            return Physics.Raycast(ray, out RaycastHit hit, 100f) ? hit.point : ray.origin + ray.direction * lookDistance;
        }
    }
}