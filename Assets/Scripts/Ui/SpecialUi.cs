using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI do Especial do Player — v3
/// Cada anel usa a duração individual recebida pelo evento OnWindowOpened(index, duration).
/// </summary>
public class SpecialUI : MonoBehaviour
{
    // ─── Referências ──────────────────────────────────────────────────────────

    [Header("Gauge")]
    [SerializeField] private Image           gaugeBarFill;
    [SerializeField] private TextMeshProUGUI gaugeText;

    [Header("Painel de Notas")]
    [SerializeField] private GameObject      notesPanel;

    [Header("Anéis — aponte para RingFILL (type=Filled, Radial360, FillOrigin=Top)")]
    [SerializeField] private Image[]           noteRings     = new Image[4];

    [Header("Labels de tecla (preenchidos automaticamente no Start)")]
    [SerializeField] private TextMeshProUGUI[] noteKeyLabels = new TextMeshProUGUI[4];

    // ─── Cores ────────────────────────────────────────────────────────────────

    [Header("Cores")]
    [SerializeField] private Color colorIdle   = new Color(0.69f, 0.66f, 0.93f, 0.35f);
    [SerializeField] private Color colorActive = new Color(0.50f, 0.47f, 0.87f, 1.00f);
    [SerializeField] private Color colorHit    = new Color(0.11f, 0.62f, 0.46f, 1.00f);
    [SerializeField] private Color colorMiss   = new Color(0.89f, 0.29f, 0.29f, 1.00f);

    [SerializeField] private Color colorKeyIdle = Color.white;
    [SerializeField] private Color colorKeyHit  = new Color(0.11f, 0.62f, 0.46f, 1.00f);
    [SerializeField] private Color colorKeyMiss = new Color(0.89f, 0.29f, 0.29f, 1.00f);

    // ─── Punch scale ──────────────────────────────────────────────────────────

    [Header("Punch scale ao acertar")]
    [SerializeField] private float punchScale    = 1.18f;
    [SerializeField] private float punchDuration = 0.12f;

    // ─── Internos ─────────────────────────────────────────────────────────────

    private PlayerSpecial      _special;
    private PlayerSpecialGauge _gauge;
    private const int          NOTE_COUNT = 4;
    private Coroutine[]        _ringTimers = new Coroutine[NOTE_COUNT];

    private static readonly System.Collections.Generic.Dictionary<string, string> KeyDisplayMap
        = new System.Collections.Generic.Dictionary<string, string>
    {
        { "semicolon",    ";"   }, { "comma",        ","   }, { "period",  "."   },
        { "slash",        "/"   }, { "backslash",    "\\"  }, { "minus",   "-"   },
        { "equals",       "="   }, { "leftbracket",  "["   }, { "rightbracket", "]" },
        { "backquote",    "`"   }, { "quote",        "'"   }, { "space",   "SPC" },
        { "enter",        "↵"   }, { "tab",          "TAB" }, { "backspace","⌫"  },
        { "escape",       "ESC" }, { "leftshift",    "⇧"   }, { "rightshift","⇧" },
        { "leftctrl",     "CTRL"}, { "rightctrl",    "CTRL"}, { "leftalt", "ALT" },
        { "rightalt",     "ALT" },
    };

    // ─── Unity ────────────────────────────────────────────────────────────────

    void Start()
    {
        _special = FindObjectOfType<PlayerSpecial>();
        _gauge   = FindObjectOfType<PlayerSpecialGauge>();

        if (_special == null) Debug.LogWarning("[SpecialUI] PlayerSpecial não encontrado!");
        if (_gauge   == null) Debug.LogWarning("[SpecialUI] PlayerSpecialGauge não encontrado!");

        SubscribeEvents();
        PopulateKeyLabels();

        if (notesPanel != null) notesPanel.SetActive(false);
        ResetAllRings();

        if (_gauge != null)
            UpdateGaugeBar(_gauge.GaugeNormalized, _gauge.CurrentGauge, _gauge.MaxGauge);
    }

