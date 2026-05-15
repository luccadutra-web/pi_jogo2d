using System.Collections;
using UnityEngine;

/// <summary>
/// HitFlash v3 — três modos:
///
/// Flash()       — pisca ao RECEBER dano (inimigo ou player).
/// ImpactFlash() — frame branco ao ACERTAR um golpe (inimigo atingido).
/// ParryFlash()  — frame colorido de feedback ao EXECUTAR um parry (player).
///
/// INTENSIDADE — usa Lerp entre cor original e cor alvo em vez de sobrescrever
/// para branco puro. Isso preserva a silhueta do sprite em vez de virar um
/// retângulo sólido. Ajuste flashIntensity (0=invisível, 1=branco total) no Inspector.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class HitFlash : MonoBehaviour
{
    [Header("Flash ao receber dano")]
    [SerializeField] private float flashDuration  = 0.07f;
    [SerializeField] private int   flashCount     = 2;
    [Range(0f, 1f)]
    [Tooltip("0 = invisível, 1 = branco total. Comece em 0.55 e ajuste no Play Mode.")]
    [SerializeField] private float flashIntensity = 0.55f;

    [Header("Flash de impacto ao acertar")]
    [SerializeField] private float impactDuration  = 0.05f;
    [Range(0f, 1f)]
    [SerializeField] private float impactIntensity = 0.75f;
    [SerializeField] private Color impactColor     = Color.white;

    [Header("Flash de parry")]
    [SerializeField] private float parryDuration  = 0.12f;
    [Range(0f, 1f)]
    [SerializeField] private float parryIntensity = 0.90f;
    [Tooltip("Cor do flash de parry. Ciano/dourado comunicam 'sucesso' melhor que branco.")]
    [SerializeField] private Color parryColor     = new Color(1f, 0.9f, 0.2f); // dourado

    [Header("Flash de quebra de postura")]
    [Tooltip("Número de piscadas no flash de poise break.")]
    [SerializeField] private int   poiseBreakFlashCount    = 3;
    [SerializeField] private float poiseBreakFlashDuration = 0.06f;
    [Range(0f, 1f)]
    [SerializeField] private float poiseBreakIntensity     = 0.85f;
    [Tooltip("Laranja/vermelho comunica 'stagger pesado' e diferencia do flash branco de hit normal.")]
    [SerializeField] private Color poiseBreakColor         = new Color(1f, 0.35f, 0.05f); // laranja-fogo

    private SpriteRenderer _sr;
    private Color          _originalColor;
    private Coroutine      _routine;

    void Awake()
    {
        _sr            = GetComponent<SpriteRenderer>();
        _originalColor = _sr.color;
    }

    // ── Dano recebido ─────────────────────────────────────────────────────────

    public void Flash()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        Color flashColor = Color.Lerp(_originalColor, Color.white, flashIntensity);

        for (int i = 0; i < flashCount; i++)
        {
            _sr.color = flashColor;
            yield return new WaitForSecondsRealtime(flashDuration);

            _sr.color = _originalColor;
            yield return new WaitForSecondsRealtime(flashDuration * 0.5f);
        }

        _sr.color = _originalColor;
        _routine  = null;
    }

    // ── Impacto ao acertar ────────────────────────────────────────────────────

    /// <summary>
    /// Chame no inimigo atingido, dentro de MeleeWeapon.ApplyHit.
    /// Flash curto e intenso que comunica "golpe conectou" antes do HitStop terminar,
    /// depois encadeia o Flash normal de dano.
    /// </summary>
    public void ImpactFlash()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ImpactFlashRoutine());
    }

    private IEnumerator ImpactFlashRoutine()
    {
        Color target = Color.Lerp(_originalColor, impactColor, impactIntensity);
        _sr.color = target;
        yield return new WaitForSecondsRealtime(impactDuration);

        // Encadeia o flash de dano automaticamente
        yield return StartCoroutine(FlashRoutine());
    }

    // ── Parry ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Chame no player dentro de PlayerHealth.ExecuteParry.
    /// Flash dourado/ciano mais longo que o de dano — comunica "parry bem-sucedido"
    /// sem ambiguidade.
    /// </summary>
    public void ParryFlash()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ParryFlashRoutine());
    }

    private IEnumerator ParryFlashRoutine()
    {
        Color target = Color.Lerp(_originalColor, parryColor, parryIntensity);

        // Vai para a cor de parry
        _sr.color = target;
        yield return new WaitForSecondsRealtime(parryDuration);

        // Fade suave de volta — mais elegante que corte abrupto
        float elapsed = 0f;
        float fadeTime = parryDuration * 0.6f;
        while (elapsed < fadeTime)
        {
            elapsed   += Time.unscaledDeltaTime;
            _sr.color  = Color.Lerp(target, _originalColor, elapsed / fadeTime);
            yield return null;
        }

        _sr.color = _originalColor;
        _routine  = null;
    }

    // ── Quebra de postura ─────────────────────────────────────────────────────

    /// <summary>
    /// Chame no inimigo dentro de EnemyBehavior.OnPoiseBreak.
    /// Flash laranja-fogo em múltiplas piscadas — mais intenso e distinto do
    /// flash branco de hit normal, comunicando claramente que o stagger é pesado.
    /// </summary>
    public void PoiseBreakFlash()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(PoiseBreakFlashRoutine());
    }

    private IEnumerator PoiseBreakFlashRoutine()
    {
        Color target = Color.Lerp(_originalColor, poiseBreakColor, poiseBreakIntensity);

        for (int i = 0; i < poiseBreakFlashCount; i++)
        {
            _sr.color = target;
            yield return new WaitForSecondsRealtime(poiseBreakFlashDuration);

            _sr.color = _originalColor;
            yield return new WaitForSecondsRealtime(poiseBreakFlashDuration * 0.4f);
        }

        _sr.color = _originalColor;
        _routine  = null;
    }

    // ─────────────────────────────────────────────────────────────────────────

    public void Cancel()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _sr.color = _originalColor;
    }

    void OnDestroy() => Cancel();
}