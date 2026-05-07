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
    [SerializeField] private float iframeDuration = 0.6f;   // era 0.25 — janela mais generosa após dano

    [Header("Parry")]
    [SerializeField] private float parryWindowDuration  = 0.20f;  // era 0.35 — janela precisa, exige leitura
    [SerializeField] private float parryCooldown        = 0.8f;   // era 1.2 — menos punitivo, ritmo mais fluido
    [SerializeField] private float parryHitStopDuration = 0.28f;  // era 0.22 — freeze mais dramático no parry

    [Header("Defesa")]
    [SerializeField] private float maxDefendDuration = 3f;    // era 4 — segura menos tempo, incentiva o parry
    [Range(0f, 1f)]
    [SerializeField] private float blockStaminaRatio = 0.9f;  // era 0.8 — bloquear é caro, parry é a resposta
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageRatio  = 0.2f;

    [Header("Guard Crush")]
    [Tooltip("Se a stamina estiver abaixo deste limiar, ataques pesados quebram a guarda")]
    [SerializeField] private float guardCrushStaminaThreshold = 35f;  // era 30 — ativa um pouco antes
    [Tooltip("Dano adicional de knockback X ao sofrer guard crush")]
    [SerializeField] private float guardCrushKnockbackBonus = 5f;     // era 4

    [Header("Counter Window (pós-parry)")]
    [Tooltip("Janela de tempo em segundos onde o próximo ataque é um counter")]
    [SerializeField] private float counterWindowDuration = 0.8f;      // era 0.6 — mais tempo para o player reagir
    [Tooltip("Multiplicador de dano durante a counter window")]
    [SerializeField] private float counterDamageMultiplier = 2.5f;    // era 2.2 — counter é devastador

    [Header("Knockback ao levar dano")]
    [SerializeField] private float knockbackForceX = 5f;   // era 6 — menos flutuação lateral
    [SerializeField] private float knockbackForceY = 3f;   // era 2 — mais quique vertical, lê-se melhor

    [Header("Hurt stun")]
    [Tooltip("Duração do lock de movimento ao levar dano.")]
    [SerializeField] private float hurtLockDuration = 0.22f;  // era 0.3 — stun menor, player recupera mais rápido

    [Header("Debug Parry")]
    [SerializeField] private bool debugParry = true;

    private int   currentHealth;
    private bool  isDead;
    private bool  isInvincible;
    private bool  isDefending;
    private bool  parryWindowOpen;
    private float parryWindowTimer;
    private float parryCooldownTimer;

    // Counter window — aberta após parry bem-sucedido
    private bool  _counterWindowOpen;
    private float _counterWindowTimer;

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
    public System.Action           OnGuardCrush;
    public System.Action           OnCounterAttack;
    public System.Action           OnDeath;

    public int   CurrentHealth           => currentHealth;
    public int   MaxHealth               => maxHealth;
    public float HealthNormalized        => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
    public bool  IsDefending             => isDefending;
    public bool  ParryWindowOpen         => parryWindowOpen;
    public bool  IsDead                  => isDead;
    public bool  IsCounterWindowOpen     => _counterWindowOpen;
    public float CounterDamageMultiplier => counterDamageMultiplier;

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

        // Counter window timer
        if (_counterWindowOpen)
        {
            _counterWindowTimer -= Time.deltaTime;
            if (_counterWindowTimer <= 0f)
            {
                _counterWindowOpen = false;
                if (debugParry) Debug.Log("[COUNTER] Janela de counter expirou.");
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
            ExecuteBlock(damage, sourcePosition);
            return;
        }

        if (behavior.IsDashInvincible) return;
        if (isInvincible)              return;

        ApplyDamage(damage, sourcePosition);
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    // Chamado por MeleeWeapon para sinalizar se o hit atual é pesado (pode causar guard crush)
    public void TakeDamageHeavy(int damage, Vector2 sourcePosition = default)
    {
        if (isDead) return;

        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            ExecuteParry();
            return;
        }

        if (isDefending)
        {
            // Guard crush: se stamina está abaixo do limiar, ignora o block
            bool guardCrush = stamina != null && stamina.CurrentStamina < guardCrushStaminaThreshold;
            if (guardCrush)
            {
                ExecuteGuardCrush(damage, sourcePosition);
                return;
            }
            ExecuteBlock(damage, sourcePosition);
            return;
        }

        if (behavior.IsDashInvincible) return;
        if (isInvincible)              return;

        ApplyDamage(damage, sourcePosition);
    }

    public void StartDefend()
    {
        if (isDead || isDefending) return;

        isDefending = true;

        // FIX DELAY — usa SetTriggerDirect para transição imediata (sem exit time),
        // e mantém SetDefending(true) para sustentar o estado Defending no Animator.
        // Antes apenas o bool era setado, causando 1 frame de delay na transição.
        animController?.SetTriggerDirect("DefendStart");
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

    // Consumido por MeleeWeapon.ApplyHit para confirmar o counter e fechar a janela
    public void ConsumeCounterWindow()
    {
        _counterWindowOpen  = false;
        _counterWindowTimer = 0f;
        OnCounterAttack?.Invoke();
        if (debugParry) Debug.Log("[COUNTER] Counter consumido!");
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

    private void ExecuteBlock(int damage, Vector2 sourcePosition = default)
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

    private void ExecuteGuardCrush(int damage, Vector2 sourcePosition)
    {
        if (debugParry) Debug.Log("[GUARD CRUSH] Guarda quebrada por ataque pesado com stamina baixa!");

        StopDefend();
        animController?.SetTriggerDirect("GuardCrush");
        hitFlash?.Flash();
        OnGuardCrush?.Invoke();

        // Aplica dano total com knockback adicional
        currentHealth = Mathf.Max(currentHealth - damage, 0);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke();

        if (currentHealth <= 0) { Die(); return; }

        if (rb != null && sourcePosition != default)
        {
            Vector2 dir   = ((Vector2)transform.position - sourcePosition).normalized;
            Vector2 force = new Vector2(
                dir.x * (knockbackForceX + guardCrushKnockbackBonus),
                knockbackForceY * 1.4f
            );
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

    private void ExecuteParry()
    {
        parryWindowOpen    = false;
        parryCooldownTimer = parryCooldown;

        if (isDefending) StopDefend();

        animController?.SetTriggerDirect("Parry");
        hitFlash?.ParryFlash();
        HitStop.Instance?.DoHitStop(parryHitStopDuration);

        // Abre counter window
        _counterWindowOpen  = true;
        _counterWindowTimer = counterWindowDuration;
        if (debugParry) Debug.Log($"[COUNTER] Janela de counter aberta | {counterWindowDuration}s");

        // Zoom de câmera no parry
        CameraImpulse.Instance?.ParryZoom();

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