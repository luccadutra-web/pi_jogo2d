using UnityEngine;
using System.Collections.Generic;

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

    // Cache de parâmetros — construído uma vez no Awake para evitar iteração
    // a cada chamada de HasParameter (que antes percorria o array inteiro por frame).
    private readonly HashSet<string> _triggerParams = new HashSet<string>();
    private readonly HashSet<string> _boolParams    = new HashSet<string>();
    private readonly HashSet<string> _floatParams   = new HashSet<string>();

    void Awake()
    {
        _animator = GetComponent<Animator>();
        BuildParameterCache();
    }

    private void BuildParameterCache()
    {
        _triggerParams.Clear();
        _boolParams.Clear();
        _floatParams.Clear();

        foreach (var param in _animator.parameters)
        {
            switch (param.type)
            {
                case AnimatorControllerParameterType.Trigger: _triggerParams.Add(param.name); break;
                case AnimatorControllerParameterType.Bool:    _boolParams.Add(param.name);    break;
                case AnimatorControllerParameterType.Float:   _floatParams.Add(param.name);   break;
            }
        }
    }

    public void SetSpeed(float speed)
    {
        if (_floatParams.Contains("Speed"))
            _animator.SetFloat(AnimSpeed, speed, 0f, Time.deltaTime);
    }

    public void UpdateGrounded(bool grounded)
    {
        if (_boolParams.Contains("IsGrounded"))
            _animator.SetBool(AnimIsGrounded, grounded);
    }

    public void TriggerJump()
    {
        if (_triggerParams.Contains("Jump"))
            _animator.SetTrigger(AnimJump);

        if (_boolParams.Contains("IsGrounded"))
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
        if (_triggerParams.Contains(triggerName))
        {
            _animator.ResetTrigger(triggerName);
            _animator.SetTrigger(triggerName);
        }
    }

    /// <summary>
    /// Trigger direto SEM ResetTrigger.
    /// Use para todos os triggers de ataque e para DefendStart.
    /// </summary>
    public void SetTriggerDirect(string triggerName)
    {
        if (_triggerParams.Contains(triggerName))
            _animator.SetTrigger(triggerName);
    }

    /// <summary>
    /// Bool genérica.
    /// </summary>
    public void SetBool(string param, bool value)
    {
        if (_boolParams.Contains(param))
            _animator.SetBool(param, value);
    }

    /// <summary>
    /// Entrar / sair da defesa.
    /// </summary>
    public void SetDefending(bool value)
    {
        if (_boolParams.Contains("IsDefending"))
            _animator.SetBool(AnimIsDefending, value);
    }

    /// <summary>
    /// Força transição imediata para o estado indicado, ignorando exit time.
    /// Se o state não existir no Animator, loga um aviso e não faz nada —
    /// evita o erro "State could not be found" em builds de teste.
    /// </summary>
    public void ForceState(string stateName, int layer = 0)
    {
        // Verifica se o state existe antes de tentar a transição.
        // GetCurrentAnimatorStateInfo não serve aqui; usamos HasState via hash.
        if (!_animator.HasState(layer, Animator.StringToHash(stateName)))
        {
            Debug.LogWarning($"[AnimController] ForceState: state '{stateName}' não existe no layer {layer}. Adicione o state ao Animator.");
            return;
        }

        foreach (var param in _animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger)
                _animator.ResetTrigger(param.name);
        }

        _animator.CrossFade(stateName, 0f, layer);
    }

    // ─── Relay de Animation Events ────────────────────────────────────────────
    //
    // Clipes compartilhados entre Player e Enemy disparam eventos neste componente.
    // Cada método faz o relay para o receptor correto no mesmo GameObject ou pai.

    /// <summary>
    /// Relay do Animation Event de ataque do Enemy.
    /// </summary>
    public void AnimationEvent_DealAttackHit()
    {
        GetComponent<EnemyBehavior>()?.DealAttackHit();
    }

    /// <summary>
    /// Relay dos Animation Events do MeleeWeapon (player).
    /// Necessário quando o MeleeWeapon está no mesmo GameObject que o Animator.
    /// </summary>
    public void OnAttackActiveStart()  => GetComponent<MeleeWeapon>()?.OnAttackActiveStart();
    public void OnAttackActiveEnd()    => GetComponent<MeleeWeapon>()?.OnAttackActiveEnd();
    public void OnAttackRecoveryEnd()  => GetComponent<MeleeWeapon>()?.OnAttackRecoveryEnd();
    public void OnComboWindowOpen()    => GetComponent<MeleeWeapon>()?.OnComboWindowOpen();
    public void OnComboWindowClose()   => GetComponent<MeleeWeapon>()?.OnComboWindowClose();

    /// <summary>
    /// Relay dos Animation Events do PlayerHealth (parry).
    /// </summary>
    public void OnParryWindowOpen()    => GetComponent<PlayerHealth>()?.OnParryWindowOpen();
    public void OnParryWindowClose()   => GetComponent<PlayerHealth>()?.OnParryWindowClose();

    private bool HasParameter(string paramName, AnimatorControllerParameterType type)
    {
        foreach (var param in _animator.parameters)
        {
            if (param.name == paramName && param.type == type)
                return true;
        }
        return false;
    }
}