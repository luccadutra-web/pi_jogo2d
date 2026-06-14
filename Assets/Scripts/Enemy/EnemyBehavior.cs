using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyBehavior : MonoBehaviour, IDamageable, IStaggerable
{
    [Header("Saúde")]
    [SerializeField] private int maxHealth = 5;

    [Header("Patrulha")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float patrolRange = 4f;

    [Header("Perseguição")]
    [SerializeField] private float detectionRange   = 6f;
    [SerializeField] private float chaseSpeed       = 3.5f;
    [SerializeField] private float stoppingDistance = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool debugAttack = true;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange = 1.5f;
    [SerializeField] private int   lightDamage      = 1;
    [SerializeField] private float lightStartup     = 0.40f;  // era 0.20 — mais tempo para o player reagir
    [SerializeField] private float lightActiveTime  = 0.10f;
    [SerializeField] private float lightRecovery    = 0.50f;  // era 0.30 — mais punível
    [SerializeField] private float lightCooldown    = 1.8f;   // era 1.20 — menos spam

    // Dano de postura causado pelo light attack ao player (se o player tiver EnemyPoise no futuro)
    // e recebido pelo inimigo quando é acertado. Configurável por tipo de inimigo.
    [SerializeField] private float lightPoiseDamage = 20f;

    [Header("Ataque pesado")]
    [SerializeField] private float heavyAttackRange  = 1.8f;
    [SerializeField] private int   heavyDamage       = 3;
    [SerializeField] private float heavyStartup      = 0.60f;
    [SerializeField] private float heavyActiveTime   = 0.15f;
    [SerializeField] private float heavyRecovery     = 0.55f;
    [SerializeField] private float heavyCooldown     = 3.0f;
    [SerializeField] private float heavyTriggerRange = 1.6f;
    [SerializeField] private float heavyPoiseDamage  = 45f;

    [Header("Telegraph do heavy (VFX/animação de carregamento)")]
    [Tooltip("VFX ou GameObject ativado durante o startup do heavy para sinalizar o ataque")]
    [SerializeField] private GameObject heavyTelegraphVFX;

    [Header("Stagger (parry / poise quebrada)")]
    [SerializeField] private float staggerDuration = 0.5f;

    // Duração do stagger quando a poise quebra — geralmente mais longo que o stagger de parry,
    // pois é a abertura principal para o finisher do jogador.
    [SerializeField] private float poiseBreakStaggerDuration = 1.8f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float     groundCheckRadius = 0.15f;

    [Header("Attack Hitbox")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float     attackPointOffset = 0.5f;
    [SerializeField] private LayerMask playerLayer;

    // ─── Nomes de trigger/bool no Animator Controller ──────────────────────────
    // Constantes para evitar typos. Quando as animações de GuardCrush e Counter
    // estiverem prontas, crie os parâmetros correspondentes no Animator e remova
    // os comentários de HasParameter nos métodos de animação abaixo.
    private const string TriggerHit        = "Hit";
    private const string TriggerStagger    = "Stagger";
    private const string TriggerDie        = "Die";
    private const string TriggerLightAtk   = "LightAttack";
    private const string TriggerHeavyAtk   = "HeavyAttack";
    // FUTURO: adicione esses triggers no Animator quando as animações estiverem prontas.
    // private const string TriggerGuardCrush = "GuardCrush";  // inimigo sofre guard crush do player
    // private const string TriggerPoiseBreak = "PoiseBreak";  // inimigo em stagger por poise

    private enum State { Patrol, Chase, Attack, Hurt, Dead }
    private State state = State.Patrol;

    private int   currentHealth;
    private bool  isStaggered;
    private bool  _isPerformingAttack;
    public event System.Action OnDeath;
    private float lightCooldownTimer;
    private float heavyCooldownTimer;

    private Vector2   patrolOrigin;
    private float     patrolDir = 1f;
    private Transform playerTransform;
    private Rigidbody2D rb;
    private CharacterAnimationController animController;
    private EnemyPoise poise;
    private HitFlash   hitFlash;

    private bool  _pendingAttack;
    private float _pendingRange;
    private int   _pendingDamage;
    private bool  _pendingIsHeavy;
    private float _playerUntouchableTimer;
    private Coroutine _hurtCoroutine;  // controla HurtRoutine para evitar múltiplas instâncias

    // Cache do PlayerBehavior — consultado antes de cada ataque para não
    // interromper o player no meio de um combo ou durante i-frames de dash.
    private PlayerBehavior _playerBehavior;

    private void Log(string msg) { if (debugAttack) Debug.Log($"[ENEMY {name}] {msg}"); }

    void Awake()
    {
        rb             = GetComponent<Rigidbody2D>();
        animController = GetComponent<CharacterAnimationController>();
        poise          = GetComponent<EnemyPoise>();
        hitFlash       = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();
        currentHealth  = maxHealth;
        rb.freezeRotation = true;
    }

    void Start()
    {
        patrolOrigin = transform.position;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform  = playerObj.transform;
            _playerBehavior  = playerObj.GetComponent<PlayerBehavior>();
        }
        else
            Debug.LogError("[EnemyBehavior] Player não encontrado — verifique a tag 'Player'.");

        // Conecta poise ao stagger do inimigo.
        // Quando o EnemyPoise detecta a postura zerada, dispara OnPoiseBreak,
        // que aciona um stagger mais longo (poiseBreakStaggerDuration) em vez
        // do stagger curto de parry.
        if (poise != null)
            poise.OnPoiseBreak += OnPoiseBreak;
    }

    void OnDestroy()
    {
        if (poise != null)
            poise.OnPoiseBreak -= OnPoiseBreak;
    }

    void Update()
    {
        if (state == State.Dead || state == State.Hurt || isStaggered) return;

        TickCooldowns();
        UpdateGroundedAnimation();

        switch (state)
        {
            case State.Patrol: DoPatrol(); break;
            case State.Chase:  DoChase();  break;
            case State.Attack: DoAttack(); break;
        }
    }

    private void TickCooldowns()
    {
        if (lightCooldownTimer > 0f) lightCooldownTimer -= Time.deltaTime;
        if (heavyCooldownTimer > 0f) heavyCooldownTimer -= Time.deltaTime;
    }

    private void DoPatrol()
    {
        rb.linearVelocity = new Vector2(patrolDir * patrolSpeed, rb.linearVelocity.y);
        UpdateFacing(patrolDir);
        animController?.SetSpeed(Mathf.Abs(rb.linearVelocity.x));

        float left  = patrolOrigin.x - patrolRange;
        float right = patrolOrigin.x + patrolRange;

        if (patrolDir > 0 && transform.position.x >= right) patrolDir = -1f;
        else if (patrolDir < 0 && transform.position.x <= left) patrolDir = 1f;

        if (playerTransform != null &&
            Vector2.Distance(transform.position, playerTransform.position) <= detectionRange)
        {
            Log("Player detectado → CHASE");
            state = State.Chase;
        }
    }

    private void DoChase()
    {
        if (playerTransform == null) { state = State.Patrol; return; }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        if (dist > detectionRange * 1.3f) { Log("Perdeu player → PATROL"); state = State.Patrol; return; }

        if (dist <= lightAttackRange)
        {
            Log($"Entrou em ATTACK | dist={dist:F2}");
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            animController?.SetSpeed(0f);
            state = State.Attack;
            return;
        }

        float dir = Mathf.Sign(playerTransform.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        UpdateFacing(dir);
        animController?.SetSpeed(Mathf.Abs(rb.linearVelocity.x));
    }

    private void DoAttack()
    {
        if (_isPerformingAttack) return;
        if (playerTransform == null) { state = State.Chase; return; }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        if (dist > lightAttackRange * 2f) { Log("Saiu do range → CHASE"); state = State.Chase; return; }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        animController?.SetSpeed(0f);

        // Não atacar enquanto o player estiver em startup/active de um combo
        // ou invencível pelo dash — evita que o inimigo reaja no meio de uma janela
        // que deveria ser exclusivamente do player.
        if (IsPlayerUntouchable())
        {
            _playerUntouchableTimer += Time.deltaTime;
            if (_playerUntouchableTimer < 3.0f) return;  // era 1.5s — dá mais espaço ao combo do player
            // Segurança: se o player ficou travado em isInAttackStartupOrActive
            // por mais de 3s, o inimigo ignora a espera e ataca normalmente.
            Log("Timeout de espera pelo player — forçando ataque");
        }
        _playerUntouchableTimer = 0f;

        if (dist <= heavyTriggerRange && heavyCooldownTimer <= 0f)
        {
            Log("Escolheu HEAVY");
            StartCoroutine(AttackRoutine(isHeavy: true));
        }
        else if (lightCooldownTimer <= 0f)
        {
            Log("Escolheu LIGHT");
            StartCoroutine(AttackRoutine(isHeavy: false));
        }
    }

    /// <summary>
    /// Retorna true se o player está numa janela em que o inimigo não deve reagir:
    ///   • isInAttackStartupOrActive — player no meio de um swing (startup ou active)
    ///   • IsDashInvincible          — player em i-frames de dash
    /// O inimigo pode estar em chase ou já no estado Attack quando isso acontece —
    /// em ambos os casos, simplesmente segura o ataque e tenta de novo no próximo frame.
    /// </summary>
    private bool IsPlayerUntouchable()
    {
        if (_playerBehavior == null) return false;
        return _playerBehavior.isInAttackStartupOrActive || _playerBehavior.IsDashInvincible;
    }

    private IEnumerator AttackRoutine(bool isHeavy)
    {
        _isPerformingAttack = true;

        float startup    = isHeavy ? heavyStartup    : lightStartup;
        float activeTime = isHeavy ? heavyActiveTime : lightActiveTime;
        float recovery   = isHeavy ? heavyRecovery   : lightRecovery;
        float cooldown   = isHeavy ? heavyCooldown   : lightCooldown;
        float range      = isHeavy ? heavyAttackRange : lightAttackRange;
        int   damage     = isHeavy ? heavyDamage     : lightDamage;

        _pendingAttack  = true;
        _pendingRange   = range;
        _pendingDamage  = damage;
        _pendingIsHeavy = isHeavy;

        if (isHeavy) heavyCooldownTimer = cooldown;
        else         lightCooldownTimer = cooldown;

        animController?.TriggerAnimation(isHeavy ? TriggerHeavyAtk : TriggerLightAtk);

        if (isHeavy)
            GetComponent<EnemyAudio>()?.OnEnemyAttackHeavy();
        else
            GetComponent<EnemyAudio>()?.OnEnemyAttackLight();

        if (isHeavy && heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(true);

        Log($"AttackRoutine START → {(isHeavy ? "HEAVY" : "LIGHT")} | startup={startup}s");

        // Startup — verifica a cada frame se o ataque foi interrompido (parry/stagger/death).
        float elapsed = 0f;
        while (elapsed < startup)
        {
            if (!_isPerformingAttack || state == State.Dead) yield break;
            elapsed += Time.unscaledDeltaTime; // não trava durante HitStop
            yield return null;
        }

        if (isHeavy && heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

        // Sai imediatamente se o ataque foi cancelado durante o startup.
        if (!_isPerformingAttack || state == State.Dead) yield break;

        if (_pendingAttack)
        {
            Log("Active — aplicando hit.");
            ApplyHit(range, damage, isHeavy);
            _pendingAttack = false;
        }

        // Recovery — também interruptível por stagger/parry.
        elapsed = 0f;
        while (elapsed < activeTime + recovery)
        {
            if (!_isPerformingAttack || state == State.Dead) yield break;
            elapsed += Time.unscaledDeltaTime; // não trava durante HitStop
            yield return null;
        }

        Log("AttackRoutine END");
        _isPerformingAttack = false;

        if (state != State.Dead)
            state = State.Chase;
    }

    private void ApplyHit(float range, int damage, bool isHeavy)
    {
        Vector2 origin = GetAttackPointPosition();

        Collider2D hit = Physics2D.OverlapCircle(origin, range, playerLayer);
        if (hit == null) return;

        PlayerHealth ph = hit.GetComponent<PlayerHealth>()
                       ?? hit.GetComponentInParent<PlayerHealth>();
        if (ph == null) return;

        ph.RegisterAttacker(transform);

        // Aplica dano de postura ao player
        var playerPoise = hit.GetComponent<PlayerPoise>() ?? hit.GetComponentInParent<PlayerPoise>();
        float poiseDmg = isHeavy ? heavyPoiseDamage : lightPoiseDamage;
        playerPoise?.ReceivePoiseHit(poiseDmg);

        // Ataques pesados do inimigo chamam TakeDamageHeavy para habilitar
        // Guard Crush no player quando a stamina estiver baixa.
        if (isHeavy)
            ph.TakeDamageHeavy(damage, transform.position);
        else
            ph.TakeDamage(damage, transform.position);

        Log($"HIT CONFIRMADO | dmg={damage} | isHeavy={isHeavy}");
    }

    // Chamado por Animation Event no clip de ataque do inimigo (relay do CharacterAnimationController).
    public void DealAttackHit()
    {
        Log($"AnimationEvent | pending={_pendingAttack}");
        if (!_pendingAttack) return;
        _pendingAttack = false;
        ApplyHit(_pendingRange, _pendingDamage, _pendingIsHeavy);
    }

    // ─── IDamageable ───────────────────────────────────────────────────────────

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        currentHealth -= damage;
        Log($"TakeDamage | dmg={damage} | hp={currentHealth}/{maxHealth}");

        animController?.SetTriggerDirect(TriggerHit);
        GetComponent<HitFlash>()?.Flash();

        // VFX e áudio de hurt — integrados ao VfxManager e EnemyAudio do projeto
        VfxManager.Instance?.SpawnEnemyHurt(transform.position);
        GetComponent<EnemyAudio>()?.OnEnemyHurt();

        // Repassa o dano de postura para o componente EnemyPoise (se existir).
        // O dano de poise é determinado pelo tipo de ataque que causou o dano —
        // por padrão usa lightPoiseDamage. MeleeWeapon chama ReceivePoiseHit
        // diretamente (veja MeleeWeapon.ApplyHit) com o valor correto por tipo.
        // Esta chamada aqui é o fallback para danos vindos de outras fontes.
        poise?.ReceivePoiseHit(lightPoiseDamage);

        if (currentHealth <= 0) { Die(); return; }

        if (_hurtCoroutine != null) StopCoroutine(_hurtCoroutine);
        _hurtCoroutine = StartCoroutine(HurtRoutine());
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    // ─── IStaggerable ──────────────────────────────────────────────────────────

    // Stagger curto — disparado por parry do player (PlayerHealth.ExecuteParry).
    public void Stagger()
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        if (heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

        StopAllCoroutines();

        // Força saída imediata do estado de ataque no Animator, ignorando exit time.
        // CrossFade com transitionDuration=0 garante que o Stagger começa no mesmo frame,
        // mesmo que o clip de ataque ainda esteja rodando com exit time habilitado.
        animController?.ForceState(TriggerStagger);
        GetComponent<EnemyAudio>()?.OnEnemyStagger();

        StartCoroutine(StaggerRoutine(staggerDuration));
    }

    // Stagger longo — disparado quando a postura (EnemyPoise) é zerada.
    private void OnPoiseBreak()
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        if (heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

        hitFlash?.PoiseBreakFlash();
        VfxManager.Instance?.SpawnPoiseBreak(transform.position);
        GetComponent<EnemyAudio>()?.OnEnemyStagger();

        StopAllCoroutines();
        animController?.ForceState(TriggerStagger);

        Log($"POISE QUEBRADA — stagger longo ({poiseBreakStaggerDuration}s)");
        StartCoroutine(StaggerRoutine(poiseBreakStaggerDuration));
    }

    // ─── Rotinas privadas ──────────────────────────────────────────────────────

    private IEnumerator HurtRoutine()
    {
        state = State.Hurt;
        yield return new WaitForSecondsRealtime(0.4f); // era 0.25s — janela maior para o combo do player
        if (state != State.Dead) state = State.Chase;
        _hurtCoroutine = null;
    }

    private IEnumerator StaggerRoutine(float duration)
    {
        state       = State.Hurt;
        isStaggered = true;

        // ForceState já foi chamado antes desta coroutine (em Stagger/OnPoiseBreak),
        // garantindo transição imediata sem depender de exit time no Animator.
        // Não dispara TriggerAnimation aqui para não reiniciar a animação.

        // WaitForSecondsRealtime em vez de WaitForSeconds — o stagger não pode
        // ficar preso durante um HitStop (timeScale=0), pois o inimigo nunca
        // sairia do estado Hurt e ficaria travado após o freeze terminar.
        yield return new WaitForSecondsRealtime(duration);

        isStaggered = false;
        if (state != State.Dead) state = State.Chase;
    }

    private void Die()
    {
        state               = State.Dead;
        _pendingAttack      = false;
        _isPerformingAttack = false;
        rb.linearVelocity   = Vector2.zero;

        if (heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

        animController?.SetSpeed(0f);
        animController?.SetTriggerDirect(TriggerDie);

        VfxManager.Instance?.SpawnEnemyHurt(transform.position);
        VfxManager.Instance?.SpawnBlood(transform, isHeavy: true);
        GetComponent<EnemyAudio>()?.OnEnemyDeath();

        OnDeath?.Invoke();
        StartCoroutine(DeathRoutine());
        PlayerSpecialGauge.Instance?.AddKill();
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(0.8f);
        Destroy(gameObject);
    }

    private Vector2 GetAttackPointPosition()
    {
        if (attackPoint != null) return attackPoint.position;

        float facing = transform.localScale.x >= 0 ? 1f : -1f;
        return (Vector2)transform.position + Vector2.right * facing * attackPointOffset;
    }

    private void UpdateGroundedAnimation()
    {
        if (groundCheck == null) return;
        bool grounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        animController?.UpdateGrounded(grounded);
    }

    private void UpdateFacing(float dir)
    {
        float newSign = Mathf.Sign(dir);
        if (Mathf.Approximately(newSign, Mathf.Sign(transform.localScale.x))) return;

        transform.localScale = new Vector3(
            Mathf.Abs(transform.localScale.x) * newSign,
            transform.localScale.y,
            transform.localScale.z
        );
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, lightAttackRange);
        Gizmos.color = new Color(1f, 0.4f, 0f);
        Gizmos.DrawWireSphere(transform.position, heavyAttackRange);

        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Vector2 attackOrigin = GetAttackPointPosition();
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(attackOrigin, lightAttackRange);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackOrigin, heavyAttackRange);
    }
}