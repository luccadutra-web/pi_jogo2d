using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SpecialUI — Interface visual do SpecialSystem.
///
/// HIERARQUIA ESPERADA NO CANVAS:
///   SpecialUI (este script)
///   ├── EnergyBarRoot
///   │   └── EnergyBarFill   (Image — Filled Horizontal)
///   └── SoloPanel
///       ├── NotesContainer  (HorizontalLayoutGroup)
///       ├── TimerBar        (Image — Filled Horizontal)
///       └── ResultLabel     (TextMeshProUGUI)
///
/// Este script apenas ASSINA os eventos do SpecialSystem.
/// Não chama nenhum método de gameplay — só reage.
/// </summary>
public class SpecialUI : MonoBehaviour
{
    // ── Referências ───────────────────────────────────────────────────────────

    [Header("Barra de Energia")]
    [SerializeField] private Image           energyBarFill;
    [SerializeField] private GameObject      readyIndicator; // brilha quando cheio

    [Header("Painel do Solo")]
    [SerializeField] private GameObject      soloPanel;
    [SerializeField] private Transform       notesContainer;
    [SerializeField] private GameObject      notePrefab;       // Image + TextMeshProUGUI filho
    [SerializeField] private Image           timerBar;         // Filled Horizontal
    [SerializeField] private TextMeshProUGUI resultLabel;

    // ── Cores ─────────────────────────────────────────────────────────────────

    [Header("Cores")]
    [SerializeField] private Color colorIdle    = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color colorActive  = Color.white;
    [SerializeField] private Color colorSuccess = new Color(0.2f, 0.95f, 0.45f);
    [SerializeField] private Color colorFail    = new Color(0.95f, 0.2f, 0.2f);
    [SerializeField] private Color colorWarning = new Color(1f, 0.55f, 0f);
    [SerializeField] private Color energyReady  = new Color(1f, 0.85f, 0.1f);
    [SerializeField] private Color energyNormal = new Color(0.4f, 0.8f, 1f);

    [Header("Timer — limiar de aviso (0–1)")]
    [SerializeField] [Range(0f, 1f)] private float warningThreshold = 0.25f;

    // ── Estado interno ────────────────────────────────────────────────────────

    private float  _noteWindow;
    private float  _timerElapsed;
    private bool   _timerRunning;
    private int    _activeIndex = -1;

    private List<Image>           _noteImages = new List<Image>();
    private List<TextMeshProUGUI> _noteLabels = new List<TextMeshProUGUI>();

