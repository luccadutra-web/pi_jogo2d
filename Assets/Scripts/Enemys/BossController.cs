using System.Collections;
using UnityEngine;

public class BossController : MonoBehaviour
{
    
    [Header("Referências")]
    public Transform player;
    public Animator bossAnimator;
    public GameObject projectilePrefab;
    public Transform projectileSpawnPoint;
    public Transform groundCheck;
    public LayerMask groundLayer;

    [Header("Vida")]
    public int maxHP = 200;

    [Header("Movimento — Fase 1")]
    public float patrolSpeed = 2f;
    public float patrolRange = 4f;
    public float dashSpeed = 18f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 3f;

    [Header("Movimento — Fase 2")]
    public float patrolSpeedPhase2 = 3.5f;
    public float dashSpeedPhase2 = 26f;

    [Header("Ataques")]
    public float attackRange = 1.5f;
    public int meleeDamage = 15;
    public float meleeCooldown = 1.2f;

    public float projectileSpeed = 8f;
    public float shootCooldown = 2.5f;
    public float shootCooldownPhase2 = 1.2f;
    public int projectilesPerBurst = 5;  

    public float slamCooldown = 5f;        
    public float slamLaunchForce = 18f;
    public int slamDamage = 30;
    public float slamRadius = 2.5f;

    [Header("Detecção")]
    public float aggroRange = 10f;
    public float groundCheckRadius = 0.2f;

    
    private Rigidbody2D rb;
    private int currentHP;
    private bool isPhase2 = false;
    private bool isDead = false;

    private enum State { Patrol, Chase, Attack, Dash, Slam, Dead }
    private State state = State.Patrol;

    private Vector2 patrolOrigin;
    private float patrolDir = 1f;
    private bool facingRight = true;

    private float dashTimer;
    private float dashCooldownTimer;
    private float shootTimer;
    private float meleeTimer;
    private float slamTimer;

