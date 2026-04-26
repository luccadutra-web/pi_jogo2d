using UnityEngine;
using UnityEngine.InputSystem;

public class SpecialAttack : MonoBehaviour
{
    public static SpecialAttack Instance { get; private set; }

    enum SpecialState { Idle, Playing, Success, Fail }
    private SpecialState state = SpecialState.Idle;

    [Header("Custo")]
    [SerializeField] private int cost = 30;

    [Header("Cooldown geral")]
    [SerializeField] private float cooldown = 3f;

    [Header("Timer por tecla")]
    [SerializeField] private float timePerKey = 2f;

    [Header("Sequência — editável pelo Inspector")]
    [SerializeField] private Key[] sequence = { Key.A, Key.D, Key.A };

    [Header("Efeito especial")]
    [SerializeField] private float damageRadius = 3f;
    [SerializeField] private int   damageAmount = 999;
    [SerializeField] private LayerMask enemyLayer;

    private int   currentIndex;
    private float nextTime;   // agora usa Time.unscaledTime — não congela com timeScale = 0
    private float keyTimer;

    private InputAction    specialAction;
    private PlayerPoints   points;
    private PlayerBehavior player;

    void Awake()
    {
        Instance      = this;
        specialAction = new InputAction("Special", InputActionType.Button, "<Keyboard>/f");
        points        = GetComponent<PlayerPoints>();
        player        = GetComponent<PlayerBehavior>();
    }

    void OnEnable()  => specialAction.Enable();
    void OnDisable() => specialAction.Disable();

    void Update()
    {
        switch (state)
        {
            case SpecialState.Idle:    CheckStart();    break;
            case SpecialState.Playing: CheckSequence(); break;
        }
    }

    // ── Início ────────────────────────────────────────────────────────────────

    void CheckStart()
    {
        if (!specialAction.WasPressedThisFrame()) return;

        // FIX: trocado Time.time por Time.unscaledTime
        // Time.time congela quando timeScale = 0, então o cooldown nunca avançava
        // após o solo terminar — o player ficava bloqueado de usar o especial novamente
        if (Time.unscaledTime < nextTime)    return;
        if (!points.UsePoints(cost))         return;
        StartSolo();
    }

    void StartSolo()
    {
        Debug.Log("SOLO START");
        state           = SpecialState.Playing;
        currentIndex    = 0;
        keyTimer        = timePerKey;
        player.isLocked = true;

        // TIME STOP: pausa o mundo durante o solo
        // UI e input continuam via unscaledDeltaTime / WaitForSecondsRealtime
        Time.timeScale = 0f;

        BuildTrackMap(out string[] keyLabels, out int[] trackIndices);
        SoloGuitarUI.Instance?.ShowSolo(keyLabels, trackIndices);
    }

    void BuildTrackMap(out string[] keyLabels, out int[] trackIndices)
    {
        keyLabels    = new string[sequence.Length];
        trackIndices = new int[sequence.Length];

        var keyToTrack = new System.Collections.Generic.Dictionary<Key, int>();
        int nextTrack  = 0;

        for (int i = 0; i < sequence.Length; i++)
        {
            Key k = sequence[i];
            if (!keyToTrack.ContainsKey(k))
                keyToTrack[k] = nextTrack++;

            keyLabels[i]    = KeyToDisplay(k);
            trackIndices[i] = keyToTrack[k];
        }
    }

    // ── Sequência ─────────────────────────────────────────────────────────────

    void CheckSequence()
    {
        // FIX: trocado Time.deltaTime por Time.unscaledDeltaTime
        // com timeScale = 0, deltaTime é sempre 0 — o timer nunca contava regressivamente
        // fazendo o "tempo esgotado" nunca disparar
        keyTimer -= Time.unscaledDeltaTime;
        if (keyTimer <= 0f)
        {
            Debug.Log("TEMPO ESGOTADO: " + sequence[currentIndex]);
            Fail();
            return;
        }

        Key expected = sequence[currentIndex];

        if (Keyboard.current[expected].wasPressedThisFrame)
        {
            bool inWindow = SoloGuitarUI.Instance != null &&
                            SoloGuitarUI.Instance.IsNoteInHitWindow(currentIndex);

            if (inWindow)
            {
                Debug.Log("Correta: " + expected);
                SoloGuitarUI.Instance?.RegisterHit(currentIndex);
                currentIndex++;
                keyTimer = timePerKey;

                if (currentIndex >= sequence.Length)
                    Success();
            }
            else
            {
                Debug.Log("CEDO DEMAIS: " + expected);
                Fail();
            }
            return;
        }

        if (AnyWrongKey())
        {
            Debug.Log("TECLA ERRADA");
            Fail();
        }
    }

