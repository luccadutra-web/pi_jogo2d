using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PortalSceneTransition : MonoBehaviour
{
    [Header("Configuração da Cena")]
    [Tooltip("Nome EXATO da cena de destino (igual ao arquivo .unity, sem extensão)")]
    [SerializeField] private string targetSceneName = "BossLvl";

    [Header("Transição Visual (opcional)")]
    [Tooltip("Tempo em segundos antes de carregar a cena (0 = instantâneo)")]
    [SerializeField] private float delayBeforeLoad = 0.5f;

    [Tooltip("Tag do jogador que ativa o portal")]
    [SerializeField] private string playerTag = "Player";

    private bool _isTransitioning = false;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isTransitioning) return;

        if (other.CompareTag(playerTag))
        {
            _isTransitioning = true;
            StartCoroutine(LoadBossScene());
        }
    }

    private IEnumerator LoadBossScene()
    {
        // Aguarda o delay configurado (útil para animar fade-out, etc.)
        if (delayBeforeLoad > 0f)
        {
            yield return new WaitForSeconds(delayBeforeLoad);
        }
           
        SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
    }

    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        var col = GetComponent<BoxCollider2D>();
        if (col != null)
            Gizmos.DrawCube(transform.position + (Vector3)col.offset,
                            col.size);
    }
    #endif
}