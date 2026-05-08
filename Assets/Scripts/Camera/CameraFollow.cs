using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    
    [Header("Alvo")]
    public Transform target;

    [Header("Suavização")]
    [Range(0.01f, 1f)]
    public float smoothSpeed = 0.12f;       
    public float verticalSmoothSpeed = 0.08f;

    [Header("Look-ahead")]
    public float lookAheadDistance = 1.8f;  
    public float lookAheadSmooth = 0.1f;    

    [Header("Dead zone vertical")]
    public float verticalDeadZone = 0.6f;   

    [Header("Offset fixo")]
    public Vector2 offset = new Vector2(0f, 5.0f); 

    [Header("Limites do mapa")]
    public bool useBounds = false;
    public float minX, maxX, minY, maxY;

   
    private float currentLookAhead;
    private float targetLookAhead;
    private float lastTargetX;
    private float currentVerticalTarget;
    private Camera cam;

    
    void Start()
    {
        cam = GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;

        if (target != null)
        {
            
            Vector3 startPos = CalculateTargetPosition();
            transform.position = startPos;
            currentVerticalTarget = target.position.y;
            lastTargetX = target.position.x;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

       
        float moveDir = Mathf.Sign(target.position.x - lastTargetX);
        if (Mathf.Abs(target.position.x - lastTargetX) > 0.01f)
            targetLookAhead = moveDir * lookAheadDistance;

        currentLookAhead = Mathf.Lerp(currentLookAhead, targetLookAhead, lookAheadSmooth);
        lastTargetX = target.position.x;

        
        if (Mathf.Abs(target.position.y - currentVerticalTarget) > verticalDeadZone)
            currentVerticalTarget = Mathf.Lerp(currentVerticalTarget, target.position.y, verticalSmoothSpeed);

        
        Vector3 desired = new Vector3(
            target.position.x + currentLookAhead + offset.x,
            currentVerticalTarget + offset.y,
            transform.position.z
        );

        
        Vector3 smoothed = new Vector3(
            Mathf.Lerp(transform.position.x, desired.x, smoothSpeed),
            Mathf.Lerp(transform.position.y, desired.y, verticalSmoothSpeed),
            desired.z
        );

       
        if (useBounds)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth  = halfHeight * cam.aspect;

            smoothed.x = Mathf.Clamp(smoothed.x, minX + halfWidth,  maxX - halfWidth);
            smoothed.y = Mathf.Clamp(smoothed.y, minY + halfHeight, maxY - halfHeight);
        }

        transform.position = smoothed;
    }

    
    Vector3 CalculateTargetPosition()
    {
        return new Vector3(
            target.position.x + offset.x,
            target.position.y + offset.y,
            transform.position.z
        );
    }

    
    public void SnapToTarget()
    {
        if (target == null) return;
        transform.position = CalculateTargetPosition();
        currentVerticalTarget = target.position.y;
        currentLookAhead = 0f;
        targetLookAhead = 0f;
    }

    
    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;

        Gizmos.color = Color.cyan;
        Vector3 center = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f);
        Vector3 size   = new Vector3(maxX - minX, maxY - minY, 0f);
        Gizmos.DrawWireCube(center, size);
    }
}