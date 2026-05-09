using System.Collections;
using UnityEngine;

/// <summary>
/// Sistema de Postura (Poise) do Player — espelho do EnemyPoise.
///
/// Quando a postura zera, o player entra em stagger (interrompe ação atual).
/// A postura regenera automaticamente após um delay sem receber hits.
///
/// SETUP:
///   1. Adicione este componente no mesmo GameObject do PlayerHealth.
///   2. Em EnemyBehavior, chame playerPoise.ReceivePoiseHit(poiseDamage)
///      ao acertar o player (já conectado neste script via evento).
///   3. O PlayerHUD escuta os eventos automaticamente via FindObjectOfType.
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerPoise : MonoBehaviour
{
    [Header("Postura")]
    [Tooltip("Postura máxima. Valores maiores = player aguenta mais hits antes de staggar.")]
    [SerializeField] private float maxPoise        = 80f;

    [Header("Regeneração")]
    [Tooltip("Tempo em segundos sem receber hit para a postura começar a regenerar.")]
    [SerializeField] private float poiseRegenDelay = 3f;
    [Tooltip("Postura regenerada por segundo após o delay.")]
    [SerializeField] private float poiseRegenRate  = 30f;

    [Header("Stagger")]
    [Tooltip("Quanto tempo o player fica em stagger ao ter a postura quebrada.")]
    [SerializeField] private float staggerDuration = 0.6f;

    [Header("Debug")]
    [SerializeField] private bool debugPoise = false;

    // ─── Eventos (mesma API do EnemyPoise) ───────────────────────────────────

    /// <summary>Disparado a cada mudança de postura. (current, max)</summary>
    public System.Action<float, float> OnPoiseChanged;
    /// <summary>Disparado quando a postura zera — use para aplicar stagger visual.</summary>
    public System.Action OnPoiseBreak;
    /// <summary>Disparado quando o stagger termina.</summary>
    public System.Action OnPoiseRecover;

    // ─── Propriedades públicas ────────────────────────────────────────────────

    public float CurrentPoise    => _currentPoise;
    public float MaxPoise        => maxPoise;
    public float PoiseNormalized => maxPoise > 0f ? _currentPoise / maxPoise : 0f;
    public bool  IsStaggered     => _isStaggered;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private float _currentPoise;
    private float _regenTimer;
    private bool  _isStaggered;

    private PlayerHealth   _health;
    private PlayerBehavior _behavior;
    private Rigidbody2D    _rb;
    private HitFlash       _hitFlash;

    // ─── Unity ───────────────────────────────────────────────────────────────

    void Awake()
    {
        _health       = GetComponent<PlayerHealth>();
        _behavior     = GetComponent<PlayerBehavior>();
        _rb           = GetComponent<Rigidbody2D>();
        _hitFlash     = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();
        _currentPoise = maxPoise;
    }

    void Update()
    {
        if (_isStaggered)           return;
        if (_currentPoise >= maxPoise) return;

        if (_regenTimer > 0f)
        {
            _regenTimer -= Time.deltaTime;
            return;
        }

        _currentPoise = Mathf.Min(_currentPoise + poiseRegenRate * Time.deltaTime, maxPoise);
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);
    }

    // ─── API pública ──────────────────────────────────────────────────────────

    /// <summary>
    /// Chame ao acertar o player com um ataque.
    /// poiseDamage sugerido: light = 15, heavy = 35.
    /// Ignorado se o player estiver em iFrames ou morto.
    /// </summary>
    public void ReceivePoiseHit(float poiseDamage)
    {
        if (_isStaggered) return;

        _currentPoise -= poiseDamage;
        _regenTimer    = poiseRegenDelay;
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);

        if (debugPoise)
            Debug.Log($"[PlayerPoise] Hit: -{poiseDamage} | Postura: {_currentPoise:F1}/{maxPoise}");

        if (_currentPoise <= 0f)
        {
            if (_staggerCoroutine != null) StopCoroutine(_staggerCoroutine);
            _staggerCoroutine = StartCoroutine(StaggerRoutine());
        }
    }

    /// <summary>
    /// Reseta a postura instantaneamente (use ao morrer ou respawnar).
    /// </summary>
    public void ResetPoise()
    {
        StopAllCoroutines();
        _currentPoise = maxPoise;
        _isStaggered  = false;
        _regenTimer   = 0f;
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);
    }

    // ─── Privado ──────────────────────────────────────────────────────────────

    private Coroutine _staggerCoroutine;

    private IEnumerator StaggerRoutine()
    {
        _isStaggered  = true;
        _currentPoise = maxPoise;

        if (debugPoise) Debug.Log("[PlayerPoise] POSTURA QUEBRADA — stagger!");

        // Cancela defesa se estiver ativa
        _health?.StopDefend();

        // Cancela HurtRoutine ativo para evitar conflito de locks
        _health?.CancelHurtRoutine();

        // Restaura gravityScale (pode ser 0 durante dash) e para o movimento
        // horizontal sem interferir na física vertical — não zeramos velocity.y
        // para não causar overlap com o chão no próximo FixedUpdate.
        if (_rb != null)
        {
            _rb.gravityScale = 1f;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            // Congela posição X durante o stagger para impedir drift
            _rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        }

        // Trava o PlayerBehavior
        if (_behavior != null)
        {
            _behavior.isLocked                  = true;
            _behavior.isAttacking               = false;
            _behavior.isInAttackStartupOrActive = false;
        }

        _hitFlash?.Flash();

        OnPoiseBreak?.Invoke();
        OnPoiseChanged?.Invoke(_currentPoise, maxPoise);

        yield return new WaitForSeconds(staggerDuration);

        // Restaura constraints normais antes de devolver controle
        if (_rb != null)
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        _isStaggered = false;
        _staggerCoroutine = null;

        // Devolve controle ao player
        if (_behavior != null)
            _behavior.isLocked = false;

        OnPoiseRecover?.Invoke();

        if (debugPoise) Debug.Log("[PlayerPoise] Player recuperou postura.");
    }
}