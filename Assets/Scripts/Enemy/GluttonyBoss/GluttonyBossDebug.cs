using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GluttonyBossDebug — Move os sprites via código, sem precisar de animação.
///
/// ═══════════════════════════════════════════════════════════════════════════
/// HIERARCHY ESPERADA (sua cena)
/// ═══════════════════════════════════════════════════════════════════════════
///   GluttonyBoss  (este script + HitFlash)
///   ├── Head          ← SpriteRenderer da cabeça
///   ├── LeftArm       ← SpriteRenderer braço esquerdo
///   ├── RightArm      ← SpriteRenderer braço direito
///   └── TongueTip     ← SpriteRenderer da língua
///       └── Tip       ← GluttonyTongue + CircleCollider2D (na enemyLayer)
///
/// ═══════════════════════════════════════════════════════════════════════════
/// SETUP NO INSPECTOR
/// ═══════════════════════════════════════════════════════════════════════════
///   Head Transform      → filho Head
///   Arm Left Transform  → filho LeftArm
///   Arm Right Transform → filho RightArm
///   Tongue Transform    → filho TongueTip  (o sprite da língua)
///   Tongue Script       → filho Tip        (que tem GluttonyTongue.cs)
///   Arm Impact Left     → filho LeftArm    (Transform de impacto no chão)
///   Arm Impact Right    → filho RightArm   (Transform de impacto no chão)
///   Player Layer        → layer do player
///   Hit Flash           → HitFlash neste GameObject
///
/// ═══════════════════════════════════════════════════════════════════════════
/// TECLAS DE DEBUG (autoPlay = false)
/// ═══════════════════════════════════════════════════════════════════════════
///   T → língua desce   R → língua sobe
///   Z → braço esquerdo X → braço direito
///   C → DoubleStrike   V → SlashVolley
///   H → imprime HP     K → mata o boss
/// </summary>
public class GluttonyBossDebug : MonoBehaviour, IDamageable, IStaggerable
{
    // ─── Referências dos filhos ──────────────────────────────────────────────

    [Header("Transforms dos sprites filhos")]
    [SerializeField] private Transform headTransform;
    [SerializeField] private Transform armLeftTransform;
    [SerializeField] private Transform armRightTransform;
    [SerializeField] private Transform tongueTransform;   // o sprite da língua

    [Header("Lógica")]
    [SerializeField] private GluttonyTongue tongueScript; // TongueTip com Collider
    [SerializeField] private Transform armImpactLeft;     // Transform vazio no chão (esquerdo)
    [SerializeField] private Transform armImpactRight;    // Transform vazio no chão (direito)

    [Header("Posições de repouso dos braços (relativas ao boss)")]
    [Tooltip("Onde o braço esquerdo fica parado. Ex: (-3, -1)")]
    [SerializeField] private Vector2 armLeftRestOffset  = new Vector2(-3f, -1f);
    [Tooltip("Onde o braço direito fica parado. Ex: (3, -1)")]
    [SerializeField] private Vector2 armRightRestOffset = new Vector2( 3f, -1f);

    [Tooltip("Quanto o braço sobe antes de descer (relativo ao repouso). Ex: (0, 2)")]
    [SerializeField] private Vector2 armRaiseOffset = new Vector2(0f, 2f);

    [Tooltip("Posição Y do chão em world space. Ex: -3.5")]
    [SerializeField] private float groundY = -3.5f;

    [Header("Vida")]
    [SerializeField] private int maxHealth = 20;

    [Header("Língua")]
    [Tooltip("Posição local da língua quando recolhida (dentro da boca). Ex: (0, -0.5)")]
    [SerializeField] private Vector2 tongueHiddenLocalPos  = new Vector2(0f, -0.5f);
    [Tooltip("Posição local da língua quando no chão. Ex: (0, -3)")]
    [SerializeField] private Vector2 tongueGroundLocalPos  = new Vector2(0f, -3f);
    [SerializeField] private float tongueExtendSpeed       = 4f;
    [SerializeField] private float tongueRetractSpeed      = 6f;
    [SerializeField] private float tongueHoldDuration_P1  = 3.0f;
    [SerializeField] private float tongueHoldDuration_P2  = 2.2f;
    [SerializeField] private float tongueHoldDuration_P3  = 1.4f;

