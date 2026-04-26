using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private float     attackRange = 2.0f;
    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private Animator  animator;

    [Header("Ataque leve")]
    [SerializeField] private int   lightDamage       = 1;
    [SerializeField] private float lightCooldown     = 0.3f;
    [SerializeField] private float lightKnockback    = 5f;
    [Tooltip("Duração do lock de movimento (deve bater com a duração da animação)")]
    [SerializeField] private float lightLockDuration = 0.35f;

    [Header("Ataque pesado")]
    [SerializeField] private int   heavyDamage       = 3;
    [SerializeField] private float heavyCooldown     = 0.8f;
    [SerializeField] private float heavyKnockback    = 10f;
    [Tooltip("Duração do lock de movimento (deve bater com a duração da animação)")]
    [SerializeField] private float heavyLockDuration = 0.6f;

    private float     nextLightTime;
    private float     nextHeavyTime;
    private Coroutine lockCoroutine;

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerPoints   playerPoints;
    private PlayerBehavior player;
    private PlayerStamina  stamina;

    
    void Awake()
    {
        lightAction = new InputAction("Light", InputActionType.Button, "<Mouse>/leftButton");
        heavyAction = new InputAction("Heavy", InputActionType.Button, "<Mouse>/rightButton");
    }

    void Start()
    {
        playerPoints = GetComponentInParent<PlayerPoints>();
        player       = GetComponentInParent<PlayerBehavior>();
        stamina      = GetComponentInParent<PlayerStamina>();

            playerPoints = GetComponentInParent<PlayerPoints>();
        player       = GetComponentInParent<PlayerBehavior>();
        stamina      = GetComponentInParent<PlayerStamina>();

        // Busca o Animator no pai se não foi arrastado no Inspector
        if (animator == null)
            animator = GetComponentInParent<Animator>();
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }
    void OnDisable() { lightAction.Disable(); heavyAction.Disable(); }

    void Update()
    {
        if (player && (player.isLocked || player.IsDashing)) return;

        
        if (lightAction.WasPressedThisFrame() && Time.time >= nextLightTime)
        {
            nextLightTime = Time.time + lightCooldown;
            animator?.SetTrigger("LightAttack");
            DealDamage(lightDamage, lightKnockback);
            LockPlayerFor(lightLockDuration);
            Debug.Log("[MeleeWeapon] Ataque leve.");
        }

        
        if (heavyAction.WasPressedThisFrame() && Time.time >= nextHeavyTime)
        {
            
            if (stamina && !stamina.UseStaminaHeavy())
            {
                Debug.Log("[MeleeWeapon] Vigor insuficiente para ataque pesado.");
                return;
            }

            nextHeavyTime = Time.time + heavyCooldown;
            animator?.SetTrigger("HeavyAttack");
            DealDamage(heavyDamage, heavyKnockback);
            LockPlayerFor(heavyLockDuration);
            Debug.Log("[MeleeWeapon] Ataque pesado.");
        }
    }

    // Trava o movimento do player pela duração do ataque.
    // Se um ataque novo chegar antes do lock anterior terminar, reinicia o timer.
    void LockPlayerFor(float duration)
    {
        if (player == null) return;
        if (lockCoroutine != null) StopCoroutine(lockCoroutine);
        lockCoroutine = StartCoroutine(LockRoutine(duration));
    }

    System.Collections.IEnumerator LockRoutine(float duration)
    {
        player.isLocked = true;
        yield return new WaitForSeconds(duration);
        player.isLocked = false;
        lockCoroutine   = null;
    }

    void DealDamage(int damage, float knockbackForce)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            attackPoint.position, attackRange, enemyLayer
        );

        foreach (Collider2D hit in hits)
        {
            hit.SendMessage("TakeDamage",     damage,
                            SendMessageOptions.DontRequireReceiver);
            hit.SendMessage("ApplyKnockback", KnockbackDir(hit) * knockbackForce,
                            SendMessageOptions.DontRequireReceiver);
        }
    }

    private Vector2 KnockbackDir(Collider2D target)
    {
        Vector3 origin = player ? player.transform.position : transform.position;
        return (target.transform.position - origin).normalized;
    }

    void OnDrawGizmosSelected()
    {
        if (!attackPoint) return;  // FIX: era "if (attackPoint) return" — gizmo nunca desenhava
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}