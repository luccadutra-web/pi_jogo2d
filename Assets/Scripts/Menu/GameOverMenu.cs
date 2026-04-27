using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;


public class GameOverMenu : MonoBehaviour
{
    public static GameOverMenu Instance { get; private set; }

    [Header("UI")]
    public GameObject gameOverPanel;
    public Button retryButton;
    public Button mainMenuButton;
    public CanvasGroup canvasGroup;

    [Header("Configuração")]
    public string mainMenuSceneName = "MainMenu";
    public float showDelay = 1.5f;   
    public float fadeDuration = 0.6f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        gameOverPanel?.SetActive(false);

        retryButton?.onClick.AddListener(Retry);
        mainMenuButton?.onClick.AddListener(GoToMainMenu);
    }

    
    public void Show()
    {
        StartCoroutine(ShowRoutine());
    }

    
    public void Retry()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    
    IEnumerator ShowRoutine()
    {
        yield return new WaitForSeconds(showDelay);

        gameOverPanel?.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            canvasGroup.alpha = 1f;
        }
    }
}