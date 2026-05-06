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
    [SerializeField] private float lightAttackRange = 1.4f;
    [SerializeField] private int   lightDamage      = 1;
    [SerializeField] private float lightStartup     = 0.20f;
    [SerializeField] private float lightActiveTime  = 0.10f;
    [SerializeField] private float lightRecovery    = 0.30f;
    [SerializeField] private float lightCooldown    = 1.2f;

    [Header("Ataque pesado")]
    [SerializeField] private float heavyAttackRange  = 1.8f;
    [SerializeField] private int   heavyDamage       = 3;
    [SerializeField] private float heavyStartup      = 0.40f;
    [SerializeField] private float heavyActiveTime   = 0.15f;
    [SerializeField] private float heavyRecovery     = 0.55f;
    [SerializeField] private float heavyCooldown     = 3.0f;
    [SerializeField] private float heavyTriggerRange = 1.6f;

    [Header("Stagger (parry)")]
    [SerializeField] private float staggerDuration = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float     groundCheckRadius = 0.15f;

    [Header("Attack Hitbox")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float     attackPointOffset = 0.8f;
    [SerializeField] private LayerMask playerLayer;

    private enum State { Patrol, Chase, Attack, Hurt, Dead }
    private State state = State.Patrol;

    private int   currentHealth;
    private bool  isStaggered;
    private bool  _isPerformingAttack;
    private float lightCooldownTimer;
    private float heavyCooldownTimer;

    private Vector2   patrolOrigin;
    private float     patrolDir = 1f;
    private Transform playerTransform;
    private Rigidbody2D rb;
    private CharacterAnimationController animController;

    private bool  _pendingAttack;
    private float _pendingRange;
    private int   _pendingDamage;

    private void Log(string msg) { if (debugAttack) Debug.Log($"[ENEMY {name}] {msg}"); }

    void Awake()
    {
        rb             = GetComponent<Rigidbody2D>();
        animController = GetComponent<CharacterAnimationController>();
        currentHealth  = maxHealth;
        rb.freezeRotation = true;
    }

    void Start()
    {
        patrolOrigin = transform.position;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
        else
            Debug.LogError("[EnemyBehavior] Player não encontrado — verifique a tag 'Player'.");
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
        Log($"DoAttack | dist={dist:F2}");

        if (dist > lightAttackRange * 2f) { Log("Saiu do range → CHASE"); state = State.Chase; return; }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        animController?.SetSpeed(0f);

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

    private IEnumerator AttackRoutine(bool isHeavy)
    {
        _isPerformingAttack = true;

        float startup    = isHeavy ? heavyStartup    : lightStartup;
        float activeTime = isHeavy ? heavyActiveTime : lightActiveTime;
        float recovery   = isHeavy ? heavyRecovery   : lightRecovery;
        float cooldown   = isHeavy ? heavyCooldown   : lightCooldown;
        float range      = isHeavy ? heavyAttackRange : lightAttackRange;
        int   damage     = isHeavy ? heavyDamage     : lightDamage;

        _pendingAttack = true;
        _pendingRange  = range;
        _pendingDamage = damage;

        if (isHeavy) heavyCooldownTimer = cooldown;
        else         lightCooldownTimer = cooldown;

        animController?.TriggerAnimation(isHeavy ? "HeavyAttack" : "LightAttack");
        Log($"AttackRoutine START → {(isHeavy ? "HEAVY" : "LIGHT")}");

        yield return new WaitForSeconds(startup);

        if (_pendingAttack)
        {
            Log("Active — aplicando hit.");
            ApplyHit(range, damage);
            _pendingAttack = false;
        }

        yield return new WaitForSeconds(activeTime + recovery);

        Log("AttackRoutine END");
        _isPerformingAttack = false;

        if (state != State.Dead)
        {
            state = State.Chase;
        }
            
    }

    private void ApplyHit(float range, int damage)
    {
        Vector2 origin = GetAttackPointPosition();
        Log($"ApplyHit | origin={origin} | range={range}");

        Collider2D hit = Physics2D.OverlapCircle(origin, range, playerLayer);
        if (hit == null) { Log("ApplyHit — nenhum collider."); return; }

        PlayerHealth ph = hit.GetComponent<PlayerHealth>()
                       ?? hit.GetComponentInParent<PlayerHealth>();

        if (ph == null) { Log($"ApplyHit — sem PlayerHealth em '{hit.name}'."); return; }

        Log($"HIT CONFIRMADO | dmg={damage}");
        ph.TakeDamage(damage);
    }

    public void DealAttackHit()
    {
        Log($"AnimationEvent | pending={_pendingAttack}");
        if (!_pendingAttack) { Log("Ignorado."); return; }
        _pendingAttack = false;
        ApplyHit(_pendingRange, _pendingDamage);
    }

    public void TakeDamage(int damage)
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        currentHealth -= damage;
        Log($"TakeDamage | dmg={damage} | hp={currentHealth}/{maxHealth}");

        animController?.TriggerAnimation("Hit");
        GetComponent<HitFlash>()?.Flash();

        if (currentHealth <= 0) { Die(); return; }

        StartCoroutine(HurtRoutine());
    }

    public void Stagger()
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        StopAllCoroutines();
        StartCoroutine(StaggerRoutine());
    }

    private IEnumerator HurtRoutine()
    {
        state = State.Hurt;
        yield return new WaitForSeconds(0.25f);
        if (state != State.Dead) state = State.Chase;
    }

    private IEnumerator StaggerRoutine()
    {
        state       = State.Hurt;
        isStaggered = true;
        animController?.TriggerAnimation("Stagger");

        yield return new WaitForSeconds(staggerDuration);

        isStaggered = false;
        if (state != State.Dead) state = State.Chase;
    }

    private void Die()
    {
        state               = State.Dead;
        _pendingAttack      = false;
        _isPerformingAttack = false;
        rb.linearVelocity   = Vector2.zero;

        animController?.SetSpeed(0f);
        animController?.TriggerAnimation("Die");

        StartCoroutine(DeathRoutine());
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

        Vector2 attackOrigin = GetAttackPointPosition();
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(attackOrigin, lightAttackRange);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(attackOrigin, heavyAttackRange);
    }
}