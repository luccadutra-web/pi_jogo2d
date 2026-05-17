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
///      repassados ao MeleeWeapon / OwlEnemy / EnemyBehavior
///    - Avisos se parâmetros ou states não existirem no Animator
///
/// ── RELAY DE ANIMATION EVENTS ────────────────────────────────────────────────
///
///  Os clipes disparam eventos NESTE componente.
///  Cada método faz o relay para o componente correto no mesmo GameObject.
///
///  Player / MeleeWeapon:
///    OnAttackActiveStart / OnAttackActiveEnd / OnAttackRecoveryEnd
///    OnComboWindowOpen   / OnComboWindowClose
///
///  Player / PlayerHealth:
///    OnParryWindowOpen   / OnParryWindowClose
///
///  OwlEnemy — light_attack:
///    OnLightHitWindowOpen / OnLightHitWindowClose / OnLightAttackEnd
///
///  OwlEnemy — heavy_attack:
///    OnHeavyLaunch / OnHeavyHitWindowOpen / OnHeavyHitWindowClose
///
///  EnemyBehavior (inimigos genéricos):
///    AnimationEvent_DealAttackHit
///
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimationController : MonoBehaviour
{
    private Animator _animator;

    // ─── Hash cache ───────────────────────────────────────────────────────────
    private static readonly int AnimSpeed       = Animator.StringToHash("Speed");
    private static readonly int AnimJump        = Animator.StringToHash("Jump");
    private static readonly int AnimIsGrounded  = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIsDefending = Animator.StringToHash("IsDefending");

    // ─── Cache de parâmetros ──────────────────────────────────────────────────
    // Construído uma vez no Awake para evitar iteração por frame.
    private readonly HashSet<string> _triggerParams = new HashSet<string>();
    private readonly HashSet<string> _boolParams    = new HashSet<string>();
    private readonly HashSet<string> _floatParams   = new HashSet<string>();

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        _animator = GetComponent<Animator>();
        BuildParameterCache();
    }

    void Start()
    {
        // Diagnóstico de setup — verifica parâmetros essenciais do PLAYER.
        // Inimigos terão parâmetros diferentes; os avisos abaixo são esperados
        // em GameObjects de inimigos e podem ser ignorados com segurança.
        string[] essentialFloats   = { "Speed" };
        string[] essentialBools    = { "IsGrounded", "IsDefending" };
        string[] essentialTriggers = { "Jump", "LightAttack1", "LightAttack2", "LightAttack3",
                                       "HeavyAttack", "Finisher", "CounterAttack" };

        foreach (string p in essentialFloats)
            if (!_floatParams.Contains(p))
                Debug.LogWarning($"[AnimController] Float '{p}' não encontrado no Animator de '{gameObject.name}'.");

        foreach (string p in essentialBools)
            if (!_boolParams.Contains(p))
                Debug.LogWarning($"[AnimController] Bool '{p}' não encontrado no Animator de '{gameObject.name}'.");

        foreach (string p in essentialTriggers)
            if (!_triggerParams.Contains(p))
                Debug.LogWarning($"[AnimController] Trigger '{p}' não encontrado no Animator de '{gameObject.name}'.");

        Debug.Log($"[AnimController] Inicializado em '{gameObject.name}'. " +
                  $"Triggers={_triggerParams.Count} Bools={_boolParams.Count} Floats={_floatParams.Count}");
    }

    // ─── Cache interno ────────────────────────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════════════
    // API PÚBLICA — chamada por Player, inimigos e sistemas externos
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Atualiza o parâmetro Speed com damping suave (0.08s). Use para movimento.</summary>
    public void SetSpeed(float speed)
    {
        if (_floatParams.Contains("Speed"))
            _animator.SetFloat(AnimSpeed, speed, 0.08f, Time.deltaTime);
    }

    /// <summary>
    /// Zera Speed instantaneamente, sem damping.
    /// SEMPRE chame este método antes de disparar um trigger de ataque ou reação,
    /// para garantir que o Animator já leia Speed = 0 no mesmo frame.
    /// </summary>
    public void StopImmediate()
    {
        if (_floatParams.Contains("Speed"))
            _animator.SetFloat(AnimSpeed, 0f);
    }

    /// <summary>Atualiza o bool IsGrounded.</summary>
    public void UpdateGrounded(bool grounded)
    {
        if (_boolParams.Contains("IsGrounded"))
            _animator.SetBool(AnimIsGrounded, grounded);
    }

    /// <summary>Dispara o trigger de pulo e marca IsGrounded = false.</summary>
    public void TriggerJump()
    {
        if (_triggerParams.Contains("Jump"))
        {
            _animator.SetTrigger(AnimJump);
            Debug.Log("[AnimController] TriggerJump disparado.");
        }
        else
            Debug.LogWarning("[AnimController] TriggerJump: trigger 'Jump' não encontrado.");

        if (_boolParams.Contains("IsGrounded"))
            _animator.SetBool(AnimIsGrounded, false);
    }

    /// <summary>
    /// Trigger genérico COM ResetTrigger defensivo.
    /// Use para estados reativos: Hurt, Die, Land — onde trigger stale
    /// acumulado causaria re-disparo indesejado.
    /// NÃO use para ataques encadeados do player (use SetTriggerDirect).
    /// </summary>
    public void TriggerAnimation(string triggerName)
    {
        if (_triggerParams.Contains(triggerName))
        {
            _animator.ResetTrigger(triggerName);
            _animator.SetTrigger(triggerName);
            Debug.Log($"[AnimController] TriggerAnimation (reset+set): '{triggerName}'");
        }
        else
            Debug.LogWarning($"[AnimController] TriggerAnimation: trigger '{triggerName}' não encontrado no Animator de '{gameObject.name}'.");
    }

    /// <summary>
    /// Trigger direto SEM ResetTrigger.
    /// Use para ataques encadeados do player e para DefendStart,
    /// onde o enfileiramento de triggers é desejável.
    /// </summary>
    public void SetTriggerDirect(string triggerName)
    {
        if (_triggerParams.Contains(triggerName))
        {
            _animator.SetTrigger(triggerName);
            Debug.Log($"[AnimController] SetTriggerDirect: '{triggerName}'");
        }
        else
            Debug.LogWarning($"[AnimController] SetTriggerDirect: trigger '{triggerName}' não encontrado no Animator de '{gameObject.name}'.");
    }

    /// <summary>Define um parâmetro bool pelo nome.</summary>
    public void SetBool(string param, bool value)
    {
        if (_boolParams.Contains(param))
            _animator.SetBool(param, value);
        else
            Debug.LogWarning($"[AnimController] SetBool: parâmetro '{param}' não encontrado.");
    }

    /// <summary>Ativa/desativa o bool IsDefending.</summary>
    public void SetDefending(bool value)
    {
        if (_boolParams.Contains("IsDefending"))
            _animator.SetBool(AnimIsDefending, value);
        else
            Debug.LogWarning("[AnimController] SetDefending: bool 'IsDefending' não encontrado.");
    }

    /// <summary>
    /// Força transição imediata para o state indicado, ignorando exit time.
    /// Reseta todos os triggers pendentes antes de fazer o CrossFade.
    /// Se o state não existir, loga aviso e não faz nada.
    /// </summary>
    public void ForceState(string stateName, int layer = 0)
    {
        int hash = Animator.StringToHash(stateName);
        if (!_animator.HasState(layer, hash))
        {
            Debug.LogWarning($"[AnimController] ForceState: state '{stateName}' não existe no layer {layer} de '{gameObject.name}'.");
            return;
        }

        foreach (var param in _animator.parameters)
            if (param.type == AnimatorControllerParameterType.Trigger)
                _animator.ResetTrigger(param.name);

        _animator.CrossFade(stateName, 0f, layer);
        Debug.Log($"[AnimController] ForceState → '{stateName}' (layer {layer})");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — PLAYER / MeleeWeapon
    // ═══════════════════════════════════════════════════════════════════════════
    //
    // Os clipes do player disparam eventos aqui; cada método repassa ao
    // MeleeWeapon no mesmo GameObject.
    // Se MeleeWeapon estiver num filho, troque GetComponent por GetComponentInChildren.

    public void OnAttackActiveStart()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackActiveStart();
        else
            Debug.LogError($"[AnimController] OnAttackActiveStart: MeleeWeapon não encontrado em '{gameObject.name}'.");
    }

    public void OnAttackActiveEnd()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackActiveEnd();
        else
            Debug.LogError($"[AnimController] OnAttackActiveEnd: MeleeWeapon não encontrado em '{gameObject.name}'.");
    }

    public void OnAttackRecoveryEnd()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnAttackRecoveryEnd();
        else
            Debug.LogError($"[AnimController] OnAttackRecoveryEnd: MeleeWeapon não encontrado em '{gameObject.name}'.");
    }

    public void OnComboWindowOpen()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnComboWindowOpen();
        else
            Debug.LogError($"[AnimController] OnComboWindowOpen: MeleeWeapon não encontrado em '{gameObject.name}'.");
    }

    public void OnComboWindowClose()
    {
        var weapon = GetComponent<MeleeWeapon>();
        if (weapon != null)
            weapon.OnComboWindowClose();
        else
            Debug.LogError($"[AnimController] OnComboWindowClose: MeleeWeapon não encontrado em '{gameObject.name}'.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — PLAYER / PlayerHealth (janela de parry)
    // ═══════════════════════════════════════════════════════════════════════════

    public void OnParryWindowOpen()
    {
        GetComponent<PlayerHealth>()?.OnParryWindowOpen();
    }

    public void OnParryWindowClose()
    {
        GetComponent<PlayerHealth>()?.OnParryWindowClose();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — OwlEnemy / light_attack
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Animation Event — clipe "light_attack".
    /// Coloque no frame que o bico começa a descer (início do hitbox).
    /// </summary>
    public void OnLightHitWindowOpen()
    {
        GetComponent<OwlEnemy>()?.OnLightHitWindowOpen();
        Debug.Log("[AnimController] Relay → OnLightHitWindowOpen");
    }

    /// <summary>
    /// Animation Event — clipe "light_attack".
    /// Coloque no frame que o bico começa a subir (fim do hitbox).
    /// </summary>
    public void OnLightHitWindowClose()
    {
        GetComponent<OwlEnemy>()?.OnLightHitWindowClose();
        Debug.Log("[AnimController] Relay → OnLightHitWindowClose");
    }

    /// <summary>
    /// Animation Event — clipe "light_attack".
    /// Coloque no último frame (ou 1–2 antes do fim) para liberar o estado.
    /// </summary>
    public void OnLightAttackEnd()
    {
        GetComponent<OwlEnemy>()?.OnLightAttackEnd();
        Debug.Log("[AnimController] Relay → OnLightAttackEnd");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — OwlEnemy / heavy_attack
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Animation Event — clipe "heavy_attack".
    /// Coloque no frame que a coruja se lança para frente.
    /// Aplica a velocidade do Rigidbody via OwlEnemy.
    /// </summary>
    public void OnHeavyLaunch()
    {
        GetComponent<OwlEnemy>()?.OnHeavyLaunch();
        Debug.Log("[AnimController] Relay → OnHeavyLaunch");
    }

    /// <summary>
    /// Animation Event — clipe "heavy_attack".
    /// Coloque no frame do bico / momento de impacto (abre hitbox).
    /// </summary>
    public void OnHeavyHitWindowOpen()
    {
        GetComponent<OwlEnemy>()?.OnHeavyHitWindowOpen();
        Debug.Log("[AnimController] Relay → OnHeavyHitWindowOpen");
    }

    /// <summary>
    /// Animation Event — clipe "heavy_attack".
    /// Coloque 1–2 frames após o impacto (fecha hitbox).
    /// </summary>
    public void OnHeavyHitWindowClose()
    {
        GetComponent<OwlEnemy>()?.OnHeavyHitWindowClose();
        Debug.Log("[AnimController] Relay → OnHeavyHitWindowClose");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — EnemyAudio (SFX dos ataques do inimigo)
    // ═══════════════════════════════════════════════════════════════════════════
    //
    // Adicione estes Animation Events nos clipes do inimigo:
    //   light_attack → OnEnemyAttackLight   (frame do bico descendo)
    //   heavy_attack → OnEnemyAttackHeavy   (frame do lançamento)
    //   hurt         → OnEnemyHurt          (frame do impacto)
    //   die          → OnEnemyDeath         (frame inicial)

    public void OnEnemyAttackLight()
    {
        GetComponent<EnemyAudio>()?.OnEnemyAttackLight();
        Debug.Log("[AnimController] Relay → OnEnemyAttackLight");
    }

    public void OnEnemyAttackHeavy()
    {
        GetComponent<EnemyAudio>()?.OnEnemyAttackHeavy();
        Debug.Log("[AnimController] Relay → OnEnemyAttackHeavy");
    }

    public void OnEnemyHurt()
    {
        GetComponent<EnemyAudio>()?.OnEnemyHurt();
        Debug.Log("[AnimController] Relay → OnEnemyHurt");
    }

    public void OnEnemyDeath()
    {
        GetComponent<EnemyAudio>()?.OnEnemyDeath();
        Debug.Log("[AnimController] Relay → OnEnemyDeath");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RELAY — EnemyBehavior (inimigos genéricos)
    // ═══════════════════════════════════════════════════════════════════════════

    public void AnimationEvent_DealAttackHit()
    {
        GetComponent<EnemyBehavior>()?.DealAttackHit();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UTILITÁRIO INTERNO
    // ═══════════════════════════════════════════════════════════════════════════

    private bool HasParameter(string paramName, AnimatorControllerParameterType type)
    {
        foreach (var param in _animator.parameters)
            if (param.name == paramName && param.type == type)
                return true;
        return false;
    }
}