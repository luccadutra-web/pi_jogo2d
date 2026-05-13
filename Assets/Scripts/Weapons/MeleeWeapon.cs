using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// MeleeWeapon — v6: Timeouts em todos os loops de Animation Event + logs de diagnóstico.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///  A animação controla cada transição de fase; o código só reage aos sinais:
///
///     Frame de início da hitbox      →  OnAttackActiveStart()
///     Frame de fim   da hitbox       →  OnAttackActiveEnd()
///     Fim do recovery / volta ao idle →  OnAttackRecoveryEnd()
///     Abertura da janela de combo    →  OnComboWindowOpen()   (apenas Light)
///     Fechamento da janela de combo  →  OnComboWindowClose()  (opcional)
///
///  Todos os eventos acima DEVEM estar configurados nos clipes do Animator.
///  A partir desta versão, qualquer evento faltando gera um warning claro no
///  console e a rotina continua via timeout — o player não trava mais.
///
/// ── LOGS DE DIAGNÓSTICO ──────────────────────────────────────────────────────
///
///  Todos os logs usam o prefixo [MeleeWeapon] e incluem:
///    - tipo de ataque e comboStep
///    - fase da rotina (Startup / Active / ComboWindow / Recovery)
///    - se o evento chegou normalmente ou por timeout/escape
///
///  Para filtrar no Console do Unity: filtre por "[MeleeWeapon]"
///
/// ── CADEIA DE IMPACT FEEL (ordem de disparo em ApplyHit) ─────────────────────
///
///   1. Hitlag         — freeze do Rigidbody do inimigo (duração por tipo)
///   2. ImpactFlash    — frame branco no inimigo (intensidade por tipo)
///   3. HitStop        — freeze global de timeScale
///   4. CameraShake    — APÓS o HitStop terminar (ShakeAfterHitStop coroutine)
///   5. CameraImpulse  — micro zoom em TODOS os golpes (DoZoom público)
///
/// ── HITLAG POR TIPO ───────────────────────────────────────────────────────────
///
///   Light    → lightHitlagPerStep[comboStep-1]
///   Heavy    → heavyHitlagDuration
///   Finisher → finisherHitlagDuration
///   Counter  → 0  (stagger cobre o freeze)
///
/// ── SETUP NO ANIMATOR ────────────────────────────────────────────────────────
///
///   Em cada clipe de ataque (LightAttack, HeavyAttack, Finisher, CounterAttack):
///     Início da hitbox    →  Animation Event → OnAttackActiveStart
///     Fim da hitbox       →  Animation Event → OnAttackActiveEnd
///     Fim do recovery     →  Animation Event → OnAttackRecoveryEnd
///
///   Nos clipes LightAttack (combo):
///     Abertura da janela  →  Animation Event → OnComboWindowOpen
///     Fechamento (opt.)   →  Animation Event → OnComboWindowClose
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class MeleeWeapon : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private Transform attackPoint;
    [SerializeField] private LayerMask enemyLayer;

    [Header("VFX")]
    [Tooltip("FeedbackFxPlayer do player. Deixe vazio para buscar no pai.")]
    [SerializeField] private VfxManager vfxManager;

    [Header("Ataque leve")]
    [SerializeField] private float lightAttackRange = 1.5f;
    [SerializeField] private int lightDamage = 1;

    [Header("Ataque leve — progressão por step do combo")]
    [Tooltip("HitStop por step (step 1, 2, 3). Valores crescentes criam sensação de peso acumulado.")]
    [SerializeField] private float[] lightHitStopPerStep        = { 0.03f, 0.05f, 0.07f };
    [Tooltip("Intensidade do camera shake por step.")]
    [SerializeField] private float[] lightShakeIntensityPerStep = { 0.06f, 0.09f, 0.12f };
    [Tooltip("Duração do camera shake por step.")]
    [SerializeField] private float[] lightShakeDurationPerStep  = { 0.12f, 0.14f, 0.16f };
    [Tooltip("Duração do hitlag no inimigo por step.")]
    [SerializeField] private float[] lightHitlagPerStep         = { 0.03f, 0.05f, 0.07f };

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

    [Header("Hitlag direcional")]
    [Tooltip("Heavy: longo para reforçar o peso do golpe.")]
    [SerializeField] private float heavyHitlagDuration    = 0.10f;
    [Tooltip("Finisher: máximo — dramático e conclusivo.")]
    [SerializeField] private float finisherHitlagDuration = 0.18f;
    [Range(0f, 1f)]
    [SerializeField] private float hitlagVelocityRetention = 0.15f;

    [Header("Knockback pós-hitlag")]
    [SerializeField] private bool  knockbackEnabled = true;
    [SerializeField] private float lightKnockbackForce    = 5f;
    [SerializeField] private float heavyKnockbackForce    = 10f;
    [SerializeField] private float finisherKnockbackForce = 16f;
    [SerializeField] private float lightKnockbackDuration    = 0.10f;
    [SerializeField] private float heavyKnockbackDuration    = 0.14f;
    [SerializeField] private float finisherKnockbackDuration = 0.18f;
    [SerializeField] private float knockbackVerticalFraction = 0.2f;

    [Header("Counter attack")]
    [SerializeField] private float counterImpactMultiplier = 2f;
    [SerializeField] private float counterKnockbackForce    = 18f;
    [SerializeField] private float counterKnockbackDuration = 0.20f;
    [SerializeField] private float counterSlowScale    = 0.08f;
    [SerializeField] private float counterSlowHold     = 0.12f;
    [SerializeField] private float counterSlowRampUp   = 0.20f;

    [Header("Camera shake direcional")]
    [Range(0f, 1f)]
    [SerializeField] private float shakeDirectionBias = 0.65f;

    [Header("Micro zoom de câmera por golpe")]
    [SerializeField] private float lightZoomAmount    = 0.02f;
    [SerializeField] private float heavyZoomAmount    = 0.05f;
    [SerializeField] private float finisherZoomAmount = 0.10f;
    [SerializeField] private float attackZoomInTime   = 0.04f;
    [SerializeField] private float attackZoomOutTime  = 0.12f;

    [Header("Dano de postura (Poise)")]
    [HideInInspector] [SerializeField] private float lightPoiseDamage    = 20f;
    [HideInInspector] [SerializeField] private float heavyPoiseDamage    = 45f;
    [HideInInspector] [SerializeField] private float finisherPoiseDamage = 999f;

    [Header("Anticipation squash")]
    [SerializeField] private bool  anticipationEnabled = true;
    [SerializeField] private float lightAnticipationScaleX    = 0.88f;
    [SerializeField] private float heavyAnticipationScaleX    = 0.82f;
    [SerializeField] private float finisherAnticipationScaleX = 0.78f;
    [SerializeField] private float lightAnticipationDuration    = 0.06f;
    [SerializeField] private float heavyAnticipationDuration    = 0.09f;
    [SerializeField] private float finisherAnticipationDuration = 0.10f;

    // ── Finalizador — guard de segurança ─────────────────────────────────────
    [Header("Finalizador — disponibilidade")]
    [Tooltip("Desative enquanto a animação do finalizador não existir no Animator.\n" +
             "Com false: o combo de 3 lights termina normalmente sem tentar entrar\n" +
             "no state 'Finisher', e o heavy after-combo é ignorado com segurança.")]
    [SerializeField] private bool finisherEnabled = false;

    // ── Timeouts de segurança ─────────────────────────────────────────────────
    // Evitam que loops de Animation Event travem o player se um evento estiver
    // faltando ou mal configurado no clipe. Ajuste conforme a duração das animações.
    [Header("Timeouts de segurança (Animation Events)")]
    [Tooltip("Tempo máximo (s) esperando OnAttackActiveStart antes de continuar forçado.")]
    [SerializeField] private float timeoutActiveStart  = 1.5f;
    [Tooltip("Tempo máximo (s) esperando OnAttackActiveEnd antes de continuar forçado.")]
    [SerializeField] private float timeoutActiveEnd    = 1.5f;
    [Tooltip("Tempo máximo (s) esperando OnComboWindowOpen antes de continuar forçado.")]
    [SerializeField] private float timeoutComboOpen    = 1.5f;
    [Tooltip("Tempo máximo (s) esperando OnAttackRecoveryEnd antes de continuar forçado.")]
    [SerializeField] private float timeoutRecoveryEnd  = 2.0f;

    // ─── Estado interno ───────────────────────────────────────────────────────

    private enum AttackType { Light, Heavy, Finisher }

    private int   comboStep = 0;
    private bool  isAttacking = false;
    private Coroutine _attackCoroutine;
    private bool  comboWindowOpen = false;
    private bool  finisherWindowOpen = false;
    private float comboWindowTimer;
    private float finisherWindowTimer;
    private float heavyCooldownTimer;

    private bool  lightBuffered;
    private bool  heavyBuffered;
    private float lightBufferTimer;
    private float heavyBufferTimer;
    private bool  _justStartedAttack;

    // Sinais dos Animation Events para a coroutine
    private bool _animEventActiveStart = false;
    private bool _animEventActiveEnd   = false;
    private bool _animEventRecoveryEnd = false;
    private bool _animEventComboOpen   = false;
    private bool _animEventComboClose  = false;

    private AttackType _currentAttackType;
    private bool _currentIsCounter;

    private static readonly string[] TriggersLight = { "LightAttack1", "LightAttack2", "LightAttack3" };
    private const string TriggerHeavy    = "HeavyAttack";
    private const string TriggerFinisher = "Finisher";
    private const string TriggerCounter  = "CounterAttack";

    private static readonly string[] StatesLight = { "light_attack_1", "light_attack_2", "light_attack_3" };
    private const string StateHeavy    = "heavy_attack";
    private const string StateFinisher = "Finisher";
    private const string StateCounter  = "CounterAttack";
    private const string StateIdle     = "idle";

    private InputAction lightAction;
    private InputAction heavyAction;

    private PlayerBehavior behavior;
    private PlayerStamina  stamina;
    private PlayerHealth   health;
    private CharacterAnimationController animController;

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
        behavior       = GetComponentInParent<PlayerBehavior>();
        stamina        = GetComponentInParent<PlayerStamina>();
        health         = GetComponentInParent<PlayerHealth>();
        animController = GetComponentInParent<CharacterAnimationController>();

        // ── Diagnóstico de setup ──────────────────────────────────────────────
        if (animController == null)
            Debug.LogError("[MeleeWeapon] CharacterAnimationController não encontrado no pai. " +
                           "Animation Events NÃO chegarão a MeleeWeapon.");

        if (behavior == null)
            Debug.LogError("[MeleeWeapon] PlayerBehavior não encontrado no pai. " +
                           "Flags de movimento não serão controladas.");

        if (attackPoint == null)
            Debug.LogWarning("[MeleeWeapon] attackPoint não atribuído no Inspector. " +
                             "Hitbox usará transform.position como fallback.");

        Debug.Log($"[MeleeWeapon] Inicializado em '{gameObject.name}'. " +
                  $"behavior={behavior?.gameObject.name ?? "NULL"}, " +
                  $"animController={animController?.gameObject.name ?? "NULL"}");

        if (!finisherEnabled)
            Debug.LogWarning("[MeleeWeapon] finisherEnabled = FALSE. " +
                             "Finalizador desabilitado — combo de 3 lights termina normalmente, " +
                             "heavy after-combo é ignorado. Ative quando 'Finisher' existir no Animator.");

        if (vfxManager == null)
            vfxManager = VfxManager.Instance;
    }

    void OnEnable()  { lightAction.Enable(); heavyAction.Enable(); }

    void OnDisable()
    {
        lightAction.Disable();
        heavyAction.Disable();

        if (behavior != null)
        {
            behavior.isAttacking               = false;
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

    public void OnAttackActiveStart()
    {
        Debug.Log($"[MeleeWeapon] Event: OnAttackActiveStart | step={comboStep} | isAttacking={isAttacking}");
        _animEventActiveStart = true;
    }

    public void OnAttackActiveEnd()
    {
        Debug.Log($"[MeleeWeapon] Event: OnAttackActiveEnd | step={comboStep}");
        _animEventActiveEnd = true;
    }

    public void OnAttackRecoveryEnd()
    {
        Debug.Log($"[MeleeWeapon] Event: OnAttackRecoveryEnd | step={comboStep}");
        _animEventRecoveryEnd = true;
    }

    public void OnComboWindowOpen()
    {
        Debug.Log($"[MeleeWeapon] Event: OnComboWindowOpen | step={comboStep}");
        _animEventComboOpen = true;
    }

    public void OnComboWindowClose()
    {
        Debug.Log($"[MeleeWeapon] Event: OnComboWindowClose | step={comboStep}");
        _animEventComboClose = true;
    }

    // ─── Input ────────────────────────────────────────────────────────────────

    private void ReadInput()
    {
        if (lightAction.WasPressedThisFrame())
        {
            lightBuffered    = true;
            lightBufferTimer = inputBufferWindow;
        }

        if (heavyAction.WasPressedThisFrame())
        {
            heavyBuffered    = true;
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
        // Finisher: só tenta se finisherEnabled = true no Inspector.
        // Com false, a janela do finisher fecha limpa e o combo reseta sem travar.
        if (heavyBuffered && finisherWindowOpen)
        {
            heavyBuffered = false; heavyBufferTimer = 0f;
            finisherWindowOpen = false; comboWindowOpen = false;

            if (finisherEnabled)
            {
                ExecuteFinisher();
                return;
            }
            else
            {
                comboStep = 0;
                Debug.Log("[MeleeWeapon] Finisher bloqueado (finisherEnabled=false) — combo resetado.");
                return;
            }
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
        StartAttackRoutine(AttackType.Light);
    }

    private void ExecuteHeavyAttack()
    {
        if (stamina != null && !stamina.Spend(heavyStaminaCost)) return;
        heavyCooldownTimer = heavyCooldown;
        _justStartedAttack = true;
        StartAttackRoutine(AttackType.Heavy);
    }

    private void ExecuteFinisher()
    {
        if (stamina != null && !stamina.Spend(finisherStaminaCost)) { comboStep = 0; return; }
        comboStep = 0;
        _justStartedAttack = true;
        StartAttackRoutine(AttackType.Finisher);
    }

    private void StartAttackRoutine(AttackType type)
    {
        if (_attackCoroutine != null && isAttacking)
        {
            StopCoroutine(_attackCoroutine);
            _animEventActiveStart = false;
            _animEventActiveEnd   = false;
            _animEventComboOpen   = false;
            _animEventComboClose  = false;
            _animEventRecoveryEnd = false;
            if (behavior != null)
            {
                behavior.isAttacking               = false;
                behavior.isInAttackStartupOrActive = false;
            }
            vfxManager?.ReturnActiveTrail();
        }
        _attackCoroutine = StartCoroutine(AttackRoutine(type));
    }

    /// <summary>
    /// Cancela o ataque em andamento imediatamente — sem esperar eventos.
    /// Chamado por PlayerHealth ao aplicar hurt, guard crush ou morte.
    /// </summary>
    public void CancelAttack()
    {
        if (_attackCoroutine != null)
        {
            StopCoroutine(_attackCoroutine);
            _attackCoroutine = null;
        }

        isAttacking        = false;
        comboWindowOpen    = false;
        finisherWindowOpen = false;
        comboStep          = 0;

        _animEventActiveStart = false;
        _animEventActiveEnd   = false;
        _animEventComboOpen   = false;
        _animEventComboClose  = false;
        _animEventRecoveryEnd = false;

        if (behavior != null)
        {
            behavior.isAttacking               = false;
            behavior.isInAttackStartupOrActive = false;
        }

        vfxManager?.ReturnActiveTrail();
        Debug.Log("[MeleeWeapon] CancelAttack chamado — ataque cancelado externamente.");
    }

    // ─── Rotina de ataque ─────────────────────────────────────────────────────

    private IEnumerator AttackRoutine(AttackType type)
    {
        isAttacking = true;

        int    lightIdx  = Mathf.Clamp(comboStep - 1, 0, StatesLight.Length - 1);
        string trigger   = type == AttackType.Light ? TriggersLight[lightIdx] : type == AttackType.Heavy ? TriggerHeavy    : TriggerFinisher;
        string stateName = type == AttackType.Light ? StatesLight[lightIdx]   : type == AttackType.Heavy ? StateHeavy      : StateFinisher;

        bool isCounter = health != null && health.IsCounterWindowOpen;
        if (isCounter)
        {
            HitStop.Instance?.DoSlowMotion(counterSlowScale, counterSlowHold, counterSlowRampUp);
            float slowTotal = counterSlowHold + counterSlowRampUp;
            yield return new WaitForSecondsRealtime(slowTotal);
            animController?.SetTriggerDirect(TriggerCounter);
            CameraImpulse.Instance?.CounterZoom();
            stateName = StateCounter;
        }
        else
            animController?.SetTriggerDirect(trigger);

        string trailType = type == AttackType.Heavy ? "heavy" : type == AttackType.Finisher ? "finisher" : "light";
        if (!isCounter)
        {
            vfxManager?.SpawnSlashTrail(trailType, attackPoint != null ? attackPoint : transform, comboStep);
            vfxManager?.SpawnSlashSprite(trailType, attackPoint != null ? attackPoint : transform);
        }

        if (behavior != null)
        {
            behavior.isAttacking               = true;
            behavior.isInAttackStartupOrActive = true;
        }

        _currentAttackType = type;
        _currentIsCounter  = isCounter;

        if (anticipationEnabled && !isCounter)
            StartCoroutine(AnticipationSquash(type));

        // WaitForEndOfFrame garante que LateUpdate deste frame terminou antes de
        // resetar as flags — eventos do clipe anterior não apagam os do novo.
        yield return new WaitForEndOfFrame();

        _animEventActiveStart = false;
        _animEventActiveEnd   = false;
        _animEventComboOpen   = false;
        _animEventComboClose  = false;
        _animEventRecoveryEnd = false;

        Debug.Log($"[MeleeWeapon] AttackRoutine START | type={type} | step={comboStep} | state={stateName} | isCounter={isCounter}");

        // ── Fase de Startup ───────────────────────────────────────────────────
        // Aguarda OnAttackActiveStart. Timeout evita trava se evento estiver faltando.
        {
            float elapsed = 0f;
            while (!_animEventActiveStart)
            {
                if (behavior != null && behavior.isLocked)
                {
                    Debug.Log($"[MeleeWeapon] Startup interrompido por isLocked | type={type} step={comboStep}");
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeoutActiveStart)
                {
                    Debug.LogWarning($"[MeleeWeapon] TIMEOUT aguardando OnAttackActiveStart " +
                                     $"(type={type} step={comboStep} state={stateName}). " +
                                     $"Verifique o Animation Event no clipe.");
                    break;
                }
                yield return null;
            }
        }

        // ── Fase Active ───────────────────────────────────────────────────────
        if (!isCounter)
            ApplyHit(type, isCounter);

        // Aguarda OnAttackActiveEnd. Timeout evita trava se evento estiver faltando.
        {
            float elapsed = 0f;
            while (!_animEventActiveEnd)
            {
                if (behavior != null && behavior.isLocked)
                {
                    Debug.Log($"[MeleeWeapon] Active interrompido por isLocked | type={type} step={comboStep}");
                    // Ainda precisa limpar as flags antes de sair
                    if (behavior != null)
                    {
                        behavior.isInAttackStartupOrActive = false;
                        behavior.isAttacking               = false;
                    }
                    isAttacking      = false;
                    _attackCoroutine = null;
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeoutActiveEnd)
                {
                    Debug.LogWarning($"[MeleeWeapon] TIMEOUT aguardando OnAttackActiveEnd " +
                                     $"(type={type} step={comboStep} state={stateName}). " +
                                     $"Verifique o Animation Event no clipe.");
                    break;
                }
                yield return null;
            }
        }

        if (isCounter)
        {
            ApplyHit(type, isCounter);
            health?.ConsumeCounterWindow();
        }

        // Libera movimento imediatamente após o active.
        if (behavior != null)
        {
            behavior.isInAttackStartupOrActive = false;
            behavior.isAttacking               = false;
        }

        Debug.Log($"[MeleeWeapon] ActiveEnd concluído | type={type} step={comboStep} — behavior liberado para movimento");

        // ── Abertura da janela de combo (apenas Light) ────────────────────────
        // Aguarda OnComboWindowOpen. Timeout evita trava se evento estiver faltando.
        if (type == AttackType.Light)
        {
            float elapsed = 0f;
            bool  escaped = false;

            while (!_animEventComboOpen)
            {
                if (behavior != null && behavior.IsDashing)
                {
                    Debug.Log($"[MeleeWeapon] ComboWindow pulada por Dash | step={comboStep}");
                    escaped = true;
                    break;
                }
                if (behavior != null && behavior.isLocked)
                {
                    Debug.Log($"[MeleeWeapon] ComboWindow pulada por isLocked | step={comboStep}");
                    escaped = true;
                    break;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeoutComboOpen)
                {
                    Debug.LogWarning($"[MeleeWeapon] TIMEOUT aguardando OnComboWindowOpen " +
                                     $"(step={comboStep} state={stateName}). " +
                                     $"Verifique o Animation Event no clipe.");
                    break; // continua: trata como se a janela abrisse agora
                }
                yield return null;
            }

            if (!escaped)
            {
                if (comboStep < 3)
                {
                    comboWindowOpen    = true;
                    comboWindowTimer   = comboWindowDuration;
                    Debug.Log($"[MeleeWeapon] ComboWindow ABERTA | step={comboStep} | duração={comboWindowDuration}s");
                }
                else
                {
                    // Só abre a janela do finisher se a feature estiver habilitada.
                    // Com finisherEnabled=false: reseta o combo limpo, sem abrir janela
                    // que nunca seria consumida, evitando qualquer trava de estado.
                    if (finisherEnabled)
                    {
                        finisherWindowOpen  = true;
                        finisherWindowTimer = finisherWindowDuration;
                        Debug.Log($"[MeleeWeapon] FinisherWindow ABERTA | step={comboStep} | duração={finisherWindowDuration}s");
                    }
                    else
                    {
                        comboStep = 0;
                        Debug.Log("[MeleeWeapon] FinisherWindow suprimida (finisherEnabled=false) — combo resetado.");
                    }
                }
            }
        }

        // ── Recovery ──────────────────────────────────────────────────────────
        // Aguarda OnAttackRecoveryEnd. Timeout evita trava se evento estiver faltando.
        {
            float elapsed = 0f;
            while (!_animEventRecoveryEnd)
            {
                if (behavior != null && behavior.IsDashing)
                {
                    Debug.Log($"[MeleeWeapon] Recovery interrompido por Dash | type={type} step={comboStep}");
                    break;
                }
                if (behavior != null && behavior.isLocked)
                {
                    Debug.Log($"[MeleeWeapon] Recovery interrompido por isLocked | type={type} step={comboStep}");
                    break;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= timeoutRecoveryEnd)
                {
                    Debug.LogWarning($"[MeleeWeapon] TIMEOUT aguardando OnAttackRecoveryEnd " +
                                     $"(type={type} step={comboStep} state={stateName}). " +
                                     $"Verifique o Animation Event no clipe.");
                    break;
                }
                yield return null;
            }
        }

        Debug.Log($"[MeleeWeapon] AttackRoutine END | type={type} step={comboStep} — resetando combo e voltando ao idle");

        isAttacking      = false;
        _attackCoroutine = null;
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

        int   step    = Mathf.Clamp(comboStep - 1, 0, 2);
        float hitStop = type == AttackType.Light ? lightHitStopPerStep[step]        : type == AttackType.Heavy ? heavyHitStop       : finisherHitStop;
        float shakeI  = type == AttackType.Light ? lightShakeIntensityPerStep[step] : type == AttackType.Heavy ? heavyShakeIntensity : finisherShakeIntensity;
        float shakeD  = type == AttackType.Light ? lightShakeDurationPerStep[step]  : type == AttackType.Heavy ? heavyShakeDuration  : finisherShakeDuration;

        int   damage     = isCounter ? Mathf.RoundToInt(baseDmg * (health != null ? health.CounterDamageMultiplier : 2f)) : baseDmg;
        float finalStop  = isCounter ? hitStop * counterImpactMultiplier : hitStop;
        float finalShakeI= isCounter ? shakeI  * counterImpactMultiplier : shakeI;

        Transform origin = attackPoint != null ? attackPoint : transform;
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin.position, range, enemyLayer);
        if (hits.Length == 0)
        {
            Debug.Log($"[MeleeWeapon] ApplyHit: nenhum inimigo na hitbox | type={type} range={range} origin={origin.position}");
            return;
        }

        Debug.Log($"[MeleeWeapon] ApplyHit: {hits.Length} alvo(s) atingido(s) | type={type} step={comboStep} damage={damage}");

        Vector2 avgHitDir = Vector2.zero;
        foreach (Collider2D h in hits)
            avgHitDir += ((Vector2)h.transform.position - (Vector2)origin.position);
        if (avgHitDir != Vector2.zero) avgHitDir.Normalize();

        float hitlagDur = isCounter ? 0f
                        : type == AttackType.Light    ? lightHitlagPerStep[step]
                        : type == AttackType.Heavy    ? heavyHitlagDuration
                        : finisherHitlagDuration;

        foreach (Collider2D hit in hits)
        {
            Rigidbody2D enemyRb = hit.GetComponent<Rigidbody2D>() ?? hit.GetComponentInParent<Rigidbody2D>();
            if (enemyRb != null && hitlagDur > 0f)
            {
                Vector2 knockDir = ((Vector2)hit.transform.position - (Vector2)origin.position).normalized;
                StartCoroutine(HitlagRoutine(enemyRb, hitlagDur, knockDir, type));
            }

            if (isCounter)
            {
                Rigidbody2D counterRb = hit.GetComponent<Rigidbody2D>() ?? hit.GetComponentInParent<Rigidbody2D>();
                if (counterRb != null)
                {
                    Vector2 cKnockDir = ((Vector2)hit.transform.position - (Vector2)transform.position).normalized;
                    Vector2 cKnockVec = new Vector2(cKnockDir.x, cKnockDir.y + knockbackVerticalFraction).normalized;
                    StartCoroutine(CounterKnockbackRoutine(counterRb, cKnockVec));
                }
            }

            var hitFlash = hit.GetComponent<HitFlash>() ?? hit.GetComponentInParent<HitFlash>();
            hitFlash?.ImpactFlash();

            string impactType = isCounter         ? "counter"
                              : type == AttackType.Finisher ? "finisher"
                              : type == AttackType.Heavy    ? "heavy"
                              : "light";
            Vector2 hitPos = hit.transform.position;
            Vector2 hitDir = ((Vector2)hit.transform.position - (Vector2)origin.position).normalized;
            vfxManager?.SpawnHitImpact(impactType, hitPos, hitDir, isCounter, comboStep);

            if (!isCounter)
            {
                bool isHeavyHit = type == AttackType.Heavy || type == AttackType.Finisher;
                vfxManager?.SpawnBlood(hit.transform, isHeavyHit, hitDir);
            }

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

        HitStop.Instance?.DoHitStop(finalStop);
        StartCoroutine(ShakeAfterHitStop(finalShakeI, shakeD, avgHitDir, shakeDirectionBias));

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

    private IEnumerator ShakeAfterHitStop(float intensity, float duration, Vector2 direction, float bias)
    {
        while (HitStop.Instance != null && HitStop.Instance.IsActive)
            yield return new WaitForEndOfFrame();

        CameraShake.Instance?.Shake(intensity, duration, direction, bias);
    }

    // ─── Hitlag ───────────────────────────────────────────────────────────────

    private IEnumerator CounterKnockbackRoutine(Rigidbody2D enemyRb, Vector2 knockVec)
    {
        if (enemyRb == null) yield break;

        float elapsed = 0f;
        while (elapsed < counterKnockbackDuration)
        {
            if (enemyRb == null) yield break;
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / counterKnockbackDuration);
            float eased = 1f - (t * t);
            enemyRb.linearVelocity = knockVec * (counterKnockbackForce * eased);
            yield return null;
        }

        if (enemyRb != null)
            enemyRb.linearVelocity = Vector2.zero;
    }

    private IEnumerator HitlagRoutine(Rigidbody2D enemyRb, float duration, Vector2 knockDir, AttackType type)
    {
        if (enemyRb == null) yield break;

        float savedGravity     = enemyRb.gravityScale;
        enemyRb.gravityScale   = 0f;
        enemyRb.linearVelocity = Vector2.zero;

        yield return new WaitForSecondsRealtime(duration);

        if (enemyRb == null) yield break;
        enemyRb.gravityScale = savedGravity;

        if (!knockbackEnabled) yield break;

        float force     = type == AttackType.Light ? lightKnockbackForce    : type == AttackType.Heavy ? heavyKnockbackForce    : finisherKnockbackForce;
        float kDuration = type == AttackType.Light ? lightKnockbackDuration : type == AttackType.Heavy ? heavyKnockbackDuration : finisherKnockbackDuration;

        Vector2 knockVec = new Vector2(knockDir.x, knockDir.y + knockbackVerticalFraction).normalized;

        float elapsed = 0f;
        while (elapsed < kDuration)
        {
            if (enemyRb == null) yield break;
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / kDuration);
            float eased = 1f - (t * t);
            enemyRb.linearVelocity = knockVec * (force * eased);
            yield return null;
        }

        if (enemyRb != null)
            enemyRb.linearVelocity = Vector2.zero;
    }

    // ─── Anticipation squash ──────────────────────────────────────────────────

    private IEnumerator AnticipationSquash(AttackType type)
    {
        float scaleX   = type == AttackType.Light ? lightAnticipationScaleX    : type == AttackType.Heavy ? heavyAnticipationScaleX    : finisherAnticipationScaleX;
        float duration = type == AttackType.Light ? lightAnticipationDuration  : type == AttackType.Heavy ? heavyAnticipationDuration  : finisherAnticipationDuration;

        Transform t  = transform.parent != null ? transform.parent : transform;
        Vector3 orig = t.localScale;

        float signX  = Mathf.Sign(orig.x);
        float signY  = Mathf.Sign(orig.y);
        float scaleY = 1f + (1f - scaleX) * 0.6f;

        Vector3 squashed = new Vector3(
            signX * Mathf.Abs(orig.x) * scaleX,
            signY * Mathf.Abs(orig.y) * scaleY,
            orig.z
        );

        float half    = duration * 0.5f;
        float elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            t.localScale = Vector3.LerpUnclamped(orig, squashed, elapsed / half);
            yield return null;
        }

        elapsed = 0f;

        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            t.localScale = Vector3.LerpUnclamped(squashed, orig, elapsed / half);
            yield return null;
        }

        t.localScale = orig;
    }

    // ─── Gizmo ────────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Transform origin = attackPoint != null ? attackPoint : transform;
        Gizmos.color = new Color(1f, 0.8f, 0.8f, 0.4f);
        Gizmos.DrawWireSphere(origin.position, lightAttackRange);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.5f);
        Gizmos.DrawWireSphere(origin.position, heavyAttackRange);
        Gizmos.color = new Color(1f, 0f, 0f, 0.7f);
        Gizmos.DrawWireSphere(origin.position, finisherAttackRange);
    }
}