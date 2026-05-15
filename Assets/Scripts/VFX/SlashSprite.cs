using System.Collections;
using UnityEngine;

/// <summary>
/// SlashSprite — sprite de slash ancorado na arma durante o swing.
///
/// ── COMO FUNCIONA ─────────────────────────────────────────────────────────────
///
///   Diferente do SlashTrail (TrailRenderer que desenha o caminho percorrido),
///   o SlashSprite exibe um sprite estático ancorado ao attackPoint.
///   Ele se move junto com a arma durante o active e faz fade out ao ser
///   desancorado — igual ao SlashTrail, mas visualmente é um sprite único.
///
///   Fluxo:
///     1. VfxManager.SpawnSlashSprite() tira do pool e ancora ao attackPoint
///     2. SlashSprite fica visível (alpha 1) enquanto ancorado
///     3. VfxManager.ReturnActiveTrail() desancora → OnTransformParentChanged
///     4. SlashSprite para de seguir a arma e faz fade out suave
///     5. VfxAutoReturn devolve ao pool após o fade
///
/// ── SETUP DO PREFAB ───────────────────────────────────────────────────────────
///
///   1. Crie um GameObject com SpriteRenderer e este componente
///   2. SpriteRenderer:
///        Sprite   = slash_01 (ou qualquer frame de slash)
///        Material = Sprites/Additive  (fundo preto some automaticamente)
///        Color    = branco (alpha controlado por este script)
///        Order in Layer = acima do sprite do player/inimigo
///   3. Ajuste os campos no Inspector por tipo (light / heavy / finisher)
///   4. Adicione VfxAutoReturn com lifetime = fadeOutDuration + 0.05
///
/// ── PARAMETROS SUGERIDOS ──────────────────────────────────────────────────────
///
///   Light:    scale=0.6, fadeOutDuration=0.10, pivotOffset=(0, 0.3)
///   Heavy:    scale=0.9, fadeOutDuration=0.14, pivotOffset=(0, 0.4)
///   Finisher: scale=1.3, fadeOutDuration=0.20, pivotOffset=(0, 0.5)
///
/// ── SHADER ADDITIVE — POR QUE USAR ───────────────────────────────────────────
///
///   A textura tem fundo preto. Com Additive, pixels pretos = alpha 0,
///   pixels brancos = alpha 1. Nenhuma mascara necessaria.
///   O shader soma a cor do sprite com o que esta atras — perfeito para
///   efeitos de brilho/impacto.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SlashSprite : MonoBehaviour
{
    // ── Aparencia ─────────────────────────────────────────────────────────────

    [Header("Aparencia")]

    [Tooltip("Cor base do sprite. Alpha e controlado pelo fade — use alpha=1.\n" +
             "Light: branco | Heavy: laranja claro | Finisher: dourado")]
    [SerializeField] private Color baseColor = Color.white;

    [Tooltip("Escala do sprite em relacao ao tamanho original da textura.\n" +
             "Light: 0.6 | Heavy: 0.9 | Finisher: 1.3")]
    [SerializeField] private float spriteScale = 0.7f;

    // ── Posicionamento ────────────────────────────────────────────────────────

    [Header("Posicionamento")]

    [Tooltip("Offset LOCAL em relacao ao attackPoint.\n" +
             "Y positivo empurra o sprite para frente da arma.\n" +
             "Ajuste ate o slash parecer centrado no arco do golpe.")]
    [SerializeField] private Vector2 pivotOffset = new Vector2(0f, 0.35f);

    [Tooltip("Rotacao fixa adicional do sprite (graus).\n" +
             "Util para alinhar a curvatura do slash com a direcao do golpe.\n" +
             "Ex: 90 gira o crescente para ficar horizontal.")]
    [SerializeField] private float baseRotation = 0f;

    // ── Fade out ──────────────────────────────────────────────────────────────

    [Header("Fade out")]

    [Tooltip("Duracao do fade out apos ser desancorado da arma.\n" +
             "Light: 0.10 | Heavy: 0.14 | Finisher: 0.20")]
    [SerializeField] private float fadeOutDuration = 0.12f;

    [Tooltip("Se true, o sprite encolhe para 0 junto com o fade.\n" +
             "Cria um efeito de 'absorção' do impacto. False = so fade de alpha.")]
    [SerializeField] private bool scaleDownOnFade = true;

    // ── Estado interno ────────────────────────────────────────────────────────

    private SpriteRenderer _sr;
    private Coroutine _fadeRoutine;
    private bool _anchored;
    private Vector3 _originalScale;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        _originalScale = Vector3.one * spriteScale;
    }

    private void OnEnable()
    {
        _anchored = true;

        // Reseta visual para o estado inicial
        transform.localPosition = new Vector3(pivotOffset.x, pivotOffset.y, 0f);
        transform.localRotation = Quaternion.Euler(0f, 0f, baseRotation);
        transform.localScale    = _originalScale;

        SetAlpha(1f);

        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
    }

    private void OnDisable()
    {
        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
            _fadeRoutine = null;
        }
        SetAlpha(0f);
        transform.localScale = _originalScale;
    }

    // ── Deteccao de desancoragem — mesmo padrao do SlashTrail ─────────────────

    /// <summary>
    /// Chamado automaticamente pelo Unity quando o parent muda.
    /// Se o novo parent for o VfxManager (pool) ou null, inicia o fade out.
    /// </summary>
    private void OnTransformParentChanged()
    {
        if (!_anchored) return;

        bool returnedToPool = transform.parent == null ||
            (transform.parent != null &&
             transform.parent.GetComponent<VfxManager>() != null);

        if (returnedToPool)
        {
            _anchored = false;

            if (isActiveAndEnabled)
                _fadeRoutine = StartCoroutine(FadeOut());
        }
    }

    // ── Fade out ──────────────────────────────────────────────────────────────

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsed < fadeOutDuration)
        {
            elapsed  += Time.deltaTime;
            float t   = Mathf.Clamp01(elapsed / fadeOutDuration);
            float ease = 1f - (t * t); // EaseOut: rapido no inicio, suave no fim

            SetAlpha(ease);

            if (scaleDownOnFade)
                transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, t);

            yield return null;
        }

        SetAlpha(0f);
        // VfxAutoReturn gerencia o return ao pool apos o lifetime
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Aplica cor e alpha no SpriteRenderer.
    /// Chamado pelo VfxManager para ajustar a cor por tipo de ataque antes do Enable.
    /// </summary>
    public void SetColor(Color color)
    {
        baseColor = color;
        if (_sr != null) SetAlpha(_sr.color.a);
    }

    /// <summary>
    /// Aplica escala do sprite — chamado pelo VfxManager para variar por tipo.
    /// </summary>
    public void SetScale(float scale)
    {
        spriteScale    = scale;
        _originalScale = Vector3.one * scale;
        transform.localScale = _originalScale;
    }

    private void SetAlpha(float alpha)
    {
        if (_sr == null) return;
        Color c = baseColor;
        c.a = alpha;
        _sr.color = c;
    }
}