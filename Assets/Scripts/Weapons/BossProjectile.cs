using UnityEngine;


[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class BossProjectile : MonoBehaviour
{
   
    [Header("Dano")]
    public int damage = 10;

    [Header("Vida útil")]
    public float lifetime = 5f;

    [Header("Efeitos")]
    public GameObject hitEffect;        
    public bool destroyOnGround = true; 

   
    void Start()
    {
        Destroy(gameObject, lifetime);
        AlignRotationToVelocity();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        
        if (other.CompareTag("Player"))
        {
            other.GetComponent<PlayerHealth>()?.TakeDamage(damage);
            SpawnHitEffect();
            Destroy(gameObject);
            return;
        }

        
        if (destroyOnGround && other.CompareTag("Ground"))
        {
            SpawnHitEffect();
            Destroy(gameObject);
        }
    }

     void AlignRotationToVelocity()
    {
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null || rb.linearVelocity == Vector2.zero) return;

        float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    void SpawnHitEffect()
    {
        if (hitEffect != null)
        {
            GameObject fx = Instantiate(hitEffect, transform.position, Quaternion.identity);
            Destroy(fx, 2f); 
        }
    }
}