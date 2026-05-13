using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerBehavior — controla movimento, pulo, dash e flags de estado do player.
///
/// ── LOGS DE DIAGNÓSTICO ──────────────────────────────────────────────────────
///
///  Filtre o Console por "[PlayerBehavior]" para ver:
///    - Mudanças nas flags isLocked / isAttacking / isInAttackStartupOrActive
///    - Início e fim de cada Dash
///    - Aviso quando ApplyMovement é chamado com flags inconsistentes
///
///  Logs de movimento frame-a-frame estão desabilitados por padrão (muito verbosos).
///  Para ativá-los, ligue a flag debugLogMovement no Inspector.
///
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerBehavior : MonoBehaviour
{
    private Rigidbody2D playerRb;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction defendAction;

    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 4.5f;

    [Header("Combate — movimento")]
    [Tooltip("Velocidade durante recovery do ataque (0 = trava total, 1 = livre). Startup e active sempre travam.")]
    [SerializeField] private float attackRecoveryMovementMultiplier = 0.15f;

    [Header("Pulo")]
    [SerializeField] private float jumpForce         = 14f;
    [SerializeField] private float fallMultiplier    = 2.8f;
    [SerializeField] private float lowJumpMultiplier = 2.0f;

    [Header("Ground check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float     groundCheckRadius = 0.15f;

    [Header("Coyote time + jump buffer")]
    [SerializeField] private float coyoteTime       = 0.10f;
    [SerializeField] private float jumpBufferWindow = 0.12f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed    = 18f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.9f;
    [SerializeField] private bool  dashInAir    = false;

    [Header("Defesa")]
    [SerializeField] private string defendKey = "<Keyboard>/e";

    [Header("Colisão")]
    [Tooltip("Layer dos inimigos — a colisão física entre player e inimigo será ignorada")]
    [SerializeField] private LayerMask enemyPhysicsLayer;

    [Header("Diagnóstico")]
    [Tooltip("Loga no console cada mudança nas flags de combate (isLocked, isAttacking, isInAttackStartupOrActive).")]
    [SerializeField] private bool debugLogCombatFlags = true;
    [Tooltip("Loga a velocidade aplicada a cada FixedUpdate. MUITO verboso — use só para investigar bugs de movimento.")]
    [SerializeField] private bool debugLogMovement = false;

    private float horizontalInput;
    private float horinzontalIsLocked;
    private bool  _isGrounded;
    private bool  _wasGrounded;
    private bool  _isJumping;
    private bool  _isDashing;
    private bool  _isDashInvincible;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float dashCooldownTimer;
    private float _facingDirection = 1f;
    private Vector3 originalScale;

    // Cache do estado anterior para detectar release mesmo quando Update estava travado.
    private bool _defendWasHeld;

    // Backing fields com notificação de mudança para diagnóstico
    private bool _isLocked;
    private bool _isAttacking;
    private bool _isInAttackStartupOrActive;

    [HideInInspector]
    public bool isLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            if (debugLogCombatFlags)
                Debug.Log($"[PlayerBehavior] isLocked = {value} | frame={Time.frameCount}");
        }
    }

    [HideInInspector]
    public bool isAttacking
    {
        get => _isAttacking;
        set
        {
            if (_isAttacking == value) return;
            _isAttacking = value;
            if (debugLogCombatFlags)
                Debug.Log($"[PlayerBehavior] isAttacking = {value} | frame={Time.frameCount}");
        }
    }

    /// <summary>
    /// Setado por MeleeWeapon: true durante startup+active (trava movimento total),
    /// false durante recovery (permite movimento reduzido).
    /// </summary>
    [HideInInspector]
    public bool isInAttackStartupOrActive
    {
        get => _isInAttackStartupOrActive;
        set
        {
            if (_isInAttackStartupOrActive == value) return;
            _isInAttackStartupOrActive = value;
            if (debugLogCombatFlags)
                Debug.Log($"[PlayerBehavior] isInAttackStartupOrActive = {value} | frame={Time.frameCount}");
        }
    }

    private PlayerHealth _health;
    private CharacterAnimationController animController;

    public bool  IsGrounded       => _isGrounded;
    public bool  IsJumping        => _isJumping;
    public bool  IsDashing        => _isDashing;
    public bool  IsDashInvincible => _isDashInvincible;
    public float FacingDirection  => _facingDirection;
    public float HorizontalSpeed  => Mathf.Abs(playerRb.linearVelocity.x);
    public float VerticalSpeed    => playerRb.linearVelocity.y;
    public float DashCooldownNorm => Mathf.Clamp01(1f - dashCooldownTimer / dashCooldown);

    void Awake()
    {
        playerRb       = GetComponent<Rigidbody2D>();
        _health        = GetComponent<PlayerHealth>();
        animController = GetComponent<CharacterAnimationController>();
        originalScale  = transform.localScale;

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Negative", "<Keyboard>/leftArrow")
            .With("Positive", "<Keyboard>/d")
            .With("Positive", "<Keyboard>/rightArrow");

        jumpAction   = new InputAction("Jump",   InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");

        dashAction   = new InputAction("Dash",   InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");

        defendAction = new InputAction("Defend", InputActionType.Button);
        defendAction.AddBinding(defendKey);
    }

    void Start()
    {
        // ── Diagnóstico de setup ──────────────────────────────────────────────
        if (groundCheck == null)
            Debug.LogError("[PlayerBehavior] groundCheck não atribuído! O player nunca detectará o chão.");

        if (animController == null)
            Debug.LogWarning("[PlayerBehavior] CharacterAnimationController não encontrado. " +
                             "Animações não serão atualizadas.");

        int playerLayerIndex = gameObject.layer;
        for (int i = 0; i < 32; i++)
        {
            if ((enemyPhysicsLayer.value & (1 << i)) != 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayerIndex, i, true);
                Debug.Log($"[PlayerBehavior] Colisão física ignorada entre layers {playerLayerIndex} e {i}.");
            }
        }

        Debug.Log($"[PlayerBehavior] Inicializado em '{gameObject.name}'. " +
                  $"moveSpeed={moveSpeed} jumpForce={jumpForce} dashSpeed={dashSpeed}");
    }

    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
        defendAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        dashAction.Disable();
        defendAction.Disable();
    }

    void Update()
    {
        // Lê input de defesa ANTES do guard isLocked.
        // Garante que press e release sejam processados mesmo durante hurt stun.
        HandleDefendInput();

        if (_isLocked)
        {
            // Limpa input horizontal durante lock — evita que valor acumulado
            // seja aplicado assim que isLocked virar false.
            horizontalInput = 0f;
            return;
        }

        horizontalInput = moveAction.ReadValue<float>();

        UpdateGrounded();
        UpdateTimers();
        HandleJump();
        HandleDash();
        UpdateFacing();
    }

    private void HandleDefendInput()
    {
        if (_health == null) return;

        bool heldNow = defendAction.IsPressed();

        if (defendAction.WasPressedThisFrame() && !_isDashing)
            _health.StartDefend();

        // Detecta soltar o botão comparando com o frame anterior.
        // WasReleasedThisFrame falhava quando Update era pulado por isLocked.
        if (_defendWasHeld && !heldNow)
            _health.StopDefend();

        _defendWasHeld = heldNow;
    }

    private void UpdateGrounded()
    {
        _wasGrounded = _isGrounded;
        _isGrounded  = Physics2D.OverlapCircle(
            groundCheck.position, groundCheckRadius, groundLayer
        );

        if (!_wasGrounded && _isGrounded)
            _isJumping = false;

        if (_wasGrounded && !_isGrounded && !_isJumping)
            coyoteTimer = coyoteTime;

        animController?.UpdateGrounded(_isGrounded);
    }

    private void UpdateTimers()
    {
        if (coyoteTimer       > 0f) coyoteTimer       -= Time.deltaTime;
        if (jumpBufferTimer   > 0f) jumpBufferTimer   -= Time.deltaTime;
        if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
    }

    private void HandleJump()
    {
        if (jumpAction.WasPressedThisFrame())
        {
            jumpBufferTimer = jumpBufferWindow;
            if (_isGrounded || coyoteTimer > 0f)
                animController?.TriggerJump();
        }

        bool canJump = _isGrounded || coyoteTimer > 0f;
        if (jumpBufferTimer > 0f && canJump && !_isDashing)
        {
            ExecuteJump();
            jumpBufferTimer = 0f;
            coyoteTimer     = 0f;
        }
    }

    private void ExecuteJump()
    {
        horinzontalIsLocked     = horizontalInput;
        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x, 0f);
        playerRb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
        _isJumping  = true;
        _isGrounded = false;
        Debug.Log($"[PlayerBehavior] Jump executado | horizontalInput={horizontalInput:F2}");
    }

    private void HandleDash()
    {
        bool canDash = dashCooldownTimer <= 0f && !_isDashing && (dashInAir || _isGrounded);
        if (dashAction.WasPressedThisFrame() && canDash)
            StartCoroutine(DashRoutine());
    }

    void FixedUpdate()
    {
        if (_isLocked || _isDashing) return;

        ApplyMovement();
        ApplyGravityMultiplier();
    }

    private void ApplyMovement()
    {
        float input = _isGrounded ? horizontalInput : horinzontalIsLocked;

        float multiplier;
        if (_isInAttackStartupOrActive)
            multiplier = 0f;
        else if (_isAttacking)
            multiplier = attackRecoveryMovementMultiplier;
        else
            multiplier = 1f;

        // Quando em startup/active, zera a velocidade horizontal — impede que
        // momento residual de antes do ataque continue deslizando o player.
        if (_isInAttackStartupOrActive && _isGrounded)
            playerRb.linearVelocity = new Vector2(0f, playerRb.linearVelocity.y);
        else
            playerRb.linearVelocity = new Vector2(input * moveSpeed * multiplier, playerRb.linearVelocity.y);

        animController?.SetSpeed(Mathf.Abs(playerRb.linearVelocity.x));

        if (debugLogMovement)
            Debug.Log($"[PlayerBehavior] ApplyMovement | input={input:F2} mult={multiplier:F2} " +
                      $"vel={playerRb.linearVelocity.x:F2} | " +
                      $"locked={_isLocked} attacking={_isAttacking} startupActive={_isInAttackStartupOrActive}");
    }

    private void ApplyGravityMultiplier()
    {
        if (playerRb.linearVelocity.y < 0f)
        {
            playerRb.linearVelocity += Vector2.up * Physics2D.gravity.y
                * (fallMultiplier - 1f) * Time.fixedDeltaTime;
        }
        else if (playerRb.linearVelocity.y > 0f && !jumpAction.IsPressed())
        {
            playerRb.linearVelocity += Vector2.up * Physics2D.gravity.y
                * (lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
        }
    }

    private IEnumerator DashRoutine()
    {
        _isDashing        = true;
        _isDashInvincible = true;
        dashCooldownTimer = dashCooldown;

        Debug.Log($"[PlayerBehavior] Dash INÍCIO | dir={(_isDashing ? "calculando" : "?")} " +
                  $"horizontalInput={horizontalInput:F2} facing={_facingDirection}");

        // Dash cancela defesa e qualquer trava de ataque
        if (_health != null && _health.IsDefending)
            _health.StopDefend();

        isAttacking               = false;
        isInAttackStartupOrActive = false;

        float dir = horizontalInput != 0f
            ? Mathf.Sign(horizontalInput)
            : _facingDirection;

        float originalGravity   = playerRb.gravityScale;
        playerRb.gravityScale   = 0f;
        playerRb.linearVelocity = new Vector2(dir * dashSpeed, 0f);

        Debug.Log($"[PlayerBehavior] Dash velocidade aplicada: {dir * dashSpeed:F2}");

        yield return new WaitForSeconds(dashDuration);

        playerRb.gravityScale   = originalGravity;
        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x * 0.25f, 0f);

        _isDashing        = false;
        _isDashInvincible = false;

        Debug.Log("[PlayerBehavior] Dash FIM");
    }

    private void UpdateFacing()
    {
        if (horizontalInput > 0.01f)
        {
            _facingDirection     = 1f;
            transform.localScale = new Vector3(
                 Mathf.Abs(originalScale.x), originalScale.y, originalScale.z);
        }
        else if (horizontalInput < -0.01f)
        {
            _facingDirection     = -1f;
            transform.localScale = new Vector3(
                -Mathf.Abs(originalScale.x), originalScale.y, originalScale.z);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!groundCheck) return;
        Gizmos.color = _isGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}