    void OnDestroy() => UnsubscribeEvents();

    // ─── Eventos ──────────────────────────────────────────────────────────────

    private void SubscribeEvents()
    {
        if (_gauge != null)
        {
            _gauge.OnGaugeChanged += HandleGaugeChanged;
            _gauge.OnGaugeFull    += HandleGaugeFull;
            _gauge.OnGaugeSpent   += HandleGaugeSpent;
        }
        if (_special != null)
        {
            _special.OnWindowOpened     += HandleWindowOpened;   // (int, float)
            _special.OnNoteResult       += HandleNoteResult;
            _special.OnSpecialSuccess   += HandleSpecialSuccess;
            _special.OnSpecialFail      += HandleSpecialFail;
            _special.OnSpecialCancelled += HandleSpecialCancelled;
        }
    }

    private void UnsubscribeEvents()
    {
        if (_gauge != null)
        {
            _gauge.OnGaugeChanged -= HandleGaugeChanged;
            _gauge.OnGaugeFull    -= HandleGaugeFull;
            _gauge.OnGaugeSpent   -= HandleGaugeSpent;
        }
        if (_special != null)
        {
            _special.OnWindowOpened     -= HandleWindowOpened;
            _special.OnNoteResult       -= HandleNoteResult;
            _special.OnSpecialSuccess   -= HandleSpecialSuccess;
            _special.OnSpecialFail      -= HandleSpecialFail;
            _special.OnSpecialCancelled -= HandleSpecialCancelled;
        }
    }

    // ─── Key labels ───────────────────────────────────────────────────────────

