using System.Collections;
using UnityEngine;

/// <summary>
/// EnemyBehavior — IA base do inimigo.
///
/// Estados:
///   Patrol  → patrulha entre dois pontos até detectar o player
///   Chase   → persegue o player
///   Attack  → executa ataque leve ou pesado dependendo do cooldown
///   Hurt    → recebeu dano, breve pausa
///   Dead    → morto
///
/// Implementa IDamageable e IStaggerable para integrar com o parry do player.
/// O dano ao player é aplicado via PlayerHealth.TakeDamage — sem acesso direto ao Rigidbody.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class EnemyBehavior : MonoBehaviour, IDamageable, IStaggerable
{
    
    [Header("Saúde")]
    [SerializeField] private int maxHealth = 5;

    [Header("Patrulha")]
    [SerializeField] private float patrolSpeed    = 2f;
    [SerializeField] private float patrolRange    = 4f;   

    [Header("Perseguição")]
    [SerializeField] private float detectionRange   = 6f;
    [SerializeField] private float chaseSpeed       = 3.5f;
    [SerializeField] private float stoppingDistance = 1.2f;

    
    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange   = 1.4f;
    [SerializeField] private int   lightDamage        = 1;
    [SerializeField] private float lightKnockback     = 5f;
    [SerializeField] private float lightStartup       = 0.20f;
    [SerializeField] private float lightActiveTime    = 0.10f;
    [SerializeField] private float lightRecovery      = 0.30f;
    [SerializeField] private float lightCooldown      = 1.2f;

    [Header("Ataque pesado")]
    [SerializeField] private float heavyAttackRange   = 1.8f;
    [SerializeField] private int   heavyDamage        = 3;
    [SerializeField] private float heavyKnockback     = 11f;
    [SerializeField] private float heavyStartup       = 0.40f;
    [SerializeField] private float heavyActiveTime    = 0.15f;
    [SerializeField] private float heavyRecovery      = 0.55f;
    [SerializeField] private float heavyCooldown      = 3.0f;
    [SerializeField] private float heavyTriggerRange  = 1.6f;

   
    [Header("Knockback recebido")]
    [SerializeField] private float knockbackDuration = 0.20f;
    [SerializeField] private float maxKnockbackForce = 15f;

   
    //[Header("Pontos")]
    //[SerializeField] private int pointsGiven = 10;

    
    private enum State { Patrol, Chase, Attack, Hurt, Dead }
    private State state = State.Patrol;

    private int   currentHealth;
    private bool  isKnockedBack;
    private float lightCooldownTimer;
    private float heavyCooldownTimer;

    private Vector2   patrolOrigin;
    private float     patrolDir    = 1f;
    private Transform playerTransform;
    private Rigidbody2D rb;
    private Animator    animator;

    
    void Awake()
    {
        rb            = GetComponent<Rigidbody2D>();
        animator      = GetComponent<Animator>();
        currentHealth = maxHealth;
        rb.freezeRotation = true;
    }

    void Start()
    {
        patrolOrigin = transform.position;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }

        else
        {
             Debug.LogError("[EnemyBehavior] Player não encontrado — verifique a tag 'Player'.");
        }
           
    }

   

    void Update()
    {
        if (state == State.Dead || state == State.Hurt || isKnockedBack) return;

        TickCooldowns();

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

       
        float left  = patrolOrigin.x - patrolRange;
        float right = patrolOrigin.x + patrolRange;

        if (patrolDir > 0 && transform.position.x >= right) patrolDir = -1f;
        else if (patrolDir < 0 && transform.position.x <= left) patrolDir =  1f;

        
        if (playerTransform != null &&
            Vector2.Distance(transform.position, playerTransform.position) <= detectionRange)
        {
            
            state = State.Chase;
        }
    }

    private void DoChase()
    {
        if (playerTransform == null) { state = State.Patrol; return; }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        
        if (dist > detectionRange * 1.3f) { state = State.Patrol; return; }

        
        if (dist <= lightAttackRange)
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            state = State.Attack;
            return;
        }

        
        float dir = Mathf.Sign(playerTransform.position.x - transform.position.x);
        rb.linearVelocity = new Vector2(dir * chaseSpeed, rb.linearVelocity.y);
        UpdateFacing(dir);
    }

    private void DoAttack()
    {
        if (playerTransform == null) { state = State.Chase; return; }

        float dist = Vector2.Distance(transform.position, playerTransform.position);

       
        if (dist > lightAttackRange * 1.5f) 
        { 
            state = State.Chase; 
            Debug.Log("Ataque leve (inimigo)");
            return; 
        }

        rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

       
        if (dist <= heavyTriggerRange && heavyCooldownTimer <= 0f)
        {
            StartCoroutine(AttackRoutine(isHeavy: true));
            Debug.Log("Ataque pesado (inimigo)");
            return;
        }

       
        if (lightCooldownTimer <= 0f)
        {
            StartCoroutine(AttackRoutine(isHeavy: false));
        }
    }

   

    private IEnumerator AttackRoutine(bool isHeavy)
    {
        state = State.Attack;

        float startup    = isHeavy ? heavyStartup    : lightStartup;
        float activeTime = isHeavy ? heavyActiveTime : lightActiveTime;
        float recovery   = isHeavy ? heavyRecovery   : lightRecovery;
        float cooldown   = isHeavy ? heavyCooldown   : lightCooldown;
        float range      = isHeavy ? heavyAttackRange : lightAttackRange;
        int   damage     = isHeavy ? heavyDamage     : lightDamage;
        float knock      = isHeavy ? heavyKnockback  : lightKnockback;

        //animator?.SetTrigger(isHeavy ? "HeavyAttack" : "LightAttack");

        // Define cooldown imediatamente para não atacar de novo enquanto executa
        if (isHeavy) heavyCooldownTimer = cooldown;
        else         lightCooldownTimer = cooldown;

        
        yield return new WaitForSeconds(startup);

        
        if (playerTransform != null &&
            Vector2.Distance(transform.position, playerTransform.position) <= range)
        {
            PlayerHealth ph = playerTransform.GetComponent<PlayerHealth>();
            if (ph != null)
            {
                Vector2 dir   = (playerTransform.position - transform.position).normalized;
                Vector2 force = dir * knock;
                ph.TakeDamage(damage, force);
            }
        }

        
        yield return new WaitForSeconds(activeTime + recovery);

        
        if (state != State.Dead)
        {
            state = State.Chase;
        }
            
    }

    
    public void TakeDamage(int damage)
    {
        if (state == State.Dead) return;

        currentHealth -= damage;
        //animator?.SetTrigger("Hit");

        if (currentHealth <= 0) { Die(); return; }
        Debug.Log("Levou dano (inimigo)");

        StartCoroutine(HurtRoutine());
    }

    public void ReceiveKnockback(Vector2 force)
    {
        if (state == State.Dead) return;

        if (force.magnitude > maxKnockbackForce)
        {
            force = force.normalized * maxKnockbackForce;

        }
        force.y = Mathf.Clamp(force.y + 3f, 2f, 8f);
        Debug.Log("Knockback (inimigo)");

        StartCoroutine(KnockbackRoutine(force));
    }

    
    public void Stagger()
    {
        if (state == State.Dead) return;
        StopAllCoroutines();
        StartCoroutine(StaggerRoutine());
    }

   

    private IEnumerator HurtRoutine()
    {
        state = State.Hurt;
        yield return new WaitForSeconds(0.15f);
        if (state != State.Dead) state = State.Chase;
    }

    private IEnumerator KnockbackRoutine(Vector2 force)
    {
        isKnockedBack     = true;
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(force, ForceMode2D.Impulse);

        yield return new WaitForSeconds(knockbackDuration);

        rb.linearVelocity = Vector2.zero;
        isKnockedBack     = false;
    }

    private IEnumerator StaggerRoutine()
    {
        state         = State.Hurt;
        isKnockedBack = true;
        //animator?.SetTrigger("Stagger");

        yield return new WaitForSeconds(0.5f);

        isKnockedBack = false;
        if (state != State.Dead) state = State.Chase;
    }

    private void Die()
    {
        state             = State.Dead;
        rb.linearVelocity = Vector2.zero;
        //animator?.SetTrigger("Die");

        //FindFirstObjectByType<PlayerPoints>()?.AddPoints(pointsGiven);

        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(0.8f);
        Destroy(gameObject);
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

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);

        // Limites de patrulha
        Vector3 origin = Application.isPlaying ? (Vector3)patrolOrigin : transform.position;
        Gizmos.color   = Color.white;
        Gizmos.DrawLine(
            new Vector3(origin.x - patrolRange, transform.position.y),
            new Vector3(origin.x + patrolRange, transform.position.y)
        );
    }
}