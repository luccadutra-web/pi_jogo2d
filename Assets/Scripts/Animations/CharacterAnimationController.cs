using UnityEngine;

/// <summary>
/// Controller centralizado de animação.
/// Player e inimigos reutilizam este componente.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimationController : MonoBehaviour
{
    private Animator _animator;

    // Hash cache
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimJump = Animator.StringToHash("Jump");
    private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIsDefending = Animator.StringToHash("IsDefending");

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void SetSpeed(float speed)
    {
        if (HasParameter("Speed", AnimatorControllerParameterType.Float))
            _animator.SetFloat(AnimSpeed, speed);
    }

    public void UpdateGrounded(bool grounded)
    {
        if (HasParameter("IsGrounded", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsGrounded, grounded);
    }

    public void TriggerJump()
    {
        if (HasParameter("Jump", AnimatorControllerParameterType.Trigger))
            _animator.SetTrigger(AnimJump);

        if (HasParameter("IsGrounded", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsGrounded, false);
    }

    /// <summary>
    /// Trigger genérico.
    /// Segurança: só dispara se existir.
    /// </summary>
    public void TriggerAnimation(string triggerName)
    {
        if (HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            _animator.ResetTrigger(triggerName);
            _animator.SetTrigger(triggerName);
        }
    }

    /// <summary>
    /// Bool genérica.
    /// </summary>
    public void SetBool(string param, bool value)
    {
        if (HasParameter(param, AnimatorControllerParameterType.Bool))
            _animator.SetBool(param, value);
    }

    /// <summary>
    /// Entrar / sair da defesa.
    /// Usa bool IsDefending.
    /// </summary>
    public void SetDefending(bool value)
    {
        if (HasParameter("IsDefending", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsDefending, value);
    }

    private bool HasParameter(string paramName, AnimatorControllerParameterType type)
    {
        foreach (var param in _animator.parameters)
        {
            if (param.name == paramName && param.type == type)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Relay do Animation Event.
    /// Clip compartilhado chama isso.
    /// Enemy usa DealAttackHit().
    /// Player ignora.
    /// </summary>
    public void AnimationEvent_DealAttackHit()
    {
        EnemyBehavior enemy = GetComponent<EnemyBehavior>();

        if (enemy != null)
            enemy.DealAttackHit();
    }
}