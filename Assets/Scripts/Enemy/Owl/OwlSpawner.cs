using System.Collections;
using UnityEngine;


public class OwlSpawner : MonoBehaviour
{
    // ─── Modo ─────────────────────────────────────────────────────────────────

    public enum SpawnMode { OnStart, OnPlayerNear, Wave }

    [Header("Modo de Spawn")]
    [Tooltip("OnStart     → spawna ao iniciar a cena\n" +
             "OnPlayerNear → spawna quando player entra no raio\n" +
             "Wave        → spawna em ondas repetidas")]
    [SerializeField] private SpawnMode spawnMode = SpawnMode.OnPlayerNear;

    // ─── Referências ──────────────────────────────────────────────────────────

    [Header("Referências")]
    [Tooltip("Prefab da coruja com OwlEnemy.cs.")]
    [SerializeField] private GameObject owlPrefab;

    [Tooltip("Pontos de spawn. Se vazio, usa a posição deste GameObject.")]
    [SerializeField] private Transform[] spawnPoints;

    // ─── Limites ──────────────────────────────────────────────────────────────

    [Header("Limites")]
    [Tooltip("Máximo de corujas vivas simultaneamente.")]
    [SerializeField] private int maxAlive = 3;

    [Tooltip("Total máximo de corujas que este spawner cria durante toda a sessão.\n" +
             "0 = ilimitado.")]
    [SerializeField] private int totalLimit = 0;

    [Tooltip("Intervalo (segundos) entre cada spawn individual.")]
    [SerializeField] private float spawnInterval = 2.5f;

    // ─── Wave ─────────────────────────────────────────────────────────────────

    [Header("Wave (só no modo Wave)")]
    [Tooltip("Corujas por onda.")]
    [SerializeField] private int owlsPerWave = 2;

    [Tooltip("Pausa entre ondas (segundos).")]
    [SerializeField] private float waveCooldown = 6f;

    [Tooltip("Número de ondas. 0 = infinito.")]
    [SerializeField] private int totalWaves = 0;

    // ─── OnPlayerNear ─────────────────────────────────────────────────────────

    [Header("OnPlayerNear (só nesse modo)")]
    [Tooltip("Raio de detecção do player para acionar o spawn.")]
    [SerializeField] private float triggerRange = 8f;

    [Tooltip("Spawna apenas uma vez ao detectar, ou continua enquanto player estiver perto.")]
    [SerializeField] private bool spawnOnlyOnce = false;

    // ─── Debug ────────────────────────────────────────────────────────────────

    [Header("Gate (opcional)")]
    [Tooltip("Arraste o OwlGate que este spawner deve notificar ao matar corujas.\n" +
             "Deixe vazio se não tiver portão nesta área.")]
    [SerializeField] private OwlGate owlGate;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;

    // ─── Estado ───────────────────────────────────────────────────────────────

    private int   _totalSpawned  = 0;
    private int   _waveCount     = 0;
    private bool  _triggered     = false;   // para spawnOnlyOnce
    private bool  _spawning      = false;

    private Transform _playerTransform;

    // ─── Unity ────────────────────────────────────────────────────────────────

    private void Start()
    {
        var playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            _playerTransform = playerObj.transform;
        else
            Debug.LogWarning("[OwlSpawner] Player não encontrado — adicione a tag 'Player'.");

        if (owlPrefab == null)
        {
            Debug.LogError("[OwlSpawner] Owl Prefab não atribuído!");
            enabled = false;
            return;
        }

        // Valida se totalLimit pode impedir o portão de abrir
        if (owlGate != null && spawnMode == SpawnMode.Wave && totalLimit > 0)
        {
            int totalWillSpawn = owlsPerWave * (totalWaves > 0 ? totalWaves : 1);
            if (totalLimit < totalWillSpawn)
                Debug.LogWarning($"[OwlSpawner] totalLimit ({totalLimit}) é menor que o total de corujas das ondas ({totalWillSpawn}). O portão pode não abrir!");
        }

        if (spawnMode == SpawnMode.OnStart)
            StartCoroutine(SpawnLoop());

        if (spawnMode == SpawnMode.Wave)
            StartCoroutine(WaveLoop());
    }

