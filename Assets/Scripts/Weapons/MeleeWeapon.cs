using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private Animator  animator;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange    = 1.5f;
    [SerializeField] private int   lightDamage         = 1;
    [SerializeField] private float lightHitStop        = 0.06f;
    [SerializeField] private float lightShakeIntensity = 0.08f;
    [SerializeField] private float lightShakeDuration  = 0.10f;

    [Header("Heavy independente")]
    [SerializeField] private float heavyAttackRange    = 2.0f;
    [SerializeField] private int   heavyDamage         = 3;
    [SerializeField] private float heavyHitStop        = 0.12f;
    [SerializeField] private float heavyStaminaCost    = 25f;
    [SerializeField] private float heavyCooldown       = 1.0f;
    [SerializeField] private float heavyShakeIntensity = 0.15f;
    [SerializeField] private float heavyShakeDuration  = 0.18f;

    [Header("Finalizador (combo x3 + heavy)")]
    [SerializeField] private float finisherAttackRange    = 2.5f;
    [SerializeField] private int   finisherDamage         = 6;
    [SerializeField] private float finisherHitStop        = 0.20f;
    [SerializeField] private float finisherStaminaCost    = 30f;
    [SerializeField] private float finisherShakeIntensity = 0.22f;
    [SerializeField] private float finisherShakeDuration  = 0.22f;

    [Header("Janela de combo")]
    [SerializeField] private float comboWindowDuration    = 0.45f;
    [SerializeField] private float finisherWindowDuration = 0.55f;

    [Header("Input buffer")]
    [SerializeField] private float inputBufferWindow = 0.16f;

    [Header("Timings — startup + active travam / recovery libera")]
    [SerializeField] private float lightStartup      = 0.08f;
    [SerializeField] private float lightActiveTime   = 0.08f;
    [SerializeField] private float lightRecovery     = 0.18f;
    [SerializeField] private float heavyStartup      = 0.20f;
    [SerializeField] private float heavyActiveTime   = 0.12f;
    [SerializeField] private float heavyRecovery     = 0.40f;
    [SerializeField] private float finisherStartup   = 0.28f;
    [SerializeField] private float finisherActiveTime = 0.15f;
    [SerializeField] private float finisherRecovery  = 0.55f;

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

    private static readonly int AnimLight    = Animator.StringToHash("LightAttack");
    private static readonly int AnimHeavy    = Animator.StringToHash("HeavyAttack");
    private static readonly int AnimFinisher = Animator.StringToHash("Finisher");

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerBehavior behavior;
    private PlayerStamina  stamina;

    void Awake()
    {
        lightAction = new InputAction("Light", InputActionType.Button);
        lightAction.AddBinding("<Mouse>/leftButton");

        heavyAction = new InputAction("Heavy", InputActionType.Button);
        heavyAction.AddBinding("<Mouse>/rightButton");
    }

    void Start()
    {
        behavior = GetComponentInParent<PlayerBehavior>();
        stamina  = GetComponentInParent<PlayerStamina>();
        if (animator == null) animator = GetComponentInParent<Animator>();
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }
    void OnDisable() { lightAction.Disable(); heavyAction.Disable(); }

    void Update()
    {
        if (behavior != null && behavior.IsDashing) return;

        ReadInput();
        UpdateTimers();

        if (!isAttacking)
            TryConsumeBuffer();
    }

    private void ReadInput()
    {
        if (lightAction.WasPressedThisFrame())
        {
            lightBuffered    = true;
            lightBufferTimer = inputBufferWindow;
            Debug.Log("[Melee] Leve bufferizado.");
        }

        if (heavyAction.WasPressedThisFrame())
        {
            heavyBuffered    = true;
            heavyBufferTimer = inputBufferWindow;
            Debug.Log("[Melee] Heavy bufferizado.");
        }
    }

    private void UpdateTimers()
    {
        if (lightBufferTimer > 0f) { lightBufferTimer -= Time.deltaTime; if (lightBufferTimer <= 0f) lightBuffered = false; }
        if (heavyBufferTimer > 0f) { heavyBufferTimer -= Time.deltaTime; if (heavyBufferTimer <= 0f) heavyBuffered = false; }

        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f && !isAttacking)
            {
                Debug.Log("[Melee] Janela de combo fechou.");
                comboWindowOpen = false;
                comboStep       = 0;
            }
        }

        if (finisherWindowTimer > 0f)
        {
            finisherWindowTimer -= Time.deltaTime;
            if (finisherWindowTimer <= 0f && !isAttacking)
            {
                Debug.Log("[Melee] Janela do finalizador fechou.");
                finisherWindowOpen = false;
                comboStep          = 0;
            }
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
        Debug.Log($"[Melee] Leve — passo {comboStep}.");
        StartCoroutine(AttackRoutine(AttackType.Light));
    }

    private void ExecuteHeavy()
    {
        if (stamina != null && !stamina.Spend(heavyStaminaCost))
        {
            Debug.Log("[Melee] Stamina insuficiente para heavy.");
            heavyBuffered = false;
            return;
        }
        heavyCooldownTimer = heavyCooldown;
        comboStep = 0; comboWindowOpen = false; finisherWindowOpen = false;
        Debug.Log("[Melee] Heavy independente.");
        StartCoroutine(AttackRoutine(AttackType.Heavy));
    }

    private void ExecuteFinisher()
    {
        if (stamina != null && !stamina.Spend(finisherStaminaCost))
        {
            Debug.Log("[Melee] Stamina insuficiente para finalizador.");
            comboStep = 0;
            return;
        }
        comboStep = 0;
        Debug.Log("[Melee] FINALIZADOR.");
        StartCoroutine(AttackRoutine(AttackType.Finisher));
    }

    private IEnumerator AttackRoutine(AttackType type)
    {
        isAttacking = true;

        float startup    = type == AttackType.Light ? lightStartup    : type == AttackType.Heavy ? heavyStartup    : finisherStartup;
        float activeTime = type == AttackType.Light ? lightActiveTime : type == AttackType.Heavy ? heavyActiveTime : finisherActiveTime;
        float recovery   = type == AttackType.Light ? lightRecovery   : type == AttackType.Heavy ? heavyRecovery   : finisherRecovery;
        int   trigger    = type == AttackType.Light ? AnimLight       : type == AttackType.Heavy ? AnimHeavy       : AnimFinisher;

        animator?.SetTrigger(trigger);

        if (behavior != null) behavior.isLocked = true;
        yield return new WaitForSeconds(startup);

        ApplyHit(type);
        yield return new WaitForSeconds(activeTime);

        if (behavior != null) behavior.isLocked = false;
        isAttacking = false;

        if (type == AttackType.Light)
        {
            if (comboStep < 3)
            {
                comboWindowOpen  = true;
                comboWindowTimer = comboWindowDuration;
                Debug.Log($"[Melee] Janela de combo aberta (passo {comboStep}).");
            }
            else
            {
                finisherWindowOpen  = true;
                finisherWindowTimer = finisherWindowDuration;
                Debug.Log("[Melee] Janela do FINALIZADOR aberta.");
            }
        }

        TryConsumeBuffer();

        yield return new WaitForSeconds(recovery);

        if (!isAttacking && !comboWindowOpen && !finisherWindowOpen)
        {
            comboStep = 0;
            Debug.Log("[Melee] Recovery terminou sem encadeamento.");
        }
    }

    private void ApplyHit(AttackType type)
    {
        float range   = type == AttackType.Light ? lightAttackRange    : type == AttackType.Heavy ? heavyAttackRange    : finisherAttackRange;
        int   damage  = type == AttackType.Light ? lightDamage         : type == AttackType.Heavy ? heavyDamage         : finisherDamage;
        float hitStop = type == AttackType.Light ? lightHitStop        : type == AttackType.Heavy ? heavyHitStop        : finisherHitStop;
        float shakeI  = type == AttackType.Light ? lightShakeIntensity : type == AttackType.Heavy ? heavyShakeIntensity : finisherShakeIntensity;
        float shakeD  = type == AttackType.Light ? lightShakeDuration  : type == AttackType.Heavy ? heavyShakeDuration  : finisherShakeDuration;

        Collider2D[] hits = Physics2D.OverlapCircleAll(attackPoint.position, range, enemyLayer);

        Debug.Log($"[Melee] ApplyHit | {type} | alvos={hits.Length}");
        if (hits.Length == 0) return;

        HitStop.Instance?.DoHitStop(hitStop);
        CameraShake.Instance?.Shake(shakeI, shakeD);

        foreach (Collider2D hit in hits)
        {
            hit.GetComponent<IDamageable>()?.TakeDamage(damage);
            hit.GetComponent<HitFlash>()?.Flash();
            hit.GetComponentInParent<HitFlash>()?.Flash();
            Debug.Log($"[Melee] Hit em {hit.name} — {damage} dano ({type}).");
        }
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