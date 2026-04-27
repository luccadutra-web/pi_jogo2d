using UnityEngine;

[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerBehavior))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerAnimator : MonoBehaviour
{
    private Animator       anim;
    private PlayerBehavior movement;
    private PlayerHealth   health;

    private static readonly int SpeedHash       = Animator.StringToHash("Speed");
    private static readonly int IsJumpingHash   = Animator.StringToHash("IsJumping");
    private static readonly int IsDeadHash      = Animator.StringToHash("IsDead");
    private static readonly int IsDefendingHash = Animator.StringToHash("IsDefending");
    private static readonly int IsDashingHash      = Animator.StringToHash("IsDashing");
    private static readonly int LightAttackHash    = Animator.StringToHash("LightAttack");
    private static readonly int HeavyAttackHash    = Animator.StringToHash("HeavyAttack");
    // Removido: IsGroundedHash — não é mais necessário com IsJumping controlado manualmente

    void Awake()
    {
        anim     = GetComponent<Animator>();
        movement = GetComponent<PlayerBehavior>();
        health   = GetComponent<PlayerHealth>();
        // Removido: rb — velocidade agora vem de movement.HorizontalSpeed

        health.OnDeath += PlayDeath;
    }

    void OnDestroy()
    {
        if (health != null)
            health.OnDeath -= PlayDeath;
    }

    void Update()
    {
        if (movement.isLocked) return;

        // Removido: Mathf.Abs(rb.linearVelocity.x) — centralizado no PlayerBehavior
        anim.SetFloat(SpeedHash,       movement.HorizontalSpeed);
        anim.SetBool(IsJumpingHash,    movement.IsJumping);
        anim.SetBool(IsDashingHash,    movement.IsDashing);
        anim.SetBool(IsDefendingHash,  health.IsDefending);
    }

    private void PlayDeath() => anim.SetTrigger(IsDeadHash);

    // Chamado pelo MeleeWeapon via animator?.SetTrigger — o Animator
    // propaga automaticamente via StringToHash, não precisa chamar aqui.
    // Esses métodos existem caso queira disparar por código no futuro.
    public void PlayLightAttack() => anim.SetTrigger(LightAttackHash);
    public void PlayHeavyAttack() => anim.SetTrigger(HeavyAttackHash);
}