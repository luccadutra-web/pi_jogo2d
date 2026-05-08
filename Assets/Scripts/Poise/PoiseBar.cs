using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PoiseBar — barra de postura World Space que flutua sobre o personagem.
///
/// Coloque este componente no mesmo GameObject do personagem (Player ou Inimigo).
/// Ele cria e gerencia um Canvas World Space próprio com a barra de preenchimento.
///
/// SETUP:
///   1. Crie um GameObject filho do personagem (ex: "PoiseBarCanvas").
///   2. Adicione Canvas (World Space), CanvasScaler, GraphicRaycaster nele.
///   3. Dentro do Canvas, crie:
///        PoiseBg   — Image de fundo (cor escura, semi-transparente)
///        PoiseFill — Image, Image Type = Filled, Fill Method = Horizontal,
///                    Fill Origin = Left
///   4. Adicione este script no MESMO GameObject do personagem (não no Canvas).
///   5. Arraste os campos no Inspector.
///
/// O script controla posição, visibilidade, cor e flash via eventos de
/// PlayerPoise (se no player) ou EnemyPoise (se no inimigo).
/// </summary>
public class PoiseBar : MonoBehaviour
{
    [Header("Referências UI")]
    [Tooltip("Raiz do Canvas World Space (filho do personagem).")]
    [SerializeField] private GameObject canvasRoot;
    [Tooltip("Image de preenchimento da barra (Filled, Horizontal, Left).")]
    [SerializeField] private Image      fillImage;

    [Header("Posição relativa ao personagem")]
    [Tooltip("Offset local em relação ao pivot do personagem. " +
             "Ajuste Y para ficar acima da cabeça.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 1.4f, 0f);

    [Header("Escala do Canvas World Space")]
    [Tooltip("Escala do Canvas para combinar com o tamanho do sprite. " +
             "Valores típicos: 0.005–0.02.")]
    [SerializeField] private float canvasScale = 0.008f;

    [Header("Visibilidade")]
    [Tooltip("Tempo que a barra permanece visível após o último hit de poise.")]
    [SerializeField] private float hideDelay = 2.5f;

    [Header("Cores")]
    [SerializeField] private Color colorFull = new Color(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] private Color colorLow  = new Color(1.00f, 0.55f, 0.10f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float colorThreshold = 0.45f;

    [Header("Flash de quebra de postura")]
    [SerializeField] private Color breakFlashColor    = new Color(1f, 0.15f, 0.05f, 1f);
    [SerializeField] private int   breakFlashCount    = 4;
    [SerializeField] private float breakFlashDuration = 0.07f;
    [SerializeField] private float breakHoldDuration  = 0.35f;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private float     _hideTimer;
    private Coroutine _breakRoutine;
    private Camera    _cam;

    // ─── Unity ───────────────────────────────────────────────────────────────

    void Awake()
    {
        _cam = Camera.main;

        ApplyCanvasScale();

        if (canvasRoot != null) canvasRoot.SetActive(false);
        if (fillImage  != null) fillImage.color = colorFull;

        // Conecta ao sistema de poise disponível neste GameObject
        var playerPoise = GetComponent<PlayerPoise>();
        var enemyPoise  = GetComponent<EnemyPoise>();

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
        var enemyPoise  = GetComponent<EnemyPoise>();

        if (playerPoise != null)
        {
            playerPoise.OnPoiseChanged -= OnPoiseChanged;
            playerPoise.OnPoiseBreak   -= OnPoiseBreak;
            playerPoise.OnPoiseRecover -= OnPoiseRecover;
        }
        else if (enemyPoise != null)
        {
            enemyPoise.OnPoiseChanged -= OnPoiseChanged;
            enemyPoise.OnPoiseBreak   -= OnPoiseBreak;
            enemyPoise.OnPoiseRecover -= OnPoiseRecover;
        }
    }

    void LateUpdate()
    {
        if (canvasRoot == null || !canvasRoot.activeSelf) return;

        // Mantém o canvas na posição correta e sempre de frente para a câmera
        canvasRoot.transform.position = transform.position + offset;
        if (_cam != null)
            canvasRoot.transform.rotation = _cam.transform.rotation;

        // Timer de esconder (pausado durante o flash de quebra)
        if (_breakRoutine != null) return;
        _hideTimer -= Time.deltaTime;
        if (_hideTimer <= 0f)
            canvasRoot.SetActive(false);
    }

    // ─── Callbacks de poise ───────────────────────────────────────────────────

    private void OnPoiseChanged(float current, float max)
    {
        float normalized = max > 0f ? current / max : 0f;

        if (fillImage != null)
        {
            fillImage.fillAmount = Mathf.Clamp01(normalized);
            fillImage.color      = GetPoiseColor(normalized);
        }

        if (canvasRoot != null) canvasRoot.SetActive(true);
        _hideTimer = hideDelay;
    }

    private void OnPoiseBreak()
    {
        if (_breakRoutine != null) StopCoroutine(_breakRoutine);
        _breakRoutine = StartCoroutine(BreakFlashRoutine());
    }

    private void OnPoiseRecover()
    {
        if (fillImage != null) fillImage.color = colorFull;
    }

    // ─── Flash de quebra ──────────────────────────────────────────────────────

    private IEnumerator BreakFlashRoutine()
    {
        if (canvasRoot != null) canvasRoot.SetActive(true);

        for (int i = 0; i < breakFlashCount; i++)
        {
            if (fillImage != null) fillImage.color = breakFlashColor;
            yield return new WaitForSecondsRealtime(breakFlashDuration);
            if (fillImage != null) fillImage.color = Color.clear;
            yield return new WaitForSecondsRealtime(breakFlashDuration * 0.5f);
        }

        if (fillImage != null) fillImage.color = breakFlashColor;
        yield return new WaitForSecondsRealtime(breakHoldDuration);

        if (canvasRoot != null) canvasRoot.SetActive(false);
        if (fillImage  != null) fillImage.color = colorFull;
        _breakRoutine = null;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private Color GetPoiseColor(float normalized)
    {
        float t = normalized >= colorThreshold ? 1f : normalized / colorThreshold;
        return Color.Lerp(colorLow, colorFull, t);
    }

    private void ApplyCanvasScale()
    {
        if (canvasRoot == null) return;
        canvasRoot.transform.localScale = Vector3.one * canvasScale;
    }
}