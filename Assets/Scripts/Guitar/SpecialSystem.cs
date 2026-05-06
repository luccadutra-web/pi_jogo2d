using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SpecialSystem — Ataque especial rítmico.
///
/// FLUXO:
///   1. Inimigo morre → AddEnergy() é chamado externamente
///   2. Barra cheia → player pressiona Q para ativar
///   3. Sequência de teclas aparece na UI
///   4. Acertou todas → ExecuteBlast() (AOE)
///   5. Errou qualquer uma → cancela, consome a energia
///
/// INTEGRAÇÃO:
///   - Chame SpecialSystem.Instance.AddEnergy(amount) ao matar inimigo
///   - SpecialUI lê os eventos públicos para animar a UI
/// </summary>
public class SpecialSystem : MonoBehaviour
{
    public static SpecialSystem Instance { get; private set; }

    // ── Energia ───────────────────────────────────────────────────────────────

    [Header("Energia")]
    [Tooltip("Energia máxima necessária para ativar o especial")]
    [SerializeField] private float maxEnergy = 100f;

    [Tooltip("Energia ganha por matar um inimigo")]
    [SerializeField] private float energyPerKill = 34f; // 3 kills = cheio

    // ── Ativação ──────────────────────────────────────────────────────────────

    [Header("Ativação")]
    [SerializeField] private string activationBinding = "<Keyboard>/f";

    // ── Sequência ─────────────────────────────────────────────────────────────

    [System.Serializable]
    public class NoteKey
    {
        public string displayName = "A";
        public string binding     = "<Keyboard>/a";
    }

    [Header("Sequência")]
    [SerializeField] private List<NoteKey> availableKeys = new List<NoteKey>
    {
        new NoteKey { displayName = "A",     binding = "<Keyboard>/a"     },
        new NoteKey { displayName = "D",     binding = "<Keyboard>/d"     },
        new NoteKey { displayName = "W",     binding = "<Keyboard>/w"     },
        new NoteKey { displayName = "S",     binding = "<Keyboard>/s"     },
        new NoteKey { displayName = "SPACE", binding = "<Keyboard>/space" },
    };

    [Tooltip("Quantas notas na sequência")]
    [SerializeField] [Range(2, 8)] private int sequenceLength = 4;

    [Tooltip("Segundos para acertar cada nota")]
    [SerializeField] private float noteWindow = 1.0f;

    // ── Blast (AOE) ───────────────────────────────────────────────────────────

    [Header("Blast AOE")]
    [SerializeField] private float     blastRadius   = 4f;
    [SerializeField] private int       blastDamage   = 8;
    [SerializeField] private float     blastKnockback = 20f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Feedback")]
    [SerializeField] private GameObject successVFX;
    [SerializeField] private GameObject failVFX;

    // ── Eventos públicos (UI assina estes) ────────────────────────────────────

    public System.Action<float, float>          OnEnergyChanged;   // current, max
    public System.Action<List<NoteKey>, float>  OnSoloStarted;     // sequência, noteWindow
    public System.Action<int>                   OnNoteActivated;   // índice
    public System.Action<int>                   OnNoteSuccess;     // índice
    public System.Action<int>                   OnNoteFail;        // índice
    public System.Action                        OnSoloSuccess;
    public System.Action                        OnSoloFail;

    // ── Propriedades públicas ─────────────────────────────────────────────────

    public float CurrentEnergy    => _energy;
    public float MaxEnergy        => maxEnergy;
    public float EnergyNormalized => maxEnergy > 0f ? _energy / maxEnergy : 0f;
    public bool  IsActive         => _isActive;
    public bool  IsReady          => _energy >= maxEnergy && !_isActive;

    // ── Estado interno ────────────────────────────────────────────────────────

    private float _energy   = 0f;
    private bool  _isActive = false;

    private List<NoteKey>  _sequence   = new List<NoteKey>();
    private List<InputAction> _noteActions = new List<InputAction>();
    private InputAction    _activationAction;
    private Coroutine      _soloRoutine;

    private PlayerBehavior _behavior;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        _behavior = GetComponent<PlayerBehavior>();

        // Ação de ativação
        _activationAction = new InputAction("Special", InputActionType.Button);
        _activationAction.AddBinding(activationBinding);

