using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerBehavior : MonoBehaviour
{
    
    private Rigidbody2D rb;
    
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;

   
    [Header("Movimento")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float jumpForce = 300f;

    [Header("Ground Check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float groundCheckRadius = 0.2f;

    
    [Header("Dash")]
    [SerializeField] private float dashSpeed      = 18f;   
    [SerializeField] private float dashDuration   = 0.18f; 
    [SerializeField] private float dashCooldown   = 0.8f;  
    [SerializeField] private bool  dashInAir      = true;  

    [Header("Dash — efeito ghost")]
    [SerializeField] private bool      ghostEnabled  = false;
    [SerializeField] private GameObject ghostPrefab  = null; 
    [SerializeField] private int       ghostCount    = 4;
    [SerializeField] private float     ghostInterval = 0.04f;

    
    private float   horizontalInput;
    private bool    isGrounded;
    private bool    isDashing;
    private float   dashCooldownTimer;
    private float   facingDirection = 1f; 
    [HideInInspector] public bool isLocked; 

    private Vector3 originalScale;

    

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
       

       
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Left",  "<Keyboard>/a")
            .With("Left",  "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/d")
            .With("Right", "<Keyboard>/rightArrow");

        jumpAction = new InputAction("Jump",  InputActionType.Button, "<Keyboard>/space");
        dashAction = new InputAction("Dash",  InputActionType.Button, "<Keyboard>/leftShift");
        originalScale = transform.localScale;
    }

    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        dashAction.Disable();
    }

    
    void Update()
    {
        if (isLocked) return;

        horizontalInput = moveAction.ReadValue<Vector2>().x;

        isGrounded = Physics2D.OverlapCircle(
            groundCheck.position,
            groundCheckRadius,
            groundLayer
        );

        
        if (dashCooldownTimer > 0f)
            dashCooldownTimer -= Time.deltaTime;

        
        if (jumpAction.WasPressedThisFrame() && isGrounded && !isDashing)
            rb.AddForce(Vector2.up * jumpForce);

       
        bool canDash = dashCooldownTimer <= 0f && !isDashing;
        bool groundCondition = dashInAir || isGrounded;
        if (dashAction.WasPressedThisFrame() && canDash && groundCondition)
            StartCoroutine(DashRoutine());

       
        UpdateFacing();
    }

    void FixedUpdate()
    {
        
        if (isDashing || isLocked) return;

        rb.linearVelocity = new Vector2(horizontalInput * moveSpeed, rb.linearVelocity.y);
    }

    private IEnumerator DashRoutine()
    {
        isDashing = true;
        dashCooldownTimer = dashCooldown;

       
        float originalGravity = rb.gravityScale;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        
        float dir = horizontalInput != 0f ? Mathf.Sign(horizontalInput) : facingDirection;
        rb.linearVelocity = new Vector2(dir * dashSpeed, 0f);

        
        if (ghostEnabled && ghostPrefab != null)
            StartCoroutine(SpawnGhosts());

        yield return new WaitForSeconds(dashDuration);

        rb.gravityScale = originalGravity;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x * 0.3f, rb.linearVelocity.y);
        isDashing = false;
    }

   
    private IEnumerator SpawnGhosts()
    {
        for (int i = 0; i < ghostCount; i++)
        {
            GameObject ghost = Instantiate(ghostPrefab, transform.position, transform.rotation);

          
            SpriteRenderer ghostSr = ghost.GetComponent<SpriteRenderer>();
            if (ghostSr)
            {
                ghostSr.color     = new Color(1f, 1f, 1f, 0.4f);
            }

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

    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }

    
    public bool  IsDashing        => isDashing;
    public float FacingDirection  => facingDirection;
    public bool  IsGrounded       => isGrounded;
    public float DashCooldownNorm => Mathf.Clamp01(1f - dashCooldownTimer / dashCooldown);
}