    private void PopulateKeyLabels()
    {
        string[] defaults = { "J", "K", "L", ";" };

        if (_special == null) { ApplyLabels(defaults); return; }

        var field = typeof(PlayerSpecial).GetField(
            "noteKeys",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (field == null)
        {
            Debug.LogWarning("[SpecialUI] Campo 'noteKeys' não encontrado via reflexão — usando defaults.");
            ApplyLabels(defaults);
            return;
        }

        string[] noteKeys = field.GetValue(_special) as string[];
        if (noteKeys == null) { ApplyLabels(defaults); return; }

        string[] labels = new string[NOTE_COUNT];
        for (int i = 0; i < NOTE_COUNT; i++)
        {
            string raw = i < noteKeys.Length ? noteKeys[i] : $"F{i + 1}";
            labels[i] = ParseKeyBinding(raw);
        }
        ApplyLabels(labels);
    }

    private void ApplyLabels(string[] labels)
    {
        for (int i = 0; i < NOTE_COUNT && i < noteKeyLabels.Length; i++)
            if (noteKeyLabels[i] != null)
                noteKeyLabels[i].text = labels[i];
    }

    private string ParseKeyBinding(string binding)
    {
        int slash = binding.LastIndexOf('/');
        string key = (slash >= 0 ? binding.Substring(slash + 1) : binding).ToLower().Trim();
        return KeyDisplayMap.TryGetValue(key, out string display) ? display : key.ToUpper();
    }

    // ─── Gauge ────────────────────────────────────────────────────────────────

    private void HandleGaugeChanged(float current, float max)
        => UpdateGaugeBar(current / max, current, max);

    private void HandleGaugeFull()
    {
        if (gaugeBarFill != null)
            StartCoroutine(PunchScaleRoutine(gaugeBarFill.transform, 1.06f, 0.15f));
    }

    private void HandleGaugeSpent()
        => UpdateGaugeBar(0f, 0f, _gauge != null ? _gauge.MaxGauge : 100f);

    private void UpdateGaugeBar(float normalized, float current, float max)
    {
        if (gaugeBarFill != null)
            gaugeBarFill.fillAmount = Mathf.Clamp01(normalized);
        if (gaugeText != null)
            gaugeText.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }

    // ─── Notas ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recebe o índice da nota e a duração individual da janela vinda do PlayerSpecial.
    /// </summary>
    private void HandleWindowOpened(int noteIndex, float windowDuration)
    {
        if (noteIndex == 0)
        {
            if (notesPanel != null) notesPanel.SetActive(true);
            ResetAllRings();
        }

        if (noteIndex < 0 || noteIndex >= NOTE_COUNT) return;

        SetRingState(noteIndex, colorActive, colorKeyIdle);
        SetRingFill(noteIndex, 1f);

        if (_ringTimers[noteIndex] != null) StopCoroutine(_ringTimers[noteIndex]);
        // Usa a duração recebida diretamente do PlayerSpecial — sem necessidade de duplicar no Inspector
        _ringTimers[noteIndex] = StartCoroutine(DrainRingRoutine(noteIndex, windowDuration));
    }

    private void HandleNoteResult(int noteIndex, bool hit)
    {
        if (noteIndex < 0 || noteIndex >= NOTE_COUNT) return;

        if (_ringTimers[noteIndex] != null)
        {
            StopCoroutine(_ringTimers[noteIndex]);
            _ringTimers[noteIndex] = null;
        }

        if (hit)
        {
            SetRingFill(noteIndex, 1f);
            SetRingState(noteIndex, colorHit, colorKeyHit);

            Transform parent = noteRings[noteIndex] != null
                ? noteRings[noteIndex].transform.parent : null;
            if (parent != null)
                StartCoroutine(PunchScaleRoutine(parent, punchScale, punchDuration));
        }
        else
        {
            SetRingFill(noteIndex, 0f);
            SetRingState(noteIndex, colorMiss, colorKeyMiss);
        }
    }

    private void HandleSpecialSuccess()
        => StartCoroutine(HideNotesAfterDelay(0.8f));

    private void HandleSpecialFail()
    {
        for (int i = 0; i < NOTE_COUNT; i++)
            if (noteRings[i] != null && noteRings[i].color == colorIdle)
                SetRingState(i, colorMiss, colorKeyMiss);
        StartCoroutine(HideNotesAfterDelay(0.5f));
    }

    private void HandleSpecialCancelled()
    {
        StopAllRingTimers();
        if (notesPanel != null) notesPanel.SetActive(false);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void ResetAllRings()
    {
        for (int i = 0; i < NOTE_COUNT; i++)
        {
            SetRingFill(i, 0f);
            SetRingState(i, colorIdle, colorKeyIdle);
        }
    }

    private void StopAllRingTimers()
    {
        for (int i = 0; i < NOTE_COUNT; i++)
        {
            if (_ringTimers[i] != null) { StopCoroutine(_ringTimers[i]); _ringTimers[i] = null; }
        }
    }

    private void SetRingFill(int i, float value)
    {
        if (i < 0 || i >= NOTE_COUNT || noteRings[i] == null) return;
        noteRings[i].fillAmount = Mathf.Clamp01(value);
    }

    private void SetRingState(int i, Color ringColor, Color keyColor)
    {
        if (i < 0 || i >= NOTE_COUNT) return;
        if (noteRings[i]     != null) noteRings[i].color     = ringColor;
        if (noteKeyLabels[i] != null) noteKeyLabels[i].color = keyColor;
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator DrainRingRoutine(int noteIndex, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetRingFill(noteIndex, 1f - Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        SetRingFill(noteIndex, 0f);
        _ringTimers[noteIndex] = null;
    }

    private IEnumerator HideNotesAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        StopAllRingTimers();
        if (notesPanel != null) notesPanel.SetActive(false);
    }

    private IEnumerator PunchScaleRoutine(Transform target, float scale, float duration)
    {
        if (target == null) yield break;
        Vector3 original = target.localScale;
        float half = duration * 0.5f;
        float t = 0f;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(original, original * scale, t / half);
            yield return null;
        }
        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            target.localScale = Vector3.LerpUnclamped(original * scale, original, t / half);
            yield return null;
        }
        target.localScale = original;
    }
}