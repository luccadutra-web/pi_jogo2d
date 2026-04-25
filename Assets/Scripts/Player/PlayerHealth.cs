using System.Collections;
using UnityEngine;


public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    
    [Header("Vida")]
    [SerializeField] private int maxHealth     = 5;
    [SerializeField] private int startingHealth = 5;

    [Header("Invencibilidade após dano (iframes)")]
    [Tooltip("Segundos de imunidade após receber dano normal (sem defesa).")]
    [SerializeField] private float iframeDuration = 0.6f;

    [Header("Defesa")]
    [Tooltip("Duração máxima de imunidade contínua enquanto defende (failsafe).")]
    [SerializeField] private float maxDefendDuration = 3f;

    [Header("Parry")]
    [Tooltip("Duração da janela de parry ao receber dano.")]
    [SerializeField] private float parryWindowDuration = 0.25f;
    [Tooltip("Cooldown após um parry bem-sucedido.")]
    [SerializeField] private float parryCooldown = 1.2f;
    [Tooltip("Força do knockback aplicado no atacante após parry.")]
    [SerializeField] private float parryKnockbackForce = 12f;

    
    private int   currentHealth;
    private bool  isDead;
    private bool  isInvincible;     
    private bool  isDefending;      
    private bool  parryWindowOpen;
    private float parryWindowTimer;
    private float parryCooldownTimer;

    
    private PlayerStamina stamina;

   
    public System.Action<int, int> OnHealthChanged;  
    public System.Action          OnDeath;
    public System.Action          OnParrySuccess;

    
    public int   CurrentHealth      => currentHealth;
    public int   MaxHealth          => maxHealth;
    public float HealthNormalized   => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
    public bool  IsDefending        => isDefending;
    public bool  ParryWindowOpen    => parryWindowOpen;

    
    void Awake()
    {
        Instance      = this;
        stamina       = GetComponent<PlayerStamina>();
        currentHealth = startingHealth;
    }

    void Update()
    {
        
        if (parryWindowOpen)
        {
            parryWindowTimer -= Time.deltaTime;
            if (parryWindowTimer <= 0f)
                parryWindowOpen = false;
        }

        
        if (parryCooldownTimer > 0f)
            parryCooldownTimer -= Time.deltaTime;

        
        if (isDefending && stamina && stamina.CurrentStamina <= 0f)
            StopDefend();
    }

    
    public void TakeDamage(int damage)
    {
        if (isDead) return;

        
        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            ExecuteParry();
            return;
        }

        
        if (isDefending)
        {
            
            return;
        }

        
        if (isInvincible) return;

        
        ApplyDamage(damage);

        
        TryOpenParryWindow();
    }

    
    public void StartDefend()
    {
        if (isDead || isDefending) return;
        isDefending = true;
        StartCoroutine(DefendTimeout());
    }

    
    public void StopDefend()
    {
        isDefending = false;
        StopCoroutine(nameof(DefendTimeout)); 
    }

   
    public void TryOpenParryWindow()
    {
        if (parryCooldownTimer > 0f) return;
        parryWindowOpen  = true;
        parryWindowTimer = parryWindowDuration;
    }

  

   private void ApplyDamage(int damage)
    {
        currentHealth = Mathf.Max(currentHealth - damage, 0);
        Debug.Log($"[PlayerHealth] Vida atual: {currentHealth}/{maxHealth}");
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    
        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        StartCoroutine(IFrameRoutine());
    }

    private void ExecuteParry()
    {
        parryWindowOpen   = false;
        parryCooldownTimer = parryCooldown;
        OnParrySuccess?.Invoke();
        Debug.Log("[PlayerHealth] PARRY bem-sucedido!");

        // Knockback no inimigo mais próximo (raio de 3u)
        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, 3f);
        foreach (Collider2D col in nearby)
        {
            if (col.gameObject == gameObject) continue;
            Vector2 dir = (col.transform.position - transform.position).normalized;
            col.SendMessage("ApplyKnockback", dir * parryKnockbackForce,
                            SendMessageOptions.DontRequireReceiver);
            col.SendMessage("Stagger", SendMessageOptions.DontRequireReceiver);
        }
    }

    private void Die()
    {
        isDead = true;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
    
        PlayerBehavior pb = GetComponent<PlayerBehavior>();
        if (pb != null) pb.isLocked = true;

        OnDeath?.Invoke();
        Debug.Log("[PlayerHealth] Player morreu.");
    }

    private IEnumerator IFrameRoutine()
    {
        isInvincible = true;
        yield return new WaitForSeconds(iframeDuration);
        isInvincible = false;
    }

    
    private IEnumerator DefendTimeout()
    {
        yield return new WaitForSeconds(maxDefendDuration);
        if (isDefending) StopDefend();
    }
}