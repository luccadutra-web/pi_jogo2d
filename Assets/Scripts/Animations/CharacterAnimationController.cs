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
    private static readonly int AnimSpeed        = Animator.StringToHash("Speed");
    private static readonly int AnimJump         = Animator.StringToHash("Jump");
    private static readonly int AnimIsGrounded   = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIsDefending  = Animator.StringToHash("IsDefending");
    private static readonly int AnimDefendStart  = Animator.StringToHash("DefendStart");

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void SetSpeed(float speed)
    {
        if (HasParameter("Speed", AnimatorControllerParameterType.Float))
            // dampTime=0 → sem interpolação, blend tree responde no mesmo frame.
            // Evita o delay visual entre idle pós-ataque e walk.
            _animator.SetFloat(AnimSpeed, speed, 0f, Time.deltaTime);
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
    /// Trigger genérico com ResetTrigger defensivo.
    /// Usado para estados reativos (hurt, die, stagger) onde não pode haver
    /// trigger stale acumulado de frame anterior.
    /// NÃO use para ataques do player — use SetTriggerDirect.
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
    /// Trigger direto SEM ResetTrigger.
    ///
    /// Use para todos os triggers de ataque (LightAttack, HeavyAttack, Finisher,
    /// CounterAttack) e para DefendStart — onde o ResetTrigger cancelaria o trigger
    /// silenciosamente durante transições de saída de estado.
    /// </summary>
    public void SetTriggerDirect(string triggerName)
    {
        if (HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
            _animator.SetTrigger(triggerName);
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
    /// FIX: StartDefend chama SetTriggerDirect("DefendStart") para a transição imediata,
    /// e depois SetDefending(true) para manter o bool que sustenta o estado Defending.
    /// Sem o trigger, a transição dependia apenas do bool avaliado no próximo frame
    /// do Animator, causando 1 frame de delay visual.
    /// </summary>
    public void SetDefending(bool value)
    {
        if (HasParameter("IsDefending", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsDefending, value);
    }

    /// <summary>
    /// Força transição imediata para o estado indicado, ignorando exit time
    /// e qualquer trigger pendente na fila do Animator.
    /// Use para interrupções de alta prioridade: parry stagger, poise break, morte.
    /// </summary>
    public void ForceState(string stateName, int layer = 0)
    {
        // Limpa todos os triggers pendentes para evitar que a animação anterior
        // "reapareça" um frame depois por um trigger stale ainda na fila.
        foreach (var param in _animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger)
                _animator.ResetTrigger(param.name);
        }

        // CrossFade com duration=0 → transição instantânea, ignora exit time.
        _animator.CrossFade(stateName, 0f, layer);
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