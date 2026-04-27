using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerBehavior : MonoBehaviour
{
    private Rigidbody2D rb;

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction defendAction;

    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float jumpForce = 300f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.2f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed    = 18f;
    [SerializeField] private float dashDuration = 0.18f;
    [SerializeField] private float dashCooldown = 0.8f;
    [SerializeField] private bool  dashInAir    = true;

    [Header("Dash ghost")]
    [SerializeField] private bool       ghostEnabled  = false;
    [SerializeField] private GameObject ghostPrefab   = null;
    [SerializeField] private int        ghostCount    = 4;
    [SerializeField] private float      ghostInterval = 0.04f;

    private bool isKnockedBack;
    private float knockbackEndTime;

    [Header("Defesa")]
    [Tooltip("Tecla usada para defender. Padrão: E")]
    [SerializeField] private string defendKey = "<Keyboard>/e";

    private float horizontalInput;
    private bool  isGrounded;
    private bool  isDashing;
    private float dashCooldownTimer;
    private float facingDirection = 1f;

    
    private bool isJumping;

    [HideInInspector] public bool isLocked;

    private Vector3 originalScale;

    private PlayerHealth  health;
    private PlayerStamina stamina;

    void Awake()
    {
        rb      = GetComponent<Rigidbody2D>();
        health  = GetComponent<PlayerHealth>();
        stamina = GetComponent<PlayerStamina>();

        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Left",  "<Keyboard>/a")
            .With("Left",  "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        jumpAction   = new InputAction("Jump",   InputActionType.Button, "<Keyboard>/space");
        dashAction   = new InputAction("Dash",   InputActionType.Button, "<Keyboard>/leftShift");
        defendAction = new InputAction("Defend", InputActionType.Button, defendKey);

        originalScale = transform.localScale;
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
        if (isLocked) return;

        horizontalInput = moveAction.ReadValue<Vector2>().x;

        isGrounded = Physics2D.OverlapCircle(
            groundCheck.position, groundCheckRadius, groundLayer
        );

        
        if (isGrounded && rb.linearVelocity.y <= 0f)
            isJumping = false;

        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        if (jumpAction.WasPressedThisFrame() && isGrounded && !isDashing)
        {
            rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);

            
            isJumping  = true;
            isGrounded = false;
            StartCoroutine(JumpGroundBuffer());
        }

        bool canDash         = dashCooldownTimer <= 0f && !isDashing;
        bool groundCondition = dashInAir || isGrounded;
        if (dashAction.WasPressedThisFrame() && canDash && groundCondition)
        {
            
            if (stamina == null || stamina.UseStaminaDash())
                StartCoroutine(DashRoutine());
        }

        HandleDefend();
        UpdateFacing();
    }

    
    private IEnumerator JumpGroundBuffer()
    {
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        
    }

    public void ApplyKnockback(Vector2 force, float duration = 0.3f)
    {
        rb.linearVelocity = Vector2.zero;
        rb.AddForce(force, ForceMode2D.Impulse);
        isKnockedBack = true;
        knockbackEndTime = Time.time + duration;
    }

    // No FixedUpdate, adiciona a checagem:
    void FixedUpdate()
    {
    
        if (isKnockedBack && Time.time >= knockbackEndTime)
            isKnockedBack = false;

        if (isDashing || isLocked || isKnockedBack) return;
        rb.linearVelocity = new Vector2(horizontalInput * moveSpeed, rb.linearVelocity.y);
    }

    private void HandleDefend()
    {
        if (health == null) return;

        if (defendAction.WasPressedThisFrame() && !isDashing)
        {
            // FIX 3 (mesmo padrão): trocado || por verificação correta
            if (stamina == null || stamina.CurrentStamina > 0f)
                health.StartDefend();
        }

        if (defendAction.WasReleasedThisFrame() && health.IsDefending)
            health.StopDefend();

        if (health.IsDefending && stamina != null)
        {
            bool stillHasStamina = stamina.DrainDefendStamina(Time.deltaTime);
            if (!stillHasStamina)
                health.StopDefend();
        }
    }

    private IEnumerator DashRoutine()
    {
        isDashing = true;
        dashCooldownTimer = dashCooldown;

        float originalGravity = rb.gravityScale;
        rb.gravityScale   = 0f;
        rb.linearVelocity = Vector2.zero;

        float dir = horizontalInput != 0f ? Mathf.Sign(horizontalInput) : facingDirection;
        rb.linearVelocity = new Vector2(dir * dashSpeed, 0f);

        if (ghostEnabled && ghostPrefab)
            StartCoroutine(SpawnGhosts());

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale   = originalGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.3f, rb.linearVelocity.y);
        isDashing         = false;
    }

    private IEnumerator SpawnGhosts()
    {
        for (int i = 0; i < ghostCount; i++)
        {
            GameObject ghost = Instantiate(ghostPrefab, transform.position, transform.rotation);
            SpriteRenderer ghostSr = ghost.GetComponent<SpriteRenderer>();
            if (ghostSr) ghostSr.color = new Color(1f, 1f, 1f, 0.4f);
            Destroy(ghost, 0.25f);
            yield return new WaitForSeconds(ghostInterval);
        }
    }

    private void UpdateFacing()
    {
        if (horizontalInput > 0.01f)
        {
            facingDirection = 1f;
            transform.localScale = new Vector3(Mathf.Abs(originalScale.x), originalScale.y, originalScale.z);
        }
        else if (horizontalInput < -0.01f)
        {
            facingDirection = -1f;
            transform.localScale = new Vector3(-Mathf.Abs(originalScale.x), originalScale.y, originalScale.z);
        }
    }

    // FIX 4: a condição original era "if (groundCheck) return" — o return early
    // fazia o Gizmo nunca desenhar quando groundCheck EXISTIA, ao contrário do esperado
    // trocado para "if (!groundCheck) return"
    void OnDrawGizmosSelected()
    {
        if (!groundCheck) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }

    public bool  IsDashing        => isDashing;
    public bool  IsJumping        => isJumping;   // FIX 1: exposto para o PlayerAnimator
    public float FacingDirection  => facingDirection;
    public bool  IsGrounded       => isGrounded;
    public float DashCooldownNorm => Mathf.Clamp01(1f - dashCooldownTimer / dashCooldown);
    public bool  IsDefending      => health != null && health.IsDefending;
    public float HorizontalSpeed  => Mathf.Abs(rb.linearVelocity.x);
}