    private Coroutine _resultRoutine;
    private Coroutine _energyPulse;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (soloPanel)      soloPanel.SetActive(false);
        if (resultLabel)    resultLabel.gameObject.SetActive(false);
        if (readyIndicator) readyIndicator.SetActive(false);
    }

    void OnEnable()
    {
        if (SpecialSystem.Instance == null) return;

        SpecialSystem.Instance.OnEnergyChanged  += HandleEnergyChanged;
        SpecialSystem.Instance.OnSoloStarted    += HandleSoloStarted;
        SpecialSystem.Instance.OnNoteActivated  += HandleNoteActivated;
        SpecialSystem.Instance.OnNoteSuccess    += HandleNoteSuccess;
        SpecialSystem.Instance.OnNoteFail       += HandleNoteFail;
        SpecialSystem.Instance.OnSoloSuccess    += HandleSoloSuccess;
        SpecialSystem.Instance.OnSoloFail       += HandleSoloFail;
    }

    void OnDisable()
    {
        if (SpecialSystem.Instance == null) return;

        SpecialSystem.Instance.OnEnergyChanged  -= HandleEnergyChanged;
        SpecialSystem.Instance.OnSoloStarted    -= HandleSoloStarted;
        SpecialSystem.Instance.OnNoteActivated  -= HandleNoteActivated;
        SpecialSystem.Instance.OnNoteSuccess    -= HandleNoteSuccess;
        SpecialSystem.Instance.OnNoteFail       -= HandleNoteFail;
        SpecialSystem.Instance.OnSoloSuccess    -= HandleSoloSuccess;
        SpecialSystem.Instance.OnSoloFail       -= HandleSoloFail;
    }

    void Update()
    {
        if (!_timerRunning) return;

        _timerElapsed += Time.deltaTime;
        float fill = Mathf.Clamp01(1f - _timerElapsed / _noteWindow);

        if (timerBar)
        {
            timerBar.fillAmount = fill;
            Color target = fill <= warningThreshold ? colorWarning : colorActive;
            timerBar.color = Color.Lerp(timerBar.color, target, Time.deltaTime * 12f);
        }

        // Pulso na nota ativa no aviso
        if (_activeIndex >= 0 && _activeIndex < _noteImages.Count && fill <= warningThreshold)
        {
            float pulse = 1f + Mathf.Sin(Time.time * 20f) * 0.09f;
            _noteImages[_activeIndex].transform.localScale = Vector3.one * pulse;
        }
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void HandleEnergyChanged(float current, float max)
    {
        if (!energyBarFill) return;

        float norm = max > 0f ? current / max : 0f;
        energyBarFill.fillAmount = norm;

        bool ready = norm >= 1f;
        energyBarFill.color = ready ? energyReady : energyNormal;

        if (readyIndicator && readyIndicator.activeSelf != ready)
        {
            readyIndicator.SetActive(ready);
            if (ready && _energyPulse == null)
                _energyPulse = StartCoroutine(PulseReadyIndicator());
        }
    }

    private void HandleSoloStarted(List<SpecialSystem.NoteKey> sequence, float noteWindow)
    {
        _noteWindow = noteWindow;

        if (_resultRoutine != null) StopCoroutine(_resultRoutine);
        if (resultLabel)    resultLabel.gameObject.SetActive(false);
        if (readyIndicator) readyIndicator.SetActive(false);

        BuildNoteSlots(sequence);

        if (timerBar) { timerBar.fillAmount = 0f; timerBar.color = colorIdle; }

        _timerRunning = false;
        _activeIndex  = -1;

        if (soloPanel) soloPanel.SetActive(true);
    }

    private void HandleNoteActivated(int index)
    {
        if (index < 0 || index >= _noteImages.Count) return;

        _activeIndex  = index;
        _timerElapsed = 0f;
        _timerRunning = true;

        for (int i = 0; i < _noteImages.Count; i++)
        {
            bool active = i == index;
            _noteImages[i].color                = active ? colorActive : colorIdle;
            _noteImages[i].transform.localScale = Vector3.one;
            _noteLabels[i].color                = active ? colorActive : colorIdle;
        }

        if (timerBar) { timerBar.fillAmount = 1f; timerBar.color = colorActive; }
    }

    private void HandleNoteSuccess(int index)
    {
        _timerRunning = false;
        if (index < 0 || index >= _noteImages.Count) return;

        _noteImages[index].color                = colorSuccess;
        _noteImages[index].transform.localScale = Vector3.one;
        _noteLabels[index].color                = colorSuccess;
        if (timerBar) timerBar.color = colorSuccess;
    }

    private void HandleNoteFail(int index)
    {
        _timerRunning = false;
        if (index >= 0 && index < _noteImages.Count)
        {
            _noteImages[index].color                = colorFail;
            _noteImages[index].transform.localScale = Vector3.one;
            _noteLabels[index].color                = colorFail;
        }
        if (timerBar) timerBar.color = colorFail;
    }

    private void HandleSoloSuccess()
    {
        ShowResult("SUCCESS!", colorSuccess, 1.4f);
    }

    private void HandleSoloFail()
    {
        ShowResult("FAIL!", colorFail, 1.0f);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BuildNoteSlots(List<SpecialSystem.NoteKey> sequence)
    {
        if (!notesContainer || !notePrefab) return;

        foreach (Transform child in notesContainer)
            Destroy(child.gameObject);

        _noteImages.Clear();
        _noteLabels.Clear();

        foreach (var note in sequence)
        {
            GameObject slot = Instantiate(notePrefab, notesContainer);

            Image img = slot.GetComponent<Image>() ?? slot.GetComponentInChildren<Image>();
            TextMeshProUGUI label = slot.GetComponentInChildren<TextMeshProUGUI>();

            if (img != null)
            {
                img.color = colorIdle;
                _noteImages.Add(img);
            }
            if (label != null)
            {
                label.text  = note.displayName;
                label.color = colorIdle;
                _noteLabels.Add(label);
            }
        }
    }

    private void ShowResult(string text, Color color, float hideDelay)
    {
        _timerRunning = false;
        if (!resultLabel) return;

        resultLabel.gameObject.SetActive(true);
        resultLabel.text  = text;
        resultLabel.color = color;

        if (_resultRoutine != null) StopCoroutine(_resultRoutine);
        _resultRoutine = StartCoroutine(HidePanel(hideDelay));
    }

    private IEnumerator HidePanel(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (soloPanel) soloPanel.SetActive(false);
        if (resultLabel) resultLabel.gameObject.SetActive(false);
        _resultRoutine = null;
    }

    private IEnumerator PulseReadyIndicator()
    {
        if (!readyIndicator) yield break;

        while (readyIndicator.activeSelf)
        {
            float t = Mathf.PingPong(Time.time * 2f, 1f);
            readyIndicator.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.1f, t);
            yield return null;
        }

        readyIndicator.transform.localScale = Vector3.one;
        _energyPulse = null;
    }
}