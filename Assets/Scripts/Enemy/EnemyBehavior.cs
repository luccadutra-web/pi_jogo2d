using System.Collections;
using UnityEngine;

/// <summary>
/// EnemyBehavior — IA base do inimigo.
///
/// Mantido:
/// - Patrol
/// - Chase
/// - Attack
/// - Hurt
/// - Dead
/// - Cooldowns
/// - Animation Event para aplicar hit
///
/// Adicionado:
/// - AttackPoint frontal
/// - Hitbox frontal via OverlapCircle
/// - Busca PlayerHealth no collider OU parent
/// - Gizmos de hitbox
/// - Lock para impedir múltiplas coroutines de ataque
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyBehavior : MonoBehaviour, IDamageable, IStaggerable
{
    [Header("Saúde")]
    [SerializeField] private int maxHealth = 5;

    [Header("Patrulha")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float patrolRange = 4f;

    [Header("Perseguição")]
    [SerializeField] private float detectionRange = 6f;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float stoppingDistance = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool debugAttack = true;

    [Header("Ataque - distância visual")]
    [SerializeField] private float attackBufferDistance = 0.35f;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange = 1.4f;
    [SerializeField] private int lightDamage = 1;
    [SerializeField] private float lightKnockback = 5f;
    [SerializeField] private float lightStartup = 0.20f;
    [SerializeField] private float lightActiveTime = 0.10f;
    [SerializeField] private float lightRecovery = 0.30f;
    [SerializeField] private float lightCooldown = 1.2f;

    [Header("Ataque pesado")]
    [SerializeField] private float heavyAttackRange = 1.8f;
    [SerializeField] private int heavyDamage = 3;
    [SerializeField] private float heavyKnockback = 11f;
    [SerializeField] private float heavyStartup = 0.40f;
    [SerializeField] private float heavyActiveTime = 0.15f;
    [SerializeField] private float heavyRecovery = 0.55f;
    [SerializeField] private float heavyCooldown = 3.0f;
    [SerializeField] private float heavyTriggerRange = 1.6f;

    [Header("Knockback recebido")]
    [SerializeField] private float knockbackDuration = 0.20f;
    [SerializeField] private float maxKnockbackForce = 15f;

    [Header("Ground Check (Animacao)")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.15f;

    // ── NOVO: ATTACK POINT ────────────────────────────────────────────────
    // Ponto frontal de origem do golpe.
    // Idealmente um child object posicionado na mão/arma do inimigo.
    [Header("Attack Hitbox")]
    [SerializeField] private Transform attackPoint;

    // Fallback caso esqueça de configurar attackPoint no inspector.
    [SerializeField] private float attackPointOffset = 0.8f;

    // Layer usada para detectar player.
    [SerializeField] private LayerMask playerLayer;
    // ──────────────────────────────────────────────────────────────────────

    private CharacterAnimationController animController;

    private enum State { Patrol, Chase, Attack, Hurt, Dead }
    private State state = State.Patrol;

    private int currentHealth;
    private bool isKnockedBack;
    private bool _isPerformingAttack;

    private float lightCooldownTimer;
    private float heavyCooldownTimer;

    private Vector2 patrolOrigin;
    private float patrolDir = 1f;
    private Transform playerTransform;
    private Rigidbody2D rb;

    // Hit armado até o Animation Event disparar
    private bool _pendingAttack;
    private float _pendingRange;
    private int _pendingDamage;
    private float _pendingKnock;

    private void AttackLog(string msg)
    {
        if (debugAttack)
            Debug.Log($"[ENEMY {name}] {msg}");
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animController = GetComponent<CharacterAnimationController>();

        currentHealth = maxHealth;
        rb.freezeRotation = true;
    }

    void Start()
    {
        patrolOrigin = transform.position;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
        else
            Debug.LogError("[EnemyBehavior] Player não encontrado — verifique a tag Player.");
    }

    void Update()
    {
        if (state == State.Dead || state == State.Hurt || isKnockedBack) return;

        TickCooldowns();
        UpdateGroundedAnimation();

        switch (state)
        {
            case State.Patrol: DoPatrol(); break;
            case State.Chase: DoChase(); break;
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

        float left = patrolOrigin.x - patrolRange;
        float right = patrolOrigin.x + patrolRange;

        if (patrolDir > 0 && transform.position.x >= right) patrolDir = -1f;
        else if (patrolDir < 0 && transform.position.x <= left) patrolDir = 1f;

        if (playerTransform != null &&
            Vector2.Distance(transform.position, playerTransform.position) <= detectionRange)
        {
            AttackLog("Player detectado -> CHASE");
            state = State.Chase;
        }
    }

    private void DoChase()
    {
        if (playerTransform == null)
        {
            state = State.Patrol;
            return;
        }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        if (dist > detectionRange * 1.3f)
        {
            AttackLog("Perdeu player -> PATROL");
            state = State.Patrol;
            return;
        }

        if (dist <= lightAttackRange + attackBufferDistance)
        {
            AttackLog($"Entrou em ATTACK | dist={dist:F2}");

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
        // impede múltiplas coroutines simultâneas
        if (_isPerformingAttack)
            return;

        if (playerTransform == null)
        {
            state = State.Chase;
            return;
        }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        animController?.SetSpeed(0f);

        AttackLog($"DoAttack | dist={dist:F2}");

        if (dist > lightAttackRange * 1.5f)
        {
            AttackLog("Saiu do range -> CHASE");
            state = State.Chase;
            return;
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        if (dist <= heavyTriggerRange && heavyCooldownTimer <= 0f)
        {
            AttackLog("Escolheu HEAVY");
            StartCoroutine(AttackRoutine(true));
            return;
        }

        if (lightCooldownTimer <= 0f)
        {
            AttackLog("Escolheu LIGHT");
            StartCoroutine(AttackRoutine(false));
        }
    }

    private IEnumerator AttackRoutine(bool isHeavy)
    {
        _isPerformingAttack = true;
        state = State.Attack;

        float startup = isHeavy ? heavyStartup : lightStartup;
        float activeTime = isHeavy ? heavyActiveTime : lightActiveTime;
        float recovery = isHeavy ? heavyRecovery : lightRecovery;
        float cooldown = isHeavy ? heavyCooldown : lightCooldown;
        float range = isHeavy ? heavyAttackRange : lightAttackRange;
        int damage = isHeavy ? heavyDamage : lightDamage;
        float knock = isHeavy ? heavyKnockback : lightKnockback;

        AttackLog($"AttackRoutine START -> {(isHeavy ? "HEAVY" : "LIGHT")}");

        _pendingAttack = true;
        _pendingRange = range;
        _pendingDamage = damage;
        _pendingKnock = knock;

        AttackLog($"ARMOU HIT | range={range:F2} dmg={damage} knock={knock}");

        animController?.TriggerAnimation(
            isHeavy ? "HeavyAttack" : "LightAttack"
        );

        if (isHeavy) heavyCooldownTimer = cooldown;
        else lightCooldownTimer = cooldown;

        yield return new WaitForSeconds(startup + activeTime + recovery);

        AttackLog("AttackRoutine END");

        _pendingAttack = false;
        _isPerformingAttack = false;

        if (state != State.Dead)
            state = State.Chase;
    }

    // ── NOVO: posição frontal do hit ──────────────────────────────────────
    private Vector2 GetAttackPointPosition()
    {
        if (attackPoint != null)
            return attackPoint.position;

        float facing = transform.localScale.x >= 0 ? 1f : -1f;

        return (Vector2)transform.position +
               Vector2.right * facing * attackPointOffset;
    }
    // ──────────────────────────────────────────────────────────────────────

    // Animation Event chama aqui
    public void DealAttackHit()
    {
        AttackLog($"AnimationEvent | pending={_pendingAttack}");

        if (!_pendingAttack)
        {
            AttackLog("EVENT IGNORADO");
            return;
        }

        _pendingAttack = false;

        // ── NOVO: usa hitbox frontal ─────────────────────────────────────
        Vector2 origin = GetAttackPointPosition();

        Collider2D hit = Physics2D.OverlapCircle(
            origin,
            _pendingRange,
            playerLayer
        );

        if (hit == null)
        {
            AttackLog("HIT FALHOU (sem collider)");
            return;
        }

        // PlayerHealth pode estar no collider...
        PlayerHealth ph = hit.GetComponent<PlayerHealth>();

        // ...ou no parent.
        if (ph == null)
            ph = hit.GetComponentInParent<PlayerHealth>();

        if (ph == null)
        {
            AttackLog("Collider encontrado, mas PlayerHealth não encontrado");
            return;
        }

        Vector2 dir =
            ((Vector2)ph.transform.position - origin).normalized;

        Vector2 force = dir * _pendingKnock;

        AttackLog($"HIT CONFIRMADO | dmg={_pendingDamage}");

        ph.TakeDamage(_pendingDamage, force);
        // ────────────────────────────────────────────────────────────────
    }

    public void TakeDamage(int damage)
    {
        if (state == State.Dead) return;

        _pendingAttack = false;
        _isPerformingAttack = false;

        currentHealth -= damage;

        animController?.TriggerAnimation("Hit");

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        StartCoroutine(HurtRoutine());
    }

    public void ReceiveKnockback(Vector2 force)
    {
        if (state == State.Dead) return;

        if (force.magnitude > maxKnockbackForce)
            force = force.normalized * maxKnockbackForce;

        force.y = Mathf.Clamp(force.y + 3f, 2f, 8f);

        StartCoroutine(KnockbackRoutine(force));
    }

    public void Stagger()
    {
        if (state == State.Dead) return;

        _pendingAttack = false;
        _isPerformingAttack = false;

        StopAllCoroutines();
        StartCoroutine(StaggerRoutine());
    }

    private IEnumerator HurtRoutine()
    {
        state = State.Hurt;
        yield return new WaitForSeconds(0.15f);

        if (state != State.Dead)
            state = State.Chase;
    }

    private IEnumerator KnockbackRoutine(Vector2 force)
    {
        isKnockedBack = true;
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(force, ForceMode2D.Impulse);

        yield return new WaitForSeconds(knockbackDuration);

        rb.linearVelocity = Vector2.zero;
        isKnockedBack = false;
    }

    private IEnumerator StaggerRoutine()
    {
        state = State.Hurt;
        isKnockedBack = true;

        animController?.TriggerAnimation("Stagger");

        yield return new WaitForSeconds(0.5f);

        isKnockedBack = false;

        if (state != State.Dead)
            state = State.Chase;
    }

    private void Die()
    {
        state = State.Dead;

        _pendingAttack = false;
        _isPerformingAttack = false;

        rb.linearVelocity = Vector2.zero;

        animController?.SetSpeed(0f);
        animController?.TriggerAnimation("Die");

        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(0.8f);
        Destroy(gameObject);
    }

    private void UpdateGroundedAnimation()
    {
        if (groundCheck == null) return;

        bool grounded = Physics2D.OverlapCircle(
            groundCheck.position,
            groundCheckRadius,
            groundLayer
        );

        animController?.UpdateGrounded(grounded);
    }

    private void UpdateFacing(float dir)
    {
        transform.localScale = new Vector3(
            Mathf.Abs(transform.localScale.x) * Mathf.Sign(dir),
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

        // ── NOVO: visualização da hitbox frontal ────────────────────────
        Vector2 attackOrigin = GetAttackPointPosition();

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(attackOrigin, lightAttackRange);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackOrigin, heavyAttackRange);
        // ────────────────────────────────────────────────────────────────
    }
}