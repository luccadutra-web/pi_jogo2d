using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Especial do player: toca guitarra e acerta 4 notas no tempo certo.
/// Acertando todas → instakill em todos os inimigos no range.
/// Errando qualquer nota → cancela sem dano e devolve a gauge.
/// </summary>
[RequireComponent(typeof(PlayerBehavior))]
[RequireComponent(typeof(PlayerSpecialGauge))]
public class PlayerSpecial : MonoBehaviour
{
    // ─── Configuração ────────────────────────────────────────────────────────

    [Header("Ativação")]
    [Tooltip("Tecla para ativar o especial")]
    [SerializeField] private string specialKey = "<Keyboard>/f";

    [Header("Range")]
    [SerializeField] private float    specialRange = 5f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Notas — 4 teclas em sequência")]
    [Tooltip("Teclas das 4 notas, em ordem")]
    [SerializeField] private string[] noteKeys = { "<Keyboard>/j", "<Keyboard>/k", "<Keyboard>/l", "<Keyboard>/semicolon" };

    [Header("Timing por nota")]
    [Tooltip("Janela de tempo individual (segundos) para cada nota. Sincronize com a música.")]
    [SerializeField] private float[] noteWindowDurations = { 0.8f, 0.8f, 0.8f, 0.8f };

    [Tooltip("Intervalo entre o resultado de uma nota e a janela da próxima abrir")]
    [SerializeField] private float noteCooldown = 0.3f;

    [Header("Invencibilidade")]
    [SerializeField] private float invincibleDuration = 2.5f;

    [Header("Animação")]
    [SerializeField] private string animTriggerSpecial = "Special";
    [SerializeField] private string animTriggerSuccess = "SpecialSuccess";
    [SerializeField] private string animTriggerFail    = "SpecialFail";

    [Header("Impacto (sucesso)")]
    [SerializeField] private float hitStopDuration  = 0.35f;
    [SerializeField] private float shakeIntensity   = 0.30f;
    [SerializeField] private float shakeDuration    = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    // ─── Eventos ─────────────────────────────────────────────────────────────

    /// <summary>Disparado quando uma nova janela de nota abre. (índice, duração da janela)</summary>
    public System.Action<int, float> OnWindowOpened;
    /// <summary>Disparado após cada nota: (índice, acertou).</summary>
    public System.Action<int, bool>  OnNoteResult;
    /// <summary>Disparado ao completar todas as notas com sucesso.</summary>
    public System.Action             OnSpecialSuccess;
    /// <summary>Disparado ao errar uma nota (gauge devolvida).</summary>
    public System.Action             OnSpecialFail;
    /// <summary>Disparado se o especial for cancelado externamente.</summary>
    public System.Action             OnSpecialCancelled;

    // ─── Privados ─────────────────────────────────────────────────────────────

    private const int NOTE_COUNT = 4;

    private InputAction   _activateAction;
    private InputAction[] _noteActions;

    private PlayerBehavior               _behavior;
    private PlayerSpecialGauge           _gauge;
    private PlayerHealth                 _health;
    private CharacterAnimationController _anim;

    private bool _isRunning;

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        _behavior = GetComponent<PlayerBehavior>();
        _gauge    = GetComponent<PlayerSpecialGauge>();
        _health   = GetComponent<PlayerHealth>();
        _anim     = GetComponent<CharacterAnimationController>();

        _activateAction = new InputAction("Special", InputActionType.Button);
        _activateAction.AddBinding(specialKey);

        _noteActions = new InputAction[NOTE_COUNT];
        for (int i = 0; i < NOTE_COUNT; i++)
        {
            _noteActions[i] = new InputAction($"Note{i}", InputActionType.Button);
            string key = i < noteKeys.Length ? noteKeys[i] : $"<Keyboard>/f{i + 1}";
            _noteActions[i].AddBinding(key);
        }