    [Header("Braços — velocidades")]
    [SerializeField] private float armRaiseSpeed   = 5f;
    [SerializeField] private float armStrikeSpeed  = 18f;
    [SerializeField] private float armReturnSpeed  = 4f;

    [Header("Cooldown entre ciclos")]
    [SerializeField] private float attackCycleCooldown_P1 = 4.0f;
    [SerializeField] private float attackCycleCooldown_P2 = 2.8f;
    [SerializeField] private float attackCycleCooldown_P3 = 1.8f;

    [Header("Dano dos braços")]
    [SerializeField] private float armHitRadius     = 0.7f;
    [SerializeField] private int   armDamage        = 1;
    [SerializeField] private int   armDamageEnraged = 2;

    [Header("SlashVolley — Fase 3")]
    [SerializeField] private int   slashVolleyCount    = 4;
    [SerializeField] private float slashVolleyInterval = 0.45f;

    [Header("Stun")]
    [SerializeField] private float stunDuration      = 1.2f;
    [SerializeField] private float parryStunDuration = 2.0f;

    [Header("Referências")]
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private HitFlash  hitFlash;

    [Header("── Debug ──")]
    [Tooltip("Se true, o boss roda sozinho sem precisar apertar teclas.")]
    [SerializeField] private bool autoPlay = false;

    // ─── Estado ─────────────────────────────────────────────────────────────

    private enum ArmPhase { Rest, Raising, Striking, Bouncing, Returning }

    private int      _hp;
    private int      _phase = 1;
    private bool     _stunned;
    private float    _stunTimer;
    private float    _cycleTimer;

    // língua
    private enum TonguePhase { Hidden, Extending, Hold, Retracting }
    private TonguePhase _tonguePhase = TonguePhase.Hidden;
    private float       _tongueHoldTimer;
    private bool        _tongueHit;

    // braços
    private ArmPhase _armLPhase = ArmPhase.Rest;
    private ArmPhase _armRPhase = ArmPhase.Rest;
    private bool     _armLHitOpen, _armLHitDone;
    private bool     _armRHitOpen, _armRHitDone;

    // posições alvo dos braços (world space)
    private Vector3  _armLTarget;
    private Vector3  _armRTarget;

    // double/volley
    private bool     _doDouble;
    private Coroutine _volleyRoutine;

    // referências de cena
    private Transform    _playerTransform;
    private PlayerHealth _playerHealth;

    // posições de repouso em world space (calculadas no Start)
    private Vector3 _armLRestPos;
    private Vector3 _armRRestPos;
    private Vector3 _armLRaisePos;
    private Vector3 _armRRaisePos;

    // ─── Unity ──────────────────────────────────────────────────────────────

    void Awake()
    {
        _hp = maxHealth;
        if (hitFlash == null)
            hitFlash = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();
        if (tongueScript == null)
            tongueScript = GetComponentInChildren<GluttonyTongue>();
    }

    void Start()
    {
        // Calcula posições de repouso dos braços em world space
        _armLRestPos  = transform.position + (Vector3)armLeftRestOffset;
        _armRRestPos  = transform.position + (Vector3)armRightRestOffset;
        _armLRaisePos = _armLRestPos + (Vector3)armRaiseOffset;
        _armRRaisePos = _armRRestPos + (Vector3)armRaiseOffset;

        // Posiciona braços no repouso
        if (armLeftTransform)  armLeftTransform.position  = _armLRestPos;
        if (armRightTransform) armRightTransform.position = _armRRestPos;

        // Esconde língua
        if (tongueTransform)
        {
            tongueTransform.localPosition = tongueHiddenLocalPos;
            tongueTransform.gameObject.SetActive(false);
        }

        // Acha player
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            _playerTransform = playerObj.transform;
            _playerHealth    = playerObj.GetComponent<PlayerHealth>()
                            ?? playerObj.GetComponentInParent<PlayerHealth>();
        }
        else Debug.LogWarning("[GluttonyDebug] Player não encontrado — tag 'Player' ausente.");