    bool AnyWrongKey()
    {
        var systemKeys = new System.Collections.Generic.HashSet<Key>
        {
            Key.None,
            Key.LeftShift,  Key.RightShift,
            Key.LeftCtrl,   Key.RightCtrl,
            Key.LeftAlt,    Key.RightAlt,
            Key.LeftMeta,   Key.RightMeta,
            Key.Escape,
        };

        foreach (Key k in System.Enum.GetValues(typeof(Key)))
        {
            if (!Keyboard.current[k].wasPressedThisFrame) continue;
            if (systemKeys.Contains(k))                   continue;
            if (k != sequence[currentIndex])              return true;
        }
        return false;
    }

    // ── Resultado ─────────────────────────────────────────────────────────────

    void Success()
    {
        Debug.Log("SOLO SUCCESS");
        state = SpecialState.Success;
        SoloGuitarUI.Instance?.ShowSuccess();
        DoSpecialEffect();
        EndSolo();
    }

    void Fail()
    {
        Debug.Log("SOLO FAIL");
        state = SpecialState.Fail;
        SoloGuitarUI.Instance?.ShowFail();
        EndSolo();
    }

    void EndSolo()
    {
        // FIX: restaura o tempo antes de qualquer outra coisa
        // sem isso o inimigo continuava congelado após o solo
        Time.timeScale  = 1f;

        player.isLocked = false;

        // FIX: trocado Time.time por Time.unscaledTime pelo mesmo motivo do CheckStart
        nextTime = Time.unscaledTime + cooldown;

        state = SpecialState.Idle;
    }

    public void OnNoteMissed(int seqIdx)
    {
        if (state != SpecialState.Playing) return;
        if (seqIdx != currentIndex)        return;
        Debug.Log("MISS pela UI: seq " + seqIdx);
        Fail();
    }

    // ── Efeito especial ───────────────────────────────────────────────────────

    void DoSpecialEffect()
    {
        // Time.timeScale já foi restaurado para 1f em EndSolo antes de Success chamar isso
        // então o inimigo já está "vivo" no physics quando OverlapCircle roda
        Collider2D[] enemies = Physics2D.OverlapCircleAll(
            transform.position, damageRadius, enemyLayer
        );

        foreach (Collider2D e in enemies)
        {
            // EnemyBehaviorTest.TakeDamage(int) — assinatura confirmada
            // SendMessage casa exatamente, o inimigo vai receber o dano
            e.SendMessage("TakeDamage", damageAmount,
                          SendMessageOptions.DontRequireReceiver);
        }
    }

    // ── Key → string ──────────────────────────────────────────────────────────

    private static readonly System.Collections.Generic.Dictionary<Key, string> KeyDisplay
        = new()
    {
        { Key.A, "A" }, { Key.B, "B" }, { Key.C, "C" }, { Key.D, "D" },
        { Key.E, "E" }, { Key.F, "F" }, { Key.G, "G" }, { Key.H, "H" },
        { Key.I, "I" }, { Key.J, "J" }, { Key.K, "K" }, { Key.L, "L" },
        { Key.M, "M" }, { Key.N, "N" }, { Key.O, "O" }, { Key.P, "P" },
        { Key.Q, "Q" }, { Key.R, "R" }, { Key.S, "S" }, { Key.T, "T" },
        { Key.U, "U" }, { Key.V, "V" }, { Key.W, "W" }, { Key.X, "X" },
        { Key.Y, "Y" }, { Key.Z, "Z" },
        { Key.Space,      "SPC" },
        { Key.LeftArrow,  "<"   }, { Key.RightArrow, ">" },
        { Key.UpArrow,    "^"   }, { Key.DownArrow,  "v" },
        { Key.Digit1, "1" }, { Key.Digit2, "2" }, { Key.Digit3, "3" },
        { Key.Digit4, "4" }, { Key.Digit5, "5" },
    };

    static string KeyToDisplay(Key key) =>
        KeyDisplay.TryGetValue(key, out string label) ? label : key.ToString();
}