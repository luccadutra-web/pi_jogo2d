using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI de solo estilo Guitar Hero — trilhas horizontais, blocos vindo da direita.
///
/// HIERARQUIA ESPERADA
/// ───────────────────────────────────────────────────────────
/// Canvas
/// └── SoloPanel                  (este script vive aqui ou em qualquer filho)
///     ├── Track_A                (RectTransform qualquer — o script lê a posição)
///     │   ├── HitRing            (Image — círculo na hit zone)
///     │   └── HitZoneLine        (Image — linha vertical fina, opcional)
///     ├── Track_D                (idem)
///     ├── Track_A2               (idem — segunda trilha para tecla A repetida)
///     └── ResultLabel            (TextMeshProUGUI)
///
/// COMO CONFIGURAR (Inspector)
/// ───────────────────────────────────────────────────────────
/// 1. soloPanel    → o GameObject raiz do painel
/// 2. tracks[]     → arraste Track_A[0], Track_D[1], Track_A2[2]
///                   A ORDEM define qual trilha cada tecla usa.
///                   BuildTrackMap() em SpecialAttack.cs gera os índices
///                   automaticamente pela ordem de aparição na sequência.
///                   Sequência {A,D,A}: A→trilha 0, D→trilha 1, segundo A→trilha 0
///                   (os dois A's compartilham trilha 0, pode criar Track_A2 só
///                    se quiser separar visualmente — nesse caso ajuste BuildTrackMap)
/// 3. notePrefab   → prefab com Image (raiz) + filho TextMeshProUGUI
/// 4. resultLabel  → TextMeshProUGUI do ResultLabel
/// 5. hitRings[]   → HitRing de cada trilha (mesma ordem de tracks[])
/// 6. noteSpeed    → px/s (300 é bom ponto de partida)
/// 7. hitWindowPx  → tolerância de acerto (40 recomendado)
///
/// COMO AS NOTAS SE MOVEM
/// ───────────────────────────────────────────────────────────
/// Cada nota nasce como filho do SoloPanel (não da Track).
/// Sua posição Y é copiada da Track correspondente.
/// Ela nasce em X = spawnX (direita) e se move para X = hitZoneX (esquerda).
/// Isso garante movimento correto independente das âncoras das Tracks.
/// </summary>
public class SoloGuitarUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Referências")]
    [SerializeField] private GameObject      soloPanel;
    [SerializeField] private RectTransform[] tracks;
    [SerializeField] private GameObject      notePrefab;
    [SerializeField] private TextMeshProUGUI resultLabel;

    [Header("Timing / Movimento")]
    [SerializeField] private float noteSpeed     = 500f;    // FIX: era 300 — aumentado para senso de urgência
    [SerializeField] private float hitWindowPx   = 40f;
    [SerializeField] private float spawnXOffset  = 300f;   // FIX: era 500 — reduzido para nota não nascer tão longe
    [SerializeField] private float hitZoneX      = -280f;  // X relativo ao centro do SoloPanel

    [Header("Intervalo entre notas (segundos)")]
    [SerializeField] private float noteInterval  = 0.9f;

    [Header("Cores")]
    [SerializeField] private Color colorIdle    = new Color(0.18f, 0.18f, 0.20f, 1f);
    [SerializeField] private Color colorHit     = Color.white;
    [SerializeField] private Color colorMiss    = new Color(0.55f, 0.10f, 0.10f, 1f);
    [SerializeField] private Color colorSuccess = new Color(0.27f, 0.86f, 0.50f, 1f);
    [SerializeField] private Color colorFail    = new Color(0.97f, 0.43f, 0.43f, 1f);

    [Header("Hit rings (opcional, mesma ordem de tracks[])")]
    [SerializeField] private Image[] hitRings;

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SoloGuitarUI Instance { get; private set; }

    // ── Dados internos ────────────────────────────────────────────────────────
    private class NoteBlock
    {
        public RectTransform rect;
        public Image         image;
        public int           trackIdx;
        public int           seqIdx;
        public bool          done;
    }

    private readonly List<NoteBlock> activeNotes = new();
    private bool      isRunning;
    private Coroutine resultCoroutine;
    private RectTransform panelRect;

    // ── Unity ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        Instance  = this;
        panelRect = soloPanel != null ? soloPanel.GetComponent<RectTransform>() : null;
        if (soloPanel) soloPanel.SetActive(false);
    }

    void Update()
    {
        if (!isRunning) return;

        // FIX: Time.deltaTime é 0 com timeScale = 0 (time stop do solo)
        // unscaledDeltaTime continua avançando independente do timeScale
        float dt = Time.unscaledDeltaTime;
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            NoteBlock n = activeNotes[i];
            if (n.done) continue;

            // Move para a esquerda
            Vector2 pos = n.rect.anchoredPosition;
            pos.x -= noteSpeed * dt;
            n.rect.anchoredPosition = pos;

            // Passou da hit zone sem acerto
            if (pos.x < hitZoneX - hitWindowPx)
                AutoMiss(n);
        }
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Abre o painel e spawna todos os blocos da sequência.
    /// keyLabels: ["A","D","A"]
    /// trackIndices: [0, 1, 0]  — índice em tracks[] para cada posição da sequência
    /// </summary>
    public void ShowSolo(string[] keyLabels, int[] trackIndices)
    {
        if (resultCoroutine != null) StopCoroutine(resultCoroutine);

        CleanNotes();
        isRunning = false;

        if (soloPanel) soloPanel.SetActive(true);
        if (resultLabel) resultLabel.gameObject.SetActive(false);

        StartCoroutine(SpawnSequence(keyLabels, trackIndices));
    }

    /// <summary>
    /// Retorna true se a nota no índice seqIdx está dentro da hit window.
    /// Chamado por SpecialAttack antes de registrar um acerto.
    /// </summary>
    public bool IsNoteInHitWindow(int seqIdx)
    {
        NoteBlock note = activeNotes.Find(n => n.seqIdx == seqIdx && !n.done);
        if (note == null) return false;

        float distToHitZone = Mathf.Abs(note.rect.anchoredPosition.x - hitZoneX);
        return distToHitZone <= hitWindowPx;
    }

    /// <summary>Registra acerto para a nota no índice seqIdx.</summary>
    public void RegisterHit(int seqIdx)

    {
        NoteBlock note = activeNotes.Find(n => n.seqIdx == seqIdx && !n.done);
        if (note == null) return;
        note.done = true;
        StartCoroutine(HitFeedback(note));
        FlashRing(note.trackIdx, true);
    }

    public void ShowSuccess()
    {
        isRunning = false;
        ShowResult("SUCCESS!", colorSuccess);
        resultCoroutine = StartCoroutine(HideAfterDelay(1.5f));
    }

    public void ShowFail()
    {
        isRunning = false;
        ShowResult("FALHOU!", colorFail);
        resultCoroutine = StartCoroutine(HideAfterDelay(1.0f));
    }

    // ── Spawn ─────────────────────────────────────────────────────────────────

    IEnumerator SpawnSequence(string[] labels, int[] tIndices)
    {
        isRunning = true;
        for (int i = 0; i < labels.Length; i++)
        {
            SpawnNote(labels[i], tIndices[i], i);
            // FIX: WaitForSeconds congela com timeScale = 0
            // WaitForSecondsRealtime ignora o timeScale e continua contando
            yield return new WaitForSecondsRealtime(noteInterval);
        }
    }

    void SpawnNote(string label, int trackIdx, int seqIdx)
    {
        if (notePrefab == null) return;
        if (tracks == null || trackIdx >= tracks.Length || tracks[trackIdx] == null) return;

        // Cria a nota como filho do SoloPanel
        GameObject go = Instantiate(notePrefab, soloPanel.transform);
        RectTransform r = go.GetComponent<RectTransform>();
        Image img = go.GetComponent<Image>();

        if (r == null) { Destroy(go); return; }

        // Configura pivot e âncora no centro do painel
        r.anchorMin = new Vector2(0.5f, 0.5f);
        r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot     = new Vector2(0.5f, 0.5f);

        // Garante tamanho mínimo
        if (r.sizeDelta.x < 10 || r.sizeDelta.y < 10)
            r.sizeDelta = new Vector2(60f, 36f);

        // Pega posição Y da trilha correspondente (em coordenadas locais do painel)
        float trackY = GetTrackLocalY(tracks[trackIdx]);

        // Nasce à direita
        r.anchoredPosition = new Vector2(spawnXOffset, trackY);

        // Cor e label
        if (img) img.color = colorIdle;

        TextMeshProUGUI tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp) tmp.text = label;

        activeNotes.Add(new NoteBlock
        {
            rect     = r,
            image    = img,
            trackIdx = trackIdx,
            seqIdx   = seqIdx,
            done     = false
        });
    }

    /// <summary>
    /// Converte a posição central de uma Track para coordenadas locais do SoloPanel.
    /// Funciona independente de como a âncora da Track foi configurada.
    /// </summary>
    float GetTrackLocalY(RectTransform track)
    {
        if (panelRect == null) return 0f;

        // Posição de mundo do centro da track
        Vector3 worldPos = track.TransformPoint(Vector3.zero);

        // Converte para local do painel
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            panelRect,
            RectTransformUtility.WorldToScreenPoint(null, worldPos),
            null,
            out local
        );
        return local.y;
    }

    // ── Feedback ──────────────────────────────────────────────────────────────

    void AutoMiss(NoteBlock note)
    {
        note.done = true;
        if (note.image) note.image.color = colorMiss;
        FlashRing(note.trackIdx, false);
        StartCoroutine(FadeAndDestroy(note, 0.25f));
        SpecialAttack.Instance?.OnNoteMissed(note.seqIdx);
    }

    IEnumerator HitFeedback(NoteBlock note)
    {
        if (note.image) note.image.color = colorHit;
        // FIX: WaitForSecondsRealtime para funcionar com timeScale = 0
        yield return new WaitForSecondsRealtime(0.06f);
        yield return StartCoroutine(FadeAndDestroy(note, 0.20f));
    }

    IEnumerator FadeAndDestroy(NoteBlock note, float duration)
    {
        if (note.image == null) yield break;
        Color start = note.image.color;
        float t = 0f;
        while (t < 1f)
        {
            // FIX: unscaledDeltaTime para o fade funcionar com timeScale = 0
            t += Time.unscaledDeltaTime / duration;
            Color c = start;
            c.a = Mathf.Lerp(start.a, 0f, t);
            if (note.image) note.image.color = c;
            yield return null;
        }
        if (note.rect) Destroy(note.rect.gameObject);
        activeNotes.Remove(note);
    }

    void FlashRing(int trackIdx, bool success)
    {
        if (hitRings == null || trackIdx >= hitRings.Length || hitRings[trackIdx] == null) return;
        StartCoroutine(RingFlash(hitRings[trackIdx], success));
    }

    IEnumerator RingFlash(Image ring, bool success)
    {
        Color originalColor = ring.color; // FIX: salva a cor original antes do flash
        ring.color = success ? colorSuccess : colorFail;
        // FIX: WaitForSecondsRealtime para funcionar com timeScale = 0
        yield return new WaitForSecondsRealtime(0.18f);
        ring.color = originalColor; // FIX: era new Color(1,1,1,0.15f) — hardcoded deixava o ring opaco
    }

    // ── Resultado / Limpeza ───────────────────────────────────────────────────

    void ShowResult(string text, Color color)
    {
        if (resultLabel == null) return;
        resultLabel.text  = text;
        resultLabel.color = color;
        resultLabel.gameObject.SetActive(true);
    }

    IEnumerator HideAfterDelay(float delay)
    {
        // FIX: WaitForSecondsRealtime ignora timeScale
        // WaitForSeconds travaria pois timeScale pode ainda ser 0 quando chamado
        yield return new WaitForSecondsRealtime(delay);
        CleanNotes();
        if (soloPanel)    soloPanel.SetActive(false);
        if (resultLabel)  resultLabel.gameObject.SetActive(false);
    }

    void CleanNotes()
    {
        foreach (NoteBlock n in activeNotes)
            if (n.rect) Destroy(n.rect.gameObject);
        activeNotes.Clear();
        isRunning = false;
    }

    
}