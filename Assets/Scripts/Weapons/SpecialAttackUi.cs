using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class SpecialAttackUI : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private GameObject      soloPanel;
    [SerializeField] private Image           circleTimer;
    [SerializeField] private Image           circleBg;
    [SerializeField] private TextMeshProUGUI keyLabel;
    [SerializeField] private Transform       progressParent;
    [SerializeField] private GameObject      dotPrefab;
    [SerializeField] private TextMeshProUGUI resultLabel;

    [Header("Timing")]
    [Tooltip("Deve ser igual ao timePerKey configurado em SpecialAttack")]
    [SerializeField] private float timePerKey = 2f;

    [Header("Cores")]
    [SerializeField] private Color colorNormal  = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color colorWarning = new Color(1f, 0.55f, 0f, 1f);
    [SerializeField] private Color colorFail    = new Color(0.9f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color colorSuccess = new Color(0.2f, 0.9f, 0.4f, 1f);

    [Header("Limiar de aviso (0-1)")]
    [SerializeField] [Range(0f, 1f)] private float warningThreshold = 0.3f;

    private float     timer;
    private bool      isRunning;
    private int       totalKeys;
    private Image[]   dots;
    private Coroutine resultCoroutine;

    public static SpecialAttackUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        soloPanel.SetActive(false);
    }

    void Update()
    {
        if (!isRunning) return;

        // FIX: Time.deltaTime é 0 com timeScale = 0
        // unscaledDeltaTime continua avançando durante o time stop
        timer -= Time.unscaledDeltaTime;

        float fill = Mathf.Clamp01(timer / timePerKey);
        circleTimer.fillAmount = fill;

        // FIX: Lerp de cor também usa unscaledDeltaTime
        Color target = fill <= warningThreshold ? colorWarning : colorNormal;
        circleTimer.color = Color.Lerp(circleTimer.color, target,
                                       Time.unscaledDeltaTime * 10f);

        // FIX: Mathf.Sin usa Time.unscaledTime para pulsar com o jogo parado
        float pulse = fill <= warningThreshold
            ? 1f + Mathf.Sin(Time.unscaledTime * 18f) * 0.08f
            : 1f;
        keyLabel.transform.localScale = Vector3.one * pulse;
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void ShowSolo(int keyCount)
    {
        if (resultCoroutine != null) StopCoroutine(resultCoroutine);

        totalKeys = keyCount;
        soloPanel.SetActive(true);
        resultLabel.gameObject.SetActive(false);
    }

    public void ShowKey(string keyName, int completedCount)
    {
        keyLabel.text = keyName;

        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].color = i < completedCount
                ? colorSuccess
                : new Color(1f, 1f, 1f, 0.25f);
        }

        timer                  = timePerKey;
        circleTimer.fillAmount = 1f;
        circleTimer.color      = colorNormal;
        isRunning              = true;
    }

    public void KeyPressed()
    {
        isRunning = false;
    }

    public void ShowSuccess()
    {
        isRunning         = false;
        circleTimer.color = colorSuccess;
        ShowResult("SUCCESS!", colorSuccess);

        // FIX: WaitForSeconds congela com timeScale = 0 — trocado para Realtime
        resultCoroutine = StartCoroutine(HideAfterDelay(1.4f));
    }

    public void ShowFail()
    {
        isRunning         = false;
        circleTimer.color = colorFail;
        keyLabel.color    = colorFail;
        ShowResult("FAIL!", colorFail);

        // FIX: idem — Realtime para funcionar durante time stop
        resultCoroutine = StartCoroutine(HideAfterDelay(1.0f));
    }

    // ── Internos ──────────────────────────────────────────────────────────────

    void ShowResult(string text, Color color)
    {
        resultLabel.gameObject.SetActive(true);
        resultLabel.text  = text;
        resultLabel.color = color;
        keyLabel.gameObject.SetActive(false);
    }

    IEnumerator HideAfterDelay(float delay)
    {
        // FIX: WaitForSecondsRealtime ignora timeScale
        // WaitForSeconds travaria aqui pois timeScale pode ainda ser 0
        // quando ShowFail/ShowSuccess são chamados antes do EndSolo restaurar
        yield return new WaitForSecondsRealtime(delay);

        soloPanel.SetActive(false);
        keyLabel.gameObject.SetActive(true);
        keyLabel.color                = Color.white;
        keyLabel.transform.localScale = Vector3.one;
    }
}