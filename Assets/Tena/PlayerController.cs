using UnityEngine;
using UnityEngine.AI;

public class PlayerController : MonoBehaviour
{
    public NavMeshAgent agent;
    public Animator animator;
    public HeadController headController;
    
    [SerializeField] private float turnThreshold = 30f;
    [SerializeField] private float turnSpeed = 180f;
    
    [Header("Idle Variations")]
    [SerializeField] private int idleVariationsCount = 3;
    [SerializeField] private float minIdleTime = 3f;
    [SerializeField] private float maxIdleTime = 8f;

    private float nextIdleTime;
    private Vector3 targetPosition;
    private bool isMoving;
    private bool isTurning;
    
    void Start()
    {
        agent.updateRotation = false;
        nextIdleTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
    }
    
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                targetPosition = hit.point;
                agent.SetDestination(targetPosition);
                isMoving = true;
                isTurning = false;
                
                headController.SetLookTarget(targetPosition);
            }
        }
        
        if (isMoving)
        {
            Vector3 direction = (targetPosition - transform.position);
            direction.y = 0;
            
            if (direction.magnitude > 0.1f)
            {
                float angle = Vector3.SignedAngle(transform.forward, direction.normalized, Vector3.up);
                
                // Начинаем поворот
                if (!isTurning && Mathf.Abs(angle) > turnThreshold)
                {
                    isTurning = true;
                    agent.isStopped = true;
                }
                
                if (isTurning)
                {
                    // Крутимся анимацией
                    animator.SetBool("IsTurning", true);
                    float turnValue = Mathf.Clamp(angle / 90f, -1f, 1f);
                    animator.SetFloat("TurnAngle", turnValue);
                    animator.SetFloat("Speed", 0);
                    
                    // Реальный поворот
                    Quaternion targetRot = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
                    
                    // Довернулись?
                    if (Mathf.Abs(angle) < 5f)
                    {
                        isTurning = false;
                        agent.isStopped = false;
                        animator.SetBool("IsTurning", false);
                    }
                }
                else
                {
                    // Идем
                    animator.SetFloat("TurnAngle", 0);
                    animator.SetFloat("Speed", agent.velocity.magnitude);
                    
                    // Небольшая подстройка направления
                    Quaternion targetRot = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 3f);
                }
            }
            
            // Дошли?
            if (!isTurning && agent.remainingDistance < 0.1f && !agent.pathPending)
            {
                isMoving = false;
                animator.SetBool("IsTurning", false);
                animator.SetFloat("Speed", 0);
                animator.SetFloat("TurnAngle", 0);
                headController.ResetToMouse();
            }
        }
        CheckIdleVariation();
    }
    
    void CheckIdleVariation()
    {
        // Только если стоим
        if (!isMoving && !isTurning && Time.time > nextIdleTime)
        {
            animator.SetFloat("IdleIndex", Random.Range(0, idleVariationsCount));
            animator.SetTrigger("PlayIdleVariation");
        
            nextIdleTime = Time.time + Random.Range(minIdleTime, maxIdleTime);
        }
    }
}