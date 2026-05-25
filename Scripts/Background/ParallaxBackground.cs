using UnityEngine;

/// <summary>
/// ParallaxBackground — Sistema de parallax para background 2D.
///
/// SETUP:
///   1. Crie um GameObject vazio chamado "Background" na cena.
///   2. Adicione este script nele.
///   3. Para cada layer (céu, nuvem0, nuvem1), crie um filho com SpriteRenderer.
///   4. Arraste os layers no Inspector em ordem (céu mais ao fundo primeiro).
///   5. Ajuste o parallaxFactor de cada layer:
///      - Céu:    0.0  (não move — fundo absoluto)
///      - Nuvem1: 0.05 (move pouquíssimo)
///      - Nuvem0: 0.1  (move um pouco mais)
///
/// TILING HORIZONTAL:
///   Ative "Loop Horizontal" nos layers de nuvem para que elas se repitam
///   infinitamente enquanto a câmera se move.
///   Requer que o sprite tenha Wrap Mode = Repeat no Import Settings.
/// </summary>
public class ParallaxBackground : MonoBehaviour
{
    [System.Serializable]
    public class ParallaxLayer
    {
        public Transform  layerTransform;

        [Tooltip("0 = fica parado com a câmera (céu). 1 = move junto com a cena (sem parallax).")]
        [Range(0f, 1f)]
        public float parallaxFactor = 0.1f;

        [Tooltip("Ativa loop horizontal infinito (para nuvens).")]
        public bool loopHorizontal = false;

        [HideInInspector] public float startX;
        [HideInInspector] public float spriteWidth;
    }

    [Header("Layers (ordem: fundo → frente)")]
    [SerializeField] private ParallaxLayer[] layers;

    [Header("Referência")]
    [Tooltip("Deixe vazio para usar Camera.main automaticamente.")]
    [SerializeField] private Transform cameraTransform;

    private Vector3 _previousCameraPos;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main?.transform;

        if (cameraTransform == null)
        {
            Debug.LogError("[ParallaxBackground] Nenhuma câmera encontrada.");
            enabled = false;
            return;
        }

        _previousCameraPos = cameraTransform.position;

        foreach (var layer in layers)
        {
            if (layer.layerTransform == null) continue;

            layer.startX = layer.layerTransform.position.x;

            // Detecta largura do sprite para loop
            var sr = layer.layerTransform.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
                layer.spriteWidth = sr.bounds.size.x;
            else
                layer.spriteWidth = 0f;
        }
    }

    void LateUpdate()
    {
        if (cameraTransform == null) return;

        Vector3 delta = cameraTransform.position - _previousCameraPos;

        foreach (var layer in layers)
        {
            if (layer.layerTransform == null) continue;

            // Move o layer proporcionalmente ao parallaxFactor
            // factor 0 → não move (céu fixo ao fundo)
            // factor 1 → move igual à cena (sem parallax)
            float moveX = delta.x * layer.parallaxFactor;
            float moveY = delta.y * layer.parallaxFactor;

            Vector3 pos = layer.layerTransform.position;
            pos.x += moveX;
            pos.y += moveY;
            layer.layerTransform.position = pos;

            // Loop horizontal infinito
            if (layer.loopHorizontal && layer.spriteWidth > 0f)
            {
                float camX    = cameraTransform.position.x;
                float relDist = camX - layer.layerTransform.position.x;

                if (Mathf.Abs(relDist) >= layer.spriteWidth)
                {
                    float offset = relDist > 0
                        ? layer.spriteWidth
                        : -layer.spriteWidth;

                    pos = layer.layerTransform.position;
                    pos.x += offset;
                    layer.layerTransform.position = pos;
                }
            }
        }

        _previousCameraPos = cameraTransform.position;
    }

    // Gizmo para visualizar os layers no editor
    void OnDrawGizmosSelected()
    {
        if (layers == null) return;
        foreach (var layer in layers)
        {
            if (layer.layerTransform == null) continue;
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireCube(layer.layerTransform.position, new Vector3(layer.spriteWidth, 1f, 0f));
        }
    }
}