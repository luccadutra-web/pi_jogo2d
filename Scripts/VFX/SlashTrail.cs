using System.Collections;
using UnityEngine;

/// <summary>
/// SlashTrail — trail vetorial que segue a arma e faz fade out suave.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///   SlashTrail usa o TrailRenderer do Unity configurado para arte vetorial:
///   sem textura, com gradiente de alpha (1 → 0), corners arredondados.
///
///   VfxManager ancora o GameObject ao weapon transform durante o active.
///   SlashTrail detecta o desancoramente (OnTransformParentChanged) e inicia
///   o fade out — o trail permanece na posição congelada enquanto desvanece.
///
/// ── SETUP DO PREFAB ──────────────────────────────────────────────────────────
///
///   1. Crie um GameObject vazio com TrailRenderer e este componente.
///   2. No TrailRenderer:
///      - Time: 0.12 (light) / 0.18 (heavy) / 0.25 (finisher)
///      - Min Vertex Distance: 0.05
///      - Width Curve: começa em widthStart e vai a 0 na ponta
///      - Color: gradiente branco com alpha 0.9→0 (ajuste por tipo)
///      - Material: Sprites/Default ou um material Unlit/Transparent
///      - Corner Vertices: 4, End Cap Vertices: 4
///   3. Adicione VfxPoolItem com lifetime = fadeOutDuration + trail.time + 0.05
///
/// ── PARÂMETROS POR TIPO ──────────────────────────────────────────────────────
///
///   Light:    widthStart=0.06, trailTime=0.12, fadeOutDuration=0.10, color=branco
///   Heavy:    widthStart=0.10, trailTime=0.18, fadeOutDuration=0.14, color=laranja claro
///   Finisher: widthStart=0.14, trailTime=0.25, fadeOutDuration=0.20, color=dourado
///
/// ────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(TrailRenderer))]
public class SlashTrail : MonoBehaviour
{
    [Header("Fade out")]
    [Tooltip("Duração do fade out em segundos após ser desancorado da arma.")]
    [SerializeField] private float fadeOutDuration = 0.12f;

    [Header("Trail")]
    [Tooltip("Largura máxima do trail (na base, junto à arma).")]
    [SerializeField] private float widthStart = 0.08f;
    [Tooltip("Cor base do trail. Alpha é controlado pelo fade — use alpha=1 aqui.")]
    [SerializeField] private Color trailColor = Color.white;

    private TrailRenderer _trail;
    private Coroutine _fadeRoutine;
    private bool _anchored = true;

    void Awake()
    {
        _trail = GetComponent<TrailRenderer>();
        ApplySettings();
    }

    void OnEnable()
    {
        _anchored = true;
        _trail.Clear();
        _trail.emitting = true;

        // Garante alpha cheio ao ativar
        SetTrailAlpha(1f);

        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
    }

    void OnDisable()
    {
        if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
        _trail.Clear();
    }

    /// <summary>
    /// Chamado pelo VfxManager (via OnTransformParentChanged) quando
    /// o trail é desancorado da arma — inicia o fade out.
    /// </summary>
    void OnTransformParentChanged()
    {
        if (!_anchored) return;
        if (transform.parent == null || (transform.parent != null &&
            transform.parent.GetComponent<VfxManager>() != null))
        {
            _anchored = false;
            _trail.emitting = false; // para de emitir novos vértices
            if (isActiveAndEnabled)
                _fadeRoutine = StartCoroutine(FadeOut());
        }
    }

    private IEnumerator FadeOut()
    {
        float elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            SetTrailAlpha(1f - elapsed / fadeOutDuration);
            yield return null;
        }
        SetTrailAlpha(0f);
        // VfxPoolItem gerencia o return ao pool
    }

    private void SetTrailAlpha(float alpha)
    {
        var c = trailColor;
        c.a = alpha;

        // Gradient do TrailRenderer: começa na cor e vai a transparente
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(0f, 1f) }
        );
        _trail.colorGradient = grad;
    }

    private void ApplySettings()
    {
        _trail.startWidth  = widthStart;
        _trail.endWidth    = 0f;
        _trail.numCornerVertices   = 4;
        _trail.numCapVertices      = 4;
        _trail.minVertexDistance   = 0.05f;
        SetTrailAlpha(1f);
    }
}