using System.Collections;
using UnityEngine;

/// <summary>
/// PoiseBar — barra de postura World Space usando SpriteRenderer.
/// Sem Canvas, sem RectTransform — escala previsível em jogos 2D.
///
/// SETUP:
///   1. Crie um filho do personagem chamado "PoiseBar".
///   2. Dentro dele crie dois filhos:
///        "PoiseBg"   — SpriteRenderer com sprite de barra (ex: pixel branco 1x1, esticado)
///        "PoiseFill" — SpriteRenderer com sprite de barra, ficará na frente do Bg
///   3. Adicione PoiseBar no GameObject do PERSONAGEM (não no filho).
///   4. Arraste PoiseBg e PoiseFill nos campos do Inspector.
///   5. Ajuste offset Y para subir a barra acima da cabeça.
///
/// SPRITES:
///   Use um sprite quadrado 1x1 pixel branco. No Inspector do sprite:
///   Pixels Per Unit = 100, Filter Mode = Point, Compression = None.
///   A escala do SpriteRenderer define o tamanho visual — ajuste em barWidth/barHeight.
/// </summary>
public class PoiseBar : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private SpriteRenderer fillRenderer;
    [SerializeField] private SpriteRenderer bgRenderer;

    [Header("Posição relativa ao personagem")]
    [Tooltip("Offset local. Ajuste Y para subir a barra acima da cabeça.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 1.2f, 0f);

    [Header("Tamanho da barra")]
    [SerializeField] private float barWidth  = 0.8f;
    [SerializeField] private float barHeight = 0.08f;

    [Header("Visibilidade")]
    [SerializeField] private float hideDelay = 2.5f;

    [Header("Cores")]
    [SerializeField] private Color colorFull = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color colorLow  = new Color(1.00f, 0.55f, 0.10f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float colorThreshold = 0.45f;
    [SerializeField] private Color bgColor = new Color(0.1f, 0.1f, 0.1f, 0.7f);

    [Header("Sorting")]
    [Tooltip("Sorting Layer da barra. Deve ficar na frente do personagem.")]
    [SerializeField] private string sortingLayerName = "UI";
    [SerializeField] private int    sortingOrder     = 10;

    [Header("Flash de quebra de postura")]
    [SerializeField] private Color breakFlashColor    = new Color(1f, 0.15f, 0.05f, 1f);
    [SerializeField] private int   breakFlashCount    = 4;
    [SerializeField] private float breakFlashDuration = 0.07f;
    [SerializeField] private float breakHoldDuration  = 0.35f;

    // ─── Estado ───────────────────────────────────────────────────────────────

    private float     _normalized   = 1f;
    private float     _hideTimer;
    private Coroutine _breakRoutine;

    // Raiz dos sprites filhos — criada em runtime se não existir
    private Transform _barRoot;

    // ─── Unity ───────────────────────────────────────────────────────────────

    void Awake()
    {
        SetupBarRoot();
        SetVisible(false);
        ApplyFill(1f);

        var playerPoise = GetComponent<PlayerPoise>();
        var enemyPoise  = GetComponent<EnemyPoise>() ?? GetComponentInChildren<EnemyPoise>();

        if (playerPoise != null)
        {
            playerPoise.OnPoiseChanged += OnPoiseChanged;
            playerPoise.OnPoiseBreak   += OnPoiseBreak;
            playerPoise.OnPoiseRecover += OnPoiseRecover;
        }
        else if (enemyPoise != null)
        {
            enemyPoise.OnPoiseChanged += OnPoiseChanged;
            enemyPoise.OnPoiseBreak   += OnPoiseBreak;
            enemyPoise.OnPoiseRecover += OnPoiseRecover;
        }
        else
        {
            Debug.LogWarning($"[PoiseBar] {name}: nenhum PlayerPoise ou EnemyPoise encontrado.");
        }
    }

    void OnDestroy()
    {
        var playerPoise = GetComponent<PlayerPoise>();
        var enemyPoise  = GetComponent<EnemyPoise>() ?? GetComponentInChildren<EnemyPoise>();
        if (playerPoise != null) { playerPoise.OnPoiseChanged -= OnPoiseChanged; playerPoise.OnPoiseBreak -= OnPoiseBreak; playerPoise.OnPoiseRecover -= OnPoiseRecover; }
        else if (enemyPoise != null) { enemyPoise.OnPoiseChanged -= OnPoiseChanged; enemyPoise.OnPoiseBreak -= OnPoiseBreak; enemyPoise.OnPoiseRecover -= OnPoiseRecover; }
    }

    void LateUpdate()
    {
        if (_barRoot == null) return;

        // Mantém posição local correta (compensa flip do sprite do personagem)
        _barRoot.localPosition = offset;
        _barRoot.localScale    = Vector3.one; // garante que o flip do pai não afeta a barra

        if (_breakRoutine != null) return;
        if (!IsVisible()) return;

        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f)
            SetVisible(false);
    }

    // ─── Callbacks ────────────────────────────────────────────────────────────

    private void OnPoiseChanged(float current, float max)
    {
        _normalized = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        ApplyFill(_normalized);
        SetVisible(true);
        _hideTimer = hideDelay;
    }

    private void OnPoiseBreak()
    {
        if (_breakRoutine != null) StopCoroutine(_breakRoutine);
        _breakRoutine = StartCoroutine(BreakFlashRoutine());
    }

    private void OnPoiseRecover()
    {
        if (fillRenderer != null) fillRenderer.color = colorFull;
    }

    // ─── Flash ────────────────────────────────────────────────────────────────

    private IEnumerator BreakFlashRoutine()
    {
        SetVisible(true);

        for (int i = 0; i < breakFlashCount; i++)
        {
            if (fillRenderer != null) fillRenderer.color = breakFlashColor;
            yield return new WaitForSecondsRealtime(breakFlashDuration);
            if (fillRenderer != null) fillRenderer.color = Color.clear;
            yield return new WaitForSecondsRealtime(breakFlashDuration * 0.5f);
        }

        if (fillRenderer != null) fillRenderer.color = breakFlashColor;
        yield return new WaitForSecondsRealtime(breakHoldDuration);

        SetVisible(false);
        if (fillRenderer != null) fillRenderer.color = colorFull;
        _breakRoutine = null;
    }

    // ─── Setup e helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Cria a raiz dos sprites filhos se fillRenderer não foi arrastado no Inspector.
    /// Se os SpriteRenderers foram configurados manualmente, apenas aplica escala/cor.
    /// </summary>
    private void SetupBarRoot()
    {
        if (fillRenderer != null)
        {
            // Renderers configurados manualmente — usa o pai comum como _barRoot.
            // Se não tiver pai dedicado, cria um wrapper para controlar visibilidade.
            Transform sharedParent = fillRenderer.transform.parent;
            if (sharedParent != null && sharedParent != transform)
            {
                _barRoot = sharedParent;
            }
            else
            {
                // Sem pai dedicado: envolve os renderers em um novo root
                var wrapper = new GameObject("PoiseBarRoot");
                wrapper.transform.SetParent(transform, false);
                _barRoot = wrapper.transform;
                fillRenderer.transform.SetParent(_barRoot, true);
                if (bgRenderer != null)
                    bgRenderer.transform.SetParent(_barRoot, true);
            }

            ApplySorting(fillRenderer, sortingOrder);
            fillRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);

            if (bgRenderer != null)
            {
                ApplySorting(bgRenderer, sortingOrder - 1);
                bgRenderer.color                = bgColor;
                bgRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);
            }
            return;
        }

        // Criação procedural — não precisa arrastar nada no Inspector
        var root = new GameObject("PoiseBarRoot");
        root.transform.SetParent(transform, false);
        _barRoot = root.transform;

        var bgGo = new GameObject("PoiseBg");
        bgGo.transform.SetParent(_barRoot, false);
        bgRenderer        = bgGo.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = GetOrCreatePixelSprite();
        bgRenderer.color  = bgColor;
        bgRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        ApplySorting(bgRenderer, sortingOrder - 1);

        var fillGo = new GameObject("PoiseFill");
        fillGo.transform.SetParent(_barRoot, false);
        fillRenderer        = fillGo.AddComponent<SpriteRenderer>();
        fillRenderer.sprite = GetOrCreatePixelSprite();
        fillRenderer.color  = colorFull;
        fillRenderer.transform.localScale = new Vector3(barWidth, barHeight, 1f);
        ApplySorting(fillRenderer, sortingOrder);
    }

    private void ApplyFill(float normalized)
    {
        if (fillRenderer == null) return;

        // Move o pivot para a esquerda escalando a partir do centro,
        // então desloca para manter a borda esquerda fixa.
        float w = barWidth * normalized;
        fillRenderer.transform.localScale    = new Vector3(w, barHeight, 1f);
        fillRenderer.transform.localPosition = new Vector3((w - barWidth) * 0.5f, 0f, -0.01f);
        fillRenderer.color = GetPoiseColor(normalized);
    }

    private Color GetPoiseColor(float normalized)
    {
        float t = normalized >= colorThreshold ? 1f : normalized / colorThreshold;
        return Color.Lerp(colorLow, colorFull, t);
    }

    private void SetVisible(bool visible)
    {
        if (_barRoot != null) _barRoot.gameObject.SetActive(visible);
    }

    private bool IsVisible() => _barRoot != null && _barRoot.gameObject.activeSelf;

    private void ApplySorting(SpriteRenderer sr, int order = -1)
    {
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder     = order < 0 ? sortingOrder : order;
    }

    // Cria um sprite de pixel branco em runtime (fallback se não houver sprite)
    private static Sprite _pixelSprite;
    private static Sprite GetOrCreatePixelSprite()
    {
        if (_pixelSprite != null) return _pixelSprite;
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        _pixelSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return _pixelSprite;
    }
}