        // Uma InputAction por tecla disponível
        foreach (var key in availableKeys)
        {
            var action = new InputAction(key.displayName, InputActionType.Button);
            action.AddBinding(key.binding);
            _noteActions.Add(action);
        }
    }

    void OnEnable()
    {
        _activationAction.Enable();
        foreach (var a in _noteActions) a.Enable();
    }

    void OnDisable()
    {
        _activationAction.Disable();
        foreach (var a in _noteActions) a.Disable();
    }

    void Update()
    {
        if (_isActive) return;
        if (_behavior != null && (_behavior.isLocked || _behavior.IsDashing)) return;
        if (!IsReady) return;

        if (_activationAction.WasPressedThisFrame())
            StartSolo();
    }

    // ── Energia ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Chame quando um inimigo morrer.
    /// Ex: SpecialSystem.Instance?.AddEnergy(energyPerKill);
    /// </summary>
    public void AddEnergy(float amount)
    {
        if (_isActive) return;

        _energy = Mathf.Min(_energy + amount, maxEnergy);
        OnEnergyChanged?.Invoke(_energy, maxEnergy);
    }

    /// <summary>
    /// Atalho: adiciona a energia padrão configurada no Inspector.
    /// </summary>
    public void AddKillEnergy() => AddEnergy(energyPerKill);

    private void ConsumeEnergy()
    {
        _energy = 0f;
        OnEnergyChanged?.Invoke(_energy, maxEnergy);
    }

    // ── Solo ──────────────────────────────────────────────────────────────────

    private void StartSolo()
    {
        if (availableKeys.Count == 0) return;

        // Gera sequência aleatória
        _sequence.Clear();
        for (int i = 0; i < sequenceLength; i++)
            _sequence.Add(availableKeys[Random.Range(0, availableKeys.Count)]);

        _isActive = true;
        if (_behavior != null) _behavior.isLocked = true;

        OnSoloStarted?.Invoke(new List<NoteKey>(_sequence), noteWindow);

        _soloRoutine = StartCoroutine(SoloRoutine());
    }

    private IEnumerator SoloRoutine()
    {
        for (int i = 0; i < _sequence.Count; i++)
        {
            NoteKey expected = _sequence[i];
            int     keyIndex = availableKeys.IndexOf(expected);

            OnNoteActivated?.Invoke(i);

            float elapsed = 0f;
            bool  hit     = false;

            while (elapsed < noteWindow)
            {
                elapsed += Time.deltaTime;

                // Tecla correta
                if (keyIndex >= 0 && keyIndex < _noteActions.Count)
                {
                    if (_noteActions[keyIndex].WasPressedThisFrame())
                    {
                        hit = true;
                        break;
                    }
                }

                // Tecla errada → falha imediata
                for (int k = 0; k < _noteActions.Count; k++)
                {
                    if (k == keyIndex) continue;
                    if (_noteActions[k].WasPressedThisFrame())
                    {
                        OnNoteFail?.Invoke(i);
                        yield return new WaitForSeconds(0.5f);
                        EndSolo(success: false);
                        yield break;
                    }
                }

                yield return null;
            }

            if (!hit)
            {
                // Timeout
                OnNoteFail?.Invoke(i);
                yield return new WaitForSeconds(0.5f);
                EndSolo(success: false);
                yield break;
            }

            OnNoteSuccess?.Invoke(i);
            yield return new WaitForSeconds(0.07f);
        }

        // Todas acertadas
        yield return new WaitForSeconds(0.15f);
        EndSolo(success: true);
    }

    private void EndSolo(bool success)
    {
        _isActive = false;

        if (_soloRoutine != null)
        {
            StopCoroutine(_soloRoutine);
            _soloRoutine = null;
        }

        if (_behavior != null) _behavior.isLocked = false;

        ConsumeEnergy(); // sempre consome, acertou ou não

        if (success)
        {
            ExecuteBlast();
            OnSoloSuccess?.Invoke();
            if (successVFX) Instantiate(successVFX, transform.position, Quaternion.identity);
        }
        else
        {
            OnSoloFail?.Invoke();
            if (failVFX) Instantiate(failVFX, transform.position, Quaternion.identity);
        }
    }

    // ── Blast AOE ─────────────────────────────────────────────────────────────

    private void ExecuteBlast()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, blastRadius, enemyLayer);

        foreach (Collider2D col in hits)
        {
            // Dano
            var damageable = col.GetComponent<IDamageable>()
                          ?? col.GetComponentInParent<IDamageable>();
            damageable?.TakeDamage(blastDamage, transform.position);

            // Knockback
            var rb = col.GetComponent<Rigidbody2D>()
                  ?? col.GetComponentInParent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 dir = ((Vector2)col.transform.position - (Vector2)transform.position).normalized;
                rb.AddForce(new Vector2(dir.x * blastKnockback, 3f), ForceMode2D.Impulse);
            }

            // Stagger
            var staggerable = col.GetComponent<IStaggerable>()
                           ?? col.GetComponentInParent<IStaggerable>();
            staggerable?.Stagger();
        }

        HitStop.Instance?.DoHitStop(0.20f);
        CameraShake.Instance?.Shake(0.25f, 0.35f);
    }

    // ── Gizmo ─────────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, blastRadius);
    }
}