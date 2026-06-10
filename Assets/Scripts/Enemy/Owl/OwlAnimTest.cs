using UnityEngine;

/// <summary>
/// Script de teste temporário — REMOVA antes de buildar.
///
/// Adicione este componente no mesmo GameObject da coruja.
/// Entre em Play e use as teclas para forçar as animações diretamente,
/// sem passar pelo CharacterAnimationController ou OwlEnemy.
///
/// L → light_attack
/// H → heavy_attack
/// R → run  (Speed = 3)
/// I → idle (Speed = 0)
/// U → hurt
/// K → die
/// </summary>
public class OwlAnimTest : MonoBehaviour
{
    private Animator _anim;

    void Awake()
    {
        _anim = GetComponent<Animator>();
        if (_anim == null)
            Debug.LogError("[OwlAnimTest] Animator não encontrado em " + gameObject.name);
    }

    void Update()
    {
        if (_anim == null) return;

        if (Input.GetKeyDown(KeyCode.L))
        {
            Debug.Log("[OwlAnimTest] → LightAttack");
            _anim.SetFloat("Speed", 0f);
            _anim.ResetTrigger("LightAttack");
            _anim.SetTrigger("LightAttack");
        }

        if (Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("[OwlAnimTest] → HeavyAttack");
            _anim.SetFloat("Speed", 0f);
            _anim.ResetTrigger("HeavyAttack");
            _anim.SetTrigger("HeavyAttack");
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("[OwlAnimTest] → run (Speed=3)");
            _anim.SetFloat("Speed", 3f);
        }

        if (Input.GetKeyDown(KeyCode.I))
        {
            Debug.Log("[OwlAnimTest] → idle (Speed=0)");
            _anim.SetFloat("Speed", 0f);
        }

        if (Input.GetKeyDown(KeyCode.U))
        {
            Debug.Log("[OwlAnimTest] → Hurt");
            _anim.ResetTrigger("Hurt");
            _anim.SetTrigger("Hurt");
        }

        if (Input.GetKeyDown(KeyCode.K))
        {
            Debug.Log("[OwlAnimTest] → Die");
            _anim.ResetTrigger("Die");
            _anim.SetTrigger("Die");
        }
    }
}