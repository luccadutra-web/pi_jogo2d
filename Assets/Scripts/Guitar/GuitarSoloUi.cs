using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// GuitarSoloUI — Display do minigame de ataque especial.
///
/// Setup na cena:
///   1. Crie um Canvas (Screen Space — Overlay)
///   2. Adicione um GameObject vazio "GuitarSoloUI" com este componente
///   3. Crie o NoteContainer: um HorizontalLayoutGroup filho do Canvas
///   4. Crie o NotePrefab: um GameObject com Image (fundo) + TextMeshProUGUI (tecla)
///      → O prefab deve ter também um filho "TimerBar" com Image (fill horizontal)
///   5. Crie o ResultText: TextMeshProUGUI para "SUCESSO!" / "FALHOU!"
///
/// Hierarquia sugerida:
///   Canvas
///   └── GuitarSoloUI (este componente)
///       ├── NoteContainer (HorizontalLayoutGroup)
///       └── ResultText (TextMeshProUGUI)
///
/// O componente cria e destrói os slots de nota dinamicamente.
/// </summary>
public class GuitarSoloUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private RectTransform noteContainer;
    [SerializeField] private GameObject    notePrefab;
    [SerializeField] private TextMeshProUGUI resultText;

    [Header("Cores")]
    [SerializeField] private Color colorIdle    = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    [SerializeField] private Color colorActive  = new Color(1.0f, 0.8f, 0.0f, 1.0f);
    [SerializeField] private Color colorSuccess = new Color(0.2f, 1.0f, 0.4f, 1.0f);
    [SerializeField] private Color colorFail    = new Color(1.0f, 0.2f, 0.2f, 1.0f);

    [Header("Animação")]
    [SerializeField] private float resultDisplayDuration = 1.5f;
    [SerializeField] private float noteSuccessScale      = 1.25f;
    [SerializeField] private float noteScaleDuration     = 0.12f;

    // ── Estado ────────────────────────────────────────────────────────────────

    private List<NoteSlot> slots    = new List<NoteSlot>();
    private Coroutine      timerCor;
    private Coroutine      resultCor;
    private float          currentNoteWindow;

    // ── Estrutura interna de cada slot ────────────────────────────────────────

    private class NoteSlot
    {
        public GameObject      root;
        public Image           background;
        public TextMeshProUGUI label;
        public Image           timerBar;   // fill image — pode ser null
    }

    // ── API pública (chamada por GuitarSoloSystem) ────────────────────────────

    /// <summary>Exibe a sequência completa, todos em estado idle.</summary>
    public void ShowSequence(List<GuitarSoloSystem.NoteKey> sequence, float noteWindow)
    {
        ClearSlots();
        currentNoteWindow = noteWindow;

        if (resultCor != null) StopCoroutine(resultCor);
        if (resultText != null) resultText.gameObject.SetActive(false);

        foreach (var note in sequence)
        {
            var slot = CreateSlot(note.displayName);
            slots.Add(slot);
        }

        noteContainer.gameObject.SetActive(true);
    }

    /// <summary>Ativa visualmente a nota atual e inicia o timer dela.</summary>
    public void ActivateNote(int index)
    {
        if (index < 0 || index >= slots.Count) return;

        // Reseta timer anterior
        if (timerCor != null) StopCoroutine(timerCor);

        var slot = slots[index];
        SetSlotColor(slot, colorActive);

        timerCor = StartCoroutine(AnimateTimer(slot, currentNoteWindow));
    }

    /// <summary>Feedback visual de nota acertada.</summary>
    public void NoteSuccess(int index)
    {
        if (index < 0 || index >= slots.Count) return;

        if (timerCor != null) StopCoroutine(timerCor);

        var slot = slots[index];
        SetSlotColor(slot, colorSuccess);

        if (slot.timerBar != null) slot.timerBar.fillAmount = 1f;

        StartCoroutine(PunchScale(slot.root, noteSuccessScale, noteScaleDuration));
    }

    /// <summary>Feedback visual de nota errada/timeout.</summary>
    public void NoteFail(int index)
    {
        if (index < 0 || index >= slots.Count) return;

        if (timerCor != null) StopCoroutine(timerCor);

        var slot = slots[index];
        SetSlotColor(slot, colorFail);

        if (slot.timerBar != null) slot.timerBar.fillAmount = 0f;

        StartCoroutine(ShakeSlot(slot.root));
    }

    /// <summary>Exibe resultado final e esconde os slots.</summary>
    public void ShowResult(bool success)
    {
        if (timerCor != null) StopCoroutine(timerCor);

        if (resultCor != null) StopCoroutine(resultCor);
        resultCor = StartCoroutine(DisplayResult(success));
    }

    // ── Criação de slots ──────────────────────────────────────────────────────

    private NoteSlot CreateSlot(string labelText)
    {
        var root = Instantiate(notePrefab, noteContainer);
        root.SetActive(true);

        var slot = new NoteSlot { root = root };

        // Busca componentes pelo nome ou tipo
        slot.background = root.GetComponent<Image>();
        slot.label      = root.GetComponentInChildren<TextMeshProUGUI>();

        // TimerBar é filho chamado "TimerBar" com Image em fill mode
        var timerObj = root.transform.Find("TimerBar");
        if (timerObj != null)
            slot.timerBar = timerObj.GetComponent<Image>();

        if (slot.label != null)
            slot.label.text = labelText;

        SetSlotColor(slot, colorIdle);

        return slot;
    }

    private void ClearSlots()
    {
        foreach (var slot in slots)
            if (slot.root != null)
                Destroy(slot.root);

        slots.Clear();
    }

    // ── Helpers visuais ───────────────────────────────────────────────────────

    private void SetSlotColor(NoteSlot slot, Color color)
    {
        if (slot.background != null) slot.background.color = color;
    }

    private IEnumerator AnimateTimer(NoteSlot slot, float duration)
    {
        if (slot.timerBar == null) yield break;

        float elapsed = 0f;
        slot.timerBar.fillAmount = 1f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            slot.timerBar.fillAmount = 1f - (elapsed / duration);
            yield return null;
        }

        slot.timerBar.fillAmount = 0f;
    }

    private IEnumerator PunchScale(GameObject target, float scale, float duration)
    {
        Vector3 original = target.transform.localScale;
        Vector3 punched  = original * scale;

        float half = duration * 0.5f;
        float t    = 0f;

        while (t < half)
        {
            t += Time.deltaTime;
            target.transform.localScale = Vector3.Lerp(original, punched, t / half);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.deltaTime;
            target.transform.localScale = Vector3.Lerp(punched, original, t / half);
            yield return null;
        }

        target.transform.localScale = original;
    }

    private IEnumerator ShakeSlot(GameObject target)
    {
        Vector3 origin    = target.transform.localPosition;
        float   intensity = 12f;
        float   elapsed   = 0f;
        float   duration  = 0.35f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float x = Random.Range(-intensity, intensity) * (1f - elapsed / duration);
            target.transform.localPosition = origin + new Vector3(x, 0f, 0f);
            yield return null;
        }

        target.transform.localPosition = origin;
    }

    private IEnumerator DisplayResult(bool success)
    {
        // Aguarda meio segundo mostrando o estado final dos slots
        yield return new WaitForSeconds(0.5f);

        noteContainer.gameObject.SetActive(false);
        ClearSlots();

        if (resultText != null)
        {
            resultText.text  = success ? "SOLO PERFEITO!" : "FALHOU!";
            resultText.color = success ? colorSuccess : colorFail;
            resultText.gameObject.SetActive(true);

            // Fade out
            float elapsed = 0f;
            Color base_c  = resultText.color;

            while (elapsed < resultDisplayDuration)
            {
                elapsed += Time.deltaTime;
                float alpha     = Mathf.Lerp(1f, 0f, elapsed / resultDisplayDuration);
                resultText.color = new Color(base_c.r, base_c.g, base_c.b, alpha);
                yield return null;
            }

            resultText.gameObject.SetActive(false);
        }
    }

    void OnDestroy()
    {
        ClearSlots();
    }
}