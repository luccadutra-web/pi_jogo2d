using System.Collections;
using UnityEngine;

/// <summary>
/// OwlEnemy — Coruja saltadora com ataque de bicada no ar.
///
/// ESTADOS:
///   Patrol    → anda (animação de salto) até detectar o player
///   Chase     → persegue o player no chão
///   Telegraph → para e toca animação de preparação (janela de parry abre aqui)
///   Leaping   → no ar com hitbox ativa; bica durante o voo
///   Stagger   → tomou parry ou poise break
///   Dead
///
/// SETUP:
///   1. Adicione este script no GameObject da coruja.
///   2. Configure os mesmos componentes do EnemyBehavior:
///      - Rigidbody2D, CharacterAnimationController, EnemyPoise, PoiseBar
///      - GroundCheck (Transform filho), AttackPoint (Transform filho)
///   3. No Animator, crie os states/triggers:
///      - "Walk"      (loop) — animação de salto no chão
///      - "Chase"     (loop) — pode ser o mesmo que Walk
///      - "Telegraph" (one-shot) — preparação pré-salto
///      - "Leap"      (one-shot) — bicada no ar
///      - "Land"      (one-shot) — pouso
///      - "Stagger"   (one-shot)
///      - "Die"       (one-shot)
///   4. Arraste os campos no Inspector.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(EnemyPoise))]
public class OwlEnemy : MonoBehaviour, IDamageable, IStaggerable
{
    // ─── Stats ────────────────────────────────────────────────────────────────

    [Header("Vida")]
    [SerializeField] private int maxHealth = 6;

    [Header("Movimento")]
    [SerializeField] private float patrolSpeed = 2.0f;
    [SerializeField] private float chaseSpeed  = 3.0f;

    [Header("Detecção")]
    [SerializeField] private float detectionRange  = 6.0f;
    [SerializeField] private float leapRange       = 4.5f;   // distância para decidir saltar
    [SerializeField] private float leapMinRange    = 1.2f;   // muito perto: não salta, espera
    [SerializeField] private LayerMask playerLayer;

    [Header("Salto / Bicada")]
    [Tooltip("Força horizontal do salto em direção ao player.")]
    [SerializeField] private float leapForceX      = 6.0f;
    [Tooltip("Força vertical do salto.")]
    [SerializeField] private float leapForceY      = 7.0f;
    [Tooltip("Dano da bicada.")]
    [SerializeField] private int   leapDamage      = 2;
    [Tooltip("Dano de postura da bicada — alto para incentivar parry.")]
    [SerializeField] private float leapPoiseDamage = 35f;
    [Tooltip("Raio do hitbox de bicada ao redor do AttackPoint.")]
    [SerializeField] private float leapHitRadius   = 0.5f;
    [Tooltip("Cooldown mínimo entre ataques.")]
    [SerializeField] private float attackCooldown  = 2.2f;

    [Header("Telegraph")]
    [Tooltip("Duração da animação de preparação antes do salto.")]
    [SerializeField] private float telegraphDuration = 0.55f;
    [Tooltip("Se o player estiver a menos que este valor durante o telegraph, cancela o salto.")]
    [SerializeField] private float telegraphCancelRange = 0.8f;

    [Header("Parry")]
    [Tooltip("Durante estes segundos após o início do Telegraph o player pode aplicar parry.")]
    [SerializeField] private float parryWindowDuration = 0.5f;
    [SerializeField] private float parryStaggerDuration     = 1.0f;
    [SerializeField] private float poiseBreakStaggerDuration = 2.0f;

    [Header("Stagger")]
    [SerializeField] private float staggerDuration = 0.8f;

    [Header("Sprite")]
    [Tooltip("Marque se o sprite padrão está virado para a esquerda (invertido).")]
    [SerializeField] private bool spriteFlippedByDefault = true;
    [SerializeField] private float patrolDistance  = 3.0f;   // quanto anda antes de virar
    [SerializeField] private float patrolWaitTime  = 0.8f;   // pausa ao virar

    [Header("Referências")]
    [SerializeField] private Transform  groundCheck;
    [SerializeField] private Transform  attackPoint;
    [SerializeField] private LayerMask  groundLayer;
    [SerializeField] private LayerMask  hitLayer;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    // ─── Triggers de animação ─────────────────────────────────────────────────
    // Por enquanto só existe Walk/Run — controlado por SetSpeed.
    // Quando novas animações forem adicionadas, descomente os triggers abaixo.
    private const string AnimStagger = "Stagger";
    private const string AnimDie     = "Die";
    // private const string AnimTelegraph = "Telegraph";
    // private const string AnimLeap      = "Leap";
    // private const string AnimLand      = "Land";

