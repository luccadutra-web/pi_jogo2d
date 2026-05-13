using System.Collections;
using UnityEngine;

/// <summary>
/// DustFootstep — spawna poeira nos pés do player enquanto ele caminha no chão.
///
/// ── COMO FUNCIONA ────────────────────────────────────────────────────────────
///
///   Detecta quando o player está se movendo no chão (Speed > threshold) e
///   spawna instâncias do prefab de dust em intervalos regulares (stepInterval).
///   Cada instância roda a animação da sprite sheet uma vez e se desativa.
///
/// ── SETUP NO INSPECTOR ───────────────────────────────────────────────────────
///
///   1. Adicione este componente no mesmo GameObject do PlayerBehavior.
///   2. Crie o prefab "DustFootstepFX" (veja instruções abaixo).
///   3. Arraste o prefab no campo dustPrefab.
///   4. Ajuste spawnOffset para alinhar com o pé do personagem.
///   5. Ajuste stepInterval para a cadência de poeira desejada.
///
/// ── CRIANDO O PREFAB DustFootstepFX ─────────────────────────────────────────
///
///   a) Crie um GameObject vazio → nomeie "DustFootstepFX".
///   b) Adicione SpriteRenderer (sem sprite inicial, Alpha = 1).
///   c) Adicione Animator.
///   d) Crie um AnimatorController:
///        • State "Dust" com o AnimationClip da sprite sheet (15 frames, ~24fps).
///        • Marque Loop Time = FALSE no clip — a animação roda uma vez só.
///        • Adicione o componente VfxAutoReturn (já existe no projeto) ou use
///          o método SelfReturn abaixo (controlado internamente por este script).
///   e) No AnimationClip configure os sprites da sheet:
///        • Fatie a textura no Sprite Editor: Grid by Cell Count, 15 colunas, 1 linha.
///        • Arraste todos os 15 sprites para a timeline do clip.
///        • Sample Rate recomendado: 18–24 fps (ajuste ao gosto).
///
/// ── SETUP DA TEXTURA (Sprite Sheet) ─────────────────────────────────────────
///
///   Texture: SmokeFX_Lite_SpriteSheet_1A-15.png  (576 × 64 px)
///   Sprite Mode: Multiple
///   Sprite Editor → Slice → Grid by Cell Count:
///     Column = 15, Row = 1  →  cada frame = 38 × 64 px
///   Pixels Per Unit: ajuste conforme escala do seu jogo (recomendado: 32–64).
///   Filter Mode: Point (sem blur em pixel art).
///   Compression: None ou Lossless.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
[RequireComponent(typeof(PlayerBehavior))]
public class DustFootstep : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Prefab")]
    [Tooltip("Prefab com SpriteRenderer + Animator configurado com o clip de 15 frames.")]
    [SerializeField] private GameObject dustPrefab;

    [Header("Posicionamento")]
    [Tooltip("Offset em relação ao centro do player para spawn da poeira.\n" +
             "Y negativo desce até o pé. X é ignorado (espelhamento automático).")]
    [SerializeField] private Vector2 spawnOffset = new Vector2(0f, -0.55f);

    [Header("Cadência")]
    [Tooltip("Intervalo em segundos entre cada puff de poeira durante o movimento.")]
    [SerializeField] private float stepInterval = 0.22f;

    [Tooltip("Velocidade mínima horizontal (em unidades/s) para spawnar poeira.\n" +
             "Evita puffs quando o player está quase parado.")]
    [SerializeField] private float minSpeedThreshold = 0.5f;

    [Header("Escala")]
    [Tooltip("Escala base do efeito de poeira.")]
    [SerializeField] private Vector3 dustScale = new Vector3(1f, 1f, 1f);

    [Header("Pool")]
    [Tooltip("Quantidade de instâncias pré-criadas. 4 é suficiente para movimento contínuo.")]
    [SerializeField] private int poolSize = 4;

    [Header("Lifetime")]
    [Tooltip("Tempo (segundos) antes de devolver o objeto ao pool.\n" +
             "Deve ser >= duração do clip de animação.")]
    [SerializeField] private float dustLifetime = 0.7f;

    // ── Referências ───────────────────────────────────────────────────────────

    private PlayerBehavior _player;

    // ── Pool simples ──────────────────────────────────────────────────────────

    private GameObject[] _pool;
    private int _poolIndex;

    // ── Estado interno ────────────────────────────────────────────────────────

    private float _stepTimer;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _player = GetComponent<PlayerBehavior>();
        BuildPool();
    }

    private void Update()
    {
        // Condições para spawnar: no chão, se movendo, não travado
        bool shouldSpawn = _player.IsGrounded
                        && _player.HorizontalSpeed >= minSpeedThreshold
                        && !_player.IsDashing;

        if (!shouldSpawn)
        {
            // Reseta o timer para que o primeiro puff apareça imediatamente
            // assim que o player começar a andar de novo
            _stepTimer = 0f;
            return;
        }

        _stepTimer -= Time.deltaTime;

        if (_stepTimer <= 0f)
        {
            SpawnDust();
            _stepTimer = stepInterval;
        }
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    private void SpawnDust()
    {
        if (dustPrefab == null) return;

        GameObject instance = GetFromPool();
        if (instance == null) return;

        // Posição: centro do player + offset (pé)
        Vector3 spawnPos = transform.position;
        spawnPos.x += spawnOffset.x;   // X centrado (offset normalmente = 0)
        spawnPos.y += spawnOffset.y;   // Y sobe/desce até o pé
        instance.transform.position = spawnPos;
        instance.transform.SetParent(null);

        // Espelha conforme o facing do player
        Vector3 scale = dustScale;
        scale.x = Mathf.Abs(scale.x) * _player.FacingDirection;
        instance.transform.localScale = scale;

        instance.SetActive(true);

        // Reinicia o Animator para o início do clip (importante ao reutilizar do pool)
        var animator = instance.GetComponent<Animator>();
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }

        // Devolve ao pool após o lifetime
        StartCoroutine(ReturnAfterDelay(instance, dustLifetime));
    }

    // ── Pool ──────────────────────────────────────────────────────────────────

    private void BuildPool()
    {
        if (dustPrefab == null) return;

        _pool = new GameObject[poolSize];

        for (int i = 0; i < poolSize; i++)
        {
            GameObject go = Instantiate(dustPrefab, transform);
            go.SetActive(false);
            _pool[i] = go;
        }
    }

    private GameObject GetFromPool()
    {
        if (_pool == null) return null;

        // Pool circular: pega a próxima instância disponível
        for (int i = 0; i < _pool.Length; i++)
        {
            int idx = (_poolIndex + i) % _pool.Length;

            if (!_pool[idx].activeInHierarchy)
            {
                _poolIndex = (idx + 1) % _pool.Length;
                return _pool[idx];
            }
        }

        // Todas ocupadas: retorna null (não spawna para evitar glitches visuais)
        // Aumente poolSize no Inspector se isso acontecer com frequência.
        Debug.LogWarning("[DustFootstep] Pool esgotado. Aumente poolSize no Inspector.");
        return null;
    }

    private IEnumerator ReturnAfterDelay(GameObject instance, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (instance != null)
        {
            instance.SetActive(false);
            instance.transform.SetParent(transform, false);
        }
    }
}