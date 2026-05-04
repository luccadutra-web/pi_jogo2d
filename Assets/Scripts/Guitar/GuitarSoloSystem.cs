using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GuitarSoloSystem — Ataque especial rítmico.
///
/// Fluxo:
///   1. Player pressiona a tecla de ativação (Q por padrão)
///   2. Uma sequência aleatória de teclas é gerada e exibida via GuitarSoloUI
///   3. O player deve pressionar cada tecla dentro da janela de tempo (noteWindow)
///   4. Erro → cancela o especial inteiro
///   5. Sucesso → executa dano em área ao redor do player
///
/// Requisitos de cena:
///   - GuitarSoloUI presente na cena (qualquer GameObject com o componente)
///   - PlayerBehavior no mesmo GameObject
///   - Inimigos devem implementar IDamageable
/// </summary>
public class GuitarSoloSystem : MonoBehaviour
{
    // ── Configuração ──────────────────────────────────────────────────────────

    [System.Serializable]
    public class NoteKey
    {
        [Tooltip("Nome de exibição na UI (ex: A, D, SPACE)")]
        public string displayName = "A";

        [Tooltip("Caminho do input binding (ex: <Keyboard>/a)")]
        public string binding = "<Keyboard>/a";
    }

    [Header("Ativação")]
    [Tooltip("Binding da tecla que ativa o especial")]
    [SerializeField] private string activationBinding = "<Keyboard>/q";
    [SerializeField] private float  activationCooldown = 8f;

    [Header("Sequência")]
    [Tooltip("Pool de teclas que podem aparecer na sequência")]
    [SerializeField] private List<NoteKey> availableKeys = new List<NoteKey>
    {
        new NoteKey { displayName = "A",     binding = "<Keyboard>/a"     },
        new NoteKey { displayName = "D",     binding = "<Keyboard>/d"     },
        new NoteKey { displayName = "SPACE", binding = "<Keyboard>/space" },
    };

    [Tooltip("Quantas notas aparecem na sequência")]
    [SerializeField] [Range(2, 8)] private int sequenceLength = 4;

    [Tooltip("Segundos que o player tem para acertar cada nota")]
    [SerializeField] private float noteWindow = 1.2f;

    [Header("Dano em área")]
    [SerializeField] private float blastRadius  = 4f;
    [SerializeField] private int   blastDamage  = 5;
    [SerializeField] private float blastKnockback = 18f;
    [SerializeField] private LayerMask enemyLayer;

    [Header("Feedback Visual (opcional)")]
    [Tooltip("Partícula ou VFX disparado no sucesso")]
    [SerializeField] private GameObject successVFX;
    [Tooltip("Partícula ou VFX disparado no falha")]
    [SerializeField] private GameObject failVFX;

    // ── Estado interno ────────────────────────────────────────────────────────

    private InputAction         activationAction;
    private List<InputAction>   noteActions = new List<InputAction>();

    private bool  isActive        = false;
    private float cooldownTimer   = 0f;
    private int   currentNoteIndex = 0;

    private List<NoteKey>    currentSequence = new List<NoteKey>();
    private Coroutine        soloCoroutine;

    private PlayerBehavior   behavior;
    private GuitarSoloUI     ui;

    // ── Eventos públicos ──────────────────────────────────────────────────────

    public System.Action          OnSoloStarted;
    public System.Action<int>     OnNoteSuccess;   // índice da nota acertada
    public System.Action<int>     OnNoteFail;      // índice da nota que falhou
    public System.Action          OnSoloSuccess;
    public System.Action          OnSoloFail;
    public System.Action<float>   OnCooldownTick;  // 0→1 normalizado

    public bool  IsActive       => isActive;
    public float CooldownNorm   => Mathf.Clamp01(1f - cooldownTimer / activationCooldown);

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        behavior = GetComponent<PlayerBehavior>();
        ui       = FindFirstObjectByType<GuitarSoloUI>();

        // Ação de ativação
        activationAction = new InputAction("GuitarSolo", InputActionType.Button);
        activationAction.AddBinding(activationBinding);