        UpdateHPBar();
        if (autoPlay) StartCoroutine(AutoPlayLoop());
    }

    void Update()
    {
        if (_stunned)
        {
            _stunTimer -= Time.deltaTime;
            if (_stunTimer <= 0f) ExitStun();
            BobHead();
            return;
        }

        HandleDebugKeys();
        UpdateTongueMovement();

        // Recalcula posições de repouso em tempo real (relativas ao boss)
        _armLRestPos  = transform.position + (Vector3)armLeftRestOffset;
        _armRRestPos  = transform.position + (Vector3)armRightRestOffset;
        _armLRaisePos = _armLRestPos + (Vector3)armRaiseOffset;
        _armRRaisePos = _armRRestPos + (Vector3)armRaiseOffset;

        UpdateArmMovement(true,  armLeftTransform,  _armLRestPos, _armLRaisePos);
        UpdateArmMovement(false, armRightTransform, _armRRestPos, _armRRaisePos);
        CheckArmHits();
        CheckPhase();
        BobHead();

        if (!autoPlay)
        {
            _cycleTimer += Time.deltaTime;
            if (_cycleTimer >= GetCycleCooldown()) { _cycleTimer = 0f; StartCycle(); }
        }
    }

    // ─── Cabeça: bob sobe/desce ──────────────────────────────────────────────

    void BobHead()
    {
        if (headTransform == null) return;
        float bob = Mathf.Sin(Time.time * 1.6f) * 0.12f;
        headTransform.localPosition = new Vector3(
            headTransform.localPosition.x,
            bob,
            headTransform.localPosition.z);
    }

    // ─── Língua: movimento ───────────────────────────────────────────────────

    void UpdateTongueMovement()
    {
        if (tongueTransform == null) return;

        Vector3 hiddenPos  = transform.position + (Vector3)tongueHiddenLocalPos;
        Vector3 groundPos  = transform.position + (Vector3)tongueGroundLocalPos;

        if (_tonguePhase == TonguePhase.Extending)
        {
            tongueTransform.position = Vector3.MoveTowards(
                tongueTransform.position, groundPos, tongueExtendSpeed * Time.deltaTime);

            if (Vector3.Distance(tongueTransform.position, groundPos) < 0.05f)
            {
                tongueTransform.position = groundPos;
                _tonguePhase    = TonguePhase.Hold;
                _tongueHoldTimer = GetTongueHoldDuration();
                tongueScript?.EnableHitbox();
                Log("Língua no chão — HITBOX ATIVO. Ataque com o machado!");
            }
        }
        else if (_tonguePhase == TonguePhase.Hold)
        {
            _tongueHoldTimer -= Time.deltaTime;
            if (_tongueHoldTimer <= 0f)
            {
                tongueScript?.DisableHitbox();
                _tonguePhase = TonguePhase.Retracting;
                Log("Língua recolhendo — hitbox desativo");
            }
        }
        else if (_tonguePhase == TonguePhase.Retracting)
        {
            tongueTransform.position = Vector3.MoveTowards(
                tongueTransform.position, hiddenPos, tongueRetractSpeed * Time.deltaTime);

            if (Vector3.Distance(tongueTransform.position, hiddenPos) < 0.05f)
            {
                tongueTransform.position = hiddenPos;
                tongueTransform.gameObject.SetActive(false);
                _tonguePhase = TonguePhase.Hidden;
                _tongueHit   = false;
            }
        }
    }

    // ─── Braço: movimento genérico ───────────────────────────────────────────

    void UpdateArmMovement(
        bool isLeft,
        Transform arm,
        Vector3 restPos,
        Vector3 raisePos)
    {
        if (arm == null) return;

        ArmPhase phase = isLeft ? _armLPhase : _armRPhase;

        switch (phase)
        {
            case ArmPhase.Rest:
                float wave = Mathf.Sin(Time.time * 1.2f + (isLeft ? 0f : 1f)) * 0.08f;
                arm.position = Vector3.Lerp(arm.position, restPos + Vector3.up * wave, Time.deltaTime * 4f);
                break;

            case ArmPhase.Raising:
                arm.position = Vector3.MoveTowards(arm.position, raisePos, armRaiseSpeed * Time.deltaTime);
                if (Vector3.Distance(arm.position, raisePos) < 0.05f)
                {
                    arm.position = raisePos;
                    SetArmPhase(isLeft, ArmPhase.Striking);
                    SetArmHit(isLeft, open: true, done: false);
                    Log($"Braço {(isLeft ? "ESQ" : "DIR")} descendo!");
                }
                break;

            case ArmPhase.Striking:
                // Mira na posição X do player, Y fixo no chão
                float targetX = _playerTransform != null
                    ? _playerTransform.position.x
                    : arm.position.x;
                Vector3 strikeTarget = new Vector3(targetX, groundY, arm.position.z);

                arm.position = Vector3.MoveTowards(arm.position, strikeTarget, armStrikeSpeed * Time.deltaTime);

                // Hitbox aberto durante toda a descida
                SetArmHit(isLeft, open: true, done: isLeft ? _armLHitDone : _armRHitDone);

                if (Vector3.Distance(arm.position, strikeTarget) < 0.1f)
                {
                    arm.position = strikeTarget;
                    SetArmHit(isLeft, open: false, done: isLeft ? _armLHitDone : _armRHitDone);
                    SetArmPhase(isLeft, ArmPhase.Bouncing);
                    Log($"Braço {(isLeft ? "ESQ" : "DIR")} impactou no chão!");
                    StartCoroutine(BounceArm(arm, isLeft));
                }
                break;

            // Bouncing e Returning são gerenciados pela coroutine BounceArm
        }
    }

    IEnumerator BounceArm(Transform arm, bool isLeft)
    {
        // pequeno bounce para cima
        Vector3 bouncePos = arm.position + Vector3.up * 0.6f;
        float elapsed = 0f;
        while (elapsed < 0.15f)
        {
            arm.position = Vector3.Lerp(arm.position, bouncePos, elapsed / 0.15f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // volta ao repouso — recalcula em tempo real
        SetArmPhase(isLeft, ArmPhase.Returning);
        Vector3 restPos = transform.position + (isLeft ? (Vector3)armLeftRestOffset : (Vector3)armRightRestOffset);
        while (Vector3.Distance(arm.position, restPos) > 0.05f)
        {
            restPos = transform.position + (isLeft ? (Vector3)armLeftRestOffset : (Vector3)armRightRestOffset);
            arm.position = Vector3.MoveTowards(arm.position, restPos, armReturnSpeed * Time.deltaTime);
            yield return null;
        }
        arm.position = restPos;
        SetArmPhase(isLeft, ArmPhase.Rest);
        Log($"Braço {(isLeft ? "ESQ" : "DIR")} voltou ao repouso");
    }

    // ─── Checagem de dano dos braços no player ───────────────────────────────

    void CheckArmHits()
    {
        if (_playerHealth == null) return;
        int dmg = (_phase >= 3) ? armDamageEnraged : armDamage;

        // Braço esquerdo — checa na posição atual do braço
        if (_armLHitOpen && !_armLHitDone && armLeftTransform != null)
        {
            Collider2D hit = Physics2D.OverlapCircle(armLeftTransform.position, armHitRadius, playerLayer);
            if (hit != null)
            {
                _armLHitDone = true;
                _armLHitOpen = false;
                _playerHealth.TakeDamage(dmg, armLeftTransform.position);
                VfxManager.Instance?.SpawnEnemyHurt(armLeftTransform.position);
                Log($"[HIT] Braço ESQ acertou o player! dmg={dmg}");
            }
        }

        // Braço direito
        if (_armRHitOpen && !_armRHitDone && armRightTransform != null)
        {
            Collider2D hit = Physics2D.OverlapCircle(armRightTransform.position, armHitRadius, playerLayer);
            if (hit != null)
            {
                _armRHitDone = true;
                _armRHitOpen = false;
                _playerHealth.TakeDamage(dmg, armRightTransform.position);
                VfxManager.Instance?.SpawnEnemyHurt(armRightTransform.position);
                Log($"[HIT] Braço DIR acertou o player! dmg={dmg}");
            }
        }
    }

    // ─── Ciclo de ataque ─────────────────────────────────────────────────────

    void StartCycle()
    {
        if (_stunned) return;
        StartTongueDown();

        // Dispara braços com pequeno delay após a língua sair
        StartCoroutine(DelayedArmAttack(0.6f));
    }

    IEnumerator DelayedArmAttack(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_stunned) yield break;

        if (_phase >= 3)
            _volleyRoutine = StartCoroutine(VolleyRoutine());
        else if (_phase == 2 && Random.value < 0.45f)
            TriggerDouble();
        else
            TriggerSingleArm(Random.value < 0.5f);
    }

    void SetArmPhase(bool isLeft, ArmPhase p)
    {
        if (isLeft) _armLPhase = p; else _armRPhase = p;
    }

    void SetArmHit(bool isLeft, bool open, bool done)
    {
        if (isLeft) { _armLHitOpen = open; _armLHitDone = done; }
        else        { _armRHitOpen = open; _armRHitDone = done; }
    }

    void TriggerSingleArm(bool left)
    {
        if (left  && _armLPhase == ArmPhase.Rest) { _armLPhase = ArmPhase.Raising; Log("ArmStrike ESQ"); }
        if (!left && _armRPhase == ArmPhase.Rest) { _armRPhase = ArmPhase.Raising; Log("ArmStrike DIR"); }
    }

    void TriggerDouble()
    {
        if (_armLPhase == ArmPhase.Rest) _armLPhase = ArmPhase.Raising;
        if (_armRPhase == ArmPhase.Rest) _armRPhase = ArmPhase.Raising;
        Log("DoubleStrike!");
    }

    IEnumerator VolleyRoutine()
    {
        Log($"SlashVolley — {slashVolleyCount} golpes");
        for (int i = 0; i < slashVolleyCount; i++)
        {
            if (_stunned) yield break;
            bool left = (i % 2 == 0);
            TriggerSingleArm(left);
            yield return new WaitForSeconds(slashVolleyInterval);
        }
    }

    // ─── Língua: ativa ───────────────────────────────────────────────────────

    void StartTongueDown()
    {
        if (_tonguePhase != TonguePhase.Hidden) return;
        _tonguePhase = TonguePhase.Extending;
        _tongueHit   = false;
        tongueScript?.DisableHitbox();

        if (tongueTransform != null)
        {
            tongueTransform.gameObject.SetActive(true);
            tongueTransform.position = transform.position + (Vector3)tongueHiddenLocalPos;
        }
        Log("Língua descendo...");
    }

    void RetractTongue()
    {
        if (_tonguePhase == TonguePhase.Hidden) return;
        tongueScript?.DisableHitbox();
        _tonguePhase = TonguePhase.Retracting;
    }

    // ─── Callbacks da língua (chamados por GluttonyTongue) ──────────────────

    public void OnTongueHit(int damage, Vector2 sourcePosition)
    {
        if (_stunned) return;
        _hp = Mathf.Max(0, _hp - damage);
        Log($"[TONGUE HIT] dmg={damage} | HP={_hp}/{maxHealth} | Fase={_phase}");

        hitFlash?.Flash();
        VfxManager.Instance?.SpawnEnemyHurt(
            tongueScript != null ? (Vector2)tongueScript.transform.position : (Vector2)transform.position);

        UpdateHPBar();
        if (_hp <= 0) { Die(); return; }

        RetractTongue();
        EnterStun(stunDuration);
    }

    public void OnTongueParried()
    {
        if (_stunned) return;
        Log($"[PARRY] Stun longo {parryStunDuration}s");
        hitFlash?.ParryFlash();
        RetractTongue();
        EnterStun(parryStunDuration);
    }

    // ─── Stun ────────────────────────────────────────────────────────────────

    void EnterStun(float duration)
    {
        _stunned   = true;
        _stunTimer = duration;
        _cycleTimer = 0f;
        if (_volleyRoutine != null) StopCoroutine(_volleyRoutine);
        Log($"[STUN] {duration}s");
    }

    void ExitStun()
    {
        _stunned = false;
        Log("Stun acabou — voltando ao ciclo");
    }

    // ─── Fase ────────────────────────────────────────────────────────────────

    void CheckPhase()
    {
        float pct = (float)_hp / maxHealth;
        int novaFase = pct > 0.66f ? 1 : pct > 0.33f ? 2 : 3;
        if (novaFase != _phase)
        {
            _phase = novaFase;
            Log($"[FASE] ═══ FASE {_phase} ═══");
        }
    }

    // ─── Morte ───────────────────────────────────────────────────────────────

    void Die()
    {
        _stunned = true;
        StopAllCoroutines();
        tongueScript?.DisableHitbox();
        tongueTransform?.gameObject.SetActive(false);
        VfxManager.Instance?.SpawnEnemyHurt(transform.position);
        Log("[MORT] Boss morreu!");
        Destroy(gameObject, 2f);
    }

    // ─── IDamageable / IStaggerable ──────────────────────────────────────────

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        hitFlash?.Flash();
        Log("[INVULNERABLE] Acerte a língua com o machado!");
    }
    public void TakeDamage(int damage) => TakeDamage(damage, default);
    public void Stagger() { hitFlash?.ParryFlash(); }

    // ─── Teclas de debug ─────────────────────────────────────────────────────

    void HandleDebugKeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.tKey.wasPressedThisFrame) StartTongueDown();
        if (kb.rKey.wasPressedThisFrame) RetractTongue();
        if (kb.zKey.wasPressedThisFrame) TriggerSingleArm(true);
        if (kb.xKey.wasPressedThisFrame) TriggerSingleArm(false);
        if (kb.cKey.wasPressedThisFrame) TriggerDouble();
        if (kb.vKey.wasPressedThisFrame) _volleyRoutine = StartCoroutine(VolleyRoutine());
        if (kb.hKey.wasPressedThisFrame) Log($"HP={_hp}/{maxHealth} | Fase={_phase}");
        if (kb.kKey.wasPressedThisFrame) { _hp = 0; Die(); }
    }

    // ─── AutoPlay ────────────────────────────────────────────────────────────

    IEnumerator AutoPlayLoop()
    {
        Log("[AUTOPLAY] Iniciando");
        yield return new WaitForSeconds(1.5f);
        while (true)
        {
            yield return new WaitForSeconds(GetCycleCooldown());
            if (_stunned) { yield return new WaitUntil(() => !_stunned); }
            StartCycle();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    float GetCycleCooldown() => _phase switch
    {
        1 => attackCycleCooldown_P1,
        2 => attackCycleCooldown_P2,
        _ => attackCycleCooldown_P3
    };

    float GetTongueHoldDuration() => _phase switch
    {
        1 => tongueHoldDuration_P1,
        2 => tongueHoldDuration_P2,
        _ => tongueHoldDuration_P3
    };

    void UpdateHPBar()
    {
        // Implementar HUD de HP aqui se quiser — ou deixar só no Console
        Log($"HP: {_hp}/{maxHealth}");
    }

    void Log(string msg) => Debug.Log($"[GluttonyDebug] {msg}");

    // ─── Gizmos ──────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // Raio dos braços no chão
        if (armImpactLeft != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(armImpactLeft.position, armHitRadius);
            Gizmos.DrawIcon(armImpactLeft.position + Vector3.up * 0.3f, "d_console.warnicon", false);
        }
        if (armImpactRight != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(armImpactRight.position, armHitRadius);
        }

        // Posições de repouso dos braços
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position + (Vector3)armLeftRestOffset,  Vector3.one * 0.2f);
        Gizmos.DrawWireCube(transform.position + (Vector3)armRightRestOffset, Vector3.one * 0.2f);

        // Posições de raise
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + (Vector3)armLeftRestOffset  + (Vector3)armRaiseOffset, Vector3.one * 0.2f);
        Gizmos.DrawWireCube(transform.position + (Vector3)armRightRestOffset + (Vector3)armRaiseOffset, Vector3.one * 0.2f);

        // Posição do chão da língua
        if (tongueTransform != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position + (Vector3)tongueGroundLocalPos, 0.25f);
        }
    }
}