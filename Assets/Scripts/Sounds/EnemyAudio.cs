using UnityEngine;

/// <summary>
/// EnemyAudio — sons do inimigo conectados ao AudioManager.
///
/// ── SETUP ────────────────────────────────────────────────────────────────────
///
///   Adicione este componente no mesmo GameObject do EnemyBehavior.
///   Os métodos públicos são chamados via Animation Events nos clips do inimigo.
///
/// ── KEYS ESPERADAS NO AUDIOMANAGER ──────────────────────────────────────────
///
///   "enemy_hurt"         — inimigo recebe dano
///   "enemy_death"        — inimigo morre
///   "enemy_attack_light" — swing do ataque leve
///   "enemy_attack_heavy" — startup/swing do ataque pesado
///   "enemy_stagger"      — inimigo entra em stagger (parry ou poise break)
///
/// ── COMO CHAMAR DE ANIMATION EVENTS ──────────────────────────────────────────
///
///   Nos clips de animação do inimigo:
///     • Clip LightAttack  → Animation Event → OnEnemyAttackLight
///     • Clip HeavyAttack  → Animation Event → OnEnemyAttackHeavy
///     • Clip Hit          → Animation Event → OnEnemyHurt
///     • Clip Die          → Animation Event → OnEnemyDeath
///     • Clip Stagger      → Animation Event → OnEnemyStagger
///
///   O receptor do evento deve ser o EnemyAudio (arraste o GameObject do inimigo
///   no campo do evento no Animation Clip).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class EnemyAudio : MonoBehaviour
{
    [Header("Volumes")]
    [Range(0f, 1f)] [SerializeField] private float attackVolume  = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float hurtVolume    = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float deathVolume   = 0.9f;
    [Range(0f, 1f)] [SerializeField] private float staggerVolume = 0.6f;

    [Header("Variação de pitch por inimigo")]
    [Tooltip("Offset de pitch aplicado a todos os sons deste inimigo.\n" +
             "Permite diferenciar dois inimigos iguais na mesma cena.\n" +
             "Ex: 0 = normal, -0.1 = mais grave, +0.1 = mais agudo.")]
    [SerializeField] private float pitchOffset = 0f;

    // ── Animation Events ──────────────────────────────────────────────────────

    public void OnEnemyAttackLight()
    {
        AudioManager.Instance?.PlaySFX("enemy_attack_light", attackVolume, 1f + pitchOffset);
    }

    public void OnEnemyAttackHeavy()
    {
        AudioManager.Instance?.PlaySFX("enemy_attack_heavy", attackVolume, 0.9f + pitchOffset);
    }

    public void OnEnemyHurt()
    {
        AudioManager.Instance?.PlaySFX("enemy_hurt", hurtVolume, 1f + pitchOffset);
    }

    public void OnEnemyDeath()
    {
        AudioManager.Instance?.PlaySFX("enemy_death", deathVolume, 0.95f + pitchOffset);
    }

    public void OnEnemyStagger()
    {
        AudioManager.Instance?.PlaySFX("enemy_stagger", staggerVolume, 1f + pitchOffset);
    }
}