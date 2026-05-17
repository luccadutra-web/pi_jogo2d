using System.Collections;
using UnityEngine;

/// <summary>
/// OwlEnemy — Coruja com bicada parada (light) e headbutt voador (heavy).
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ESTADOS
/// ═══════════════════════════════════════════════════════════════════════════
///   Patrol       → anda até detectar o player (usa animação "run")
///   Chase        → persegue no chão (usa animação "run")
///   LightAttack  → para e bica (one-shot, player está em meleeRange)
///   HeavyAttack  → salta / voa em direção ao player (player fora de meleeRange)
///   Dead
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ANIMATOR — parâmetros e states necessários
/// ═══════════════════════════════════════════════════════════════════════════
///
///   Float  │ "Speed"          │ 0 = parado, >0 = andando/correndo
///   ────────┼──────────────────┼─────────────────────────────────────────────
///   Trigger │ "LightAttack"   │ dispara bicada parada (one-shot)
///   Trigger │ "HeavyAttack"   │ dispara headbutt voador (one-shot)
///   Trigger │ "Land"          │ dispara pouso após heavy (one-shot)
///   Trigger │ "Hurt"          │ levar dano (one-shot)
///   Trigger │ "Die"           │ morte (one-shot)
///
///   States base layer:
///     run          (loop)    ← patrulha e perseguição
///     light_attack (one-shot)← bicada
///     heavy_attack (one-shot)← headbutt voador
///     hurt         (one-shot)
///     die          (one-shot)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// ANIMATION EVENTS
/// ═══════════════════════════════════════════════════════════════════════════
///
///   Clip "light_attack":
///     frame do bico descendo  → OnLightHitWindowOpen()
///     frame do bico subindo   → OnLightHitWindowClose()
///     último frame            → OnLightAttackEnd()
///
///   Clip "heavy_attack":
///     frame que sai voando    → OnHeavyLaunch()       (aplica a velocidade)
///     frame do bico / impacto → OnHeavyHitWindowOpen()
///     frame após impacto      → OnHeavyHitWindowClose()
///     (o pouso é detectado por IsGrounded; não precisa de event de fim)
///
///   Clip "hurt":
///     (sem events obrigatórios)
///
///   Clip "die":
///     (sem events obrigatórios)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// SETUP
/// ═══════════════════════════════════════════════════════════════════════════
///   1. Adicione este script no GameObject da coruja.
///   2. Componentes: Rigidbody2D, CharacterAnimationController, HitFlash.
///   3. Filhos: GroundCheck (Transform), AttackPoint (Transform).
///   4. Arraste os campos no Inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class OwlEnemy : MonoBehaviour, IDamageable, IStaggerable
{
    // ─── Stats ────────────────────────────────────────────────────────────────

    [Header("Vida")]
    [SerializeField] private int maxHealth = 6;

    [Header("Movimento")]
    [SerializeField] private float patrolSpeed  = 2.0f;
    [SerializeField] private float chaseSpeed   = 3.0f;
    [SerializeField] private float patrolDistance = 3.0f;
    [SerializeField] private float patrolWaitTime = 0.8f;

    [Header("Detecção")]
    [SerializeField] private float detectionRange = 7.0f;
    [SerializeField] private LayerMask playerLayer;

    // ─── Light Attack (bicada parada) ─────────────────────────────────────────

    [Header("Light Attack — Bicada parada")]
    [Tooltip("Distância máxima para acionar a bicada parada.")]
    [SerializeField] private float lightAttackRange  = 1.8f;
    [Tooltip("Dano da bicada.")]
    [SerializeField] private int   lightAttackDamage = 1;
    [Tooltip("Raio do hitbox ao redor do AttackPoint.")]
    [SerializeField] private float lightHitRadius    = 0.45f;
    [Tooltip("Cooldown após a bicada antes de poder atacar de novo.")]
    [SerializeField] private float lightAttackCooldown = 1.6f;

    // ─── Heavy Attack (headbutt voador) ──────────────────────────────────────

    [Header("Heavy Attack — Superman headbutt")]
    [Tooltip("Distância máxima para acionar o headbutt.")]
    [SerializeField] private float heavyAttackRange  = 5.5f;
    [Tooltip("Distância mínima — abaixo disso usa light attack em vez de heavy.")]
    [SerializeField] private float heavyMinRange     = 1.8f;
    [Tooltip("Velocidade horizontal do voo.")]
    [SerializeField] private float heavyForceX       = 7.0f;
    [Tooltip("Impulso vertical no lançamento.")]
    [SerializeField] private float heavyForceY       = 5.5f;
    [Tooltip("Dano do headbutt.")]
    [SerializeField] private int   heavyAttackDamage = 2;
    [Tooltip("Raio do hitbox do headbutt.")]
    [SerializeField] private float heavyHitRadius    = 0.6f;
    [Tooltip("Cooldown após o headbutt antes de poder atacar de novo.")]
    [SerializeField] private float heavyAttackCooldown = 2.5f;

    [Header("Sprite")]
    [Tooltip("Marque se o sprite padrão está virado para a esquerda.")]
    [SerializeField] private bool spriteFlippedByDefault = true;

    [Header("Referências")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private LayerMask hitLayer;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    // ─── Triggers de animação ─────────────────────────────────────────────────

    private const string AnimHurt        = "IsHurt"; // Bool no Animator (não trigger)
    private const string AnimDie         = "Die";
    private const string AnimLightAttack = "LightAttack";
    private const string AnimHeavyAttack = "HeavyAttack";
    private const string AnimLand        = "Land";

    // ─── Estado ───────────────────────────────────────────────────────────────

    private enum State { Patrol, Chase, LightAttack, HeavyAttack, Hurt, Dead }

    private State _state          = State.Patrol;
    private int   _currentHealth;
    private bool  _attackOnCooldown;

    // light attack
    private bool  _lightHitWindowOpen;
    private bool  _lightHitApplied;
    private bool  _lightAttackRunning;

    // heavy attack
    private bool  _heavyLaunched;
    private bool  _heavyHitWindowOpen;
    private bool  _heavyHitApplied;

    // patrulha
    private int   _patrolDir    = 1;
    private float _patrolWalked = 0f;
    private bool  _patrolWaiting;

    // hurt
    private bool      _isPlayingHurt;  // evita re-disparar o trigger Hurt durante o combo
    private Coroutine _hurtCoroutine;
    private State     _stateBeforeHurt;

    // ─── Componentes ──────────────────────────────────────────────────────────

    private Rigidbody2D                  _rb;
    private CharacterAnimationController _anim;
    private HitFlash                     _hitFlash;
    private Transform                    _playerTransform;

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb       = GetComponent<Rigidbody2D>();
        _anim     = GetComponent<CharacterAnimationController>();
        _hitFlash = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();

        _currentHealth = maxHealth;
    }

    void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) _playerTransform = playerObj.transform;
        else Debug.LogWarning("[OwlEnemy] Player não encontrado — adicione a tag 'Player'.");
    }

    void Update()
    {
        if (_state == State.Dead || _state == State.Hurt) return;

        switch (_state)
        {
            case State.Patrol:      DoPatrol();      break;
            case State.Chase:       DoChase();       break;
            case State.LightAttack: DoLightAttack(); break;
            case State.HeavyAttack: DoHeavyAttack(); break;
        }

        UpdateFacing();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ESTADOS
    // ═══════════════════════════════════════════════════════════════════════════

    private void DoPatrol()
    {
        if (_patrolWaiting) return;

        if (PlayerInRange(detectionRange))
        {
            Log("Detectou player → CHASE");
            _state = State.Chase;
            return;
        }

        _rb.linearVelocity = new Vector2(_patrolDir * patrolSpeed, _rb.linearVelocity.y);
        _patrolWalked     += patrolSpeed * Time.deltaTime;
        _anim?.SetSpeed(patrolSpeed);

        if (_patrolWalked >= patrolDistance)
        {
            _patrolWalked = 0f;
            StartCoroutine(PatrolTurnRoutine());
        }
    }

    private void DoChase()
    {
        if (_playerTransform == null) { _state = State.Patrol; return; }

        float dist = PlayerDistance();

        // Perdeu o player
        if (dist > detectionRange * 1.4f)
        {
            Log("Perdeu player → PATROL");
            _anim?.SetSpeed(0f);
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _state = State.Patrol;
            return;
        }

        if (!_attackOnCooldown && IsGrounded())
        {
            // ── Light Attack: player dentro do range corpo-a-corpo ─────────────
            if (dist <= lightAttackRange)
            {
                Log("Player perto → LIGHT ATTACK");
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                _anim?.StopImmediate();   // sem damping — trigger precisa do Speed = 0 já
                _state = State.LightAttack;
                StartCoroutine(LightAttackRoutine());
                return;
            }

            // ── Heavy Attack: player em range médio ───────────────────────────
            if (dist >= heavyMinRange && dist <= heavyAttackRange)
            {
                Log("Player em range médio → HEAVY ATTACK");
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                _anim?.StopImmediate();   // sem damping
                _state = State.HeavyAttack;
                StartCoroutine(HeavyAttackRoutine());
                return;
            }
        }

        // ── Persegue no chão ──────────────────────────────────────────────────
        float dir = _playerTransform.position.x > transform.position.x ? 1f : -1f;

        // Para se estiver encostado no player (evita empurrar)
        bool blocked = Physics2D.Raycast(
            transform.position, Vector2.right * dir, lightAttackRange * 0.85f,
            playerLayer | groundLayer
        );

        if (blocked)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _anim?.StopImmediate();
            return;
        }

        // Em cooldown E dentro do range de ataque: para e espera sem animar.
        // Evita o loop do run (pulo de sapo) enquanto aguarda o cooldown.
        if (_attackOnCooldown && dist <= lightAttackRange)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _anim?.StopImmediate();
            return;
        }

        _rb.linearVelocity = new Vector2(dir * chaseSpeed, _rb.linearVelocity.y);
        _anim?.SetSpeed(chaseSpeed);
    }

    // ── Light Attack: bicada parada ───────────────────────────────────────────

    private void DoLightAttack()
    {
        // Checagem de hitbox feita por Animation Event (OnLightHitWindowOpen/Close).
        // Aqui apenas aplicamos o dano enquanto a janela estiver aberta.
        if (!_lightHitWindowOpen || _lightHitApplied) return;

        Vector2 origin = GetAttackPointPosition();
        var hits = Physics2D.OverlapCircleAll(origin, lightHitRadius, hitLayer);
        foreach (var col in hits)
        {
            var ph = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
            if (ph == null) continue;

            ph.TakeDamage(lightAttackDamage, transform.position);
            _lightHitApplied = true;
            Log($"Light Attack HIT | dmg={lightAttackDamage}");
            break;
        }
    }

    // ── Heavy Attack: superman headbutt ──────────────────────────────────────

    private void DoHeavyAttack()
    {
        // Hitbox ativa enquanto _heavyHitWindowOpen = true (controlado por Animation Events).
        if (!_heavyHitWindowOpen || _heavyHitApplied) return;

        Vector2 origin = GetAttackPointPosition();
        var hits = Physics2D.OverlapCircleAll(origin, heavyHitRadius, hitLayer);
        foreach (var col in hits)
        {
            var ph = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
            if (ph == null) continue;

            ph.TakeDamage(heavyAttackDamage, transform.position);
            _heavyHitApplied = true;
            Log($"Heavy Attack HIT | dmg={heavyAttackDamage}");
            break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COROUTINES
    // ═══════════════════════════════════════════════════════════════════════════

    private IEnumerator PatrolTurnRoutine()
    {
        _patrolWaiting = true;
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        _anim?.SetSpeed(0f);
        yield return new WaitForSeconds(patrolWaitTime);
        _patrolDir    *= -1;
        _patrolWaiting = false;
    }

    private IEnumerator LightAttackRoutine()
    {
        _lightHitApplied    = false;
        _lightHitWindowOpen = false;
        _lightAttackRunning = true;

        _anim?.TriggerAnimation(AnimLightAttack);

        // Fallback de fim de animação: caso OnLightAttackEnd não seja configurado,
        // aguarda um tempo generoso e retorna ao Chase.
        // Se o Animation Event estiver presente, OnLightAttackEnd() encerrará antes.
        float timeout = 1.8f;
        float elapsed = 0f;
        while (_lightAttackRunning && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Garante que fechou a janela
        _lightHitWindowOpen = false;
        _lightAttackRunning = false;

        _state = State.Chase;
        Log("LightAttack fim → cooldown");
        StartCoroutine(AttackCooldownRoutine(lightAttackCooldown));
    }

    private IEnumerator HeavyAttackRoutine()
    {
        _heavyLaunched      = false;
        _heavyHitApplied    = false;
        _heavyHitWindowOpen = false;

        _anim?.TriggerAnimation(AnimHeavyAttack);

        // Fallback de lançamento: caso OnHeavyLaunch não seja configurado,
        // aplica velocidade após curto delay.
        StartCoroutine(HeavyLaunchFallback());

        // Aguarda pousar
        yield return new WaitForSeconds(0.25f);   // margem para sair do chão
        yield return new WaitUntil(() => IsGrounded() || _state == State.Dead);

        if (_state == State.Dead) yield break;

        // Pouso
        _heavyHitWindowOpen = false;
        _rb.linearVelocity  = new Vector2(0f, _rb.linearVelocity.y);
        _anim?.TriggerAnimation(AnimLand);

        // Aguarda animação de land (curta)
        yield return new WaitForSeconds(0.35f);

        _state = State.Chase;
        Log("HeavyAttack pousou → cooldown");
        StartCoroutine(AttackCooldownRoutine(heavyAttackCooldown));
    }

    /// <summary>
    /// Fallback: aplica o impulso do heavy se OnHeavyLaunch não vier do Animation Event.
    /// </summary>
    private IEnumerator HeavyLaunchFallback()
    {
        yield return new WaitForSeconds(0.2f);
        if (!_heavyLaunched) DoHeavyLaunch();
    }

    private void DoHeavyLaunch()
    {
        if (_heavyLaunched) return;
        _heavyLaunched = true;

        float dir = _playerTransform != null
            ? (_playerTransform.position.x > transform.position.x ? 1f : -1f)
            : _patrolDir;

        _rb.linearVelocity = new Vector2(dir * heavyForceX, heavyForceY);
        _anim?.SetSpeed(heavyForceX);
        Log("Heavy lançado");
    }

    private IEnumerator AttackCooldownRoutine(float duration)
    {
        _attackOnCooldown = true;
        yield return new WaitForSeconds(duration);
        _attackOnCooldown = false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ANIMATION EVENTS — chame estes métodos diretamente nos clipes
    // ═══════════════════════════════════════════════════════════════════════════

    // ── light_attack ──────────────────────────────────────────────────────────

    /// <summary>
    /// Animation Event — "light_attack"
    /// Adicione no frame que o bico começa a descer (início do hitbox ativo).
    /// </summary>
    public void OnLightHitWindowOpen()
    {
        _lightHitWindowOpen = true;
        _lightHitApplied    = false;
        Log("Light hit window ABERTA");
    }

    /// <summary>
    /// Animation Event — "light_attack"
    /// Adicione no frame que o bico começa a subir (fim do hitbox).
    /// </summary>
    public void OnLightHitWindowClose()
    {
        _lightHitWindowOpen = false;
        Log("Light hit window FECHADA");
    }

    /// <summary>
    /// Animation Event — "light_attack"
    /// Adicione no último frame do clipe (ou 1–2 frames antes do fim).
    /// Sinaliza que a animação acabou e libera o estado.
    /// </summary>
    public void OnLightAttackEnd()
    {
        _lightHitWindowOpen = false;
        _lightAttackRunning = false;
        Log("OnLightAttackEnd recebido");
    }

    // ── heavy_attack ──────────────────────────────────────────────────────────

    /// <summary>
    /// Animation Event — "heavy_attack"
    /// Adicione no frame que a coruja se lança (começa a voar para frente).
    /// Este event aplica a velocidade do Rigidbody.
    /// </summary>
    public void OnHeavyLaunch()
    {
        DoHeavyLaunch();
        Log("OnHeavyLaunch (Animation Event)");
    }

    /// <summary>
    /// Animation Event — "heavy_attack"
    /// Adicione no frame do bico / momento de impacto (hitbox ativo).
    /// </summary>
    public void OnHeavyHitWindowOpen()
    {
        _heavyHitWindowOpen = true;
        _heavyHitApplied    = false;
        Log("Heavy hit window ABERTA");
    }

    /// <summary>
    /// Animation Event — "heavy_attack"
    /// Adicione 1–2 frames após o impacto (fecha hitbox).
    /// </summary>
    public void OnHeavyHitWindowClose()
    {
        _heavyHitWindowOpen = false;
        Log("Heavy hit window FECHADA");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IDamageable
    // ═══════════════════════════════════════════════════════════════════════════

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        if (_state == State.Dead) return;

        _currentHealth -= damage;
        Log($"TakeDamage | dmg={damage} | hp={_currentHealth}/{maxHealth}");

        _hitFlash?.Flash();
        VfxManager.Instance?.SpawnEnemyHurt(transform.position);

        if (_currentHealth <= 0) { Die(); return; }

        // Salva o estado atual para restaurar depois, para o movimento e entra em Hurt.
        if (_state != State.Hurt)
            _stateBeforeHurt = _state;

        _state = State.Hurt;
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        _anim?.StopImmediate();

        // Liga o bool IsHurt — o Animator entra em hurt e fica lá enquanto true.
        // Hits subsequentes do combo apenas renovam o timer sem reiniciar a animação.
        _anim?.SetBool(AnimHurt, true);

        if (_hurtCoroutine != null) StopCoroutine(_hurtCoroutine);
        _hurtCoroutine = StartCoroutine(HurtRoutine());
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    // ═══════════════════════════════════════════════════════════════════════════
    // IStaggerable — sem stagger neste inimigo; apenas absorve o dano
    // ═══════════════════════════════════════════════════════════════════════════

    public void Stagger()
    {
        // A coruja não possui animação de stagger nem telegraph,
        // portanto um parry fora de contexto só causa flash visual.
        if (_state == State.Dead) return;
        Log("Stagger chamado (sem efeito — sem anim de stagger)");
        _hitFlash?.ParryFlash();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MORTE
    // ═══════════════════════════════════════════════════════════════════════════

    private IEnumerator HurtRoutine()
    {
        _isPlayingHurt = true;
        yield return new WaitForSecondsRealtime(0.25f);
        _isPlayingHurt = false;
        _anim?.SetBool(AnimHurt, false); // Animator sai de hurt → idle
        if (_state != State.Dead)
            _state = _stateBeforeHurt;   // retoma o que estava fazendo antes
    }

    private void Die()
    {
        _state = State.Dead;
        StopAllCoroutines();

        _rb.linearVelocity = Vector2.zero;
        _rb.gravityScale   = 0f;
        _rb.constraints    = RigidbodyConstraints2D.FreezeAll;

        _anim?.SetSpeed(0f);
        _anim?.TriggerAnimation(AnimDie);
        Log("Morreu.");

        StartCoroutine(DeathCleanupRoutine(2.5f));
    }

    private IEnumerator DeathCleanupRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        Destroy(gameObject);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private bool IsGrounded()
    {
        if (groundCheck == null) return false;
        return Physics2D.OverlapCircle(groundCheck.position, 0.15f, groundLayer);
    }

    private bool PlayerInRange(float range)
        => _playerTransform != null && PlayerDistance() <= range;

    private float PlayerDistance()
        => _playerTransform != null
            ? Vector2.Distance(transform.position, _playerTransform.position)
            : float.MaxValue;

    private Vector2 GetAttackPointPosition()
    {
        if (attackPoint != null) return attackPoint.position;
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        if (spriteFlippedByDefault) facing *= -1f;
        return (Vector2)transform.position + Vector2.right * facing * lightHitRadius;
    }

    private void UpdateFacing()
    {
        float velX = _rb.linearVelocity.x;
        float absS = Mathf.Abs(transform.localScale.x);

        bool facingRight;
        if (Mathf.Abs(velX) > 0.1f)
            facingRight = velX > 0;
        else if (_playerTransform != null &&
                 (_state == State.Chase || _state == State.LightAttack || _state == State.HeavyAttack))
            facingRight = _playerTransform.position.x > transform.position.x;
        else
            return;

        bool flip = spriteFlippedByDefault ? facingRight : !facingRight;
        Vector3 s = transform.localScale;
        s.x = flip ? -absS : absS;
        transform.localScale = s;
    }

    private void Log(string msg) { if (debugLog) Debug.Log($"[OwlEnemy {name}] {msg}"); }

    // ═══════════════════════════════════════════════════════════════════════════
    // GIZMOS
    // ═══════════════════════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, lightAttackRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, heavyAttackRange);

        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(GetAttackPointPosition(), lightHitRadius);

        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, 0.15f);
        }
    }
}