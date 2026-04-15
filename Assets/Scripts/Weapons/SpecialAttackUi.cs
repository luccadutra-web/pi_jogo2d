using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// UI da mecânica de ataque especial (solo).
/// 
/// Hierarquia esperada no Canvas:
///   SpecialAttackUI (este script)
///   └── SoloPanel
///       ├── CircleTimer          (Image — tipo Filled, Fill Method: Radial 360, Fill Origin: Top)
///       ├── CircleBg             (Image — círculo de fundo, cor escura)
///       ├── KeyLabel             (TextMeshProUGUI — exibe a tecla esperada)
///       ├── ProgressDots         (HorizontalLayoutGroup)
///       │   └── [Dot prefabs gerados via código]
///       └── ResultLabel          (TextMeshProUGUI — "SUCCESS!" / "FAIL!")
/// 
/// Configure no Inspector:
///   - soloPanel, circleTimer, circleBg, keyLabel, progressParent, dotPrefab, resultLabel
///   - timePerKey  (tempo em segundos para cada tecla — espelhado de SpecialAttack)
///   - colorNormal, colorWarning, colorFail, colorSuccess
/// </summary>
public class SpecialAttackUI : MonoBehaviour
{
    [Header("Referências / References")]
    [SerializeField] private GameObject soloPanel;
    [SerializeField] private Image circleTimer;       // Image com Fill Method = Radial 360
    [SerializeField] private Image circleBg;
    [SerializeField] private TextMeshProUGUI keyLabel;
    [SerializeField] private Transform progressParent; // pai dos dots de progresso
    [SerializeField] private GameObject dotPrefab;     // prefab do dot (Image simples)
    [SerializeField] private TextMeshProUGUI resultLabel;

    [Header("Timing")]
    [Tooltip("Deve ser igual ao timePerKey configurado em SpecialAttack")]
    [SerializeField] private float timePerKey = 2f;

    [Header("Cores / Colors")]
    [SerializeField] private Color colorNormal  = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color colorWarning = new Color(1f, 0.55f, 0f, 1f);  // laranja
    [SerializeField] private Color colorFail    = new Color(0.9f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color colorSuccess = new Color(0.2f, 0.9f, 0.4f, 1f);

    [Header("Limiar de aviso / Warning threshold (0-1)")]
    [SerializeField] [Range(0f, 1f)] private float warningThreshold = 0.3f;

    // ── Estado interno ────────────────────────────────────────────────────────
    private float   timer;
    private bool    isRunning;
    private int     totalKeys;
    private Image[] dots;
    private Coroutine resultCoroutine;

    // ── Singleton leve ────────────────────────────────────────────────────────
    public static SpecialAttackUI Instance { get; private set; }

    void Awake()
    {
        Instance = this;
        soloPanel.SetActive(false);
    }

    void Update()
    {
        if (!isRunning) return;

        timer -= Time.deltaTime;

        // Atualiza fill do círculo (1 = cheio, 0 = vazio)
        float fill = Mathf.Clamp01(timer / timePerKey);
        circleTimer.fillAmount = fill;

        // Muda cor quando perto do limite
        Color target = fill <= warningThreshold ? colorWarning : colorNormal;
        circleTimer.color = Color.Lerp(circleTimer.color, target, Time.deltaTime * 10f);

        // Pulsa a label da tecla quando em aviso
        float pulse = fill <= warningThreshold
            ? 1f + Mathf.Sin(Time.time * 18f) * 0.08f
            : 1f;
        keyLabel.transform.localScale = Vector3.one * pulse;
    }

    // ── API pública chamada por SpecialAttack ─────────────────────────────────

    /// <summary>Abre o painel e prepara os dots de progresso.</summary>
    public void ShowSolo(int keyCount)
    {
        if (resultCoroutine != null) StopCoroutine(resultCoroutine);

        totalKeys = keyCount;
        soloPanel.SetActive(true);
        resultLabel.gameObject.SetActive(false);

        //BuildDots(keyCount);
    }

    /// <summary>Exibe a próxima tecla esperada e reinicia o timer visual.</summary>
    public void ShowKey(string keyName, int completedCount)
    {
        keyLabel.text = keyName;

        // Acende dots já completados
        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].color = i < completedCount
                ? colorSuccess
                : new Color(1f, 1f, 1f, 0.25f);
        }

        // Reinicia timer
        timer = timePerKey;
        circleTimer.fillAmount = 1f;
        circleTimer.color = colorNormal;
        isRunning = true;
    }

    /// <summary>Para o timer sem fechar o painel (SpecialAttack detectou a tecla).</summary>
    public void KeyPressed()
    {
        isRunning = false;
    }

    /// <summary>Exibe feedback de sucesso e fecha.</summary>
    public void ShowSuccess()
    {
        isRunning = false;
        circleTimer.color = colorSuccess;
        // Acende todos os dots
        ///foreach (var d in dots) d.color = colorSuccess;
        ShowResult("SUCCESS!", colorSuccess);
        resultCoroutine = StartCoroutine(HideAfterDelay(1.4f));
    }

    /// <summary>Exibe feedback de falha e fecha.</summary>
    public void ShowFail()
    {
        isRunning = false;
        circleTimer.color = colorFail;
        keyLabel.color = colorFail;
        ShowResult("FAIL!", colorFail);
        resultCoroutine = StartCoroutine(HideAfterDelay(1.0f));
    }

    // ── Internos ──────────────────────────────────────────────────────────────

    void ShowResult(string text, Color color)
    {
        resultLabel.gameObject.SetActive(true);
        resultLabel.text = text;
        resultLabel.color = color;
        keyLabel.gameObject.SetActive(false);
    }

    IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        soloPanel.SetActive(false);
        keyLabel.gameObject.SetActive(true);
        keyLabel.color = Color.white;
        keyLabel.transform.localScale = Vector3.one;
    }

/*    void BuildDots(int count)
    {
        // Limpa dots anteriores
        foreach (Transform child in progressParent)
            Destroy(child.gameObject);

        dots = new Image[count];
        for (int i = 0; i < count; i++)
        {
            GameObject go = Instantiate(dotPrefab, progressParent);
            dots[i] = go.GetComponent<Image>();
            dots[i].color = new Color(1f, 1f, 1f, 0.25f);
        }
    }

*/

}