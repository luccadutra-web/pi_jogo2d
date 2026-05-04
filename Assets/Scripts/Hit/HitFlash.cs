using System.Collections;
using UnityEngine;

/// <summary>
/// HitFlash — pisca branco ao levar dano.
/// Adicione em qualquer inimigo que tenha SpriteRenderer.
///
/// Uso:
///   GetComponent<HitFlash>().Flash();
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class HitFlash : MonoBehaviour
{
    [Header("Flash")]
    [Tooltip("Duração do flash branco em segundos.")]
    [SerializeField] private float flashDuration = 0.08f;
    [Tooltip("Quantas vezes pisca.")]
    [SerializeField] private int   flashCount    = 2;

    private SpriteRenderer _sr;
    private Color          _originalColor;
    private Coroutine      _routine;

    void Awake()
    {
        _sr            = GetComponent<SpriteRenderer>();
        _originalColor = _sr.color;
    }

    public void Flash()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        for (int i = 0; i < flashCount; i++)
        {
            // Branco total
            _sr.color = Color.white;
            yield return new WaitForSecondsRealtime(flashDuration);

            // Volta à cor original
            _sr.color = _originalColor;
            yield return new WaitForSecondsRealtime(flashDuration * 0.5f);
        }

        _sr.color = _originalColor;
        _routine  = null;
    }

    /// <summary>
    /// Restaura a cor imediatamente — use ao destruir o objeto.
    /// </summary>
    public void Cancel()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _sr.color = _originalColor;
    }

    void OnDestroy() => Cancel();
}