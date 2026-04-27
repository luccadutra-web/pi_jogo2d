using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using System.Collections;


public class PauseMenu : MonoBehaviour
{
    public static PauseMenu Instance { get; private set; }

    [Header("UI")]
    public GameObject pausePanel;
    public Button resumeButton;
    public Button restartButton;
    public Button mainMenuButton;
    public CanvasGroup canvasGroup;

    [Header("Configuração")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Animação")]
    public float fadeDuration = 0.2f;

    private bool isPaused = false;

    void Start()
    {
        pausePanel?.SetActive(false);

        resumeButton?.onClick.AddListener(Resume);
        restartButton?.onClick.AddListener(Restart);
        mainMenuButton?.onClick.AddListener(GoToMainMenu);
    }

    private InputAction pauseAction;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
        pauseAction.performed += _ => { if (isPaused) Resume(); else Pause(); };
    }

    void OnEnable()  { pauseAction.Enable(); }
    void OnDisable() { pauseAction.Disable(); }

    void Update() { }

        public void Pause()
    {
        isPaused = true;
        Time.timeScale = 0f;
        pausePanel?.SetActive(true);

        if (canvasGroup != null)
            StartCoroutine(FadeCanvas(0f, 1f));
    }

    public void Resume()
    {
        isPaused = false;
        Time.timeScale = 1f;

        if (canvasGroup != null)
            StartCoroutine(FadeCanvas(1f, 0f, () => pausePanel?.SetActive(false)));
        else
            pausePanel?.SetActive(false);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    
    IEnumerator FadeCanvas(float from, float to, System.Action onComplete = null)
    {
        float t = 0f;
        canvasGroup.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime; 
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
        onComplete?.Invoke();
    }
}