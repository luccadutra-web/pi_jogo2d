using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyBehaviorTest : MonoBehaviour
{
    
    [Header("Saúde")]
    [SerializeField] private int maxHealth = 5;

    
    [Header("Knockback")]
    [SerializeField] private float knockbackDuration = 0.2f;
    [SerializeField] private float maxKnockbackForce = 15f;

    
    [Header("Pontos")]
    [SerializeField] private int pointsGiven = 10;

    
    [Header("Movimentação")]
    [Tooltip("Distância para começar a perseguir o player.")]
    [SerializeField] private float detectionRange   = 6f;
    [Tooltip("Distância mínima mantida do player (para de andar).")]
    [SerializeField] private float stoppingDistance = 1.2f;
    [SerializeField] private float moveSpeed        = 2.5f;

    
    [Header("Ataque leve")]
    [SerializeField] private int   lightDamage   = 1;
    [SerializeField] private float lightCooldown = 1.2f;

    [Header("Ataque pesado")]
    [SerializeField] private int   heavyDamage        = 3;
    [Tooltip("A cada quantos ataques leves ocorre um pesado (ex: 3 = leve,leve,leve,pesado).")]
    [SerializeField] private int   heavyEvery         = 3;
    [SerializeField] private float heavyExtraCooldown = 0.6f;

    [Tooltip("Alcance de ataque.")]
    [SerializeField] private float attackRange = 1.4f;

    
    private int         currentHealth;
    private bool        isDead;
    private bool        isKnockedBack;
    private Rigidbody2D rb;
    private Animator    animator;
    private Transform   playerTransform;

    private float nextAttackTime;
    private int   lightAttackCount;

    
    void Awake()
    {
        rb            = GetComponent<Rigidbody2D>();
        animator      = GetComponent<Animator>();
        currentHealth = maxHealth;
        rb.freezeRotation = true;
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
            Debug.Log("[Enemy] Player encontrado: " + playerObj.name);
        }
        else
        {
            Debug.LogError("[Enemy] Player NÃO encontrado! Verifique se o Player tem a tag 'Player'.");
        }
    }

    void Update()
    {
        if (isDead || playerTransform == null || isKnockedBack) return;

        float dist = Vector2.Distance(transform.position, playerTransform.position);

        if (dist <= detectionRange)
        {
            if (dist <= attackRange)
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                TryAttack();
            }
            else if (dist > stoppingDistance)
            {
                MoveTowardsPlayer();
            }
            else
            {
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            }
        }
        else
        {
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    

    private void MoveTowardsPlayer()
    {
        float dir = playerTransform.position.x > transform.position.x ? 1f : -1f;
        rb.linearVelocity = new Vector2(dir * moveSpeed, rb.linearVelocity.y);

        transform.localScale = new Vector3(
            Mathf.Abs(transform.localScale.x) * dir,
            transform.localScale.y,
            transform.localScale.z
        );
    }

   

    private void TryAttack()
    {
        if (Time.time < nextAttackTime) return;

        bool doHeavy = lightAttackCount > 0 && (lightAttackCount % heavyEvery == 0);

        if (doHeavy)
        {
            ExecuteHeavyAttack();
            lightAttackCount = 0;
        }
        else
        {
            ExecuteLightAttack();
            lightAttackCount++;
        }
    }

    private void ExecuteLightAttack()
    {
        playerTransform.GetComponent<PlayerHealth>()?.TakeDamage(lightDamage);
        Debug.Log($"[Enemy] Ataque leve — {lightDamage} dano.");
        nextAttackTime = Time.time + lightCooldown;
    }

    private void ExecuteHeavyAttack()
    {
        playerTransform.GetComponent<PlayerHealth>()?.TakeDamage(heavyDamage);
        Debug.Log($"[Enemy] Ataque PESADO — {heavyDamage} dano.");
        nextAttackTime = Time.time + lightCooldown + heavyExtraCooldown;
    }

    

    public void TakeDamage(int damage)
    {
        if (isDead) return;
        currentHealth -= damage;
       // animator?.SetTrigger("Hit");
        if (currentHealth <= 0) Die();
    }

    public void ApplyKnockback(Vector2 force)
    {
        if (isDead) return;
        if (force.magnitude > maxKnockbackForce)
            force = force.normalized * maxKnockbackForce;
        force.y = Mathf.Clamp(force.y + 2f, 0f, 6f);
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(force, ForceMode2D.Impulse);
        StartCoroutine(KnockbackPause());
    }

    public void Stagger()
    {
        if (isDead) return;
        StartCoroutine(KnockbackPause());
        Debug.Log("[Enemy] Staggered pelo parry!");
    }

    private IEnumerator KnockbackPause()
    {
        isKnockedBack = true;
        yield return new WaitForSeconds(knockbackDuration);
        isKnockedBack = false;
    }

  

    private void Die()
    {
        isDead = true;
        rb.linearVelocity = Vector2.zero;
        GivePoints();
        //animator?.SetTrigger("Die");
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        yield return new WaitForSeconds(0.3f);
        Destroy(gameObject);
    }

    private void GivePoints()
    {
        FindFirstObjectByType<PlayerPoints>()?.AddPoints(pointsGiven);
    }

    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
}