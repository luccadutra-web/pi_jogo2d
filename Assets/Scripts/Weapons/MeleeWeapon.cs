using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange    = 1.5f;
    [SerializeField] private int   lightDamage         = 1;
    [SerializeField] private float lightHitStop        = 0.04f;
    [SerializeField] private float lightShakeIntensity = 0.08f;
    [SerializeField] private float lightShakeDuration  = 0.10f;

    [Header("Heavy independente")]
    [SerializeField] private float heavyAttackRange    = 2.0f;
    [SerializeField] private int   heavyDamage         = 3;
    [SerializeField] private float heavyHitStop        = 0.14f;
    [SerializeField] private float heavyStaminaCost    = 25f;
    [SerializeField] private float heavyCooldown       = 1.0f;
    [SerializeField] private float heavyShakeIntensity = 0.15f;
    [SerializeField] private float heavyShakeDuration  = 0.18f;

    [Header("Finalizador (combo x3 + heavy)")]
    [SerializeField] private float finisherAttackRange    = 2.5f;
    [SerializeField] private int   finisherDamage         = 6;
    [SerializeField] private float finisherHitStop        = 0.28f;
    [SerializeField] private float finisherStaminaCost    = 30f;
    [SerializeField] private float finisherShakeIntensity = 0.22f;
    [SerializeField] private float finisherShakeDuration  = 0.22f;

    [Header("Janela de combo")]
    [SerializeField] private float comboWindowDuration    = 0.45f;
    [SerializeField] private float finisherWindowDuration = 0.55f;

    [Header("Input buffer")]
    [SerializeField] private float inputBufferWindow = 0.10f;

    [Header("Timings — startup + active travam / recovery libera")]
    [SerializeField] private float lightStartup       = 0.08f;
    [SerializeField] private float lightActiveTime    = 0.08f;
    [SerializeField] private float lightRecovery      = 0.18f;
    [SerializeField] private float heavyStartup       = 0.20f;
    [SerializeField] private float heavyActiveTime    = 0.12f;
    [SerializeField] private float heavyRecovery      = 0.40f;
    [SerializeField] private float finisherStartup    = 0.28f;
    [SerializeField] private float finisherActiveTime = 0.15f;
    [SerializeField] private float finisherRecovery   = 0.55f;

    [Header("Hitlag direcional")]
    [Tooltip("Duração em segundos que o inimigo fica congelado no momento do hit")]
    [SerializeField] private float hitlagDuration = 0.05f;
    [Tooltip("Fator de retenção de velocidade após o hitlag (0 = para completamente)")]
    [Range(0f, 1f)]
    [SerializeField] private float hitlagVelocityRetention = 0.15f;

    [Header("Counter attack")]
    [Tooltip("Multiplicador de shake e hitstop quando o hit é um counter (pós-parry)")]
    [SerializeField] private float counterImpactMultiplier = 2f;

    [Header("Dano de postura (Poise)")]
    [Tooltip("Quanto dano de postura o light attack causa ao inimigo. Inimigos com EnemyPoise entram em stagger quando postura zera.")]
    [SerializeField] private float lightPoiseDamage    = 20f;
    [Tooltip("Quanto dano de postura o heavy attack causa.")]
    [SerializeField] private float heavyPoiseDamage    = 45f;
    [Tooltip("Quanto dano de postura o finisher causa. 999 = quebra a postura sempre.")]
    [SerializeField] private float finisherPoiseDamage = 999f;

    private enum AttackType { Light, Heavy, Finisher }

    private int   comboStep          = 0;
    private bool  isAttacking        = false;
    private bool  comboWindowOpen    = false;
    private bool  finisherWindowOpen = false;
    private float comboWindowTimer;
    private float finisherWindowTimer;
    private float heavyCooldownTimer;

    private bool  lightBuffered;
    private bool  heavyBuffered;
    private float lightBufferTimer;
    private float heavyBufferTimer;
    private bool  _justStartedAttack;

    private const string TriggerLight    = "LightAttack";
    private const string TriggerHeavy    = "HeavyAttack";
    private const string TriggerFinisher = "Finisher";
    private const string TriggerCounter  = "CounterAttack";

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerBehavior               behavior;
    private PlayerStamina                stamina;
    private PlayerHealth                 health;
    private CharacterAnimationController animController;

    void Awake()
    {
        lightAction = new InputAction("Light", InputActionType.Button);
        lightAction.AddBinding("<Mouse>/leftButton");

        heavyAction = new InputAction("Heavy", InputActionType.Button);
        heavyAction.AddBinding("<Mouse>/rightButton");
    }

    void Start()
    {
        behavior       = GetComponentInParent<PlayerBehavior>();
        stamina        = GetComponentInParent<PlayerStamina>();
        health         = GetComponentInParent<PlayerHealth>();
        animController = GetComponentInParent<CharacterAnimationController>();

        if (animController == null)
            Debug.LogError("[MeleeWeapon] CharacterAnimationController não encontrado no pai.");
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }
    void OnDisable()
    {
        lightAction.Disable();
        heavyAction.Disable();

        // Garante limpeza das flags se o objeto for desativado no meio de um ataque.
        if (behavior != null)
        {
            behavior.isAttacking               = false;
            behavior.isInAttackStartupOrActive = false;
        }
    }

    void Update()
    {
        if (behavior != null && behavior.IsDashing) return;

        _justStartedAttack = false;

        ReadInput();
        UpdateTimers();

        if (!isAttacking && !_justStartedAttack)
            TryConsumeBuffer();
    }

    private void ReadInput()
    {
        if (lightAction.WasPressedThisFrame())
        {
            lightBuffered    = true;
            lightBufferTimer = inputBufferWindow;
        }

        if (heavyAction.WasPressedThisFrame())
        {
            heavyBuffered    = true;
            heavyBufferTimer = inputBufferWindow;
        }
    }

    private void UpdateTimers()
    {
        if (lightBufferTimer > 0f) { lightBufferTimer -= Time.deltaTime; if (lightBufferTimer <= 0f) lightBuffered = false; }
        if (heavyBufferTimer > 0f) { heavyBufferTimer -= Time.deltaTime; if (heavyBufferTimer <= 0f) heavyBuffered = false; }

        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f && !isAttacking) { comboWindowOpen = false; comboStep = 0; }
        }

        if (finisherWindowTimer > 0f)
        {
            finisherWindowTimer -= Time.deltaTime;
            if (finisherWindowTimer <= 0f && !isAttacking) { finisherWindowOpen = false; comboStep = 0; }
        }

        if (heavyCooldownTimer > 0f) heavyCooldownTimer -= Time.deltaTime;
    }

    private void TryConsumeBuffer()
    {
        if (heavyBuffered && finisherWindowOpen)
        {
            heavyBuffered = false; heavyBufferTimer = 0f;
            finisherWindowOpen = false; comboWindowOpen = false;
            ExecuteFinisher();
            return;
        }

        if (lightBuffered)
        {
            if (comboWindowOpen)
            {
                lightBuffered = false; lightBufferTimer = 0f;
                ExecuteLight();
                return;
            }
            if (!comboWindowOpen && !finisherWindowOpen)
            {
                lightBuffered = false; lightBufferTimer = 0f;
                comboStep = 0;
                ExecuteLight();
                return;
            }
        }

        if (heavyBuffered && !finisherWindowOpen && heavyCooldownTimer <= 0f)
        {
            heavyBuffered = false; heavyBufferTimer = 0f;
            ExecuteHeavy();
        }
    }

    private void ExecuteLight()
    {
        comboStep++;
        comboWindowOpen = false; finisherWindowOpen = false;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Light));
    }

    private void ExecuteHeavy()
    {
        if (stamina != null && !stamina.Spend(heavyStaminaCost)) { heavyBuffered = false; return; }
        heavyCooldownTimer = heavyCooldown;
        comboStep = 0; comboWindowOpen = false; finisherWindowOpen = false;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Heavy));
    }

    private void ExecuteFinisher()
    {
        if (stamina != null && !stamina.Spend(finisherStaminaCost)) { comboStep = 0; return; }
        comboStep = 0;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Finisher));
    }

    private IEnumerator AttackRoutine(AttackType type)
    {
        isAttacking = true;

        float startup    = type == AttackType.Light ? lightStartup    : type == AttackType.Heavy ? heavyStartup    : finisherStartup;
        float activeTime = type == AttackType.Light ? lightActiveTime : type == AttackType.Heavy ? heavyActiveTime : finisherActiveTime;
        float recovery   = type == AttackType.Light ? lightRecovery   : type == AttackType.Heavy ? heavyRecovery   : finisherRecovery;
        string trigger   = type == AttackType.Light ? TriggerLight    : type == AttackType.Heavy ? TriggerHeavy    : TriggerFinisher;

        // Verifica counter ANTES de setar o trigger para usar a animação correta
        bool isCounter = health != null && health.IsCounterWindowOpen;
        if (isCounter)
        {
            animController?.SetTriggerDirect(TriggerCounter);
            // Zoom sincronizado com o início da animação de counter —
            // não no ApplyHit (que depende de acertar o inimigo e roda no active).
            CameraImpulse.Instance?.CounterZoom();
        }
        else
            animController?.SetTriggerDirect(trigger);

        if (behavior != null)
        {
            behavior.isAttacking              = true;
            behavior.isInAttackStartupOrActive = true;  // trava movimento
        }

        yield return new WaitForSecondsRealtime(startup);

        ApplyHit(type, isCounter);

        // Consome a counter window após o hit ser aplicado
        if (isCounter && health != null)
            health.ConsumeCounterWindow();

        yield return new WaitForSecondsRealtime(activeTime);

        // Aguarda o HitStop terminar antes de liberar o movimento.
        // WaitForSecondsRealtime avança com timeScale=0, mas o Animator congela —
        // soltar a trava enquanto o Animator ainda está no frame congelado causava
        // o "travamento no último frame" visível especialmente no heavy attack.
        if (HitStop.Instance != null)
            while (HitStop.Instance.IsActive)
                yield return null;

        // Libera só a trava de movimento — isAttacking permanece true
        // até o fim do recovery para bloquear TryConsumeBuffer no Update,
        // impedindo que spam dispare um novo trigger antes do clip terminar.
        if (behavior != null)
        {
            behavior.isInAttackStartupOrActive = false;
            behavior.isAttacking              = false;
        }

        if (type == AttackType.Light)
        {
            if (comboStep < 3) { comboWindowOpen  = true; comboWindowTimer  = comboWindowDuration; }
            else               { finisherWindowOpen = true; finisherWindowTimer = finisherWindowDuration; }
        }

        // Recovery completo antes de liberar o buffer.
        // isAttacking (local) fica true durante todo o recovery — Update não
        // chama TryConsumeBuffer enquanto isso, então o próximo ataque só
        // começa (e dispara o trigger) quando o clip atual já terminou.
        float recoveryElapsed = 0f;
        while (recoveryElapsed < recovery)
        {
            if (behavior != null && behavior.IsDashing) break;
            recoveryElapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        isAttacking = false;
        TryConsumeBuffer();

        // Se nenhum novo ataque foi iniciado pelo buffer, força saída do estado
        // de ataque no Animator. "Idle" deve ser o estado raiz da blend tree
        // (Speed=0→Idle, Speed>0→Walk/Run) para que a transição idle→walk seja
        // natural sem delay de interpolação.
        if (!isAttacking)
        {
            comboStep = 0;
            animController?.ForceState("Idle");
        }
    }

    private void ApplyHit(AttackType type, bool isCounter)
    {
        float range   = type == AttackType.Light ? lightAttackRange    : type == AttackType.Heavy ? heavyAttackRange    : finisherAttackRange;
        int   baseDmg = type == AttackType.Light ? lightDamage         : type == AttackType.Heavy ? heavyDamage         : finisherDamage;
        float hitStop = type == AttackType.Light ? lightHitStop        : type == AttackType.Heavy ? heavyHitStop        : finisherHitStop;
        float shakeI  = type == AttackType.Light ? lightShakeIntensity : type == AttackType.Heavy ? heavyShakeIntensity : finisherShakeIntensity;
        float shakeD  = type == AttackType.Light ? lightShakeDuration  : type == AttackType.Heavy ? heavyShakeDuration  : finisherShakeDuration;

        // Aplica multiplicadores de counter
        int   damage     = isCounter
            ? Mathf.RoundToInt(baseDmg * (health != null ? health.CounterDamageMultiplier : 2f))
            : baseDmg;
        float finalStop  = isCounter ? hitStop  * counterImpactMultiplier : hitStop;
        float finalShakeI = isCounter ? shakeI  * counterImpactMultiplier : shakeI;

        Collider2D[] hits = Physics2D.OverlapCircleAll(attackPoint.position, range, enemyLayer);
        if (hits.Length == 0) return;

        HitStop.Instance?.DoHitStop(finalStop);
        CameraShake.Instance?.Shake(finalShakeI, shakeD);

        foreach (Collider2D hit in hits)
        {
            // Hitlag direcional — congela o inimigo no momento exato do impacto
            Rigidbody2D enemyRb = hit.GetComponent<Rigidbody2D>()
                                ?? hit.GetComponentInParent<Rigidbody2D>();
            if (enemyRb != null)
                StartCoroutine(HitlagRoutine(enemyRb));

            // Ataques pesados chamam TakeDamageHeavy para habilitar guard crush
            if (type == AttackType.Heavy)
            {
                var damageable = hit.GetComponent<IDamageable>()
                              ?? hit.GetComponentInParent<IDamageable>();
                var playerDmg = hit.GetComponent<PlayerHealth>()
                             ?? hit.GetComponentInParent<PlayerHealth>();

                if (playerDmg != null)
                    playerDmg.TakeDamageHeavy(damage, transform.position);
                else
                    damageable?.TakeDamage(damage, transform.position);
            }
            else
            {
                var damageable = hit.GetComponent<IDamageable>()
                              ?? hit.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, transform.position);
            }

            // Dano de postura — independente do dano de vida.
            // O poise damage é determinado aqui (no atacante) e não no inimigo,
            // para que cada arma possa ter valores diferentes sem alterar EnemyPoise.
            float poiseDmg = type == AttackType.Light    ? lightPoiseDamage
                           : type == AttackType.Heavy    ? heavyPoiseDamage
                           : /* Finisher */                finisherPoiseDamage;

            var enemyPoise = hit.GetComponent<EnemyPoise>()
                          ?? hit.GetComponentInParent<EnemyPoise>();
            enemyPoise?.ReceivePoiseHit(poiseDmg);

            var hitFlash = hit.GetComponent<HitFlash>()
                        ?? hit.GetComponentInParent<HitFlash>();
            hitFlash?.ImpactFlash();
        }
    }

    // Hitlag direcional — congela o inimigo por alguns frames no momento do hit,
    // criando a sensação de peso e impacto físico inspirada em Blasphemous.
    private IEnumerator HitlagRoutine(Rigidbody2D enemyRb)
    {
        if (enemyRb == null) yield break;

        float   savedGravity = enemyRb.gravityScale;
        Vector2 savedVel     = enemyRb.linearVelocity;

        enemyRb.gravityScale   = 0f;
        enemyRb.linearVelocity = Vector2.zero;

        yield return new WaitForSecondsRealtime(hitlagDuration);

        if (enemyRb == null) yield break;

        enemyRb.gravityScale   = savedGravity;
        enemyRb.linearVelocity = savedVel * hitlagVelocityRetention;
    }

    void OnDrawGizmosSelected()
    {
        if (!attackPoint) return;
        Gizmos.color = new Color(1f, 0.8f, 0.8f, 0.4f);
        Gizmos.DrawWireSphere(attackPoint.position, lightAttackRange);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(attackPoint.position, heavyAttackRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.7f);
        Gizmos.DrawWireSphere(attackPoint.position, finisherAttackRange);
    }
}