        // Uma InputAction por tecla disponível
        foreach (var key in availableKeys)
        {
            var action = new InputAction(key.displayName, InputActionType.Button);
            action.AddBinding(key.binding);
            noteActions.Add(action);
        }
    }

    void OnEnable()
    {
        activationAction.Enable();
        foreach (var a in noteActions) a.Enable();
    }

    void OnDisable()
    {
        activationAction.Disable();
        foreach (var a in noteActions) a.Disable();
    }

    void Update()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            OnCooldownTick?.Invoke(CooldownNorm);
        }

        if (isActive) return;
        if (behavior != null && (behavior.isLocked || behavior.IsDashing)) return;

        if (activationAction.WasPressedThisFrame() && cooldownTimer <= 0f)
            StartSolo();
    }

    // ── Solo ──────────────────────────────────────────────────────────────────

    private void StartSolo()
    {
        if (availableKeys.Count == 0)
        {
            Debug.LogWarning("[GuitarSoloSystem] Nenhuma tecla configurada em availableKeys.");
            return;
        }

        currentSequence.Clear();
        for (int i = 0; i < sequenceLength; i++)
            currentSequence.Add(availableKeys[Random.Range(0, availableKeys.Count)]);

        isActive         = true;
        currentNoteIndex = 0;

        if (behavior != null) behavior.isLocked = true;

        ui?.ShowSequence(currentSequence, noteWindow);
        OnSoloStarted?.Invoke();

        soloCoroutine = StartCoroutine(SoloRoutine());
    }

    private IEnumerator SoloRoutine()
    {
        for (int i = 0; i < currentSequence.Count; i++)
        {
            currentNoteIndex = i;
            NoteKey expected = currentSequence[i];
            int     keyIndex = availableKeys.IndexOf(expected);

            float elapsed = 0f;
            bool  hit     = false;

            ui?.ActivateNote(i);

            while (elapsed < noteWindow)
            {
                elapsed += Time.deltaTime;

                // Checa a tecla correta
                if (keyIndex >= 0 && keyIndex < noteActions.Count)
                {
                    if (noteActions[keyIndex].WasPressedThisFrame())
                    {
                        hit = true;
                        break;
                    }
                }

                // Checa se o player pressionou uma tecla ERRADA
                for (int k = 0; k < noteActions.Count; k++)
                {
                    if (k == keyIndex) continue;
                    if (noteActions[k].WasPressedThisFrame())
                    {
                        // Tecla errada → falha imediata
                        OnNoteFail?.Invoke(i);
                        ui?.NoteFail(i);
                        yield return new WaitForSeconds(0.4f);
                        EndSolo(success: false);
                        yield break;
                    }
                }

                yield return null;
            }

            if (!hit)
            {
                // Timer esgotado → falha
                OnNoteFail?.Invoke(i);
                ui?.NoteFail(i);
                yield return new WaitForSeconds(0.4f);
                EndSolo(success: false);
                yield break;
            }

            OnNoteSuccess?.Invoke(i);
            ui?.NoteSuccess(i);

            // Pequena pausa entre notas
            yield return new WaitForSeconds(0.08f);
        }

        // Todas as notas acertadas
        yield return new WaitForSeconds(0.2f);
        EndSolo(success: true);
    }

    private void EndSolo(bool success)
    {
        isActive = false;
        StopCoroutine(soloCoroutine);

        if (behavior != null) behavior.isLocked = false;

        if (success)
        {
            cooldownTimer = activationCooldown;
            ExecuteBlast();
            OnSoloSuccess?.Invoke();
            ui?.ShowResult(true);

            if (successVFX != null)
                Instantiate(successVFX, transform.position, Quaternion.identity);
        }
        else
        {
            // Cooldown menor na falha — não pune tanto
            cooldownTimer = activationCooldown * 0.3f;
            OnSoloFail?.Invoke();
            ui?.ShowResult(false);

            if (failVFX != null)
                Instantiate(failVFX, transform.position, Quaternion.identity);
        }
    }

    // ── Dano em área ──────────────────────────────────────────────────────────

    private void ExecuteBlast()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, blastRadius, enemyLayer);

        foreach (Collider2D hit in hits)
        {
            var damageable = hit.GetComponent<IDamageable>()
                          ?? hit.GetComponentInParent<IDamageable>();

            if (damageable == null) continue;

            Vector2 dir   = ((Vector2)hit.transform.position - (Vector2)transform.position).normalized;
            Vector2 force = new Vector2(dir.x * blastKnockback, 3f);

            damageable.TakeDamage(blastDamage);
            damageable.ReceiveKnockback(force);

            // Stagger em todos os atingidos
            hit.GetComponent<IStaggerable>()?.Stagger();
            hit.GetComponentInParent<IStaggerable>()?.Stagger();
        }

        HitStop.Instance?.DoHitStop(0.18f);
    }

    // ── Gizmo ─────────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, blastRadius);
    }
}