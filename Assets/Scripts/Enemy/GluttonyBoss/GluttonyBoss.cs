using System.Collections;
using UnityEngine;

public class GluttonyBoss : MonoBehaviour, IDamageable, IStaggerable
{
   

    [Header("Vida")]
    [SerializeField] private int maxHealth = 20;

    [Header("Limiares de Fase (% de HP restante)")]
    [Range(0f, 1f)]
    [SerializeField] private float phase2Threshold = 0.66f;
    [Range(0f, 1f)]
    [SerializeField] private float phase3Threshold = 0.33f;


    [Header("Língua")]
    [Tooltip("Componente GluttonyTongue no filho TongueTip. " +
             "Ele tem o Collider2D que o MeleeWeapon detecta.")]
    [SerializeField] private GluttonyTongue tongue;

    [Tooltip("Tempo que a língua fica no chão — Fase 1.")]
    [SerializeField] private float tongueHoldDuration_P1 = 3.0f;
    [Tooltip("Fase 2.")]
    [SerializeField] private float tongueHoldDuration_P2 = 2.2f;
    [Tooltip("Fase 3 — janela pequena, player precisa ser rápido.")]
    [SerializeField] private float tongueHoldDuration_P3 = 1.4f;

    [Header("Cooldown entre ciclos")]
    [SerializeField] private float attackCycleCooldown_P1 = 4.0f;
    [SerializeField] private float attackCycleCooldown_P2 = 2.8f;
    [SerializeField] private float attackCycleCooldown_P3 = 1.8f;


    [Header("Braços")]
    [Tooltip("Transform posicionado no chão onde o balde esquerdo bate.")]
    [SerializeField] private Transform armImpactLeft;
    [Tooltip("Transform posicionado no chão onde o balde direito bate.")]
    [SerializeField] private Transform armImpactRight;
    [SerializeField] private float armHitRadius    = 0.7f;
    [SerializeField] private int   armDamage       = 1;
    [SerializeField] private int   armDamageEnraged = 2;

    [Header("SlashVolley — Fase 3")]
    [SerializeField] private int   slashVolleyCount    = 4;
    [SerializeField] private float slashVolleyInterval = 0.45f;


    [Header("Stun ao levar hit na língua")]
    [SerializeField] private float stunDuration      = 1.2f;
    [Tooltip("Stun mais longo quando o player usa o counter/parry.")]
    [SerializeField] private float parryStunDuration = 2.0f;


    [Header("Referências")]
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private HitFlash  hitFlash;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;


    private enum State
    {
        Idle,
        TongueLunge,
        TongueHold,
        TongueRetract,
        ArmStrike,
        DoubleStrike,
        SlashVolley,
        Stunned,
        Dead
    }

    private State _state        = State.Idle;
    private int   _currentHealth;
    private int   _currentPhase = 1;
    private bool  _phaseTransitionPlaying;

    private bool  _tongueOnGround;

    // braços
    private bool  _armLeftHitWindowOpen;
    private bool  _armRightHitWindowOpen;
    private bool  _armLeftHitApplied;
    private bool  _armRightHitApplied;

    private bool  _doubleHitWindowOpen;
    private bool  _doubleHitApplied;

    private Transform    _playerTransform;
    private PlayerHealth _playerHealth;
    private Animator     _anim;
    private Coroutine    _stateMachineRoutine;
    private Coroutine    _stunRoutine;


    void Awake()
    {
        _anim          = GetComponent<Animator>();
        _currentHealth = maxHealth;

        if (hitFlash == null)
            hitFlash = GetComponent<HitFlash>() ?? GetComponentInChildren<HitFlash>();

        if (tongue == null)
            tongue = GetComponentInChildren<GluttonyTongue>();
    }

