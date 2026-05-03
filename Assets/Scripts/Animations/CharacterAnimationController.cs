using UnityEngine;

/// <summary>
/// Controller centralizado de animação.
/// Player e inimigos podem reutilizar este componente.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimationController : MonoBehaviour
{
    private Animator _animator;

    // Hashes dos parâmetros base
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimJump = Animator.StringToHash("Jump");
    private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");

    void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Atualiza velocidade horizontal (Idle / Walk / Run).
    /// </summary>
    public void SetSpeed(float speed)
    {
        if (_animator == null) return;

        // Segurança:
        // só seta se existir parâmetro float "Speed"
        if (HasParameter("Speed", AnimatorControllerParameterType.Float))
            _animator.SetFloat(AnimSpeed, speed);
    }

    /// <summary>
    /// Atualiza estado de chão.
    /// </summary>
    public void UpdateGrounded(bool grounded)
    {
        if (_animator == null) return;

        // Segurança:
        // só seta se existir parâmetro bool "IsGrounded"
        if (HasParameter("IsGrounded", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsGrounded, grounded);
    }

    /// <summary>
    /// Dispara trigger de animação.
    ///
    /// Ex:
    /// "Jump"
    /// "LightAttack"
    /// "HeavyAttack"
    /// "Hit"
    /// "Die"
    ///
    /// Agora com validação:
    /// se o parâmetro não existir, não quebra o jogo.
    /// </summary>
    public void TriggerAnimation(string triggerName)
    {
        if (_animator == null) return;

        // ── ALTERAÇÃO (NOVO) ──────────────────────────────────────────────
        // Antes:
        // _animator.SetTrigger(triggerName);
        //
        // Problema:
        // Se o Animator não tiver esse parâmetro -> erro.
        //
        // Agora:
        // validamos antes.
        // ─────────────────────────────────────────────────────────────────
        if (HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            _animator.SetTrigger(triggerName);
        }
        else
        {
            Debug.LogWarning(
                $"[{gameObject.name}] Trigger '{triggerName}' não existe no Animator."
            );
        }
    }

    /// <summary>
    /// Setter bool genérico.
    /// </summary>
    public void SetBool(string param, bool value)
    {
        if (_animator == null) return;

        if (HasParameter(param, AnimatorControllerParameterType.Bool))
            _animator.SetBool(param, value);
    }

    /// <summary>
    /// Trigger de pulo com ajuste de grounded.
    /// </summary>
    public void TriggerJump()
    {
        if (_animator == null) return;

        if (HasParameter("Jump", AnimatorControllerParameterType.Trigger))
            _animator.SetTrigger(AnimJump);

        if (HasParameter("IsGrounded", AnimatorControllerParameterType.Bool))
            _animator.SetBool(AnimIsGrounded, false);
    }

    // ── HELPER (NOVO) ────────────────────────────────────────────────────
    // Verifica se o Animator possui determinado parâmetro
    // e se ele é do tipo esperado.
    //
    // Isso torna o controller reutilizável para qualquer personagem.
    // ─────────────────────────────────────────────────────────────────────
    private bool HasParameter(string paramName, AnimatorControllerParameterType type)
    {
        if (_animator == null) return false;

        foreach (var param in _animator.parameters)
        {
            if (param.name == paramName && param.type == type)
                return true;
        }

        return false;
    }

    // ── ANIMATION EVENTS ─────────────────────────────────────────────────
    // Relay para clips compartilhados.
    // Player ignora.
    // Enemy repassa hit.
    // ─────────────────────────────────────────────────────────────────────
    public void AnimationEvent_DealAttackHit()
    {
        EnemyBehavior enemy = GetComponent<EnemyBehavior>();

        if (enemy != null)
        {
            enemy.DealAttackHit();
        }
    }
}