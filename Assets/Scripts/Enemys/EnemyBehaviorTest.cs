using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyBehaviorTest : MonoBehaviour
{
    [Header("Saúde")]
    [SerializeField] private int maxHealth = 5;

    [Header("Knockback")]
    [SerializeField] private float knockbackDuration  = 0.2f;
    [SerializeField] private float maxKnockbackForce  = 15f; 

    [Header("Pontos")]
    [SerializeField] private int pointsGiven = 10;

   
    private int   currentHealth;
    private bool  isDead;
    private Rigidbody2D rb;
    private Animator    animator;

   

    void Awake()
    {
        rb            = GetComponent<Rigidbody2D>();
        animator      = GetComponent<Animator>();
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        animator?.SetTrigger("Hit");

        if (currentHealth <= 0) Die();
    }

    public void ApplyKnockback(Vector2 force)
    {
        if (isDead) return;

        
        if (force.magnitude > maxKnockbackForce)
            force = force.normalized * maxKnockbackForce;

        force.y = Mathf.Clamp(force.y + 2f, 0f, 6f); 

        rb.linearVelocity = Vector2.zero;
        rb.AddForce(force, ForceMode2D.Impulse);
    }

    void Die()
    {
        isDead = true;
        GivePoints();
        animator?.SetTrigger("Die");
        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
       
        yield return new WaitForSeconds(0.3f);
        Destroy(gameObject);
    }

    void GivePoints()
    {
        
        PlayerPoints pp = FindFirstObjectByType<PlayerPoints>();
        pp?.AddPoints(pointsGiven);
    }
}