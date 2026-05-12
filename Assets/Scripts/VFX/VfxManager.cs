using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// VfxManager — singleton central de efeitos visuais do jogo.
///
/// ── RESPONSABILIDADES ────────────────────────────────────────────────────────
///
///   • Pool de prefabs de VFX (slash trails, hit impacts, parry, counter window,
///     enemy hurt, poise break).
///   • Spawna e ancora slash trails no transform da arma durante o ataque.
///   • Spawna hit impacts no ponto de contato com o inimigo.
///   • Spawna efeitos de parry, counter window, hurt e poise break.
///
/// ── SETUP NO INSPECTOR ───────────────────────────────────────────────────────
///
///   1. Adicione VfxManager a um GameObject persistente na cena (ex.: "Managers").
///   2. Arraste os prefabs nos campos correspondentes.
///   3. Ajuste poolSize se precisar de mais instâncias simultâneas.
///
/// ── PREFABS NECESSÁRIOS ──────────────────────────────────────────────────────
///
///   Slash Trails  (SlashTrail.cs + TrailRenderer):
///     - slashTrailLightPrefab    — trail fino e rápido (light attack)
///     - slashTrailHeavyPrefab    — trail médio e laranja (heavy attack)
///     - slashTrailFinisherPrefab — trail largo e dourado (finisher)
///
///   Hit Impacts  (SpriteRenderer + Animator, lifecycle gerenciado por VfxAutoReturn):
///     - hitImpactLightPrefab
///     - hitImpactHeavyPrefab
///     - hitImpactFinisherPrefab
///     - hitImpactCounterPrefab
///
///   Outros:
///     - parryFxPrefab            — flash de parry (SpriteRenderer/Particles)
///     - counterWindowPrefab      — indicador de janela de counter (UI/Sprite)
///     - enemyHurtFxPrefab        — partículas de hit no inimigo
///     - poiseBreakFxPrefab       — burst de partículas de poise break
///
/// ── LIFECYCLE DOS OBJETOS POOLADOS ───────────────────────────────────────────
///
///   Objetos com VfxAutoReturn retornam ao pool automaticamente após lifetime.
///   Slash trails retornam após fade out (controlado pelo SlashTrail).
///   Counter window é filho do player transform — retorna via VfxAutoReturn.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class VfxManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static VfxManager Instance { get; private set; }

    // ── Slash Trails ──────────────────────────────────────────────────────────

    [Header("Slash Trails")]
    [Tooltip("Prefab do trail para ataques leves. Deve ter SlashTrail + TrailRenderer.")]
    [SerializeField] private GameObject slashTrailLightPrefab;

    [Tooltip("Prefab do trail para ataques pesados.")]
    [SerializeField] private GameObject slashTrailHeavyPrefab;

    [Tooltip("Prefab do trail para o finisher.")]
    [SerializeField] private GameObject slashTrailFinisherPrefab;

    // ── Hit Impacts ───────────────────────────────────────────────────────────

    [Header("Hit Impacts")]
    [Tooltip("VFX de impacto para ataque leve.")]
    [SerializeField] private GameObject hitImpactLightPrefab;

    [Tooltip("VFX de impacto para ataque pesado.")]
    [SerializeField] private GameObject hitImpactHeavyPrefab;

    [Tooltip("VFX de impacto para o finisher.")]
    [SerializeField] private GameObject hitImpactFinisherPrefab;

    [Tooltip("VFX de impacto para o counter attack.")]
    [SerializeField] private GameObject hitImpactCounterPrefab;

    // ── Outros VFX ────────────────────────────────────────────────────────────

    [Header("Parry & Counter")]
    [Tooltip("Flash / partículas do parry bem-sucedido.")]
    [SerializeField] private GameObject parryFxPrefab;

    [Tooltip("Offset local do efeito de parry em relação ao centro do player.\n" +
             "X é espelhado automaticamente conforme o facing.\n" +
             "Ajuste no Inspector até encaixar na ponta da arma.")]
    [SerializeField] private Vector2 parryFxOffset = new Vector2(0.5f, 0.5f);

    [Tooltip("Indicador visual da janela de counter (filho do player).")]
    [SerializeField] private GameObject counterWindowPrefab;

    [Header("Enemy Feedback")]
    [Tooltip("Partículas spawnadas no inimigo ao receber dano.")]
    [SerializeField] private GameObject enemyHurtFxPrefab;

    [Tooltip("Burst de partículas de poise break.")]
    [SerializeField] private GameObject poiseBreakFxPrefab;

    [Header("Player Feedback")]
    [Tooltip("Burst de poeira no pé do player ao executar parry bem-sucedido.")]
    [SerializeField] private GameObject dustParryFxPrefab;

    [Tooltip("Offset em relação ao centro do player para o dust de parry.\n" +
             "Y negativo desce até o pé. X é espelhado conforme o facing.")]
    [SerializeField] private Vector2 dustFxOffset = new Vector2(0f, -0.5f);

    // ── Pool ──────────────────────────────────────────────────────────────────

    [Header("Pool")]
    [Tooltip("Tamanho inicial de cada pool. Aumente se ver objetos sendo criados em runtime.")]
    [SerializeField] private int poolSize = 8;

    [Tooltip("Lifetime padrão (segundos) para objetos sem VfxAutoReturn explícito.")]
    [SerializeField] private float defaultLifetime = 1.5f;

    // ── Estado interno ────────────────────────────────────────────────────────

    // Trail ativo atual (ancoramos ao weapon transform durante o ataque)
    private GameObject _activeTrail;

    // Pools por prefab
    private readonly Dictionary<GameObject, Queue<GameObject>> _pools =
        new Dictionary<GameObject, Queue<GameObject>>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Pre-aquecer pools
        PrewarmPool(slashTrailLightPrefab,    poolSize);
        PrewarmPool(slashTrailHeavyPrefab,    poolSize);
        PrewarmPool(slashTrailFinisherPrefab, poolSize);
        PrewarmPool(hitImpactLightPrefab,     poolSize);
        PrewarmPool(hitImpactHeavyPrefab,     poolSize);
        PrewarmPool(hitImpactFinisherPrefab,  poolSize);
        PrewarmPool(hitImpactCounterPrefab,   poolSize);
        PrewarmPool(parryFxPrefab,            poolSize / 2);
        PrewarmPool(counterWindowPrefab,      2);
        PrewarmPool(enemyHurtFxPrefab,        poolSize);
        PrewarmPool(poiseBreakFxPrefab,       poolSize / 2);
        PrewarmPool(dustParryFxPrefab,        poolSize / 2);
    }

    // ── API pública — Slash Trails ────────────────────────────────────────────

    /// <summary>
    /// Spawna e ancora um slash trail ao transform da arma.
    /// Chame no início do startup do ataque (junto com o trigger da animação).
    /// </summary>
    /// <param name="type">"light", "heavy" ou "finisher"</param>
    /// <param name="weaponTransform">Transform ao qual o trail será ancorado.</param>
    /// <param name="comboStep">Step atual do combo (para escalar trail se necessário).</param>
    public void SpawnSlashTrail(string type, Transform weaponTransform, int comboStep = 1)
    {
        // Devolve o trail anterior ao pool, se ainda estiver ativo
        ReturnActiveTrail();

        GameObject prefab = SelectTrailPrefab(type);
        if (prefab == null) return;

        GameObject instance = GetFromPool(prefab);
        if (instance == null) return;

        // Ancora ao weapon transform
        instance.transform.SetParent(weaponTransform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;

        instance.SetActive(true);
        _activeTrail = instance;

        // Inicia coroutine de devolução ao pool após o trail
        StartCoroutine(ReturnTrailAfterFade(instance, prefab));
    }

    /// <summary>
    /// Desancora o trail ativo da arma, iniciando o fade out.
    /// Chamado automaticamente no SpawnSlashTrail ou pode ser chamado
    /// manualmente ao fim do recovery.
    /// </summary>
    public void ReturnActiveTrail()
    {
        if (_activeTrail == null) return;

        // Desancora: SlashTrail detecta OnTransformParentChanged e inicia fade
        _activeTrail.transform.SetParent(transform, true);
        _activeTrail = null;
    }

    // ── API pública — Hit Impacts ─────────────────────────────────────────────

    /// <summary>
    /// Spawna um VFX de impacto no ponto de contato com o inimigo.
    /// </summary>
    /// <param name="impactType">"light", "heavy", "finisher" ou "counter"</param>
    /// <param name="position">Posição do impacto no mundo.</param>
    /// <param name="direction">Direção do golpe (para rotacionar o VFX).</param>
    /// <param name="isCounter">Se true, força o prefab de counter.</param>
    /// <param name="comboStep">Step do combo atual.</param>
    public void SpawnHitImpact(string impactType, Vector2 position, Vector2 direction,
                               bool isCounter = false, int comboStep = 1)
    {
        GameObject prefab = isCounter ? hitImpactCounterPrefab : SelectImpactPrefab(impactType);
        if (prefab == null) return;

        GameObject instance = GetFromPool(prefab);
        if (instance == null) return;

        instance.transform.SetParent(null);
        instance.transform.position = position;

        // Rotaciona o VFX na direção do golpe
        if (direction != Vector2.zero)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            instance.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        instance.SetActive(true);
        EnsureAutoReturn(instance, prefab, defaultLifetime);
    }

    // ── API pública — Parry & Counter ─────────────────────────────────────────

    /// <summary>
    /// Spawna o flash de parry na posição do player.
    /// </summary>
    /// <param name="position">Posição do player.</param>
    /// <param name="facing">Direção do player (+1 direita, -1 esquerda).</param>
    public void SpawnParry(Vector3 position, float facing = 1f)
    {
        if (parryFxPrefab == null) return;

        GameObject instance = GetFromPool(parryFxPrefab);
        if (instance == null) return;

        instance.transform.SetParent(null);

        // Aplica offset espelhado conforme o facing do player
        Vector3 spawnPos = position;
        spawnPos.x += parryFxOffset.x * facing;
        spawnPos.y += parryFxOffset.y;
        instance.transform.position = spawnPos;

        // Espelha o scale na direção do player
        Vector3 scale = instance.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * Mathf.Sign(facing);
        instance.transform.localScale = scale;

        instance.SetActive(true);

        // Força Play() — evita delay de um frame ao reutilizar do pool
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear();
            ps.Play();
        }

        EnsureAutoReturn(instance, parryFxPrefab, defaultLifetime);
    }

    /// <summary>
    /// Spawna o indicador de janela de counter como filho do player transform.
    /// O VfxAutoReturn cuida de devolver ao pool após o tempo da janela.
    /// </summary>
    /// <param name="playerTransform">Transform do player.</param>
    public void SpawnCounterWindow(Transform playerTransform)
    {
        if (counterWindowPrefab == null) return;

        GameObject instance = GetFromPool(counterWindowPrefab);
        if (instance == null) return;

        instance.transform.SetParent(playerTransform, false);
        instance.transform.localPosition = Vector3.zero;

        instance.SetActive(true);
        EnsureAutoReturn(instance, counterWindowPrefab, defaultLifetime);
    }

    // ── API pública — Enemy Feedback ──────────────────────────────────────────

    /// <summary>
    /// Spawna partículas de hurt no inimigo ao receber dano.
    /// </summary>
    public void SpawnEnemyHurt(Vector3 position)
    {
        SpawnSimple(enemyHurtFxPrefab, position);
    }

    /// <summary>
    /// Spawna burst de partículas de poise break.
    /// </summary>
    public void SpawnPoiseBreak(Vector3 position)
    {
        SpawnSimple(poiseBreakFxPrefab, position);
    }

    /// <summary>
    /// Spawna burst de poeira no pé do player ao executar parry.
    /// O offset X é espelhado automaticamente conforme o facing.
    /// </summary>
    /// <param name="position">Posição do centro do player.</param>
    /// <param name="facing">Direção do player (+1 direita, -1 esquerda).</param>
    public void SpawnDustParry(Vector3 position, float facing = 1f)
    {
        if (dustParryFxPrefab == null) return;

        GameObject instance = GetFromPool(dustParryFxPrefab);
        if (instance == null) return;

        instance.transform.SetParent(null);

        // Aplica offset: Y desce ao chão, X espelhado conforme facing
        Vector3 spawnPos = position;
        spawnPos.x += dustFxOffset.x * facing;
        spawnPos.y += dustFxOffset.y;
        instance.transform.position = spawnPos;

        instance.SetActive(true);

        // Força Play() em todos os ParticleSystems filhos — evita o delay
        // de um frame que ocorre ao reutilizar objetos do pool via SetActive
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>())
        {
            ps.Clear();
            ps.Play();
        }

        EnsureAutoReturn(instance, dustParryFxPrefab, defaultLifetime);
    }

    // ── Helpers privados ──────────────────────────────────────────────────────

    private void SpawnSimple(GameObject prefab, Vector3 position)
    {
        if (prefab == null) return;

        GameObject instance = GetFromPool(prefab);
        if (instance == null) return;

        instance.transform.SetParent(null);
        instance.transform.position = position;
        instance.SetActive(true);
        EnsureAutoReturn(instance, prefab, defaultLifetime);
    }

    private GameObject SelectTrailPrefab(string type) => type switch
    {
        "heavy"    => slashTrailHeavyPrefab,
        "finisher" => slashTrailFinisherPrefab,
        _          => slashTrailLightPrefab,
    };

    private GameObject SelectImpactPrefab(string type) => type switch
    {
        "heavy"    => hitImpactHeavyPrefab,
        "finisher" => hitImpactFinisherPrefab,
        "counter"  => hitImpactCounterPrefab,
        _          => hitImpactLightPrefab,
    };

    // ── Pool ──────────────────────────────────────────────────────────────────

    private void PrewarmPool(GameObject prefab, int count)
    {
        if (prefab == null) return;

        if (!_pools.ContainsKey(prefab))
            _pools[prefab] = new Queue<GameObject>();

        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(prefab, transform);
            go.SetActive(false);
            _pools[prefab].Enqueue(go);
        }
    }

    private GameObject GetFromPool(GameObject prefab)
    {
        if (prefab == null) return null;

        if (!_pools.ContainsKey(prefab))
            _pools[prefab] = new Queue<GameObject>();

        if (_pools[prefab].Count > 0)
            return _pools[prefab].Dequeue();

        // Pool vazio: cria novo (sem limite — ajuste poolSize se necessário)
        GameObject go = Instantiate(prefab, transform);
        go.SetActive(false);
        return go;
    }

    /// <summary>
    /// Devolve um objeto ao pool e o desativa.
    /// Chamado por VfxAutoReturn ou pela coroutine de fade do trail.
    /// </summary>
    public void ReturnToPool(GameObject instance, GameObject prefab)
    {
        if (instance == null) return;

        instance.SetActive(false);
        instance.transform.SetParent(transform, false);

        if (!_pools.ContainsKey(prefab))
            _pools[prefab] = new Queue<GameObject>();

        _pools[prefab].Enqueue(instance);
    }

    // Garante que o objeto tem um VfxAutoReturn configurado para devolver ao pool
    private void EnsureAutoReturn(GameObject instance, GameObject prefab, float lifetime)
    {
        var ar = instance.GetComponent<VfxAutoReturn>();
        if (ar == null)
            ar = instance.AddComponent<VfxAutoReturn>();

        ar.Initialize(instance, prefab, lifetime, this);
    }

    // Aguarda o trail terminar o fade antes de devolver ao pool
    private IEnumerator ReturnTrailAfterFade(GameObject instance, GameObject prefab)
    {
        yield return new WaitForSeconds(defaultLifetime);

        if (instance != null && instance.activeInHierarchy)
            ReturnToPool(instance, prefab);
    }
}