        // Garante que o array sempre tem 4 entradas
        if (noteWindowDurations == null || noteWindowDurations.Length != NOTE_COUNT)
        {
            float[] filled = new float[NOTE_COUNT];
            for (int i = 0; i < NOTE_COUNT; i++)
                filled[i] = (noteWindowDurations != null && i < noteWindowDurations.Length)
                    ? noteWindowDurations[i] : 0.8f;
            noteWindowDurations = filled;
        }
    }

    void OnEnable()
    {
        _activateAction.Enable();
        foreach (var a in _noteActions) a.Enable();
    }

    void OnDisable()
    {
        _activateAction.Disable();
        foreach (var a in _noteActions) a.Disable();
    }

    void Update()
    {
        if (_isRunning)              return;
        if (_behavior.isLocked)      return;
        if (_health != null && _health.IsDead) return;

        if (_activateAction.WasPressedThisFrame())
            TryActivate();
    }

    // ─── Ativação ─────────────────────────────────────────────────────────────

    private void TryActivate()
    {
        if (!_gauge.TrySpend())
        {
            if (debugLog) Debug.Log("[Special] Gauge insuficiente.");
            return;
        }
        if (debugLog) Debug.Log("[Special] Ativado!");
        StartCoroutine(SpecialRoutine());
    }

    // ─── Rotina principal ─────────────────────────────────────────────────────

    private IEnumerator SpecialRoutine()
    {
        _isRunning = true;
        _behavior.isLocked = true;
        _behavior.isAttacking = false;
        _behavior.isInAttackStartupOrActive = false;

        _anim?.SetTriggerDirect(animTriggerSpecial);
        StartCoroutine(InvincibleRoutine());

        bool allCorrect = true;

        for (int i = 0; i < NOTE_COUNT; i++)
        {
            if (i > 0)
                yield return new WaitForSecondsRealtime(noteCooldown);

            float windowDuration = noteWindowDurations[i];

            // Passa a duração da janela junto com o índice para a UI poder usar
            OnWindowOpened?.Invoke(i, windowDuration);
            if (debugLog) Debug.Log($"[Special] Nota {i + 1}/{NOTE_COUNT} — janela {windowDuration:F2}s.");

            bool  hit   = false;
            float timer = windowDuration;

            // Flush: descarta inputs deste frame
            yield return null;

            while (timer > 0f)
            {
                timer -= Time.unscaledDeltaTime;

                if (_noteActions[i].WasPressedThisFrame())
                {
                    hit = true;
                    break;
                }

                for (int j = 0; j < NOTE_COUNT; j++)
                {
                    if (j != i && _noteActions[j].WasPressedThisFrame())
                    {
                        timer = 0f;
                        break;
                    }
                }

                yield return null;
            }

            OnNoteResult?.Invoke(i, hit);

            if (!hit)
            {
                if (debugLog) Debug.Log($"[Special] Nota {i + 1} ERRADA ou timeout.");
                allCorrect = false;
                break;
            }

            if (debugLog) Debug.Log($"[Special] Nota {i + 1} ACERTADA!");
        }

        if (allCorrect)
            yield return StartCoroutine(ExecuteSuccess());
        else
            yield return StartCoroutine(ExecuteFail());

        _behavior.isLocked = false;
        _isRunning = false;
    }

    // ─── Sucesso ──────────────────────────────────────────────────────────────

    private IEnumerator ExecuteSuccess()
    {
        if (debugLog) Debug.Log("[Special] SUCESSO! Instakill.");

        _anim?.SetTriggerDirect(animTriggerSuccess);
        HitStop.Instance?.DoHitStop(hitStopDuration);

        if (HitStop.Instance != null)
            while (HitStop.Instance.IsActive)
                yield return null;

        CameraShake.Instance?.Shake(shakeIntensity, shakeDuration);

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, specialRange, enemyLayer);
        if (debugLog) Debug.Log($"[Special] {hits.Length} inimigo(s) no range.");

        foreach (Collider2D col in hits)
        {
            Rigidbody2D enemyRb = col.GetComponent<Rigidbody2D>() ?? col.GetComponentInParent<Rigidbody2D>();
            if (enemyRb != null) StartCoroutine(HitlagRoutine(enemyRb));

            var damageable = col.GetComponent<IDamageable>() ?? col.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(9999, transform.position);

            var poise = col.GetComponent<EnemyPoise>() ?? col.GetComponentInParent<EnemyPoise>();
            poise?.ReceivePoiseHit(9999f);

            var hitFlash = col.GetComponent<HitFlash>() ?? col.GetComponentInParent<HitFlash>();
            hitFlash?.ImpactFlash();
        }

        OnSpecialSuccess?.Invoke();
        yield return new WaitForSecondsRealtime(0.4f);
    }

    // ─── Falha ────────────────────────────────────────────────────────────────

    private IEnumerator ExecuteFail()
    {
        if (debugLog) Debug.Log("[Special] Falha. Gauge devolvida.");
        _anim?.SetTriggerDirect(animTriggerFail);
        _gauge.Refund();
        OnSpecialFail?.Invoke();
        yield return new WaitForSecondsRealtime(0.3f);
    }

    // ─── Invencibilidade ──────────────────────────────────────────────────────

    private IEnumerator InvincibleRoutine()
    {
        if (_health != null)
            _health.EnableSpecialInvincible(invincibleDuration);
        yield break;
    }

    // ─── Hitlag ───────────────────────────────────────────────────────────────

    private IEnumerator HitlagRoutine(Rigidbody2D enemyRb)
    {
        if (enemyRb == null) yield break;
        float saved = enemyRb.gravityScale;
        enemyRb.gravityScale   = 0f;
        enemyRb.linearVelocity = Vector2.zero;
        yield return new WaitForSecondsRealtime(0.08f);
        if (enemyRb != null) enemyRb.gravityScale = saved;
    }

    // ─── Cancelamento externo ─────────────────────────────────────────────────

    public void CancelSpecial()
    {
        if (!_isRunning) return;
        StopAllCoroutines();
        _gauge.Refund();
        _behavior.isLocked = false;
        _isRunning = false;
        OnSpecialCancelled?.Invoke();
        if (debugLog) Debug.Log("[Special] Cancelado externamente.");
    }

    // ─── Gizmo ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, specialRange);
    }
}