    private void Update()
    {
        if (spawnMode != SpawnMode.OnPlayerNear) return;
        if (_playerTransform == null) return;
        if (spawnOnlyOnce && _triggered) return;

        float dist = Vector2.Distance(transform.position, _playerTransform.position);
        if (dist <= triggerRange && !_spawning)
        {
            _triggered = true;
            StartCoroutine(SpawnLoop());
        }
    }

    // ─── Loops ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawna corujas uma a uma com intervalo, respeitando maxAlive e totalLimit.
    /// </summary>
    private IEnumerator SpawnLoop()
    {
        _spawning = true;

        while (!ReachedTotalLimit())
        {
            if (CountAlive() < maxAlive)
            {
                SpawnOne();
                yield return new WaitForSeconds(spawnInterval);
            }
            else
            {
                // Aguarda uma coruja morrer antes de tentar de novo
                yield return new WaitForSeconds(1f);
            }

            // No modo OnPlayerNear sem loop, para após spawnar maxAlive
            if (spawnMode == SpawnMode.OnPlayerNear && spawnOnlyOnce)
                break;
        }

        _spawning = false;
        Log("SpawnLoop encerrado.");
    }

    /// <summary>
    /// Modo Wave: spawna N corujas por onda com pausa entre ondas.
    /// </summary>
    private IEnumerator WaveLoop()
    {
        while (!ReachedTotalLimit() && (totalWaves == 0 || _waveCount < totalWaves))
        {
            _waveCount++;
            Log($"Onda {_waveCount} iniciada — {owlsPerWave} corujas");

            int spawned = 0;
            while (spawned < owlsPerWave && !ReachedTotalLimit())
            {
                if (CountAlive() < maxAlive)
                {
                    SpawnOne();
                    spawned++;
                    yield return new WaitForSeconds(spawnInterval);
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }
            }

            Log($"Onda {_waveCount} completa. Aguardando {waveCooldown}s...");

            // Só espera o cooldown se ainda houver ondas pela frente
            bool hasMoreWaves = totalWaves == 0 || _waveCount < totalWaves;
            if (hasMoreWaves && !ReachedTotalLimit())
                yield return new WaitForSeconds(waveCooldown);
        }

        Log("Todas as ondas concluídas. Aguardando mortes para abrir o portão...");
    }

    // ─── Spawn ────────────────────────────────────────────────────────────────

    private void SpawnOne()
    {
        Transform point = GetSpawnPoint();
        var owl = Instantiate(owlPrefab, point.position, Quaternion.identity);
        _totalSpawned++;
        Log($"Spawnou coruja #{_totalSpawned} em {point.position}");

        // Notifica o gate quando esta coruja morrer
        if (owlGate != null)
        {
            var owlEnemy = owl.GetComponent<OwlEnemy>();
            if (owlEnemy != null)
                owlEnemy.OnDeath += () => owlGate.OnOwlKilled();
        }
    }

    private Transform GetSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform;

        // Escolhe um ponto aleatório que não esteja muito perto do player
        // para evitar spawn em cima dele
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var candidate = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (_playerTransform == null) return candidate;

            float dist = Vector2.Distance(candidate.position, _playerTransform.position);
            if (dist > 2f) return candidate;
        }

        // Fallback: qualquer ponto
        return spawnPoints[Random.Range(0, spawnPoints.Length)];
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Conta corujas vivas desta cena com tag "Enemy" que tenham OwlEnemy.
    /// </summary>
    private int CountAlive()
    {
        return FindObjectsByType<OwlEnemy>(FindObjectsSortMode.None).Length;
    }

    private bool ReachedTotalLimit()
        => totalLimit > 0 && _totalSpawned >= totalLimit;

    private void Log(string msg) { if (debugLog) Debug.Log($"[OwlSpawner] {msg}"); }

    // ─── Gizmos ───────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        // Raio de detecção (modo OnPlayerNear)
        if (spawnMode == SpawnMode.OnPlayerNear)
        {
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, triggerRange);
        }

        // Pontos de spawn
        if (spawnPoints != null)
        {
            Gizmos.color = Color.cyan;
            foreach (var p in spawnPoints)
            {
                if (p == null) continue;
                Gizmos.DrawWireSphere(p.position, 0.3f);
                Gizmos.DrawLine(transform.position, p.position);
            }
        }
    }
}