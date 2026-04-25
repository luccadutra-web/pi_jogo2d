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
    [Tooltip("Tempo em segundos para apertar cada tecla.")]
    [SerializeField] private float timePerKey = 2f;

    [Header("Sequencia")]
    [SerializeField] private Key[] sequence = { Key.A, Key.D, Key.A };

    private int   currentIndex;
    private float nextTime;
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

    // ── Inicio ───────────────────────────────────────────────────────────────

    void CheckStart()
    {
        if (!specialAction.WasPressedThisFrame()) return;
        if (Time.time < nextTime)                 return;
        if (!points.UsePoints(cost))              return;
        StartSolo();
    }

    void StartSolo()
    {
        Debug.Log("SOLO START");
        state        = SpecialState.Playing;
        currentIndex = 0;
        keyTimer     = timePerKey;
        player.isLocked = true;

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

    // ── Sequencia ────────────────────────────────────────────────────────────

    void CheckSequence()
    {
        // Timer esgotou — falha por tempo
        keyTimer -= Time.deltaTime;
        if (keyTimer <= 0f)
        {
            Debug.Log("TEMPO ESGOTADO: " + sequence[currentIndex]);
            Fail();
            return;
        }

        Key expected = sequence[currentIndex];

        // Tecla correta pressionada
        if (Keyboard.current[expected].wasPressedThisFrame)
        {
            Debug.Log("Correta: " + expected);
            SoloGuitarUI.Instance?.RegisterHit(currentIndex);
            currentIndex++;
            keyTimer = timePerKey;

            if (currentIndex >= sequence.Length)
                Success();

            return;
        }

        // Qualquer outra tecla — falha imediata
        if (AnyWrongKey())
        {
            Debug.Log("TECLA ERRADA");
            Fail();
        }
    }

    bool AnyWrongKey()
    {
        foreach (Key k in System.Enum.GetValues(typeof(Key)))
        {
            if (!Keyboard.current[k].wasPressedThisFrame) continue;

            // Ignora teclas de sistema que nao sao input do jogador
            if (k == Key.None)       continue;
            if (k == Key.LeftShift)  continue;
            if (k == Key.RightShift) continue;
            if (k == Key.LeftCtrl)   continue;
            if (k == Key.RightCtrl)  continue;
            if (k == Key.LeftAlt)    continue;
            if (k == Key.RightAlt)   continue;
            if (k == Key.LeftMeta)   continue;
            if (k == Key.RightMeta)  continue;
            if (k == Key.Escape)     continue;

            // Se chegou aqui e nao e a tecla esperada, e errada
            if (k != sequence[currentIndex])
                return true;
        }
        return false;
    }

    // ── Resultado ────────────────────────────────────────────────────────────

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
        player.isLocked = false;
        nextTime        = Time.time + cooldown;
        state           = SpecialState.Idle;
    }

    // ── Chamado pela UI quando bloco passa sem ser acertado ──────────────────

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
        // TODO: adicionar LayerMask para filtrar apenas inimigos
        Collider2D[] enemies = Physics2D.OverlapCircleAll(transform.position, 3f);
        foreach (Collider2D e in enemies)
            e.SendMessage("TakeDamage", 999, SendMessageOptions.DontRequireReceiver);
    }

    // ── Key -> string ─────────────────────────────────────────────────────────

    static string KeyToDisplay(Key key) => key switch
    {
        Key.A => "A", Key.B => "B", Key.C => "C", Key.D => "D",
        Key.E => "E", Key.F => "F", Key.G => "G", Key.H => "H",
        Key.I => "I", Key.J => "J", Key.K => "K", Key.L => "L",
        Key.M => "M", Key.N => "N", Key.O => "O", Key.P => "P",
        Key.Q => "Q", Key.R => "R", Key.S => "S", Key.T => "T",
        Key.U => "U", Key.V => "V", Key.W => "W", Key.X => "X",
        Key.Y => "Y", Key.Z => "Z",
        Key.Space      => "SPC",
        Key.LeftArrow  => "<",  Key.RightArrow => ">",
        Key.UpArrow    => "^",  Key.DownArrow  => "v",
        Key.Digit1     => "1",  Key.Digit2     => "2",
        Key.Digit3     => "3",  Key.Digit4     => "4",
        Key.Digit5     => "5",
        _ => key.ToString()
    };
}