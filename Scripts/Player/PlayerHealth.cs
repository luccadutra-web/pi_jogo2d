using System.Collections;
using UnityEngine;

/// <summary>
/// PlayerHealth — v2: janela de parry e hitbox de dano sincronizadas via Animation Events.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///  PARRY (defesa):
///   O código NÃO abre mais a janela de parry automaticamente ao pressionar E.
///   Em vez disso, a animação de defesa tem dois Animation Events:
///
///     Frame onde o player entra em posição  → chama  OnParryWindowOpen()
///     Frame onde a pose de parry termina    → chama  OnParryWindowClose()
///
///   Assim a janela é 100% sincronizada com o visual.
///
///  HIT (receber dano):
///   A hitbox de dano dos inimigos já é controlada pelo MeleeWeapon deles,
///   então não há mudança aqui — mas se quiser adicionar i-frames visuais
///   também basta chamar EnableHitWindow() / DisableHitWindow() via evento.
///
/// ── SETUP NO ANIMATOR ────────────────────────────────────────────────────────
///
///   State "DefendStart" (ou o estado de entrada da guarda):
///     - No frame em que o escudo/braço chega na posição: Animation Event → OnParryWindowOpen
///     - No último frame do startup / início do loop idle de guarda: Animation Event → OnParryWindowClose
///       (se quiser parry só no startup; remova esta chamada para parry durante todo o hold)
///
///   State "Parry" (animação de sucesso do parry):
///     - Nenhum evento necessário — disparado pelo código ao receber hit com janela aberta.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(PlayerBehavior))]
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }

    [Header("Vida")]
    [SerializeField] private int maxHealth      = 5;
    [SerializeField] private int startingHealth = 5;

    [Header("iFrames")]
    [SerializeField] private float iframeDuration = 0.6f;

    [Header("Parry")]
    [Tooltip("Se true, a janela de parry é controlada pelos Animation Events (recomendado).\n" +
             "Se false, usa o timer legado parryWindowDuration ao pressionar E.")]
    [SerializeField] private bool  parryDrivenByAnimation = true;
    [Tooltip("Usado apenas se parryDrivenByAnimation = false (modo legado).")]
    [SerializeField] private float parryWindowDuration    = 0.20f;
    [SerializeField] private float parryCooldown          = 0.8f;
    [Tooltip("Escala de tempo durante o slow motion do parry (0.05 = quase parado, 0.15 = levemente lento).\n" +
             "Valores muito baixos são mais dramáticos mas podem parecer exagerados — comece em 0.05.")]
    [SerializeField] private float parrySlowScale         = 0.05f;
    [Tooltip("Quanto tempo o slow motion fica no pico antes de voltar ao normal (segundos reais).")]
    [SerializeField] private float parrySlowHold          = 0.10f;
    [Tooltip("Quanto tempo leva para o timeScale voltar de slowScale para 1 (segundos reais).\n" +
             "O ramp up suave é o que dá a sensação de 'respirar' após o parry.")]
    [SerializeField] private float parrySlowRampUp        = 0.20f;

    [Header("Defesa")]
    [SerializeField] private float maxDefendDuration = 3f;
    [Range(0f, 1f)]
    [SerializeField] private float blockStaminaRatio = 0.9f;
    [Range(0f, 1f)]
    [SerializeField] private float blockDamageRatio  = 0.2f;

    [Header("Guard Crush")]
    [SerializeField] private float guardCrushStaminaThreshold = 35f;
    [SerializeField] private float guardCrushKnockbackBonus   = 5f;

    [Header("Counter Window (pós-parry)")]
    [SerializeField] private float counterWindowDuration    = 0.8f;
    [SerializeField] private float counterDamageMultiplier  = 2.5f;

    [Header("Knockback")]
    [SerializeField] private float knockbackForceX = 5f;
    [SerializeField] private float knockbackForceY = 3f;

    [Header("Hurt stun")]
    [SerializeField] private float hurtLockDuration = 0.22f;

    [Header("Debug")]
    [SerializeField] private bool debugParry = true;

    // ─── Estado ───────────────────────────────────────────────────────────────

    private int   currentHealth;
    private bool  isDead;
    private bool  isInvincible;
    private bool  isDefending;
    private bool  parryWindowOpen;
    private float parryWindowTimer;     // usado apenas no modo legado
    private float parryCooldownTimer;

    private bool  _counterWindowOpen;
    private float _counterWindowTimer;

    private Transform lastAttackerTransform;
    private Coroutine defendCoroutine;
    private Coroutine hurtCoroutine;

    // ─── Componentes ──────────────────────────────────────────────────────────

    private PlayerBehavior               behavior;
    private PlayerStamina                stamina;
    private Rigidbody2D                  rb;
    private CharacterAnimationController animController;
    private HitFlash                     hitFlash;

    // ─── Eventos ──────────────────────────────────────────────────────────────

    public System.Action<int, int> OnHealthChanged;
    public System.Action           OnDamaged;
    public System.Action           OnBlocked;
    public System.Action           OnBlockBroken;
    public System.Action           OnParrySuccess;
    public System.Action           OnGuardCrush;
    public System.Action           OnCounterAttack;
    public System.Action           OnDeath;

    // ─── Propriedades públicas ────────────────────────────────────────────────

    public int   CurrentHealth           => currentHealth;
    public int   MaxHealth               => maxHealth;
    public float HealthNormalized        => maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
    public bool  IsDefending             => isDefending;
    public bool  ParryWindowOpen         => parryWindowOpen;
    public bool  IsDead                  => isDead;
    public bool  IsCounterWindowOpen     => _counterWindowOpen;
    public float CounterDamageMultiplier => counterDamageMultiplier;

    // ─── Unity ────────────────────────────────────────────────────────────────

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
        if (stamina != null) stamina.OnStaminaEmpty += BreakDefend;
    }

    void OnDestroy()
    {
        if (stamina != null) stamina.OnStaminaEmpty -= BreakDefend;
    }

    void Update()
    {
        if (parryCooldownTimer > 0f) parryCooldownTimer -= Time.deltaTime;

        // Modo legado: fecha a janela pelo timer
        if (!parryDrivenByAnimation && parryWindowOpen)
        {
            parryWindowTimer -= Time.deltaTime;
            if (parryWindowTimer <= 0f)
            {
                parryWindowOpen = false;
                if (debugParry) Debug.Log("[PARRY] Janela fechou por timer (modo legado).");
            }
        }

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

    // ─── Animation Events ─────────────────────────────────────────────────────

    /// <summary>
    /// Chame via Animation Event no frame em que o player entra em posição de parry.
    /// </summary>
    public void OnParryWindowOpen()
    {
        if (!isDefending) return;           // segurança: só conta se ainda está defendendo
        if (parryCooldownTimer > 0f)
        {
            if (debugParry) Debug.Log("[PARRY] Animation Event: cooldown ativo — janela bloqueada.");
            return;
        }

        parryWindowOpen  = true;
        parryWindowTimer = parryWindowDuration; // referência para o modo legado; ignorado no modo anim
        if (debugParry) Debug.Log("[PARRY] Animation Event: janela ABERTA.");
    }

    /// <summary>
    /// Chame via Animation Event quando o startup de parry termina.
    /// Se quiser parry apenas no startup, use este evento.
    /// Se quiser parry durante todo o hold, não adicione este evento.
    /// </summary>
    public void OnParryWindowClose()
    {
        if (!parryWindowOpen) return;
        parryWindowOpen = false;
        if (debugParry) Debug.Log("[PARRY] Animation Event: janela FECHADA.");
    }

    // ─── Defesa (input) ───────────────────────────────────────────────────────

    public void StartDefend()
    {
        if (isDead || isDefending) return;

        isDefending = true;
        animController?.SetTriggerDirect("DefendStart");
        animController?.SetDefending(true);

        defendCoroutine = StartCoroutine(DefendTimeout());

        // Modo legado: abre janela imediatamente pelo timer
        if (!parryDrivenByAnimation && parryCooldownTimer <= 0f)
        {
            parryWindowOpen  = true;
            parryWindowTimer = parryWindowDuration;
            if (debugParry) Debug.Log($"[PARRY] Janela aberta por timer legado | {parryWindowDuration}s");
        }
        else if (parryDrivenByAnimation)
        {
            // A janela será aberta pelo Animation Event OnParryWindowOpen()
            if (debugParry) Debug.Log("[PARRY] DefendStart — aguardando Animation Event para abrir janela.");
        }
    }

    public void StopDefend()
    {
        if (!isDefending) return;

        isDefending     = false;
        parryWindowOpen = false; // fecha a janela ao soltar (evita parry fantasma)

        animController?.SetDefending(false);

        if (defendCoroutine != null)
        {
            StopCoroutine(defendCoroutine);
            defendCoroutine = null;
        }

        if (debugParry) Debug.Log("[PARRY] Defesa encerrada.");
    }

    // ─── Dano ─────────────────────────────────────────────────────────────────

    /// <returns>true se o dano foi absorvido (parry, block ou iFrame) — false se o dano passou.</returns>
    public bool TakeDamage(int damage, Vector2 sourcePosition = default)
    {
        if (isDead) return true;

        if (debugParry)
            Debug.Log($"[PARRY] TakeDamage | parryOpen={parryWindowOpen} | cooldown={parryCooldownTimer:F3} | defending={isDefending}");

        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            if (debugParry) Debug.Log("[PARRY] SUCCESS!");
            ExecuteParry();
            return true;   // absorvido por parry
        }

        if (isDefending)
        {
            if (debugParry) Debug.Log("[PARRY] Bloqueado.");
            ExecuteBlock(damage, sourcePosition);
            return true;   // absorvido pelo block
        }

        if (behavior.IsDashInvincible) return true;
        if (isInvincible)              return true;

        ApplyDamage(damage, sourcePosition);
        return false;      // dano passou
    }

    public bool TakeDamage(int damage) => TakeDamage(damage, default);

    /// <returns>true se o dano foi absorvido — false se o dano passou.</returns>
    public bool TakeDamageHeavy(int damage, Vector2 sourcePosition = default)
    {
        if (isDead) return true;

        if (parryWindowOpen && parryCooldownTimer <= 0f)
        {
            ExecuteParry();
            return true;
        }

        if (isDefending)
        {
            bool guardCrush = stamina != null && stamina.CurrentStamina < guardCrushStaminaThreshold;
            if (guardCrush) { ExecuteGuardCrush(damage, sourcePosition); return false; }
            ExecuteBlock(damage, sourcePosition);
            return true;
        }

        if (behavior.IsDashInvincible) return true;
        if (isInvincible)              return true;

        ApplyDamage(damage, sourcePosition);
        return false;
    }

    // ─── Counter ──────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Cancela o HurtRoutine em andamento e libera o isLocked que ele controlava.
    /// Chamado pelo PlayerPoise antes de assumir o controle do stagger,
    /// evitando que dois locks colidam e o player fique preso para sempre.
    /// </summary>
    public void CancelHurtRoutine()
    {
        if (hurtCoroutine != null)
        {
            StopCoroutine(hurtCoroutine);
            hurtCoroutine        = null;
            behavior.isLocked    = false;   // limpa o lock do hurt antes do poise assumir
            behavior.isAttacking = false;
        }
    }

    // ─── Internos ─────────────────────────────────────────────────────────────

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
        if (debugParry) Debug.Log("[GUARD CRUSH] Guarda quebrada!");

        StopDefend();
        GetComponent<MeleeWeapon>()?.CancelAttack();
        animController?.SetTriggerDirect("GuardCrush");
        hitFlash?.Flash();
        OnGuardCrush?.Invoke();

        currentHealth = Mathf.Max(currentHealth - damage, 0);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke();

        if (currentHealth <= 0) { Die(); return; }

        if (rb != null && sourcePosition != default)
        {
            Vector2 dir   = ((Vector2)transform.position - sourcePosition).normalized;
            Vector2 force = new Vector2(dir.x * (knockbackForceX + guardCrushKnockbackBonus), knockbackForceY * 1.4f);
            rb.linearVelocity = Vector2.zero;
            rb.AddForce(force, ForceMode2D.Impulse);
        }

        if (hurtCoroutine != null) { StopCoroutine(hurtCoroutine); behavior.isLocked = false; }
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

        // VFX spawnam ANTES do slow motion — evita que o timeScale alterado
        // atrase o início dos Particle Systems
        float facing = transform.localScale.x >= 0 ? 1f : -1f;
        VfxManager.Instance?.SpawnParry(transform.position, facing);
        VfxManager.Instance?.SpawnDustParry(transform.position, facing);

        // Slow motion em vez de freeze total: o jogador vê o inimigo recuando
        // em câmera lenta antes do controle ser devolvido — o momento mais
        // satisfatório do combate. DoSlowMotion já cuida do ramp up suave de
        // volta a timeScale=1, então não é necessário nenhum cleanup manual.
        HitStop.Instance?.DoSlowMotion(parrySlowScale, parrySlowHold, parrySlowRampUp);

        _counterWindowOpen  = true;
        _counterWindowTimer = counterWindowDuration;
        if (debugParry) Debug.Log($"[COUNTER] Janela de counter aberta | {counterWindowDuration}s");

        AudioManager.Instance?.PlaySFX("parry_success", 1f);
        CameraImpulse.Instance?.ParryZoom();
        OnParrySuccess?.Invoke();

        // Spawna indicador de janela de counter
        VfxManager.Instance?.SpawnCounterWindow(transform);

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

        if (hurtCoroutine != null) { StopCoroutine(hurtCoroutine); behavior.isLocked = false; }
        hurtCoroutine = StartCoroutine(HurtRoutine());
        StartCoroutine(IFrameRoutine());
    }

    private IEnumerator HurtRoutine()
    {
        GetComponent<MeleeWeapon>()?.CancelAttack();

        behavior.isLocked    = true;
        behavior.isAttacking = false;
        yield return new WaitForSecondsRealtime(hurtLockDuration);
        if (!isDead) behavior.isLocked = false;
        hurtCoroutine = null;
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hurtCoroutine != null) { StopCoroutine(hurtCoroutine); hurtCoroutine = null; }

        GetComponent<MeleeWeapon>()?.CancelAttack();

        // Congela o Rigidbody completamente durante a animação de morte.
        // Sem isso o player cai pelo chão enquanto a animação de Die não existe.
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.gravityScale   = 0f;
            rb.constraints    = RigidbodyConstraints2D.FreezeAll;
        }

        behavior.isLocked = true;
        animController?.SetTriggerDirect("Die");
        GetComponent<PlayerSpecial>()?.CancelSpecial();

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

    public void EnableSpecialInvincible(float duration)
        => StartCoroutine(SpecialInvincibleRoutine(duration));

    private IEnumerator SpecialInvincibleRoutine(float duration)
    {
        isInvincible = true;
        yield return new WaitForSeconds(duration);
        isInvincible = false;
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