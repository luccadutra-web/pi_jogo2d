using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))] 
public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform      attackPoint;
    [SerializeField] private float          attackRange = 2.0f;
    [SerializeField] private LayerMask      enemyLayer;
    [SerializeField] private Animator       animator;

    [Header("Ataque leve")]
    [SerializeField] private int   lightDamage    = 1;
    [SerializeField] private float lightCooldown  = 0.3f;
    [SerializeField] private float lightKnockback = 5f;

    [Header("Ataque pesado")]
    [SerializeField] private int   heavyDamage    = 3;
    [SerializeField] private float heavyCooldown  = 0.8f;
    [SerializeField] private float heavyKnockback = 10f;

    
    private float nextLightTime;
    private float nextHeavyTime;

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerPoints   playerPoints;
    private PlayerBehavior player;

    

    void Awake()
    {
        lightAction = new InputAction("Light", InputActionType.Button, "<Mouse>/leftButton");
        heavyAction = new InputAction("Heavy", InputActionType.Button, "<Mouse>/rightButton");
    }

    void Start()
    {
        playerPoints = GetComponentInParent<PlayerPoints>();
        player       = GetComponentInParent<PlayerBehavior>();
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }
    void OnDisable() { lightAction.Disable(); heavyAction.Disable(); }

    void Update()
    {
        
        if (player != null && (player.isLocked || player.IsDashing)) return;

        if (lightAction.WasPressedThisFrame() && Time.time >= nextLightTime)
        {
            nextLightTime = Time.time + lightCooldown;
            //animator?.SetTrigger("LightAttack");
            DealDamage(lightDamage, lightKnockback);
            Debug.Log("Light attack");
        }

        if (heavyAction.WasPressedThisFrame() && Time.time >= nextHeavyTime)
        {
            nextHeavyTime = Time.time + heavyCooldown;
            //animator?.SetTrigger("HeavyAttack");
            DealDamage(heavyDamage, heavyKnockback);
            Debug.Log("Heavy attack");
        }
    }

    void DealDamage(int damage, float knockbackForce)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            attackPoint.position, attackRange, enemyLayer
        );

        foreach (Collider2D hit in hits)
        {
            hit.SendMessage("TakeDamage",     damage,                       SendMessageOptions.DontRequireReceiver);
            hit.SendMessage("ApplyKnockback", KnockbackDir(hit) * knockbackForce, SendMessageOptions.DontRequireReceiver);
        }
    }

    
    private Vector2 KnockbackDir(Collider2D target)
    {
        Vector3 origin = player != null ? player.transform.position : transform.position;
        return (target.transform.position - origin).normalized;
    }

    void OnDrawGizmosSelected()
    {
        if (attackPoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }
}