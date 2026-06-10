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
    public Vector2 offset = new Vector2(0f, 1f);

    [Header("Limites do mapa")]
    public bool useBounds = false;

    [Tooltip("Opção 1 — Arraste um GameObject com BoxCollider2D que cobre todo o seu nível.\n" +
             "Crie um GameObject vazio, adicione BoxCollider2D, redimensione para cobrir o fundo, marque como Trigger.")]
    public Collider2D worldBoundsCollider;

    [Tooltip("Opção 2 — Arraste o SpriteRenderer do background principal (funciona se for sprite único).")]
    public SpriteRenderer backgroundSprite;

    [Tooltip("Opção 3 — Preencha manualmente se não usar nenhuma das opções acima.")]
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

        if (useBounds)
            RecalculateBounds();

        if (target != null)
        {
            Vector3 startPos = CalculateTargetPosition();
            transform.position = startPos;
            currentVerticalTarget = target.position.y;
            lastTargetX = target.position.x;
        }
    }

    // Prioridade: Collider2D > SpriteRenderer > valores manuais
    public void RecalculateBounds()
    {
        if (worldBoundsCollider != null)
        {
            Bounds b = worldBoundsCollider.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
            return;
        }

        if (backgroundSprite != null)
        {
            Bounds b = backgroundSprite.bounds;
            minX = b.min.x;
            maxX = b.max.x;
            minY = b.min.y;
            maxY = b.max.y;
        }

        // Se nenhum foi setado, mantém os valores manuais já digitados
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Look-ahead horizontal
        float moveDir = Mathf.Sign(target.position.x - lastTargetX);
        if (Mathf.Abs(target.position.x - lastTargetX) > 0.01f)
            targetLookAhead = moveDir * lookAheadDistance;

        currentLookAhead = Mathf.Lerp(currentLookAhead, targetLookAhead, lookAheadSmooth);
        lastTargetX = target.position.x;

        // Dead zone vertical
        if (Mathf.Abs(target.position.y - currentVerticalTarget) > verticalDeadZone)
            currentVerticalTarget = Mathf.Lerp(currentVerticalTarget, target.position.y, verticalSmoothSpeed);

        // Posição desejada
        Vector3 desired = new Vector3(
            target.position.x + currentLookAhead + offset.x,
            currentVerticalTarget + offset.y,
            transform.position.z
        );

        // Suavização
        Vector3 smoothed = new Vector3(
            Mathf.Lerp(transform.position.x, desired.x, smoothSpeed),
            Mathf.Lerp(transform.position.y, desired.y, verticalSmoothSpeed),
            desired.z
        );

        // Clamp dentro dos bounds
        if (useBounds)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth  = halfHeight * cam.aspect;

            float clampMinX = minX + halfWidth;
            float clampMaxX = maxX - halfWidth;
            float clampMinY = minY + halfHeight;
            float clampMaxY = maxY - halfHeight;

            // Se o mundo for menor que a câmera, centraliza em vez de travar em posição inválida
            smoothed.x = clampMinX > clampMaxX
                ? (minX + maxX) / 2f
                : Mathf.Clamp(smoothed.x, clampMinX, clampMaxX);

            smoothed.y = clampMinY > clampMaxY
                ? (minY + maxY) / 2f
                : Mathf.Clamp(smoothed.y, clampMinY, clampMaxY);
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

        // Atualiza preview no editor
        if (worldBoundsCollider != null)
        {
            Bounds b = worldBoundsCollider.bounds;
            minX = b.min.x; maxX = b.max.x;
            minY = b.min.y; maxY = b.max.y;
        }
        else if (backgroundSprite != null)
        {
            Bounds b = backgroundSprite.bounds;
            minX = b.min.x; maxX = b.max.x;
            minY = b.min.y; maxY = b.max.y;
        }

        // Vermelho = borda total do mundo
        Gizmos.color = Color.red;
        Vector3 center = new Vector3((minX + maxX) / 2f, (minY + maxY) / 2f, 0f);
        Vector3 size   = new Vector3(maxX - minX, maxY - minY, 0f);
        Gizmos.DrawWireCube(center, size);

        // Ciano = área onde o centro da câmera pode andar
        Camera c = GetComponentInChildren<Camera>();
        if (c == null) c = Camera.main;
        if (c != null)
        {
            float hh = c.orthographicSize;
            float hw = hh * c.aspect;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(center, new Vector3(
                Mathf.Max(0, maxX - minX - hw * 2f),
                Mathf.Max(0, maxY - minY - hh * 2f), 0f));
        }
    }
}