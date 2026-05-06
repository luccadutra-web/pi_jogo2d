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
    // FIX ALCANCE — lightAttackRange era 1.4f, mas GetAttackPointPosition()
    // adicionava attackPointOffset (0.8f) ao transform antes de usar esse raio,
    // totalizando ~2.2f de alcance real. O player tem lightAttackRange=1.5f a
    // partir do attackPoint que já está deslocado — resultando em alcance real menor.
    // Solução: attackPointOffset zerado por padrão (use um Transform filho em vez
    // de offset manual) e ranges reduzidos para refletir a distância real da hitbox.
    // Se você usar um Transform filho como attackPoint, o offset não importa.
    [SerializeField] private float lightAttackRange = 1.5f;
    [SerializeField] private int   lightDamage      = 1;
    [SerializeField] private float lightStartup     = 0.20f;
    [SerializeField] private float lightActiveTime  = 0.10f;
    [SerializeField] private float lightRecovery    = 0.30f;
    [SerializeField] private float lightCooldown    = 1.2f;

    [Header("Ataque pesado")]
    [SerializeField] private float heavyAttackRange  = 1.8f;
    [SerializeField] private int   heavyDamage       = 3;
    [SerializeField] private float heavyStartup      = 0.60f;
    [SerializeField] private float heavyActiveTime   = 0.15f;
    [SerializeField] private float heavyRecovery     = 0.55f;
    [SerializeField] private float heavyCooldown     = 3.0f;
    [SerializeField] private float heavyTriggerRange = 1.6f;

    [Header("Telegraph do heavy (VFX/animação de carregamento)")]
    [Tooltip("VFX ou GameObject ativado durante o startup do heavy para sinalizar o ataque")]
    [SerializeField] private GameObject heavyTelegraphVFX;

    [Header("Stagger (parry)")]
    [SerializeField] private float staggerDuration = 0.5f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float     groundCheckRadius = 0.15f;

    [Header("Attack Hitbox")]
    [SerializeField] private Transform attackPoint;
    // FIX ALCANCE — attackPointOffset era 0.8f. Quando attackPoint=null, a hitbox
    // era criada a partir do transform.position + 0.8f de offset E depois expandida
    // pelo raio do ataque, dando alcance real = offset + range. O player não tem
    // esse offset duplo. Reduzido para 0.5f como fallback; o ideal é atribuir um
    // Transform filho posicionado corretamente no Inspector (igual ao player).
    [SerializeField] private float     attackPointOffset = 0.5f;
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

        if (isHeavy && heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(true);

        Log($"AttackRoutine START → {(isHeavy ? "HEAVY" : "LIGHT")} | startup={startup}s");

        yield return new WaitForSeconds(startup);

        if (isHeavy && heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

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
            state = State.Chase;
    }

    private void ApplyHit(float range, int damage)
    {
        // FIX ALCANCE — GetAttackPointPosition retorna a origem da hitbox.
        // O OverlapCircle expande o raio a partir desse ponto. Se attackPoint
        // for null, o fallback usa attackPointOffset para calcular a posição —
        // mas isso não muda o raio. O range aqui já deve ser o raio da hitbox,
        // não o alcance total. Certifique-se de que attackPoint está configurado
        // no Inspector para ter um alcance consistente com o player.
        Vector2 origin = GetAttackPointPosition();

        Collider2D hit = Physics2D.OverlapCircle(origin, range, playerLayer);
        if (hit == null) return;

        PlayerHealth ph = hit.GetComponent<PlayerHealth>()
                       ?? hit.GetComponentInParent<PlayerHealth>();

        if (ph == null) return;

        ph.RegisterAttacker(transform);
        ph.TakeDamage(damage, transform.position);

        Log($"HIT CONFIRMADO | dmg={damage}");
    }

    public void DealAttackHit()
    {
        Log($"AnimationEvent | pending={_pendingAttack}");
        if (!_pendingAttack) return;
        _pendingAttack = false;
        ApplyHit(_pendingRange, _pendingDamage);
    }

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        currentHealth -= damage;
        Log($"TakeDamage | dmg={damage} | hp={currentHealth}/{maxHealth}");

        // FIX HURT DELAY — inimigo usa TriggerAnimation (com ResetTrigger) porque
        // estados reativos do inimigo não sofrem o mesmo problema: o inimigo não
        // tem spam de input e o Animator raramente está em transição no momento
        // do hit. Mantido como estava; se ocorrer o mesmo delay, troque por
        // SetTriggerDirect("Hit") aqui também.
        animController?.TriggerAnimation("Hit");
        GetComponent<HitFlash>()?.Flash();

        if (currentHealth <= 0) { Die(); return; }

        StartCoroutine(HurtRoutine());
    }

    public void TakeDamage(int damage)
    {
        TakeDamage(damage, default);
    }

    public void Stagger()
    {
        if (state == State.Dead) return;

        _pendingAttack      = false;
        _isPerformingAttack = false;

        if (heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

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

        if (heavyTelegraphVFX != null)
            heavyTelegraphVFX.SetActive(false);

        animController?.SetSpeed(0f);
        animController?.TriggerAnimation("Die");
        SpecialSystem.Instance?.AddKillEnergy();

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

        // Fallback sem Transform filho: usa offset a partir do pivot do sprite.
        // IMPORTANTE: este fallback adiciona offset À posição do transform,
        // e então ApplyHit expande o OverlapCircle pelo range a partir daqui.
        // Alcance real = attackPointOffset + range. Para igualar ao player,
        // configure um Transform filho como attackPoint no Inspector.
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