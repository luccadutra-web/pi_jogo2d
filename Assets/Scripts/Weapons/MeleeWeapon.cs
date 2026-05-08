using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MeleeWeapon — v4: cadeia de impact feel completa e sincronizada.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///  O mesmo padrão do parry (PlayerHealth) agora se aplica ao ataque inteiro.
///  A animação controla cada transição de fase; o código só reage aos sinais:
///
///     Frame de início da hitbox      →  OnAttackActiveStart()
///     Frame de fim   da hitbox       →  OnAttackActiveEnd()        (opcional*)
///     Frame de início do recovery    →  OnAttackRecoveryEnd()
///     Frame de abertura de combo     →  OnComboWindowOpen()        (apenas Light)
///     Frame de fechamento de combo   →  OnComboWindowClose()       (opcional**)
///
///  (*) Se OnAttackActiveEnd não for adicionado, o fallback activeTime é usado.
///  (**) Se OnComboWindowClose não for adicionado, o timer comboWindowDuration
///       ainda fecha a janela como segurança — igual ao modo legado.
///
/// ── CADEIA DE IMPACT FEEL (ordem de disparo em ApplyHit) ─────────────────────
///
///   1. Hitlag         — freeze do Rigidbody do inimigo (duração por tipo)
///   2. ImpactFlash    — frame branco no inimigo (intensidade por tipo)
///   3. HitStop        — freeze global de timeScale
///   4. CameraShake    — APÓS o HitStop terminar (ShakeAfterHitStop coroutine)
///   5. CameraImpulse  — micro zoom em TODOS os golpes (DoZoom público)
///
///   CameraShake durante HitStop é invisível pois a tela está congelada.
///   Por isso o shake é sempre adiado via ShakeAfterHitStop().
///
/// ── HITLAG POR TIPO ───────────────────────────────────────────────────────────
///
///   Light    → lightHitlagDuration   (curto — mantém ritmo do combo)
///   Heavy    → heavyHitlagDuration   (longo — reforça peso)
///   Finisher → finisherHitlagDuration (muito longo — dramático)
///   Counter  → 0                     (stagger já cobre o freeze do inimigo)
///
/// ── SETUP NO ANIMATOR ────────────────────────────────────────────────────────
///
///   Em cada clipe de ataque (LightAttack, HeavyAttack, Finisher, CounterAttack):
///
///     Início da hitbox    →  Animation Event → OnAttackActiveStart
///     Fim da hitbox       →  Animation Event → OnAttackActiveEnd
///     Fim do recovery     →  Animation Event → OnAttackRecoveryEnd
///
///   Nos clipes LightAttack (combo):
///     Abertura da janela  →  Animation Event → OnComboWindowOpen
///     Fechamento (opt.)   →  Animation Event → OnComboWindowClose
///
///   Para finisher, a janela de finisher segue o mesmo padrão com
///   OnComboWindowOpen / OnComboWindowClose no clipe do 3º light.
///
///   O componente receptor deve ser o MeleeWeapon (arraste o GameObject da arma
///   ou do player, dependendo de onde o componente está, no campo do evento).
///
/// ── MODO LEGADO ──────────────────────────────────────────────────────────────
///
///   attackDrivenByAnimation = false  →  comportamento original com timers.
///   Útil para testar ou para ataques sem animação ainda configurada.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Modo de sincronização")]
    [Tooltip("Se true, o hit é aplicado pelo Animation Event OnAttackActiveStart.\n" +
             "Se false, usa os timers de startup do Inspector (modo legado).")]
    [SerializeField] private bool attackDrivenByAnimation = true;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange = 1.5f;
    [SerializeField] private int lightDamage = 1;
    [SerializeField] private float lightHitStop = 0.04f;
    [SerializeField] private float lightShakeIntensity = 0.08f;
    [SerializeField] private float lightShakeDuration = 0.10f;

    [Header("Heavy independente")]
    [SerializeField] private float heavyAttackRange = 2.0f;
    [SerializeField] private int heavyDamage = 3;
    [SerializeField] private float heavyHitStop = 0.14f;
    [SerializeField] private float heavyStaminaCost = 25f;
    [SerializeField] private float heavyCooldown = 1.0f;
    [SerializeField] private float heavyShakeIntensity = 0.15f;
    [SerializeField] private float heavyShakeDuration = 0.18f;

    [Header("Finalizador (combo x3 + heavy)")]
    [SerializeField] private float finisherAttackRange = 2.5f;
    [SerializeField] private int finisherDamage = 6;
    [SerializeField] private float finisherHitStop = 0.28f;
    [SerializeField] private float finisherStaminaCost = 30f;
    [SerializeField] private float finisherShakeIntensity = 0.22f;
    [SerializeField] private float finisherShakeDuration = 0.22f;

    [Header("Janela de combo")]
    [SerializeField] private float comboWindowDuration = 0.45f;
    [SerializeField] private float finisherWindowDuration = 0.55f;

    [Header("Input buffer")]
    [SerializeField] private float inputBufferWindow = 0.10f;

    [Header("Timings — usados no modo legado e como fallback de segurança")]
    [Tooltip("Startup: tempo até o hit (usado no modo legado)")]
    [SerializeField] private float lightStartup = 0.08f;
    [SerializeField] private float lightActiveTime = 0.08f;
    [SerializeField] private float lightRecovery = 0.18f;
    [SerializeField] private float heavyStartup = 0.20f;
    [SerializeField] private float heavyActiveTime = 0.12f;
    [SerializeField] private float heavyRecovery = 0.40f;
    [SerializeField] private float finisherStartup = 0.28f;
    [SerializeField] private float finisherActiveTime = 0.15f;
    [SerializeField] private float finisherRecovery = 0.55f;

    [Header("Fallback — modo animação")]
    [Tooltip("Tempo máximo de espera pelo Animation Event OnAttackActiveStart.\n" +
             "Se a animação não disparar o evento nesse tempo, o hit é aplicado assim mesmo.\n" +
             "Deve ser maior que o startup mais longo (finisher = ~0.28s). Recomendado: 0.6s.")]
    [SerializeField] private float animEventTimeout = 0.6f;
    [Tooltip("Tempo máximo de espera pelo Animation Event OnAttackRecoveryEnd.\n" +
             "Se não vier, encerra o recovery com base no timer de fallback (recovery + margem).\n" +
             "Recomendado: maior que o recovery mais longo (finisher = ~0.55s). Recomendado: 1.2s.")]
    [SerializeField] private float recoveryEventTimeout = 1.2f;

    [Header("Hitlag direcional")]
    [Tooltip("Light: curto para manter o ritmo do combo.")]
    [SerializeField] private float lightHitlagDuration    = 0.04f;
    [Tooltip("Heavy: longo para reforçar o peso do golpe.")]
    [SerializeField] private float heavyHitlagDuration    = 0.10f;
    [Tooltip("Finisher: máximo — dramático e conclusivo.")]
    [SerializeField] private float finisherHitlagDuration = 0.18f;
    // Counter não tem hitlag — o stagger do inimigo já cobre o freeze.
    [Range(0f, 1f)]
    [SerializeField] private float hitlagVelocityRetention = 0.15f;

    [Header("Counter attack")]
    [SerializeField] private float counterImpactMultiplier = 2f;

    [Header("Micro zoom de câmera por golpe")]
    [Tooltip("Zoom in ao acertar um light attack. 0 = desativado.")]
    [SerializeField] private float lightZoomAmount    = 0.02f;
    [Tooltip("Zoom in ao acertar um heavy attack.")]
    [SerializeField] private float heavyZoomAmount    = 0.05f;
    [Tooltip("Zoom in ao acertar o finisher.")]
    [SerializeField] private float finisherZoomAmount = 0.10f;
    [Tooltip("Duração do zoom in para golpes normais (segundos reais).")]
    [SerializeField] private float attackZoomInTime   = 0.04f;
    [Tooltip("Duração do zoom out para golpes normais (segundos reais).")]
    [SerializeField] private float attackZoomOutTime  = 0.12f;

    [Header("Dano de postura (Poise)")]
    [SerializeField] private float lightPoiseDamage = 20f;
    [SerializeField] private float heavyPoiseDamage = 45f;
    [SerializeField] private float finisherPoiseDamage = 999f;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private enum AttackType { Light, Heavy, Finisher }

    private int comboStep = 0;
    private bool isAttacking = false;
    private bool comboWindowOpen = false;
    private bool finisherWindowOpen = false;
    private float comboWindowTimer;
    private float finisherWindowTimer;
    private float heavyCooldownTimer;

    private bool lightBuffered;
    private bool heavyBuffered;
    private float lightBufferTimer;
    private float heavyBufferTimer;
    private bool _justStartedAttack;

    // Sinais dos Animation Events para a coroutine
    private bool _animEventActiveStart = false;
    private bool _animEventActiveEnd = false;
    private bool _animEventRecoveryEnd = false;
    private bool _animEventComboOpen = false;
    private bool _animEventComboClose = false;

    // Tipo do ataque em execução (para ApplyHit saber o tipo quando chamado pelo evento)
    private AttackType _currentAttackType;
    private bool _currentIsCounter;

    private const string TriggerLight = "LightAttack";
    private const string TriggerHeavy = "HeavyAttack";
    private const string TriggerFinisher = "Finisher";
    private const string TriggerCounter = "CounterAttack";

    // Nomes dos states no Animator — devem bater exatamente com o grafo (case-sensitive).
    // Se o state não existir, AttackRoutine cai automaticamente nos timers legados.
    private const string StateLight = "light_attack";
    private const string StateHeavy = "heavy_attack";
    private const string StateFinisher = "Finisher";
    private const string StateCounter = "CounterAttack";
    private const string StateIdle = "idle";

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerBehavior behavior;
    private PlayerStamina stamina;
    private PlayerHealth health;
    private CharacterAnimationController animController;
    private Animator _animator;

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Awake()
    {
        lightAction = new InputAction("Light", InputActionType.Button);
        lightAction.AddBinding("<Mouse>/leftButton");

        heavyAction = new InputAction("Heavy", InputActionType.Button);
        heavyAction.AddBinding("<Mouse>/rightButton");
    }

    void Start()
    {
        behavior = GetComponentInParent<PlayerBehavior>();
        stamina = GetComponentInParent<PlayerStamina>();
        health = GetComponentInParent<PlayerHealth>();
        animController = GetComponentInParent<CharacterAnimationController>();
        _animator = GetComponentInParent<Animator>();

        if (animController == null)
            Debug.LogError("[MeleeWeapon] CharacterAnimationController não encontrado no pai.");
    }

    void OnEnable() { lightAction.Enable(); heavyAction.Enable(); }

    void OnDisable()
    {
        lightAction.Disable();
        heavyAction.Disable();

        if (behavior != null)
        {
            behavior.isAttacking = false;
            behavior.isInAttackStartupOrActive = false;
        }
    }

    void Update()
    {
        if (behavior != null && behavior.IsDashing) return;

        _justStartedAttack = false;

        ReadInput();
        UpdateTimers();

        if (!isAttacking && !_justStartedAttack)
            TryConsumeBuffer();
    }

    // ─── Animation Events ─────────────────────────────────────────────────────

    /// <summary>
    /// Chame via Animation Event no frame em que a arma entra na zona de hit.
    /// A coroutine aguarda este sinal para aplicar o dano.
    /// </summary>
    public void OnAttackActiveStart()
    {
        _animEventActiveStart = true;
    }

    /// <summary>
    /// Chame via Animation Event no frame em que a arma sai da zona de hit.
    /// A coroutine usa este sinal para encerrar o estado active.
    /// Opcional: se não adicionado, o fallback activeTime do Inspector é usado.
    /// </summary>
    public void OnAttackActiveEnd()
    {
        _animEventActiveEnd = true;
    }

    /// <summary>
    /// Chame via Animation Event no frame em que o recovery termina
    /// (último frame antes de Idle ou antes da janela de combo se abrir).
    /// A coroutine usa este sinal para encerrar o ataque e consumir o buffer.
    /// Opcional: se não adicionado, o fallback recoveryEventTimeout é usado.
    /// </summary>
    public void OnAttackRecoveryEnd()
    {
        _animEventRecoveryEnd = true;
    }

    /// <summary>
    /// Chame via Animation Event no frame em que a janela de combo deve abrir
    /// (nos clipes LightAttack). Substitui a abertura por timer fixo.
    /// </summary>
    public void OnComboWindowOpen()
    {
        _animEventComboOpen = true;
    }

    /// <summary>
    /// Chame via Animation Event no frame em que a janela de combo deve fechar.
    /// Opcional: se não adicionado, o timer comboWindowDuration/finisherWindowDuration
    /// ainda fecha a janela como segurança.
    /// </summary>
    public void OnComboWindowClose()
    {
        _animEventComboClose = true;
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        if (lightAction.WasPressedThisFrame())
        {
            lightBuffered = true;
            lightBufferTimer = inputBufferWindow;
        }

        if (heavyAction.WasPressedThisFrame())
        {
            heavyBuffered = true;
            heavyBufferTimer = inputBufferWindow;
        }
    }

    private void UpdateTimers()
    {
        if (lightBufferTimer > 0f) { lightBufferTimer -= Time.deltaTime; if (lightBufferTimer <= 0f) lightBuffered = false; }
        if (heavyBufferTimer > 0f) { heavyBufferTimer -= Time.deltaTime; if (heavyBufferTimer <= 0f) heavyBuffered = false; }

        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f && !isAttacking) { comboWindowOpen = false; comboStep = 0; }
        }

        if (finisherWindowTimer > 0f)
        {
            finisherWindowTimer -= Time.deltaTime;
            if (finisherWindowTimer <= 0f && !isAttacking) { finisherWindowOpen = false; comboStep = 0; }
        }

        if (heavyCooldownTimer > 0f) heavyCooldownTimer -= Time.deltaTime;
    }

    private void TryConsumeBuffer()
    {
        if (heavyBuffered && finisherWindowOpen)
        {
            heavyBuffered = false; heavyBufferTimer = 0f;
            finisherWindowOpen = false; comboWindowOpen = false;
            ExecuteFinisher();
            return;
        }

        if (lightBuffered)
        {
            lightBuffered = false; lightBufferTimer = 0f;

            if (comboWindowOpen && comboStep < 3)
            {
                comboWindowOpen = false;
                ExecuteLightAttack();
                return;
            }

            if (!comboWindowOpen && !isAttacking)
            {
                comboStep = 0;
                ExecuteLightAttack();
                return;
            }
        }

        if (heavyBuffered && !finisherWindowOpen)
        {
            if (heavyCooldownTimer > 0f) { heavyBuffered = false; return; }
            heavyBuffered = false; heavyBufferTimer = 0f;
            comboStep = 0; comboWindowOpen = false; finisherWindowOpen = false;
            ExecuteHeavyAttack();
        }
    }

    // ─── Execute ──────────────────────────────────────────────────────────────

    private void ExecuteLightAttack()
    {
        comboStep++;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Light));
    }

    private void ExecuteHeavyAttack()
    {
        if (stamina != null && !stamina.Spend(heavyStaminaCost)) return;
        heavyCooldownTimer = heavyCooldown;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Heavy));
    }

    private void ExecuteFinisher()
    {
        if (stamina != null && !stamina.Spend(finisherStaminaCost)) { comboStep = 0; return; }
        comboStep = 0;
        _justStartedAttack = true;
        StartCoroutine(AttackRoutine(AttackType.Finisher));
    }

    // ─── Rotina de ataque ─────────────────────────────────────────────────────

    /// <summary>
    /// Retorna true se o Animator está no state de nome <paramref name="stateName"/>
    /// na Base Layer. Usado para detectar se um clipe existe antes de aguardar eventos.
    /// </summary>
    private bool IsInState(string stateName)
    {
        if (_animator == null) return false;
        return _animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
    }

    private IEnumerator AttackRoutine(AttackType type)
    {
        isAttacking = true;

        float startup = type == AttackType.Light ? lightStartup : type == AttackType.Heavy ? heavyStartup : finisherStartup;
        float activeTime = type == AttackType.Light ? lightActiveTime : type == AttackType.Heavy ? heavyActiveTime : finisherActiveTime;
        float recovery = type == AttackType.Light ? lightRecovery : type == AttackType.Heavy ? heavyRecovery : finisherRecovery;
        string trigger = type == AttackType.Light ? TriggerLight : type == AttackType.Heavy ? TriggerHeavy : TriggerFinisher;
        string stateName = type == AttackType.Light ? StateLight : type == AttackType.Heavy ? StateHeavy : StateFinisher;

        // Verifica counter antes de setar o trigger
        bool isCounter = health != null && health.IsCounterWindowOpen;
        if (isCounter)
        {
            // Sem animação de counter dedicada — reutiliza o heavy attack.
            // O dano e os efeitos de impacto ainda aplicam o multiplicador de counter.
            animController?.SetTriggerDirect(TriggerHeavy);
            CameraImpulse.Instance?.CounterZoom();
            stateName = StateHeavy;
        }
        else
            animController?.SetTriggerDirect(trigger);

        if (behavior != null)
        {
            behavior.isAttacking = true;
            behavior.isInAttackStartupOrActive = true;
        }

        _currentAttackType = type;
        _currentIsCounter = isCounter;

        // ── Detecta se o clipe existe ─────────────────────────────────────────
        //
        // Aguarda 1 frame para o Animator processar o trigger e fazer a transição.
        // Se o state ativo não for o esperado, o clipe não existe no grafo —
        // todas as fases usam timers legados sem emitir warnings de timeout.

        yield return null;
        bool stateExists = IsInState(stateName);

        if (!stateExists)
            Debug.Log($"[MeleeWeapon] State '{stateName}' não encontrado no Animator — usando timers legados para {type}.");

        // ── Fase de Startup ───────────────────────────────────────────────────

        if (attackDrivenByAnimation && stateExists)
        {
            _animEventActiveStart = false;
            float elapsed = 0f;
            while (!_animEventActiveStart && elapsed < animEventTimeout)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_animEventActiveStart)
                Debug.LogWarning($"[MeleeWeapon] Timeout aguardando OnAttackActiveStart ({type}). Adicione o Animation Event no clipe '{stateName}'.");
        }
        else
        {
            yield return new WaitForSecondsRealtime(startup);
        }

        // ── Fase Active: aplica o hit ─────────────────────────────────────────

        ApplyHit(type, isCounter);

        if (isCounter && health != null)
            health.ConsumeCounterWindow();

        // ── Fim do Active ─────────────────────────────────────────────────────

        if (attackDrivenByAnimation && stateExists)
        {
            _animEventActiveEnd = false;
            float elapsed = 0f;
            while (!_animEventActiveEnd && elapsed < activeTime + 0.1f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(activeTime);
        }

        // Aguarda o HitStop terminar antes de liberar o movimento do player.
        //
        // Por que WaitForEndOfFrame e não yield return null:
        //   Com Time.timeScale = 0, "yield return null" não avança — a coroutine
        //   fica presa até o tempo ser restaurado, que é exatamente o que não
        //   queremos (o freeze nunca terminaria de forma controlada).
        //   WaitForEndOfFrame avança independente do timeScale, permitindo que
        //   o loop verifique IsActive a cada frame real até o freeze acabar.
        //
        // Por que liberar DEPOIS do freeze e não antes:
        //   Se liberarmos behavior.isAttacking = false enquanto timeScale = 0,
        //   o player já pode se mover durante o congelamento — o que quebra
        //   a leitura visual do impacto. O freeze deve durar inteiro antes de
        //   devolver o controle.
        if (HitStop.Instance != null)
            while (HitStop.Instance.IsActive)
                yield return new WaitForEndOfFrame();

        if (behavior != null)
        {
            behavior.isInAttackStartupOrActive = false;
            behavior.isAttacking = false;
        }

        // ── Abertura da janela de combo ───────────────────────────────────────

        if (type == AttackType.Light)
        {
            bool dashedDuringComboWait = false;

            if (attackDrivenByAnimation && stateExists)
            {
                _animEventComboOpen = false;
                _animEventComboClose = false;
                float elapsed = 0f;
                while (!_animEventComboOpen && elapsed < recoveryEventTimeout)
                {
                    if (behavior != null && behavior.IsDashing) { dashedDuringComboWait = true; break; }
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!_animEventComboOpen && !dashedDuringComboWait)
                    Debug.LogWarning($"[MeleeWeapon] Timeout aguardando OnComboWindowOpen (comboStep={comboStep}). Adicione o Animation Event no clipe '{stateName}'.");
            }

            if (!dashedDuringComboWait)
            {
                if (comboStep < 3) { comboWindowOpen = true; comboWindowTimer = comboWindowDuration; }
                else { finisherWindowOpen = true; finisherWindowTimer = finisherWindowDuration; }
            }
        }

        // ── Recovery ──────────────────────────────────────────────────────────

        if (attackDrivenByAnimation && stateExists)
        {
            _animEventRecoveryEnd = false;
            float elapsed = 0f;
            while (!_animEventRecoveryEnd && elapsed < recoveryEventTimeout)
            {
                if (behavior != null && behavior.IsDashing) break;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_animEventRecoveryEnd && !(behavior != null && behavior.IsDashing))
                Debug.LogWarning($"[MeleeWeapon] Timeout aguardando OnAttackRecoveryEnd ({type}). Adicione o Animation Event no clipe '{stateName}'.");
        }
        else
        {
            float recoveryElapsed = 0f;
            while (recoveryElapsed < recovery)
            {
                if (behavior != null && behavior.IsDashing) break;
                recoveryElapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        isAttacking = false;
        TryConsumeBuffer();

        if (!isAttacking)
        {
            comboStep = 0;
            animController?.ForceState(StateIdle);
        }
    }

    // ─── Aplicação do dano ────────────────────────────────────────────────────

    private void ApplyHit(AttackType type, bool isCounter)
    {
        float range   = type == AttackType.Light ? lightAttackRange   : type == AttackType.Heavy ? heavyAttackRange   : finisherAttackRange;
        int   baseDmg = type == AttackType.Light ? lightDamage        : type == AttackType.Heavy ? heavyDamage        : finisherDamage;
        float hitStop = type == AttackType.Light ? lightHitStop       : type == AttackType.Heavy ? heavyHitStop       : finisherHitStop;
        float shakeI  = type == AttackType.Light ? lightShakeIntensity: type == AttackType.Heavy ? heavyShakeIntensity: finisherShakeIntensity;
        float shakeD  = type == AttackType.Light ? lightShakeDuration : type == AttackType.Heavy ? heavyShakeDuration : finisherShakeDuration;

        int   damage     = isCounter ? Mathf.RoundToInt(baseDmg * (health != null ? health.CounterDamageMultiplier : 2f)) : baseDmg;
        float finalStop  = isCounter ? hitStop * counterImpactMultiplier : hitStop;
        float finalShakeI= isCounter ? shakeI  * counterImpactMultiplier : shakeI;

        Collider2D[] hits = Physics2D.OverlapCircleAll(attackPoint.position, range, enemyLayer);
        if (hits.Length == 0) return;

        // ── 1. Hitlag — freeze do Rigidbody do inimigo ───────────────────────
        // Counter não tem hitlag: o stagger já gerencia o freeze do inimigo.
        float hitlagDur = isCounter ? 0f
                        : type == AttackType.Light    ? lightHitlagDuration
                        : type == AttackType.Heavy    ? heavyHitlagDuration
                        : finisherHitlagDuration;

        foreach (Collider2D hit in hits)
        {
            Rigidbody2D enemyRb = hit.GetComponent<Rigidbody2D>() ?? hit.GetComponentInParent<Rigidbody2D>();
            if (enemyRb != null && hitlagDur > 0f)
                StartCoroutine(HitlagRoutine(enemyRb, hitlagDur));

            // ── 2. ImpactFlash — frame branco no inimigo ─────────────────────
            var hitFlash = hit.GetComponent<HitFlash>() ?? hit.GetComponentInParent<HitFlash>();
            hitFlash?.ImpactFlash();

            // ── Dano e poise ──────────────────────────────────────────────────
            if (type == AttackType.Heavy)
            {
                var playerDmg  = hit.GetComponent<PlayerHealth>() ?? hit.GetComponentInParent<PlayerHealth>();
                var damageable = hit.GetComponent<IDamageable>()  ?? hit.GetComponentInParent<IDamageable>();
                if (playerDmg != null) playerDmg.TakeDamageHeavy(damage, transform.position);
                else                   damageable?.TakeDamage(damage, transform.position);
            }
            else
            {
                var damageable = hit.GetComponent<IDamageable>() ?? hit.GetComponentInParent<IDamageable>();
                damageable?.TakeDamage(damage, transform.position);
            }

            float poiseDmg = type == AttackType.Light ? lightPoiseDamage : type == AttackType.Heavy ? heavyPoiseDamage : finisherPoiseDamage;
            var enemyPoise = hit.GetComponent<EnemyPoise>() ?? hit.GetComponentInParent<EnemyPoise>();
            enemyPoise?.ReceivePoiseHit(poiseDmg);
        }

        // ── 3. HitStop — freeze global de timeScale ───────────────────────────
        HitStop.Instance?.DoHitStop(finalStop);

        // ── 4. CameraShake — adiado para APÓS o HitStop terminar ─────────────
        // Chamar Shake durante timeScale=0 é invisível: a câmera treme enquanto
        // tudo está congelado. ShakeAfterHitStop aguarda o freeze e só então treme.
        StartCoroutine(ShakeAfterHitStop(finalShakeI, shakeD));

        // ── 5. CameraImpulse — micro zoom em todos os golpes ─────────────────
        // Counter já chama CounterZoom() em AttackRoutine — não duplica aqui.
        if (!isCounter)
        {
            float zoom = type == AttackType.Light    ? lightZoomAmount
                       : type == AttackType.Heavy    ? heavyZoomAmount
                       : finisherZoomAmount;
            if (zoom > 0f)
                CameraImpulse.Instance?.AttackZoom(zoom, attackZoomInTime, attackZoomOutTime);
        }
    }

    // ─── Shake adiado ─────────────────────────────────────────────────────────

    /// <summary>
    /// Aguarda o HitStop terminar antes de tremer a câmera.
    /// Durante timeScale=0 a câmera treme mas o freeze cobre tudo — o player
    /// não vê nada. O shake deve ocorrer quando a imagem volta a se mover.
    /// </summary>
    private IEnumerator ShakeAfterHitStop(float intensity, float duration)
    {
        while (HitStop.Instance != null && HitStop.Instance.IsActive)
            yield return new WaitForEndOfFrame();

        CameraShake.Instance?.Shake(intensity, duration);
    }

    // ─── Hitlag ───────────────────────────────────────────────────────────────

    private IEnumerator HitlagRoutine(Rigidbody2D enemyRb, float duration)
    {
        if (enemyRb == null) yield break;

        float savedGravity = enemyRb.gravityScale;
        Vector2 savedVel   = enemyRb.linearVelocity;

        enemyRb.gravityScale   = 0f;
        enemyRb.linearVelocity = Vector2.zero;

        yield return new WaitForSecondsRealtime(duration);

        if (enemyRb == null) yield break;
        enemyRb.gravityScale   = savedGravity;
        enemyRb.linearVelocity = savedVel * hitlagVelocityRetention;
    }

    // ─── Gizmo ────────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        if (!attackPoint) return;
        Gizmos.color = new Color(1f, 0.8f, 0.8f, 0.4f);
        Gizmos.DrawWireSphere(attackPoint.position, lightAttackRange);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(attackPoint.position, heavyAttackRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.7f);
        Gizmos.DrawWireSphere(attackPoint.position, finisherAttackRange);
    }
}