    void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            _playerTransform = playerObj.transform;
            _playerHealth    = playerObj.GetComponent<PlayerHealth>()
                            ?? playerObj.GetComponentInParent<PlayerHealth>();
        }
        else
        {
            Debug.LogWarning("[GluttonyBoss] Player não encontrado — tag 'Player' ausente.");
        }

        _stateMachineRoutine = StartCoroutine(BossStateMachine());
    }

    void Update()
    {
        if (_state == State.Dead) return;
        CheckArmHits();
        CheckDoubleHits();
        CheckPhaseTransition();
    }


    private IEnumerator BossStateMachine()
    {
        yield return new WaitForSeconds(1.5f);

        while (_state != State.Dead)
        {
            yield return new WaitForSeconds(GetCycleCooldown());
            if (_state == State.Dead || _state == State.Stunned) { yield return null; continue; }

            // 1. Língua desce (expõe fraqueza ao machado)
            yield return StartCoroutine(TongueLungeRoutine());
            if (_state == State.Dead) yield break;

            // 2. Braços atacam enquanto língua está no chão
            if (_state != State.Stunned)
                yield return StartCoroutine(ArmAttackPhase());
            if (_state == State.Dead) yield break;

            // 3. Língua recolhe
            if (_tongueOnGround)
                yield return StartCoroutine(TongueRetractRoutine());
        }
    }


    private IEnumerator TongueLungeRoutine()
    {
        _state          = State.TongueLunge;
        _tongueOnGround = false;
        Log("TongueLunge");

        _anim?.SetTrigger("TongueLunge");

        float timeout = 2.5f;
        while (!_tongueOnGround && timeout > 0f && _state != State.Dead)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (_tongueOnGround)
        {
            _state = State.TongueHold;
            tongue?.EnableHitbox();               
            Log("TongueHold — hitbox ATIVO, machado pode acertar");
            yield return new WaitForSeconds(GetTongueHoldDuration());
        }
    }


    private IEnumerator ArmAttackPhase()
    {
        if (_currentPhase >= 3)
            yield return StartCoroutine(SlashVolleyRoutine());
        else if (_currentPhase == 2 && Random.value < 0.45f)
            yield return StartCoroutine(DoubleStrikeRoutine());
        else
            yield return StartCoroutine(SingleArmStrikeRoutine());
    }

    private IEnumerator SingleArmStrikeRoutine()
    {
        _state = State.ArmStrike;
        bool useLeft   = (Random.value < 0.5f);
        _armLeftHitApplied  = false;
        _armRightHitApplied = false;

        Log($"ArmStrike → {(useLeft ? "Esquerdo" : "Direito")}");
        _anim?.SetTrigger(useLeft ? "ArmStrike_L" : "ArmStrike_R");

        float timeout = 3.0f;
        while (timeout > 0f && _state != State.Dead && _state != State.Stunned)
        {
            bool done = useLeft ? (!_armLeftHitWindowOpen  && _armLeftHitApplied)
                                : (!_armRightHitWindowOpen && _armRightHitApplied);
            if (done) break;
            timeout -= Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.4f);
    }

    private IEnumerator DoubleStrikeRoutine()
    {
        _state = State.DoubleStrike;
        _doubleHitApplied = false;
        Log("DoubleStrike!");

        _anim?.SetTrigger("DoubleStrike");

        float timeout = 3.5f;
        while (!_doubleHitApplied && timeout > 0f && _state != State.Dead && _state != State.Stunned)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator SlashVolleyRoutine()
    {
        _state = State.SlashVolley;
        Log($"SlashVolley — {slashVolleyCount} golpes");

        for (int i = 0; i < slashVolleyCount; i++)
        {
            if (_state == State.Dead || _state == State.Stunned) yield break;

            _armLeftHitApplied  = false;
            _armRightHitApplied = false;
            _anim?.SetTrigger((i % 2 == 0) ? "ArmStrike_L" : "ArmStrike_R");
            yield return new WaitForSeconds(slashVolleyInterval);
        }

        yield return new WaitForSeconds(0.6f);
    }


    private IEnumerator TongueRetractRoutine()
    {
        _state = State.TongueRetract;
        tongue?.DisableHitbox();
        Log("TongueRetract — hitbox DESATIVO");

        _anim?.SetTrigger("TongueRetract");

        float timeout = 2.5f;
        while (_tongueOnGround && timeout > 0f && _state != State.Dead)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        _state = State.Idle;
    }


    private IEnumerator StunRoutine(float duration)
    {
        _state = State.Stunned;
        tongue?.DisableHitbox();
        Log($"Stunned {duration}s");

        _anim?.SetTrigger("Stunned");
        yield return new WaitForSeconds(duration);

        if (_state == State.Dead) yield break;

        _tongueOnGround = false;
        yield return StartCoroutine(TongueRetractRoutine());

        if (_stateMachineRoutine != null) StopCoroutine(_stateMachineRoutine);
        _stateMachineRoutine = StartCoroutine(BossStateMachine());
    }

   

    private void CheckArmHits()
    {
        if (_playerHealth == null) return;
        int dmg = (_currentPhase >= 3) ? armDamageEnraged : armDamage;

        if (_armLeftHitWindowOpen && !_armLeftHitApplied && armImpactLeft != null)
        {
            if (Physics2D.OverlapCircle(armImpactLeft.position, armHitRadius, playerLayer))
            {
                _armLeftHitApplied = true;
                _playerHealth.TakeDamage(dmg, armImpactLeft.position);
                VfxManager.Instance?.SpawnEnemyHurt(armImpactLeft.position);
                Log($"Braço Esquerdo acertou | dmg={dmg}");
            }
        }

        if (_armRightHitWindowOpen && !_armRightHitApplied && armImpactRight != null)
        {
            if (Physics2D.OverlapCircle(armImpactRight.position, armHitRadius, playerLayer))
            {
                _armRightHitApplied = true;
                _playerHealth.TakeDamage(dmg, armImpactRight.position);
                VfxManager.Instance?.SpawnEnemyHurt(armImpactRight.position);
                Log($"Braço Direito acertou | dmg={dmg}");
            }
        }
    }

    private void CheckDoubleHits()
    {
        if (!_doubleHitWindowOpen || _doubleHitApplied || _playerHealth == null) return;
        int dmg = (_currentPhase >= 3) ? armDamageEnraged : armDamage;

        Vector2 posHit = transform.position;
        bool acertou   = false;

        if (armImpactLeft  != null && Physics2D.OverlapCircle(armImpactLeft.position,  armHitRadius, playerLayer)) { acertou = true; posHit = armImpactLeft.position; }
        if (!acertou &&
            armImpactRight != null && Physics2D.OverlapCircle(armImpactRight.position, armHitRadius, playerLayer)) { acertou = true; posHit = armImpactRight.position; }

        if (acertou)
        {
            _doubleHitApplied = true;
            _playerHealth.TakeDamage(dmg, posHit);
            VfxManager.Instance?.SpawnEnemyHurt(posHit);
            Log($"DoubleStrike acertou | dmg={dmg}");
        }
    }

    public void OnTongueHit(int damage, Vector2 sourcePosition)
    {
        if (_state == State.Dead || _state == State.Stunned) return;

        _currentHealth -= damage;
        Log($"OnTongueHit | dmg={damage} | hp={_currentHealth}/{maxHealth}");

        hitFlash?.Flash();
        VfxManager.Instance?.SpawnEnemyHurt(
            tongue != null ? (Vector2)tongue.transform.position : (Vector2)transform.position);

        if (_currentHealth <= 0) { Die(); return; }

        if (_stateMachineRoutine != null) StopCoroutine(_stateMachineRoutine);
        if (_stunRoutine         != null) StopCoroutine(_stunRoutine);
        _stunRoutine = StartCoroutine(StunRoutine(stunDuration));
    }
   
    public void OnTongueParried()
    {
        if (_state == State.Dead || _state == State.Stunned) return;

        Log("OnTongueParried — stun longo!");
        hitFlash?.ParryFlash();

        if (_stateMachineRoutine != null) StopCoroutine(_stateMachineRoutine);
        if (_stunRoutine         != null) StopCoroutine(_stunRoutine);
        _stunRoutine = StartCoroutine(StunRoutine(parryStunDuration));
    }

   
    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        hitFlash?.Flash();
        Log("TakeDamage no raiz ignorado — acerte a língua!");
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    public void Stagger()
    {
        if (_state == State.Dead) return;
        hitFlash?.ParryFlash();
    }

    
    private void CheckPhaseTransition()
    {
        if (_phaseTransitionPlaying || _state == State.Dead) return;
        float hpPct = (float)_currentHealth / maxHealth;

        if      (_currentPhase == 1 && hpPct <= phase2Threshold) StartCoroutine(EnterPhase(2));
        else if (_currentPhase == 2 && hpPct <= phase3Threshold) StartCoroutine(EnterPhase(3));
    }

    private IEnumerator EnterPhase(int phase)
    {
        _phaseTransitionPlaying = true;
        _currentPhase = phase;
        Log($"═══ FASE {phase} ═══");

        if (phase == 3)
        {
            _anim?.SetTrigger("Enrage");
            _anim?.SetBool("IsEnraged", true);
            yield return new WaitForSeconds(1.5f);
        }
        else
        {
            yield return new WaitForSeconds(0.4f);
        }

        _phaseTransitionPlaying = false;
    }


    public void OnTongueTouchGround()
    {
        _tongueOnGround = true;
        Log("OnTongueTouchGround");
    }

    public void OnTongueFullyRetracted()
    {
        _tongueOnGround = false;
        Log("OnTongueFullyRetracted");
    }

    public void OnArmImpact(int side)
    {
        if (side == 0) { _armLeftHitWindowOpen  = true; _armLeftHitApplied  = false; }
        else           { _armRightHitWindowOpen = true; _armRightHitApplied = false; }
        Log($"OnArmImpact side={side}");
    }
   
    public void OnArmStrikeEnd(int side)
    {
        if (side == 0) _armLeftHitWindowOpen  = false;
        else           _armRightHitWindowOpen = false;
        Log($"OnArmStrikeEnd side={side}");
    }

    public void OnDoubleImpact()
    {
        _doubleHitWindowOpen = true;
        _doubleHitApplied    = false;
        Log("OnDoubleImpact");
    }

    public void OnDoubleStrikeEnd()
    {
        _doubleHitWindowOpen = false;
        Log("OnDoubleStrikeEnd");
    }


    private void Die()
    {
        _state = State.Dead;
        StopAllCoroutines();

        tongue?.DisableHitbox();
        _armLeftHitWindowOpen  = false;
        _armRightHitWindowOpen = false;

        _anim?.SetBool("IsEnraged", false);
        _anim?.SetTrigger("Die");
        Log("Morreu.");

        StartCoroutine(DeathCleanupRoutine(3.0f));
    }

    private IEnumerator DeathCleanupRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        Destroy(gameObject);
    }

    
    private float GetCycleCooldown() => _currentPhase switch
    {
        1 => attackCycleCooldown_P1,
        2 => attackCycleCooldown_P2,
        _ => attackCycleCooldown_P3
    };

    private float GetTongueHoldDuration() => _currentPhase switch
    {
        1 => tongueHoldDuration_P1,
        2 => tongueHoldDuration_P2,
        _ => tongueHoldDuration_P3
    };

    private void Log(string msg) { if (debugLog) Debug.Log($"[GluttonyBoss] {msg}"); }


    void OnDrawGizmosSelected()
    {
        if (armImpactLeft  != null) { Gizmos.color = Color.red;     Gizmos.DrawWireSphere(armImpactLeft.position,  armHitRadius); }
        if (armImpactRight != null) { Gizmos.color = Color.red;     Gizmos.DrawWireSphere(armImpactRight.position, armHitRadius); }
        if (tongue         != null) { Gizmos.color = Color.magenta; Gizmos.DrawWireSphere(tongue.transform.position, 0.35f); }
    }
}