    private bool isGrounded;

    
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        currentHP = maxHP;
        patrolOrigin = transform.position;
        dashCooldownTimer = dashCooldown; 

        
        if (player != null)
        {
            Collider2D bossCol  = GetComponent<Collider2D>();
            Collider2D playerCol = player.GetComponent<Collider2D>();
            if (bossCol != null && playerCol != null)
                Physics2D.IgnoreCollision(bossCol, playerCol);
        }
    }

    void Update()
    {
        if (isDead) return;

        isGrounded = Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer);
        CheckPhase();
        TickCooldowns();

        switch (state)
        {
            case State.Patrol: DoPatrol(); break;
            case State.Chase:  DoChase();  break;
            case State.Attack: DoAttack(); break;
            case State.Dash:   break;       
            case State.Slam:   break;
        }

        UpdateAnimator();
    }

    
    void CheckPhase()
    {
        if (!isPhase2 && currentHP <= maxHP / 2)
        {
            isPhase2 = true;
           // bossAnimator?.SetTrigger("Phase2");
            
        }
    }

    
    void TickCooldowns()
    {
        dashCooldownTimer -= Time.deltaTime;
        shootTimer        -= Time.deltaTime;
        meleeTimer        -= Time.deltaTime;
        slamTimer         -= Time.deltaTime;
    }

    
    void DoPatrol()
    {
        float speed = isPhase2 ? patrolSpeedPhase2 : patrolSpeed;
        rb.linearVelocity = new Vector2(patrolDir * speed, rb.linearVelocity.y);

        float leftLimit  = patrolOrigin.x - patrolRange;
        float rightLimit = patrolOrigin.x + patrolRange;

        // Força a inversão assim que passa do limite, sem depender de Mathf.Abs
        if (patrolDir > 0 && transform.position.x >= rightLimit)
        {
            patrolDir = -1f;
            Flip(false);
        }
        else if (patrolDir < 0 && transform.position.x <= leftLimit)
        {
            patrolDir = 1f;
            Flip(true);
        }

        if (player != null && Vector2.Distance(transform.position, player.position) <= aggroRange)
            state = State.Chase;
    }

    void DoChase()
    {
        if (player == null) { state = State.Patrol; return; }

        float dist = Vector2.Distance(transform.position, player.position);
    
        Debug.Log($"[Boss] DoChase | dist={dist} | attackRange={attackRange} | dashTimer={dashCooldownTimer}");

        
        if (dist > aggroRange * 1.2f) { state = State.Patrol; return; }

        
        if (dist <= attackRange)
        {
            rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
            state = State.Attack;
            return;
        }

        
        if (dashCooldownTimer <= 0f && dist > attackRange * 2.5f)
        {
            StartCoroutine(DashCoroutine());
            return;
        }

        
        if (isPhase2 && slamTimer <= 0f && isGrounded)
        {
            StartCoroutine(SlamCoroutine());
            return;
        }

        
        float dir = Mathf.Sign(player.position.x - transform.position.x);
        float speed = isPhase2 ? patrolSpeedPhase2 : patrolSpeed;
        rb.linearVelocity = new Vector2(dir * speed, rb.linearVelocity.y);
        Flip(dir > 0);
    }

    void DoAttack()
    {
        if (player == null) { state = State.Chase; return; }

        float dist = Vector2.Distance(transform.position, player.position);

        
        if (dist > attackRange * 2f) { state = State.Chase; return; }

        
        if (meleeTimer <= 0f)
        {
            meleeTimer = meleeCooldown;
            //bossAnimator?.SetTrigger("Melee");
            DealMeleeDamage();
        }

        
        float cd = isPhase2 ? shootCooldownPhase2 : shootCooldown;
        if (shootTimer <= 0f)
        {
            shootTimer = cd;
            if (isPhase2)
                StartCoroutine(ShootBurst());
            else
                ShootProjectile();
        }
    }

    
    IEnumerator DashCoroutine()
    {
        state = State.Dash;
        dashCooldownTimer = isPhase2 ? dashCooldown * 0.7f : dashCooldown;
        dashTimer = dashDuration;

        //bossAnimator?.SetTrigger("Dash");
        CameraShake.Instance?.Shake(0.12f, 0.15f);

        float dir = player != null
            ? Mathf.Sign(player.position.x - transform.position.x)
            : (facingRight ? 1f : -1f);

        Flip(dir > 0);

        float speed = isPhase2 ? dashSpeedPhase2 : dashSpeed;

        while (dashTimer > 0f)
        {
            dashTimer -= Time.deltaTime;
            rb.linearVelocity = new Vector2(dir * speed, rb.linearVelocity.y);
            yield return null;
        }

        rb.linearVelocity = new Vector2(0, rb.linearVelocity.y);
        state = State.Chase;
    }

    IEnumerator SlamCoroutine()
    {
        state = State.Slam;
        slamTimer = slamCooldown;
        //bossAnimator?.SetTrigger("Slam");

        // Salto
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, slamLaunchForce);
        yield return new WaitUntil(() => rb.linearVelocity.y < 0f);  
        yield return new WaitUntil(() => isGrounded);           

        
        CameraShake.Instance?.Shake(0.35f, 0.45f);

        
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, slamRadius);
        foreach (var hit in hits)
        {
            if (hit.CompareTag("Player"))
                hit.GetComponent<PlayerHealth>()?.TakeDamage(slamDamage);
        }

        //bossAnimator?.SetTrigger("SlamLand");
        state = State.Chase;
    }

    IEnumerator ShootBurst()
    {
        for (int i = 0; i < projectilesPerBurst; i++)
        {
            ShootProjectile(randomSpread: true);
            yield return new WaitForSeconds(0.12f);
        }
    }

    
    void DealMeleeDamage()
    {
        
        if (player == null) return;
        float dist = Vector2.Distance(transform.position, player.position);
        if (dist <= attackRange)
        {
            PlayerHealth ph = player.GetComponent<PlayerHealth>();
            if (ph != null)
                ph.TakeDamage(meleeDamage);
            else
                Debug.LogWarning("[Boss] PlayerHealth não encontrado no player!");
        }
    }

    void ShootProjectile(bool randomSpread = false)
    {
        if (projectilePrefab == null || projectileSpawnPoint == null || player == null) return;

        //bossAnimator?.SetTrigger("Shoot");

        Vector2 dir = ((Vector2)player.position - (Vector2)projectileSpawnPoint.position).normalized;

        if (randomSpread)
        {
            float angle = Random.Range(-25f, 25f);
            dir = Quaternion.Euler(0, 0, angle) * dir;
        }

        GameObject proj = Instantiate(projectilePrefab, projectileSpawnPoint.position, Quaternion.identity);
        Rigidbody2D projRb = proj.GetComponent<Rigidbody2D>();
        if (projRb != null)
            projRb.linearVelocity = dir * projectileSpeed;

        Destroy(proj, 5f);
    }

    
    public void TakeDamage(int amount)
    {
        if (isDead) return;

        currentHP = Mathf.Max(0, currentHP - amount);
        //bossAnimator?.SetTrigger("Hurt");

        if (currentHP == 0)
            StartCoroutine(DieCoroutine());
    }

    IEnumerator DieCoroutine()
    {
        isDead = true;
        state = State.Dead;
        rb.linearVelocity = Vector2.zero;
        rb.isKinematic = true;

        //bossAnimator?.SetTrigger("Die");
        CameraShake.Instance?.Shake(0.6f, 0.5f);

        yield return new WaitForSeconds(2.5f); 
        Destroy(gameObject);
    }

    
    void Flip(bool faceRight)
    {
        if (facingRight == faceRight) return;
        facingRight = faceRight;
        Vector3 s = transform.localScale;
        s.x *= -1;
        transform.localScale = s;
    }

    void UpdateAnimator()
    {
        if (bossAnimator == null) return;
        //bossAnimator.SetFloat("SpeedX", Mathf.Abs(rb.linearVelocity.x));
        //bossAnimator.SetBool("IsGrounded", isGrounded);
        //bossAnimator.SetFloat("HPRatio", (float)currentHP / maxHP);
    }

    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, aggroRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, slamRadius);

        if (groundCheck)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        Gizmos.color = Color.white;
        Gizmos.DrawLine(
            new Vector3(patrolOrigin.x - patrolRange, transform.position.y),
            new Vector3(patrolOrigin.x + patrolRange, transform.position.y)
        );
    }
}