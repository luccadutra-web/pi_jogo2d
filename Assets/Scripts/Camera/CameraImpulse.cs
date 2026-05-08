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
    [SerializeField] private float parryZoomAmount   = 0.08f;
    [Tooltip("Duração do zoom in do parry, em segundos.")]
    [SerializeField] private float parryZoomInTime   = 0.06f;
    [Tooltip("Duração do zoom out do parry, em segundos.")]
    [SerializeField] private float parryZoomOutTime  = 0.14f;

    [Header("Counter zoom")]
    [Tooltip("Quanto a câmera aproxima durante um counter attack.")]
    [SerializeField] private float counterZoomAmount  = 0.12f;
    [Tooltip("Duração do zoom in do counter, em segundos.")]
    [SerializeField] private float counterZoomInTime  = 0.05f;
    [Tooltip("Duração do zoom out do counter, em segundos.")]
    [SerializeField] private float counterZoomOutTime = 0.18f;

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
    /// </summary>
    public void ParryZoom()
    {
        DoZoom(parryZoomAmount, parryZoomInTime, parryZoomOutTime);
    }

    /// <summary>
    /// Dispare no momento de um counter attack (chamado por MeleeWeapon.ApplyHit).
    /// </summary>
    public void CounterZoom()
    {
        DoZoom(counterZoomAmount, counterZoomInTime, counterZoomOutTime);
    }

    private void DoZoom(float amount, float inTime, float outTime)
    {
        if (targetCamera == null) return;

        if (_zoomCoroutine != null)
        {
            StopCoroutine(_zoomCoroutine);
            // NÃO reseta para _baseSize aqui — o ZoomRoutine parte do tamanho atual,
            // permitindo que um counter acumule o zoom sobre um parry ainda ativo.
        }

        _zoomCoroutine = StartCoroutine(ZoomRoutine(amount, inTime, outTime));
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