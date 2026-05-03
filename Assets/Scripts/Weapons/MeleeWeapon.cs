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
    [SerializeField] private float lightAttackRange = 1.5f;
    [SerializeField] private int   lightDamage      = 1;
    [SerializeField] private float lightKnockback   = 5f;
    [SerializeField] private float lightHitStop     = 0.06f;

    [Header("Finalizador pesado")]
    [SerializeField] private float heavyAttackRange = 2.0f;
    [SerializeField] private int   heavyDamage      = 4;
    [SerializeField] private float heavyKnockback   = 12f;
    [SerializeField] private float heavyHitStop     = 0.14f;
    [SerializeField] private float heavyStaminaCost = 25f;

    [Header("Janela de combo")]
    [SerializeField] private float comboWindowDuration = 0.4f;

    [Header("Input buffer")]
    [SerializeField] private float inputBufferWindow = 0.12f;

    [Header("Timings provisórios (substituir por AnimationEvents)")]
    [SerializeField] private float lightStartup    = 0.10f;
    [SerializeField] private float lightActivetime = 0.08f;
    [SerializeField] private float lightRecovery   = 0.20f;
    [SerializeField] private float heavyStartup    = 0.20f;
    [SerializeField] private float heavyActivetime = 0.12f;
    [SerializeField] private float heavyRecovery   = 0.40f;

    private enum AttackType { Light, Heavy }

    private int   comboStep       = 0;
    private bool  isAttacking     = false;
    private bool  comboWindowOpen = false;
    private bool  inputBuffered   = false;
    private float comboWindowTimer;
    private float inputBufferTimer;

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerBehavior behavior;
    private PlayerStamina  stamina;

    // ── ANIMAÇÃO ──────────────────────────────────────────────────────────────
    // Todos os passos do combo (1, 2, 3) disparam o mesmo trigger enquanto
    // não houver clips distintos. Quando criar animações por passo, basta
    // voltar para $"LightAttack{comboStep}" na montagem do trigger abaixo.
    private static readonly int AnimLightAttack = Animator.StringToHash("LightAttack");
    private static readonly int AnimHeavyAttack = Animator.StringToHash("HeavyAttack");
    // ─────────────────────────────────────────────────────────────────────────

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

        if (animator == null)
            animator = GetComponentInParent<Animator>();
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }
    void OnDisable() { lightAction.Disable(); heavyAction.Disable(); }

    void Update()
    {
        if (behavior != null && (behavior.isLocked || behavior.IsDashing)) return;

        ReadInput();
        UpdateTimers();
        TryConsumeBuffer();
    }

    private void ReadInput()
    {
        if (lightAction.WasPressedThisFrame() || heavyAction.WasPressedThisFrame())
        {
            inputBuffered    = true;
            inputBufferTimer = inputBufferWindow;
            Debug.Log("Inicio combo");
        }
    }

    private void UpdateTimers()
    {
        if (inputBufferTimer > 0f)
        {
            inputBufferTimer -= Time.deltaTime;
            if (inputBufferTimer <= 0f)
                inputBuffered = false;
        }

        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f)
            {
                if (!isAttacking)
                {
                    comboWindowOpen = false;
                    comboStep       = 0;
                }
            }
        }
    }

    private void TryConsumeBuffer()
    {
        if (!inputBuffered) return;
        if (isAttacking) return;

        if (comboStep == 0 || comboWindowOpen)
        {
            ExecuteNextAttack();
            inputBuffered    = false;
            inputBufferTimer = 0f;
        }
    }

    private void ExecuteNextAttack()
    {
        comboWindowOpen = false;

        if (comboStep == 3)
        {
            if (stamina != null && !stamina.Spend(heavyStaminaCost))
            {
                comboStep = 0;
                return;
            }
            comboStep = 4;
            StartCoroutine(AttackRoutine(AttackType.Heavy));
        }
        else
        {
            comboStep++;
            StartCoroutine(AttackRoutine(AttackType.Light));
        }
    }

    private IEnumerator AttackRoutine(AttackType type)
    {
        isAttacking = true;

        if (behavior != null) behavior.isLocked = true;

        float startup    = type == AttackType.Heavy ? heavyStartup    : lightStartup;
        float activeTime = type == AttackType.Heavy ? heavyActivetime : lightActivetime;
        float recovery   = type == AttackType.Heavy ? heavyRecovery   : lightRecovery;

        // ── ANIMAÇÃO ──────────────────────────────────────────────────────────
        // Dispara o trigger correspondente ao tipo de ataque.
        // LightAttack usa um único clip para todos os passos do combo por ora.
        // Quando houver clips por passo, substituir AnimLightAttack por:
        //   Animator.StringToHash($"LightAttack{comboStep}")
        // e criar os parâmetros LightAttack1, LightAttack2, LightAttack3 no Animator.
        if (type == AttackType.Heavy)
            animator?.SetTrigger(AnimHeavyAttack);
        else
            animator?.SetTrigger(AnimLightAttack);
        // ─────────────────────────────────────────────────────────────────────

        yield return new WaitForSeconds(startup);

        OnAttackActiveStart(type);
        yield return new WaitForSeconds(activeTime);

        OnAttackActiveEnd();

        if (type == AttackType.Light && comboStep < 3)
        {
            comboWindowOpen  = true;
            comboWindowTimer = comboWindowDuration;
        }

        yield return new WaitForSeconds(recovery);

        OnAttackRecoveryEnd(type);
    }

    private void OnAttackActiveStart(AttackType type)
    {
        float range   = type == AttackType.Heavy ? heavyAttackRange : lightAttackRange;
        int   damage  = type == AttackType.Heavy ? heavyDamage      : lightDamage;
        float knock   = type == AttackType.Heavy ? heavyKnockback   : lightKnockback;
        float hitStop = type == AttackType.Heavy ? heavyHitStop     : lightHitStop;

        Collider2D[] hits = Physics2D.OverlapCircleAll(
            attackPoint.position, range, enemyLayer
        );

        if (hits.Length == 0) return;

        HitStop.Instance?.DoHitStop(hitStop);

        foreach (Collider2D hit in hits)
        {
            Vector2 dir = (hit.transform.position - transform.position).normalized;
            hit.GetComponent<IDamageable>()?.TakeDamage(damage);
            hit.GetComponent<IDamageable>()?.ReceiveKnockback(dir * knock);
        }
    }

    private void OnAttackActiveEnd()
    {
        // Hitbox desliga, nada mais a fazer por enquanto
        // Quando tiver sprite de hitbox visual, desativa aqui
    }

    private void OnAttackRecoveryEnd(AttackType type)
    {
        isAttacking = false;

        if (behavior != null) behavior.isLocked = false;

        if (type == AttackType.Heavy || !comboWindowOpen)
            comboStep = 0;
    }

    void OnDrawGizmosSelected()
    {
        if (!attackPoint) return;

        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(attackPoint.position, lightAttackRange);

        Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
        Gizmos.DrawWireSphere(attackPoint.position, heavyAttackRange);
    }
}