using System.Collections;
using UnityEngine;

/// <summary>
/// Micro-zoom de câmera para parry e counter attack.
/// Inspirado no feedback visual de Blasphemous: um zoom curto e preciso
/// no momento exato do impacto amplifica a percepção de controle do jogador.
///
/// Requer Camera2D ortográfica ou câmera com orthographicSize.
/// Singleton — adicione em um GameObject persistente na cena (ex: CameraRig).
/// </summary>
public class CameraImpulse : MonoBehaviour
{
    public static CameraImpulse Instance { get; private set; }

    [Header("Referência")]
    [Tooltip("Câmera principal. Deixe vazio para usar Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Parry zoom")]
    [Tooltip("Quanto a câmera aproxima durante o parry (valor positivo = zoom in).")]
    [SerializeField] private float parryZoomAmount   = 0.14f;
    [Tooltip("Duração do zoom in do parry, em segundos reais.")]
    [SerializeField] private float parryZoomInTime   = 0.05f;
    [Tooltip("Duração do zoom out do parry, em segundos reais.")]
    [SerializeField] private float parryZoomOutTime  = 0.28f;
    [Tooltip("Se true, segura o zoom no pico enquanto o HitStop estiver ativo antes de fazer zoom out.\n" +
             "Cria o momento dramático de 'tela parada no pico do zoom' do Blasphemous.")]
    [SerializeField] private bool  parryHoldDuringHitStop = true;

    [Header("Counter zoom")]
    [Tooltip("Quanto a câmera aproxima durante um counter attack.")]
    [SerializeField] private float counterZoomAmount  = 0.12f;
    [Tooltip("Duração do zoom in do counter, em segundos.")]
    [SerializeField] private float counterZoomInTime  = 0.05f;
    [Tooltip("Duração do zoom out do counter, em segundos.")]
    [SerializeField] private float counterZoomOutTime = 0.18f;

    [Header("Attack zoom (light / heavy / finisher)")]
    [Tooltip("Intensidade máxima permitida para golpes normais. Mantenha baixo para não competir com o parry.")]
    [SerializeField] private float attackZoomMax = 0.03f;

    private float     _baseSize;
    private Coroutine _zoomCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera != null)
            _baseSize = targetCamera.orthographicSize;
    }

    /// <summary>
    /// Dispare no momento do parry bem-sucedido (chamado por PlayerHealth.ExecuteParry).
    /// Zoom in rápido → segura no pico durante o HitStop → zoom out lento.
    /// </summary>
    public void ParryZoom()
    {
        if (targetCamera == null) return;
        if (_zoomCoroutine != null) StopCoroutine(_zoomCoroutine);
        _zoomCoroutine = StartCoroutine(ParryZoomRoutine());
    }

    /// <summary>
    /// Dispare no momento de um counter attack (chamado por MeleeWeapon.ApplyHit).
    /// </summary>
    public void CounterZoom()
    {
        DoZoom(counterZoomAmount, counterZoomInTime, counterZoomOutTime);
    }

    /// <summary>
    /// Zoom para golpes normais. O amount é limitado por attackZoomMax para
    /// não competir visualmente com o parry.
    /// </summary>
    public void AttackZoom(float amount, float inTime = 0.04f, float outTime = 0.14f)
    {
        DoZoom(Mathf.Min(amount, attackZoomMax), inTime, outTime);
    }

    private void DoZoom(float amount, float inTime, float outTime)
    {
        if (targetCamera == null) return;
        if (_zoomCoroutine != null) StopCoroutine(_zoomCoroutine);
        _zoomCoroutine = StartCoroutine(ZoomRoutine(amount, inTime, outTime));
    }

    /// <summary>
    /// Rotina de parry: zoom in rápido → segura no pico enquanto HitStop estiver
    /// ativo → zoom out lento. Cria o momento dramático estilo Blasphemous onde
    /// a tela "para" no zoom antes de soltar.
    /// </summary>
    private IEnumerator ParryZoomRoutine()
    {
        float startSize  = targetCamera.orthographicSize;
        float targetSize = _baseSize * (1f - parryZoomAmount);
        if (targetSize > startSize)
            targetSize = startSize * (1f - parryZoomAmount * 0.5f);

        // ── Zoom in ───────────────────────────────────────────────────────────
        float t = 0f;
        while (t < parryZoomInTime)
        {
            t += Time.unscaledDeltaTime;
            targetCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t / parryZoomInTime);
            yield return null;
        }
        targetCamera.orthographicSize = targetSize;

        // ── Segura no pico enquanto o HitStop estiver ativo ───────────────────
        // O HitStop congela timeScale mas não unscaledDeltaTime, então a coroutine
        // avança normalmente. Aguardamos IsActive zerar antes de fazer zoom out —
        // o jogador vê a tela "travada" no zoom máximo durante todo o freeze.
        if (parryHoldDuringHitStop)
            while (HitStop.Instance != null && HitStop.Instance.IsActive)
                yield return null;

        // ── Zoom out lento ────────────────────────────────────────────────────
        t = 0f;
        while (t < parryZoomOutTime)
        {
            t += Time.unscaledDeltaTime;
            targetCamera.orthographicSize = Mathf.Lerp(targetSize, _baseSize, t / parryZoomOutTime);
            yield return null;
        }

        targetCamera.orthographicSize = _baseSize;
        _zoomCoroutine = null;
    }

    private IEnumerator ZoomRoutine(float amount, float inTime, float outTime)
    {
        // Parte do tamanho atual da câmera (pode estar no meio de outro zoom).
        float startSize  = targetCamera.orthographicSize;
        float targetSize = _baseSize * (1f - amount);

        // Se já estamos mais próximos que o target (ex: parry ainda ativo),
        // usa o menor valor — counter sempre é mais agressivo que parry.
        if (targetSize > startSize)
            targetSize = startSize * (1f - amount * 0.5f);

        // Zoom in (usando tempo real — não pausa com HitStop)
        float t = 0f;
        while (t < inTime)
        {
            t += Time.unscaledDeltaTime;
            targetCamera.orthographicSize = Mathf.Lerp(startSize, targetSize, t / inTime);
            yield return null;
        }
        targetCamera.orthographicSize = targetSize;

        // Zoom out sempre retorna ao _baseSize
        t = 0f;
        while (t < outTime)
        {
            t += Time.unscaledDeltaTime;
            targetCamera.orthographicSize = Mathf.Lerp(targetSize, _baseSize, t / outTime);
            yield return null;
        }

        targetCamera.orthographicSize = _baseSize;
        _zoomCoroutine = null;
    }

    /// <summary>
    /// Atualiza o tamanho base se a câmera mudar de zoom por outro sistema (ex: zoom de level).
    /// </summary>
    public void RefreshBaseSize()
    {
        if (targetCamera != null)
            _baseSize = targetCamera.orthographicSize;
    }
}