    // ─── Estado ───────────────────────────────────────────────────────────────

    private enum State { Patrol, Chase, Telegraph, Leaping, Stagger, Dead }

    private State  _state        = State.Patrol;
    private int    _currentHealth;
    private bool   _isStaggered;
    private bool   _attackOnCooldown;
    private bool   _hitAppliedThisLeap;
    private bool   _parryWindowOpen;
    private bool   _hitWindowOpen;       // true quando animation event abre a janela de hit

    // patrulha
    private int    _patrolDir     = 1;
    private float  _patrolWalked  = 0f;
    private bool   _patrolWaiting = false;

    // ─── Componentes ──────────────────────────────────────────────────────────

    private Rigidbody2D                  _rb;
    private CharacterAnimationController _anim;
    private EnemyPoise                   _poise;
    private HitFlash                     _hitFlash;
    private Transform                    _playerTransform;

    // ─── Unity ───────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb       = GetComponent<Rigidbody2D>();
        _anim     = GetComponent<CharacterAnimationController>();
        _poise    = GetComponent<EnemyPoise>() ?? GetComponentInChildren<EnemyPoise>();
        _hitFlash = GetComponent<HitFlash>()   ?? GetComponentInChildren<HitFlash>();

        _currentHealth = maxHealth;

        if (_poise != null)
            _poise.OnPoiseBreak += OnPoiseBreak;
    }

    void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) _playerTransform = playerObj.transform;
        else Debug.LogWarning("[OwlEnemy] Player não encontrado — adicione a tag 'Player'.");
    }

    void OnDestroy()
    {
        if (_poise != null) _poise.OnPoiseBreak -= OnPoiseBreak;
    }

    void Update()
    {
        if (_state == State.Dead || _state == State.Stagger) return;
        if (_isStaggered) return;

        switch (_state)
        {
            case State.Patrol:    DoPatrol();    break;
            case State.Chase:     DoChase();     break;
            case State.Leaping:   DoLeaping();   break;
            // Telegraph e Land são gerenciados por coroutines
        }

        UpdateFacing();
    }

    // ─── Estados ─────────────────────────────────────────────────────────────

    private void DoPatrol()
    {
        if (_patrolWaiting) return;

        if (PlayerInRange(detectionRange))
        {
            Log("Detectou player → CHASE");
            _state = State.Chase;
            return;
        }

        _rb.linearVelocity = new Vector2(_patrolDir * patrolSpeed, _rb.linearVelocity.y);
        _patrolWalked += patrolSpeed * Time.deltaTime;
        _anim?.SetSpeed(patrolSpeed);

        if (_patrolWalked >= patrolDistance)
        {
            _patrolWalked = 0f;
            StartCoroutine(PatrolTurnRoutine());
        }
    }

    private void DoChase()
    {
        if (_playerTransform == null) { _state = State.Patrol; return; }

        float dist = PlayerDistance();

        // Perdeu o player
        if (dist > detectionRange * 1.4f)
        {
            Log("Perdeu player → PATROL");
            _state = State.Patrol;
            return;
        }

        // Decide saltar
        if (!_attackOnCooldown && dist <= leapRange && dist >= leapMinRange && IsGrounded())
        {
            Log("Em range → TELEGRAPH");
            _state = State.Telegraph;
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _anim?.SetSpeed(0f);
            StartCoroutine(TelegraphRoutine());
            return;
        }

        // Muito perto: para e espera o player se afastar (evita empurrar e saltar em cima)
        if (dist < leapMinRange)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _anim?.SetSpeed(0f);
            return;
        }

        // Persegue no chão
        float dir = _playerTransform.position.x > transform.position.x ? 1f : -1f;

        // Verifica se há algo bloqueando horizontalmente (paredes ou player)
        // antes de aplicar velocidade — evita empurrar o player que está colado.
        // Usa um raycast curto na direção do movimento.
        bool blocked = Physics2D.Raycast(
            transform.position, Vector2.right * dir, leapMinRange * 0.9f, playerLayer | groundLayer
        );

        if (blocked)
        {
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _anim?.SetSpeed(0f);
            return;
        }

        _rb.linearVelocity = new Vector2(dir * chaseSpeed, _rb.linearVelocity.y);
        _anim?.SetSpeed(chaseSpeed);
    }

    private void DoLeaping()
    {
        if (_hitAppliedThisLeap || !_hitWindowOpen) return;

        Vector2 origin = GetAttackPointPosition();
        var hits = Physics2D.OverlapCircleAll(origin, leapHitRadius, hitLayer);
        foreach (var col in hits)
        {
            var ph = col.GetComponent<PlayerHealth>() ?? col.GetComponentInParent<PlayerHealth>();
            if (ph == null) continue;

            // Poise hit só é aplicado se o dano não foi absorvido por parry/block/iFrame
            bool absorbed = ph.TakeDamage(leapDamage, transform.position);
            if (!absorbed)
            {
                var pp = col.GetComponent<PlayerPoise>() ?? col.GetComponentInParent<PlayerPoise>();
                pp?.ReceivePoiseHit(leapPoiseDamage);
            }

            _hitAppliedThisLeap = true;
            Log($"Bicada HIT | dmg={leapDamage}");
            break;
        }
    }

    /// <summary>
    /// Retorna a posição do AttackPoint respeitando o flip de scale.
    /// Se o Transform filho estiver atribuído, usa a posição dele diretamente
    /// (o Unity já espelha filhos junto com o localScale do pai).
    /// Se não estiver, calcula o offset manualmente a partir do facing.
    /// </summary>
    private Vector2 GetAttackPointPosition()
    {
        if (attackPoint != null) return attackPoint.position;

        // Fallback: recalcula offset manual quando o Transform não está atribuído
        float facing = transform.localScale.x >= 0f ? 1f : -1f;
        if (spriteFlippedByDefault) facing *= -1f;   // corrige se sprite padrão está invertido
        return (Vector2)transform.position + Vector2.right * facing * leapHitRadius;
    }

    // ─── Coroutines de estado ─────────────────────────────────────────────────

    private IEnumerator PatrolTurnRoutine()
    {
        _patrolWaiting = true;
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        _anim?.SetSpeed(0f);
        yield return new WaitForSeconds(patrolWaitTime);
        _patrolDir *= -1;
        _patrolWaiting = false;
    }

    private IEnumerator TelegraphRoutine()
    {
        // Quando tiver animação: _anim?.TriggerAnimation("Telegraph");
        // Por enquanto: para no lugar, velocidade zero já comunica a pausa

        // Abre janela de parry
        _parryWindowOpen = true;
        yield return new WaitForSeconds(parryWindowDuration);
        _parryWindowOpen = false;

        // Aguarda o resto do telegraph
        float remaining = telegraphDuration - parryWindowDuration;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        // Cancela se player sumiu ou está muito perto
        if (_state != State.Telegraph) yield break;
        // Usa leapMinRange para manter consistência com DoChase (telegraphCancelRange era menor)
        if (_playerTransform == null || PlayerDistance() < leapMinRange)
        {
            Log("Telegraph cancelado — player muito perto ou sumiu");
            _state = State.Chase;
            yield break;
        }

        StartCoroutine(LeapRoutine());
    }

    private IEnumerator LeapRoutine()
    {
        _state              = State.Leaping;
        _hitAppliedThisLeap = false;

        float dir = _playerTransform != null
            ? (_playerTransform.position.x > transform.position.x ? 1f : -1f)
            : _patrolDir;

        _rb.linearVelocity = new Vector2(dir * leapForceX, leapForceY);
        _anim?.TriggerAnimation("Leap");   // necessário para os Animation Events dispararem
        _anim?.SetSpeed(leapForceX);

        // Fallback: se os Animation Events não estiverem presentes no clipe,
        // abre a hit window por tempo. Se OnHitWindowOpen já foi chamado pelo event,
        // este bloco apenas confirma o flag sem efeito colateral.
        StartCoroutine(HitWindowFallbackRoutine());

        // Aguarda pousar — monitora distância durante o voo para não empurrar o player.
        // Como a colisão física entre player e inimigo está ignorada, o Owl atravessaria
        // o player e o empurraria indefinidamente sem esta verificação.
        yield return new WaitForSeconds(0.2f);
        yield return new WaitUntil(() =>
        {
            // Para o impulso horizontal ao chegar perto do player no ar
            if (_playerTransform != null && PlayerDistance() < leapMinRange)
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);

            return IsGrounded() || _state == State.Stagger || _state == State.Dead;
        });

        if (_state == State.Stagger || _state == State.Dead) yield break;

        _hitWindowOpen     = false;   // garante fechamento mesmo sem Anim Event de close
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        _anim?.TriggerAnimation("Land");
        _state = State.Chase;

        Log("Pousou → cooldown");
        StartCoroutine(AttackCooldownRoutine());
    }

    /// <summary>
    /// Abre a hit window automaticamente caso o Animation Event "OnHitWindowOpen"
    /// não esteja configurado no clipe de Leap. Sem efeito colateral se o event já existir.
    /// </summary>
    private IEnumerator HitWindowFallbackRoutine()
    {
        yield return new WaitForSeconds(0.15f);  // espera pico aproximado do salto
        if (!_hitWindowOpen)
        {
            _hitWindowOpen = true;
            Log("HitWindow aberta via fallback por tempo (Anim Event não encontrado)");
        }
    }

    private IEnumerator AttackCooldownRoutine()
    {
        _attackOnCooldown = true;
        yield return new WaitForSeconds(attackCooldown);
        _attackOnCooldown = false;
    }

    private IEnumerator StaggerRoutine(float duration)
    {
        _isStaggered = true;
        _state       = State.Stagger;
        _anim?.SetSpeed(0f);

        // Para o movimento horizontal sem tocar no Y — zerar velocity completo
        // pode causar overlap com o chão no próximo FixedUpdate e o inimigo cair.
        _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        // Congela X e rotação durante o stagger para evitar drift de física
        _rb.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;

        yield return new WaitForSeconds(duration);

        // Restaura constraints antes de voltar ao jogo
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (_state == State.Dead) yield break;

        _isStaggered = false;
        _state       = State.Chase;
        Log($"Recuperou do stagger ({duration:F1}s)");
    }

    // ─── IDamageable ──────────────────────────────────────────────────────────

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        if (_state == State.Dead) return;

        _currentHealth -= damage;
        Log($"TakeDamage | dmg={damage} | hp={_currentHealth}/{maxHealth}");

        _hitFlash?.Flash();
        _poise?.ReceivePoiseHit(damage * 5f);

        if (_currentHealth <= 0) { Die(); return; }
        // Quando tiver animação de Hit: _anim?.TriggerAnimation("Hit");
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    // ─── IStaggerable (parry) ─────────────────────────────────────────────────

    public void Stagger()
    {
        if (_state == State.Dead) return;

        if (!_parryWindowOpen)
        {
            Log("Parry fora da janela — ignorado");
            _hitFlash?.ParryFlash();
            return;
        }

        Log("PARRY CONFIRMADO → stagger");
        _parryWindowOpen = false;
        StopAllCoroutines();

        // Preserva gravidade, cancela impulso horizontal para não atravessar o chão
        _rb.gravityScale    = 1f;
        _rb.linearVelocity  = new Vector2(0f, _rb.linearVelocity.y);
        _rb.constraints     = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        _hitWindowOpen      = false;
        _hitAppliedThisLeap = true;

        _hitFlash?.ParryFlash();
        _anim?.ForceState(AnimStagger);
        StartCoroutine(StaggerRoutine(parryStaggerDuration));
        StartCoroutine(AttackCooldownRoutine());
    }

    // ─── Poise break ─────────────────────────────────────────────────────────

    private void OnPoiseBreak()
    {
        if (_state == State.Dead) return;

        Log("POISE QUEBRADA → stagger longo");
        StopAllCoroutines();

        // Garante que o inimigo não fique no ar ou atravesse o chão
        // Preserva velocidade Y para a gravidade continuar atuando normalmente
        _rb.gravityScale    = 1f;
        _rb.linearVelocity  = new Vector2(0f, _rb.linearVelocity.y);
        _rb.constraints     = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
        _hitWindowOpen      = false;
        _hitAppliedThisLeap = true; // evita que hit residual seja aplicado no stagger

        _hitFlash?.PoiseBreakFlash();
        _anim?.ForceState(AnimStagger);
        StartCoroutine(StaggerRoutine(poiseBreakStaggerDuration));
    }

    // ─── Morte ────────────────────────────────────────────────────────────────

    private void Die()
    {
        _state = State.Dead;
        StopAllCoroutines();

        // Congela o Rigidbody completamente: sem velocidade, sem gravidade,
        // sem movimento — o inimigo fica parado no lugar durante a animação de morte.
        // NÃO desativamos o Collider aqui pois isso faria o corpo cair pelo chão
        // enquanto a animação de Die ainda não está configurada no Animator.
        _rb.linearVelocity = Vector2.zero;
        _rb.gravityScale   = 0f;
        _rb.constraints    = RigidbodyConstraints2D.FreezeAll;

        _anim?.SetSpeed(0f);
        _anim?.TriggerAnimation(AnimDie);
        Log("Morreu.");

        // Desativa o collider e destrói apenas após a animação ter tempo de rodar.
        // Quando a animação de Die estiver configurada, substitua o tempo pelo
        // comprimento real do clipe. Por ora 2.5s é margem suficiente.
        StartCoroutine(DeathCleanupRoutine(2.5f));
    }

    private IEnumerator DeathCleanupRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        Destroy(gameObject);
    }

    // ─── Animation Events ─────────────────────────────────────────────────────
    // Chame estes métodos via Animation Events no clipe de run/ataque
    // quando tiver as animações de telegraph e leap prontas.
    //
    // Por enquanto o telegraph e leap são controlados por tempo (telegraphDuration).
    // Quando tiver os clipes, substitua o WaitForSeconds por estas janelas.

    /// <summary>
    /// Animation Event — abre a janela de parry (início do telegraph).
    /// Adicione no frame onde o inimigo começa a preparar o salto.
    /// </summary>
    public void OnParryWindowOpen()
    {
        _parryWindowOpen = true;
        Log("Janela de parry ABERTA");
    }

    /// <summary>
    /// Animation Event — fecha a janela de parry.
    /// Adicione no frame onde o inimigo sai do telegraph e vai saltar.
    /// </summary>
    public void OnParryWindowClose()
    {
        _parryWindowOpen = false;
        Log("Janela de parry FECHADA");
    }

    /// <summary>
    /// Animation Event — abre a hitbox da bicada durante o voo.
    /// Adicione no frame do pico do salto / bico descendo.
    /// </summary>
    public void OnHitWindowOpen()
    {
        _hitWindowOpen = true;
        Log("Janela de hit ABERTA");
    }

    /// <summary>
    /// Animation Event — fecha a hitbox da bicada.
    /// Adicione no frame do pouso.
    /// </summary>
    public void OnHitWindowClose()
    {
        _hitWindowOpen      = false;
        _hitAppliedThisLeap = false;
        Log("Janela de hit FECHADA");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private bool IsGrounded()
    {
        if (groundCheck == null) return false;
        return Physics2D.OverlapCircle(groundCheck.position, 0.15f, groundLayer);
    }

    private bool PlayerInRange(float range)
        => _playerTransform != null && PlayerDistance() <= range;

    private float PlayerDistance()
        => _playerTransform != null
            ? Vector2.Distance(transform.position, _playerTransform.position)
            : float.MaxValue;

    private void UpdateFacing()
    {
        if (_playerTransform == null) return;

        float velX = _rb.linearVelocity.x;
        float absS = Mathf.Abs(transform.localScale.x);

        // Se o sprite padrão aponta para a esquerda, inverte a lógica
        bool facingRight;
        if (Mathf.Abs(velX) > 0.1f)
            facingRight = velX > 0;
        else if (_state == State.Chase || _state == State.Telegraph)
            facingRight = _playerTransform.position.x > transform.position.x;
        else
            return;

        // spriteFlippedByDefault = true  → sprite padrão aponta esquerda
        //   facingRight → precisa inverter (scale negativo)
        //   facingLeft  → scale positivo
        bool flip = spriteFlippedByDefault ? facingRight : !facingRight;

        Vector3 s = transform.localScale;
        s.x = flip ? -absS : absS;
        transform.localScale = s;
    }

    private void Log(string msg) { if (debugLog) Debug.Log($"[OwlEnemy {name}] {msg}"); }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        // Detecção
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        // Range de salto
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, leapRange);

        // Range mínimo (não salta se player estiver aqui dentro)
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, leapMinRange);

        // Hitbox de bicada — usa GetAttackPointPosition para refletir o facing correto
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.5f);
        Gizmos.DrawWireSphere(GetAttackPointPosition(), leapHitRadius);

        // Ground check
        if (groundCheck != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(groundCheck.position, 0.15f);
        }
    }
}