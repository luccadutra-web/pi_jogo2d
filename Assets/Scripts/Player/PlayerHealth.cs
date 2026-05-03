using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PlayerBehavior))]
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("Vida")]
    [SerializeField] private int maxHealth      = 5;
    [SerializeField] private int startingHealth = 5;

    [Header("iFrames")]
    [SerializeField] private float iframeDuration = 0.5f; // Reduzido de 0.8s

    [Header("Parry")]
    [SerializeField] private float parryWindowDuration  = 0.16f;
    [SerializeField] private float parryCooldown        = 1.2f;
    [SerializeField] private float parryKnockbackForce  = 14f;
    [SerializeField] private float parryHitStopDuration = 0.22f;

    [Header("Defesa")]
    [SerializeField] private float maxDefendDuration = 4f;
    [Range(0f, 1f)]
    [SerializeField] private float blockStaminaRatio = 0.8f;
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageRatio  = 0.2f;

    private int   currentHealth;
    private bool  isDead;
    private bool  isInvincible;
    private bool  isDefending;
    private bool  parryWindowOpen;
    private float parryWindowTimer;
    private float parryCooldownTimer;

    private Coroutine defendCoroutine;

    private PlayerBehavior behavior;
    private PlayerStamina  stamina;

    public System.Action<int, int> OnHealthChanged;
    public System.Action           OnDamaged;
    public System.Action           OnBlocked;
    public System.Action           OnBlockBroken;
    public System.Action           OnParrySuccess;
    public System.Action           OnDeath;

    public int   CurrentHealth    => currentHealth;
    public int   MaxHealth        => maxHealth;
    public float HealthNormalized => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
    public bool  IsDefending      => isDefending;
    public bool  ParryWindowOpen  => parryWindowOpen;
    public bool  IsDead           => isDead;

    void Awake()
    {
        Instance      = this;
        behavior      = GetComponent<PlayerBehavior>();
        stamina       = GetComponent<PlayerStamina>();
        currentHealth = startingHealth;
    }

    void Start()
    {
        if (stamina != null)
            stamina.OnStaminaEmpty += BreakDefend;
    }

    void OnDestroy()
    {
        if (stamina != null)
            stamina.OnStaminaEmpty -= BreakDefend;
    }

    void Update()
    {
        if (parryCooldownTimer > 0f) parryCooldownTimer -= Time.deltaTime;

        if (parryWindowOpen)
        {
            Debug.Log("Janela do parry aberta");
            parryWindowTimer -= Time.deltaTime;

            if (parryWindowTimer <= 0f)
                parryWindowOpen = false;
        }
    }

    public void TakeDamage(int damage, Vector2 knockbackForce = default)
    {
        if (isDead) return;

        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            ExecuteParry();
            Debug.Log("Parry feito");
            return;
        }

        if (isDefending)
        {
            ExecuteBlock(damage, knockbackForce);
            Debug.Log("Defesa feita");
            return;
        }

        if (behavior.IsDashInvincible) return;

        // Knockback sempre aplicado, independente de iFrames
        if (knockbackForce != Vector2.zero)
            behavior.ApplyKnockback(knockbackForce);

        if (isInvincible) return;

        Debug.Log("Vida= " + currentHealth + "/" + maxHealth);
        ApplyDamage(damage);
    }

    public void StartDefend()
    {
        if (isDead || isDefending) return;

        isDefending     = true;
        defendCoroutine = StartCoroutine(DefendTimeout());

        if (parryCooldownTimer <= 0f)
        {
            parryWindowOpen  = true;
            parryWindowTimer = parryWindowDuration;
        }
    }

    public void StopDefend()
    {
        if (!isDefending) return;
        isDefending = false;

        if (defendCoroutine != null)
        {
            StopCoroutine(defendCoroutine);
            defendCoroutine = null;
        }
    }

    private void BreakDefend()
    {
        if (!isDefending) return;
        StopDefend();
        OnBlockBroken?.Invoke();
    }

    private void ExecuteBlock(int damage, Vector2 knockbackForce)
    {
        float staminaCost   = damage * blockStaminaRatio * 10f;
        int   reducedDamage = Mathf.RoundToInt(damage * blockDamageRatio);

        bool staminaOk = stamina != null && stamina.Spend(staminaCost);

        if (reducedDamage > 0)
        {
            currentHealth = Mathf.Max(currentHealth - reducedDamage, 0);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (currentHealth <= 0) { Die(); return; }
        }

        if (knockbackForce != Vector2.zero)
            behavior.ApplyKnockback(knockbackForce * 0.4f);

        OnBlocked?.Invoke();
    }

    private void ExecuteParry()
    {
        parryWindowOpen    = false;
        parryCooldownTimer = parryCooldown;

        HitStop.Instance?.DoHitStop(parryHitStopDuration);
        OnParrySuccess?.Invoke();

        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, 3f);
        foreach (Collider2D col in nearby)
        {
            if (col.gameObject == gameObject) continue;
            Vector2 dir = (col.transform.position - transform.position).normalized;
            col.GetComponent<IDamageable>()?.ReceiveKnockback(dir * parryKnockbackForce);
            col.GetComponent<IStaggerable>()?.Stagger();
        }
    }

    // Knockback removido daqui — agora é aplicado antes da checagem de iFrames em TakeDamage
    private void ApplyDamage(int damage)
    {
        currentHealth = Mathf.Max(currentHealth - damage, 0);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke();

        if (currentHealth <= 0) { Die(); return; }

        StartCoroutine(IFrameRoutine());
    }

    private void Die()
    {
        isDead            = true;
        behavior.isLocked = true;
        Debug.Log("Player morreu");
        OnDeath?.Invoke();
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
        StopDefend();
    }
}

public interface IDamageable
{
    void TakeDamage(int damage);
    void ReceiveKnockback(Vector2 force);
}

public interface IStaggerable
{
    void Stagger();
}