using UnityEngine;

/// <summary>
/// GluttonyTongue — fica no GameObject "Tip" (filho de TongueTip, filho do boss).
///
/// HIERARCHY:
///   GluttonyBoss
///   └── TongueTip   ← sprite da língua
///       └── Tip     ← ESTE SCRIPT + CircleCollider2D
///                      Layer: Enemy (mesma do MeleeWeapon)
///                      Collider começa DESATIVADO
///
/// O MeleeWeapon chama TakeDamage() aqui. Este script repassa para o boss
/// seja ele GluttonyBoss ou GluttonyBossDebug (funciona com os dois).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class GluttonyTongue : MonoBehaviour, IDamageable, IStaggerable
{
    private Collider2D        _col;

    // Suporta tanto GluttonyBoss quanto GluttonyBossDebug
    private GluttonyBoss      _boss;
    private GluttonyBossDebug _bossDebug;

    void Awake()
    {
        _col       = GetComponent<Collider2D>();
        _boss      = GetComponentInParent<GluttonyBoss>();
        _bossDebug = GetComponentInParent<GluttonyBossDebug>();

        if (_boss == null && _bossDebug == null)
            Debug.LogError("[GluttonyTongue] Nenhum boss encontrado no pai! " +
                           "Certifique-se que Tip é neto do GluttonyBoss.");

        _col.enabled = false; // começa desativado
    }

    // ── Ativação pelo boss ───────────────────────────────────────────────────

    public void EnableHitbox()  { _col.enabled = true;  }
    public void DisableHitbox() { _col.enabled = false; }

    // ── IDamageable — chamado pelo MeleeWeapon ───────────────────────────────

    public void TakeDamage(int damage, Vector2 sourcePosition)
    {
        if (_boss      != null) _boss.OnTongueHit(damage, sourcePosition);
        if (_bossDebug != null) _bossDebug.OnTongueHit(damage, sourcePosition);
    }

    public void TakeDamage(int damage) => TakeDamage(damage, default);

    // ── IStaggerable — chamado pelo MeleeWeapon em parry ────────────────────

    public void Stagger()
    {
        if (_boss      != null) _boss.OnTongueParried();
        if (_bossDebug != null) _bossDebug.OnTongueParried();
    }
}