using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// SpecialAttackUi — UI do ataque especial rítmico.
///
/// Hierarquia esperada no Canvas:
///   GuitarSoloUI (este script)
///   └── SoloPanel
///       ├── NotesContainer      (HorizontalLayoutGroup — gerado via código)
///       ├── CircleTimer         (Image — Filled, Radial 360, Fill Origin: Top)
///       └── ResultLabel         (TextMeshProUGUI — "SUCCESS!" / "FAIL!")
///
/// A API pública é chamada exclusivamente pelo GuitarSoloSystem.
/// </summary>
public class SpecialAttackUi : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SpecialAttackUi Instance { get; private set; }

    // ── Referências ───────────────────────────────────────────────────────────
    [Header("Referências / References")]
    [SerializeField] private GameObject soloPanel;
    [SerializeField] private Image      circleTimer;    // Filled, Radial 360
    [SerializeField] private Transform  notesContainer; // pai dos slots de nota
    [SerializeField] private GameObject notePrefab;     // prefab: Image + TextMeshProUGUI filho
    [SerializeField] private TextMeshProUGUI resultLabel;

    // ── Cores ─────────────────────────────────────────────────────────────────
    [Header("Cores / Colors")]
    [SerializeField] private Color colorIdle    = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color colorActive  = Color.white;
    [SerializeField] private Color colorSuccess = new Color(0.2f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color colorFail    = new Color(0.9f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color colorWarning = new Color(1f, 0.55f, 0f, 1f);

    [Header("Limiar de aviso / Warning threshold (0–1)")]
    [SerializeField] [Range(0f, 1f)] private float warningThreshold = 0.3f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private float      noteWindow;        // espelhado do GuitarSoloSystem
    private float      timer;
    private bool       timerRunning;
    private int        activeNoteIndex = -1;

    private List<Image>            noteImages = new List<Image>();
    private List<TextMeshProUGUI>  noteLabels = new List<TextMeshProUGUI>();

    private Coroutine resultCoroutine;

    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        soloPanel.SetActive(false);
    }

    void Update()
    {
        if (!timerRunning) return;

        timer -= Time.deltaTime;

        float fill = Mathf.Clamp01(timer / noteWindow);
        circleTimer.fillAmount = fill;

        // Cor do timer muda quando perto do limite
        Color target = fill <= warningThreshold ? colorWarning : colorActive;
        circleTimer.color = Color.Lerp(circleTimer.color, target, Time.deltaTime * 10f);

        // Pulso na nota ativa quando em aviso
        if (activeNoteIndex >= 0 && activeNoteIndex < noteImages.Count)
        {
            float pulse = fill <= warningThreshold
                ? 1f + Mathf.Sin(Time.time * 18f) * 0.08f
                : 1f;
            noteImages[activeNoteIndex].transform.localScale = Vector3.one * pulse;
        }
    }

    // ── API pública chamada pelo GuitarSoloSystem ─────────────────────────────

    /// <summary>
    /// Abre o painel e constrói os slots de nota para a sequência.
    /// Chamado em StartSolo().
    /// </summary>
    public void ShowSequence(List<GuitarSoloSystem.NoteKey> sequence, float noteWindowDuration)
    {
        if (resultCoroutine != null) StopCoroutine(resultCoroutine);

        noteWindow = noteWindowDuration;

        soloPanel.SetActive(true);
        resultLabel.gameObject.SetActive(false);

        BuildNoteSlots(sequence);

        circleTimer.fillAmount = 0f;
        circleTimer.color      = colorIdle;
        timerRunning           = false;
        activeNoteIndex        = -1;
    }

    /// <summary>
    /// Destaca a nota no índice dado e reinicia o timer visual.
    /// Chamado no início de cada iteração do SoloRoutine.
    /// </summary>
    public void ActivateNote(int index)
    {
        if (index < 0 || index >= noteImages.Count) return;

        activeNoteIndex = index;

        for (int i = 0; i < noteImages.Count; i++)
        {
            bool isActive = i == index;
            noteImages[i].color                    = isActive ? colorActive : colorIdle;
            noteImages[i].transform.localScale      = Vector3.one;
            noteLabels[i].color                    = isActive ? colorActive : colorIdle;
        }

        // Reinicia timer visual
        timer                  = noteWindow;
        circleTimer.fillAmount = 1f;
        circleTimer.color      = colorActive;
        timerRunning           = true;
    }

    /// <summary>
    /// Marca a nota como acertada (verde).
    /// Chamado pelo GuitarSoloSystem quando a tecla correta é pressionada.
    /// </summary>
    public void NoteSuccess(int index)
    {
        timerRunning = false;

        if (index < 0 || index >= noteImages.Count) return;

        noteImages[index].color           = colorSuccess;
        noteImages[index].transform.localScale = Vector3.one;
        noteLabels[index].color           = colorSuccess;
        circleTimer.color                 = colorSuccess;
    }

    /// <summary>
    /// Marca a nota como errada (vermelho) e para o timer.
    /// Chamado pelo GuitarSoloSystem em caso de tecla errada ou timeout.
    /// </summary>
    public void NoteFail(int index)
    {
        timerRunning = false;

        if (index >= 0 && index < noteImages.Count)
        {
            noteImages[index].color           = colorFail;
            noteImages[index].transform.localScale = Vector3.one;
            noteLabels[index].color           = colorFail;
        }

        circleTimer.color = colorFail;
    }

    /// <summary>
    /// Exibe o resultado final e fecha o painel após um delay.
    /// Chamado por EndSolo().
    /// </summary>
    public void ShowResult(bool success)
    {
        timerRunning = false;
        string text  = success ? "SUCCESS!" : "FAIL!";
        Color  color = success ? colorSuccess : colorFail;

        resultLabel.gameObject.SetActive(true);
        resultLabel.text  = text;
        resultLabel.color = color;

        float delay = success ? 1.4f : 1.0f;
        resultCoroutine = StartCoroutine(HideAfterDelay(delay));
    }

    // ── Internos ──────────────────────────────────────────────────────────────

    private void BuildNoteSlots(List<GuitarSoloSystem.NoteKey> sequence)
    {
        // Limpa slots anteriores
        foreach (Transform child in notesContainer)
            Destroy(child.gameObject);

        noteImages.Clear();
        noteLabels.Clear();

        foreach (var note in sequence)
        {
            GameObject slot = Instantiate(notePrefab, notesContainer);

            Image img = slot.GetComponent<Image>();
            if (img == null) img = slot.GetComponentInChildren<Image>();

            TextMeshProUGUI label = slot.GetComponentInChildren<TextMeshProUGUI>();

            if (img   != null) { img.color = colorIdle;   noteImages.Add(img);   }
            if (label != null) { label.text = note.displayName; label.color = colorIdle; noteLabels.Add(label); }
        }
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        soloPanel.SetActive(false);
    }
}