using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Controller centralizado de animação.
/// Player e inimigos reutilizam este componente.
///
/// ── LOGS DE DIAGNÓSTICO ──────────────────────────────────────────────────────
///
///  Filtre o Console por "[AnimController]" para ver:
///    - Todos os triggers disparados (SetTriggerDirect / TriggerAnimation)
///    - ForceState: estado solicitado + resultado (sucesso ou state não encontrado)
///    - Relay dos Animation Events: confirma que os eventos chegaram e foram
///      repassados ao MeleeWeapon
///    - Avisos se parâmetros ou states não existirem no Animator
///
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimationController : MonoBehaviour
{
    private Animator _animator;

    // Hash cache
    private static readonly int AnimSpeed       = Animator.StringToHash("Speed");
    private static readonly int AnimJump        = Animator.StringToHash("Jump");
    private static readonly int AnimIsGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIsDefending = Animator.StringToHash("IsDefending");
    private static readonly int AnimDefendStart = Animator.StringToHash("DefendStart");

    // Cache de parâmetros — construído uma vez no Awake para evitar iteração por frame.
    private readonly HashSet<string> _triggerParams = new HashSet<string>();
    private readonly HashSet<string> _boolParams    = new HashSet<string>();
    private readonly HashSet<string> _floatParams   = new HashSet<string>();

    void Awake()
    {
        _animator = GetComponent<Animator>();
        BuildParameterCache();
    }

    void Start()
    {
        // ── Diagnóstico de setup ──────────────────────────────────────────────
        // Verifica se os parâmetros essenciais existem no Animator Controller.
        // Se algum estiver faltando, aparece no console logo ao entrar em Play.
        string[] essentialFloats   = { "Speed" };
        string[] essentialBools    = { "IsGrounded", "IsDefending" };
        string[] essentialTriggers = { "Jump", "LightAttack1", "LightAttack2", "LightAttack3",
                                       "HeavyAttack", "Finisher", "CounterAttack" };

        foreach (string p in essentialFloats)
            if (!_floatParams.Contains(p))
                Debug.LogWarning($"[AnimController] Parâmetro float '{p}' não encontrado no Animator. " +
                                 $"Verifique o Animator Controller em '{gameObject.name}'.");

        foreach (string p in essentialBools)
            if (!_boolParams.Contains(p))
                Debug.LogWarning($"[AnimController] Parâmetro bool '{p}' não encontrado no Animator. " +
                                 $"Verifique o Animator Controller em '{gameObject.name}'.");

        foreach (string p in essentialTriggers)
            if (!_triggerParams.Contains(p))
                Debug.LogWarning($"[AnimController] Trigger '{p}' não encontrado no Animator. " +
                                 $"Verifique o Animator Controller em '{gameObject.name}'.");

        Debug.Log($"[AnimController] Inicializado em '{gameObject.name}'. " +
                  $"Triggers={_triggerParams.Count} Bools={_boolParams.Count} Floats={_floatParams.Count}");
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
            _animator.SetFloat(AnimSpeed, speed, 0.08f, Time.deltaTime);
    }

    public void UpdateGrounded(bool grounded)
    {
        if (_boolParams.Contains("IsGrounded"))
            _animator.SetBool(AnimIsGrounded, grounded);
    }

    public void TriggerJump()
    {
        if (_triggerParams.Contains("Jump"))
        {
            _animator.SetTrigger(AnimJump);
            Debug.Log("[AnimController] TriggerJump disparado.");
        }
        else
            Debug.LogWarning("[AnimController] TriggerJump: trigger 'Jump' não encontrado no Animator.");

        if (_boolParams.Contains("IsGrounded"))
            _animator.SetBool(AnimIsGrounded, false);
    }

    /// <summary>
    /// Trigger genérico com ResetTrigger defensivo.
    /// Usado para estados reativos (hurt, die, stagger) onde não pode haver
    /// trigger stale acumulado. NÃO use para ataques do player — use SetTriggerDirect.
    /// </summary>
    public void TriggerAnimation(string triggerName)
    {
        if (_triggerParams.Contains(triggerName))
        {
            _animator.ResetTrigger(triggerName);
            _animator.SetTrigger(triggerName);
            Debug.Log($"[AnimController] TriggerAnimation (com reset): '{triggerName}'");
        }
        else
            Debug.LogWarning($"[AnimController] TriggerAnimation: trigger '{triggerName}' não encontrado no Animator.");
    }

    /// <summary>
    /// Trigger direto SEM ResetTrigger.
    /// Use para todos os triggers de ataque e para DefendStart.
    /// </summary>
    public void SetTriggerDirect(string triggerName)
    {
        if (_triggerParams.Contains(triggerName))
        {
            _animator.SetTrigger(triggerName);
            Debug.Log($"[AnimController] SetTriggerDirect: '{triggerName}'");
        }
        else
            Debug.LogWarning($"[AnimController] SetTriggerDirect: trigger '{triggerName}' não encontrado no Animator. " +
                             $"Verifique se o trigger está cadastrado no Animator Controller.");
    }

    /// <summary>
    /// Bool genérica.
    /// </summary>
    public void SetBool(string param, bool value)
    {
        if (_boolParams.Contains(param))
            _animator.SetBool(param, value);
        else
            Debug.LogWarning($"[AnimController] SetBool: parâmetro '{param}' não encontrado no Animator.");
    }

    /// <summary>
    /// Entrar / sair da defesa.
    /// </summary>
    public void SetDefending(bool value)
    {
        if (_boolParams.Contains("IsDefending"))
            _animator.SetBool(AnimIsDefending, value);
        else
            Debug.LogWarning("[AnimController] SetDefending: bool 'IsDefending' não encontrado no Animator.");
    }

    /// <summary>
    /// Força transição imediata para o estado indicado, ignorando exit time.
    /// Se o state não existir no Animator, loga um aviso e não faz nada —
    /// evita o erro "State could not be found" em builds de teste.
    /// </summary>
    public void ForceState(string stateName, int layer = 0)
    {
        int hash = Animator.StringToHash(stateName);
        if (!_animator.HasState(layer, hash))
        {
            Debug.LogWarning($"[AnimController] ForceState: state '{stateName}' não existe no layer {layer}. " +
                             $"Adicione o state ao Animator — o player pode ficar preso na animação atual.");
            return;
        }

        foreach (var param in _animator.parameters)
        {
            if (param.type == AnimatorControllerParameterType.Trigger)
                _animator.ResetTrigger(param.name);
        }

        _animator.CrossFade(stateName, 0f, layer);
        Debug.Log($"[AnimController] ForceState: '{stateName}' (layer {layer})");
    }

    // ─── Relay de Animation Events ────────────────────────────────────────────
    //
    // Os clipes de animação disparam eventos neste componente.
    // Cada método faz o relay para MeleeWeapon no mesmo GameObject.
    //
    // IMPORTANTE: MeleeWeapon deve estar no MESMO GameObject que este componente.
    // Se estiver num filho, troque GetComponent por GetComponentInChildren abaixo.

    public void OnAttackActiveStart()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackActiveStart();
        else
            Debug.LogError("[AnimController] OnAttackActiveStart: MeleeWeapon não encontrado em " +
                           $"'{gameObject.name}'. O evento não chegará à rotina de ataque.");
    }

    public void OnAttackActiveEnd()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackActiveEnd();
        else
            Debug.LogError("[AnimController] OnAttackActiveEnd: MeleeWeapon não encontrado em " +
                           $"'{gameObject.name}'.");
    }

    public void OnAttackRecoveryEnd()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackRecoveryEnd();
        else
            Debug.LogError("[AnimController] OnAttackRecoveryEnd: MeleeWeapon não encontrado em " +
                           $"'{gameObject.name}'.");
    }

    public void OnComboWindowOpen()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnComboWindowOpen();
        else
            Debug.LogError("[AnimController] OnComboWindowOpen: MeleeWeapon não encontrado em " +
                           $"'{gameObject.name}'.");
    }

    public void OnComboWindowClose()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnComboWindowClose();
        else
            Debug.LogError("[AnimController] OnComboWindowClose: MeleeWeapon não encontrado em " +
                           $"'{gameObject.name}'.");
    }

    // ─── Relay de parry ───────────────────────────────────────────────────────

    public void OnParryWindowOpen()
    {
        GetComponent<PlayerHealth>()?.OnParryWindowOpen();
        GetComponent<OwlEnemy>()?.OnParryWindowOpen();
    }

    public void OnParryWindowClose()
    {
        GetComponent<PlayerHealth>()?.OnParryWindowClose();
        GetComponent<OwlEnemy>()?.OnParryWindowClose();
    }

    // ─── Relay OwlEnemy ───────────────────────────────────────────────────────

    public void OnHitWindowOpen()  => GetComponent<OwlEnemy>()?.OnHitWindowOpen();
    public void OnHitWindowClose() => GetComponent<OwlEnemy>()?.OnHitWindowClose();

    // ─── Relay Enemy genérico ─────────────────────────────────────────────────

    public void AnimationEvent_DealAttackHit()
    {
        GetComponent<EnemyBehavior>()?.DealAttackHit();
    }

    // ─── Utilitário interno ───────────────────────────────────────────────────

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