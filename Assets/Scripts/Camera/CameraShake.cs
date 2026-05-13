using System.Collections;
using UnityEngine;

/// <summary>
/// CameraShake — shake com bias direcional opcional.
///
/// Shake(intensity, duration)
///   Shake randômico puro — usado para explosões, dano recebido, etc.
///
/// Shake(intensity, duration, direction, directionBias)
///   O primeiro deslocamento vai na direção do golpe (bias), os seguintes
///   decaem para oscilação randômica normal. Isso ancora o impacto no espaço:
///   um soco pra direita empurra a câmera pra direita antes de oscilar.
///   directionBias (0–1): 0 = randômico puro, 1 = só na direção.
///   Recomendado: 0.6–0.75 para golpes normais, 0.85 para finisher/counter.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Limites")]
    [SerializeField] private float maxIntensity = 0.5f;
    [SerializeField] private float maxDuration  = 0.5f;

    private Vector3   _originalPos;
    private Coroutine _routine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance     = this;
        _originalPos = transform.localPosition;
    }

    /// <summary>Shake randômico puro — sem bias direcional.</summary>
    public void Shake(float intensity, float duration)
    {
        Shake(intensity, duration, Vector2.zero, 0f);
    }

    /// <summary>
    /// Shake com bias direcional.
    /// <paramref name="direction"/> deve ser normalizado (player → inimigo).
    /// <paramref name="directionBias"/> entre 0 (randômico puro) e 1 (só na direção).
    /// </summary>
    public void Shake(float intensity, float duration, Vector2 direction, float directionBias)
    {
        intensity = Mathf.Clamp(intensity, 0f, maxIntensity);
        duration  = Mathf.Clamp(duration,  0f, maxDuration);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShakeRoutine(intensity, duration, direction, directionBias));
    }

    private IEnumerator ShakeRoutine(float intensity, float duration, Vector2 direction, float directionBias)
    {
        float elapsed = 0f;
        bool firstFrame = true;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float strength = Mathf.Lerp(intensity, 0f, elapsed / duration);

            Vector2 offset;

            if (firstFrame && directionBias > 0f && direction != Vector2.zero)
            {
                // Primeiro frame: deslocamento na direção do golpe com bias total.
                // O jogador vê a câmera "empurrar" na direção do impacto antes de oscilar.
                Vector2 randComponent = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f));
                offset    = Vector2.Lerp(randComponent, direction, directionBias) * strength;
                firstFrame = false;
            }
            else
            {
                // Frames seguintes: randômico puro com decaimento normal.
                offset = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * strength;
            }

            transform.localPosition = _originalPos + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        transform.localPosition = _originalPos;
        _routine = null;
    }

    public void Cancel()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        transform.localPosition = _originalPos;
    }
}