using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD unificado: Vida, Stamina e Gauge do Especial.
/// A postura (poise) do player e dos inimigos é exibida por PoiseBar,
/// um componente World Space filho de cada personagem — não passa por aqui.
///
/// HIERARCHY:
///  Canvas (Screen Space)
///  └─ HUD
///     ├─ Health
///     │   ├─ HealthBg
///     │   ├─ HealthGhostFill  ← healthGhostFill
///     │   └─ HealthFill       ← healthFill
///     ├─ Stamina
///     │   ├─ StaminaBg
///     │   └─ StaminaFill      ← staminaFill
///     └─ Gauge
///         ├─ GaugeBg
///         ├─ GaugeFill        ← gaugeFill
///         └─ GaugeText        ← gaugeText
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    // ─── Vida ────────────────────────────────────────────────────────────────

    [Header("Vida")]
    [SerializeField] private Image           healthFill;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Animação de dano (vida)")]
    [SerializeField] private Image healthGhostFill;
    [SerializeField] private float healthGhostDelay = 0.4f;
    [SerializeField] private float healthGhostSpeed = 1.5f;

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
    [SerializeField] private Color healthColorFull = new Color(0.20f, 0.78f, 0.35f, 1f);
    [SerializeField] private Color healthColorMid  = new Color(0.95f, 0.75f, 0.10f, 1f);
    [SerializeField] private Color healthColorLow  = new Color(0.89f, 0.20f, 0.20f, 1f);
    [SerializeField] private float healthMidThreshold = 0.5f;
    [SerializeField] private float healthLowThreshold = 0.25f;

    // ─── Referências ──────────────────────────────────────────────────────────

    private PlayerHealth       _health;
    private PlayerStamina      _stamina;
    private PlayerSpecialGauge _gauge;

    // ─── Ghost bar ────────────────────────────────────────────────────────────

    private float _ghostFill       = 1f;
    private float _ghostDelayTimer = 0f;
    private bool  _ghostFalling    = false;

    // ─── Stamina ─────────────────────────────────────────────────────────────

    private Vector3   _staminaParentOriginalScale;
    private float     _lastStamina = -1f;
    private Coroutine _staminaPulseCoroutine;

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

        if (staminaFill != null)
            _staminaParentOriginalScale = staminaFill.transform.parent.localScale;
        _lastStamina = _stamina != null ? _stamina.CurrentStamina : -1f;
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

    // ─── Sync inicial ─────────────────────────────────────────────────────────

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
            SetFill(staminaFill, _stamina.StaminaNormalized);
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
        float n = max > 0 ? (float)current / max : 0f;
        SetFill(healthFill, n);
        SetHealthColor(n);
        SetText(healthText, current, max);
        if (healthGhostFill != null) { _ghostDelayTimer = healthGhostDelay; _ghostFalling = false; }
    }

    private void HandleDamaged()
    {
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
        float n = max > 0 ? current / max : 0f;
        SetFill(staminaFill, n);
        SetText(staminaText, Mathf.RoundToInt(current), Mathf.RoundToInt(max));

        bool spent = _lastStamina >= 0f && current < _lastStamina;
        _lastStamina = current;

        if (spent && staminaFill != null)
        {
            if (_staminaPulseCoroutine != null) StopCoroutine(_staminaPulseCoroutine);
            staminaFill.transform.parent.localScale = _staminaParentOriginalScale;
            _staminaPulseCoroutine = StartCoroutine(
                PunchScaleRoutine(staminaFill.transform.parent, staminaPulseScale, staminaPulseDuration, _staminaParentOriginalScale));
        }
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

    // ─── Ghost bar ────────────────────────────────────────────────────────────

    private void UpdateGhostBar()
    {
        if (healthGhostFill == null || _health == null) return;
        float target = _health.HealthNormalized;

        if (_ghostFill > target)
        {
            if (_ghostDelayTimer > 0f) _ghostDelayTimer -= Time.deltaTime;
            else _ghostFalling = true;

            if (_ghostFalling)
            {
                _ghostFill = Mathf.MoveTowards(_ghostFill, target, healthGhostSpeed * Time.deltaTime);
                SetFill(healthGhostFill, _ghostFill);
            }
        }
        else
        {
            _ghostFill = target; _ghostFalling = false; _ghostDelayTimer = 0f;
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
        healthFill.color = normalized <= healthLowThreshold ? healthColorLow
                         : normalized <= healthMidThreshold ? healthColorMid
                         : healthColorFull;
    }

    // ─── Coroutines ──────────────────────────────────────────────────────────

    private IEnumerator PunchScaleRoutine(Transform target, float scale, float duration)
    {
        if (target == null) yield break;
        Vector3 original = target.localScale;
        float half = duration * 0.5f, t = 0f;
        while (t < half) { t += Time.unscaledDeltaTime; target.localScale = Vector3.LerpUnclamped(original, original * scale, t / half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.unscaledDeltaTime; target.localScale = Vector3.LerpUnclamped(original * scale, original, t / half); yield return null; }
        target.localScale = original;
    }

    private IEnumerator PunchScaleRoutine(Transform target, float scale, float duration, Vector3 fixedOriginal)
    {
        if (target == null) yield break;
        float half = duration * 0.5f, t = 0f;
        while (t < half) { t += Time.unscaledDeltaTime; target.localScale = Vector3.LerpUnclamped(fixedOriginal, fixedOriginal * scale, t / half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.unscaledDeltaTime; target.localScale = Vector3.LerpUnclamped(fixedOriginal * scale, fixedOriginal, t / half); yield return null; }
        target.localScale = fixedOriginal;
    }
}