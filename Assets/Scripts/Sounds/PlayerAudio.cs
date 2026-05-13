using UnityEngine;

/// <summary>
/// PlayerAudio — conector entre os eventos do player e o AudioManager.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///   Este script escuta os eventos existentes de PlayerHealth, PlayerBehavior
///   e MeleeWeapon e chama AudioManager.Instance.PlaySFX() no momento certo.
///   Nenhum script existente foi modificado.
///
/// ── SETUP ─────────────────────────────────────────────────────────────────
///
///   1. Adicione este componente no mesmo GameObject do PlayerBehavior.
///   2. Cadastre os clips no AudioManager com os nomes abaixo (ou mude as keys).
///   3. O MeleeWeapon precisa expor um evento — veja a seção ATAQUE abaixo.
///
/// ── KEYS ESPERADAS NO AUDIOMANAGER ──────────────────────────────────────────
///
///   Movimento:
///     "player_footstep"    — 2-4 variações de passo (array no Inspector)
///     "player_land"        — pouso no chão após pulo
///     "player_jump"        — pulo
///     "player_dash"        — dash
///
///   Combate:
///     "sword_light_1"      — ataque leve step 1
///     "sword_light_2"      — ataque leve step 2
///     "sword_light_3"      — ataque leve step 3
///     "sword_heavy"        — ataque pesado
///     "sword_finisher"     — finisher
///     "hit_enemy"          — impacto em inimigo (genérico)
///     "hit_enemy_heavy"    — impacto pesado
///     "hit_enemy_finisher" — impacto finisher
///
///   Defesa:
///     "parry_success"      — parry bem-sucedido (flash)
///     "player_block"       — bloqueio normal
///     "player_guard_crush" — guarda quebrada
///
///   Dano:
///     "player_hurt"        — player recebe dano
///     "player_death"       — player morre
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(PlayerBehavior))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerAudio : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Passos")]
    [Tooltip("Intervalo entre sons de passos (segundos). Sincronize com a animação.")]
    [SerializeField] private float footstepInterval = 0.30f;

    [Tooltip("Velocidade mínima horizontal para tocar o som de passo.\n" +
             "Deve ser parecida com o threshold do DustFootstep.")]
    [SerializeField] private float footstepMinSpeed = 0.5f;

    [Tooltip("Volume do som de passo (0–1).")]
    [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.6f;

    [Header("Aterrisagem")]
    [Tooltip("Volume do som de pouso.")]
    [Range(0f, 1f)] [SerializeField] private float landVolume = 0.8f;

    [Tooltip("Velocidade vertical mínima (para baixo) para tocar o som de land.\n" +
             "Evita som ao andar em rampas. Ex: -3 significa caindo rápido.")]
    [SerializeField] private float landMinFallSpeed = -3f;

    [Header("Dash")]
    [Range(0f, 1f)] [SerializeField] private float dashVolume = 0.9f;

    [Header("Pulo")]
    [Range(0f, 1f)] [SerializeField] private float jumpVolume = 0.7f;

    [Header("Combate")]
    [Range(0f, 1f)] [SerializeField] private float attackVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float hitVolume    = 0.9f;
    [Range(0f, 1f)] [SerializeField] private float parryVolume  = 1.0f;
    [Range(0f, 1f)] [SerializeField] private float hurtVolume   = 0.8f;

    // ── Referências ───────────────────────────────────────────────────────────

    private PlayerBehavior _behavior;
    private PlayerHealth   _health;

    // ── Estado interno ────────────────────────────────────────────────────────

    private float _footstepTimer;
    private bool  _wasGrounded;
    private float _verticalSpeedLastFrame;
    private bool  _wasDashing;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _behavior = GetComponent<PlayerBehavior>();
        _health   = GetComponent<PlayerHealth>();
    }

    private void OnEnable()
    {
        // Inscreve nos eventos do PlayerHealth
        _health.OnDamaged      += OnPlayerHurt;
        _health.OnDeath        += OnPlayerDeath;
        _health.OnParrySuccess += OnParrySuccess;
        _health.OnBlocked      += OnPlayerBlock;
        _health.OnGuardCrush   += OnGuardCrush;
    }

    private void OnDisable()
    {
        _health.OnDamaged      -= OnPlayerHurt;
        _health.OnDeath        -= OnPlayerDeath;
        _health.OnParrySuccess -= OnParrySuccess;
        _health.OnBlocked      -= OnPlayerBlock;
        _health.OnGuardCrush   -= OnGuardCrush;
    }

    private void Update()
    {
        HandleFootsteps();
        HandleLanding();
        HandleDash();
    }

    // ── Movimento ─────────────────────────────────────────────────────────────

    private void HandleFootsteps()
    {
        bool moving = _behavior.IsGrounded && _behavior.HorizontalSpeed >= footstepMinSpeed;

        if (!moving)
        {
            _footstepTimer = 0f;
            return;
        }

        _footstepTimer -= Time.deltaTime;
        if (_footstepTimer > 0f) return;

        AudioManager.Instance?.PlaySFX("player_footstep", footstepVolume);
        _footstepTimer = footstepInterval;
    }

    private void HandleLanding()
    {
        bool isGrounded    = _behavior.IsGrounded;
        bool justLanded    = isGrounded && !_wasGrounded;
        bool wasFallingFast = _verticalSpeedLastFrame <= landMinFallSpeed;

        if (justLanded && wasFallingFast)
            AudioManager.Instance?.PlaySFX("player_land", landVolume);

        _wasGrounded            = isGrounded;
        _verticalSpeedLastFrame = _behavior.VerticalSpeed;
    }

    private void HandleDash()
    {
        bool isDashing = _behavior.IsDashing;

        if (isDashing && !_wasDashing)
            AudioManager.Instance?.PlaySFX("player_dash", dashVolume);

        _wasDashing = isDashing;
    }

    // ── Pulo — chamado por Animation Event ou pelo CharacterAnimationController ──
    // Se preferir chamar via código, exponha este método como public e chame de
    // PlayerBehavior.ExecuteJump() ou de um Animation Event.
    public void OnJump()
    {
        AudioManager.Instance?.PlaySFX("player_jump", jumpVolume);
    }

    // ── Ataques — chamados por Animation Events na arma ───────────────────────
    //
    // Adicione Animation Events nos clips de ataque chamando estes métodos.
    // O MeleeWeapon já tem eventos de animação; use os mesmos frames ou
    // adicione eventos extras no início de cada swing.

    public void OnLightAttack1() => PlayAttack("sword_light_1");
    public void OnLightAttack2() => PlayAttack("sword_light_2");
    public void OnLightAttack3() => PlayAttack("sword_light_3");
    public void OnHeavyAttack()  => PlayAttack("sword_heavy");
    public void OnFinisher()     => PlayAttack("sword_finisher");

    private void PlayAttack(string key)
    {
        AudioManager.Instance?.PlaySFX(key, attackVolume);
    }

    // ── Hits no inimigo — chamados pelo MeleeWeapon via evento ───────────────
    //
    // O MeleeWeapon não expõe evento de hit. A forma mais limpa é:
    //
    //   OPÇÃO A (recomendada): adicione um evento em MeleeWeapon:
    //       public System.Action<string> OnHitConfirmed;
    //       // em ApplyHit, após hits.Length == 0: OnHitConfirmed?.Invoke(type);
    //   E inscreva aqui em OnEnable:
    //       _weapon.OnHitConfirmed += PlayHitSound;
    //
    //   OPÇÃO B (sem modificar MeleeWeapon): use Animation Events nos clipes
    //       chamando OnHitSoundLight(), OnHitSoundHeavy(), OnHitSoundFinisher().
    //
    // Por enquanto os métodos estão disponíveis para ambas as opções:

    public void OnHitSoundLight()    => AudioManager.Instance?.PlaySFX("hit_enemy",          hitVolume);
    public void OnHitSoundHeavy()    => AudioManager.Instance?.PlaySFX("hit_enemy_heavy",    hitVolume);
    public void OnHitSoundFinisher() => AudioManager.Instance?.PlaySFX("hit_enemy_finisher", hitVolume);

    // ── Defesa ────────────────────────────────────────────────────────────────

    private void OnParrySuccess()
    {
        AudioManager.Instance?.PlaySFX("parry_success", parryVolume);
    }

    private void OnPlayerBlock()
    {
        AudioManager.Instance?.PlaySFX("player_block", parryVolume * 0.8f);
    }

    private void OnGuardCrush()
    {
        AudioManager.Instance?.PlaySFX("player_guard_crush", hurtVolume);
    }

    // ── Dano / Morte ──────────────────────────────────────────────────────────

    private void OnPlayerHurt()
    {
        AudioManager.Instance?.PlaySFX("player_hurt", hurtVolume);
    }

    private void OnPlayerDeath()
    {
        AudioManager.Instance?.PlaySFX("player_death", hurtVolume);
    }
}