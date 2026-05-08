using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD unificado: Vida, Stamina e Gauge do Especial.
///
/// HIERARCHY SUGERIDA:
///  Canvas
///  └─ HUD (este componente)
///     ├─ Health
///     │   ├─ HealthBg
///     │   └─ HealthFill      ← healthFill   (Image, Filled, Horizontal, Left)
///     ├─ Stamina
///     │   ├─ StaminaBg
///     │   └─ StaminaFill     ← staminaFill  (Image, Filled, Horizontal, Left)
///     └─ Gauge
///         ├─ GaugeBg
///         ├─ GaugeFill       ← gaugeFill    (Image, Filled, Horizontal, Left)
///         └─ GaugeText       ← gaugeText    (TextMeshProUGUI, opcional)
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    // ─── Vida ────────────────────────────────────────────────────────────────

    [Header("Vida")]
    [SerializeField] private Image           healthFill;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Animação de dano (vida)")]
    [Tooltip("Barra fantasma que acompanha a queda da vida com delay")]
    [SerializeField] private Image  healthGhostFill;
    [SerializeField] private float  healthGhostDelay  = 0.4f;   // tempo parado antes de começar a cair
    [SerializeField] private float  healthGhostSpeed  = 1.5f;   // velocidade da queda

    // ─── Stamina ─────────────────────────────────────────────────────────────

    [Header("Stamina")]
    [SerializeField] private Image           staminaFill;
    [SerializeField] private TextMeshProUGUI staminaText;

    [Header("Pulse ao gastar stamina")]
    [SerializeField] private float staminaPulseScale    = 1.04f;
    [SerializeField] private float staminaPulseDuration = 0.10f;

    // ─── Gauge do Especial ───────────────────────────────────────────────────

    [Header("Gauge do Especial")]
    [SerializeField] private Image           gaugeFill;
    [SerializeField] private TextMeshProUGUI gaugeText;

    [Header("Pulse ao encher gauge")]
    [SerializeField] private float gaugePulseScale    = 1.06f;
    [SerializeField] private float gaugePulseDuration = 0.15f;

    // ─── Cores da barra de vida ───────────────────────────────────────────────

    [Header("Cores — Vida")]
    [SerializeField] private Color healthColorFull = new Color(0.20f, 0.78f, 0.35f, 1f);  // verde
    [SerializeField] private Color healthColorMid  = new Color(0.95f, 0.75f, 0.10f, 1f);  // amarelo
    [SerializeField] private Color healthColorLow  = new Color(0.89f, 0.20f, 0.20f, 1f);  // vermelho
    [Tooltip("Abaixo deste % a barra fica amarela")]
    [SerializeField] private float healthMidThreshold = 0.5f;
    [Tooltip("Abaixo deste % a barra fica vermelha")]
    [SerializeField] private float healthLowThreshold = 0.25f;

    // ─── Referências de sistema ───────────────────────────────────────────────

    private PlayerHealth       _health;
    private PlayerStamina      _stamina;
    private PlayerSpecialGauge _gauge;

    // Estado interno da barra fantasma
    private float      _ghostFill        = 1f;
    private float      _ghostDelayTimer  = 0f;
    private bool       _ghostFalling     = false;
    private Coroutine  _ghostCoroutine;

    // ─── Unity ───────────────────────────────────────────────────────────────

    void Start()
    {
        _health  = FindObjectOfType<PlayerHealth>();
        _stamina = FindObjectOfType<PlayerStamina>();
        _gauge   = FindObjectOfType<PlayerSpecialGauge>();

        if (_health  == null) Debug.LogWarning("[PlayerHUD] PlayerHealth não encontrado!");
        if (_stamina == null) Debug.LogWarning("[PlayerHUD] PlayerStamina não encontrado!");
        if (_gauge   == null) Debug.LogWarning("[PlayerHUD] PlayerSpecialGauge não encontrado!");

        SubscribeEvents();
        SyncAll();
    }

    void OnDestroy() => UnsubscribeEvents();

    void Update()
    {
        UpdateGhostBar();
    }

    // ─── Subscrição ──────────────────────────────────────────────────────────

    private void SubscribeEvents()
    {
        if (_health != null)
        {
            _health.OnHealthChanged += HandleHealthChanged;
            _health.OnDamaged       += HandleDamaged;
            _health.OnDeath         += HandleDeath;
        }

        if (_stamina != null)
            _stamina.OnStaminaChanged += HandleStaminaChanged;

        if (_gauge != null)
        {
            _gauge.OnGaugeChanged += HandleGaugeChanged;
            _gauge.OnGaugeFull    += HandleGaugeFull;
        }
    }

    private void UnsubscribeEvents()
    {
        if (_health != null)
        {
            _health.OnHealthChanged -= HandleHealthChanged;
            _health.OnDamaged       -= HandleDamaged;
            _health.OnDeath         -= HandleDeath;
        }

        if (_stamina != null)
            _stamina.OnStaminaChanged -= HandleStaminaChanged;

        if (_gauge != null)
        {
            _gauge.OnGaugeChanged -= HandleGaugeChanged;
            _gauge.OnGaugeFull    -= HandleGaugeFull;
        }
    }

    // ─── Sync inicial ────────────────────────────────────────────────────────

    private void SyncAll()
    {
        if (_health != null)
        {
            float hp = _health.HealthNormalized;
            SetFill(healthFill, hp);
            SetFill(healthGhostFill, hp);
            _ghostFill = hp;
            SetHealthColor(hp);
            SetText(healthText, _health.CurrentHealth, _health.MaxHealth);
        }

        if (_stamina != null)
        {
            float st = _stamina.StaminaNormalized;
            SetFill(staminaFill, st);
            SetText(staminaText, Mathf.RoundToInt(_stamina.CurrentStamina), Mathf.RoundToInt(_stamina.MaxStamina));
        }

        if (_gauge != null)
        {
            SetFill(gaugeFill, _gauge.GaugeNormalized);
            SetGaugeText(_gauge.CurrentGauge, _gauge.MaxGauge);
        }
    }

    // ─── Handlers — Vida ─────────────────────────────────────────────────────

    private void HandleHealthChanged(int current, int max)
    {
        float normalized = max > 0 ? (float)current / max : 0f;
        SetFill(healthFill, normalized);
        SetHealthColor(normalized);
        SetText(healthText, current, max);

        // Dispara a barra fantasma
        if (healthGhostFill != null)
        {
            _ghostDelayTimer = healthGhostDelay;
            _ghostFalling    = false;
        }
    }

    private void HandleDamaged()
    {
        // Pulse na barra de vida ao levar dano
        if (healthFill != null)
            StartCoroutine(PunchScaleRoutine(healthFill.transform.parent, 1.04f, 0.10f));
    }

    private void HandleDeath()
    {
        SetFill(healthFill, 0f);
        SetFill(healthGhostFill, 0f);
        SetHealthColor(0f);
    }

    // ─── Handlers — Stamina ──────────────────────────────────────────────────

    private void HandleStaminaChanged(float current, float max)
    {
        float normalized = max > 0 ? current / max : 0f;
        SetFill(staminaFill, normalized);
        SetText(staminaText, Mathf.RoundToInt(current), Mathf.RoundToInt(max));

        if (staminaFill != null)
            StartCoroutine(PunchScaleRoutine(staminaFill.transform.parent, staminaPulseScale, staminaPulseDuration));
    }

    // ─── Handlers — Gauge ────────────────────────────────────────────────────

    private void HandleGaugeChanged(float current, float max)
    {
        SetFill(gaugeFill, max > 0f ? current / max : 0f);
        SetGaugeText(current, max);
    }

    private void HandleGaugeFull()
    {
        if (gaugeFill != null)
            StartCoroutine(PunchScaleRoutine(gaugeFill.transform.parent, gaugePulseScale, gaugePulseDuration));
    }

    // ─── Barra fantasma (ghost bar) ───────────────────────────────────────────

    private void UpdateGhostBar()
    {
        if (healthGhostFill == null || _health == null) return;

        float target = _health.HealthNormalized;

        // Se a ghost está acima do valor real, começa a cair após delay
        if (_ghostFill > target)
        {
            if (_ghostDelayTimer > 0f)
            {
                _ghostDelayTimer -= Time.deltaTime;
            }
            else
            {
                _ghostFalling = true;
            }

            if (_ghostFalling)
            {
                _ghostFill = Mathf.MoveTowards(_ghostFill, target, healthGhostSpeed * Time.deltaTime);
                SetFill(healthGhostFill, _ghostFill);
            }
        }
        else
        {
            // Ghost acompanha instantaneamente se vida subir (cura)
            _ghostFill       = target;
            _ghostFalling    = false;
            _ghostDelayTimer = 0f;
            SetFill(healthGhostFill, _ghostFill);
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SetFill(Image bar, float normalized)
    {
        if (bar == null) return;
        bar.fillAmount = Mathf.Clamp01(normalized);
    }

    private void SetText(TextMeshProUGUI label, int current, int max)
    {
        if (label == null) return;
        label.text = $"{current} / {max}";
    }

    private void SetText(TextMeshProUGUI label, float current, float max)
        => SetText(label, Mathf.RoundToInt(current), Mathf.RoundToInt(max));

    private void SetGaugeText(float current, float max)
    {
        if (gaugeText == null) return;
        gaugeText.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }

    private void SetHealthColor(float normalized)
    {
        if (healthFill == null) return;
        if (normalized <= healthLowThreshold)
            healthFill.color = healthColorLow;
        else if (normalized <= healthMidThreshold)
            healthFill.color = healthColorMid;
        else
            healthFill.color = healthColorFull;
    }

    // ─── Coroutines ──────────────────────────────────────────────────────────

    private IEnumerator PunchScaleRoutine(Transform target, float scale, float duration)
    {
        if (target == null) yield break;
        Vector3 original = target.localScale;
        float half = duration * 0.5f;
        float t = 0f;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(original, original * scale, t / half);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(original * scale, original, t / half);
            yield return null;
        }
        target.localScale = original;
    }
}