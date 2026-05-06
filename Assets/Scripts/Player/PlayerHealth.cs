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
    [SerializeField] private float iframeDuration = 0.25f;

    [Header("Parry")]
    [SerializeField] private float parryWindowDuration  = 0.35f;
    [SerializeField] private float parryCooldown        = 1.2f;
    [SerializeField] private float parryHitStopDuration = 0.22f;

    [Header("Defesa")]
    [SerializeField] private float maxDefendDuration = 4f;
    [Range(0f, 1f)]
    [SerializeField] private float blockStaminaRatio = 0.8f;
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageRatio  = 0.2f;

    [Header("Knockback ao levar dano")]
    [SerializeField] private float knockbackForceX = 6f;
    [SerializeField] private float knockbackForceY = 2f;

    [Header("Hurt stun")]
    [Tooltip("Duração do lock de movimento ao levar dano.")]
    [SerializeField] private float hurtLockDuration = 0.3f;

    [Header("Debug Parry")]
    [SerializeField] private bool debugParry = true;

    private int   currentHealth;
    private bool  isDead;
    private bool  isInvincible;
    private bool  isDefending;
    private bool  parryWindowOpen;
    private float parryWindowTimer;
    private float parryCooldownTimer;

    private Transform lastAttackerTransform;

    private Coroutine defendCoroutine;
    private Coroutine hurtCoroutine;

    private PlayerBehavior               behavior;
    private PlayerStamina                stamina;
    private Rigidbody2D                  rb;
    private CharacterAnimationController animController;
    private HitFlash                     hitFlash;

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
        Instance       = this;
        behavior       = GetComponent<PlayerBehavior>();
        stamina        = GetComponent<PlayerStamina>();
        rb             = GetComponent<Rigidbody2D>();
        animController = GetComponent<CharacterAnimationController>();
        hitFlash       = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();
        currentHealth  = startingHealth;
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
            parryWindowTimer -= Time.deltaTime;
            if (parryWindowTimer <= 0f)
            {
                parryWindowOpen = false;
                if (debugParry) Debug.Log("[PARRY] Janela fechou sem hit.");
            }
        }
    }

    public void TakeDamage(int damage, Vector2 sourcePosition = default)
    {
        if (isDead) return;

        if (debugParry)
            Debug.Log($"[PARRY] TakeDamage | parryOpen={parryWindowOpen} | cooldown={parryCooldownTimer:F3} | defending={isDefending}");

        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            if (debugParry) Debug.Log("[PARRY] SUCCESS!");
            ExecuteParry();
            return;
        }

        if (isDefending)
        {
            if (debugParry) Debug.Log("[PARRY] Bloqueado.");
            ExecuteBlock(damage);
            return;
        }

        if (behavior.IsDashInvincible) return;
        if (isInvincible)              return;

        ApplyDamage(damage, sourcePosition);
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    public void StartDefend()
    {
        if (isDead || isDefending) return;

        isDefending = true;

        // Animator entra no estado de defesa imediatamente ao pressionar
        animController?.SetDefending(true);

        defendCoroutine = StartCoroutine(DefendTimeout());

        if (parryCooldownTimer <= 0f)
        {
            parryWindowOpen  = true;
            parryWindowTimer = parryWindowDuration;
            if (debugParry) Debug.Log($"[PARRY] Janela aberta | {parryWindowDuration}s");
        }
        else
        {
            if (debugParry) Debug.Log($"[PARRY] Cooldown ativo ({parryCooldownTimer:F2}s) — só block.");
        }
    }

    public void StopDefend()
    {
        if (!isDefending) return;

        isDefending = false;

        // Animator sai do estado de defesa ao soltar o botão
        animController?.SetDefending(false);

        if (defendCoroutine != null)
        {
            StopCoroutine(defendCoroutine);
            defendCoroutine = null;
        }
        // parryWindowOpen NÃO é fechado aqui intencionalmente:
        // um hit que chegar logo após soltar ainda conta como parry
        // se ainda estiver dentro da janela.
    }

    public void RegisterAttacker(Transform attacker)
    {
        lastAttackerTransform = attacker;
    }

    private void BreakDefend()
    {
        if (!isDefending) return;
        StopDefend();
        OnBlockBroken?.Invoke();
    }

    private void ExecuteBlock(int damage)
    {
        float staminaCost   = damage * blockStaminaRatio * 10f;
        int   reducedDamage = Mathf.RoundToInt(damage * blockDamageRatio);

        stamina?.Spend(staminaCost);

        if (reducedDamage > 0)
        {
            currentHealth = Mathf.Max(currentHealth - reducedDamage, 0);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            if (currentHealth <= 0) { Die(); return; }
        }

        OnBlocked?.Invoke();
    }

    private void ExecuteParry()
    {
        parryWindowOpen    = false;
        parryCooldownTimer = parryCooldown;

        // StopDefend já chama SetDefending(false) — o Animator sai de defending
        // após o parry ser confirmado, não antes. Isso garante que a transição
        // defending → parry acontece na ordem certa:
        //   IsDefending=true → hit detectado → parry confirmado → IsDefending=false
        if (isDefending) StopDefend();

        // Trigger de parry — crie um trigger "Parry" no Animator Controller
        // com transição de qualquer estado (ou do estado Defending) sem exit time.
        // O clip de parry deve ser curto (0.2–0.3s) e ter prioridade sobre idle/walk.
        animController?.SetTriggerDirect("Parry");

        hitFlash?.ParryFlash();

        HitStop.Instance?.DoHitStop(parryHitStopDuration);
        OnParrySuccess?.Invoke();

        if (lastAttackerTransform != null)
        {
            lastAttackerTransform.GetComponent<IStaggerable>()?.Stagger();
            lastAttackerTransform.GetComponentInParent<IStaggerable>()?.Stagger();
            Debug.Log($"[PlayerHealth] Parry — stagger em {lastAttackerTransform.name}.");
        }
    }

    private void ApplyDamage(int damage, Vector2 sourcePosition)
    {
        currentHealth = Mathf.Max(currentHealth - damage, 0);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke();

        if (currentHealth <= 0) { Die(); return; }

        animController?.SetTriggerDirect("Hit");
        hitFlash?.Flash();

        if (rb != null && sourcePosition != default)
        {
            Vector2 dir   = ((Vector2)transform.position - sourcePosition).normalized;
            Vector2 force = new Vector2(dir.x * knockbackForceX, knockbackForceY);
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(force, ForceMode2D.Impulse);
        }

        if (hurtCoroutine != null)
        {
            StopCoroutine(hurtCoroutine);
            behavior.isLocked = false;
        }

        hurtCoroutine = StartCoroutine(HurtRoutine());
        StartCoroutine(IFrameRoutine());
    }

    private IEnumerator HurtRoutine()
    {
        behavior.isLocked    = true;
        behavior.isAttacking = false;

        yield return new WaitForSecondsRealtime(hurtLockDuration);

        if (!isDead)
            behavior.isLocked = false;

        hurtCoroutine = null;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hurtCoroutine != null)
        {
            StopCoroutine(hurtCoroutine);
            hurtCoroutine = null;
        }

        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        behavior.isLocked = true;
        animController?.SetTriggerDirect("Die");

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
    void TakeDamage(int damage, Vector2 sourcePosition);
    void TakeDamage(int damage);
}

public interface IStaggerable
{
    void Stagger();
}