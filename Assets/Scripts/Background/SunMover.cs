using UnityEngine;

/// <summary>
/// SunMover — Move o sol/lua em arco lento e independente da câmera.
///
/// SETUP:
///   1. Adicione este script no GameObject do sol/lua.
///   2. Configure o pivô de órbita (orbitCenter) — geralmente o centro
///      inferior da tela, ou um GameObject vazio posicionado lá.
///   3. Ajuste orbitRadius, cycleSeconds e os ângulos de início/fim.
///
/// COMPORTAMENTO:
///   - O sol sobe da esquerda (angleStart) até a direita (angleEnd)
///     ao longo de cycleSeconds.
///   - Ao completar, reinicia do início (loop) ou pode ficar parado
///     no ângulo final se loop = false.
///   - O script move o GameObject diretamente em world space,
///     independente do parallax das nuvens/céu.
///
/// DICA DE POSICIONAMENTO:
///   - orbitCenter em Y negativo (abaixo do chão) cria um arco mais suave.
///   - angleStart = 200°, angleEnd = 340° → nascer no lado esquerdo até
///     o lado direito passando pelo topo.
/// </summary>
public class SunMover : MonoBehaviour
{
    [Header("Órbita")]
    [Tooltip("Centro da órbita. Deixe vazio para usar a posição inicial deste GameObject como pivô.")]
    [SerializeField] private Transform orbitCenter;

    [Tooltip("Raio da órbita em unidades Unity.")]
    [SerializeField] private float orbitRadius = 8f;

    [Tooltip("Ângulo inicial em graus (0 = direita, 90 = cima, 180 = esquerda).")]
    [SerializeField] private float angleStart = 200f;

    [Tooltip("Ângulo final em graus.")]
    [SerializeField] private float angleEnd = 340f;

    [Header("Tempo")]
    [Tooltip("Duração do ciclo completo (nascer → pôr) em segundos.")]
    [SerializeField] private float cycleSeconds = 120f;

    [Tooltip("Se verdadeiro, reinicia do início ao completar o ciclo.")]
    [SerializeField] private bool loop = true;

    [Header("Câmera (para manter visível)")]
    [Tooltip("Se marcado, o sol acompanha a posição X da câmera com parallaxFactor baixo.")]
    [SerializeField] private bool followCameraX = true;
    [SerializeField] [Range(0f, 0.5f)] private float cameraParallaxFactor = 0.05f;
    [Tooltip("Deixe vazio para usar Camera.main.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private float   _elapsed    = 0f;
    private bool    _completed  = false;
    private Vector3 _pivotPos;
    private Vector3 _previousCameraPos;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        // Usa a posição do orbitCenter se atribuído, senão usa a origem do script
        _pivotPos = orbitCenter != null
            ? orbitCenter.position
            : transform.position;

        if (followCameraX && cameraTransform != null)
            _previousCameraPos = cameraTransform.position;

        // Posiciona no ângulo inicial
        ApplyAngle(angleStart);
    }

    void Update()
    {
        if (_completed && !loop) return;

        _elapsed += Time.deltaTime;

        if (loop && _elapsed >= cycleSeconds)
            _elapsed -= cycleSeconds;

        float t     = Mathf.Clamp01(_elapsed / cycleSeconds);
        float angle = Mathf.LerpAngle(angleStart, angleEnd, t);

        ApplyAngle(angle);

        if (!loop && t >= 1f)
        {
            _completed = true;
            if (debugLog) Debug.Log("[SunMover] Ciclo completo.");
        }

        // Parallax leve com a câmera
        if (followCameraX && cameraTransform != null)
        {
            float deltaX = (cameraTransform.position.x - _previousCameraPos.x) * cameraParallaxFactor;
            _pivotPos.x += deltaX;

            Vector3 pos = transform.position;
            pos.x += deltaX;
            transform.position = pos;

            _previousCameraPos = cameraTransform.position;
        }
    }

    private void ApplyAngle(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(
            Mathf.Cos(rad) * orbitRadius,
            Mathf.Sin(rad) * orbitRadius,
            0f
        );
        transform.position = _pivotPos + offset;

        if (debugLog)
            Debug.Log($"[SunMover] angle={angleDeg:F1}° pos={transform.position}");
    }

    // Reinicia o ciclo pelo código (ex: ao recarregar a fase)
    public void ResetCycle()
    {
        _elapsed   = 0f;
        _completed = false;
        ApplyAngle(angleStart);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 pivot = orbitCenter != null ? orbitCenter.position : transform.position;

        // Desenha o arco de órbita
        int steps = 60;
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.6f);
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= steps; i++)
        {
            float t   = (float)i / steps;
            float ang = Mathf.LerpAngle(angleStart, angleEnd, t) * Mathf.Deg2Rad;
            Vector3 p = pivot + new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * orbitRadius;
            if (i > 0) Gizmos.DrawLine(prev, p);
            prev = p;
        }

        // Marca início e fim do arco
        float startRad = angleStart * Mathf.Deg2Rad;
        float endRad   = angleEnd   * Mathf.Deg2Rad;
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(pivot + new Vector3(Mathf.Cos(startRad), Mathf.Sin(startRad), 0f) * orbitRadius, 0.15f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(pivot + new Vector3(Mathf.Cos(endRad),   Mathf.Sin(endRad),   0f) * orbitRadius, 0.15f);

        // Centro
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(pivot, 0.2f);
    }
}