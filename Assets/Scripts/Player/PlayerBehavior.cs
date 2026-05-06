using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

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
    [Tooltip("Fração da velocidade mantida durante startup/active do ataque (0 = trava, 1 = livre)")]
    [SerializeField] private float attackMovementMultiplier = 0.3f;

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

    [HideInInspector] public bool isLocked;
    [HideInInspector] public bool isAttacking;

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

        jumpAction   = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");

        dashAction   = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");

        defendAction = new InputAction("Defend", InputActionType.Button);
        defendAction.AddBinding(defendKey);
    }

    void Start()
    {
        int playerLayerIndex = gameObject.layer;
        for (int i = 0; i < 32; i++)
        {
            if ((enemyPhysicsLayer.value & (1 << i)) != 0)
            {
                Physics2D.IgnoreLayerCollision(playerLayerIndex, i, true);
                Debug.Log($"[PlayerBehavior] Colisão física ignorada entre layers {playerLayerIndex} e {i}.");
            }
        }
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
        // FIX HURT — quando travado, limpa o input horizontal para evitar
        // que o valor acumulado seja aplicado assim que isLocked virar false.
        // Sem isso, o player "escorrega" na direção do último input após o hurt.
        if (isLocked)
        {
            horizontalInput = 0f;
            return;
        }

        horizontalInput = moveAction.ReadValue<float>();

        UpdateGrounded();
        UpdateTimers();
        HandleJump();
        HandleDash();
        HandleDefend();
        UpdateFacing();
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
    }

    private void HandleDash()
    {
        bool canDash = dashCooldownTimer <= 0f && !_isDashing && (dashInAir || _isGrounded);
        if (dashAction.WasPressedThisFrame() && canDash)
            StartCoroutine(DashRoutine());
    }

    private void HandleDefend()
    {
        if (_health == null) return;

        if (defendAction.WasPressedThisFrame() && !_isDashing)
            _health.StartDefend();

        if (defendAction.WasReleasedThisFrame())
            _health.StopDefend();
    }

    void FixedUpdate()
    {
        if (isLocked || _isDashing) return;

        ApplyMovement();
        ApplyGravityMultiplier();
    }

    private void ApplyMovement()
    {
        float input = _isGrounded ? horizontalInput : horinzontalIsLocked;

        float multiplier = isAttacking ? attackMovementMultiplier : 1f;

        playerRb.linearVelocity = new Vector2(input * moveSpeed * multiplier, playerRb.linearVelocity.y);
        animController?.SetSpeed(Mathf.Abs(playerRb.linearVelocity.x));
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

        float dir = horizontalInput != 0f
            ? Mathf.Sign(horizontalInput)
            : _facingDirection;

        float originalGravity   = playerRb.gravityScale;
        playerRb.gravityScale   = 0f;
        playerRb.linearVelocity = new Vector2(dir * dashSpeed, 0f);

        yield return new WaitForSeconds(dashDuration);

        playerRb.gravityScale   = originalGravity;
        playerRb.linearVelocity = new Vector2(playerRb.linearVelocity.x * 0.25f, 0f);

        _isDashing        = false;
        _isDashInvincible = false;
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